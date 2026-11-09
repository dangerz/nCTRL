#if TOOLS
using Godot;
[Tool]
public partial class VaultExport : EditorPlugin
{
    private VaultAndroidExport? exporter;
    public override void _EnterTree(){exporter=new VaultAndroidExport();AddExportPlugin(exporter);}
    public override void _ExitTree(){if(exporter!=null)RemoveExportPlugin(exporter);exporter=null;}
}
[Tool]
public partial class VaultAndroidExport : EditorExportPlugin
{
    public override string _GetName()=>"NGarageVault";
    public override bool _SupportsPlatform(EditorExportPlatform platform)=>platform is EditorExportPlatformAndroid or EditorExportPlatformIos;
    public override void _ExportBegin(string[] features,bool isDebug,string path,uint flags)
    {
        // Only keys Godot's iOS template lacks: it already sets ITSAppUsesNonExemptEncryption=false, and
        // repeating a template key here dropped this plugin's whole fragment from the exported Info.plist.
        if(GetExportPlatform() is EditorExportPlatformIos)
        {
            // gtCTRL exports carry the "gtctrl" feature tag (see Core/Brand.cs).
            var flavor=System.Array.IndexOf(features,"gtctrl")>=0?NGarage.Core.Brand.GtCtrl:NGarage.Core.Brand.NCtrl;
            string app=flavor.App,scheme=flavor.Id;
            AddAppleEmbeddedPlatformPlistContent($"<key>NSFaceIDUsageDescription</key><string>Use Face ID to confirm the risk disclaimer and limit overrides in {app}.</string>");
            // nctrl:// (gtctrl:// for gtCTRL) opens the app: from the widget (Car screen) and from the Share extension (Send to car).
            AddAppleEmbeddedPlatformPlistContent($"<key>CFBundleURLTypes</key><array><dict><key>CFBundleURLName</key><string>{scheme}</string><key>CFBundleURLSchemes</key><array><string>{scheme}</string></array></dict></array>");
            // While the map is open, to show where you are next to the car. Used on the phone only.
            AddAppleEmbeddedPlatformPlistContent($"<key>NSLocationWhenInUseUsageDescription</key><string>Show where you are on {app}'s car map. Your location stays on this iPhone.</string>");
            // Add-only photo access, for saving Surround View pictures you choose to keep.
            AddAppleEmbeddedPlatformPlistContent("<key>NSPhotoLibraryAddUsageDescription</key><string>Save the Surround View pictures you choose to your photo library.</string>");
            // Alerts when closed: iOS background refresh runs the alert check (Native/iOS/NGarageVault.mm, Core/AlertCheck.cs).
            AddAppleEmbeddedPlatformPlistContent("<key>UIBackgroundModes</key><array><string>fetch</string></array>");
            AddAppleEmbeddedPlatformPlistContent("<key>BGTaskSchedulerPermittedIdentifiers</key><array><string>com.ngarage.nctrl.alerts</string></array>");
        }
    }
    public override string[] _GetAndroidLibraries(EditorExportPlatform platform,bool debug)
        =>new[]{"res://addons/ngarage_vault/NGarageVault.aar"};
}
#endif
