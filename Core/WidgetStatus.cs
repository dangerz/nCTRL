using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

/// <summary>What the home-screen widget shows (status.json in the App Group), already formatted in the user's
/// units and app color. It holds no location, VIN, email or account details.</summary>
public sealed record WidgetStatus(string Name,int? Battery,string Range,string Cold,bool? Locked,bool? Charging,string Charge,
    DateTimeOffset Updated,string Accent,string Back,string Text,string Muted)
{
    public static WidgetStatus From(LiveVehicleState state,string name,UnitSystem units,ColdRangeSettings cold,double? outsideF,
        IReadOnlyDictionary<ThemeRole,string> theme,DateTimeOffset now,TimeZoneInfo zone)
    {
        string coldText=ColdRange.Estimate(state.RangeMiles,outsideF,cold) is double estimate&&outsideF is double f?
            "~"+Units.Distance(estimate,units)+" at "+Units.Temperature(f,units):"";
        string charge="";
        if(state.Charging==true&&ChargeEstimate.FullAt(state) is DateTimeOffset full&&full>now) {
            var local=TimeZoneInfo.ConvertTime(full,zone);
            charge="Full around "+local.ToString(local.Date==TimeZoneInfo.ConvertTime(now,zone).Date?"h:mm tt":"ddd h:mm tt",CultureInfo.InvariantCulture);
        }
        return new(name.Length>40?name[..40]:name,state.Battery,state.RangeMiles is double range?Units.Distance(range,units):"—",coldText,
            state.Locked,state.Charging,charge,state.RetrievedAt==default?now:state.RetrievedAt,
            theme[ThemeRole.Accent],theme[ThemeRole.Back],theme[ThemeRole.Text],theme[ThemeRole.Muted]);
    }
    public string Encode()
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",1);w.WriteString("name",Name);
            if(Battery is int battery)w.WriteNumber("battery",battery);
            w.WriteString("range",Range);w.WriteString("cold",Cold);
            if(Locked is bool locked)w.WriteBoolean("locked",locked);
            if(Charging is bool charging)w.WriteBoolean("charging",charging);
            w.WriteString("charge",Charge);
            // ISO 8601 without fractions, as the widget's decoder expects.
            w.WriteString("updated",Updated.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",CultureInfo.InvariantCulture));
            w.WriteString("accent",Accent);w.WriteString("back",Back);w.WriteString("text",Text);w.WriteString("muted",Muted);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
