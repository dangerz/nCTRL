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
    public override bool _SupportsPlatform(EditorExportPlatform platform)=>platform is EditorExportPlatformAndroid;
    public override string[] _GetAndroidLibraries(EditorExportPlatform platform,bool debug)
        =>new[]{"res://addons/ngarage_vault/NGarageVault.aar"};
}
#endif
