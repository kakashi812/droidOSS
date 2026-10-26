# Changelog

All notable changes to droidOSS are recorded here, written for people who use it.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and
versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- GitHub Actions: every pull request runs the server unit tests, the new
  integration tests, the Android unit tests and a cross-language protocol check,
  with merged coverage in the job summary (#5).
- GitHub Actions: the server `.exe` and the Android APK are built on every pull
  request and nightly, and uploaded as downloadable artifacts (#6).
- Integration tests that run the real server over UDP on loopback, including one
  driven by `tools/fake_phone.py` (#7).
- `tools/check_golden.py`, which fails if the C#, Kotlin and Python copies of the
  golden packet ever disagree.
- `CONTRIBUTING.md`, `docs/RELEASING.md`, and issue and pull request templates (#8).

### Changed
- The server's sockets (`UdpSessionListener`, `DiscoveryResponder`,
  `ServerHost`) moved into a platform-neutral `DroidOSS.Net` library. No
  change in behaviour.
- Discovery now announces the input port the server actually bound.

## [0.2.0] - 2026-10-06

### Added
- Automatic discovery: the app lists every server on the Wi-Fi by PC name; tap
  one to connect (#1).
- A proper server window with four Player cards, connection quality, and a
  Copy button for the PC's address (#3).
- Developer mode on the server: live sticks, triggers and buttons per phone,
  packet rates and a log.
- Up to 10 custom layouts, and sharing them by file, WhatsApp or QR code (#2).
- Friendly screens for a missing ViGEmBus driver and for a second running copy.

### Changed
- The app is signed with a new key. Uninstall 0.1.x before installing.

## [0.1.1] - 2026-07-30

### Added
- Customise the controller layout from the home screen.
- The ViGEmBus driver installer is included in the release.

## [0.1.0] - 2026-07-21

### Added
- First release: a Windows server that creates virtual Xbox 360 controllers
  through ViGEmBus, and an Android app that streams touch input over UDP.
- Up to four phones at once.

[Unreleased]: https://github.com/kakashi812/droidOSS/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/kakashi812/droidOSS/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/kakashi812/droidOSS/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/kakashi812/droidOSS/releases/tag/v0.1.0
