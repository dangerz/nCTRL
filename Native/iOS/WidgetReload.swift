import WidgetKit

// Called from NGarageVault.mm after nCTRL writes new status for its widget. WidgetKit has no Objective-C API.
@_cdecl("ng_widget_reload_swift")
public func ngWidgetReload() {
    WidgetCenter.shared.reloadAllTimelines()
}
