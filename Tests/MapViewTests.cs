using System;
using System.Text.Json;
using NGarage.Core;

static class MapViewTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Map view: "+label);count++;}
        var car=new VehicleLocation(40.0,-74.0,"",default,90);

        var alone=VehicleMap.Frame(car);
        Check(alone.Zoom==15&&alone.Pixel(40,-74)==(206,160)&&Math.Abs(alone.MetersPerPixel-3.66)<0.01,"The car alone: close up, in the middle");
        var near=VehicleMap.Frame(car,40.002,-74.002);
        Check(near==alone&&near.Shows(40.002,-74.002,VehicleMap.FitMargin),"A phone already in view leaves the car close up and centered");
        var fit=VehicleMap.Frame(car,40.02,-74.02);
        Check(fit.Zoom<15&&fit.Zoom>=VehicleMap.FarthestZoom&&fit.Shows(40,-74,VehicleMap.FitMargin)&&fit.Shows(40.02,-74.02,VehicleMap.FitMargin)&&
            !new MapFrame(fit.Latitude,fit.Longitude,fit.Zoom+1).Shows(40.02,-74.02,VehicleMap.FitMargin),"A phone a few km away: zoomed out just enough to show both");
        var far=VehicleMap.Frame(car,40.2,-74.2);
        Check(far==alone&&!far.Shows(40.2,-74.2),"A phone farther than 8 km leaves the car close up (an arrow points to the phone)");
        Check(VehicleMap.Frame(car,double.NaN,5)==alone&&VehicleMap.Frame(car,95,5)==alone,"A bad phone position is ignored");
        var dateline=new MapFrame(0,179.999,15);
        double across=dateline.Pixel(0,-179.999).X-dateline.Width/2.0;
        Check(across>40&&across<55,"Points across the date line are measured the short way (0.002° east, about 47 px)");
        Check(VehicleMap.TilesFor(fit).Count>0,"Tiles cover a fitted view");

        // Heading: degrees clockwise from north, from the status report and from find-my-car.
        LiveVehicleState Parse(string location)=>HyundaiUsClient.ParseState(JsonDocument.Parse("{\"vehicleStatus\":{\"dateTime\":\"x\",\"vehicleLocation\":{\"coord\":{\"lat\":40.1,\"lon\":-74.2}"+location+"}}}").RootElement);
        Check(Parse(",\"head\":90").StatusLocation?.Heading==90&&Parse(",\"head\":360").StatusLocation?.Heading==0&&
            Parse(",\"head\":400").StatusLocation?.Heading==null&&Parse(",\"head\":-5").StatusLocation?.Heading==null&&Parse("").StatusLocation?.Heading==null,"Heading read from the status location, 0–360 only");
        var now=DateTimeOffset.UtcNow;
        var stored=LastLocation.Decode(LastLocation.Encode("car",car with {RetrievedAt=now}),"car",now);
        Check(stored?.Heading==90,"The last known location keeps its heading");
        Check(LastLocation.Decode(LastLocation.Encode("car",car with {Heading=null,RetrievedAt=now}),"car",now) is {Heading: null},"Locations saved without a heading still read");

        Check(Units.Short(50,UnitSystem.Imperial)=="160 ft"&&Units.Short(5000,UnitSystem.Imperial)=="3.1 mi"&&Units.Short(20000,UnitSystem.Imperial)=="12 mi"&&
            Units.Short(450,UnitSystem.Metric)=="450 m"&&Units.Short(5000,UnitSystem.Metric)=="5.0 km"&&Units.Short(20000,UnitSystem.Metric)=="20 km","Short distances");
        Console.WriteLine($"PASS: {count} map view checks.");
    }
}
