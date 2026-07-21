# droidOSS wire protocol

**Version 1** · UDP · little-endian

This is the canonical specification. It is implemented by hand in **three languages** — C# (server), Kotlin (Android app), Python (`tools/fake_phone.py`) — and there is no shared code generating it. A mismatch between implementations **fails silently**: no exception, no error, just a stick that moves when you press B and a long confusing evening.

**Changing anything here means changing all three implementations and bumping `ver`.**

---

## Ports

| Port | Purpose |
|---|---|
| `27500` | UDP — input stream and session messages |
| `27501` | UDP — discovery broadcast only |

Discovery is kept on its own port so broadcast traffic never touches the input socket's hot path.

## Input packet — 20 bytes

Message type `0x01`. Sent by the phone at a fixed 125 Hz.

```
byte   0     1     2     3     4-7      8-9     10    11   12-13 14-15 16-17 18-19
     0xDA   ver  type   pad    seq    buttons   LT    RT    LX    LY    RX    RY
      u8    u8    u8    u8     u32      u16     u8    u8   i16   i16   i16   i16
     └───── header ─────┘  └ordering┘  └────────────── payload ──────────────────┘
```

| Field | Type | Meaning |
|---|---|---|
| `0xDA` | `u8` | Magic byte. If absent, the packet is not ours — discard immediately. |
| `ver` | `u8` | Protocol version. Currently `1`. |
| `type` | `u8` | Message type — see table below. |
| `pad` | `u8` | Which of the four virtual pads (`0`–`3`) this belongs to. |
| `seq` | `u32` | Monotonic counter, +1 per packet sent. |
| `buttons` | `u16` | Button bitmask — see below. |
| `LT` / `RT` | `u8` | Trigger travel, `0`–`255`. |
| `LX`/`LY`/`RX`/`RY` | `i16` | Stick axes, `-32768`–`+32767`. Y is **positive-up**. |

**Bytes 8–19 are byte-for-byte identical to `XINPUT_GAMEPAD`.** This is deliberate: the server performs zero conversion, reading straight off the wire into the driver. A future Linux/`uinput` backend does its own conversion, because it is the exception rather than the common case.

### Button bitmask (bytes 8–9)

| Bit | Value | Button | Bit | Value | Button |
|---|---|---|---|---|---|
| 0 | `0x0001` | D-pad Up | 8 | `0x0100` | Left shoulder |
| 1 | `0x0002` | D-pad Down | 9 | `0x0200` | Right shoulder |
| 2 | `0x0004` | D-pad Left | 10 | `0x0400` | Guide |
| 3 | `0x0008` | D-pad Right | 11 | `0x0800` | *unused* |
| 4 | `0x0010` | Start | 12 | `0x1000` | A |
| 5 | `0x0020` | Back | 13 | `0x2000` | B |
| 6 | `0x0040` | Left stick click | 14 | `0x4000` | X |
| 7 | `0x0080` | Right stick click | 15 | `0x8000` | Y |

## Message types (byte 2)

| Type | Direction | Purpose |
|---|---|---|
| `0x01` INPUT | phone → PC | The 20-byte snapshot, 125×/sec. |
| `0x02` HELLO | phone → PC | "I'm here." Server assigns a pad slot and plugs in a virtual pad. |
| `0x03` WELCOME | PC → phone | "You're pad 2." Also proves a server exists at this address. |
| `0x04` BYE | phone → PC | Clean exit — unplug now rather than waiting for the timeout. |
| `0x05` RUMBLE | PC → phone | Vibration intensity from the game. |
| `0x06` DISCOVER | broadcast | "Any servers out there?" Answered with WELCOME. |

Non-INPUT messages share the same 4-byte header; their payloads are defined as each is implemented.

**There is deliberately no heartbeat message.** The input stream *is* the heartbeat — a packet every 8 ms means silence is unmistakable.

---

## The three rules that break things silently

### 1. Little-endian, set explicitly on Kotlin

ARM and x86 are both little-endian natively, so this costs nothing on either side. **But Java/Kotlin's `ByteBuffer` defaults to big-endian.** Forget `.order(ByteOrder.LITTLE_ENDIAN)` and every multi-byte field is silently wrong — `1000` reads back as `59395`, with no error anywhere.

C#'s `BitConverter` and Python's `struct` with `<` prefix are already little-endian.

### 2. Sequence comparison must handle wrap

UDP can deliver out of order. Discard anything not newer:

```csharp
if ((int)(seq - lastSeq) <= 0) return;   // stale or duplicate
lastSeq = seq;
```

**Use the subtract-and-cast form, not `seq <= lastSeq`.** At 125 packets/sec the `u32` counter wraps after ~1.1 years of continuous play; the naive comparison would then reject every subsequent packet forever, permanently freezing the controller. Subtracting unsigned and interpreting as signed handles the boundary correctly.

### 3. Validate before trusting

Check the magic byte *and* the version before reading any field. Sockets receive port scans, other apps' strays, and mistyped-IP traffic. Without the magic byte, garbage becomes controller state and the character spasms.

---

## Version history

| `ver` | Status | Changes |
|---|---|---|
| `1` | current | Initial format — 20-byte input packet, six message types. |
