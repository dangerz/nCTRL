import WidgetKit
import SwiftUI

// nCTRL's home-screen and lock-screen widget: the car's status from the app's last update. The app asks for a
// redraw whenever it writes new status; the widget itself never contacts Hyundai.
struct StatusEntry: TimelineEntry {
    let date: Date
    let status: WidgetStatus?
    let picture: CGImage?
}

struct StatusProvider: TimelineProvider {
    func placeholder(in context: Context) -> StatusEntry { StatusEntry(date: Date(), status: .sample, picture: nil) }
    func getSnapshot(in context: Context, completion: @escaping (StatusEntry) -> Void) {
        let entry = load()
        completion(context.isPreview && entry.status == nil ? placeholder(in: context) : entry)
    }
    func getTimeline(in context: Context, completion: @escaping (Timeline<StatusEntry>) -> Void) {
        completion(Timeline(entries: [load()], policy: .never))
    }
    private func load() -> StatusEntry {
        StatusEntry(date: Date(), status: WidgetStatus.load(from: NctrlGroup.file("status.json")), picture: loadPicture(NctrlGroup.file("car.png")))
    }
}

struct StatusEntryView: View {
    @Environment(\.widgetFamily) private var family
    let entry: StatusEntry
    var body: some View {
        StatusView(status: entry.status, picture: entry.picture, layout: layout)
            .widgetURL(URL(string: "nctrl://car"))
            .containerBackground(for: .widget) {
                if family == .systemSmall || family == .systemMedium { Color(hex: entry.status?.back ?? "08121d") } else { AccessoryWidgetBackground() }
            }
    }
    private var layout: StatusLayout {
        switch family {
        case .systemMedium: return .medium
        case .accessoryRectangular: return .rectangular
        case .accessoryCircular: return .circular
        default: return .small
        }
    }
}

struct NctrlStatusWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "NctrlStatus", provider: StatusProvider()) { entry in StatusEntryView(entry: entry) }
            .configurationDisplayName("Car status")
            .description("Battery, range and locks from nCTRL's last update.")
            .supportedFamilies([.systemSmall, .systemMedium, .accessoryRectangular, .accessoryCircular])
    }
}

@main
struct NctrlWidgets: WidgetBundle {
    var body: some Widget { NctrlStatusWidget() }
}
