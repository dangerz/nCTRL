# nCTRL saved login and biometric unlock

This revision adds secure phone-side credential persistence, biometric unlock, background locking, and account removal. The Godot UI and app logic remain C#. Small native OS bridges access Apple Keychain/LocalAuthentication and Android Keystore/BiometricPrompt. There is no backend.

## What the user does

1. Open Settings → Connect Hyundai / Bluelink (USA).
2. Enter email, password and the four-digit Bluelink service PIN. On a supported phone, **Save account with Face ID / Touch ID / biometrics** is selected by default and can be turned off.
3. After Hyundai accepts the account, complete the OS biometric prompt to save it. If saving fails or is cancelled, the current manual sign-in can continue for that session; the UI reports that it was not saved. An existing saved login is unchanged by a failed replacement.
4. On subsequent launches, tap **Unlock with Face ID** (or the available biometric). The native vault releases the credentials only after authentication, then the app logs into Hyundai and shows vehicle selection.
5. Sending the app to the background ends the live session, cancels pending local work, clears account/vehicle references and hides the live dashboard. Unlock or manually sign in again to continue. A command already sent to Hyundai cannot be recalled by locking the app.
6. **Lock app** keeps the saved vault account. **Sign out & forget** clears the active session and deletes the saved account. The locked login screen also has **Forget saved account**.

Manual sign-in remains available when biometrics are unavailable. It never decrypts the saved account as a fallback. If you manually sign in with Save account unchecked, an existing saved login is removed after successful Hyundai authentication. Disabling saved login does not write a plaintext substitute.

Face ID is local authorization to access the saved credentials, not a replacement for Hyundai's authentication or MFA. The current US adapter's MFA limitation remains unchanged.

## iPhone / iPad implementation

`Native/iOS/NGarageVault.mm` uses a generic-password Keychain item with:

- `kSecAttrAccessibleWhenPasscodeSetThisDeviceOnly`: a device passcode is required; the item is device-bound and not migratable to another device.
- `kSecAccessControlBiometryCurrentSet`: reading the item requires the currently enrolled Face ID / Touch ID. Adding or replacing enrolled biometrics invalidates access.
- `kSecAttrSynchronizable = false`: no iCloud Keychain synchronization.
- A fresh `LAContext` for each operation, with biometric reuse disabled. No device-passcode or app-level success-flag fallback decrypts the item.

Saving first authenticates the owner, then creates or updates the protected Keychain item. Unlock uses `SecItemCopyMatching` against the protected item itself, so the authentication is bound to the secret read. A fresh-install marker clears orphaned Keychain credentials that could otherwise outlive an uninstall. A native privacy cover hides the UI while the app is inactive/app-switcher snapshots are taken.

### Build on a Mac

The repository does **not** include a compiled iOS vault binary. Run on a Mac with Xcode command-line tools:

```sh
bash Native/iOS/build.sh
```

This creates `ios/plugins/ngarage_vault/NGarageVault.xcframework` (arm64 iOS device and Apple Silicon simulator) and copies its `.gdip` descriptor. It requires no Python runtime or custom Godot engine build. The native library has a small C ABI accessed from C#; it does not register a Godot singleton.

In Godot's iOS export options, enable **NGarageVault**. The descriptor adds Foundation, Security, LocalAuthentication and UIKit, preserves the native entry points, and supplies `NSFaceIDUsageDescription`. Use your own bundle identifier, signing team and provisioning profile. No Keychain Sharing entitlement is required for this app-private item. Use iOS 15 or newer for this bridge. The provided script targets arm64 simulators; Intel Mac simulators require adding an x86_64 simulator slice.

Build the complete signed app and test the checklist below. The native iOS source has not been compiled or executed in this Windows validation environment; plugin linking, Face ID permission behavior, AOT interop and lifecycle behavior still require Xcode/device verification. If the bridge is absent, secure saving is unavailable and manual login stays memory-only.

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

While unlocked, credentials exist in process memory because Hyundai's token refresh protocol requires the password. Tokens, vehicle state and location are not persisted. Managed strings cannot be guaranteed to be cryptographically zeroized; the implementation drops references on lock/sign-out. Native temporary byte buffers are cleared where possible. Errors are sanitized, and no credentials or raw API responses are logged.

## Validation and remaining device checks

Validation repeated in the Windows checkout:

- C# application and C# Android export plugin compile.
- Android native plugin Java compile against Android 35 and Godot 4.7 APIs.
- 18 secure-login coordinator checks with a fake native bridge, in addition to 132 vehicle/protocol checks (150 total).
- Rendered security smoke remains available for save preference, background lock, cancelled biometric unlock, mocked unlock, stale-unlock rejection and account removal. The current Godot smoke launch stalled during .NET initialization before app execution; current rendered flows remain unverified. Prior rendered results are not native/device verification; see README for details.

The nCTRL rename preserves all secure-storage identifiers and encryption settings, including the legacy Android AES-GCM associated-data text. The bundled Android AAR includes the new prompt titles; rebuild the iOS plugin for the new prompt/Face ID description. Keep the app's existing package/bundle identity and signing configuration when upgrading. Changing those may require users to sign in and securely save the account again. No plaintext or cross-identity credential migration is performed. See README for appearance preference compatibility.

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
- Godot Android plugins: https://docs.godotengine.org/en/stable/tutorials/platform/android/android_plugin.html
