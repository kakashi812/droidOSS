using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Navigation;
using System.Windows.Threading;
using DroidOSS.Core;
using DroidOSS.ViGEm;

namespace DroidOSS.App.Ui;

/// <summary>
/// The server's window: four player cards, how phones find this PC, and —
/// behind the developer-mode switch — live input, packet rates and a log.
/// </summary>
/// <remarks>
/// The server itself runs on the thread pool, never on this window's thread: the
/// receive loop must not wait behind a repaint. Everything it reports reaches
/// the window through <see cref="Dispatcher.BeginInvoke(Delegate, object[])"/>,
/// and the cards poll <see cref="SessionManager.Snapshot"/> on a timer — fast
/// enough for a smooth live preview in developer mode, slow and cheap otherwise.
/// </remarks>
internal partial class MainWindow : Window
{
    /// <summary>~30 fps for the live input preview.</summary>
    private static readonly TimeSpan DeveloperRefresh = TimeSpan.FromMilliseconds(33);

    /// <summary>Plenty for "connected for 3 min" and the connection quality.</summary>
    private static readonly TimeSpan NormalRefresh = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// A steadily climbing drop count gets a log line this often, not every
    /// second; the counters above the log stay live regardless.
    /// </summary>
    private static readonly TimeSpan DroppedLogInterval = TimeSpan.FromSeconds(10);

    /// <summary>Games can change rumble every frame; the log only needs a sample.</summary>
    private static readonly TimeSpan RumbleLogInterval = TimeSpan.FromMilliseconds(250);

    private readonly MainViewModel _vm = new();
    private readonly ServerSettings _settings = ServerSettings.Load();
    private readonly DispatcherTimer _timer = new();
    private readonly bool _dark = Theme.IsDark();

    private ViGEmPadBackend? _backend;
    private ServerHost? _host;
    private CancellationTokenSource? _cts;
    private Task? _run;

    private readonly DateTime[] _lastRumbleLog = new DateTime[IPadBackend.MaxPads];
    private DateTime _lastCountersCheck;
    private DateTime _lastDroppedLog;
    private long _lastDropped;

    public MainWindow()
    {
        InitializeComponent();
        Theme.Apply(Resources, _dark);

        _vm.DeveloperMode = _settings.DeveloperMode;
        _vm.PadKind = _settings.PadKind;
        _vm.PropertyChanged += OnViewModelChanged;
        _vm.Log.CollectionChanged += OnLogChanged;
        DataContext = _vm;

        _timer.Tick += (_, _) => Refresh();
        SourceInitialized += (_, _) => Theme.ApplyTitleBar(this, _dark);
        Loaded += (_, _) =>
        {
            if (_vm.DeveloperMode) MakeRoomForDeveloperMode();
            StartServer();
        };
        Closing += OnClosing;

        // Wi-Fi reconnects and adapters come and go; keep the shown address true.
        NetworkChange.NetworkAddressChanged += (_, _) => Dispatcher.BeginInvoke(ShowAddresses);
    }

    // ── starting and stopping ────────────────────────────────────────────

    private void StartServer()
    {
        _vm.Screen = Screen.Starting;

        if (_backend is null)
        {
            try
            {
                _backend = new ViGEmPadBackend(_vm.PadKind);
                _backend.RumbleReceived += OnRumble;
                _backend.LightChanged += OnLightReported;
                _backend.OutputReportReceived += OnOutputReport;
            }
            catch (PadDriverUnavailableException ex)
            {
                _vm.ErrorDetails = $"Details: {ex.InnerException?.Message ?? ex.Message}";
                _vm.Screen = Screen.DriverMissing;
                return;
            }
        }

        var host = new ServerHost(_backend, Environment.MachineName);
        try
        {
            host.Start();
        }
        catch (SocketException ex)
        {
            host.Dispose();
            _vm.ErrorDetails = $"UDP port {Protocol.InputPort}: {ex.Message}";
            _vm.Screen = Screen.PortInUse;
            return;
        }

        _host = host;
        host.Sessions.SessionOpened += (_, e) => Dispatcher.BeginInvoke(() =>
            _vm.AddLog($"+ Player {e.Slot + 1} connected — {e.Client}"));
        host.Sessions.SessionClosed += (_, e) => Dispatcher.BeginInvoke(() =>
            _vm.AddLog($"- Player {e.Slot + 1} left — {e.Client} ({ServerHost.Describe(e.Reason)})"));

        _vm.ServerName = host.ServerName;
        _vm.Discoverable = host.Discoverable;
        _vm.DiscoveryWarning = host.Discoverable
            ? ""
            : $"Phones can't find this PC on their own: UDP port {Protocol.DiscoveryPort} is in use. " +
              "Type the address below into the app instead.";
        ShowAddresses();

        _cts = new CancellationTokenSource();
        // Task.Run, so the receive loop's awaits never resume on this window's thread.
        _run = Task.Run(() => host.RunAsync(_cts.Token));

        _vm.Screen = Screen.Running;
        _vm.AddLog($"Server started. Input on UDP {Protocol.InputPort}" +
                   (host.Discoverable
                       ? $", discovery on UDP {Protocol.DiscoveryPort} as \"{host.ServerName}\"."
                       : $"; discovery off ({host.DiscoveryError})."));

        ApplyRefreshRate();
        _timer.Start();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _timer.Stop();
        _cts?.Cancel();

        // RunAsync zeroes and unplugs every pad as it finishes. Wait for that, so
        // no game is left holding a stick down after the window has gone.
        try
        {
            _run?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Already stopping; nothing useful to add.
        }

        _host?.Dispose();
        _backend?.Dispose();
    }

    // ── keeping the window current ───────────────────────────────────────

    private void Refresh()
    {
        if (_host is null) return;

        var now = DateTimeOffset.UtcNow;
        var sessions = _host.Sessions;
        var snapshot = sessions.Snapshot();

        foreach (var slot in _vm.Slots)
        {
            SessionInfo? match = null;
            foreach (var s in snapshot)
                if (s.Slot == slot.Slot) match = s;
            slot.Update(match, now);

            if (_host.Feedback.LightOf(slot.Slot) is { } light)
                slot.SetLight(light.Player, light.Colour, _vm.PadKind);
        }
        _vm.ConnectedCount = snapshot.Count;

        // Dropped-packet counters: once a second is plenty.
        if (DateTime.UtcNow - _lastCountersCheck < TimeSpan.FromSeconds(1)) return;
        _lastCountersCheck = DateTime.UtcNow;

        _vm.DroppedText =
            $"dropped: {sessions.Malformed} bad · {sessions.Stale} stale · " +
            $"{sessions.UnknownSender} unknown sender · {sessions.Rejected} refused (full)";

        // A climbing count is worth a line in the log; a steady one is not. A
        // climbing "unknown sender" is a phone that thinks it is connected
        // while this server has never heard of it.
        var dropped = sessions.Malformed + sessions.Stale + sessions.UnknownSender + sessions.Rejected;
        if (dropped != _lastDropped && DateTime.UtcNow - _lastDroppedLog >= DroppedLogInterval)
        {
            _lastDropped = dropped;
            _lastDroppedLog = DateTime.UtcNow;
            _vm.AddLog(_vm.DroppedText);
        }
    }

    private void ApplyRefreshRate() =>
        _timer.Interval = _vm.DeveloperMode ? DeveloperRefresh : NormalRefresh;

    private void ShowAddresses()
    {
        _vm.Addresses.Clear();
        foreach (var address in ServerHost.LocalAddresses())
            _vm.Addresses.Add(address.ToString());
        if (_vm.Addresses.Count == 0)
            _vm.Addresses.Add("No network — connect this PC to Wi-Fi or Ethernet");
    }

    private void OnRumble(object? sender, RumbleEventArgs e)
    {
        // Raised on a driver thread.
        Dispatcher.BeginInvoke(() =>
        {
            if (e.Slot < 0 || e.Slot >= _vm.Slots.Length) return;

            var slot = _vm.Slots[e.Slot];
            var off = e.LargeMotor == 0 && e.SmallMotor == 0;
            slot.RumbleText = off ? "" : $"rumble: large {e.LargeMotor * 100 / 255}% · small {e.SmallMotor * 100 / 255}%";

            // Zero/zero is also the driver assigning an LED index; only log real requests.
            if (off) return;
            var now = DateTime.UtcNow;
            if (now - _lastRumbleLog[e.Slot] < RumbleLogInterval) return;
            _lastRumbleLog[e.Slot] = now;
            _vm.AddLog($"  Player {e.Slot + 1} {slot.RumbleText}");
        });
    }

    // Last colour and raw report logged per slot. A DualShock 4 is sent the
    // same report over and over; only a change is worth a line.
    private readonly (byte, Rgb)?[] _lastLightLogged = new (byte, Rgb)?[IPadBackend.MaxPads];
    private readonly string?[] _lastReportLogged = new string?[IPadBackend.MaxPads];

    /// <summary>
    /// Logs each new colour a pad reports. In PlayStation 4 mode this is how to
    /// tell whether a game or Steam is setting the lightbar at all.
    /// </summary>
    private void OnLightReported(object? sender, LightEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.Slot < 0 || e.Slot >= _lastLightLogged.Length) return;
        if (_lastLightLogged[e.Slot] == (e.Player, e.Colour)) return;
        _lastLightLogged[e.Slot] = (e.Player, e.Colour);
        _vm.AddLog($"  Player {e.Slot + 1} light: {e.Colour}" +
                   (_vm.PadKind == PadKind.Xbox360 ? $" (player {e.Player} in games)" : ""));
    });

    /// <summary>What a game or Steam sent a PlayStation 4 pad, byte for byte, when it changes.</summary>
    private void OnOutputReport(int slot, byte[] report)
    {
        var hex = Convert.ToHexString(report.AsSpan(0, Math.Min(report.Length, 11)));
        Dispatcher.BeginInvoke(() =>
        {
            if (slot < 0 || slot >= _lastReportLogged.Length || _lastReportLogged[slot] == hex) return;
            _lastReportLogged[slot] = hex;
            var lightbar = DualShock4OutputReport.TryParse(report, out _, out var colour) && colour is { } c
                ? $" — lightbar {c}"
                : "";
            _vm.AddLog($"  Player {slot + 1} PS4 report {hex}{lightbar}");
        });
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PadKind))
        {
            ChangePadKind(_vm.PadKind);
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.DeveloperMode)) return;

        _settings.DeveloperMode = _vm.DeveloperMode;
        _settings.Save();
        ApplyRefreshRate();
        if (_vm.DeveloperMode) MakeRoomForDeveloperMode();
    }

    /// <summary>
    /// Switch every pad between Xbox 360 and DualShock 4. Connected phones stay
    /// connected; games see their controllers unplugged and the new kind appear.
    /// </summary>
    private void ChangePadKind(PadKind kind)
    {
        _settings.PadKind = kind;
        _settings.Save();
        if (_backend is null) return;

        try
        {
            _backend.SetKind(kind);
            _vm.AddLog(kind == PadKind.DualShock4
                ? "Games now see PlayStation 4 controllers."
                : "Games now see Xbox 360 controllers.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _vm.AddLog($"Could not switch controller type: {ex.Message}");
        }
    }

    /// <summary>Tall enough for the live previews and the log together.</summary>
    private const double DeveloperHeight = 860;

    /// <summary>
    /// Grow the window so the previews and the log fit without scrolling, as far
    /// as the screen allows. Never shrinks it, and leaves a maximised window be.
    /// </summary>
    private void MakeRoomForDeveloperMode()
    {
        if (WindowState != WindowState.Normal || Height >= DeveloperHeight) return;

        var area = SystemParameters.WorkArea;
        Height = Math.Min(DeveloperHeight, area.Height);
        if (Top + Height > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Follow the newest line, like a terminal would -- but not from inside
        // this event. Scrolling forces a layout pass, and this handler can run
        // before the list has heard about the new line; WPF then finds the list
        // and its source disagreeing and throws, taking the server down with it.
        // Queued, it runs once every listener has caught up.
        if (e.Action == NotifyCollectionChangedAction.Add && _vm.DeveloperMode && e.NewItems?[0] is { } item)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_vm.Log.Contains(item)) LogList.ScrollIntoView(item);
            });
    }

    // ── buttons ──────────────────────────────────────────────────────────

    private void Retry_Click(object sender, RoutedEventArgs e) => StartServer();

    private void DownloadDriver_Click(object sender, RoutedEventArgs e) => Open(DriverHelp.DownloadUrl);

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private async void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string address } button) return;
        TrySetClipboard(address);

        button.Content = "Copied";
        await Task.Delay(1200);
        button.Content = "Copy";
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e) =>
        TrySetClipboard(string.Join(Environment.NewLine, _vm.Log));

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _vm.Log.Clear();

    private static void TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another app has the clipboard open; a second click will do it.
        }
    }

    private static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}

/// <summary>True → collapsed, false → visible.</summary>
internal sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Developer mode → one row of four cards; otherwise two by two.</summary>
internal sealed class CardColumnsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? IPadBackend.MaxPads : 2;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
