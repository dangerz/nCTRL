using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NGarage.Core;

/// <summary>A share read as far as it can go without an address lookup: the place, or what to look up (keeping the
/// shared name), or why it can't be read.</summary>
public sealed record ShareResult(Destination? Place,string Lookup,string Name,string Problem);

/// <summary>Reading and sending places shared to nCTRL, used by the app (UI/SharedPlaces.cs) and by the Share
/// extension through the C entry points in Native/iOS/CoreLib, so both do exactly the same thing.</summary>
public static class ShareSender
{
    /// <summary>Opens a Google Maps short link if the share has nothing else to go on (resolveLink: ShortLinks.ResolveAsync).</summary>
    public static async Task<ShareResult> ReadAsync(SharedItem item,Func<string,Task<string?>> resolveLink)
    {
        var place=item.Place;string lookup=item.Lookup,name=item.Name,problem="";
        if(place==null&&item.ShortLink.Length>0) {
            string? full=null;
            try {full=await resolveLink(item.ShortLink);} catch(Exception) { }
            if(full!=null) {
                var (found,more)=Destinations.FromText(full);
                place=found;
                if(place!=null&&name.Length>0&&place.Name==Destinations.Format(place.Latitude,place.Longitude))place=place with {Name=name};
                if(place==null&&lookup.Length==0)lookup=more;
            } else if(lookup.Length==0)problem="Only a Google Maps short link, and it didn't lead to a place";
        }
        if(place==null&&lookup.Length==0&&problem.Length==0)problem="Nothing in it looked like a place";
        return new(place,place==null?lookup:"",name,place==null?problem:"");
    }

    // For the Share extension. In: share.json. Out: {"place":{…}} or {"lookup","name"} or {"problem"}, with source and kinds.
    public static async Task<string> ReadJsonAsync(string shareJson,HttpMessageHandler? links,CancellationToken ct)
    {
        var item=Destinations.FromShare(shareJson);
        var read=await ReadAsync(item,link=>ShortLinks.ResolveAsync(link,links,ct));
        return Json(w=>{
            if(read.Place is {} p)WritePlace(w,p);
            w.WriteString("lookup",read.Lookup);w.WriteString("name",read.Name);w.WriteString("problem",read.Problem);
            w.WriteString("source",item.Source);w.WriteString("kinds",item.Kinds);
        });
    }

    /// <summary>Sends a place to the car for the Share extension, with the app's saved login and session. In:
    /// {"account":&lt;saved login&gt;,"session":&lt;SharedSession&gt;|null,"place":{…},"limits":"&lt;limits file&gt;"}. Out:
    /// {"ok":true,"tracking":…,"session":…} or {"ok":false,"error":…}. One request, never retried; counts toward the same
    /// request limits as the app.</summary>
    public static async Task<string> SendAsync(string json,HttpMessageHandler? handler,CancellationToken ct)
    {
        SavedAccount account;SharedSession? session;Destination? place;string limits;
        try {
            using var doc=JsonDocument.Parse(json);var r=doc.RootElement;
            account=SavedAccount.Decode(r.GetProperty("account").GetString()??"");
            session=r.TryGetProperty("session",out var s)&&s.ValueKind==JsonValueKind.String?SharedSession.Decode(s.GetString()??""):null;
            place=r.TryGetProperty("place",out var p)?ReadPlace(p):null;
            limits=r.TryGetProperty("limits",out var l)&&l.ValueKind==JsonValueKind.String?l.GetString()??"":"";
        } catch(Exception){return Failed("nCTRL couldn't read your saved login. Open nCTRL to send it.");}
        // Only Hyundai USA can take a destination. Any other login stops here, before anything is sent.
        var service=Brand.ServiceFor(account.Service);
        if(!service.SendToCar||account.Service!=Brand.Hyundai)
            return Failed($"Send to car isn't available for {service.ServiceName}. Nothing was sent.");
        if(place==null||!Destinations.Valid(place))return Failed("That place has no valid location.");
        // A session is used only for the login it came from; one left over from another account or service is ignored.
        string owner=SharedSession.OwnerOf(account.Service,VehicleLink.AccountKey(account.Email));
        if(session!=null&&!session.BelongsTo(account.Service,VehicleLink.AccountKey(account.Email)))session=null;
        var budget=new RequestBudget(limits.Length>0?new PathBudgetStore(limits):new MemoryBudgetStore());
        using var client=handler==null?new HyundaiUsClient(budget):new HyundaiUsClient(handler,null,budget);
        try {
            if(session!=null)client.ResumeSession(account.Email,account.Password,account.Pin,session);
            else {
                // No session for this login yet: sign in, which works only when there's one car to choose.
                var cars=await client.LoginAsync(account.Email,account.Password,account.Pin,ct,implicitSignIn:true);
                if(cars.Count!=1)return Failed("Open nCTRL once so it knows which car to send to.");
                client.UseVehicle(cars[0]);
            }
            string? tracking=await client.SendDestinationAsync(place,ct);
            return Json(w=>{
                w.WriteBoolean("ok",true);w.WriteString("tracking",tracking??"");
                if(client.ExportSession() is {} now)w.WriteString("session",(now with {Owner=owner}).Encode());
            });
        }
        catch(RequestLimitException ex){return Failed(ex.Message);}
        catch(BluelinkException ex){return Failed(ex.Message);}
        catch(HttpRequestException){return Failed("Couldn't reach Hyundai. Nothing was retried.");}
        catch(OperationCanceledException){return Failed("Hyundai didn't answer in time. Open nCTRL to check.");}
        catch(Exception ex){return Failed("Send to car: "+SafeFailure.Describe(ex));}
    }
    public static void WritePlace(Utf8JsonWriter w,Destination p)
    {
        w.WriteStartObject("place");
        w.WriteString("name",p.Name);w.WriteString("address",p.Address);w.WriteString("zip",p.Zip);
        w.WriteNumber("lat",p.Latitude);w.WriteNumber("lon",p.Longitude);
        w.WriteEndObject();
    }
    public static Destination? ReadPlace(JsonElement p)
    {
        try {
            string Text(string key)=>p.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
            var place=new Destination(Destinations.Clean(Text("name"),Destinations.MaxName),Destinations.Clean(Text("address"),Destinations.MaxAddress),
                p.GetProperty("lat").GetDouble(),p.GetProperty("lon").GetDouble(),Destinations.Clean(Text("zip"),10));
            if(place.Name.Length==0)place=place with {Name=place.Address.Length>0?Destinations.Clean(place.Address,Destinations.MaxName):Destinations.Format(place.Latitude,place.Longitude)};
            return Destinations.Valid(place)?place:null;
        } catch(Exception){return null;}
    }
    private static string Failed(string error)=>Json(w=>{w.WriteBoolean("ok",false);w.WriteString("error",error);});
    private static string Json(Action<Utf8JsonWriter> fields)
    {
        using var stream=new MemoryStream();
        using(var w=new Utf8JsonWriter(stream)){w.WriteStartObject();fields(w);w.WriteEndObject();}
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
