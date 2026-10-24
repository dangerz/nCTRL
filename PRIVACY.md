# nCTRL privacy policy

nCTRL is an unofficial app for controlling your own Hyundai through your Hyundai USA (Bluelink) account. It is not made by or affiliated with Hyundai.

**In short:** nCTRL has no server, no analytics, no ads and no tracking. It talks directly from your phone to Hyundai, to OpenStreetMap for map pictures, and to Open-Meteo for the outside temperature. Everything it keeps stays on your phone.

## What nCTRL sends, and to whom

- **Hyundai (api.telematics.hyundaiusa.com).** When you sign in or use the app, your phone sends your Bluelink email, password and service PIN to Hyundai, then asks Hyundai for your car's status and location, Surround View pictures, and the commands you choose (lock, climate, charging and so on). Hyundai's own privacy policy covers what Hyundai does with this.
- **OpenStreetMap (tile.openstreetmap.org).** When you view your car's location, nCTRL downloads map pictures of that area. OpenStreetMap sees your IP address and which map area was shown, but not the car marker, your account or any identifiers. Its policy is at <https://osmfoundation.org/wiki/Privacy_Policy>.
- **Open-Meteo (api.open-meteo.com).** For the cold-weather range estimate, nCTRL sends the car's last known location, rounded to about 11 km (7 miles), and gets back the current temperature there. This happens at most every 30 minutes, and never while the estimate is turned off (**Settings → Range & units**). Open-Meteo sees your IP address and that rounded area, but no account or identifiers. Its terms and privacy policy are at <https://open-meteo.com/en/terms>.
- **Your maps app.** Tapping the map opens Apple Maps (or your default maps app) at the car's location.

nCTRL sends nothing anywhere else. Its developer receives no data from the app.

## What nCTRL keeps on your phone

- **Your saved login,** if you choose to save it: email, password and service PIN, in the iOS Keychain, locked to this device and to your current Face ID or Touch ID enrollment. It is never synced.
- **Your car's last status** (battery, range, doors, climate and charging, plus when it was reported), so the app can open without a request. It holds no location, email or full VIN; the account and car are identified only by one-way hashes.
- **The car's last known location,** in its own Keychain item on this device only, so the map can show where the car was when a new location can't be requested.
- **Ignored places** you add (a name and a location), in the Keychain on this device only.
- **Preferences:** paint, app color, garage and car names, the car picture you picked, climate presets, metric or imperial, the cold-weather range settings, request-limit counts, and when you accepted the risk disclaimer (tied to a hash of your account, not your email).
- **The outside temperature** stays in the app's memory only and is never saved.
- **Map pictures** downloaded from OpenStreetMap, cached for about a week. They contain no car marker or account data.
- **Surround View pictures** stay in the app's memory until you leave the app. One is saved to your photo library only if you tap **Save to Photos**, which uses add-only access: nCTRL can add a picture but never see your library.

Face ID is handled by iOS. nCTRL never sees your face data.

## The demo

**Try the demo** uses a pretend car built into the app. It sends nothing to Hyundai, requests no weather (it uses a pretend temperature) and saves nothing; ending the demo puts back your own settings. Viewing the demo car's location still downloads OpenStreetMap pictures of a public place.

## Deleting your data

**Settings → Sign out & forget** deletes the saved login, saved status, ignored places, last known location and your car names and pictures. Deleting the app removes its files; any Keychain items left behind are cleared the next time nCTRL is installed. Data Hyundai holds is managed through Hyundai.

## Children

nCTRL is meant for adult vehicle owners and isn't directed at children.

## Changes and contact

Changes to this policy are made in this file, with history on GitHub. Questions: open an issue at <https://github.com/dangerz/nCTRL/issues>.
