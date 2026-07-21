namespace DroidOSS.Core;

/// <summary>
/// The wire format shared by the server, the phone, and the test tools.
/// </summary>
/// <remarks>
/// These values are transcribed from <c>docs/PROTOCOL.md</c>, which is the
/// canonical specification. They are implemented by hand in three languages —
/// C# here, Kotlin in the Android app, Python in <c>tools/fake_phone.py</c> —
/// and a disagreement between them produces no error at all, just a controller
/// that behaves strangely.
///
/// Change anything here and you must change all three, and bump <see cref="Version"/>.
/// </remarks>
public static class Protocol
{
    /// <summary>
    /// First byte of every packet. Anything without it is not ours.
    /// </summary>
    /// <remarks>
    /// A UDP socket receives port scans, other applications' stray broadcasts,
    /// and traffic from someone who mistyped an address. Without a marker byte
    /// that garbage would be read as controller state.
    /// </remarks>
    public const byte MagicByte = 0xDA;

    /// <summary>
    /// Format version, so an old phone and a new server can refuse each other
    /// politely rather than misreading the layout.
    /// </summary>
    public const byte Version = 1;

    /// <summary>UDP port carrying input and session messages.</summary>
    public const int InputPort = 27500;

    /// <summary>
    /// UDP port for discovery broadcasts only, kept separate so broadcast
    /// traffic never reaches the input socket's hot path.
    /// </summary>
    public const int DiscoveryPort = 27501;

    /// <summary>Total size of an INPUT packet: 4 header + 4 sequence + 12 payload.</summary>
    public const int InputPacketSize = 20;

    /// <summary>Size of the header every message shares, whatever its type.</summary>
    public const int HeaderSize = 4;

    /// <summary>
    /// Size of HELLO, WELCOME and BYE. They carry no payload — the header says
    /// everything they need to.
    /// </summary>
    public const int SessionMessageSize = HeaderSize;

    /// <summary>
    /// The <c>pad</c> byte when it names no particular slot.
    /// </summary>
    /// <remarks>
    /// In HELLO it means "any slot, you choose" — a phone never picks its own,
    /// or two phones both claim pad 0. In WELCOME it means the opposite end of
    /// the same conversation: "no slot for you", all four are in use.
    /// </remarks>
    public const byte NoPad = 0xFF;

    /// <summary>
    /// How long a session survives without a packet before the pad is zeroed
    /// and unplugged.
    /// </summary>
    /// <remarks>
    /// The phone sends every 8 ms, so 2 s is 250 missed packets — unambiguous
    /// silence rather than a rough patch of Wi-Fi. This is why the Android app
    /// must keep a send-rate floor even when idle: throttle below one packet per
    /// two seconds and the server would disconnect a phone sitting in a menu.
    /// </remarks>
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a client waits for WELCOME before sending HELLO again.
    /// </summary>
    /// <remarks>
    /// UDP does not guarantee delivery, so a HELLO can simply vanish and the
    /// server would never know a phone was there. The client retries — it is the
    /// side that wants something. This also covers the ordinary case of the app
    /// being opened before the server is running.
    /// </remarks>
    public static readonly TimeSpan HelloRetryInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>Byte positions within a packet. The codec never contains a bare number.</summary>
    public static class Offset
    {
        public const int Magic = 0;
        public const int Version = 1;
        public const int Type = 2;
        public const int Pad = 3;
        public const int Sequence = 4;

        // The payload below is byte-for-byte XINPUT_GAMEPAD — see PadState.
        public const int Buttons = 8;
        public const int LeftTrigger = 10;
        public const int RightTrigger = 11;
        public const int ThumbLX = 12;
        public const int ThumbLY = 14;
        public const int ThumbRX = 16;
        public const int ThumbRY = 18;
    }
}

/// <summary>
/// What a packet is for. Byte 2 of every message.
/// </summary>
/// <remarks>
/// There is deliberately no heartbeat type. The input stream is the heartbeat —
/// a packet every 8 ms means silence is unmistakable, which is what the
/// disconnect timeout relies on.
/// </remarks>
public enum MessageType : byte
{
    /// <summary>The 20-byte state snapshot. Phone to PC, 125 times a second.</summary>
    Input = 0x01,

    /// <summary>"I'm here." Phone to PC. The server assigns a pad slot in reply.</summary>
    Hello = 0x02,

    /// <summary>"You're pad 2." PC to phone. Also proves a server exists at this address.</summary>
    Welcome = 0x03,

    /// <summary>Clean exit. Phone to PC, so the pad unplugs now rather than on timeout.</summary>
    Bye = 0x04,

    /// <summary>Vibration intensity from the game. PC to phone.</summary>
    Rumble = 0x05,

    /// <summary>"Any servers out there?" Broadcast, answered with <see cref="Welcome"/>.</summary>
    Discover = 0x06,
}
