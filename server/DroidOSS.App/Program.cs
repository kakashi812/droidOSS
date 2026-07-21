using System.Diagnostics;
using DroidOSS.Core;
using DroidOSS.ViGEm;

namespace DroidOSS.App;

/// <summary>
/// B0 — prove the driver works.
/// </summary>
/// <remarks>
/// No phone, no network. This exists to demonstrate that a controller which does
/// not physically exist can appear to Windows and move, which is the one part of
/// this project that could have turned out to be impossible.
///
/// Everything here is scaffolding except the shutdown path, which follows the
/// zero-then-unplug rule that the real server depends on.
/// </remarks>
internal static class Program
{
    /// <summary>How often we push a new state. 125 Hz is the rate the phone will send at.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(8);

    /// <summary>Seconds for the stick to complete one full circle.</summary>
    private const double SweepPeriodSeconds = 2.0;

    /// <summary>How far from centre to push the stick. Short of the rim so it reads as deliberate.</summary>
    private const double SweepAmplitude = 0.85 * short.MaxValue;

    private static async Task<int> Main()
    {
        Console.WriteLine("droidOSS — B0: virtual pad test");
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
                Console.WriteLine($"  rumble on pad {e.Slot}: large={e.LargeMotor} small={e.SmallMotor}");

            const int slot = 0;
            backend.Connect(slot);

            Console.WriteLine("Virtual controller connected.");
            Console.WriteLine();
            Console.WriteLine("  Open joy.cpl (Win+R, type 'joy.cpl') and click Properties.");
            Console.WriteLine("  The left stick should be tracing a circle on its own.");
            Console.WriteLine();
            Console.WriteLine("Press Ctrl+C to unplug it cleanly.");
            Console.WriteLine();

            await SweepUntilCancelled(backend, slot);

            // The rule that matters: neutralise the state and let the driver see it
            // BEFORE unplugging. A game that latches the last state it saw would
            // otherwise hold the stick down forever.
            Console.WriteLine();
            Console.WriteLine("Zeroing state...");
            backend.Submit(slot, PadState.Neutral);

            Console.WriteLine("Unplugging...");
            backend.Disconnect(slot);
        }

        Console.WriteLine("Done. The controller should be gone from joy.cpl.");
        return 0;
    }

    /// <summary>Sweeps the left stick in a circle until Ctrl+C.</summary>
    private static async Task SweepUntilCancelled(IPadBackend backend, int slot)
    {
        using var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;   // we want to shut down tidily, not be killed
            cts.Cancel();
        };

        // Reused across every tick — nothing in this loop allocates.
        var state = PadState.Neutral;

        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        var ticks = 0L;

        using var timer = new PeriodicTimer(Tick);

        try
        {
            while (await timer.WaitForNextTickAsync(cts.Token))
            {
                var angle = clock.Elapsed.TotalSeconds / SweepPeriodSeconds * 2 * Math.PI;

                state.ThumbLX = (short)(Math.Cos(angle) * SweepAmplitude);
                state.ThumbLY = (short)(Math.Sin(angle) * SweepAmplitude);

                backend.Submit(slot, in state);
                ticks++;

                // A once-a-second heartbeat, so it is obvious the loop is alive
                // and roughly on rate.
                if (clock.Elapsed - lastReport < TimeSpan.FromSeconds(1)) continue;

                lastReport = clock.Elapsed;
                var rate = ticks / clock.Elapsed.TotalSeconds;
                Console.WriteLine(
                    $"  t={clock.Elapsed.TotalSeconds,5:F1}s  " +
                    $"LX={state.ThumbLX,7}  LY={state.ThumbLY,7}  ~{rate:F0} Hz");
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C. Expected.
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
