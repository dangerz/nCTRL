import Foundation

// Files shared with the nCTRL app, in its App Group "group.<app bundle ID>". Each extension's bundle ID is the
// app's plus one more part (".widget", ".share").
enum NctrlGroup {
    static var appBundleID: String {
        let parts = (Bundle.main.bundleIdentifier ?? "").split(separator: ".")
        return parts.dropLast().joined(separator: ".")
    }
    static func file(_ name: String) -> URL? {
        FileManager.default.containerURL(forSecurityApplicationGroupIdentifier: "group." + appBundleID)?.appendingPathComponent(name)
    }
}
