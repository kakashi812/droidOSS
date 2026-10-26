# Releasing droidOSS

A release ships two files that must come from the same commit, because the
protocol has no version negotiation yet (#12): the server zip and the APK.

## Checklist

1. **Milestone done.** Every issue in the milestone is closed, or moved on.
2. **Branch.** `release/vX.Y.Z` from the latest `main`.
3. **Bump the version** in both places:
   - `server/DroidOSS.App/DroidOSS.App.csproj` — `<Version>`
   - `androidapp3/app/build.gradle.kts` — `versionName`, and `versionCode` + 1
4. **Changelog.** Move *Unreleased* in `CHANGELOG.md` under the new version and date.
5. **Pull request** titled `Version X.Y.Z`. Wait for `tests` and `builds`.
6. **Build the server** on Windows (the `builds` artifact is identical):

   ```bash
   dotnet publish server/DroidOSS.App -c Release -r win-x64 --self-contained \
     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
     -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
   ```

   Zip `dist/droidOSS-server.exe` with a short README as
   `droidOSS-server-vX.Y.Z-win-x64.zip`.
7. **Build the APK** with the release keystore from `local.properties`
   (never committed, never given to CI):
   `cd androidapp3 && ./gradlew assembleRelease`, then rename it to `droidOSS-vX.Y.Z.apk`.
8. **Smoke test.** Install both on real hardware, connect a phone, check
   `joy.cpl` and one game.
9. **Merge** the pull request, then **tag and publish** a GitHub release
   `vX.Y.Z` with the zip, the APK, the ViGEmBus installer and their SHA-256 sums.
10. **Close the milestone.**
