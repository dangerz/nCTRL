# fiveN command comparison

Reviewed October 23, 2026. Reference: `C:\Users\Roy\Documents\fiveN\Classes\Managers\CommsManager.cs`, particularly `Connect` and `Lock(bool)`, plus `Classes/Scenes/Main.cs`. The reference files were read only. No account credentials or real vehicle commands were used.

## Differences found and corrected

Previously nCTRL followed the upstream Hyundai USA door payload (`userName`, `vin`) and the upstream browser-style vehicle headers. Only token/enrollment requests had been aligned with the owner's fiveN example. That was not a complete command comparison.

| Detail | fiveN | Updated nCTRL |
| --- | --- | --- |
| Lock/unlock method and path | POST `/ac/v2/rcs/rdo/off` for lock, `/on` for unlock | Same |
| Door JSON | `userName`, `vin`, **`pin`** | Same; body PIN was missing previously |
| Door-specific header | `APPCLOUD-VIN` | Same |
| Account/vehicle headers | `username`, `accessToken`, `registrationId`, `vin`, `gen`, `blueLinkServicePin` | Same |
| Common headers | `accept`, `from`, `to`, `language`, `offset`, `brandIndicator`, `client_id`, `clientSecret`, short `User-Agent`, `refresh=false` | Same profile for all vehicle commands |
| Additional browser headers | None | Removed `origin`, `referer`, `encryptFlag`, `accept-language`, and long User-Agent suffix from vehicle requests |
| Body encoding | UTF-8 `application/json` | Same |

The common profile now applies to lock/unlock, climate start/stop, charging start/stop, charge targets, and vehicle read/tracking requests. The service PIN remains a vehicle-request header. The JSON body PIN is added specifically to door commands because that is the payload demonstrated by fiveN.

fiveN contains no climate, charging, charge-target, location-command or transaction-tracking implementation to compare. nCTRL keeps the existing endpoint-specific payloads for those operations: generation-aware climate JSON, both AC/DC targets, and no body for climate-stop/charging-start/charging-stop. There is no evidence in fiveN for adding an arbitrary JSON PIN to these other endpoints or changing their routes.

## Differences retained and why

- **Session reuse:** fiveN creates a new HTTP client, logs in, and loads the first enrolled vehicle for each door command. nCTRL reuses the current session, refreshes it when needed, and targets the user's selected vehicle. Repeating sign-in for every tap adds unnecessary requests and could target the wrong vehicle in a multi-vehicle account.
- **Mobile-compatible JSON and async execution:** fiveN uses reflection-based serialization and blocks on HTTP calls. nCTRL writes JSON explicitly and awaits HTTP to support iOS NativeAOT and keep the UI responsive. The door JSON fields/values and content type match.
- **Transport limits:** fiveN uses HttpClient defaults, including redirects and the default timeout. nCTRL refuses redirects to avoid forwarding account headers and uses a 45-second timeout/2-MiB response limit. HTTPS certificate validation stays enabled. Neither implementation automatically repeats a failed door command.
- **Results:** fiveN treats HTTP success as “sent,” prints the raw response, and its UI labels the action sent when the method returns. It does not verify vehicle completion. nCTRL checks HTTP and recognized JSON API errors, never prints raw account responses, distinguishes acceptance from completion, and offers transaction-status checks only when a recognized tracking ID is returned.
- **Read requests:** fiveN's `GetVehicleDetails` adds registration/VIN/generation but no service-PIN header. nCTRL retains the PIN header on vehicle reads and tracking from its original US adapter; these do not establish a difference in the compared door command.
- **Pacing/state:** nCTRL serializes operations and retains cooldowns and pending-command protection. It never changes reported vehicle state merely because a command was accepted.

The user's “accepted; no tracking ID” message establishes an HTTP-success response from Hyundai, not car execution. The missing body PIN was a real difference worth correcting, but it is not yet proven to be the reason for the absent car response.

## Verification

- Executed a scratch copy of fiveN's actual `CommsManager` with only its two `new HttpClient()` construction sites replaced by a fake transport factory. Synthetic account values were supplied in memory; reference logging was suppressed. No reference files were edited or real app launched.
- Executed nCTRL against the same fake response shapes. Both lock and unlock matched method, endpoint, HTTP version, **all 17 explicit request headers**, content type, and all three JSON fields/values. Each submitted exactly one command. This compares constructed requests before network transport, not TLS or OS-generated wire headers.
- **270 offline checks passed:** 227 protocol, 32 secure-login coordinator, 11 remembered-vehicle/door-control. Protocol checks enforce the common minimal headers across vehicle requests, both door PIN locations, unchanged EV payload behavior, and no retries. JSON reflection remains disabled in the main test suite.
- Debug and ExportRelease C# builds passed with zero warnings/errors. The isolated reference comparison compiled with one existing unused-field warning in fiveN's source.
- Live Hyundai behavior, vehicle response, and iPhone export/device behavior remain unverified. Re-export/reinstall the app to use the changed requests.
