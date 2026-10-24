using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NGarage.Core;

static class ColdRangeTests
{
    sealed class Weather(HttpStatusCode code,string body) : HttpMessageHandler
    {
        public string? Url;public int Requests;public string Headers="";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests++;Url=request.RequestUri!.ToString();Headers=request.Headers.ToString();
            return Task.FromResult(new HttpResponseMessage(code){Content=new StringContent(body,Encoding.UTF8,"application/json")});
        }
    }
    public static async Task Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Cold range: "+label);count++;}
        bool Near(double? a,double b)=>a is double v&&Math.Abs(v-b)<0.001;
        var d=ColdRangeSettings.Default;

        Check(d.Enabled&&d.StartF==70&&d.ColdestF==0&&d.LossPercent==50,"Defaults: 70°F to 0°F, half the range lost at 0°F");
        Check(ColdRange.Estimate(200,70,d)==null&&ColdRange.Estimate(200,85,d)==null,"No estimate at or above the start temperature");
        Check(Near(ColdRange.Estimate(200,35,d),150)&&Near(ColdRange.Estimate(200,0,d),100)&&Near(ColdRange.Estimate(200,-20,d),100),
            "Even drop: 35°F keeps 75%, 0°F and colder keep 50%");
        Check(Near(ColdRange.Estimate(184,69,d),184*(1-0.5/70)),"Just below the start, a small drop");
        Check(ColdRange.Estimate(200,20,d with {Enabled=false})==null&&ColdRange.Estimate(null,20,d)==null&&ColdRange.Estimate(200,null,d)==null&&
            ColdRange.Estimate(200,double.NaN,d)==null,"Nothing when off, or when range or temperature is missing");
        var custom=new ColdRangeSettings(true,50,10,30);
        Check(Near(ColdRange.Estimate(100,30,custom),85)&&Near(ColdRange.Estimate(100,10,custom),70)&&ColdRange.Estimate(100,55,custom)==null,"Custom temperatures and loss");
        var bad=new ColdRangeSettings(true,20,40,150).Valid();
        Check(bad.StartF==20&&bad.ColdestF==19&&bad.LossPercent==90,"The coldest stays below the start, and the loss is capped at 90%");
        Check(new ColdRangeSettings(true,500,-500,-5).Valid() is {StartF:100,ColdestF:-40,LossPercent:0},"Settings stay within limits");
        Check(Near(ColdRange.Factor(-100,new ColdRangeSettings(true,20,40,50)),0.5),"Even reversed settings never divide by zero");

        // Units: display only.
        Check(Units.Distance(184,UnitSystem.Imperial)=="184 mi"&&Units.Distance(184,UnitSystem.Metric)=="296 km","Distance");
        Check(Units.Temperature(28,UnitSystem.Imperial)=="28°F"&&Units.Temperature(28,UnitSystem.Metric)=="-2°C"&&Units.Temperature(32.2,UnitSystem.Metric)=="0°C","Temperature, with no -0");
        Check(Units.Setpoint(72,UnitSystem.Imperial)=="72°F"&&Units.Setpoint(72,UnitSystem.Metric)=="22°C"&&Units.Setpoint(71,UnitSystem.Metric)=="21.5°C"&&
            Units.Setpoint(62,UnitSystem.Metric)=="16.5°C"&&Units.Setpoint(82,UnitSystem.Metric)=="28°C","Climate setpoints to the half degree");
        Check(Units.Pressure(36,UnitSystem.Metric)=="248 kPa"&&Units.Pressure(36,UnitSystem.Imperial)=="36 psi","Tire pressure");
        Check(Units.Radius(150,UnitSystem.Imperial)=="490 ft"&&Units.Radius(150,UnitSystem.Metric)=="150 m","Place radius");
        Check(Units.Step(70,1,UnitSystem.Imperial)==75&&Units.Step(70,-1,UnitSystem.Imperial)==65&&
            Units.Temperature(Units.Step(70,1,UnitSystem.Metric),UnitSystem.Metric)=="23°C"&&Units.Temperature(Units.Step(70,-1,UnitSystem.Metric),UnitSystem.Metric)=="19°C",
            "Steps: 5°F, or 2°C on whole degrees");
        Check(Units.Parse("metric")==UnitSystem.Metric&&Units.Parse("imperial")==UnitSystem.Imperial&&Units.Parse(null)==UnitSystem.Imperial&&
            Units.Parse(Units.Name(UnitSystem.Metric))==UnitSystem.Metric,"Imperial unless metric was saved");

        // Status details in metric, including a range Hyundai reported in km.
        using(var doc=JsonDocument.Parse("""{"tirePressure":{"tirePressureFrontLeft":36},"odometer":1000,"airTemp":{"value":"72","unit":1},"evStatus":{"drvDistance":[{"rangeByFuel":{"totalAvailableRange":{"value":184,"unit":3},"evModeRange":{"value":296,"unit":1}}}]}}""")) {
            var rows=StatusDetails.Rows(doc.RootElement,UnitSystem.Metric);
            string Value(string label)=>rows.FirstOrDefault(r=>r.Label==label)?.Value??"(missing)";
            Check(Value("Front-left tire")=="248 kPa"&&Value("Odometer")=="1609 km"&&Value("Climate setpoint")=="22°C"&&Value("Range")=="296 km"&&Value("Electric range")=="296 km",
                "Details show metric values");
            Check(StatusDetails.Rows(doc.RootElement).First(r=>r.Label=="Electric range").Value=="184 mi","A range reported in km shows in miles for imperial");
        }

        // Weather: only a rounded area is sent; anything unexpected gives no temperature.
        var at=new VehicleLocation(40.781234,-73.966512,"");
        Check(OutsideTemperature.Area(at)=="40.8,-74.0"&&OutsideTemperature.Area(new VehicleLocation(0.04,-0.04,""))=="0.0,0.0","Location rounded to 0.1°");
        Check(OutsideTemperature.Url("40.8,-74.0")=="https://api.open-meteo.com/v1/forecast?latitude=40.8&longitude=-74.0&current=temperature_2m&temperature_unit=fahrenheit",
            "The request holds the rounded area and nothing else");
        Check(OutsideTemperature.Parse("""{"current":{"time":"x","temperature_2m":28.4}}""")==28.4,"Reads the current temperature");
        Check(new[]{"","{","[]","{\"current\":{}}","{\"current\":{\"temperature_2m\":\"28\"}}","{\"current\":{\"temperature_2m\":999}}"}.All(j=>OutsideTemperature.Parse(j)==null),
            "Unexpected replies give no temperature");
        var now=new DateTimeOffset(2026,10,24,12,0,0,TimeSpan.Zero);
        var ok=new Weather(HttpStatusCode.OK,"""{"current":{"temperature_2m":28.4}}""");
        using(var weather=new OutsideTemperature(ok)) {
            var reading=await weather.ReadAsync(at,now,default);
            Check(reading==new OutsideReading(28.4,now,"40.8,-74.0")&&ok.Requests==1&&ok.Url!.Contains("latitude=40.8&longitude=-74.0")&&
                !ok.Url.Contains("40.78")&&!ok.Headers.Contains("Authorization"),"One request with only the rounded area, no account headers");
        }
        foreach(var failing in new[]{new Weather(HttpStatusCode.InternalServerError,"{}"),new Weather(HttpStatusCode.OK,"not json")}) {
            using var weather=new OutsideTemperature(failing);bool threw=false;
            try {await weather.ReadAsync(at,now,default);} catch(Exception ex) when(ex is HttpRequestException or FormatException){threw=true;}
            Check(threw&&failing.Requests==1,"A failed reply is reported once, never retried");
        }
        Check(DemoHyundai.OutsideF<ColdRangeSettings.Default.StartF,"The demo's pretend temperature shows the estimate");
        Console.WriteLine($"PASS: {count} cold-range, units and weather checks (no network).");
    }
}
