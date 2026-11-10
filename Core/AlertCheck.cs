using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

/// <summary>The background alert check (iOS background refresh, Native/iOS/NGarageVault.mm). Reads the status the service
/// already has (never wakes the car), compares it with the last check and says what to notify. Uses the saved login and
/// the session shared with the Share extension, and counts toward the same request limits as the app.</summary>
public static class AlertCheck
{
    /// <summary>Stored-status reads left for you: the check skips itself rather than use these up.</summary>
    public const int ReadsKeptForYou=30;
    /// <summary>Background sign-ins and session renewals allowed a day. Renewals in the app aren't counted here.</summary>
    public const int SignInsPerDay=4;
    public const string LimitsFile="request-limits.json";
    public const string PausedTitle="Alerts are paused";

    /// <summary>In: {"account":&lt;saved login&gt;,"session":&lt;SharedSession&gt;|null,"places":&lt;ignored places&gt;,"folder":&lt;App Group path&gt;}.
    /// Out: {"ok":true,"notices":[{"kind","title","body"}],"session":…} or {"ok":false,"skipped":why}. Reads and writes
    /// alerts.json and the request limits in the folder. Never sends a command; never asks for a one-time code.</summary>
    public static async Task<string> CheckJsonAsync(string json,HttpMessageHandler? handler,CancellationToken ct,DateTimeOffset? clock=null)
    {
        var now=clock??DateTimeOffset.UtcNow;
        SavedAccount account;SharedSession? session;IReadOnlyList<IgnoredPlace> places;string folder;
        try {
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            account=SavedAccount.Decode(r.GetProperty("account").GetString()??"");
            session=r.TryGetProperty("session",out var s)&&s.ValueKind==JsonValueKind.String?SharedSession.Decode(s.GetString()??""):null;
            places=r.TryGetProperty("places",out var p)&&p.ValueKind==JsonValueKind.String?IgnoredPlaces.Decode(p.GetString()??""):Array.Empty<IgnoredPlace>();
            folder=r.GetProperty("folder").GetString()??"";
        } catch(Exception){return Skipped("no saved login");}
        if(folder.Length==0||!Directory.Exists(folder))return Skipped("no shared folder");
        string alertsPath=Path.Combine(folder,AlertFile.Name);
        var file=AlertFile.Decode(File.Exists(alertsPath)?File.ReadAllText(alertsPath):"");
        if(!file.On)return Skipped("alerts are off");
        if(file.Paused)return Skipped($"{Brand.ServiceFor(account.Service).Maker} refused the saved login, so checks wait until you sign in again in {Brand.App}");
        var budget=new RequestBudget(new PathBudgetStore(Path.Combine(folder,LimitsFile)));
        var reads=budget.Status(RequestKind.Read);
        if(reads.UsedToday>=reads.DailyCap-ReadsKeptForYou)return Skipped("keeping the rest of today's reads for you");
        string key=VehicleLink.AccountKey(account.Email);
        if(session!=null&&!session.BelongsTo(account.Service,key))session=null;
        // A sign-in (no session) or a renewal (the clients renew within a minute of expiry) sends the saved password:
        // counted before it's sent, so a run that fails part-way still counts.
        if(session==null||now>=session.Expires.AddSeconds(-60)) {
            string today=now.UtcDateTime.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
            int used=file.SignInDay==today?file.SignIns:0;
            if(used>=SignInsPerDay)return Skipped("background sign-ins are used up for today");
            file=file with {SignInDay=today,SignIns=used+1};
            Save(alertsPath,file);
        }
        using var client=Brand.ConnectFor(account.Service,budget,handler);
        if(client is IOneTimeCodeSignIn codes&&account.RememberToken.Length>0){codes.RememberToken=account.RememberToken;if(account.DeviceId.Length>0)codes.DeviceId=account.DeviceId;}
        try {
            if(session!=null)client.ResumeSession(account.Email,account.Password,account.Pin,session);
            else {
                // No session for this login: one quiet sign-in, which works only with a single car on the account.
                var cars=await client.LoginAsync(account.Email,account.Password,account.Pin,ct,implicitSignIn:true);
                if(cars.Count!=1)return Skipped("open the app once to pick the car");
                client.UseVehicle(cars[0]);
            }
            var state=await client.ReadAsync(false,ct);
            bool ignored=IgnoredPlaces.Match(places,state.StatusLocation)!=null;
            var (memory,notices)=BackgroundAlerts.Evaluate(file.Memory,state,now,file.Car,ignored,file.Allows);
            Save(alertsPath,file with {Memory=memory});
            return Json(w=>{
                w.WriteBoolean("ok",true);
                w.WriteStartArray("notices");
                foreach(var n in notices){w.WriteStartObject();w.WriteString("kind",n.Kind.ToString());w.WriteString("title",n.Title);w.WriteString("body",n.Body);w.WriteEndObject();}
                w.WriteEndArray();
                if(client.ExportSession() is {} now2)w.WriteString("session",(now2 with {Owner=SharedSession.OwnerOf(account.Service,key)}).Encode());
            });
        }
        catch(OneTimeCodeRequiredException){return Pause(alertsPath,file,account.Service,"wants a one-time code");}
        catch(RequestLimitException){return Skipped("request limit");}
        catch(BluelinkException e) when(Refused(e)){return Pause(alertsPath,file,account.Service,"refused the saved login");}
        catch(BluelinkException){return Skipped("the service refused");}
        catch(HttpRequestException){return Skipped("no connection");}
        catch(OperationCanceledException){return Skipped("timed out");}
        catch(Exception){return Skipped("failed");}
    }
    /// <summary>The login itself was refused (wrong password, locked account, session ended, a check the app can't do), as
    /// opposed to the service being down or busy.</summary>
    public static bool Refused(BluelinkException e)=>
        e.HttpStatus is 401 or 403||e.ApiErrorCode is 7401 or 7402 or 7404 or 7435 or 7530 or 7531 or 7532 or 7549 or 7603||
        RefusedTexts.Any(t=>e.Message.Contains(t,StringComparison.Ordinal));
    private static readonly string[] RefusedTexts={"didn't accept that email","locked this account","Sign in again","Sign in to ","can't complete"};
    // Stops background checks until a sign-in in the app (Resume), with one notification saying so.
    private static string Pause(string alertsPath,AlertFile file,string service,string what)
    {
        Save(alertsPath,file with {Paused=true});
        string body=$"{Brand.ServiceFor(service).Maker} {what}. Open {Brand.App} to sign in again.";
        return Json(w=>{
            w.WriteBoolean("ok",false);w.WriteString("skipped",$"{Brand.ServiceFor(service).Maker} {what}; checks are paused until you sign in again");
            w.WriteStartArray("notices");
            w.WriteStartObject();w.WriteString("kind",NoticeKind.SignIn.ToString());w.WriteString("title",PausedTitle);w.WriteString("body",body);w.WriteEndObject();
            w.WriteEndArray();
        });
    }
    /// <summary>For the app, after a sign-in with the password: background checks start again.</summary>
    public static void Resume(string folder)
    {
        string path=Path.Combine(folder,AlertFile.Name);
        if(!File.Exists(path))return;
        var file=AlertFile.Decode(File.ReadAllText(path));
        if(file.Paused||file.SignIns>0)Save(path,file with {Paused=false,SignInDay="",SignIns=0});
    }
    /// <summary>For the app: updates the memory from a status it just read, without notifying, so the next background check
    /// doesn't announce what you've already seen.</summary>
    public static void Remember(string folder,LiveVehicleState state,bool atIgnoredPlace,string car,DateTimeOffset now)
    {
        string path=Path.Combine(folder,AlertFile.Name);
        if(!File.Exists(path))return;
        var file=AlertFile.Decode(File.ReadAllText(path));
        if(!file.On)return;
        var (memory,_)=BackgroundAlerts.Evaluate(file.Memory,state,now,car,atIgnoredPlace,_=>false);
        Save(path,file with {Memory=memory,Car=car});
    }
    private static void Save(string path,AlertFile file)
    {
        string temporary=path+".tmp";
        File.WriteAllText(temporary,file.Encode());File.Move(temporary,path,true);
    }
    private static string Skipped(string why)=>Json(w=>{w.WriteBoolean("ok",false);w.WriteString("skipped",why);});
    private static string Json(Action<Utf8JsonWriter> fields)
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)){w.WriteStartObject();fields(w);w.WriteEndObject();}
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
