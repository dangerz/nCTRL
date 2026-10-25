import SwiftUI
import ImageIO
import UniformTypeIdentifiers

// Draws the widget's views on a Mac from a status.json and car.png, for checking the layouts without a phone:
//   swiftc -parse-as-library Shared/NctrlStyle.swift Widget/WidgetViews.swift Preview/RenderWidget.swift -o render && ./render status.json car.png out/
@main
struct RenderWidget {
    @MainActor static func main() {
        let args = CommandLine.arguments
        guard args.count == 4, let status = WidgetStatus.load(from: URL(fileURLWithPath: args[1])) else {
            print("Usage: render <status.json> <car.png> <output folder>"); exit(2)
        }
        let picture = loadPicture(URL(fileURLWithPath: args[2]))
        let sizes: [(StatusLayout, CGFloat, CGFloat, String, Bool)] = [
            (.small, 170, 170, "widget-small", false), (.medium, 364, 170, "widget-medium", false),
            (.small, 170, 170, "widget-small-locked", true), (.rectangular, 172, 76, "widget-lock-rectangular", false),
            (.circular, 76, 76, "widget-lock-circular", false)]
        for (layout, width, height, name, locked) in sizes {
            let home = layout == .small || layout == .medium
            let view = StatusView(status: status, picture: picture, layout: layout)
                .redacted(reason: locked ? .privacy : [])
                .padding(home ? 16 : 6)
                .frame(width: width, height: height)
                .foregroundStyle(.white)
                .background(home ? Color(hex: status.back) : Color.black.opacity(0.85))
                .clipShape(RoundedRectangle(cornerRadius: home ? 22 : 12))
                .environment(\.colorScheme, .dark)
            let renderer = ImageRenderer(content: view)
            renderer.scale = 3
            guard let image = renderer.cgImage,
                  let out = CGImageDestinationCreateWithURL(URL(fileURLWithPath: args[3]).appendingPathComponent(name + ".png") as CFURL, UTType.png.identifier as CFString, 1, nil)
            else { continue }
            CGImageDestinationAddImage(out, image, nil)
            CGImageDestinationFinalize(out)
        }
    }
}
