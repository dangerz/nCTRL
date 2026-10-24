using Godot;
using NGarage.Core;
using System.Linq;
using System.Text.Json;

public partial class Main
{
    // Tires and every field from the latest status read in this session (memory only).
    private void LiveDetails()
    {
        var raw=connection?.RawStatus;
        Title("VEHICLE DETAILS.",raw==null?"Tires and everything Hyundai reports":Updated(connection?.State?.RetrievedAt)+" · everything Hyundai reports");
        if(raw is not JsonElement status) {
            Label(24,206,412,96,"Tap Update to load tire pressures and every status field Hyundai reports."+
                (NeedsFaceId?" "+vault.BiometricName+" is required for the first request.":""),14,White,false,true);
        } else {
            Label(24,188,412,22,"TIRES",11,Muted,true);
            var tires=StatusDetails.Tires(status);
            for(int i=0;i<tires.Count;i++) {
                var tire=tires[i];int x=24+(i%2)*214,y=214+(i/2)*62;bool low=tire.Warning==true;
                Panel(x,y,198,54,Skin.Card,false);Rect(x,y,3,54,low?Red:Blue);
                Label(x+14,y+4,180,20,tire.Position,11,Muted,true);
                Label(x+14,y+24,180,26,(tire.Psi is double psi?Units.Pressure(psi,units):"—")+(tire.Warning switch {true=>" · Low",false=>" · OK",_=>""}),15,low?Red:White,true)
                    .Name="Tire"+i;
            }
            if(tires.All(t=>t.Psi==null&&t.Warning==null))
                Label(24,214,412,116,"Hyundai did not report tire data for this vehicle.",13,Muted,false,true);
            Label(24,342,412,22,"FULL DETAIL",11,Muted,true);
            Panel(24,366,412,346,Skin.Card,false);
            var scroll=new ScrollContainer {Name="DetailScroll",Position=new(32,372),Size=new(398,334),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
            var list=new VBoxContainer {CustomMinimumSize=new(382,0)};
            list.AddThemeConstantOverride("separation",6);
            foreach(var row in StatusDetails.Rows(status,units)) {
                var line=new HBoxContainer();
                var name=new Label {Text=row.Label,AutowrapMode=TextServer.AutowrapMode.Arbitrary,CustomMinimumSize=new(232,0),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
                name.AddThemeFontOverride("font",regular);name.AddThemeFontSizeOverride("font_size",11);name.AddThemeColorOverride("font_color",Muted);
                var value=new Label {Text=row.Value,AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new(144,0),HorizontalAlignment=HorizontalAlignment.Right};
                value.AddThemeFontOverride("font",bold);value.AddThemeFontSizeOverride("font_size",12);value.AddThemeColorOverride("font_color",White);
                line.AddChild(name);line.AddChild(value);list.AddChild(line);
            }
            scroll.AddChild(list);canvas.AddChild(scroll);
        }
        LiveButton(24,722,412,46,"Update",13,()=>_ = RefreshLive(false),false,"DetailsUpdate");
    }
}
