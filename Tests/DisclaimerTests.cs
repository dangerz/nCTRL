using System;
using NGarage.Core;

static class DisclaimerTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Disclaimer: "+label);count++;}
        foreach(string typed in new[]{"I Agree","i agree","  I AGREE  ","I   agree"})Check(Disclaimer.PhraseMatches(typed),"Accept phrase variant '"+typed+"'");
        foreach(string typed in new[]{"","I","Agree","I Agreed","I Agree.","IAgree","I agree to nothing"})Check(!Disclaimer.PhraseMatches(typed),"Reject phrase variant '"+typed+"'");
        string tester=VehicleLink.AccountKey("tester@example.invalid"),other=VehicleLink.AccountKey("other@example.invalid");
        var now=new DateTimeOffset(2026,10,23,12,0,0,TimeSpan.Zero);
        Check(!Disclaimer.IsCurrent(null)&&!Disclaimer.Allows(null,tester),"No acceptance blocks every account");
        var unbound=Disclaimer.Accept("",now);
        Check(Disclaimer.IsCurrent(unbound)&&Disclaimer.Allows(unbound,tester)&&Disclaimer.Allows(unbound,other),"Unbound acceptance allows the first sign-in");
        var bound=Disclaimer.Bind(unbound,tester)!;
        Check(bound.AccountKey==tester&&bound.AcceptedAt==now,"First successful sign-in binds acceptance");
        Check(Disclaimer.Allows(bound,tester)&&!Disclaimer.Allows(bound,other),"Different account must accept again");
        Check(Disclaimer.Bind(bound,other)==bound,"Bound acceptance never moves to another account");
        Check(Disclaimer.Bind(unbound,"not-a-hash")==unbound,"Invalid account key never binds");
        Check(!Disclaimer.Allows(bound with {Version=Disclaimer.Version-1},tester),"Outdated disclaimer version requires acceptance");
        var restored=Disclaimer.Restore(Disclaimer.Version,tester,now.ToString("O"));
        Check(restored==bound,"Stored acceptance round-trips");
        Check(Disclaimer.Restore(Disclaimer.Version,"",now.ToString("O"))?.AccountKey=="","Unbound acceptance round-trips");
        Check(Disclaimer.Restore(Disclaimer.Version-1,tester,now.ToString("O"))==null,"Old stored version is discarded");
        Check(Disclaimer.Restore(Disclaimer.Version,"tester@example.invalid",now.ToString("O"))==null,"Raw email is never accepted as a key");
        Check(Disclaimer.Restore(Disclaimer.Version,tester,"garbage")==null,"Malformed timestamp is discarded");
        Check(!tester.Contains("tester")&&tester.Length==64,"Stored key contains no raw email");
        Check(Disclaimer.Text.Contains("third-party")&&Disclaimer.Text.Contains("ban")&&Disclaimer.Text.Contains("change or shut down")&&
            Disclaimer.Text.Contains("every risk")&&Disclaimer.Text.Contains("not liable"),"Text covers every required risk");
        Console.WriteLine($"PASS: {count} risk-disclaimer checks.");
    }
}
