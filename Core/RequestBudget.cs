using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace NGarage.Core;

public enum RequestKind { SignIn, Read, Wake, Command, Location, Camera }
public sealed record RequestLimit(TimeSpan Cooldown,int DailyCap);
// Display snapshot. Cooldown and DailyCap already include any rate-limit tightening.
public sealed record RequestAllowance(RequestKind Kind,TimeSpan Cooldown,int DailyCap,int UsedToday,int Factor,DateTimeOffset TightenedUntil);

public sealed class RequestLimitException : BluelinkException
{
    public RequestKind Kind { get; }
    // False while Hyundai itself is rate limiting: sending anyway would only be refused.
    public bool Overridable { get; }
    public RequestLimitException(string message,RequestKind kind,bool overridable) : base(message) { Kind=kind;Overridable=overridable; }
}

public interface IRequestBudgetStore { string Load(); void Save(string data); }
public sealed class MemoryBudgetStore : IRequestBudgetStore
{
    private string data="";
    public string Load()=>data;
    public void Save(string value)=>data=value;
}

/// <summary>nCTRL's own request limits. Hyundai publishes no US quotas, so these are conservative app policy.
/// One budget is shared by every client and persisted: locking, relaunching or signing in again never resets it.
/// Holds only counts and timestamps, never account data.</summary>
public sealed class RequestBudget
{
    public static readonly IReadOnlyDictionary<RequestKind,RequestLimit> Defaults=new Dictionary<RequestKind,RequestLimit> {
        [RequestKind.SignIn]=new(TimeSpan.FromSeconds(60),20),
        [RequestKind.Read]=new(TimeSpan.FromSeconds(15),150),
        [RequestKind.Wake]=new(TimeSpan.FromMinutes(10),10),
        [RequestKind.Command]=new(TimeSpan.FromSeconds(30),30),
        [RequestKind.Location]=new(TimeSpan.FromHours(1),24),
        // Surround View wakes the car and runs its cameras.
        [RequestKind.Camera]=new(TimeSpan.FromMinutes(5),6)
    };
    public const int MaxFactor=8;
    private static readonly TimeSpan TightenFor=TimeSpan.FromHours(24),DefaultBackoff=TimeSpan.FromMinutes(5),
        MaxBackoff=TimeSpan.FromHours(24),OverrideWindow=TimeSpan.FromMinutes(2);
    private sealed class Entry { public DateTimeOffset Last;public int Count;public int Factor=1;public DateTimeOffset TightenedUntil; }

    private readonly IRequestBudgetStore store;
    private readonly TimeProvider clock;
    private readonly IReadOnlyDictionary<RequestKind,RequestLimit> limits;
    private readonly object sync=new();
    private readonly Dictionary<RequestKind,Entry> entries=new();
    private string day="";
    private DateTimeOffset blockedUntil;
    private RequestKind? overrideKind;
    private DateTimeOffset overrideUntil;

    public RequestBudget(IRequestBudgetStore store,TimeProvider? clock=null,IReadOnlyDictionary<RequestKind,RequestLimit>? limits=null)
    {
        this.store=store;this.clock=clock??TimeProvider.System;this.limits=limits??Defaults;
        foreach(RequestKind kind in Enum.GetValues<RequestKind>())entries[kind]=new Entry();
        Load();
    }

    public DateTimeOffset HyundaiBlockedUntil {get{lock(sync)return blockedUntil;}}
    public RequestAllowance Status(RequestKind kind)
    {
        lock(sync) {
            Roll();var e=entries[kind];var (cooldown,cap,factor)=Effective(kind);
            return new(kind,cooldown,cap,e.Count,factor,factor>1?e.TightenedUntil:default);
        }
    }
    public DateTimeOffset NextAllowed(RequestKind kind) {lock(sync)return entries[kind].Last+Effective(kind).Cooldown;}
    public bool CanAcquire(RequestKind kind,bool enforceCooldown=true) {lock(sync)return Refusal(kind,enforceCooldown,false)==null;}

    /// <summary>Consumes one request or throws <see cref="RequestLimitException"/> before anything is sent.
    /// enforceCooldown=false still counts toward the daily cap (session start and command tracking checks).</summary>
    public void Acquire(RequestKind kind,bool enforceCooldown=true,bool startsCooldown=true)
    {
        lock(sync) {
            var now=clock.GetUtcNow();
            bool overriding=overrideKind==kind&&now<overrideUntil;
            if(Refusal(kind,enforceCooldown,overriding) is RequestLimitException refusal)throw refusal;
            if(overriding)overrideKind=null;
            var e=entries[kind];e.Count++;
            if(startsCooldown)e.Last=now;
            Save();
        }
    }
    // One request of this kind may exceed nCTRL's own cooldown or daily cap. Never bypasses Hyundai rate limiting.
    public void AllowOnce(RequestKind kind) {lock(sync){overrideKind=kind;overrideUntil=clock.GetUtcNow()+OverrideWindow;}}
    public void ThrowIfHyundaiBlocked(string stage,RequestKind kind)
    {
        lock(sync)if(clock.GetUtcNow()<blockedUntil)throw new RequestLimitException(stage+": "+BlockedMessage(),kind,false);
    }
    // Hyundai answered 429: pause everything, then tighten this kind's limits for 24 hours (doubling, up to ×8).
    public void RecordRateLimited(RequestKind kind,TimeSpan? retryAfter)
    {
        lock(sync) {
            var now=clock.GetUtcNow();
            var wait=retryAfter is TimeSpan d&&d>TimeSpan.Zero?(d>MaxBackoff?MaxBackoff:d):DefaultBackoff;
            if(now+wait>blockedUntil)blockedUntil=now+wait;
            var e=entries[kind];
            e.Factor=now<e.TightenedUntil?Math.Min(MaxFactor,e.Factor*2):2;
            e.TightenedUntil=now+TightenFor;
            Save();
        }
    }

    private RequestLimitException? Refusal(RequestKind kind,bool enforceCooldown,bool overriding)
    {
        Roll();var now=clock.GetUtcNow();
        if(now<blockedUntil)return new(BlockedMessage(),kind,false);
        if(overriding)return null;
        var e=entries[kind];var (cooldown,cap,factor)=Effective(kind);
        string tightened=factor>1?$" Limits are tightened ×{factor} until {Time(e.TightenedUntil)} after Hyundai rate limiting.":"";
        if(e.Count>=cap)
            return new($"Daily limit reached: {e.Count} of {cap} {Label(kind)} used today. It resets at midnight.{tightened}",kind,true);
        if(enforceCooldown&&now<e.Last+cooldown)
            return new($"nCTRL allows one {Singular(kind)} every {Duration(cooldown)}. Next allowed in {Duration(e.Last+cooldown-now)}.{tightened}",kind,true);
        return null;
    }
    private (TimeSpan Cooldown,int Cap,int Factor) Effective(RequestKind kind)
    {
        var e=entries[kind];var limit=limits[kind];
        int factor=clock.GetUtcNow()<e.TightenedUntil?Math.Clamp(e.Factor,1,MaxFactor):1;
        return (limit.Cooldown*factor,Math.Max(1,limit.DailyCap/factor),factor);
    }
    private string BlockedMessage()=>$"Hyundai is rate limiting requests. Wait until {Time(blockedUntil)} before trying again. This cannot be overridden.";
    // Daily caps reset at local midnight.
    private void Roll()
    {
        string today=TimeZoneInfo.ConvertTime(clock.GetUtcNow(),clock.LocalTimeZone).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        if(today==day)return;
        day=today;foreach(var e in entries.Values)e.Count=0;
    }
    private string Time(DateTimeOffset at)=>TimeZoneInfo.ConvertTime(at,clock.LocalTimeZone).ToString("t",CultureInfo.CurrentCulture);

    public static string Label(RequestKind kind)=>kind switch {
        RequestKind.SignIn=>"sign-ins",RequestKind.Read=>"status reads",RequestKind.Wake=>"wake requests",
        RequestKind.Command=>"remote commands",RequestKind.Camera=>"camera captures",_=>"location requests"};
    public static string Singular(RequestKind kind)=>Label(kind).TrimEnd('s');
    public static string Duration(TimeSpan span)
    {
        int seconds=(int)Math.Ceiling(Math.Max(0,span.TotalSeconds));
        static string Unit(int n,string unit)=>n==1?"1 "+unit:n+" "+unit+"s";
        if(seconds<60)return Unit(seconds,"second");
        int minutes=(seconds+59)/60;
        if(minutes<60)return Unit(minutes,"minute");
        return minutes%60==0?Unit(minutes/60,"hour"):Unit(minutes/60,"hour")+" "+Unit(minutes%60,"minute");
    }

    // Stored state is untrusted: anything malformed resets to defaults, and timestamps
    // too far in the future (clock changes, tampering) are clamped.
    private void Load()
    {
        string text;
        try {text=store.Load();} catch(Exception) {return;}
        if(string.IsNullOrWhiteSpace(text)||text.Length>16384)return;
        try {
            using var doc=JsonDocument.Parse(text);var root=doc.RootElement;
            if(root.GetProperty("version").GetInt32()!=1)return;
            var now=clock.GetUtcNow();var latest=now+MaxBackoff;
            DateTimeOffset At(JsonElement e,string name)=>
                e.TryGetProperty(name,out var v)&&DateTimeOffset.TryParse(v.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var t)
                    ?(t>latest?latest:t):default;
            day=root.GetProperty("day").GetString()??"";
            blockedUntil=At(root,"blockedUntil");
            var kinds=root.GetProperty("kinds");
            foreach(RequestKind kind in Enum.GetValues<RequestKind>()) {
                if(!kinds.TryGetProperty(kind.ToString(),out var k))continue;
                var e=entries[kind];
                e.Last=At(k,"last");if(e.Last>now)e.Last=now;
                e.Count=Math.Max(0,k.GetProperty("count").GetInt32());
                e.Factor=Math.Clamp(k.GetProperty("factor").GetInt32(),1,MaxFactor);
                e.TightenedUntil=At(k,"tightenedUntil");
            }
        } catch(Exception) {
            day="";blockedUntil=default;
            foreach(RequestKind kind in Enum.GetValues<RequestKind>())entries[kind]=new Entry();
        }
    }
    private void Save()
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)) {
            w.WriteStartObject();w.WriteNumber("version",1);w.WriteString("day",day);
            w.WriteString("blockedUntil",blockedUntil.ToString("O",CultureInfo.InvariantCulture));
            w.WriteStartObject("kinds");
            foreach(var (kind,e) in entries) {
                w.WriteStartObject(kind.ToString());
                w.WriteString("last",e.Last.ToString("O",CultureInfo.InvariantCulture));w.WriteNumber("count",e.Count);
                w.WriteNumber("factor",e.Factor);w.WriteString("tightenedUntil",e.TightenedUntil.ToString("O",CultureInfo.InvariantCulture));
                w.WriteEndObject();
            }
            w.WriteEndObject();w.WriteEndObject();
        }
        // Limits keep working in memory if the device cannot write them.
        try {store.Save(Encoding.UTF8.GetString(stream.ToArray()));} catch(Exception) { }
    }
}
