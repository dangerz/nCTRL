import UIKit
import UniformTypeIdentifiers
import os

// "Send to car" in the Share sheet: saves what was shared (links and text, including Apple Maps' contact card)
// for the nCTRL app, then opens nCTRL on its Send to car screen. Nothing is sent from here; the app confirms first.
// Each share also leaves a receipt (when, what kinds of items, whether it was saved and nCTRL opened; never the
// content), which nCTRL shows under Activity → Shared.
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
        log.info("Share received: \(kinds.joined(separator: ", "), privacy: .public) from \(hosts.joined(separator: ", "), privacy: .public)")
        let saved = save(id: id, urls: Array(urls.prefix(5)), texts: Array(texts.prefix(5)), kinds: kinds)
        message.text = saved ? "Saved for nCTRL.\nOpening nCTRL…" : "nCTRL couldn't save this.\nNothing in it looked like a place."
        try? await Task.sleep(nanoseconds: 700_000_000)
        let opened = saved ? await open(URL(string: "nctrl://send")!) : false
        log.info("Saved: \(saved, privacy: .public), opened nCTRL: \(opened, privacy: .public)")
        receipt(id: id, kinds: kinds, hosts: hosts, saved: saved, opened: opened)
        if saved && !opened { message.text = "Saved for nCTRL.\nOpen nCTRL to send it to your car." }
        finish(after: opened ? 0.2 : 2.5)
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

    private func save(id: String, urls: [String], texts: [String], kinds: [String]) -> Bool {
        guard let file = NctrlGroup.file("share.json"), !(urls.isEmpty && texts.isEmpty) else { return false }
        let shared: [String: Any] = ["version": 1, "id": id, "urls": urls, "texts": texts.map { String($0.prefix(4000)) },
                                     "kinds": Array(Set(kinds)).sorted(), "at": ISO8601DateFormatter().string(from: Date())]
        guard let data = try? JSONSerialization.data(withJSONObject: shared) else { return false }
        return (try? data.write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])) != nil
    }

    // The last 10 receipts, kept until nCTRL reads them. No content: only the kinds of items and the link hosts.
    private func receipt(id: String, kinds: [String], hosts: [String], saved: Bool, opened: Bool) {
        guard let file = NctrlGroup.file("share-log.json") else { return }
        var receipts = ((try? Data(contentsOf: file)).flatMap { try? JSONSerialization.jsonObject(with: $0) } as? [[String: Any]]) ?? []
        receipts.append(["id": id, "at": ISO8601DateFormatter().string(from: Date()), "kinds": Array(Set(kinds)).sorted(),
                         "hosts": Array(Set(hosts)).sorted(), "saved": saved, "opened": opened])
        if let data = try? JSONSerialization.data(withJSONObject: Array(receipts.suffix(10))) {
            try? data.write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])
        }
    }

    // Share extensions have no official way to open their app. Walk the responder chain to the object that can
    // open URLs (the application) and call it through the Objective-C runtime, since extensions may only use
    // extension-safe API directly. True only when iOS reports nCTRL opened.
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

    private func finish(after seconds: Double) {
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds) {
            self.extensionContext?.completeRequest(returningItems: nil, completionHandler: nil)
        }
    }
}
