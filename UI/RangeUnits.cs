using Godot;
using NGarage.Core;
using System;
using System.Threading.Tasks;

// Range & units: metric or imperial display, and the cold-weather range estimate. The outside temperature
// comes from Open-Meteo for the car's last known location, rounded to about 11 km; it is kept in memory only.
public partial class Main
{
    private UnitSystem units=UnitSystem.Imperial;
    private ColdRangeSettings coldRange=ColdRangeSettings.Default;
    private OutsideTemperature weather=null!;
    private OutsideReading? outside;
    // The last weather request (successful or not), so a failure isn't retried for 10 minutes.
    private DateTimeOffset outsideTried;private string outsideArea="";
    private bool outsideBusy;

    // Called after launch, each request, resume and turning the estimate on. At most one request per
    // area every 30 minutes; nothing at all while the estimate is off.
    private async Task UpdateOutsideTemperature()
    {
        if(!coldRange.Enabled||outsideBusy||!liveMode||liveBusy)return;
        var now=DateTimeOffset.UtcNow;
        if(demo){outside=new(DemoHyundai.OutsideF,now,"demo");return;}
        int version=liveEpoch;outsideBusy=true;
        try {
            if(await OldLocation(version) is not {} at||version!=liveEpoch)return;
            string area=OutsideTemperature.Area(at);
            if(outside?.Area==area&&now-outside.RetrievedAt<OutsideTemperature.Fresh)return;
            if(outsideArea==area&&now-outsideTried<OutsideTemperature.AfterFailure)return;
            outsideTried=now;outsideArea=area;
            var reading=await weather.ReadAsync(at,now,liveLifetime.Token);
            if(version!=liveEpoch||!coldRange.Enabled)return;
            outside=reading;
            if(IsInsideTree()&&!liveBusy&&screen is "Car" or "Range")Render();
        }
        catch(Exception) { } // No estimate this time.
        finally {outsideBusy=false;}
    }
    private void ForgetOutside(){outside=null;outsideArea="";outsideTried=default;}

    // Under the range on the Car screen: a snowflake and the cold-weather estimate.
    private void ColdRangeLine(LiveVehicleState? s)
    {
        if(outside is not {} o||ColdRange.Estimate(s?.RangeMiles,o.TemperatureF,coldRange) is not double cold)return;
        canvas.AddChild(new Glyph {Name="ColdIcon",Kind="Cold",Ink=Blue,Position=new(290,440),Size=new(14,14)});
        Label(308,437,130,20,"~"+Units.Distance(cold,units)+" at "+Units.Temperature(o.TemperatureF,units),11,Blue,true).Name="ColdRange";
    }

    private void RangeScreen()
    {
        Title("RANGE & UNITS.","Saved on this device. Hyundai always gets miles, °F and psi.");
        Label(24,192,412,22,"UNITS",11,Muted,true);
        foreach(var (system,x,text) in new[]{(UnitSystem.Imperial,24,"Imperial  ·  mi, °F, psi"),(UnitSystem.Metric,238,"Metric  ·  km, °C, kPa")}) {
            var b=Button(x,216,198,46,text,13,()=>{units=system;SavePrefs();Render();},false,"Units"+system);
            if(units==system)b.AddThemeStyleboxOverride("normal",Style(Skin.Selected,Blue,2));
        }
        Label(24,278,412,22,"COLD-WEATHER RANGE",11,Muted,true);
        var on=new CheckBox {Name="ColdRangeOn",Position=new(24,300),Size=new(412,40),Text="Estimate range in cold weather",ButtonPressed=coldRange.Enabled};
        on.AddThemeFontOverride("font",regular);on.AddThemeFontSizeOverride("font_size",13);
        on.Toggled+=value=>{
            coldRange=coldRange with {Enabled=value};SavePrefs();
            if(value)_ = UpdateOutsideTemperature();else ForgetOutside();
            Callable.From(Render).CallDeferred();
        };
        canvas.AddChild(on);
        bool enabled=coldRange.Enabled;
        Stepper(348,"Range starts dropping below",Units.Temperature(coldRange.StartF,units),"ColdStart",enabled,
            d=>coldRange with {StartF=Units.Step(coldRange.StartF,d,units)});
        Stepper(402,"Coldest temperature",Units.Temperature(coldRange.ColdestF,units),"ColdLow",enabled,
            d=>coldRange with {ColdestF=Units.Step(coldRange.ColdestF,d,units)});
        Stepper(456,"Range lost at the coldest",coldRange.LossPercent+"%","ColdLoss",enabled,
            d=>coldRange with {LossPercent=coldRange.LossPercent+5*d});
        double range=CurrentState?.RangeMiles??200;
        string example;
        if(!enabled)example="Off. Only the range the car reports is shown.";
        else if(outside is {} o)example="Now "+Units.Temperature(o.TemperatureF,units)+" near the car: "+(ColdRange.Estimate(range,o.TemperatureF,coldRange) is double now?
            Units.Distance(range,units)+" shows as about "+Units.Distance(now,units)+".":"warm enough that the range isn't reduced.");
        else {
            double middle=(coldRange.StartF+coldRange.ColdestF)/2;
            example="Example: at "+Units.Temperature(middle,units)+", "+Units.Distance(range,units)+" shows as about "+Units.Distance(range*ColdRange.Factor(middle,coldRange),units)+".";
        }
        Label(24,512,412,44,example,13,White,true,true).Name="ColdExample";
        Label(24,560,412,96,!enabled?"No weather requests are sent while this is off.":demo?"Demo: a pretend "+Units.Temperature(DemoHyundai.OutsideF,units)+". No weather is requested.":
            (outside is {} r?"Temperature from Open-Meteo, "+LastLocation.Age(r.RetrievedAt,DateTimeOffset.UtcNow)+". ":"No temperature yet: it needs the car's location from an update. ")+
            "For the temperature, the car's location, rounded to about 11 km (7 miles), is sent to Open-Meteo, at most every 30 minutes. A guide only: the car's own estimate learns from your driving.",
            11,Muted,false,true).Name="ColdStatus";
        // Open-Meteo's data is CC BY 4.0, so it is credited with a link.
        if(enabled&&!demo)Button(24,662,412,28,"Weather data by Open-Meteo.com",11,()=>OS.ShellOpen("https://open-meteo.com/"),false,"OpenMeteoCredit");
        Button(24,718,412,46,"Back to settings",13,()=>Navigate("Settings"),false,"RangeBack");
    }
    private void Stepper(int y,string text,string value,string name,bool enabled,Func<int,ColdRangeSettings> change)
    {
        Label(24,y,220,46,text,13,enabled?White:Muted);
        var shown=Label(296,y,98,46,value,16,enabled?White:Muted,true);shown.Name=name+"Value";shown.HorizontalAlignment=HorizontalAlignment.Center;
        foreach(var (x,label,direction) in new[]{(248,"−",-1),(390,"+",1)}) {
            var b=Button(x,y,46,46,label,22,()=>{coldRange=change(direction).Valid();SavePrefs();Render();},false,name+(direction<0?"Down":"Up"));
            b.Disabled=!enabled;
        }
    }
}
