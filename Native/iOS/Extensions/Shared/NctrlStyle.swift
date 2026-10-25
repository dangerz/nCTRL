import SwiftUI
import CoreGraphics
import ImageIO

// What nCTRL writes for the widget (status.json): already formatted in the user's units and app color.
// It holds no location, VIN, email or account details.
struct WidgetStatus: Codable {
    var version: Int
    var name: String
    var battery: Int?
    var range: String
    var cold: String
    var locked: Bool?
    var charging: Bool?
    var charge: String
    var updated: Date
    var accent: String
    var back: String
    var text: String
    var muted: String

    static func load(from url: URL?) -> WidgetStatus? {
        guard let url, let data = try? Data(contentsOf: url) else { return nil }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        guard let status = try? decoder.decode(WidgetStatus.self, from: data), status.version == 1 else { return nil }
        return status
    }
    static let sample = WidgetStatus(version: 1, name: "IONIQ 5 N", battery: 78, range: "184 mi", cold: "~129 mi at 28°F", locked: true,
        charging: false, charge: "", updated: Date(), accent: "8ecbfa", back: "08121d", text: "eef5fb", muted: "9ab0c6")
}

func loadPicture(_ url: URL?) -> CGImage? {
    guard let url, let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
    return CGImageSourceCreateImageAtIndex(source, 0, nil)
}

extension Color {
    init(hex: String) {
        let value = UInt64(hex, radix: 16) ?? 0x8ecbfa
        self.init(red: Double((value >> 16) & 0xff) / 255, green: Double((value >> 8) & 0xff) / 255, blue: Double(value & 0xff) / 255)
    }
}
