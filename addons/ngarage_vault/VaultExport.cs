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
        if(GetExportPlatform() is EditorExportPlatformIos)
            // HTTPS only (exempt encryption): skips the App Store Connect compliance question per upload.
            AddAppleEmbeddedPlatformPlistContent("<key>NSFaceIDUsageDescription</key><string>Use Face ID to unlock your saved Hyundai account in nCTRL on this device.</string>"+
                "<key>ITSAppUsesNonExemptEncryption</key><false/>");
    }
    public override string[] _GetAndroidLibraries(EditorExportPlatform platform,bool debug)
        =>new[]{"res://addons/ngarage_vault/NGarageVault.aar"};
}
#endif
