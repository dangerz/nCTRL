# nCTRL saved login

nCTRL saves the login on the phone, in the Keychain, and (like the Hyundai app) uses it without Face ID: the app reads status when it opens, and requests, commands and Send to car need no prompt. Face ID or the passcode is still asked to accept the risk disclaimer and to go past nCTRL's request limits. The Godot UI and app logic remain C#. Small native OS bridges access Apple Keychain/LocalAuthentication and Android Keystore/BiometricPrompt. There is no backend.

## What the user does

1. Open nCTRL; it starts on the sign-in screen, or opens the car with the saved login.
2. Enter email, password and the four-digit Bluelink service PIN. On a supported phone, **Save account on this phone** is selected by default and can be turned off.
3. After Hyundai accepts the account, it is saved with no prompt. If saving fails, the current manual sign-in continues for that session and the UI says it was not saved.
4. On later launches the app opens the Car screen with the last saved status, then reads Hyundai's cached status once (no car wake), signing in with the saved login. With no saved status yet, the saved-login screen signs in by itself (or tap **Open my car**). The read is skipped quietly inside the 15-second read limit; an automatic read never offers to go past a limit.
5. Sending the app to the background ends the foreground session, cancels pending local work and hides the live dashboard. Returning shows the saved status and reads once more; an idle session with a valid token, kept in memory while backgrounded, is reused for the same account without a new Hyundai sign-in. A command already sent to Hyundai cannot be recalled by leaving the app.
6. **Sign out & forget** clears the active session and deletes the saved account and saved status. The saved-login screen (shown when there is no saved status yet) also has **Forget saved account**.

If you manually sign in with Save account unchecked, an existing saved login is removed after successful Hyundai authentication. Disabling saved login does not write a plaintext substitute. Anyone holding your unlocked phone can use nCTRL, as with the Hyundai app. The current US adapter's MFA limitation remains unchanged.

## iPhone / iPad implementation

`Native/iOS/NGarageVault.mm` keeps the login in a generic-password Keychain item (service `com.ngarage.nctrl.account.v2`) with:

- `kSecAttrAccessibleWhenUnlockedThisDeviceOnly`: encrypted by iOS with the device's keys, readable only while the phone is unlocked, device-bound and never in backups to another device.
- `kSecAttrSynchronizable = false`: no iCloud Keychain synchronization.
- No biometric access control, so reading it never prompts.
- In the App Group's Keychain access group (`group.<app bundle ID>`) when the build has the App Group, so nCTRL's Share extension can read it to send a shared place to the car. A login saved before the App Group existed moves into it on the next unlock, with no prompt.

Hyundai's session (access and refresh tokens, when the access token expires, and the selected car's ID, VIN, name, model and generation) is kept in another item in the same group (`com.ngarage.nctrl.session.v1`, `kSecAttrAccessibleWhenUnlockedThisDeviceOnly`, not synchronized). The app updates it after each request; the Share extension uses it to send without signing in again and saves it back if it renewed the token. Sign out & forget deletes it, and it is cleared on a fresh install.

Older builds kept the login behind Face ID (`kSecAccessControlBiometryCurrentSet`, service `com.ngarage.bluelink.credentials.v1`). The first unlock after updating asks for Face ID once to read that item, moves the login to the new item and deletes the old one. Cancelling leaves it in place; signing in again replaces it. Sign out & forget deletes both.

The same bridge also performs a device-owner check, the `authenticate` operation, used to record risk-disclaimer acceptance and to confirm going past a request limit. It evaluates `LAPolicyDeviceOwnerAuthentication` (Face ID/Touch ID with the system passcode fallback) in a fresh `LAContext` and reads or writes no Keychain data. Android does not implement this operation yet, so Android builds stay locked at the disclaimer.

The car's last known location uses another generic-password item, `location_load`/`location_save`/`location_forget`, stored the same way as ignored places (`kSecAttrAccessibleWhenUnlockedThisDeviceOnly`, not synchronized, no biometric access control, cleared on a fresh install, deleted by Sign out & forget). It holds one location: coordinates, Hyundai's report time, when nCTRL received it, and the account-scoped vehicle fingerprint, never the VIN or email. The app shows it marked old, including from saved status. Android does not implement these operations yet.

Surround View pictures are held in app memory only and dropped when the foreground session ends. **Save to Photos** uses `PHPhotoLibrary` with `PHAccessLevelAddOnly`, so nCTRL can add a picture but never read the library; iOS asks once, and a denial is reported with where to change it. The native bridge links the Photos framework (`ng_photo_save`/`ng_photo_status`).

Ignored places (car locations where an unlocked-doors alert is hidden) use a separate generic-password item, `places_load`/`places_save`/`places_forget`, with `kSecAttrAccessibleWhenUnlockedThisDeviceOnly` and no biometric access control so alerts can be checked after each update; it is not synchronized, is cleared with the credentials on a fresh install, and is deleted by Sign out & forget. Android does not implement these operations yet.

Saving first authenticates the owner, then creates or updates the protected Keychain item. Unlock uses `SecItemCopyMatching` against the protected item itself, so the authentication is bound to the secret read. A fresh-install marker clears orphaned Keychain credentials that could otherwise outlive an uninstall. A native privacy cover hides the UI while the app is inactive/app-switcher snapshots are taken. It now handles both UIKit app and scene activation notifications, avoids stacking duplicate covers, and includes a managed resume cleanup call. Godot hides the old canvas on pause and rebuilds the locked UI on resume. These address identified lifecycle gaps; the reported blank screen still needs physical-device verification.

### Build on a Mac

The vault is now compiled and statically linked into the C# NativeAOT framework during Godot's iOS export. `IoniqN.csproj` imports `Native/iOS/Vault.targets`, which runs `build-aot.sh` for the selected device/simulator architecture and binds the eight C bridge functions (including foreground cover cleanup and system maps handoff) using DirectPInvoke. A failed native compile/link stops export. This replaces the previous optional `.gdip` integration, which could be omitted from an otherwise successful export or leave `__Internal` imports unresolved at runtime.

1. On your Mac, open this updated project with Godot .NET 4.7 and Xcode selected via `xcode-select`. Build the C# project so the editor addon loads.
2. Keep **nCTRL Secure Vault** enabled under Project Settings → Plugins (now enabled in `project.godot`). It supplies `NSFaceIDUsageDescription` and `NSPhotoLibraryAddUsageDescription` to the exported Info.plist. Android also uses this addon for its AAR.
3. If you previously built/enabled the separate **NGarageVault** under the iOS export preset's Plugins section, disable that legacy export plugin. Do not package both copies. `Native/iOS/build.sh` and the `.gdip` remain only for legacy integrations; they are no longer the nCTRL build path.
4. Export a fresh iOS Xcode project, targeting **iOS 15+**, and build/install the signed app. Keep your existing bundle identifier, signing team and Keychain access group. No Keychain Sharing entitlement is needed for the app-private item. Rebuilding an older exported Xcode project alone will not incorporate the updated C# framework or plist.

The AOT build script supports `ios-arm64`, `iossimulator-arm64` and `iossimulator-x64`. The generated archives live in ignored `Native/iOS/build/aot` directories. No custom Godot engine, Python runtime, server or separately downloaded vault binary is needed.

The iOS source and new build integration **have not been compiled or run on a Mac/device here**. MSBuild evaluation confirms the iOS archive, direct imports and framework dependencies; Windows C# builds do not verify Apple linking. Face ID permission, Keychain enforcement and lifecycle behavior still need the device checklist below. The app now distinguishes missing bridge/permission text from missing passcode, unenrolled biometrics and biometric lockout. Manual login always stays memory-only when secure saving is unavailable.

## Android implementation

`Native/Android/.../NGarageVault.java` is a Godot v2 Android plugin. It requires Android 11 / API 30 or newer and an enrolled **Class 3 / strong biometric**. Devices offering only weak face unlock cannot use that face unlock for this vault; an available strong fingerprint/face authenticator is needed.

Credentials are encrypted with AES-256-GCM using a non-exportable Android Keystore key configured for biometric authentication on every use. Both encryption and decryption use the authenticated `BiometricPrompt.CryptoObject`; an unauthenticated callback cannot decrypt the file. The key is invalidated by changes to biometric enrollment. There is no hard-coded encryption key or PIN fallback.

Only the version, Keystore alias, IV and authenticated ciphertext are written to an `AtomicFile` in `getNoBackupFilesDir()`. Account data is excluded from Android backup by using that directory; no credentials are written to SharedPreferences. Saving rotates the key and commits the ciphertext before removing the old key. Sign-out removes vault keys and ciphertext. `FLAG_SECURE` blocks screenshots and recent-app snapshots on Android. The system may use hardware-backed storage when supported; the app does not claim every device provides the same hardware security.

### Android packaging

A small compiled `addons/ngarage_vault/NGarageVault.aar` is included. Its Java code was compiled against the official Android 35 API jar and the Godot 4.7 Android library, then packaged as an AAR. A full Gradle app export/device install was not performed here.

1. In Godot, enable **nCTRL Secure Vault** under Project Settings → Plugins. The C# editor plugin adds the AAR to Android exports.
2. Install/use Godot's Android Gradle build template, enable Gradle Build in the export preset, and set minimum SDK to **30** or newer. The manifest adds `USE_BIOMETRIC` and Godot's plugin metadata.
3. Complete the usual C# Android export setup, SDK/workloads, signing, and install on a physical device.

To rebuild the AAR, use JDK 17, Gradle 8.9 and Android SDK platform 35:

```sh
cd Native/Android
gradle installPlugin
```

The included Gradle project uses Android Gradle Plugin 8.7.3 and Godot `4.7.0.stable` as a compile-only dependency. The build copies the release AAR into the addon. Neither the Godot engine nor Android API jars are bundled in the small vault AAR.

## Desktop and failure behavior

Desktop builds have no credential-storage backend. They disable the save option and explicitly say that manual sign-in is session-only. There is no fallback to `ConfigFile`, plaintext JSON, environment variables, hard-coded encryption, or an unprotected desktop credential cache.

Cancelling a biometric prompt returns to the locked flow; no Hyundai login is initiated. Locking during an in-flight unlock/login cancels the operation and invalidates its session generation, so a late result cannot reopen the live session. Removing saved data is allowed even if biometrics are no longer available. If deletion fails, the error is shown and deletion can be retried.

While unlocked, credentials exist in process memory because Hyundai's token refresh protocol requires the password. Tokens, vehicle state and exact location are not persisted. Public map tiles are cached in user://map-cache for at least seven days and can reveal the viewed area; they contain no credentials or vehicle marker. Viewing the map shares the area with OpenStreetMap, and opening an external map shares the vehicle coordinates with that app. Managed strings cannot be guaranteed to be cryptographically zeroized; the implementation drops references on lock/sign-out. Native temporary byte buffers are cleared where possible. Errors are sanitized, and no credentials or raw API responses are logged.

## Validation and remaining device checks

Validation repeated in the Windows checkout:

- C# Debug and ExportRelease application/export-plugin builds pass with zero warnings/errors (October 23, 2026).
- 32 secure-login coordinator checks with a fake native bridge, plus 227 protocol and 11 remembered-vehicle/door-control checks (270 total), with JSON reflection explicitly disabled. These cover safe mobile runtime diagnostics, capability failures and existing no-retry/credential-handling behavior.
- The previous Android Java/AAR validation remains unchanged; Android export/device checks have not been performed.
- iOS MSBuild settings were evaluated for NativeAOT linkage. Apple native compilation and a signed export remain unverified; Windows Git Bash could not start in this sandbox, so the new shell script could not be executed here either.
- Debug-only security smoke remains available for save preference, background lock, cancelled biometric unlock, mocked unlock, stale-unlock rejection and account removal. The latest Godot smoke launch failed with missing .NET assemblies and crashed before app test execution; current resume/rendered flows remain unverified. Prior rendered results are not native/device verification; see README for details.

The nCTRL rename and new iOS linkage preserve all secure-storage identifiers and encryption settings, including the iOS Keychain service/account, fresh-install marker and legacy Android AES-GCM associated-data text. No credential migration is required when upgrading with the same app identity/signing. Keep the existing package/bundle identity and signing configuration; changing those may require signing in and securely saving again. No plaintext or cross-identity credential migration is performed. See README for appearance preference compatibility.

The mock tests validate application logic only. They do not demonstrate actual Keychain/Keystore enforcement or real biometrics. Before treating the feature as device-verified, test on signed iOS and Android builds:

- Save, force-close/relaunch, and authenticate; verify account recovery.
- Cancel, fail and lock out biometrics; verify no credentials or live controls are released.
- Background during unlock, Hyundai login and command submission; verify the session stays locked after late responses.
- Change enrolled biometrics or remove the device passcode; verify manual re-enrollment is required.
- Sign out, restart and verify the saved account is gone.
- Test app-switcher/screenshot privacy, backup/restore and reinstall behavior.
- Confirm a real Hyundai account still authenticates after secure retrieval.

Run the mocked security UI test after building:

```sh
godot --path . -- --secure-smoke-test
```

Screenshots `19`–`21` use a fake vault and have a TEST badge. They are not screenshots of Apple's actual Face ID sheet.

## Primary references

- Apple Keychain with biometrics: https://developer.apple.com/documentation/localauthentication/accessing-keychain-items-with-face-id-or-touch-id
- Apple current-enrollment access control: https://developer.apple.com/documentation/security/secaccesscontrolcreateflags/biometrycurrentset
- Android biometric cryptography: https://developer.android.com/identity/sign-in/biometric-auth
- Android Keystore authentication parameters: https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec.Builder
- Godot iOS plugins: https://docs.godotengine.org/en/stable/tutorials/platform/ios/ios_plugin.html
- .NET NativeAOT direct interop/static linking: https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/interop
- Godot Android plugins: https://docs.godotengine.org/en/stable/tutorials/platform/android/android_plugin.html
