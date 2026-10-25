import SwiftUI

// What the Share sheet shows while it sends a place: your car in its paint, on your app color (from the widget's
// status.json and car.png), with how the send is going. Kept free of UIKit so it can also be drawn on a Mac.
enum SendPhase: Equatable {
    case working(String)
    case sending(place: String)
    case sent(place: String, detail: String)
    case notSent(String)
    case handedOff(String)
}

final class SendCardModel: ObservableObject {
    @Published var phase: SendPhase = .working("Reading what you shared…")
}

struct SendCardView: View {
    @ObservedObject var model: SendCardModel
    let status: WidgetStatus?
    let picture: CGImage?
    private static let amber = Color(hex: "e0a43a")

    private var accent: Color { Color(hex: status?.accent ?? "8ecbfa") }
    private var back: Color { Color(hex: status?.back ?? "08121d") }
    private var text: Color { Color(hex: status?.text ?? "eef5fb") }
    private var muted: Color { Color(hex: status?.muted ?? "9ab0c6") }
    private var car: String { status.map { $0.name.isEmpty ? "car" : $0.name } ?? "car" }

    var body: some View {
        ZStack {
            back.ignoresSafeArea()
            VStack(spacing: 14) {
                carPicture.frame(maxWidth: .infinity).frame(height: 120)
                content
            }
            .padding(.horizontal, 22).padding(.top, 18).padding(.bottom, 22)
            .frame(maxWidth: 360)
            .background(RoundedRectangle(cornerRadius: 26).fill(Color.white.opacity(0.06)))
            .overlay(RoundedRectangle(cornerRadius: 26).stroke(accent.opacity(0.35), lineWidth: 1))
            .overlay(alignment: .bottom) { if working { SweepLine(color: accent).padding(.horizontal, 26) } }
            .clipShape(RoundedRectangle(cornerRadius: 26))
            .padding(24)
        }
    }

    private var working: Bool {
        switch model.phase { case .working, .sending: return true; default: return false }
    }
    @ViewBuilder private var carPicture: some View {
        if let picture {
            Image(decorative: picture, scale: 1).resizable().scaledToFit()
        } else {
            Image(systemName: "car.side.fill").resizable().scaledToFit().foregroundStyle(accent).padding(28)
        }
    }

    @ViewBuilder private var content: some View {
        switch model.phase {
        case .working(let step):
            Text(step).font(.system(size: 17, weight: .bold)).foregroundStyle(text).multilineTextAlignment(.center)
        case .sending(let place):
            Text("Sending \(place)").font(.system(size: 17, weight: .bold)).foregroundStyle(text).multilineTextAlignment(.center)
            Text("to your \(car)…").font(.system(size: 13)).foregroundStyle(muted)
        case .sent(let place, let detail):
            Image(systemName: "checkmark.circle.fill").font(.system(size: 34)).foregroundStyle(accent)
            Text("SENT TO CAR").font(.system(size: 22, weight: .heavy)).foregroundStyle(text)
            Text(place).font(.system(size: 16, weight: .bold)).foregroundStyle(text).multilineTextAlignment(.center)
            if !detail.isEmpty { Text(detail).font(.system(size: 12)).foregroundStyle(muted).multilineTextAlignment(.center) }
            Text("Shared with your \(car)").font(.system(size: 12, weight: .semibold)).foregroundStyle(accent)
        case .notSent(let reason):
            Image(systemName: "exclamationmark.triangle.fill").font(.system(size: 30)).foregroundStyle(Self.amber)
            Text("NOT SENT").font(.system(size: 22, weight: .heavy)).foregroundStyle(text)
            Text(reason).font(.system(size: 13)).foregroundStyle(muted).multilineTextAlignment(.center)
        case .handedOff(let reason):
            Image(systemName: "arrow.up.forward.app.fill").font(.system(size: 30)).foregroundStyle(accent)
            Text("SAVED FOR nCTRL").font(.system(size: 20, weight: .heavy)).foregroundStyle(text)
            Text(reason).font(.system(size: 13)).foregroundStyle(muted).multilineTextAlignment(.center)
        }
    }
}

// The accent line sweeping along the card's bottom edge while it works, like the charging line on the Car screen.
private struct SweepLine: View {
    let color: Color
    var body: some View {
        TimelineView(.animation) { timeline in
            GeometryReader { geometry in
                let phase = timeline.date.timeIntervalSinceReferenceDate.truncatingRemainder(dividingBy: 1.4) / 1.4
                let width = geometry.size.width * 0.35
                Capsule().fill(color)
                    .frame(width: width, height: 3)
                    .offset(x: (geometry.size.width + width) * phase - width)
            }
            .frame(height: 3)
        }
        .padding(.bottom, 6)
    }
}
