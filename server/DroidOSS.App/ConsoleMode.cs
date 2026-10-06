using System.Diagnostics;
using System.Net.Sockets;
using DroidOSS.Core;
using DroidOSS.ViGEm;

namespace DroidOSS.App;

/// <summary>
/// The text front end: <c>--console</c> for headless use and debugging, and
/// <c>--demo</c>.
/// </summary>
/// <remarks>
/// <c>--demo</c> keeps B0's self-driven sweep. It drives a pad directly,
/// bypassing sessions entirely — useful precisely for that reason when something
/// breaks and you need to know whether the driver or the network is at fault,
/// since it involves neither a phone nor a socket.
/// </remarks>
internal static class ConsoleMode
{
    /// <summary>The pad <c>--demo</c> drives. Real sessions are assigned slots by the server.</summary>
    private const int DemoSlot = 0;

    public static async Task<int> RunAsync(bool demo)
    {
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
                Console.WriteLine($"  rumble: pad {e.Slot}  large={e.LargeMotor} small={e.SmallMotor}");
            };

            return demo
                ? await RunDemoAsync(backend)
                : await RunServerAsync(backend);
        }
    }

    /// <summary>Listens for phones and gives each one a pad.</summary>
    private static async Task<int> RunServerAsync(IPadBackend backend)
    {
        using var host = new ServerHost(backend, Environment.MachineName);

        try
        {
            host.Start();
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"Could not bind UDP port {Protocol.InputPort}.");
            Console.Error.WriteLine("Another copy of the server is probably already running.");
            Console.Error.WriteLine();
            Console.Error.WriteLine($"Details: {ex.Message}");
            return 1;
        }

        if (!host.Discoverable)
        {
            Console.Error.WriteLine($"Could not bind UDP port {Protocol.DiscoveryPort} for discovery.");
            Console.Error.WriteLine("Phones will not find this PC on their own; type the address below instead.");
            Console.Error.WriteLine($"Details: {host.DiscoveryError}");
            Console.Error.WriteLine();
        }

        var sessions = host.Sessions;
        sessions.SessionOpened += (_, e) =>
            Console.WriteLine($"  + pad {e.Slot}  {e.Client}  connected");

        sessions.SessionClosed += (_, e) =>
            Console.WriteLine($"  - pad {e.Slot}  {e.Client}  gone ({ServerHost.Describe(e.Reason)})");

        if (host.Discoverable)
        {
            Console.WriteLine($"Waiting for a phone. On the same Wi-Fi, the app lists this PC as \"{host.ServerName}\".");
            Console.WriteLine("Or type one of these addresses into it:");
        }
        else
        {
            Console.WriteLine("Waiting for a phone. Point it at:");
        }
        foreach (var address in ServerHost.LocalAddresses())
            Console.WriteLine($"      {address}:{Protocol.InputPort}");
        Console.WriteLine();
        Console.WriteLine("  Or test with:  py tools/fake_phone.py --host 127.0.0.1");
        Console.WriteLine();
        Console.WriteLine($"Up to {IPadBackend.MaxPads} phones. Press Ctrl+C to stop.");
        Console.WriteLine();

        using var cts = CancelOnCtrlC();

        await Task.WhenAll(
            host.RunAsync(cts.Token),
            ReportStatusAsync(sessions, cts.Token));

        Console.WriteLine();
        Console.WriteLine("Pads disconnected. Done.");
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

        backend.Connect(DemoSlot);

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
                backend.Submit(DemoSlot, in state);
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C, expected.
        }

        // Zero before unplugging, for the same reason sessions do.
        Console.WriteLine();
        Console.WriteLine("Zeroing state...");
        backend.Submit(DemoSlot, PadState.Neutral);

        Console.WriteLine("Unplugging...");
        backend.Disconnect(DemoSlot);

        Console.WriteLine("Done.");
        return 0;
    }

    /// <summary>Prints one line per connected phone, once a second.</summary>
    private static async Task ReportStatusAsync(
        SessionManager sessions, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        // Applied count per slot at the last tick, so the difference is a rate.
        var previous = new long[IPadBackend.MaxPads];
        var lastDropped = 0L;

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                foreach (var session in sessions.Snapshot())
                {
                    var rate = session.Applied - previous[session.Slot];
                    previous[session.Slot] = session.Applied;

                    var state = session.LastState;
                    Console.WriteLine(
                        $"    pad {session.Slot}  {session.Client}  " +
                        $"LX={state.ThumbLX,7} LY={state.ThumbLY,7}  " +
                        $"btn=0x{state.Buttons:X4}  ~{rate} Hz");
                }

                // Only mentioned when it changes. A climbing unknown count is
                // the signature of a phone that thinks it is connected while the
                // server has never heard of it — worth saying out loud, because
                // it is otherwise invisible and maddening to diagnose.
                var dropped = sessions.Malformed + sessions.UnknownSender + sessions.Stale;
                if (dropped != lastDropped)
                {
                    lastDropped = dropped;
                    Console.WriteLine(
                        $"    dropped: {sessions.Malformed} bad, " +
                        $"{sessions.Stale} stale, " +
                        $"{sessions.UnknownSender} from unknown senders");
                }
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

    private static void ReportMissingDriver(Exception ex)
    {
        Console.Error.WriteLine("Could not reach the ViGEmBus driver.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("droidOSS needs it to create virtual controllers. To install:");
        Console.Error.WriteLine("  1. Download the installer from");
        Console.Error.WriteLine($"     {DriverHelp.DownloadUrl}");
        Console.Error.WriteLine("  2. Run it and accept the admin prompt.");
        Console.Error.WriteLine("  3. Reboot if asked, then run this again.");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Details: {ex.InnerException?.Message ?? ex.Message}");
    }
}

/// <summary>Where to get the driver, shared by both front ends.</summary>
internal static class DriverHelp
{
    public const string DownloadUrl = "https://github.com/nefarius/ViGEmBus/releases";
}
