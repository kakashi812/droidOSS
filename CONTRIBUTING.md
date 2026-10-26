# Contributing to droidOSS

## How work is planned

- Every piece of work starts as a **GitHub issue**: a bug with steps to
  reproduce, or a feature with the behaviour wanted.
- Issues are grouped into **milestones**, one per version (`v0.3.0`, `v0.4.0`…).
  A milestone is one iteration: the issues we commit to for that release. When
  they are all closed, the version is released.
- Anything not yet planned stays in the backlog with no milestone.

## Branches and commits

- `main` always builds and passes every check. Nobody commits to it directly.
- Work happens on a short-lived branch from the latest `main`:

  | Prefix | For |
  |---|---|
  | `feature/` | new behaviour — `feature/server-autodiscovery` |
  | `fix/` | bug fixes — `fix/discovery-port-<issue>` |
  | `release/` | version bumps — `release/v0.2.0` |
  | `docs/`, `test/`, `ci/` | changes that touch only those |

- Commit messages are short and imperative, saying what changes for someone
  using droidOSS: `Add LAN autodiscovery so the app finds servers without typing an IP`.

## Pull requests

- Open a pull request into `main` and fill in the template.
- Write `Fixes #<issue>` in the description so merging closes the issue and
  moves the milestone forward.
- The `tests` and `builds` checks must be green. A failing check is fixed on the
  branch, never after merging.
- Anything a computer cannot check — how it feels on a phone, a real game — is
  tested on a device, and the description says which device.

## Definition of done

A change is done when:

1. It does what the issue asked, on a real phone and a real Windows PC where
   that matters.
2. New logic has unit tests, and the negative cases are covered: malformed
   packets, a full server, a phone that goes silent, Unicode names.
3. Anything that crosses a socket has an integration test in
   `server/DroidOSS.IntegrationTests`.
4. A protocol change updates `docs/PROTOCOL.md` **and** all three
   implementations (C#, Kotlin, Python), and `tools/check_golden.py` passes.
5. `tests` and `builds` are green on the pull request.
6. `CHANGELOG.md` has a line under *Unreleased* if users will notice.

## Running the checks locally

```bash
dotnet test server/DroidOSS.Tests                  # unit
dotnet test server/DroidOSS.IntegrationTests       # real UDP on loopback
py tools/fake_phone.py --selftest
py tools/check_golden.py                           # C#, Kotlin, Python agree
cd androidapp3 && ./gradlew testDebugUnitTest
```

On Linux the whole solution, WPF included, builds with
`dotnet build server/DroidOSS.sln -p:EnableWindowsTargeting=true`. It only runs
on Windows.
