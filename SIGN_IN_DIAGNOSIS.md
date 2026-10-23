# nCTRL sign-in investigation

Reviewed October 22, 2026 (US Eastern). No real account requests or vehicle commands were sent during this investigation.

## Evidence

The checkout did not contain the reported prior diagnostic patch: token and enrollment errors were indistinguishable and the protocol tests were the older 50-check version. The owner's reported 502 cannot be assigned to a stage retrospectively from that code alone.

Current upstream was inspected at revision `6b966c021bd3e2e7fd39c979b2abb3defeab8a2b`, also the original attribution revision:
[HyundaiBlueLinkApiUSA.py](https://github.com/Hyundai-Kia-Connect/hyundai_kia_connect_api/blob/6b966c021bd3e2e7fd39c979b2abb3defeab8a2b/hyundai_kia_connect_api/HyundaiBlueLinkApiUSA.py).
Upstream still posts a JSON username/password pair to `/v2/ac/oauth/token`, then gets `/ac/v2/enrollment/details/{username}`. Its JSON error helper treats errorCode 502 separately from authentication errors; this does not establish the cause of this user's sign-in failure. Its token-refresh payload includes username/password plus grant_type/refresh_token, which nCTRL already implements.

The owner then identified a local fiveN checkout as successfully authenticating. Its `Classes/Managers/CommsManager.cs` was inspected read-only; it was not executed. Both applications use the same host, methods, token body, client constants and JSON content type. The relevant differences were:

| Request detail | nCTRL before | Working fiveN example | nCTRL now |
| --- | --- | --- | --- |
| User-Agent | Full Chrome 75 string | Short Mozilla/AppleWebKit string | fiveN string for token/enrollment |
| Extra browser headers | origin, referer, accept-language, encryptFlag | Omitted | Omitted for token/enrollment |
| Enrollment authentication | username, accessToken, blueLinkServicePin | username, accessToken | fiveN headers; PIN retained for vehicle requests |
| Enrollment path | All email punctuation escaped | Raw email interpolation | Literal @/+ like fiveN; path/query delimiters still escaped |
| Compression | Explicit automatic decompression/Accept-Encoding | Default HttpClient, no automatic decompression | fiveN default compression behavior |
| Redirects | Disabled | HttpClient default | Still disabled to avoid forwarding account headers |

These are compatibility changes based on the working local example, not a demonstrated root cause. No fallback profiles, alternate endpoints, certificate overrides or automatic login/command retries were added. fiveN's raw-response/full-VIN logging was not copied. Upstream's TLS customization and raw response logging were not adopted. Vehicle request headers and command semantics were preserved.

## Diagnostics and behavior

- `Authentication: HTTP 502` means the token request failed at the HTTP layer; enrollment was not attempted.
- `Vehicle list: HTTP 502` means a usable token was returned, then enrollment failed at the HTTP layer.
- `Authentication: HTTP 200; API code 502` (or Vehicle list) means an HTTP-success response contained a JSON error. An HTTP error can also include a separate API code.
- Non-numeric/unexpected error-code strings are withheld; server messages, full bodies, account URLs, tokens, PINs and VINs are never echoed. Transport failures also get sanitized stage labels. A 502 alone is not proof of a bad password, outage or changed endpoint.
- Explicit challenge markers stop sign-in even when accompanied by a token. Unsupported/missing-session responses stop the flow. MFA/OTP/CAPTCHA/safety challenges are not implemented or bypassed; there is no claim to recognize every possible challenge schema.
- Failed sign-in clears partial session data. A new user-initiated sign-in can succeed. Failed refresh preserves the last known state; command 502/timeouts remain unknown outcomes and are never automatically resubmitted.

## Minimal live verification

### Mobile-only failure investigated October 23, 2026

The owner reports that sign-in works in the editor but shows the generic “Could not complete the request” message on a physical iPhone. The checkout serialized all POST bodies through `JsonSerializer.Serialize(object)`, requiring runtime reflection. Godot 4.7.2's installed iOS SDK enables NativeAOT; .NET's trimmed builds disable JSON reflection by default ([Microsoft compatibility documentation](https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/8.0/publishtrimmed)).

The unchanged source passed its 134 desktop checks, then threw `InvalidOperationException` in the authentication request when run with JSON reflection disabled. This is a reproduced client defect before an HTTP request is sent, not evidence of a Hyundai 502. Explicit `Utf8JsonWriter` payloads now cover authentication, renewal, door commands, climate and charge targets. Both project and tests explicitly disable JSON reflection so desktop tests expose future regressions. All 179 revised offline checks pass under that restriction.

Unexpected login exceptions now retain the Authentication/Vehicle list stage and a fixed safe category such as `runtime-state` or `platform-unsupported`; original messages, inner exceptions and raw type names are withheld. HTTP/API diagnostics and manual recovery remain intact. The iOS secure-vault packaging is separately revised in `SECURE_LOGIN.md`.

This identifies and fixes a reproducible cause consistent with the report. It does **not** prove that the user's installed iPhone build encountered precisely this exception or that live sign-in is resolved. No real credentials/API requests were used.

### One sign-in attempt on the rebuilt phone app

Freshly export/build/install nCTRL using `SECURE_LOGIN.md`, then sign in once on its opening account screen. Stop at vehicle selection. This sends only token/enrollment requests; no vehicle command is needed. If saving is selected, complete the local biometric save prompt. Successful saving can be checked by backgrounding/reopening the app and unlocking the saved account; stop again at vehicle selection.

If it fails, report only the displayed error text beginning **Authentication:** or **Vehicle list:** (including HTTP/API codes, if present). Do not provide credentials, screenshots containing your email, request/response dumps, tokens, PINs or full VINs. If vehicle selection appears, report that outcome; it verifies this sign-in attempt, not remote commands or mobile secure storage.

See README and SECURE_LOGIN for offline checks, build results and device-validation limits.
