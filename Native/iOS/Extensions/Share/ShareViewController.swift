import UIKit
import SwiftUI
import UniformTypeIdentifiers
import CoreLocation
import Security
import os

// "Send to car" in the Share sheet. It sends the shared place to the car itself, without opening nCTRL, using nCTRL's
// own C# Core (NctrlCore.framework, see Native/iOS/CoreLib) with the saved login and session the app keeps in the
// Keychain group it shares only with this extension: Core reads the place (opening a Google Maps short link if that's all there is),
// Apple's geocoder looks up an address, and Core sends it through the same request limits as the app. If it can't
// send (no saved login, Hyundai refused, no connection), the share is saved for nCTRL to show the next time it opens,
// without sending again by itself. Each share leaves a note for Activity → Shared, written first and updated as it goes.
final class ShareViewController: UIViewController {
    private let card = SendCardModel()
    private let log = Logger(subsystem: NctrlGroup.appBundleID + ".share", category: "share")

    override func viewDidLoad() {
        super.viewDidLoad()
        // Your car in its paint, on your app color, from what nCTRL keeps for its widget.
        let status = WidgetStatus.load(from: NctrlGroup.file("status.json"))
        let host = UIHostingController(rootView: SendCardView(model: card, status: status, picture: loadPicture(NctrlGroup.file("car.png"))))
        addChild(host)
        host.view.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(host.view)
        NSLayoutConstraint.activate([
            host.view.leadingAnchor.constraint(equalTo: view.leadingAnchor), host.view.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            host.view.topAnchor.constraint(equalTo: view.topAnchor), host.view.bottomAnchor.constraint(equalTo: view.bottomAnchor)])
        host.didMove(toParent: self)
        Task { await share() }
    }

    private func share() async {
        // A note first, updated at each step, so every share is listed in Activity → Shared with how far it got,
        // even one that stops part-way.
        let id = UUID().uuidString
        note(id, ["stage": "started", "saved": false, "opened": false, "sent": false])
        var urls: [String] = [], texts: [String] = [], kinds: [String] = [], linkLate = false
        for item in extensionContext?.inputItems as? [NSExtensionItem] ?? [] {
            if let text = item.attributedContentText?.string, !text.isEmpty { texts.append(text); kinds.append("text") }
            for provider in item.attachments ?? [] {
                if provider.hasItemConformingToTypeIdentifier(UTType.url.identifier) {
                    // Google Maps makes its link while the sheet is open, which can take several seconds the first time.
                    card.phase = .working("Waiting for the shared link…")
                    let (value, late) = await load(provider, UTType.url, seconds: 15)
                    linkLate = linkLate || late
                    if let url = value as? URL { urls.append(url.absoluteString); kinds.append("link") }
                    else if let data = value as? Data, let url = URL(dataRepresentation: data, relativeTo: nil) { urls.append(url.absoluteString); kinds.append("link") }
                }
                for (type, kind) in [(UTType.vCard, "contact card"), (UTType.plainText, "text")] where provider.hasItemConformingToTypeIdentifier(type.identifier) {
                    if let value = await load(provider, type, seconds: 5).value {
                        if let text = value as? String { texts.append(text); kinds.append(kind) }
                        else if let data = value as? Data, let text = String(data: data, encoding: .utf8) { texts.append(text); kinds.append(kind) }
                        else if let url = value as? URL, let text = try? String(contentsOf: url, encoding: .utf8) { texts.append(text); kinds.append(kind) }
                    }
                }
            }
        }
        let hosts = urls.compactMap { URL(string: $0)?.host }
        let late = "The app didn't hand over the place in time. Try sharing again."
        note(id, ["stage": "received", "kinds": Array(Set(kinds)).sorted(), "hosts": Array(Set(hosts)).sorted()])
        log.info("Share received: \(kinds.joined(separator: ", "), privacy: .public) from \(hosts.joined(separator: ", "), privacy: .public), link late: \(linkLate, privacy: .public)")
        if urls.isEmpty && texts.isEmpty {
            fail(id, linkLate ? late : "Nothing came with the share. Try sharing again.")
            return
        }
        let shared: [String: Any] = ["version": 1, "id": id, "urls": Array(urls.prefix(5)), "texts": texts.prefix(5).map { String($0.prefix(4000)) },
                                     "kinds": Array(Set(kinds)).sorted(), "at": ISO8601DateFormatter().string(from: Date())]

        // Read the place with nCTRL's Core, then look up an address with Apple's geocoder if needed.
        card.phase = .working("Finding the place…")
        guard let read = await core("read_share", shared) else {
            fail(id, "nCTRL couldn't read the share (its reader didn't start).")
            return
        }
        var found = read["place"] as? [String: Any]
        if found == nil, let lookup = read["lookup"] as? String, !lookup.isEmpty {
            note(id, ["stage": "looking up"])
            card.phase = .working("Looking up the address…")
            found = await geocode(lookup, name: read["name"] as? String ?? "")
            if found == nil { fail(id, linkLate ? late : "Apple Maps couldn't find “\(lookup)”."); return }
        }
        guard var place = found else {
            let problem = (read["problem"] as? String).flatMap { $0.isEmpty ? nil : $0 + "." } ?? "Nothing in it looked like a place."
            fail(id, linkLate ? late : problem)
            return
        }
        let name = place["name"] as? String ?? "the place"
        note(id, ["stage": "found", "place": place])

        // Send it with the app's saved login and session.
        guard let account = keychain("com.ngarage.nctrl.account.v2", "account"), let limits = NctrlGroup.file("request-limits.json")?.path else {
            // No saved login here yet (or not signed in): nCTRL sends it as usual when it opens.
            await handOff(shared, id: id, unsent: "")
            return
        }
        // A hand-written link named the place: show Apple's address for its coordinates and send only on a tap.
        if read["confirm"] as? Bool == true, let lat = place["lat"] as? Double, let lon = place["lon"] as? Double {
            note(id, ["stage": "confirming"])
            card.phase = .working("Checking where the link points…")
            let address = await reverseGeocode(lat, lon)
            if let address { place["address"] = address }
            card.phase = .confirm(place: name, address: address ?? "Apple Maps found no address at this pin.")
            let send = await withCheckedContinuation { (continuation: CheckedContinuation<Bool, Never>) in
                let card = self.card
                card.answer = { [weak card] yes in card?.answer = nil; continuation.resume(returning: yes) }
            }
            if !send {
                note(id, ["stage": "cancelled", "problem": "Cancelled after checking the address."])
                finish(.notSent("Cancelled. Nothing was sent."), after: 0.8)
                return
            }
        }
        note(id, ["stage": "sending"])
        card.phase = .sending(place: name)
        let session = keychain("com.ngarage.nctrl.session.v1", "session")
        let request: [String: Any] = ["account": account, "session": session ?? NSNull(), "place": place, "limits": limits]
        let result = await core("send", request)
        if result?["ok"] as? Bool == true {
            if let updated = result?["session"] as? String { saveSession(updated) }
            note(id, ["stage": "sent", "saved": true, "sent": true])
            log.info("Sent to the car from the Share sheet")
            finish(.sent(place: name, detail: place["address"] as? String ?? ""), after: 2.2)
        } else {
            let error = result?["error"] as? String ?? "nCTRL couldn't send it from here."
            log.info("Not sent from the Share sheet: \(error, privacy: .private)")
            note(id, ["stage": "not sent", "problem": error])
            await handOff(shared, id: id, unsent: error)
        }
    }

    private func fail(_ id: String, _ problem: String) {
        note(id, ["stage": "failed", "problem": problem])
        log.info("Share not read: \(problem, privacy: .private)")
        finish(.notSent(problem), after: 3.0)
    }

    // Saves the share for nCTRL and tries to bring nCTRL forward. With a reason it wasn't sent (Hyundai refused, no
    // connection), nCTRL shows it on Send to car without sending again by itself; otherwise nCTRL sends it as usual.
    private func handOff(_ shared: [String: Any], id: String, unsent: String) async {
        var shared = shared
        if !unsent.isEmpty { shared["unsent"] = unsent }
        let saved = save(shared)
        let opened = saved ? await open(URL(string: "nctrl://send")!) : false
        note(id, ["stage": "handed off", "saved": saved, "opened": opened])
        let phase: SendPhase = !saved ? .notSent("nCTRL couldn't save this.") :
            unsent.isEmpty ? .handedOff("Open nCTRL to send it.") : .notSent("\(unsent)\nOpen nCTRL to send it.")
        finish(phase, after: opened ? 0.2 : 3.5)
    }

    private func finish(_ phase: SendPhase, after seconds: Double) {
        card.phase = phase
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds) {
            self.extensionContext?.completeRequest(returningItems: nil, completionHandler: nil)
        }
    }

    // nCTRL's Core: an operation and JSON in, JSON out. Runs off the main thread (it waits for the network).
    private func core(_ operation: String, _ input: [String: Any]) async -> [String: Any]? {
        guard let data = try? JSONSerialization.data(withJSONObject: input), let json = String(data: data, encoding: .utf8) else { return nil }
        return await Task.detached {
            guard let result = nctrl_core_call(operation, json) else { return nil }
            defer { nctrl_core_free(result) }
            let text = String(cString: result)
            return (try? JSONSerialization.jsonObject(with: Data(text.utf8))) as? [String: Any]
        }.value
    }

    // The address Apple Maps gives for a pin, in one line.
    private func reverseGeocode(_ lat: Double, _ lon: Double) async -> String? {
        guard let mark = try? await CLGeocoder().reverseGeocodeLocation(CLLocation(latitude: lat, longitude: lon)).first else { return nil }
        let street = mark.thoroughfare.map { street in mark.subThoroughfare.map { "\($0) \(street)" } ?? street }
        let region = [mark.administrativeArea, mark.postalCode].compactMap { $0 }.joined(separator: " ")
        let full = [street, mark.locality, region.isEmpty ? nil : region].compactMap { $0 }.joined(separator: ", ")
        return full.isEmpty ? nil : full
    }

    private func geocode(_ address: String, name: String) async -> [String: Any]? {
        guard let mark = try? await CLGeocoder().geocodeAddressString(address).first, let location = mark.location else { return nil }
        let street = mark.thoroughfare.map { street in mark.subThoroughfare.map { "\($0) \(street)" } ?? street }
        let region = [mark.administrativeArea, mark.postalCode].compactMap { $0 }.joined(separator: " ")
        let full = [street, mark.locality, region.isEmpty ? nil : region].compactMap { $0 }.joined(separator: ", ")
        return ["name": name.isEmpty ? (mark.name ?? address) : name, "address": full, "zip": mark.postalCode ?? "",
                "lat": location.coordinate.latitude, "lon": location.coordinate.longitude]
    }

    // The Keychain items the app keeps for the Share sheet, in the access group only the app and this extension have
    // ("<team>.<app bundle ID>.secure", the first of both targets' keychain-access-groups, so it's the default for new
    // items). Queries name no group, so a login an older nCTRL kept in the App Group's group is still found until the
    // app moves it.
    private func keychain(_ service: String, _ account: String) -> String? {
        var query = itemQuery(service, account)
        query[kSecReturnData as String] = true
        var value: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &value) == errSecSuccess, let data = value as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }
    // Replaced in place (never deleted first), so the app reading it at the same moment doesn't find it missing.
    private func saveSession(_ json: String) {
        let query = itemQuery("com.ngarage.nctrl.session.v1", "session")
        let data = Data(json.utf8)
        if SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary) != errSecItemNotFound { return }
        var item = query
        item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        SecItemAdd(item as CFDictionary, nil)
    }
    private func itemQuery(_ service: String, _ account: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account,
         kSecAttrSynchronizable as String: false]
    }

    // Some apps (Google Maps) build what they share while the sheet is open; wait at most this long. late: it didn't
    // arrive in time.
    private func load(_ provider: NSItemProvider, _ type: UTType, seconds: Double) async -> (value: NSSecureCoding?, late: Bool) {
        await withTaskGroup(of: (NSSecureCoding?, Bool).self) { group in
            group.addTask { (try? await provider.loadItem(forTypeIdentifier: type.identifier), false) }
            group.addTask { try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000)); return (nil, true) }
            let first = await group.next() ?? (nil, true)
            group.cancelAll()
            return first
        }
    }

    private func save(_ shared: [String: Any]) -> Bool {
        guard let file = NctrlGroup.file("share.json"), let data = try? JSONSerialization.data(withJSONObject: shared) else { return false }
        return (try? data.write(to: file, options: [.atomic, .completeFileProtection])) != nil
    }

    // The last 10 notes about shares, kept until nCTRL reads them (Activity → Shared): when, the kinds of items, the
    // link hosts, how far it got, whether it was saved, sent or opened nCTRL, why not, and the place once found. Like
    // share.json it can be read only while the phone is unlocked, and nCTRL deletes it once read. The system log gets
    // only the kinds of items and link hosts; anything that could name the place is marked private.
    private func note(_ id: String, _ fields: [String: Any]) {
        guard let file = NctrlGroup.file("share-log.json") else { return }
        var notes = ((try? Data(contentsOf: file)).flatMap { try? JSONSerialization.jsonObject(with: $0) } as? [[String: Any]]) ?? []
        if let index = notes.firstIndex(where: { $0["id"] as? String == id }) {
            notes[index].merge(fields) { $1 }
        } else {
            var entry = fields
            entry["id"] = id
            entry["at"] = ISO8601DateFormatter().string(from: Date())
            notes.append(entry)
        }
        if let data = try? JSONSerialization.data(withJSONObject: Array(notes.suffix(10))) {
            try? data.write(to: file, options: [.atomic, .completeFileProtection])
        }
    }

    // Share extensions have no official way to open their app. Walk the responder chain to the object that can
    // open URLs and call it through the Objective-C runtime. True only when iOS reports nCTRL opened.
    private func open(_ url: URL) async -> Bool {
        let selector = NSSelectorFromString("openURL:options:completionHandler:")
        typealias OpenURL = @convention(c) (AnyObject, Selector, NSURL, NSDictionary, AnyObject?) -> Void
        var responder: UIResponder? = self
        while let current = responder {
            if current !== self, current.responds(to: selector), let method = current.method(for: selector) {
                return await withCheckedContinuation { continuation in
                    var answered = false
                    func answer(_ opened: Bool) { if !answered { answered = true; continuation.resume(returning: opened) } }
                    let done: @convention(block) (Bool) -> Void = { opened in DispatchQueue.main.async { answer(opened) } }
                    unsafeBitCast(method, to: OpenURL.self)(current, selector, url as NSURL, NSDictionary(), done as AnyObject)
                    DispatchQueue.main.asyncAfter(deadline: .now() + 2) { answer(false) }
                }
            }
            responder = current.next
        }
        return false
    }
}
