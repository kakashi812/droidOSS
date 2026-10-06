using System.Runtime.InteropServices;
using System.Windows;
using DroidOSS.App.Ui;

namespace DroidOSS.App;

/// <summary>
/// The droidOSS server.
/// </summary>
/// <remarks>
/// Double-clicked, it opens a window: four player cards, the address phones
/// need, and — behind a developer-mode switch — a live view of every pad and a
/// log. <c>--console</c> runs the same server as text instead, for headless use
/// and debugging; <c>--demo</c> sweeps a stick with no network at all.
///
/// The executable is a Windows (GUI) app so double-clicking it does not flash up
/// a console window. The text modes borrow the console they were started from,
/// or open one of their own.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var demo = HasFlag(args, "--demo");
        var console = demo || HasFlag(args, "--console");

        if (console)
        {
            AttachOrAllocateConsole();
            return ConsoleMode.RunAsync(demo).GetAwaiter().GetResult();
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        return app.Run(new MainWindow());
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Contains(flag, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Give the text modes somewhere to write.
    /// </summary>
    /// <remarks>
    /// Output already redirected to a file or pipe needs nothing. Started from a
    /// terminal, use that terminal. Started any other way — a shortcut with
    /// <c>--console</c>, say — open a console window of our own.
    /// </remarks>
    private static void AttachOrAllocateConsole()
    {
        if (Console.IsOutputRedirected) return;
        if (!AttachConsole(AttachParentProcess)) AllocConsole();
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();
}
