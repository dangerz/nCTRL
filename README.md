# nCTRL — Godot 4.7 C# / Hyundai USA

N Performance styling, pixel Ioniq 5 N, six presets including **Ecotonic Gray**, and custom paint (Settings → Appearance → Car color). This revision includes direct Hyundai USA / Bluelink HTTP connectivity, secure phone-side saved login, and biometric unlock. App logic is C# with small native OS storage bridges. No Python runtime or backend.

**Implementation status:** C# app/export plugin and Android vault Java compiled. The bundled Android AAR includes the nCTRL prompts. The iOS vault now builds with the C# NativeAOT export; Apple compilation and physical-device validation are still required. Mobile JSON serialization no longer uses reflection. See `SECURE_LOGIN.md` for mobile setup. The owner reported a live sign-in 502; this revision has not been authenticated against a real Hyundai account or tested on a physical vehicle. The working local fiveN sign-in profile was compared and adopted for token/enrollment requests; live resolution remains unverified. See `SIGN_IN_DIAGNOSIS.md`. Mobile exports have not been tested.

## Run and connect

1. Install **Godot 4.7 .NET** and the .NET 8 SDK for a desktop preview.
2. Import `project.godot`, build, and press **F5**. First build restores the Godot NuGet packages. `IoniqN.sln` retains its internal name for compatibility; the app is nCTRL.
3. The app first shows the **risk disclaimer** until the current account has accepted it (see below), then **Connect your Hyundai**, or biometric unlock if an account is saved.
4. Enter your US Bluelink email, password and four-digit Bluelink service PIN **inside the app**. Do not put credentials into the source code or a chat message.
5. On a supported phone, leave **Save account with Face ID / biometrics** selected and authenticate to store the account securely. Afterwards the app opens straight to the Car screen with the last saved status, with no Face ID or request; the first **Update**, command or location request after each launch or return from the background asks for Face ID. Desktop builds remain session-only.
6. Select your enrolled vehicle once. After later unlocks/sign-ins, nCTRL matches that vehicle against fresh enrollment and loads its cached status automatically. Connecting sends no remote vehicle command.
7. A **LIVE** badge indicates the connected UI. Tapping a command sends it directly, with no confirmation dialog. One door button offers Unlock when reported locked, or Lock when reported unlocked; unknown door state disables it until status is refreshed.

There is no simulated vehicle mode. With a saved phone account and a remembered vehicle, launching or returning to the app opens the Car screen showing the last saved status (marked with when it last updated) without Face ID or any request. The first **Update**, wake, command or location request of each foreground session asks for Face ID, which unlocks the saved login; cancelling sends nothing. The rest of that session needs no further prompts, and backgrounding ends it. If the previous session is idle (a vehicle selected, no request or command check running) and its Hyundai token is valid, it stays in memory while backgrounded and is reused after Face ID without a new sign-in; otherwise nCTRL signs in for you, which never shows the sign-in cooldown warning. Without saved status (first use, or after Sign out & forget) the saved-login screen offers **Unlock with Face ID**. **Sign out & forget** deletes the saved account, saved status and selected-vehicle preference. There is no separate Lock app button. Vehicle navigation appears only after an enrolled vehicle is selected or restored. After unlocking, the previous vehicle opens automatically if still enrolled. An expired or denied session can be re-entered by unlocking or signing in manually. Failed refreshes preserve the last known vehicle state.

## Risk disclaimer

Nothing in nCTRL works until the risk disclaimer is accepted. It states that nCTRL is a third-party, completely unsupported app; that it could damage or disable the vehicle; that Hyundai may ban the account or change its API; and that the user accepts every risk and holds no one else liable. To accept, the user types **I Agree** (case and extra spaces ignored), checks the box and taps **Submit**. On phones, Submit then requires Face ID/Touch ID or the device passcode; cancelling records nothing. Desktop builds are development-only and skip the device check.

Acceptance is per Hyundai account. It is stored in `user://preferences.cfg` as the disclaimer version, acceptance time and the same SHA-256 account hash used for the remembered vehicle (never the email). An acceptance made before sign-in is bound to the first account that signs in successfully. A different account is stopped before any Hyundai request and must accept again; its credentials are held in memory only until it accepts or cancels. **Sign out & forget** clears the acceptance, and a new disclaimer version (`Disclaimer.Version` in `Core/Disclaimer.cs`) requires everyone to accept again. **Backgrounding keeps it. Settings → **View disclaimer** shows the text and acceptance date.

The disclaimer text is not legal advice and has not been reviewed by a lawyer.

## Implemented live functions

- US account sign-in and in-session token refresh, using Hyundai's published-by-community JSON protocol.
- Active vehicle enrollment selection and cached status reads.
- Battery, range, door lock, climate, cable, charging, and separate AC/DC limits where reported.
- Explicit **Wake vehicle and refresh**, separate from cached refresh.
- Lock and unlock; EV climate start/update and stop, with front seat heat/ventilation (Off, Low, Medium, High) and steering-wheel heat; EV charging start and stop; AC/DC charge targets.
- Command acceptance and transaction-status checks. **Accepted is not confirmed**; successful completion does not fabricate a new vehicle status. Refresh the reported state afterward.
- Vehicle Location automatically retrieves the reported position and displays a tappable map, with Update Location for a new request (existing hourly wake limit applies). Locked doors and active climate use the red N accent.
- Car art on the Car screen, from the last reported state: a pulsing red padlock with a glint when locked, or an open-padlock outline when unlocked; tapping it toggles the lock like the door button. With climate on, the windshield is tinted red at a setpoint of 72°F or above and blue below (Hyundai reports the setpoint, not whether it is heating or cooling). Front seats are tinted red when reported heating and blue when reported ventilating, and the steering wheel is outlined red when reported heated. Off, unknown or unreported states show nothing. Only the padlock animates, and only while the Car screen is visible.
- Optional device-bound saved credentials and biometric unlock, masked password/PIN inputs, background locking, sanitized errors, and no automatic command retries.

Missing status fields display **—** or **Unknown**. Server timestamps are shown as supplied; compact timestamps are not assigned an invented timezone. The dashboard is the last reported state, not continuous telemetry.

The shared target endpoint sends both AC and DC targets. Tap **Set AC and DC targets** to send both displayed selections. EV climate requests keep defrost and rear-window heat off. Seat and steering-wheel choices are remembered and sent with each climate start or update in the same single request. The steering wheel uses `heating1 = 3` ("steering wheel only" in the upstream value list) and is sent for any electric vehicle. Front-seat codes (heat 6–8, cool 3–5) go only to generation 3 vehicles, as upstream does; on other generations the seat controls are disabled and a seat request is refused before anything is sent. Rear seats always stay off. Generation 3 requests a five-minute duration. The Climate screen shows reported seat and steering-wheel state when Hyundai includes it. The exact heating values have not yet been confirmed against Hyundai's US servers on a real vehicle. The adapter does not start an ICE engine.

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
- **Plugins:** leave any legacy NGarageVault `.gdip` disabled. The enabled **nCTRL Secure Vault** editor addon adds `NSFaceIDUsageDescription` to Info.plist; without it the app cannot use Face ID and stays locked at the disclaimer. Godot's iOS template already sets `ITSAppUsesNonExemptEncryption = false` (the app uses only standard HTTPS); the addon must not repeat template keys. After exporting, confirm `NSFaceIDUsageDescription` is present in the generated `nCTRL/nCTRL-Info.plist`.
- Keep signing credentials in Godot's `.godot/export_credentials.cfg`, which is git-ignored, never in tracked files.

## Storage, request limits and errors

With secure saving enabled on a supported phone, email/password/PIN are protected by Apple Keychain or Android Keystore-backed encryption and biometric authentication. Tokens, enrollment, exact location and history remain memory-only. The last vehicle status (battery, range, doors, climate, charging, targets, seats, reported and updated times), vehicle name, last six VIN characters and generation are saved in `user://vehicle-snapshot.json` so the app can open to it; it holds no location, email, full VIN or credentials, identifies the account and vehicle only by SHA-256 hashes, and is shown without Face ID. Sign out & forget deletes it. A session kept while backgrounded, and the saved login unlocked by Face ID for the current foreground session, stay in app memory only. Public map tiles are cached under `user://map-cache` for at least seven days; they can reveal the viewed area, but contain no vehicle marker, account data or credentials. Locking drops session references; this is not guaranteed zeroization of managed strings. Paint, garage name and an account-scoped SHA-256 vehicle fingerprint go into `user://preferences.cfg`. The fingerprint is only a selection preference, contains no raw email/VIN/credentials, and is checked against current authenticated enrollment. Sign out & forget clears it. See `SECURE_LOGIN.md` for platform setup, exact protection and device-test limitations.

The adapter serializes operations. Hyundai publishes no US request quotas, so nCTRL enforces its own conservative limits (`Core/RequestBudget.cs`):

| Request | Cooldown | Daily cap |
|---|---|---|
| Sign-in (token request) | 60 seconds | 20 |
| Cached status read | 15 seconds | 150 |
| Wake vehicle and refresh | 10 minutes | 10 |
| Remote command | 30 seconds | 30 |
| Location | 1 hour | 24 |

The limits are shared by every account and client on the device and saved in `user://request-limits.json` (counts and timestamps only, no account data). Locking, relaunching, signing in again and Sign out & forget do not reset them. Daily caps reset at local midnight. The vehicle read when a session starts counts toward the daily cap and starts the read cooldown but is never refused by it. Command tracking checks count toward the read cap without a cooldown. Automatic token renewals (about once a day) are not budgeted. Settings → **Request limits** shows what is left.

When a limit refuses a request you tapped, a warning explains it and offers **Cancel** (nothing is sent) or **Send anyway**. On phones, Send anyway requires Face ID/Touch ID or the device passcode. An override covers that single request, still counts, and is recorded in Activity. Every later refusal warns again. Automatic requests never prompt; they skip whatever the limits refuse.

When Hyundai answers HTTP 429, every request pauses for the server's Retry-After (or five minutes, capped at 24 hours), with no override. That request type's limits then tighten for 24 hours: the cooldown doubles and the daily cap halves, stacking up to ×8 on repeated 429s, and return to normal after 24 hours without one.

After submission, the UI automatically makes up to eight follow-up checks, 15 seconds apart (network time is additional), and disables further commands during those checks. Backgrounding/sign-out cancels the monitor. Successful early confirmation retains the command cooldown.

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

Current validation (October 23, 2026, macOS with Godot 4.7.2 .NET, .NET 8 and Xcode 27.1):

- **582 offline checks passed: 428 protocol, 36 secure-login coordinator, 11 remembered-vehicle/door-control, 25 risk-disclaimer, 9 car-art indicator, 7 vehicle-snapshot, 42 request-limit and 24 map checks**, with JSON reflection disabled to match mobile restrictions. The unchanged source passed 134 checks in desktop mode but failed authentication when reflection was disabled; explicit JSON writing fixes that reproducible defect. The 16 checks for the removed simulated client were deleted with that client. Protocol regressions cover both sign-in stages, HTTP and JSON 502, sanitized transport/timeouts and malicious error-code strings, manual recovery, explicit challenges, fiveN request headers/path punctuation, escaped path delimiters, preserved state after failed refresh, and unknown command outcomes without retries.
- Standard .NET 8.0.100/MSBuild Debug and ExportRelease builds of `IoniqN.csproj` against Godot 4.7.2 succeeded with zero warnings/errors, including the C# mobile export plugin. iOS MSBuild evaluation confirms the vault archive, all eight direct imports (including maps handoff) and Apple frameworks; actual Apple compilation/linking remains unverified. In previous validation, ExportRelease metadata was checked to ensure the synthetic HTTP/native-vault fixtures and smoke methods are absent. The default SDK attempted unavailable NuGet resolution; validation used the installed .NET 8 SDK and cached dependencies. The project target/framework was not changed.
- Android vault Java compiled with JDK 17 against Android 35 and official Godot `4.7.0.stable`; the bundled AAR was rebuilt with those classes while retaining its manifest/resources. A full Gradle app export/install was not performed.
- The iOS vault source compiles for `ios-arm64` and `iossimulator-arm64` with Xcode 27.1 (two pre-existing Keychain API deprecation warnings).
- Both Godot smoke tests pass on macOS, headless and with a window. The secure smoke covers the disclaimer gate with a fake device check (cancel, then accept), binding on first sign-in, holding a different manual or saved account before any request, View disclaimer, save, background lock, biometric cancellation, unlock, opening to the saved status without Face ID or requests, Face ID on the first request (cancel sends nothing) and not again until backgrounded, reuse of a parked session without a new sign-in, a limit override cancelled at the device check, and sign-out reset that discards the parked session. The live smoke covers the disclaimer, sign-in recovery, selection, the read-cooldown warning (cancel sends nothing; one override sends exactly one request), the Request limits screen, car-art state mapping with captures of the heat and cool tints, a usable padlock tap target, the overlay absent off the Car screen, screens, direct door control, tracking, remembered vehicle, and seat/steering-wheel choices carried on the climate start. Earlier Windows attempts stalled at .NET initialization. Native scene/cover behavior and real Face ID/passcode prompts still require an iPhone build and repeated background/resume tests.
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
