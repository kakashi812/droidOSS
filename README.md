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

Up to **four phones at once**, which is XInput's own limit.

---

## Install

Download both files from the [latest release](https://github.com/kakashi812/droidOSS/releases/latest):

| File | Goes on |
|---|---|
| `droidOSS-server-vX.Y.Z-win-x64.zip` | Your PC |
| `droidOSS-vX.Y.Z.apk` | Your phone |

> **Use the server and app from the same release.** The protocol has no version negotiation yet, so a mismatched pair fails in confusing ways rather than telling you.

### 1. Install the ViGEmBus driver

This is what lets a program create a controller Windows believes in. droidOSS cannot work without it, and you only do this once.

Download the installer from [ViGEmBus releases](https://github.com/nefarius/ViGEmBus/releases), run it, accept the admin prompt, and reboot if asked.

### 2. Run the server

Unzip and double-click `droidOSS-server.exe`.

Windows will likely show a blue **"Windows protected your PC"** box — that's SmartScreen noting the file isn't signed by a company that paid Microsoft for a certificate. Click **More info** → **Run anyway**.

Nothing else is required. The .NET runtime is bundled inside the executable, so there is no framework to install.

A console window opens and prints the address to use:

```
Waiting for a phone. Point it at:
      192.168.1.42:27500
```

Leave it open — closing it disconnects the controller. If Windows Firewall prompts, allow it and make sure **Private networks** is ticked.

### 3. Install the app

Copy the `.apk` to your phone and tap it. Android will block installing from unknown sources the first time; follow the prompt to allow it for your browser or file manager, then tap the file again.

### 4. Connect

Type the address from step 2 into the app and tap **Connect**. The phone turns sideways and shows a gamepad.

To check it's working, press <kbd>Win</kbd>+<kbd>R</kbd> and run `joy.cpl` → Properties.

---

## The controls

Everything an Xbox 360 pad has, except the Guide button:

- **Two analog sticks** — left and right, independent
- **A real D-pad**, not a stick pretending to be one
- **A / B / X / Y**, **LB / RB**, **LT / RT**
- **L3 / R3** stick clicks
- **Back / Start**

Sticks use a radial deadzone, so diagonals stay diagonal instead of snapping to the compass points.

---

## What it's good at

A touchscreen has no tactile stick registration and no trigger travel. That's physics rather than a bug, and it lands very unevenly:

| | |
|---|---|
| **Platformers, 2D, emulation** | The sweet spot — a real D-pad matters here |
| **Sports (FC26 etc.)** | Strong fit. Tested and comfortable |
| **Racing** | Strong fit |
| **Fighting games** | Decent; tight combos suffer |
| **First-person shooters** | Permanent compromise — twin-stick aiming on glass is poor no matter how well it's built |

---

## Current limitations

Honest list, all being worked on:

- **You type the PC's address by hand.** Automatic discovery isn't built yet.
- **The server is a console window** — no tray icon, no settings.
- **No rumble.** Vibration doesn't travel back to the phone yet.
- **Triggers are on/off**, not gradual.
- **The layout can't be customised.**
- **Windows x64 only.** No ARM build.

---

## If it doesn't work

**"No answer from that address"** — the two devices can't see each other. Check, in order:

1. The server window is still open.
2. The address matches **exactly** what the server printed.
3. Both devices are on the same Wi-Fi — not one on mobile data, and not one on a 2.4 GHz network with the other on the 5 GHz version of it.
4. Your router doesn't have **AP isolation** (sometimes "client isolation") enabled. This blocks devices from reaching each other, and no software can work around it.

**"Could not reach the ViGEmBus driver"** — step 1 didn't complete. Reinstall and reboot.

**The controller works but a game ignores it** — if it's on Steam, try right-clicking the game → Properties → Controller → **Disable Steam Input**. Steam's remapping layer sits on top and sometimes swallows input.

**It feels laggy** — 5 GHz Wi-Fi helps a lot, and a PC on Ethernet helps more. 2.4 GHz in a building full of networks is the usual cause.

---

## Building from source

**Server** — needs the [.NET SDK](https://dotnet.microsoft.com/download) 10.0 or newer:

```bash
dotnet test server/DroidOSS.sln          # 109 tests
dotnet run --project server/DroidOSS.App
```

**Android app** — open `androidapp3/` in Android Studio (not the repo root), or:

```bash
cd androidapp3
./gradlew testDebugUnitTest
./gradlew installDebug
```

**Testing without a phone** — `tools/fake_phone.py` is a complete second implementation of the protocol, used as a permanent regression harness:

```bash
py tools/fake_phone.py --selftest        # verify encoding against golden vectors
py tools/fake_phone.py --host 127.0.0.1  # stream to a running server
py tools/fake_phone.py --listen          # decode an incoming stream
```

---

## Design

Some decisions here are deliberately counterintuitive — UDP over TCP, fixed binary over JSON, sending complete state rather than button events. Each is load-bearing, and each is explained in full:

- **[`docs/PROTOCOL.md`](docs/PROTOCOL.md)** — the wire protocol. Canonical spec, implemented by hand in C#, Kotlin and Python.
- **[`book/`](book/)** — a 12-chapter design document covering the architecture from first principles. Open `book/index.html` in a browser.

```
server/       C# / .NET  — Windows server (Core / ViGEm / App / Tests)
androidapp3/  Kotlin     — the phone app, Jetpack Compose
tools/        Python     — fake-phone test client
docs/                    — protocol spec
book/                    — design document
```

---

## Licence

**GPL-3.0-only.** See [`LICENSE`](LICENSE).

Includes code derived from [PadConnect](https://github.com/Ishan09811/PadConnect) © 2026 Ishan, which is GPL-3.0-only — this is why droidOSS carries the same licence. Derived files keep their original copyright notice and are marked as modified.

## Acknowledgements

Built on [ViGEmBus](https://github.com/nefarius/ViGEmBus) by Nefarius. The architecture draws on prior open-source work solving the same problem — [PadConnect](https://github.com/Ishan09811/PadConnect), [Joy2DroidX](https://github.com/OzymandiasTheGreat/Joy2DroidX-server), and [VirtualGamePad](https://kitswas.github.io/VirtualGamePad/).

Not affiliated with, endorsed by, or derived from the source of [DroidJoy](https://grill2010.github.io/droidJoy.html), a closed-source product solving a similar problem. Where the design document discusses DroidJoy, it reasons only from its public documentation.
