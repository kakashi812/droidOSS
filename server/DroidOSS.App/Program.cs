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
/// Default mode listens for phones. Each one that says HELLO is given a pad
/// slot, and gets it taken away again when it says BYE or simply goes quiet.
///
/// <c>--demo</c> keeps B0's self-driven sweep. It is opt-in rather than always-on
/// because it drives a pad directly, bypassing sessions entirely — useful
/// precisely for that reason when something breaks and you need to know whether
/// the driver or the network is at fault, since it involves neither Python nor a
/// socket.
/// </remarks>
internal static class Program
{
    /// <summary>The pad <c>--demo</c> drives. Real sessions are assigned slots by the server.</summary>
    private const int DemoSlot = 0;

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
                Console.WriteLine($"  rumble: pad {e.Slot}  large={e.LargeMotor} small={e.SmallMotor}");
            };

            return demoMode
                ? await RunDemoAsync(backend)
                : await RunServerAsync(backend);
        }
    }

    /// <summary>Listens for phones and gives each one a pad.</summary>
    private static async Task<int> RunServerAsync(IPadBackend backend)
    {
        var sessions = new SessionManager(backend);
        using var listener = new UdpSessionListener(sessions);

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

        sessions.SessionOpened += (_, e) =>
            Console.WriteLine($"  + pad {e.Slot}  {e.Client}  connected");

        sessions.SessionClosed += (_, e) =>
            Console.WriteLine($"  - pad {e.Slot}  {e.Client}  gone ({Describe(e.Reason)})");

        Console.WriteLine("Waiting for a phone. Point it at:");
        foreach (var address in LocalAddresses())
            Console.WriteLine($"      {address}:{Protocol.InputPort}");
        Console.WriteLine();
        Console.WriteLine("  Or test with:  py tools/fake_phone.py --host 127.0.0.1");
        Console.WriteLine();
        Console.WriteLine($"Up to {IPadBackend.MaxPads} phones. Press Ctrl+C to stop.");
        Console.WriteLine();

        using var cts = CancelOnCtrlC();

        await Task.WhenAll(
            listener.ListenAsync(cts.Token),
            SweepAsync(sessions, cts.Token),
            ReportStatusAsync(sessions, cts.Token));

        // Every remaining session is zeroed and unplugged, in that order — the
        // manager owns that ordering so it cannot be got wrong here.
        Console.WriteLine();
        Console.WriteLine("Disconnecting pads...");
        sessions.CloseAll();

        Console.WriteLine("Done.");
        return 0;
    }

    /// <summary>
    /// Drops phones that have stopped sending.
    /// </summary>
    /// <remarks>
    /// Separate from the receive loop on purpose: a phone whose battery died
    /// sends nothing at all, so nothing in the receive path would ever run to
    /// notice. Ten times a second is far finer than the two-second timeout needs
    /// and costs nothing — the sweep walks at most four entries.
    /// </remarks>
    private static async Task SweepAsync(SessionManager sessions, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                sessions.SweepTimeouts();
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C, expected.
        }
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

    private static string Describe(SessionCloseReason? reason) => reason switch
    {
        SessionCloseReason.Bye => "said goodbye",
        SessionCloseReason.Timeout => "timed out",
        SessionCloseReason.ServerShutdown => "server stopping",
        _ => "unknown",
    };

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
