# nCTRL — Godot 4.7 C# / Hyundai USA

N Performance styling, pixel Ioniq 5 N, six presets including **Ecotonic Gray**, and custom paint. This revision includes direct Hyundai USA / Bluelink HTTP connectivity, secure phone-side saved login, and biometric unlock. App logic is C# with small native OS storage bridges. No Python runtime or backend.

**Implementation status:** C# app/export plugin and Android vault Java compiled. The bundled Android AAR includes the nCTRL prompts. The iOS vault source/build script requires compilation and validation on a Mac. See `SECURE_LOGIN.md` for mobile setup. The owner reported a live sign-in 502; this revision has not been authenticated against a real Hyundai account or tested on a physical vehicle. The working local fiveN sign-in profile was compared and adopted for token/enrollment requests; live resolution remains unverified. See `SIGN_IN_DIAGNOSIS.md`. Mobile exports have not been tested.

## Run and connect

1. Install **Godot 4.7 .NET** and the .NET 8 SDK for a desktop preview.
2. Import `project.godot`, build, and press **F5**. First build restores the Godot NuGet packages. `IoniqN.sln` retains its internal name for compatibility; the app is nCTRL.
3. The app opens directly to **Connect your Hyundai**, or biometric unlock if an account is saved.
4. Enter your US Bluelink email, password and four-digit Bluelink service PIN **inside the app**. Do not put credentials into the source code or a chat message.
5. On a supported phone, leave **Save account with Face ID / biometrics** selected and authenticate to store the account securely. Subsequent launches show biometric unlock. Desktop builds remain session-only.
6. Select your enrolled vehicle. The app reads its cached status; connecting does not send a remote vehicle command.
7. A **LIVE** badge indicates the connected UI. Controls require confirmation and display the selected vehicle's name and VIN suffix before submission.

There is no simulated vehicle mode. Startup, locking, backgrounding and sign-out all return to the account screen. Vehicle navigation appears only after you select an enrolled vehicle and its status loads. A saved phone account opens the locked login screen. **Lock app** clears the active session but retains protected credentials; **Sign out & forget** also deletes the saved account. Backgrounding ends the live session. An expired or denied session can be re-entered by unlocking or signing in manually. Failed refreshes preserve the last known vehicle state.

## Implemented live functions

- US account sign-in and in-session token refresh, using Hyundai's published-by-community JSON protocol.
- Active vehicle enrollment selection and cached status reads.
- Battery, range, door lock, climate, cable, charging, and separate AC/DC limits where reported.
- Explicit **Wake vehicle and refresh**, separate from cached refresh.
- Lock and unlock; EV climate start/update and stop; EV charging start and stop; AC/DC charge targets.
- Command acceptance and transaction-status checks. **Accepted is not confirmed**; successful completion does not fabricate a new vehicle status. Refresh the reported state afterward.
- On-demand vehicle location with coordinates and an explicit option to open Maps.
- Optional device-bound saved credentials and biometric unlock, masked password/PIN inputs, background locking, sanitized errors, and no automatic command retries.

Missing status fields display **—** or **Unknown**. Server timestamps are shown as supplied; compact timestamps are not assigned an invented timezone. The dashboard is the last reported state, not continuous telemetry.

The shared target endpoint sends both AC and DC targets. Both selections appear in the confirmation dialog; inspect them before confirming. EV climate requests turn off defrost and supplemental heating; generation 3 includes five minutes and seats off. The adapter does not start an ICE engine.

## Account compatibility and limitations

This is an **unofficial US Hyundai-only adapter**, adapted from the current Hyundai-Kia-Connect implementation. It is not an official SDK, and does not support Kia, Canadian, European or other regional accounts.

The source US adapter currently implements username/password token login, not an MFA/OTP challenge exchange. If Hyundai requires MFA, CAPTCHA, a safety acknowledgement or another challenge, this build stops with a sanitized error. It does not bypass checks or automatically retry sign-in. Completing a challenge in the official app is not guaranteed to make this adapter work.

The application uses standard HTTPS validation and does not weaken TLS settings. Hyundai's service can deny an unofficial client or change its protocol. Authentication and commands require a working account and the appropriate enabled services; server acceptance remains authoritative. No claim is made for phone keys, cameras, remote performance settings or full Tesla-app parity.

## Sign-in diagnostics

Sign-in failures identify **Authentication** (token request) or **Vehicle list** (enrollment request). HTTP status and JSON API error codes are separate: `HTTP 502` and `HTTP 200; API code 502` are different observations. Neither alone establishes a bad password, outage or endpoint change. Only sanitized stage/status/code text is displayed; raw responses and account URLs are never included. To diagnose a failure, share only that in-app error text, never credentials or network dumps.

## Rename and saved-data compatibility

The display name, visible branding, native biometric prompts and editor plugin label are **nCTRL**. `NGarage.Core`, `IoniqN.csproj`/assembly/solution, `NGarageVault`, native entry points/package names, iOS Keychain service/install marker, Android key prefix/ciphertext filename/AES-GCM associated data remain unchanged. In particular, the literal `N Garage / Bluelink credentials / v1` is cryptographic associated data, not visible branding; renaming it would break existing ciphertext authentication.

Desktop `user://` explicitly stays at `Godot/app_userdata/N Garage` under the OS app-data directory, preserving this checkout's paint and garage preferences. Godot's custom-directory setting only affects desktop; mobile storage remains within the existing app sandbox ([Godot data-path documentation](https://docs.godotengine.org/en/stable/tutorials/io/data_paths.html)). Ecotonic Gray and the existing Cyber Gray preference migration are retained. An older build that stored preferences under `IoniqN` needs a one-time copy of **only** `preferences.cfg` into the retained N Garage directory while the app is closed; no credential files should be copied or converted.

There is no `export_presets.cfg` or configured application bundle/package ID in this checkout. New export display names default to the project name, nCTRL. In existing external presets, set Android **Package → Name** and iOS **Application → Name** to nCTRL (or leave blank to inherit). Keep the previously shipped Android package ID/signing key and iOS bundle ID/team/Keychain access group to retain access to saved credentials. If an identity must change, users must sign in and save credentials again; this revision does not migrate secrets between app identities. Rebuild the iOS plugin to pick up its renamed Face ID text.

## Storage, request limits and errors

With secure saving enabled on a supported phone, email/password/PIN are protected by Apple Keychain or Android Keystore-backed encryption and biometric authentication. Tokens, enrollment, vehicle state, location and history remain memory-only. Locking drops session references; this is not guaranteed zeroization of managed strings. Only paint and garage name go into `user://preferences.cfg`. See `SECURE_LOGIN.md` for platform setup, exact protection and device-test limitations.

The adapter serializes operations. Cached reads are limited to one per 15 seconds, wake requests to one per two minutes, remote commands to one per 30 seconds, and location to one per hour. A tracked pending command blocks another command for up to two minutes. The UI limits command-status checks to one per five seconds. HTTP 429 introduces server-directed or five-minute backoff. These are conservative app policies, not documented Hyundai quotas.

HTTP 200/empty-body commands are treated as accepted. When there is no transaction ID, use vehicle status to verify. On a timeout or network interruption, a command may have reached the vehicle; the app reports an unknown outcome and never automatically resubmits it. A status refresh can still be stale: inspect the reported timestamp or use an explicit wake request when appropriate.

Opening Maps sends the requested coordinates to the map provider. It happens only after pressing the Maps button. No map SDK, background polling, telemetry, server or cloud credential storage is included.

## Source layout

- `Core/HyundaiUsClient.cs`: injectable .NET HTTP transport, session management, enrollment/status mapping, commands, tracking and location.
- `Core/AccountVault.cs`, `UI/SecureLogin.cs`, `UI/NativeVaultBridge.cs`: biometric saved-login coordination and platform interop.
- `Native/iOS`, `Native/Android`, `addons/ngarage_vault`: native secure storage, build scripts and Android export support.
- `Core/Vehicle.cs`: shared live vehicle command types.
- `UI/Main.cs`: account-first navigation, shared styling and appearance settings.
- `UI/Live.cs`: account connection, live status, controls and confirmation screens.
- `UI/LiveSmoke.cs`: explicitly invoked offline UI smoke fixture, compiled only in debug builds; never used in ordinary sign-in.
- `Assets/Paint.gdshader`: body recoloring tuned to this sprite. Existing saved Cyber Gray preferences migrate to Ecotonic Gray.
- `Tests/`: dependency-free console behavior and protocol checks using fake HTTP responses.
- `Preview/`: captured screens. Connected-flow screenshots have a **TEST** badge and synthetic data; they are not proof of a real vehicle connection.

Protocol attribution and the reference revision are in `THIRD_PARTY_NOTICES.md`. Fonts include their license; the vehicle illustration is generated pixel artwork. Paint names/colors are illustrative, not factory color matching.

## Verification

Current Windows checkout validation (October 22, 2026, US Eastern):

- **134 offline checks passed: 116 protocol and 18 secure-login coordinator.** The 16 checks for the removed simulated client were deleted with that client. Protocol regressions cover both sign-in stages, HTTP and JSON 502, sanitized transport/timeouts and malicious error-code strings, manual recovery, explicit challenges, fiveN request headers/path punctuation, escaped path delimiters, preserved state after failed refresh, and unknown command outcomes without retries.
- Standard .NET 8.0.100/MSBuild Debug and ExportRelease builds of `IoniqN.csproj` against Godot 4.7.2 succeeded with zero warnings/errors, including the C# export plugin. ExportRelease metadata was checked to ensure the synthetic HTTP/native-vault fixtures and smoke methods are absent. The default SDK attempted unavailable NuGet resolution; validation used the installed .NET 8 SDK and cached dependencies. The project target/framework was not changed.
- Android vault Java compiled with JDK 17 against Android 35 and official Godot `4.7.0.stable`; the bundled AAR was rebuilt with those classes while retaining its manifest/resources. A full Gradle app export/install was not performed.
- Rendered smoke was attempted with Godot `4.7.2.stable.mono.official.ed1daf0bf`, but stalled at `.NET: Initializing module...` before the app loaded and was stopped. The sandbox also denied shader-cache writes to `user://`. The new headless account-flow smoke attempt also did not finish engine startup and was stopped. Current UI runtime/rendering is **unverified**, including account-only navigation and authentication/enrollment 502 recovery. Prior rendered results are historical only.
- No real account credentials, real vehicle commands, mobile exports, iOS native builds or physical-device biometric tests were used in this validation.

On a normal development machine:

```sh
dotnet build IoniqN.csproj
dotnet run --project Tests/CoreTests.csproj
```

After a Debug build, use your Godot .NET executable (smoke fixtures are excluded from ExportRelease):

```sh
godot --path . -- --live-smoke-test
godot --path . -- --secure-smoke-test
```

The live and security tests use fake HTTP handlers, a fake native vault and a reserved `.invalid` address. None of the smoke tests makes real Hyundai requests. With a display, they save screenshots to Godot's user-data folder. With `--headless`, they check behavior without screenshots or visual verification. They exit with code 0 on success and leave appearance preferences untouched. Startup, pre-selection navigation, background locking and sign-out are checked to ensure vehicle screens stay inaccessible without a selected vehicle. To save screenshots elsewhere, create the destination first and append `--smoke-output=C:/absolute/path` after `--`. Screenshot write failures now fail the smoke test instead of silently passing.

## Mobile work remaining

This is source, not an APK/IPA. The portrait canvas is 460×920 with aspect-preserving scaling. Safe areas, soft keyboards, accessibility and device lifecycle behavior need physical-phone tests. The C# project conditionally targets `net9.0` for Android; install the corresponding SDK/workloads, Godot export templates and platform toolchains. iOS requires Mac/Xcode and signing. Secure saved login is implemented with native bridges; follow `SECURE_LOGIN.md` to include them and perform device tests. The iOS native bridge still requires a Mac/Xcode build. Android requires API 30+ and the provided vault addon enabled.
