using System;
using System.Linq;
using System.Text.Json;
using NGarage.Core;

static class DetailTests
{
    const string Status="""
    {"vehicleStatus":{"dateTime":"2026-10-22T10:00:00Z","doorLock":true,"doorOpen":{"frontLeft":0,"frontRight":1,"backLeft":0,"backRight":0},
      "trunkOpen":false,"hoodOpen":false,"battery":{"batSoc":87},"airCtrlOn":false,"steerWheelHeat":0,
      "seatHeaterVentState":{"flSeatHeatState":8,"frSeatHeatState":3},
      "tirePressure":{"tirePressureFrontLeft":36,"tirePressureFrontRight":"35","tirePressureRearLeft":34,"tirePressureRearRight":33},
      "tirePressureLamp":{"tirePressureWarningLampAll":1,"tirePressureWarningLampFrontLeft":0,"tirePressureWarningLampFrontRight":0,"tirePressureWarningLampRearLeft":0,"tirePressureWarningLampRearRight":1},
      "evStatus":{"batteryStatus":78,"batteryPlugin":2,"remainTime2":{"atc":{"value":95,"unit":1}},"newHyundaiField":"x"},
      "vehicleLocation":{"coord":{"lat":40.123456,"lon":-74.654321},"speed":{"value":0}},"vin":"TESTVIN0000000001","someFutureFlag":true}}
    """;
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Status details: "+label);count++;}
        using var doc=JsonDocument.Parse(Status);var status=doc.RootElement.GetProperty("vehicleStatus");
        var tires=StatusDetails.Tires(status);
        Check(tires.Count==4&&tires[0].Position=="Front left"&&tires[0].Psi==36&&tires[1].Psi==35&&tires[3].Psi==33,"Tire pressures in PSI, numeric or text");
        Check(tires[0].Warning==false&&tires[3].Warning==true&&StatusDetails.AnyTireWarning(status)==true,"Tire warning lights per tire and overall");
        using(var none=JsonDocument.Parse("{}"))Check(StatusDetails.Tires(none.RootElement).All(t=>t.Psi==null&&t.Warning==null)&&StatusDetails.AnyTireWarning(none.RootElement)==null,"Unreported tires stay unknown");
        var rows=StatusDetails.Rows(status);
        string Value(string label)=>rows.FirstOrDefault(r=>r.Label==label)?.Value??"(missing)";
        Check(Value("12-volt battery")=="87%"&&Value("Battery")=="78%"&&Value("Doors locked")=="Yes","Known fields get readable names and values");
        Check(Value("Front-right door")=="Open"&&Value("Front-left door")=="Closed"&&Value("Tailgate")=="Closed","Door and tailgate states");
        Check(Value("Driver seat")=="Heat high"&&Value("Passenger seat")=="Cool low"&&Value("Steering wheel heat")=="Off","Seat and wheel states");
        Check(Value("Rear-right tire")=="33 psi"&&Value("Rear-right tire warning")=="Warning"&&Value("Front-left tire warning")=="OK","Tire rows have readable names");
        Check(Value("Charge time left (min)")=="95"&&Value("Charge cable")=="Plugged in (type 2)","EV charging details");
        Check(Value("evStatus.newHyundaiField")=="x"&&Value("someFutureFlag")=="Yes","Unknown fields are still listed by their path");
        Check(rows.All(r=>!r.Label.Contains("coord",StringComparison.OrdinalIgnoreCase)&&!r.Label.Contains("vehicleLocation",StringComparison.OrdinalIgnoreCase)&&
            !r.Value.Contains("40.12")&&!r.Value.Contains("74.65")&&!r.Value.Contains("TESTVIN")),"Location, coordinates and VIN are never listed");
        Check(rows.Count<=StatusDetails.MaxRows,"Row count is bounded");
        Console.WriteLine($"PASS: {count} vehicle-detail checks.");
    }
}
