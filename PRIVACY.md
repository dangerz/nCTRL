# nCTRL privacy policy

nCTRL is an unofficial app for controlling your own Hyundai through your Hyundai USA (Bluelink) account. It is not made by or affiliated with Hyundai.

**In short:** nCTRL has no server, no analytics, no ads and no tracking. It talks directly from your phone to Hyundai, to OpenStreetMap for map pictures, and to Open-Meteo for the outside temperature. Everything it keeps stays on your phone.

## What nCTRL sends, and to whom

- **Hyundai (api.telematics.hyundaiusa.com).** When you open or use the app, your phone sends your Bluelink email, password and service PIN to Hyundai, then asks Hyundai for your car's status and location, Surround View pictures, and the commands you choose (lock, climate, charging and so on). **Send to car** sends the place you confirm, or a place you share to nCTRL (right away), with its name, address and location, to Hyundai for your car's navigation.
- **Google (shared Google Maps links).** When you share a Google Maps place to nCTRL, Google's short link holds no place until it's opened, so nCTRL opens that one link and reads only where it leads (up to 3 redirects, Google addresses only). Google sees your IP address and the link, which Google made; no cookies, account details or anything else are sent.
- **Apple (address lookup).** When you look up an address in Send to car, the text you typed goes to Apple's geocoding service through iOS, which returns the place. Apple's privacy policy covers it. Hyundai's own privacy policy covers what Hyundai does with this.
- **OpenStreetMap (tile.openstreetmap.org).** When you view your car's location or a place to send, nCTRL downloads map pictures of that area (which can include the area around you when the map shows your position next to the car). OpenStreetMap sees your IP address and which map area was shown, but not the car marker, your account or any identifiers. Its policy is at <https://osmfoundation.org/wiki/Privacy_Policy>.
- **Open-Meteo (api.open-meteo.com).** For the cold-weather range estimate, nCTRL sends the car's last known location, rounded to about 11 km (7 miles), and gets back the current temperature there. This happens at most every 30 minutes, and never while the estimate is turned off (**Settings → Range & units**). Open-Meteo sees your IP address and that rounded area, but no account or identifiers. Its terms and privacy policy are at <https://open-meteo.com/en/terms>.
- **Your maps app.** Tapping the map opens Apple Maps (or your default maps app) at the car's location.

nCTRL sends nothing anywhere else. Its developer receives no data from the app.

## What nCTRL keeps on your phone

- **Your saved login,** if you choose to save it: email, password and service PIN, in the iOS Keychain, encrypted by iOS, readable only while your phone is unlocked and locked to this device. It is never synced. nCTRL uses it without Face ID, like the Hyundai app, so anyone with your unlocked phone can use nCTRL.
- **What the widget shows** (battery, range, locks, charging, when it updated, the car's name and picture, your colors), in a folder on this phone shared only with nCTRL's widget and Share extension. No location, VIN or account details. Turning the widget off in Settings → Appearance, or Sign out & forget, deletes it.
- **A place you share to nCTRL** from another app (the link and text that app shared), in that same folder until nCTRL opens it, then deleted, plus a short note per share (the kinds of items and link hosts, whether it was saved and nCTRL opened; never the content).
- **Your shared places** (Activity → Shared): the last 20 places shared to nCTRL, with when and from where, in the iOS Keychain on this device only. **Clear shared places** or Sign out & forget deletes them.
- **Your phone's position,** if you allow it: shown on the car map while it's open, kept in memory only, never saved or sent anywhere.
- **Your car's last status** (battery, range, doors, climate and charging, plus when it was reported), so the app can open without a request. It holds no location, email or full VIN; the account and car are identified only by one-way hashes.
- **The car's last known location,** in its own Keychain item on this device only, so the map can show where the car was when a new location can't be requested.
- **Ignored places** you add (a name and a location), in the Keychain on this device only.
- **Preferences:** paint, app color, garage and car names, the car picture you picked, climate presets, metric or imperial, the cold-weather range settings, request-limit counts, and when you accepted the risk disclaimer (tied to a hash of your account, not your email).
- **The outside temperature** stays in the app's memory only and is never saved.
- **Map pictures** downloaded from OpenStreetMap, cached for about a week. They contain no car marker or account data.
- **Surround View pictures** stay in the app's memory until you leave the app. One is saved to your photo library only if you tap **Save to Photos**, which uses add-only access: nCTRL can add a picture but never see your library.

Face ID (asked only to accept the risk disclaimer and to go past a request limit) is handled by iOS. nCTRL never sees your face data.

## The demo

**Try the demo** uses a pretend car built into the app. It sends nothing to Hyundai, requests no weather (it uses a pretend temperature) and saves nothing; ending the demo puts back your own settings. Viewing the demo car's location still downloads OpenStreetMap pictures of a public place.

## Deleting your data

**Settings → Sign out & forget** deletes the saved login, saved status, ignored places, last known location and your car names and pictures. Deleting the app removes its files; any Keychain items left behind are cleared the next time nCTRL is installed. Data Hyundai holds is managed through Hyundai.

## Children

nCTRL is meant for adult vehicle owners and isn't directed at children.

## Changes and contact

Changes to this policy are made in this file, with history on GitHub. Questions: open an issue at <https://github.com/dangerz/nCTRL/issues>.
