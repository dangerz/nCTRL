import SwiftUI
import ImageIO
import UniformTypeIdentifiers

// Draws the Share sheet's card on a Mac from a status.json and car.png, for checking it without a phone:
//   swiftc -parse-as-library Shared/NctrlStyle.swift Share/SendCardView.swift Preview/RenderShareCard.swift -o render-share
//   ./render-share status.json car.png out/ [accent back]
@main
struct RenderShareCard {
    @MainActor static func main() {
        let args = CommandLine.arguments
        guard args.count >= 4, var status = WidgetStatus.load(from: URL(fileURLWithPath: args[1])) else {
            print("Usage: render-share <status.json> <car.png> <output folder> [accent back]"); exit(2)
        }
        if args.count >= 6 { status.accent = args[4]; status.back = args[5] }
        let picture = loadPicture(URL(fileURLWithPath: args[2]))
        let states: [(SendPhase, String)] = [
            (.sending(place: "Central Park"), "share-sending"),
            (.sent(place: "Central Park", detail: "5th Ave, New York, NY 10022"), "share-sent"),
            (.notSent("Hyundai is rate limiting requests. No automatic retry was sent.\nOpen nCTRL to send it."), "share-not-sent"),
            (.handedOff("Open nCTRL to send it."), "share-saved")]
        for (phase, name) in states {
            let model = SendCardModel()
            model.phase = phase
            let view = SendCardView(model: model, status: status, picture: picture).frame(width: 390, height: 520)
            let renderer = ImageRenderer(content: view)
            renderer.scale = 2
            guard let image = renderer.cgImage,
                  let out = CGImageDestinationCreateWithURL(URL(fileURLWithPath: args[3]).appendingPathComponent(name + (args.count >= 6 ? "-themed" : "") + ".png") as CFURL, UTType.png.identifier as CFString, 1, nil)
            else { continue }
            CGImageDestinationAddImage(out, image, nil)
            CGImageDestinationFinalize(out)
        }
    }
}
