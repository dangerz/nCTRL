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

enum StatusLayout { case small, medium, rectangular, circular }

// The widget's look, kept free of WidgetKit so it can also be drawn in tests.
struct StatusView: View {
    let status: WidgetStatus?
    let picture: CGImage?
    let layout: StatusLayout
    private static let lockRed = Color(hex: "f24a4d"), chargeYellow = Color(hex: "ffd129")

    var body: some View {
        if let status {
            switch layout {
            case .small: small(status)
            case .medium: medium(status)
            case .rectangular: rectangular(status)
            case .circular: circular(status)
            }
        } else {
            VStack(alignment: .leading, spacing: 4) {
                Text("nCTRL").font(.headline)
                Text("Open nCTRL to show your car here.").font(.caption)
            }
        }
    }
    private func lockIcon(_ s: WidgetStatus) -> some View {
        Image(systemName: s.locked == false ? "lock.open.fill" : "lock.fill")
            .foregroundStyle(s.locked == false ? Color(hex: s.text) : Self.lockRed)
            .opacity(s.locked == nil ? 0 : 1)
            .privacySensitive()
    }
    private func carPicture() -> some View {
        Group {
            if let picture { Image(decorative: picture, scale: 1).resizable().scaledToFit() } else { Color.clear }
        }
    }
    private func batteryText(_ s: WidgetStatus, size: CGFloat) -> some View {
        Text(s.battery.map { "\($0)%" } ?? "—").font(.system(size: size, weight: .heavy)).foregroundStyle(Color(hex: s.accent))
            .privacySensitive()
    }
    private func details(_ s: WidgetStatus) -> some View {
        VStack(alignment: .leading, spacing: 1) {
            Text(s.range).font(.system(size: 13, weight: .bold)).foregroundStyle(Color(hex: s.text)).privacySensitive()
            if !s.cold.isEmpty {
                HStack(spacing: 3) { Image(systemName: "snowflake"); Text(s.cold) }
                    .font(.system(size: 10, weight: .semibold)).foregroundStyle(Color(hex: s.accent)).privacySensitive()
            }
            if s.charging == true {
                HStack(spacing: 3) { Image(systemName: "bolt.fill"); Text(s.charge.isEmpty ? "Charging" : s.charge) }
                    .font(.system(size: 10, weight: .bold)).foregroundStyle(Self.chargeYellow).lineLimit(1)
            }
        }
    }
    private func small(_ s: WidgetStatus) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(s.name).font(.system(size: 11, weight: .bold)).foregroundStyle(Color(hex: s.muted)).lineLimit(1)
                Spacer(minLength: 4)
                lockIcon(s).font(.system(size: 12))
            }
            carPicture().frame(maxWidth: .infinity, maxHeight: 40)
            batteryText(s, size: 28)
            details(s)
            Spacer(minLength: 0)
        }
    }
    private func medium(_ s: WidgetStatus) -> some View {
        HStack(spacing: 10) {
            VStack(alignment: .leading, spacing: 4) {
                Text(s.name).font(.system(size: 12, weight: .bold)).foregroundStyle(Color(hex: s.muted)).lineLimit(1)
                carPicture().frame(maxWidth: .infinity, maxHeight: .infinity)
            }
            VStack(alignment: .leading, spacing: 3) {
                HStack { batteryText(s, size: 34); Spacer(minLength: 0); lockIcon(s).font(.system(size: 16)) }
                details(s)
                Spacer(minLength: 0)
                (Text("Updated ") + Text(s.updated, style: .time)).font(.system(size: 9)).foregroundStyle(Color(hex: s.muted))
            }
            .frame(width: 130)
        }
    }
    private func rectangular(_ s: WidgetStatus) -> some View {
        HStack(spacing: 6) {
            Image(systemName: s.charging == true ? "bolt.car.fill" : "car.fill").font(.system(size: 20))
            VStack(alignment: .leading, spacing: 0) {
                Text((s.battery.map { "\($0)%" } ?? "—") + " · " + s.range).font(.headline).privacySensitive()
                Text(s.charging == true ? "Charging" : s.locked == false ? "Unlocked" : s.locked == true ? "Locked" : s.name).font(.caption)
                    .privacySensitive()
            }
        }
    }
    private func circular(_ s: WidgetStatus) -> some View {
        Gauge(value: Double(s.battery ?? 0), in: 0...100) {
            Image(systemName: "car.fill")
        } currentValueLabel: {
            Text(s.battery.map(String.init) ?? "—").privacySensitive()
        }
        .gaugeStyle(.accessoryCircularCapacity)
    }
}
