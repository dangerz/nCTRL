using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

public enum NoticeKind { Charge, Open, Battery12V, Tires }
/// <summary>A notification to show: one per thing that happened.</summary>
public sealed record Notice(NoticeKind Kind,string Title,string Body);

/// <summary>What the last check saw, so each alert fires once per occurrence instead of on every check.</summary>
public sealed record AlertMemory(bool Seen=false,bool Charging=false,bool PluggedIn=false,int? Battery=null,int? Limit=null,
    DateTimeOffset? OpenSince=null,bool OpenAlerted=false,bool Low12V=false,bool TireWarning=false,string Report="");

/// <summary>The alert settings and memory, kept in the App Group file "alerts.json" so the background check and the app
/// share them. Holds no login, location or VIN: only which alerts are on, the car's display name and what the last status
/// said about charging, doors, the 12-volt battery and tires.</summary>
public sealed record AlertFile(bool On=false,bool Charge=true,bool Open=true,bool Battery12V=true,bool Tires=true,string Car="",AlertMemory? Memory=null)
{
    public const string Name="alerts.json";
    public bool Allows(NoticeKind kind)=>On&&kind switch {NoticeKind.Charge=>Charge,NoticeKind.Open=>Open,NoticeKind.Battery12V=>Battery12V,_=>Tires};
    public string Encode()
    {
        var m=Memory??new AlertMemory();
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",1);
            w.WriteBoolean("on",On);w.WriteBoolean("charge",Charge);w.WriteBoolean("open",Open);w.WriteBoolean("battery12v",Battery12V);w.WriteBoolean("tires",Tires);
            w.WriteString("car",Car);
            w.WriteStartObject("memory");
            w.WriteBoolean("seen",m.Seen);w.WriteBoolean("charging",m.Charging);w.WriteBoolean("plugged",m.PluggedIn);
            if(m.Battery is int b)w.WriteNumber("battery",b);
            if(m.Limit is int l)w.WriteNumber("limit",l);
            if(m.OpenSince is DateTimeOffset since)w.WriteString("openSince",since.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture));
            w.WriteBoolean("openAlerted",m.OpenAlerted);w.WriteBoolean("low12v",m.Low12V);w.WriteBoolean("tire",m.TireWarning);w.WriteString("report",m.Report);
            w.WriteEndObject();w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    /// <summary>Damaged or missing files read as alerts off.</summary>
    public static AlertFile Decode(string json)
    {
        if(string.IsNullOrWhiteSpace(json)||json.Length>16*1024)return new();
        try {
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            if(r.GetProperty("version").GetInt32()!=1)return new();
            bool Flag(JsonElement e,string key,bool fallback)=>e.TryGetProperty(key,out var v)&&v.ValueKind is JsonValueKind.True or JsonValueKind.False?v.GetBoolean():fallback;
            int? Number(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out int n)?n:null;
            string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
            AlertMemory? memory=null;
            if(r.TryGetProperty("memory",out var m)&&m.ValueKind==JsonValueKind.Object) {
                DateTimeOffset? since=DateTimeOffset.TryParse(Text(m,"openSince"),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var s)?s:null;
                memory=new(Flag(m,"seen",false),Flag(m,"charging",false),Flag(m,"plugged",false),Number(m,"battery"),Number(m,"limit"),
                    since,Flag(m,"openAlerted",false),Flag(m,"low12v",false),Flag(m,"tire",false),Text(m,"report"));
            }
            string car=Text(r,"car");
            return new(Flag(r,"on",false),Flag(r,"charge",true),Flag(r,"open",true),Flag(r,"battery12v",true),Flag(r,"tires",true),
                car.Length>CarModels.MaxName?car[..CarModels.MaxName]:car,memory);
        } catch(Exception){return new();}
    }
}

/// <summary>Turns a new status into notifications by comparing it with what the last check saw. Unknown values never
/// alert. Runs in the background check and, silently, after each read in the app, so something already seen in the app
/// isn't announced again later.</summary>
public static class BackgroundAlerts
{
    /// <summary>Left unlocked or open for this long (from when a check first saw it) before an alert.</summary>
    public static readonly TimeSpan OpenGrace=TimeSpan.FromMinutes(10);
    /// <summary>The 12-volt alert fires below this, and can fire again only after the battery is back above <see cref="Ok12V"/>.</summary>
    public const int Low12VBelow=40,Ok12V=50;

    public static (AlertMemory Memory,IReadOnlyList<Notice> Notices) Evaluate(AlertMemory? previous,LiveVehicleState state,DateTimeOffset now,
        string car,bool atIgnoredPlace,Func<NoticeKind,bool> allowed)
    {
        var mem=previous??new AlertMemory();var notices=new List<Notice>();
        string name=car.Length>0?car:"Your car";
        bool charging=state.Charging==true,plugged=state.PluggedIn==true;
        int? limit=state.AcLimit??state.DcLimit??mem.Limit;
        // Charging: finished, reached the limit, stopped early, or unplugged.
        if(mem.Seen&&mem.Charging&&state.Charging==false&&allowed(NoticeKind.Charge)) {
            string pct=state.Battery is int b?$"{b}%":"";
            bool reached=state.Battery is int battery&&(limit is not int l||battery>=l-1);
            notices.Add(state.Battery==100?new(NoticeKind.Charge,$"{name} is fully charged","Battery at 100%."):
                reached?new(NoticeKind.Charge,$"{name} finished charging",$"Battery at {(pct.Length>0?pct:"its limit")}."):
                state.PluggedIn==true?new(NoticeKind.Charge,$"{name} stopped charging",$"Still plugged in{(pct.Length>0?" at "+pct:"")}{(limit is int lim?$", below the {lim}% limit":"")}."):
                new(NoticeKind.Charge,$"{name} was unplugged",$"Charging stopped{(pct.Length>0?" at "+pct:"")}."));
        }
        // Unlocked, or a door, window, tailgate or hood open, for ten minutes. Unlocked alone is fine at an ignored place.
        var open=Openings(state,atIgnoredPlace);
        DateTimeOffset? since=mem.OpenSince;bool openAlerted=mem.OpenAlerted;
        if(open.Count==0){since=null;openAlerted=false;}
        else {
            since??=now;
            if(!openAlerted&&now-since>=OpenGrace&&allowed(NoticeKind.Open)) {
                string what=string.Join(", ",open);
                notices.Add(new(NoticeKind.Open,state.Locked==false&&!atIgnoredPlace?$"{name} is unlocked":$"{name} is open",char.ToUpperInvariant(what[0])+what[1..]+"."));
                openAlerted=true;
            }
        }
        // 12-volt battery, with a gap between "low" and "fine again" so it doesn't flap around one value.
        bool low=mem.Low12V;
        if(state.Health?.Battery12V is int v12) {
            if(!low&&v12<Low12VBelow) {
                low=true;
                if(allowed(NoticeKind.Battery12V))notices.Add(new(NoticeKind.Battery12V,$"{name}'s 12-volt battery is low",$"At {v12}%. Driving, or charging the main battery, tops it up."));
            } else if(low&&v12>Ok12V)low=false;
        }
        bool tire=state.Health?.TireWarning==true;
        if(tire&&!mem.TireWarning&&allowed(NoticeKind.Tires))notices.Add(new(NoticeKind.Tires,$"{name}: check tire pressure","The tire pressure warning is on."));
        if(state.Health?.TireWarning==null)tire=mem.TireWarning;
        var next=new AlertMemory(true,state.Charging is bool c?c:mem.Charging,state.PluggedIn is bool p?p:mem.PluggedIn,state.Battery??mem.Battery,limit,
            since,openAlerted,low,tire,state.ReportedAt);
        return (next,notices);
    }
    private static List<string> Openings(LiveVehicleState state,bool atIgnoredPlace)
    {
        var open=new List<string>();var h=state.Health;
        if(state.Locked==false&&!atIgnoredPlace)open.Add("unlocked");
        if(h==null)return open;
        int doors=new[]{h.FrontLeftDoor,h.FrontRightDoor,h.RearLeftDoor,h.RearRightDoor}.Count(d=>d==true);
        if(doors>0)open.Add(doors==1?"a door open":$"{doors} doors open");
        int windows=new[]{h.FrontLeftWindow,h.FrontRightWindow,h.RearLeftWindow,h.RearRightWindow}.Count(d=>d==true);
        if(windows>0)open.Add(windows==1?"a window open":$"{windows} windows open");
        if(h.Tailgate==true)open.Add("tailgate open");
        if(h.Hood==true)open.Add("hood open");
        return open;
    }
}
