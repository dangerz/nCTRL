import UIKit
import UniformTypeIdentifiers
import CoreLocation
import Security
import os

// "Send to car" in the Share sheet. It sends the shared place to the car itself, without opening nCTRL, using nCTRL's
// own C# Core (NctrlCore.framework, see Native/iOS/CoreLib) with the saved login and Hyundai session the app keeps in
// the App Group's Keychain group: Core reads the place (opening a Google Maps short link if that's all there is),
// Apple's geocoder looks up an address, and Core sends it through the same request limits as the app. If it can't
// send (no saved login, Hyundai refused, no connection), the share is saved for nCTRL to show the next time it opens,
// without sending again by itself. Each share leaves a receipt for Activity → Shared.
final class ShareViewController: UIViewController {
    private let message = UILabel()
    private let log = Logger(subsystem: NctrlGroup.appBundleID + ".share", category: "share")

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = UIColor(red: 0.03, green: 0.07, blue: 0.11, alpha: 1)
        message.text = "Reading what you shared…"
        message.textColor = .white
        message.font = .boldSystemFont(ofSize: 17)
        message.numberOfLines = 0
        message.textAlignment = .center
        message.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(message)
        NSLayoutConstraint.activate([
            message.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            message.centerYAnchor.constraint(equalTo: view.centerYAnchor),
            message.leadingAnchor.constraint(greaterThanOrEqualTo: view.leadingAnchor, constant: 24)])
        Task { await share() }
    }

    private func share() async {
        var urls: [String] = [], texts: [String] = [], kinds: [String] = []
        for item in extensionContext?.inputItems as? [NSExtensionItem] ?? [] {
            if let text = item.attributedContentText?.string, !text.isEmpty { texts.append(text); kinds.append("text") }
            for provider in item.attachments ?? [] {
                if provider.hasItemConformingToTypeIdentifier(UTType.url.identifier), let value = await load(provider, UTType.url) {
                    if let url = value as? URL { urls.append(url.absoluteString); kinds.append("link") }
                    else if let data = value as? Data, let url = URL(dataRepresentation: data, relativeTo: nil) { urls.append(url.absoluteString); kinds.append("link") }
                }
                for (type, kind) in [(UTType.vCard, "contact card"), (UTType.plainText, "text")] where provider.hasItemConformingToTypeIdentifier(type.identifier) {
                    if let value = await load(provider, type) {
                        if let text = value as? String { texts.append(text); kinds.append(kind) }
                        else if let data = value as? Data, let text = String(data: data, encoding: .utf8) { texts.append(text); kinds.append(kind) }
                        else if let url = value as? URL, let text = try? String(contentsOf: url, encoding: .utf8) { texts.append(text); kinds.append(kind) }
                    }
                }
            }
        }
        let id = UUID().uuidString
        let hosts = urls.compactMap { URL(string: $0)?.host }
        var shared: [String: Any] = ["version": 1, "id": id, "urls": Array(urls.prefix(5)), "texts": texts.prefix(5).map { String($0.prefix(4000)) },
                                     "kinds": Array(Set(kinds)).sorted(), "at": ISO8601DateFormatter().string(from: Date())]
        log.info("Share received: \(kinds.joined(separator: ", "), privacy: .public) from \(hosts.joined(separator: ", "), privacy: .public)")
        if urls.isEmpty && texts.isEmpty {
            receipt(id: id, kinds: kinds, hosts: hosts, saved: false, opened: false)
            show("nCTRL couldn't find a place in what was shared.", for: 2.5)
            return
        }

        // Read the place with nCTRL's Core, then look up an address with Apple's geocoder if needed.
        message.text = "Finding the place…"
        var place: [String: Any]?
        if let read = await core("read_share", shared) {
            place = read["place"] as? [String: Any]
            if place == nil, let lookup = read["lookup"] as? String, !lookup.isEmpty {
                message.text = "Looking up the address…"
                place = await geocode(lookup, name: read["name"] as? String ?? "")
            }
        }
        guard let place else {
            receipt(id: id, kinds: kinds, hosts: hosts, saved: false, opened: false)
            show("nCTRL couldn't find a place in what was shared.", for: 2.5)
            return
        }
        let name = place["name"] as? String ?? "the place"

        // Send it with the app's saved login and session.
        guard let account = keychain("com.ngarage.nctrl.account.v2", "account"), let limits = NctrlGroup.file("request-limits.json")?.path else {
            // No saved login here yet (or not signed in): nCTRL sends it as usual when it opens.
            await handOff(shared, id: id, kinds: kinds, hosts: hosts, unsent: "")
            return
        }
        message.text = "Sending \(name) to your car…"
        let session = keychain("com.ngarage.nctrl.session.v1", "session")
        let request: [String: Any] = ["account": account, "session": session ?? NSNull(), "place": place, "limits": limits]
        let result = await core("send", request)
        if result?["ok"] as? Bool == true {
            if let updated = result?["session"] as? String { saveSession(updated) }
            receipt(id: id, kinds: kinds, hosts: hosts, saved: true, opened: false, sent: true, place: place)
            log.info("Sent to the car from the Share sheet")
            show("Sent to your car ✓\n\(name)", for: 1.5)
        } else {
            let error = result?["error"] as? String ?? "nCTRL couldn't send it from here."
            log.info("Not sent from the Share sheet: \(error, privacy: .public)")
            await handOff(shared, id: id, kinds: kinds, hosts: hosts, unsent: error)
        }
    }

    // Saves the share for nCTRL and tries to bring nCTRL forward. With a reason it wasn't sent (Hyundai refused, no
    // connection), nCTRL shows it on Send to car without sending again by itself; otherwise nCTRL sends it as usual.
    private func handOff(_ shared: [String: Any], id: String, kinds: [String], hosts: [String], unsent: String) async {
        var shared = shared
        if !unsent.isEmpty { shared["unsent"] = unsent }
        let saved = save(shared)
        let opened = saved ? await open(URL(string: "nctrl://send")!) : false
        receipt(id: id, kinds: kinds, hosts: hosts, saved: saved, opened: opened)
        let text = !saved ? "nCTRL couldn't save this." : unsent.isEmpty ? "Saved for nCTRL.\nOpen nCTRL to send it." : "Not sent: \(unsent)\nOpen nCTRL to send it."
        show(text, for: opened ? 0.2 : 3.5)
    }

    private func show(_ text: String, for seconds: Double) {
        message.text = text
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

    private func geocode(_ address: String, name: String) async -> [String: Any]? {
        guard let mark = try? await CLGeocoder().geocodeAddressString(address).first, let location = mark.location else { return nil }
        let street = mark.thoroughfare.map { street in mark.subThoroughfare.map { "\($0) \(street)" } ?? street }
        let region = [mark.administrativeArea, mark.postalCode].compactMap { $0 }.joined(separator: " ")
        let full = [street, mark.locality, region.isEmpty ? nil : region].compactMap { $0 }.joined(separator: ", ")
        return ["name": name.isEmpty ? (mark.name ?? address) : name, "address": full, "zip": mark.postalCode ?? "",
                "lat": location.coordinate.latitude, "lon": location.coordinate.longitude]
    }

    // The App Group's Keychain items the app keeps for the Share sheet.
    private func keychain(_ service: String, _ account: String) -> String? {
        var query = itemQuery(service, account)
        query[kSecReturnData as String] = true
        var value: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &value) == errSecSuccess, let data = value as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }
    private func saveSession(_ json: String) {
        let query = itemQuery("com.ngarage.nctrl.session.v1", "session")
        SecItemDelete(query as CFDictionary)
        var item = query
        item[kSecValueData as String] = Data(json.utf8)
        item[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        SecItemAdd(item as CFDictionary, nil)
    }
    private func itemQuery(_ service: String, _ account: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account,
         kSecAttrSynchronizable as String: false, kSecAttrAccessGroup as String: "group." + NctrlGroup.appBundleID]
    }

    // Some apps (Google Maps) build what they share while the sheet is open; never wait more than 5 seconds.
    private func load(_ provider: NSItemProvider, _ type: UTType) async -> NSSecureCoding? {
        await withTaskGroup(of: NSSecureCoding?.self) { group in
            group.addTask { try? await provider.loadItem(forTypeIdentifier: type.identifier) }
            group.addTask { try? await Task.sleep(nanoseconds: 5_000_000_000); return nil }
            let first = await group.next() ?? nil
            group.cancelAll()
            return first
        }
    }

    private func save(_ shared: [String: Any]) -> Bool {
        guard let file = NctrlGroup.file("share.json"), let data = try? JSONSerialization.data(withJSONObject: shared) else { return false }
        return (try? data.write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])) != nil
    }

    // The last 10 receipts, kept until nCTRL reads them: the kinds of items, the link hosts, whether the share was
    // saved, sent or opened nCTRL, and the place when it was sent from here.
    private func receipt(id: String, kinds: [String], hosts: [String], saved: Bool, opened: Bool, sent: Bool = false, place: [String: Any]? = nil) {
        guard let file = NctrlGroup.file("share-log.json") else { return }
        var receipts = ((try? Data(contentsOf: file)).flatMap { try? JSONSerialization.jsonObject(with: $0) } as? [[String: Any]]) ?? []
        var entry: [String: Any] = ["id": id, "at": ISO8601DateFormatter().string(from: Date()), "kinds": Array(Set(kinds)).sorted(),
                                    "hosts": Array(Set(hosts)).sorted(), "saved": saved, "opened": opened, "sent": sent]
        if let place { entry["place"] = place }
        receipts.append(entry)
        if let data = try? JSONSerialization.data(withJSONObject: Array(receipts.suffix(10))) {
            try? data.write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
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
