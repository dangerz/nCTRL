using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NGarage.Core;

static class RequestBudgetTests
{
    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now=new(2026,10,23,9,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>Now;
        public override TimeZoneInfo LocalTimeZone=>TimeZoneInfo.Utc;
    }
    // Answers every Hyundai route with a fixed reply; counts what was actually sent.
    sealed class Hyundai : HttpMessageHandler
    {
        public int Sent;public HttpStatusCode Next=HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
        {
            Sent++;string path=r.RequestUri!.AbsolutePath;
            string body=path.EndsWith("/oauth/token")?"{\"access_token\":\"a\",\"refresh_token\":\"r\",\"expires_in\":86400}":
                path.Contains("/enrollment/")?"{\"enrolledVehicleDetails\":[{\"vehicleDetails\":{\"regid\":\"reg-1\",\"vin\":\"TESTVIN0000000001\",\"nickName\":\"Test\",\"modelCode\":\"IONIQ5N\",\"vehicleGeneration\":\"3\",\"evStatus\":\"E\"}}]}":
                path.EndsWith("/findMyCar")?"{\"coord\":{\"lat\":1,\"lon\":2},\"time\":\"t\"}":
                "{\"vehicleStatus\":{\"dateTime\":\"2026-10-22T10:00:00Z\",\"doorLock\":true}}";
            var status=Next;Next=HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(status==HttpStatusCode.OK?body:"",Encoding.UTF8,"application/json")});
        }
    }
    static int count;
    static void Check(bool ok,string name){if(!ok)throw new Exception("Request limits: "+name);count++;}
    static RequestLimitException Refused(Action action,string name)
    {
        try{action();}catch(RequestLimitException e){count++;return e;}
        throw new Exception("Request limits: expected refusal: "+name);
    }
    static async Task<RequestLimitException> RefusedAsync(Func<Task> action,string name)
    {
        try{await action();}catch(RequestLimitException e){count++;return e;}
        throw new Exception("Request limits: expected refusal: "+name);
    }
    public static async Task Run()
    {
        var d=RequestBudget.Defaults;
        Check(d[RequestKind.SignIn]==new RequestLimit(TimeSpan.FromSeconds(60),20)&&d[RequestKind.Read]==new RequestLimit(TimeSpan.FromSeconds(15),150)&&
            d[RequestKind.Wake]==new RequestLimit(TimeSpan.FromMinutes(10),10)&&d[RequestKind.Command]==new RequestLimit(TimeSpan.FromSeconds(30),30)&&
            d[RequestKind.Location]==new RequestLimit(TimeSpan.FromHours(1),24),"Documented default limits");

        var clock=new Clock();var store=new MemoryBudgetStore();var budget=new RequestBudget(store,clock);
        budget.Acquire(RequestKind.Read);
        var cooldown=Refused(()=>budget.Acquire(RequestKind.Read),"Read cooldown");
        Check(cooldown.Overridable&&cooldown.Kind==RequestKind.Read&&cooldown.Message.Contains("15 seconds"),"Cooldown refusal is overridable and explains the wait");
        Check(budget.CanAcquire(RequestKind.Command)&&budget.CanAcquire(RequestKind.Read,enforceCooldown:false),"Kinds are independent; cooldown-exempt reads still allowed");
        clock.Now+=TimeSpan.FromSeconds(15);budget.Acquire(RequestKind.Read);Check(budget.Status(RequestKind.Read).UsedToday==2,"Allowed after cooldown and counted");

        budget.AllowOnce(RequestKind.Read);budget.Acquire(RequestKind.Read);
        Check(budget.Status(RequestKind.Read).UsedToday==3,"Override sends and still counts");
        Refused(()=>budget.Acquire(RequestKind.Read),"Override covers exactly one request");
        budget.AllowOnce(RequestKind.Wake);Refused(()=>budget.Acquire(RequestKind.Read),"Override applies only to its own kind");
        budget.AllowOnce(RequestKind.Read);clock.Now+=TimeSpan.FromMinutes(3);budget.Acquire(RequestKind.Read);clock.Now+=TimeSpan.FromSeconds(1);
        Refused(()=>budget.Acquire(RequestKind.Read),"Unused override expires");

        for(int i=0;i<10;i++){budget.Acquire(RequestKind.Wake);clock.Now+=TimeSpan.FromMinutes(10);}
        var daily=Refused(()=>budget.Acquire(RequestKind.Wake),"Wake daily cap");
        Check(daily.Overridable&&daily.Message.Contains("10 of 10")&&daily.Message.Contains("midnight"),"Daily cap refusal explains the reset");
        clock.Now=new DateTimeOffset(2026,10,24,0,0,1,TimeSpan.Zero);
        Check(budget.CanAcquire(RequestKind.Wake)&&budget.Status(RequestKind.Wake).UsedToday==0,"Daily caps reset at local midnight");

        clock.Now+=TimeSpan.FromMinutes(1);
        budget.Acquire(RequestKind.Location);
        var reopened=new RequestBudget(store,clock);
        Refused(()=>reopened.Acquire(RequestKind.Location),"Limits survive a new client and an app restart");
        Check(reopened.Status(RequestKind.Location).UsedToday==1,"Daily counts persist");

        budget.RecordRateLimited(RequestKind.Read,TimeSpan.FromMinutes(3));
        var blocked=Refused(()=>budget.Acquire(RequestKind.Command),"Hyundai 429 pauses every kind");
        Check(!blocked.Overridable&&blocked.Message.Contains("cannot be overridden"),"Hyundai rate limiting is never overridable");
        budget.AllowOnce(RequestKind.Command);Refused(()=>budget.Acquire(RequestKind.Command),"Override never bypasses Hyundai rate limiting");
        Refused(()=>new RequestBudget(store,clock).ThrowIfHyundaiBlocked("Command",RequestKind.Command),"Hyundai pause persists across restarts");
        clock.Now+=TimeSpan.FromMinutes(3);
        var tightened=budget.Status(RequestKind.Read);
        Check(tightened.Factor==2&&tightened.Cooldown==TimeSpan.FromSeconds(30)&&tightened.DailyCap==75,"First 429 doubles that kind's limits");
        Check(budget.Status(RequestKind.Command).Factor==1,"Tightening applies only to the rate-limited kind");
        budget.Acquire(RequestKind.Read);clock.Now+=TimeSpan.FromSeconds(20);
        Check(Refused(()=>budget.Acquire(RequestKind.Read),"Tightened cooldown").Message.Contains("×2"),"Refusal names the tightening");
        for(int i=0;i<3;i++){budget.RecordRateLimited(RequestKind.Read,null);clock.Now+=TimeSpan.FromMinutes(5);}
        Check(budget.Status(RequestKind.Read).Factor==RequestBudget.MaxFactor&&budget.Status(RequestKind.Read).DailyCap==150/8,"Repeated 429s stack up to ×8");
        Check(budget.HyundaiBlockedUntil<=clock.Now,"Missing Retry-After uses a five-minute pause");
        clock.Now+=TimeSpan.FromHours(24);
        Check(budget.Status(RequestKind.Read).Factor==1&&budget.Status(RequestKind.Read).Cooldown==TimeSpan.FromSeconds(15),"Limits return to normal after 24 hours without 429");
        budget.RecordRateLimited(RequestKind.Wake,TimeSpan.FromDays(30));
        Check(budget.HyundaiBlockedUntil==clock.Now+TimeSpan.FromHours(24),"Server pause is capped at 24 hours");

        var tampered=new MemoryBudgetStore();tampered.Save("{\"version\":1,\"day\":\"x\",\"blockedUntil\":\"2099-01-01T00:00:00Z\",\"kinds\":{\"Read\":{\"last\":\"2099-01-01T00:00:00Z\",\"count\":-4,\"factor\":99,\"tightenedUntil\":\"2099-01-01T00:00:00Z\"}}}");
        var clamped=new RequestBudget(tampered,clock);
        Check(clamped.HyundaiBlockedUntil<=clock.Now+TimeSpan.FromHours(24)&&clamped.Status(RequestKind.Read).Factor<=RequestBudget.MaxFactor&&
            clamped.NextAllowed(RequestKind.Read)<=clock.Now+TimeSpan.FromSeconds(15*RequestBudget.MaxFactor),"Future or out-of-range stored values are clamped");
        var corrupt=new MemoryBudgetStore();corrupt.Save("not json");
        Check(new RequestBudget(corrupt,clock).CanAcquire(RequestKind.Read),"Corrupt storage falls back to defaults");
        Check(!store.Load().Contains("@")&&!store.Load().Contains("TESTVIN"),"Stored limits contain no account data");
        Check(RequestBudget.Duration(TimeSpan.FromSeconds(1))=="1 second"&&RequestBudget.Duration(TimeSpan.FromSeconds(61))=="2 minutes"&&
            RequestBudget.Duration(TimeSpan.FromMinutes(90))=="1 hour 30 minutes"&&RequestBudget.Duration(TimeSpan.FromHours(1))=="1 hour","Readable durations");

        // Client integration: a shared budget outlives clients and refuses before any HTTP request.
        clock=new Clock();var shared=new RequestBudget(new MemoryBudgetStore(),clock);var hyundai=new Hyundai();
        using(var first=new HyundaiUsClient(hyundai,clock,shared)) {
            var list=await first.LoginAsync("tester@example.invalid","test-password","1234",default);
            await first.SelectAsync(list[0],default);await first.LocateAsync(default);
        }
        using(var second=new HyundaiUsClient(hyundai,clock,shared)) {
            int sent=hyundai.Sent;
            await RefusedAsync(()=>second.LoginAsync("tester@example.invalid","test-password","1234",default),"Sign-in cooldown spans clients");
            Check(hyundai.Sent==sent,"Refused sign-in sends nothing");
            clock.Now+=TimeSpan.FromSeconds(60);
            var list=await second.LoginAsync("tester@example.invalid","test-password","1234",default);
            await second.SelectAsync(list[0],default);
            Check(!second.CanRequestLocation,"Location cooldown survives locking and signing in again");
            sent=hyundai.Sent;
            await RefusedAsync(()=>second.LocateAsync(default),"Location refused before request");
            await RefusedAsync(()=>second.ReadAsync(false,default),"Session-start read starts the read cooldown");
            Check(hyundai.Sent==sent,"Refused requests send nothing");
            shared.AllowOnce(RequestKind.Location);await second.LocateAsync(default);
            Check(hyundai.Sent==sent+1,"Override sends exactly one location request");
            clock.Now+=TimeSpan.FromSeconds(15);hyundai.Next=HttpStatusCode.TooManyRequests;
            try{await second.ReadAsync(false,default);throw new Exception("Request limits: 429 accepted");}
            catch(BluelinkException e) when(e is not RequestLimitException){Check(e.HttpStatus==429,"Hyundai 429 surfaces as an HTTP failure");}
            Check(shared.Status(RequestKind.Read).Factor==2&&shared.HyundaiBlockedUntil>clock.Now,"429 from the client pauses and tightens the shared budget");
            sent=hyundai.Sent;await RefusedAsync(()=>second.ExecuteAsync(new(ActionKind.Lock),null,default),"Commands paused during Hyundai rate limiting");
            Check(hyundai.Sent==sent,"Nothing is sent during Hyundai rate limiting");
        }
        Console.WriteLine($"PASS: {count} request-limit checks (fake clock and transport).");
    }
}
