using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace DroidOSS.App.Ui;

/// <summary>
/// The window's colours, in light and dark, chosen to match Windows' own app
/// setting — and the phone app, whose dark navy and light blue these echo.
/// </summary>
/// <remarks>
/// Brushes are put into the window's resources under the keys below and used
/// through <c>DynamicResource</c>, so the XAML never names a colour itself.
/// </remarks>
internal static class Theme
{
    private static readonly (string Key, Color Dark, Color Light)[] Palette =
    [
        ("Bg",          Rgb(0x0E1116), Rgb(0xF3F5F8)),
        ("Surface",     Rgb(0x1A2028), Rgb(0xFFFFFF)),
        ("SurfaceAlt",  Rgb(0x222A34), Rgb(0xEDF0F4)),
        ("Border",      Rgb(0x2E3743), Rgb(0xD8DDE4)),
        ("Text",        Rgb(0xE6EAF0), Rgb(0x1A1F26)),
        ("TextMuted",   Rgb(0x98A2B0), Rgb(0x5B6573)),
        ("Accent",      Rgb(0x8AC4FF), Rgb(0x1F6FD1)),
        ("AccentText",  Rgb(0x0B1A2A), Rgb(0xFFFFFF)),
        ("Good",        Rgb(0x4CC26A), Rgb(0x2E7D32)),
        ("Warn",        Rgb(0xFFB84D), Rgb(0xB26A00)),
        ("Bad",         Rgb(0xF0716A), Rgb(0xC62828)),
        ("PadA",        Rgb(0x5CC85C), Rgb(0x3A9E3A)),
        ("PadB",        Rgb(0xE5534B), Rgb(0xC9362E)),
        ("PadX",        Rgb(0x4C8DFF), Rgb(0x2F6FE0)),
        ("PadY",        Rgb(0xF2C230), Rgb(0xD9A400)),
    ];

    /// <summary>
    /// True when Windows is set to dark mode for apps. <c>DROIDOSS_THEME</c> set
    /// to <c>light</c> or <c>dark</c> overrides it, for screenshots and testing.
    /// </summary>
    public static bool IsDark()
    {
        switch (Environment.GetEnvironmentVariable("DROIDOSS_THEME")?.ToLowerInvariant())
        {
            case "light": return false;
            case "dark": return true;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>Fill <paramref name="resources"/> with the palette for the current mode.</summary>
    public static void Apply(ResourceDictionary resources, bool dark)
    {
        foreach (var (key, darkColour, lightColour) in Palette)
        {
            var brush = new SolidColorBrush(dark ? darkColour : lightColour);
            brush.Freeze();
            resources[key] = brush;
        }
    }

    /// <summary>
    /// Dark title bar to match a dark window. Without it Windows draws a white
    /// bar over a dark app, which looks broken.
    /// </summary>
    public static void ApplyTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var value = dark ? 1 : 0;
        // 20 on Windows 11 and recent 10; 19 on older Windows 10 builds.
        if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
    }

    private static Color Rgb(int rgb) =>
        Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
