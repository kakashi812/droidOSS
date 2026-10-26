using System.Diagnostics;
using DroidOSS.Core;

namespace DroidOSS.IntegrationTests;

// tools/fake_phone.py (an independent Python implementation of the protocol) driving the C# server.
public class FakePhoneScriptTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "tools", "fake_phone.py")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("tools/fake_phone.py not found above the test binaries");
    }

    /// <summary><c>python3</c> on Linux and macOS, the <c>py</c> launcher on Windows.</summary>
    private static string Python() =>
        Environment.GetEnvironmentVariable("DROIDOSS_PYTHON") ?? (OperatingSystem.IsWindows() ? "py" : "python3");

    private static async Task<(int ExitCode, string Output)> RunScriptAsync(params string[] args)
    {
        var start = new ProcessStartInfo(Python())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = RepoRoot(),
        };
        start.ArgumentList.Add(Path.Combine("tools", "fake_phone.py"));
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        return (process.ExitCode, await stdout + await stderr);
    }

    [Fact]
    public async Task The_python_selftest_passes()
    {
        var (exit, output) = await RunScriptAsync("--selftest");

        Assert.True(exit == 0, output);
    }

    [Fact]
    public async Task The_python_phone_gets_a_pad_and_streams_input_into_it()
    {
        await using var server = new TestServer();

        var (exit, output) = await RunScriptAsync(
            "--host", "127.0.0.1", "--port", server.Host.InputPort.ToString(),
            "--rate", "100", "--duration", "2");

        Assert.True(exit == 0, output);

        // About 200 packets at 100 Hz for 2 s; loopback can still lose a few under load.
        var submissions = server.Backend.SubmissionsTo(0);
        Assert.True(submissions.Count > 100, $"only {submissions.Count} states reached the pad\n{output}");

        // The script sweeps the left stick in a circle and toggles A once a second.
        Assert.Contains(submissions, s => s.ThumbLX > 10000);
        Assert.Contains(submissions, s => s.ThumbLX < -10000);
        Assert.Contains(submissions, s => s.IsPressed(GamepadButtons.A));

        // Ctrl+C or the end of --duration sends a neutral state and BYE on the way out.
        await TestServer.Eventually(() => server.Backend.Count("disconnect", 0) == 1, "the script said goodbye");
    }
}
