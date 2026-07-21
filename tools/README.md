# tools

## `fake_phone.py`

A phone that isn't there. Sends droidOSS INPUT packets over UDP so the server can
be built and debugged with no Android device involved.

**This is not scaffolding to delete.** It stays as the permanent regression
harness — when the real app misbehaves later, this is how you find out whether
the problem is the phone or the server, by swapping one for a known-good
implementation.

It is also an independent implementation of the wire format. The server speaks it
in C#, the app will speak it in Kotlin; a third implementation that agrees
byte-for-byte is what proves [`../docs/PROTOCOL.md`](../docs/PROTOCOL.md) is
unambiguous rather than merely written down.

Standard library only — no `pip install` needed.

### Self-test — check the two implementations still agree

```
py tools/fake_phone.py --selftest
```

Prints the golden packet as hex. It must match `GoldenBytes` in
`server/DroidOSS.Tests/InputPacketTests.cs` exactly:

```
DA 01 01 02 78 56 34 12 04 10 20 C8 E8 03 18 FC FF 7F 00 80
```

A mismatch means the protocol has drifted between languages — the failure mode
that produces no error at all, just a controller that behaves strangely.

### Send — drive the server

```
py tools/fake_phone.py --host 127.0.0.1
py tools/fake_phone.py --host 192.168.1.12 --pad 1 --rate 125
```

Streams INPUT packets at a fixed rate, sweeping the left stick in a circle and
toggling the A button once a second so both axes and button bits are visibly
moving. Ctrl+C sends a neutral state on the way out.

### Listen — see what's on the wire

```
py tools/fake_phone.py --listen
```

Decodes an incoming stream and reports rate and lost-packet count. Useful for
answering "is anything actually being sent?" without involving the server —
point the real Android app at this to check the phone side in isolation.

### Options

| | |
|---|---|
| `--host` | server address (default `127.0.0.1`) |
| `--port` | UDP port (default `27500`) |
| `--pad` | pad slot 0–3 to claim (default `0`) |
| `--rate` | packets per second (default `125`) |
| `--duration` | stop after N seconds (default: until Ctrl+C) |
| `--selftest` | verify encoding, print the golden packet, exit |
| `--listen` | receive and decode instead of sending |

### Testing both ends at once

Two terminals — listener first:

```
py tools/fake_phone.py --listen
py tools/fake_phone.py --host 127.0.0.1
```

Expect `0 lost` and a steady 125 Hz over loopback.

## A note on `python` vs `py` on this machine

`python` is shadowed by the Microsoft Store alias stub and will tell you to
install from the Store. **Use `py`.** To fix it permanently: Settings → Apps →
Advanced app settings → App execution aliases → turn off `python.exe` and
`python3.exe`.

Python **3.11+** matters here: that release switched `time.sleep()` on Windows to
high-resolution waitable timers. On older versions the send loop is capped near
64 Hz by the default 15.6 ms timer granularity. Verified working on 3.13.
