# nCTRL — Godot 4.7 C# / Hyundai USA

N Performance styling, pixel Ioniq 5 N, six presets including **Ecotonic Gray**, and custom paint. This revision includes direct Hyundai USA / Bluelink HTTP connectivity, secure phone-side saved login, and biometric unlock. App logic is C# with small native OS storage bridges. No Python runtime or backend.

**Implementation status:** C# app/export plugin and Android vault Java compiled. The bundled Android AAR includes the nCTRL prompts. The iOS vault now builds with the C# NativeAOT export; Apple compilation and physical-device validation are still required. Mobile JSON serialization no longer uses reflection. See `SECURE_LOGIN.md` for mobile setup. The owner reported a live sign-in 502; this revision has not been authenticated against a real Hyundai account or tested on a physical vehicle. The working local fiveN sign-in profile was compared and adopted for token/enrollment requests; live resolution remains unverified. See `SIGN_IN_DIAGNOSIS.md`. Mobile exports have not been tested.

## Run and connect

1. Install **Godot 4.7 .NET** and the .NET 8 SDK for a desktop preview.
2. Import `project.godot`, build, and press **F5**. First build restores the Godot NuGet packages. `IoniqN.sln` retains its internal name for compatibility; the app is nCTRL.
3. The app opens directly to **Connect your Hyundai**, or biometric unlock if an account is saved.
4. Enter your US Bluelink email, password and four-digit Bluelink service PIN **inside the app**. Do not put credentials into the source code or a chat message.
5. On a supported phone, leave **Save account with Face ID / biometrics** selected and authenticate to store the account securely. Subsequent launches show biometric unlock. Desktop builds remain session-only.
6. Select your enrolled vehicle once. After later unlocks/sign-ins, nCTRL matches that vehicle against fresh enrollment and loads its cached status automatically. Connecting sends no remote vehicle command.
7. A **LIVE** badge indicates the connected UI. Tapping a command sends it directly, with no confirmation dialog. One door button offers Unlock when reported locked, or Lock when reported unlocked; unknown door state disables it until status is refreshed.

There is no simulated vehicle mode. Startup, locking, backgrounding and sign-out all return to the account screen. Vehicle navigation appears only after an enrolled vehicle is selected or restored and its status loads. A saved phone account opens the locked login screen. **Lock app** clears the active session but retains protected credentials; **Sign out & forget** deletes the saved account and selected-vehicle preference. Backgrounding ends the live session and hides the old canvas; foreground resume rebuilds the locked account UI. After unlocking, the previous vehicle opens automatically if still enrolled. An expired or denied session can be re-entered by unlocking or signing in manually. Failed refreshes preserve the last known vehicle state.

## Implemented live functions

- US account sign-in and in-session token refresh, using Hyundai's published-by-community JSON protocol.
- Active vehicle enrollment selection and cached status reads.
- Battery, range, door lock, climate, cable, charging, and separate AC/DC limits where reported.
- Explicit **Wake vehicle and refresh**, separate from cached refresh.
- Lock and unlock; EV climate start/update and stop; EV charging start and stop; AC/DC charge targets.
- Command acceptance and transaction-status checks. **Accepted is not confirmed**; successful completion does not fabricate a new vehicle status. Refresh the reported state afterward.
- Vehicle Location automatically retrieves the reported position and displays a tappable map, with Update Location for a new request (existing hourly wake limit applies). Locked doors and active climate use the red N accent.
- Optional device-bound saved credentials and biometric unlock, masked password/PIN inputs, background locking, sanitized errors, and no automatic command retries.

Missing status fields display **—** or **Unknown**. Server timestamps are shown as supplied; compact timestamps are not assigned an invented timezone. The dashboard is the last reported state, not continuous telemetry.

The shared target endpoint sends both AC and DC targets. Tap **Set AC and DC targets** to send both displayed selections. EV climate requests turn off defrost and supplemental heating; generation 3 includes five minutes and seats off. The adapter does not start an ICE engine.

## Account compatibility and limitations

This is an **unofficial US Hyundai-only adapter**, adapted from the current Hyundai-Kia-Connect implementation. It is not an official SDK, and does not support Kia, Canadian, European or other regional accounts.

The source US adapter currently implements username/password token login, not an MFA/OTP challenge exchange. If Hyundai requires MFA, CAPTCHA, a safety acknowledgement or another challenge, this build stops with a sanitized error. It does not bypass checks or automatically retry sign-in. Completing a challenge in the official app is not guaranteed to make this adapter work.

The application uses standard HTTPS validation and does not weaken TLS settings. Hyundai's service can deny an unofficial client or change its protocol. Authentication and commands require a working account and the appropriate enabled services; server acceptance remains authoritative. No claim is made for phone keys, cameras, remote performance settings or full Tesla-app parity.

## Sign-in diagnostics

Sign-in failures identify **Authentication** (token request) or **Vehicle list** (enrollment request). HTTP status and JSON API error codes are separate: `HTTP 502` and `HTTP 200; API code 502` are different observations. Neither alone establishes a bad password, outage or endpoint change. Only sanitized stage/status/code text is displayed; raw responses and account URLs are never included. To diagnose a failure, share only that in-app error text, never credentials or network dumps.

## Rename and saved-data compatibility

The display name, visible branding, native biometric prompts and editor plugin label are **nCTRL**. `NGarage.Core`, `IoniqN.csproj`/assembly/solution, `NGarageVault`, native entry points/package names, iOS Keychain service/install marker, Android key prefix/ciphertext filename/AES-GCM associated data remain unchanged. In particular, the literal `N Garage / Bluelink credentials / v1` is cryptographic associated data, not visible branding; renaming it would break existing ciphertext authentication.

Desktop `user://` explicitly stays at `Godot/app_userdata/N Garage` under the OS app-data directory, preserving this checkout's paint and garage preferences. Godot's custom-directory setting only affects desktop; mobile storage remains within the existing app sandbox ([Godot data-path documentation](https://docs.godotengine.org/en/stable/tutorials/io/data_paths.html)). Ecotonic Gray and the existing Cyber Gray preference migration are retained. An older build that stored preferences under `IoniqN` needs a one-time copy of **only** `preferences.cfg` into the retained N Garage directory while the app is closed; no credential files should be copied or converted.

`export_presets.cfg` is intentionally not committed: this is a public repository and presets contain the Apple team ID and bundle ID. Create your own preset in **Project → Export**. New export display names default to the project name, nCTRL. In existing external presets, set Android **Package → Name** and iOS **Application → Name** to nCTRL (or leave blank to inherit). Keep the previously shipped Android package ID/signing key and iOS bundle ID/team/Keychain access group to retain access to saved credentials. If an identity must change, users must sign in and save credentials again; this revision does not migrate secrets between app identities. Freshly export iOS to include its NativeAOT-linked vault and Face ID permission text; see SECURE_LOGIN.md.

### iOS preset for TestFlight

The release flow is: export from Godot, open the generated Xcode project, then **Product → Archive** and upload to TestFlight. Recommended iOS preset settings:

- **Export Path:** `exports/ios/nCTRL.ipa`. `exports/` is git-ignored except its `.gdignore`, which stops Godot importing the generated project's images.
- **Application → Export Method Release:** App Store. **Code Sign Identity (Debug and Release):** Apple Development, with provisioning profiles left blank so Xcode signs automatically; Xcode re-signs for distribution when uploading.
- **Application → Targeted Device Family:** iPhone. The UI is a fixed portrait phone layout.
- **Application → Short Version:** blank, inheriting `application/config/version` from `project.godot`. **Application → Version** (the build number) must increase for every TestFlight upload of the same version.
- **Privacy → User Defaults Access Reasons:** CA92.1 (“Access info from same app”). The iOS vault stores its install marker in `NSUserDefaults`.
- **Plugins:** leave any legacy NGarageVault `.gdip` disabled. The enabled **nCTRL Secure Vault** editor addon adds `NSFaceIDUsageDescription` and `ITSAppUsesNonExemptEncryption = false` (the app uses only standard HTTPS) to Info.plist.
- Keep signing credentials in Godot's `.godot/export_credentials.cfg`, which is git-ignored, never in tracked files.

## Storage, request limits and errors

With secure saving enabled on a supported phone, email/password/PIN are protected by Apple Keychain or Android Keystore-backed encryption and biometric authentication. Tokens, enrollment, vehicle state, exact location and history remain memory-only. Public map tiles are cached under `user://map-cache` for at least seven days; they can reveal the viewed area, but contain no vehicle marker, account data or credentials. Locking drops session references; this is not guaranteed zeroization of managed strings. Paint, garage name and an account-scoped SHA-256 vehicle fingerprint go into `user://preferences.cfg`. The fingerprint is only a selection preference, contains no raw email/VIN/credentials, and is checked against current authenticated enrollment. Sign out & forget clears it. See `SECURE_LOGIN.md` for platform setup, exact protection and device-test limitations.

The adapter serializes operations. Cached reads are limited to one per 15 seconds, wake requests to one per two minutes, remote commands to one per 30 seconds, and location to one per hour. After submission, the UI automatically makes up to eight follow-up checks, 15 seconds apart (network time is additional), and disables further commands during those checks. Backgrounding/sign-out cancels the monitor. Successful early confirmation retains the 30-second command cooldown. HTTP 429 introduces server-directed or five-minute backoff. These are conservative app policies, not documented Hyundai quotas.

HTTP 200/empty-body commands are treated as accepted. The receipt displays HTTP acceptance; automatic GETs then check transaction status and cached vehicle state. **Check last command has been removed.** A control shows … while checking, ✓ after confirmed completion or a matching changed vehicle report, ! for reported failure, or ? if unverified. A tracking SUCCESS is completion evidence, but button colors still follow reported vehicle state, never an optimistic local toggle. Without tracking, a changed report timestamp and a transition to the requested state are required; unchanged/already-matching reports do not establish completion. Failed reads preserve last known state. Automatic checks never wake the vehicle or repeat a command POST. Rate limiting/access denial ends monitoring; unresolved outcomes remain explicitly unverified.

Climate On sends exactly one POST to /ac/v2/evc/fatc/start, with no charging request or charging fields. Charging POSTs exist only behind the explicit Start/Stop Charging controls. The offline regression suite checks this request count and payload isolation. It cannot determine why the owner's vehicle began charging or establish what an older phone build sent.

The map uses only visible HTTPS OpenStreetMap tiles, a separate account-free HTTP client, `nCTRL` identification, visible attribution, seven-day caching and conditional revalidation, following [OSM tile policy](https://operations.osmfoundation.org/policies/tiles/). Viewing shares the map area with OSM; tapping the map shares the exact vehicle point with the external maps app. Android uses a package-free geo: intent; iOS tries the system geo-navigation:///place?coordinate= URL and falls back to Apple Maps if dispatch fails. [Apple documents default navigation](https://developer.apple.com/documentation/MapKit/preparing-your-app-to-be-the-default-navigation-app) as region/OS dependent. Returning from Maps still locks the session as required. Reopening location within the hourly cooldown reuses the last point; after cooldown it requests a new point. Failed location/map requests preserve the previous point/map when available. No device GPS permission or backend is needed.

The owner reported “Request accepted; no tracking ID. Refresh status to verify” after a command. That code path requires a successful HTTP response without a detected API error; it establishes submission and a Hyundai server response, not physical vehicle action. The original routes/payloads followed the upstream US implementation. A subsequent direct comparison with fiveN found its extra door-body PIN and minimal command headers; nCTRL now matches those, and applies the common header profile to every command. See [FIVEN_COMMAND_COMPARISON.md](FIVEN_COMMAND_COMPARISON.md) for measured request equivalence and intentionally retained differences. No evidence currently establishes why the vehicle did not respond. Without a tracking ID, automatic checks use reported vehicle status; cached reads can lag. No command replay, endpoint changes or fabricated completion were introduced.

Location is requested when its screen opens or Update Location is tapped. Command follow-up runs only while the session is unlocked. There is no background polling, telemetry, backend or cloud credential storage.

## Source layout

- `Core/HyundaiUsClient.cs`: injectable .NET HTTP transport, session management, enrollment/status mapping, commands, tracking and location.
- `Core/AccountVault.cs`, `UI/SecureLogin.cs`, `UI/NativeVaultBridge.cs`: biometric saved-login coordination and platform interop.
- `Native/iOS`, `Native/Android`, `addons/ngarage_vault`: native secure storage, build scripts and mobile export support.
- `Core/Vehicle.cs`: shared live vehicle command types.
- `UI/Main.cs`: account-first navigation, shared styling and appearance settings.
- `UI/Live.cs`: account connection, live status, direct controls and command receipts.
- `UI/LiveSmoke.cs`: explicitly invoked offline UI smoke fixture, compiled only in debug builds; never used in ordinary sign-in.
- `Assets/Paint.gdshader`: body recoloring tuned to this sprite. Existing saved Cyber Gray preferences migrate to Ecotonic Gray.
- `Tests/`: dependency-free console behavior and protocol checks using fake HTTP responses.
- `Preview/`: captured screens. Connected-flow screenshots have a **TEST** badge and synthetic data; they are not proof of a real vehicle connection.

Protocol attribution and the reference revision are in `THIRD_PARTY_NOTICES.md`. Fonts include their license; the vehicle illustration is generated pixel artwork. Paint names/colors are illustrative, not factory color matching.

## Verification

Current Windows checkout validation (October 23, 2026, US Eastern):

- **467 offline checks passed: 400 protocol, 32 secure-login coordinator, 11 remembered-vehicle/door-control and 24 map checks**, with JSON reflection disabled to match mobile restrictions. The unchanged source passed 134 checks in desktop mode but failed authentication when reflection was disabled; explicit JSON writing fixes that reproducible defect. The 16 checks for the removed simulated client were deleted with that client. Protocol regressions cover both sign-in stages, HTTP and JSON 502, sanitized transport/timeouts and malicious error-code strings, manual recovery, explicit challenges, fiveN request headers/path punctuation, escaped path delimiters, preserved state after failed refresh, and unknown command outcomes without retries.
- Standard .NET 8.0.100/MSBuild Debug and ExportRelease builds of `IoniqN.csproj` against Godot 4.7.2 succeeded with zero warnings/errors, including the C# mobile export plugin. iOS MSBuild evaluation confirms the vault archive, all eight direct imports (including maps handoff) and Apple frameworks; actual Apple compilation/linking remains unverified. In previous validation, ExportRelease metadata was checked to ensure the synthetic HTTP/native-vault fixtures and smoke methods are absent. The default SDK attempted unavailable NuGet resolution; validation used the installed .NET 8 SDK and cached dependencies. The project target/framework was not changed.
- Android vault Java compiled with JDK 17 against Android 35 and official Godot `4.7.0.stable`; the bundled AAR was rebuilt with those classes while retaining its manifest/resources. A full Gradle app export/install was not performed.
- The latest offline live-flow smoke stalled at .NET initialization for 20 seconds and could not open its user-data log; only that test process was stopped. Previously, the headless secure-flow smoke was attempted with Godot `4.7.2.stable.mono.official.ed1daf0bf`. The engine reported `.NET: Assemblies not found` and crashed before executing app tests; it also could not open its user-data log. Earlier attempts stalled at .NET initialization. Current UI runtime/rendering is **unverified**, including command polling/button feedback, map rendering/handoff, the combined door button, direct commands, remembered selection, and resume. The updated debug smoke fixtures cover these paths but are not counted as passed checks. Native scene/cover behavior still requires an iPhone build and repeated background/resume tests.
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

This is source, not an APK/IPA. The portrait canvas is 460×920 with aspect-preserving scaling. Safe areas, soft keyboards, accessibility and device lifecycle behavior need physical-phone tests. The C# project conditionally targets `net9.0` for Android; install the corresponding SDK/workloads, Godot export templates and platform toolchains. iOS requires Mac/Xcode and signing. Secure saved login is implemented with native bridges; follow `SECURE_LOGIN.md` to include them and perform device tests. The iOS native bridge now builds and links during C# export on a Mac/Xcode; the enabled editor addon supplies Face ID permission text. Disable any previously enabled legacy NGarageVault .gdip export plugin before re-exporting. Android requires API 30+ and the provided vault addon enabled.
