# droidOSS

Turn an Android phone into a gamepad your PC can't tell from plastic.

A Windows server creates a virtual Xbox 360 controller through the [ViGEmBus](https://github.com/nefarius/ViGEmBus) driver, and an Android app streams touch input to it over Wi-Fi. Games, Steam, and Windows itself see an ordinary controller.

```
   your phone                 droidOSS server              any game
  ┌───────────┐   UDP/Wi-Fi   ┌──────────────┐  ViGEmBus  ┌──────────┐
  │  on-screen│ ────────────► │  20 bytes →  │ ─────────► │  sees a  │
  │  gamepad  │   125 × /sec  │ virtual pad  │            │ real pad │
  └───────────┘               └──────────────┘            └──────────┘
```

## Status

**In development — nothing is released yet.** No installable build exists.

Being built in blocks, each producing something runnable:

- [ ] **B0–B3** — the server: virtual pad, UDP listener, sessions, timeout handling
- [ ] **B4–B5** — the Android app: touch tracking and feel
- [ ] **B6–B7** — discovery, packaging, usable by someone else
- [ ] **B8+** — rumble, then post-v1 extras

## Design

Some decisions here are deliberately counterintuitive — UDP over TCP, fixed binary over JSON, sending complete state rather than button events. Each is load-bearing, and each is explained in full:

- **[`docs/PROTOCOL.md`](docs/PROTOCOL.md)** — the wire protocol. Canonical spec, implemented by hand in C#, Kotlin and Python.
- **[`book/`](book/)** — a 12-chapter design document covering the whole architecture from first principles. Open `book/index.html` in a browser.

## Layout

```
server/    C# / .NET  — Windows server
android/   Kotlin     — the phone app
tools/     Python     — fake-phone test client
docs/                 — protocol spec
book/                 — design document
```

## Requirements

- Windows PC with [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) installed
- An Android phone on the same Wi-Fi network
- 5 GHz Wi-Fi strongly recommended; PC on Ethernet is better still

## Acknowledgements

Built on [ViGEmBus](https://github.com/nefarius/ViGEmBus) by Nefarius. The architecture draws on prior open-source work solving the same problem — [PadConnect](https://github.com/Ishan09811/PadConnect), [Joy2DroidX](https://github.com/OzymandiasTheGreat/Joy2DroidX-server), and [VirtualGamePad](https://kitswas.github.io/VirtualGamePad/).

Not affiliated with, endorsed by, or derived from the source of [DroidJoy](https://grill2010.github.io/droidJoy.html), a closed-source product solving a similar problem. Where the design document discusses DroidJoy, it reasons only from its public documentation.
