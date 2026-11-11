using Godot;
using NGarage.Core;
using System.Linq;
using System.Text.Json;

public partial class Main
{
    // Details: plain names and values, or the service's raw field names (kept in preferences).
    private bool detailsReadable=true;
    // Tires and every field from the latest status read in this session (memory only).
    private void LiveDetails()
    {
        var raw=connection?.RawStatus;
        Title("VEHICLE DETAILS.",raw==null?$"Everything {Brand.Maker} reports":Updated(connection?.State?.RetrievedAt)+$" · everything {Brand.Maker} reports");
        if(raw is not JsonElement status) {
            Label(24,206,412,96,$"Tap Update to load every status field {Brand.Maker} reports.",14,White,false,true);
        } else {
            // Tires only when the service reports per-tire readings (Hyundai does; Kia Connect US sends only the
            // overall tire warning, which Check your car shows). Without them, the full detail takes the space.
            var tires=StatusDetails.Tires(status);
            bool hasTires=tires.Any(t=>t.Psi!=null||t.Warning!=null);
            if(hasTires) {
                Label(24,188,412,22,"TIRES",11,Muted,true);
                for(int i=0;i<tires.Count;i++) {
                    var tire=tires[i];int x=24+(i%2)*214,y=214+(i/2)*62;bool low=tire.Warning==true;
                    Panel(x,y,198,54,Skin.Card,false);Rect(x,y,3,54,low?Red:Blue);
                    Label(x+14,y+4,180,20,tire.Position,11,Muted,true);
                    Label(x+14,y+24,180,26,(tire.Psi is double psi?Units.Pressure(psi,units):"—")+(tire.Warning switch {true=>" · Low",false=>" · OK",_=>""}),15,low?Red:White,true)
                        .Name="Tire"+i;
                }
            }
            int top=hasTires?342:188;
            Label(24,top,200,22,"FULL DETAIL",11,Muted,true);
            var readable=new CheckBox {Name="DetailsReadable",Position=new(236,top-9),Size=new(200,36),Text="Readable names",ButtonPressed=detailsReadable};
            readable.AddThemeFontOverride("font",regular);readable.AddThemeFontSizeOverride("font_size",12);
            readable.Toggled+=on=>{detailsReadable=on;SavePrefs();Callable.From(Render).CallDeferred();};
            canvas.AddChild(readable);
            Panel(24,top+24,412,712-top-24,Skin.Card,false);
            var scroll=new ScrollContainer {Name="DetailScroll",Position=new(32,top+30),Size=new(398,712-top-36),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
            var list=new VBoxContainer {CustomMinimumSize=new(382,0)};
            list.AddThemeConstantOverride("separation",6);
            foreach(var row in StatusDetails.Rows(status,units,detailsReadable)) {
                var line=new HBoxContainer();
                var name=new Label {Text=row.Label,AutowrapMode=TextServer.AutowrapMode.Arbitrary,CustomMinimumSize=new(232,0),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
                name.AddThemeFontOverride("font",regular);name.AddThemeFontSizeOverride("font_size",11);name.AddThemeColorOverride("font_color",Muted);
                var value=new Label {Text=row.Value,AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new(144,0),HorizontalAlignment=HorizontalAlignment.Right};
                value.AddThemeFontOverride("font",bold);value.AddThemeFontSizeOverride("font_size",12);value.AddThemeColorOverride("font_color",White);
                line.AddChild(name);line.AddChild(value);list.AddChild(line);
            }
            scroll.AddChild(list);canvas.AddChild(scroll);
        }
        if(raw is JsonElement report) {
            Button(24,722,198,46,"Copy for a bug report",12,()=>CopyStatusReport(report),false,"DetailsCopy");
            LiveButton(238,722,198,46,"Update",13,()=>_ = RefreshLive(false),false,"DetailsUpdate");
        } else LiveButton(24,722,412,46,"Update",13,()=>_ = RefreshLive(false),false,"DetailsUpdate");
    }
    // The last report copied, for the smoke test (the headless clipboard may not exist).
    private string copiedReport="";
    private void CopyStatusReport(JsonElement status)
    {
        string version=ProjectSettings.GetSetting("application/config/version").AsString();
        copiedReport=StatusReport.Build(status,Brand.App,version.Length>0?version:"(unknown version)",AppService.ServiceName);
        DisplayServer.ClipboardSet(copiedReport);
        notice="Copied, with your name, contact details, location, IDs and the VIN's serial number removed. Paste it into the bug report.";noticeError=false;Render();
    }
}
