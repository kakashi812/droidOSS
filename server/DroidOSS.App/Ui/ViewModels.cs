using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using DroidOSS.Core;

namespace DroidOSS.App.Ui;

/// <summary>Raises <see cref="PropertyChanged"/> only when a value really changes.</summary>
internal abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Which of the window's screens is showing.</summary>
internal enum Screen
{
    Starting,
    Running,

    /// <summary>ViGEmBus is not installed, or not answering.</summary>
    DriverMissing,

    /// <summary>The input port is taken: another copy of the server is running.</summary>
    PortInUse,
}

/// <summary>One of the four player cards.</summary>
internal sealed class SlotViewModel(int slot) : ObservableObject
{
    private readonly RateMeter _rate = new();

    private bool _connected;
    private string _address = "";
    private string _connectedFor = "";
    private LinkQuality _quality;
    private string _rateText = "";
    private string _packetsText = "";
    private string _rumbleText = "";
    private PadState _state;
    private Brush _light = Brushes.Transparent;
    private string _lightText = "";
    private (byte, Rgb, PadKind)? _lastLight;

    public int Slot { get; } = slot;

    /// <summary>The colour the phone shows: the player's, or the game's lightbar.</summary>
    public Brush Light { get => _light; private set => Set(ref _light, value); }

    /// <summary>"Player 2 in games", or the lightbar colour on a DualShock 4.</summary>
    public string LightText { get => _lightText; private set => Set(ref _lightText, value); }

    /// <summary>Show what the phone in this slot is being told about its light.</summary>
    public void SetLight(byte player, Rgb colour, PadKind kind)
    {
        if (_lastLight == (player, colour, kind)) return;
        _lastLight = (player, colour, kind);

        var brush = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
        brush.Freeze();
        Light = brush;
        LightText = kind == PadKind.DualShock4
            ? $"Lightbar {colour}"
            : $"Player {player} in games";
    }

    /// <summary>Player numbers start at 1, as on the phone and on an Xbox.</summary>
    public string Title => $"Player {Slot + 1}";

    public bool IsConnected { get => _connected; private set => Set(ref _connected, value); }

    /// <summary>The phone's IP address, without the port.</summary>
    public string Address { get => _address; private set => Set(ref _address, value); }

    public string ConnectedFor { get => _connectedFor; private set => Set(ref _connectedFor, value); }

    public LinkQuality Quality
    {
        get => _quality;
        private set
        {
            if (Set(ref _quality, value)) Raise(nameof(QualityText));
        }
    }

    public string QualityText => Quality switch
    {
        LinkQuality.Good => "Good connection",
        LinkQuality.Fair => "Fair connection",
        LinkQuality.Poor => "Poor connection",
        _ => "Checking connection…",
    };

    // ── developer mode ───────────────────────────────────────────────────

    public string RateText { get => _rateText; private set => Set(ref _rateText, value); }
    public string PacketsText { get => _packetsText; private set => Set(ref _packetsText, value); }
    public string RumbleText { get => _rumbleText; set => Set(ref _rumbleText, value); }

    /// <summary>The latest input, for the live preview.</summary>
    public PadState State { get => _state; private set => Set(ref _state, value); }

    /// <summary>Refresh from the latest snapshot; null when the slot is empty.</summary>
    public void Update(SessionInfo? session, DateTimeOffset now)
    {
        if (session is not { } s)
        {
            if (!IsConnected) return;
            IsConnected = false;
            Address = ConnectedFor = RateText = PacketsText = RumbleText = "";
            State = default;
            Quality = LinkQuality.Unknown;
            _rate.Reset();
            return;
        }

        IsConnected = true;
        Address = s.Client.ToString().Split(':')[0];
        ConnectedFor = Describe(now - s.ConnectedAt);
        State = s.LastState;

        _rate.Update(s.Applied, now);
        Quality = LinkQualityRating.FromRate(_rate.Rate);
        RateText = $"{(_rate.Rate is { } hz ? $"{hz:0}" : "–")} Hz · {s.Applied:N0} packets";
        PacketsText = $"from port {s.Client.Port}";
    }

    private static string Describe(TimeSpan span) => span switch
    {
        { TotalSeconds: < 60 } => "Connected just now",
        { TotalMinutes: < 60 } => $"Connected for {(int)span.TotalMinutes} min",
        _ => $"Connected for {(int)span.TotalHours} h {span.Minutes} min",
    };
}

/// <summary>One line in the developer log.</summary>
internal sealed record LogEntry(DateTime Time, string Text)
{
    public override string ToString() => $"{Time:HH:mm:ss}  {Text}";
}

/// <summary>Everything the main window shows.</summary>
internal sealed class MainViewModel : ObservableObject
{
    /// <summary>Old lines are dropped past this, so a long session cannot grow without bound.</summary>
    public const int MaxLogLines = 1000;

    private Screen _screen = Screen.Starting;
    private string _serverName = Environment.MachineName;
    private bool _discoverable = true;
    private string _discoveryWarning = "";
    private int _connectedCount;
    private bool _developerMode;
    private string _errorDetails = "";
    private string _droppedText = "";
    private PadKind _padKind;

    public SlotViewModel[] Slots { get; } =
        Enumerable.Range(0, IPadBackend.MaxPads).Select(i => new SlotViewModel(i)).ToArray();

    public ObservableCollection<string> Addresses { get; } = [];

    /// <summary>"version 0.2.0", from the assembly — set once, in the csproj.</summary>
    public string Version { get; } =
        $"version {typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "?"}";

    public ObservableCollection<LogEntry> Log { get; } = [];

    public Screen Screen
    {
        get => _screen;
        set
        {
            if (!Set(ref _screen, value)) return;
            Raise(nameof(IsRunning));
            Raise(nameof(IsDriverMissing));
            Raise(nameof(IsPortInUse));
            Raise(nameof(IsStarting));
        }
    }

    public bool IsStarting => Screen == Screen.Starting;
    public bool IsRunning => Screen == Screen.Running;
    public bool IsDriverMissing => Screen == Screen.DriverMissing;
    public bool IsPortInUse => Screen == Screen.PortInUse;

    public string ServerName { get => _serverName; set => Set(ref _serverName, value); }

    public bool Discoverable { get => _discoverable; set => Set(ref _discoverable, value); }

    public string DiscoveryWarning { get => _discoveryWarning; set => Set(ref _discoveryWarning, value); }

    public string ErrorDetails { get => _errorDetails; set => Set(ref _errorDetails, value); }

    public int ConnectedCount
    {
        get => _connectedCount;
        set
        {
            if (Set(ref _connectedCount, value)) Raise(nameof(StatusText));
        }
    }

    public string StatusText => ConnectedCount switch
    {
        0 => "Ready — waiting for phones",
        1 => "1 player connected",
        _ => $"{ConnectedCount} players connected",
    };

    public bool DeveloperMode { get => _developerMode; set => Set(ref _developerMode, value); }

    /// <summary>Which controller games see.</summary>
    public PadKind PadKind
    {
        get => _padKind;
        set
        {
            if (!Set(ref _padKind, value)) return;
            Raise(nameof(IsXbox));
            Raise(nameof(IsDualShock4));
            Raise(nameof(PadKindHint));
        }
    }

    // Two-way bindings for the pair of radio buttons.
    public bool IsXbox { get => PadKind == PadKind.Xbox360; set { if (value) PadKind = PadKind.Xbox360; } }
    public bool IsDualShock4 { get => PadKind == PadKind.DualShock4; set { if (value) PadKind = PadKind.DualShock4; } }

    public string PadKindHint => PadKind == PadKind.DualShock4
        ? "Phones show the lightbar colour the game or Steam sets. Games show PlayStation buttons; " +
          "older games may need Steam Input on to see it."
        : "Works with every PC game. Phones show their player colour: 1 blue, 2 red, 3 green, 4 pink.";

    /// <summary>Dropped-packet counters, shown above the log.</summary>
    public string DroppedText { get => _droppedText; set => Set(ref _droppedText, value); }

    public void AddLog(string text)
    {
        Log.Add(new LogEntry(DateTime.Now, text));
        while (Log.Count > MaxLogLines) Log.RemoveAt(0);
    }
}
