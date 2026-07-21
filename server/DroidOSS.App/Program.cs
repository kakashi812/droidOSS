using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DroidOSS.Core;
using DroidOSS.ViGEm;

namespace DroidOSS.App;

/// <summary>
/// The droidOSS server.
/// </summary>
/// <remarks>
/// Default mode listens for INPUT packets and drives a virtual pad with them.
///
/// <c>--demo</c> keeps B0's self-driven sweep. It is opt-in rather than always-on
/// because both paths write to the same pad: the sweep and incoming packets would
/// fight, last writer wins, and the stick would jitter between the circle and
/// whatever the phone is doing — which looks exactly like a network bug. As a
/// flag it stays useful as a zero-dependency "is the driver still alive?" check
/// that involves neither Python nor the network.
/// </remarks>
internal static class Program
{
    private const int PadSlot = 0;   // one client for now; slot assignment is B3

    private static async Task<int> Main(string[] args)
    {
        var demoMode = args.Contains("--demo", StringComparer.OrdinalIgnoreCase);

        Console.WriteLine("droidOSS server");
        Console.WriteLine();

        ViGEmPadBackend backend;
        try
        {
            backend = new ViGEmPadBackend();
        }
        catch (PadDriverUnavailableException ex)
        {
            ReportMissingDriver(ex);
            return 1;
        }

        using (backend)
        {
            backend.RumbleReceived += (_, e) =>
            {
                // Zero/zero is the driver assigning an LED index, not a game
                // asking for vibration. Only report the real ones.
                if (e.LargeMotor == 0 && e.SmallMotor == 0) return;
                Console.WriteLine($"  rumble: large={e.LargeMotor} small={e.SmallMotor}");
            };

            backend.Connect(PadSlot);

            var exitCode = demoMode
                ? await RunDemoAsync(backend)
                : await RunServerAsync(backend);

            // Zero the state and let the driver see it BEFORE unplugging. Some
            // games latch the last state they saw when a pad disappears, so
            // unplugging mid-input leaves the stick stuck.
            Console.WriteLine();
            Console.WriteLine("Zeroing state...");
            backend.Submit(PadSlot, PadState.Neutral);

            Console.WriteLine("Unplugging...");
            backend.Disconnect(PadSlot);

            Console.WriteLine("Done.");
            return exitCode;
        }
    }

    /// <summary>Listens for packets and drives the pad with them.</summary>
    private static async Task<int> RunServerAsync(IPadBackend backend)
    {
        var server = new PadServer(backend, PadSlot);
        using var listener = new UdpInputListener(server);

        try
        {
            listener.Bind();
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"Could not bind UDP port {Protocol.InputPort}.");
            Console.Error.WriteLine("Another copy of the server is probably already running.");
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Details: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Virtual controller connected. Listening for input.");
        Console.WriteLine();
        Console.WriteLine("  Point your phone at:");
        foreach (var address in LocalAddresses())
            Console.WriteLine($"      {address}:{Protocol.InputPort}");
        Console.WriteLine();
        Console.WriteLine("  Or test with:  py tools/fake_phone.py --host 127.0.0.1");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        using var cts = CancelOnCtrlC();
        var listening = listener.ListenAsync(cts.Token);
        var reporting = ReportStatusAsync(server, listener, cts.Token);

        await Task.WhenAll(listening, reporting);
        return 0;
    }

    /// <summary>B0's self-driven sweep, kept for smoke-testing the driver.</summary>
    private static async Task<int> RunDemoAsync(IPadBackend backend)
    {
        const double sweepPeriodSeconds = 2.0;
        const double amplitude = 0.85 * short.MaxValue;

        Console.WriteLine("Demo mode — sweeping the stick. No socket is bound.");
        Console.WriteLine();
        Console.WriteLine("  Open joy.cpl and click Properties to watch it.");
        Console.WriteLine();
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        using var cts = CancelOnCtrlC();

        var state = PadState.Neutral;   // reused every tick, never reallocated
        var clock = Stopwatch.StartNew();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(8));

        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                var angle = clock.Elapsed.TotalSeconds / sweepPeriodSeconds * 2 * Math.PI;
                state.ThumbLX = (short)(Math.Cos(angle) * amplitude);
                state.ThumbLY = (short)(Math.Sin(angle) * amplitude);
                backend.Submit(PadSlot, in state);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C, expected.
        }

        return 0;
    }

    /// <summary>Prints a status line once a second while packets flow.</summary>
    private static async Task ReportStatusAsync(
        PadServer server, UdpInputListener listener, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        var lastApplied = 0L;
        var lastSeen = false;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var applied = server.Applied;
                var rate = applied - lastApplied;
                lastApplied = applied;

                if (rate == 0)
                {
                    // Only say it once, rather than every second forever.
                    if (lastSeen)
                    {
                        Console.WriteLine("  ...silence. The pad holds its last position (B3 fixes that).");
                        lastSeen = false;
                    }
                    continue;
                }

                lastSeen = true;
                var state = server.LastApplied;
                Console.WriteLine(
                    $"  {listener.LastSender}  " +
                    $"LX={state.ThumbLX,7} LY={state.ThumbLY,7}  " +
                    $"btn=0x{state.Buttons:X4}  " +
                    $"~{rate} Hz  " +
                    $"applied={server.Applied} stale={server.Stale} bad={server.Malformed}");
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C, expected.
        }
    }

    private static CancellationTokenSource CancelOnCtrlC()
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;   // shut down tidily rather than being killed
            cts.Cancel();
        };
        return cts;
    }

    /// <summary>
    /// This machine's LAN addresses — what gets typed into the phone.
    /// </summary>
    /// <remarks>
    /// Loopback is excluded because it is useless to a phone. Several may be
    /// listed when there is both Wi-Fi and Ethernet, or a VM adapter; showing all
    /// of them beats guessing wrong.
    /// </remarks>
    private static IEnumerable<IPAddress> LocalAddresses()
    {
        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(Dns.GetHostName());
        }
        catch (SocketException)
        {
            yield break;
        }

        foreach (var address in addresses)
        {
            if (address.AddressFamily != AddressFamily.InterNetwork) continue;
            if (IPAddress.IsLoopback(address)) continue;
            yield return address;
        }
    }

    private static void ReportMissingDriver(Exception ex)
    {
        Console.Error.WriteLine("Could not reach the ViGEmBus driver.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("droidOSS needs it to create virtual controllers. To install:");
        Console.Error.WriteLine("  1. Download the installer from");
        Console.Error.WriteLine("     https://github.com/nefarius/ViGEmBus/releases");
        Console.Error.WriteLine("  2. Run it and accept the admin prompt.");
        Console.Error.WriteLine("  3. Reboot if asked, then run this again.");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Details: {ex.InnerException?.Message ?? ex.Message}");
    }
}
