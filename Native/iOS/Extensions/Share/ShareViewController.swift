import UIKit
import UniformTypeIdentifiers

// "Send to car" in the Share sheet: saves what was shared (links and text, including Apple Maps' contact card)
// for the nCTRL app, then opens nCTRL on its Send to car screen. Nothing is sent from here; the app confirms first.
final class ShareViewController: UIViewController {
    private let message = UILabel()

    override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = UIColor(red: 0.03, green: 0.07, blue: 0.11, alpha: 1)
        message.text = "Opening nCTRL…"
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
        var urls: [String] = [], texts: [String] = []
        for item in extensionContext?.inputItems as? [NSExtensionItem] ?? [] {
            if let text = item.attributedContentText?.string, !text.isEmpty { texts.append(text) }
            for provider in item.attachments ?? [] {
                if provider.hasItemConformingToTypeIdentifier(UTType.url.identifier),
                   let value = try? await provider.loadItem(forTypeIdentifier: UTType.url.identifier) {
                    if let url = value as? URL { urls.append(url.absoluteString) }
                    else if let data = value as? Data, let url = URL(dataRepresentation: data, relativeTo: nil) { urls.append(url.absoluteString) }
                }
                for type in [UTType.vCard, UTType.plainText] where provider.hasItemConformingToTypeIdentifier(type.identifier) {
                    if let value = try? await provider.loadItem(forTypeIdentifier: type.identifier) {
                        if let text = value as? String { texts.append(text) }
                        else if let data = value as? Data, let text = String(data: data, encoding: .utf8) { texts.append(text) }
                        else if let url = value as? URL, let text = try? String(contentsOf: url, encoding: .utf8) { texts.append(text) }
                    }
                }
            }
        }
        let saved = save(urls: Array(urls.prefix(5)), texts: Array(texts.prefix(5)))
        if saved, open(URL(string: "nctrl://send")!) {
            finish(after: 0.3)
        } else {
            message.text = saved ? "Saved for nCTRL.\nOpen nCTRL to send it to your car." : "nCTRL couldn't save this place."
            finish(after: 2.0)
        }
    }

    private func save(urls: [String], texts: [String]) -> Bool {
        guard let file = NctrlGroup.file("share.json"), !(urls.isEmpty && texts.isEmpty) else { return false }
        let shared: [String: Any] = ["version": 1, "urls": urls, "texts": texts.map { String($0.prefix(4000)) },
                                     "at": ISO8601DateFormatter().string(from: Date())]
        guard let data = try? JSONSerialization.data(withJSONObject: shared) else { return false }
        return (try? data.write(to: file, options: [.atomic, .completeFileProtectionUntilFirstUserAuthentication])) != nil
    }

    // Share extensions have no official way to open their app. Walk the responder chain to the object that can
    // open URLs (the application) and call it through the Objective-C runtime, since extensions may only use
    // extension-safe API directly.
    private func open(_ url: URL) -> Bool {
        let selector = NSSelectorFromString("openURL:options:completionHandler:")
        typealias OpenURL = @convention(c) (AnyObject, Selector, NSURL, NSDictionary, AnyObject?) -> Void
        var responder: UIResponder? = self
        while let current = responder {
            if current !== self, current.responds(to: selector), let method = current.method(for: selector) {
                unsafeBitCast(method, to: OpenURL.self)(current, selector, url as NSURL, NSDictionary(), nil)
                return true
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
