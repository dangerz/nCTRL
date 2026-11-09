# Security policy

nCTRL is an unofficial, personal app for controlling your own Hyundai through a Hyundai USA (Bluelink) account. It isn't made by or affiliated with Hyundai. Because it can unlock doors and start climate, security reports are welcome.

## Supported versions

Only the latest TestFlight build gets security fixes. Older builds and desktop development builds aren't supported.

## Reporting a vulnerability

**Please don't open a public issue for a security problem.**

Report it privately through GitHub instead:
1. Open the repository's **Security** tab.
2. Choose **Report a vulnerability**.
3. Describe what you found, the steps to reproduce it, which nCTRL version you used, and what an attacker could do with it.

Please leave out your own Bluelink email, password, PIN, VIN and car location, and anyone else's. Redacted logs or screenshots are enough.

## What to expect

nCTRL is maintained on a best-effort basis by one person, so there's no fixed response time. Every private report is read. Confirmed problems are fixed in a new TestFlight build, and credit goes in the advisory if you'd like it. Please keep the problem private until a fix is available.

## In scope

- How nCTRL stores and uses your Hyundai login, session and PIN: the iOS Keychain, the saved login and the session kept in memory.
- Anything that sends a command to the car (lock, unlock, climate, charging, send to car and so on) without you meaning to, or gets past the request limits or their Face ID override.
- The Share extension, the widget and the folder they share with the app.
- Files the app saves on the phone: saved status, preferences, the map cache, shared places, ignored places and last known location.
- Leaks of personal data (login details, VIN, location) through logs, files, the widget, links or network requests.
- Handling of shared links and places from other apps, including Google Maps short links.

## Out of scope

- **Hyundai's servers and the Bluelink API.** Report these to Hyundai.
- **Third-party services** nCTRL talks to: OpenStreetMap, Open-Meteo, Apple Maps, Google Maps and Apple's address lookup. Report these to those services.
- **The upstream [hyundai_kia_connect_api](https://github.com/Hyundai-Kia-Connect/hyundai_kia_connect_api) library.** Report to that project, unless the problem is in how nCTRL itself uses the same requests.
- Attacks that need a jailbroken or already compromised phone, or someone who already has your phone unlocked. nCTRL deliberately uses the saved login without Face ID, like the Hyundai app.
- Hyundai's own request limits, or Hyundai banning or changing accounts.

## How nCTRL handles your data

- No server, analytics, ads or tracking. The developer receives no data from the app.
- Commands and status go straight from your phone to Hyundai.
- The saved login stays in the iOS Keychain on this device only. It is never synced and is readable only while the phone is unlocked.
- **Sign out & forget** deletes the saved login and the data nCTRL keeps on the phone.

The details are in [PRIVACY.md](PRIVACY.md), which covers what is sent where and what is kept on the phone, and [SECURE_LOGIN.md](SECURE_LOGIN.md), which covers how the saved login is protected on each platform.
