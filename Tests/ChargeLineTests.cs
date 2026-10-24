using System;
using System.Linq;
using System.Text.Json;
using NGarage.Core;

static class ChargeLineTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Charging line: "+label);count++;}
        var now=new DateTimeOffset(2026,10,23,20,0,0,TimeSpan.Zero);var utc=TimeZoneInfo.Utc;
        LiveVehicleState State(bool? charging,int? minutes,string reported="2026-10-23T19:30:00Z")=>
            new(true,false,72,60,true,charging,150,80,90,reported,now,ChargeMinutesLeft:minutes);

        Check(ChargeEstimate.Headline(null,now,utc)==null&&ChargeEstimate.Headline(State(null,null),now,utc)==null,"Unreported charging shows nothing new (gas cars keep the old line)");
        Check(ChargeEstimate.Headline(State(false,null),now,utc)==new ChargeLine("NOT CHARGING",false),"Not charging");
        Check(ChargeEstimate.Headline(State(true,70),now,utc)==new ChargeLine("CHARGING · FINISHING AROUND 8:40 PM",true),"Charging with Hyundai's finish time, counted from the report");
        Check(ChargeEstimate.Headline(State(true,60*20),now,utc)?.Text=="CHARGING · FINISHING AROUND SAT 3:30 PM","A finish on another day names the day");
        Check(ChargeEstimate.Headline(State(true,null),now,utc)==new ChargeLine("CHARGING",true)&&
            ChargeEstimate.Headline(State(true,20),now,utc)==new ChargeLine("CHARGING",true),"Still charging with no estimate, or one that has passed");
        Check(ChargeEstimate.Overdue(State(true,20),now)&&!ChargeEstimate.Overdue(State(true,70),now)&&!ChargeEstimate.Overdue(State(false,20),now)&&
            !ChargeEstimate.Overdue(State(true,null),now),"Overdue only when reported charging past Hyundai's finish time");

        LiveVehicleState Parse(string location)=>HyundaiUsClient.ParseState(JsonDocument.Parse("{\"vehicleStatus\":{\"dateTime\":\"x\""+location+"}}").RootElement);
        Check(Parse(",\"vehicleLocation\":{\"speed\":{\"value\":34,\"unit\":1}}").Moving==true&&Parse(",\"vehicleLocation\":{\"speed\":{\"value\":0,\"unit\":1}}").Moving==false&&
            Parse("").Moving==null&&Parse(",\"vehicleLocation\":{\"speed\":{\"value\":-3}}").Moving==null,"Moving means a reported speed above zero");

        // Silhouette: a backdrop with a car body, a dark grille above its lip, a ground shadow, a speck, and a light
        // pixel in the shadow below the car.
        const int w=40,h=20;var rgb=new byte[w*h*3];
        void Paint(int x0,int y0,int x1,int y1,byte r,byte g,byte b){for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++){int p=(y*w+x)*3;rgb[p]=r;rgb[p+1]=g;rgb[p+2]=b;}}
        Paint(0,0,w-1,h-1,17,24,33);Paint(8,4,31,14,140,180,240);Paint(10,9,14,13,6,7,9);Paint(4,15,35,17,8,9,12);Paint(37,2,37,2,200,200,200);Paint(20,18,20,18,60,60,60);Paint(1,13,6,19,70,70,70);
        var mask=Silhouette.Mask(rgb,w,h);
        bool In(int x,int y)=>mask[y*w+x]==1;
        Check(In(8,4)&&In(31,14)&&In(20,9)&&In(12,12),"The body, including its dark grille, is inside");
        Check(!In(7,9)&&!In(32,9)&&!In(20,3)&&!In(20,16)&&!In(5,16),"Backdrop and ground shadow are outside");
        Check(!In(37,2)&&mask.Count(m=>m==1)==24*11,"Specks, spikes and separate patches are dropped, leaving exactly the body");
        Console.WriteLine($"PASS: {count} charging-line and outline checks.");
    }
}
