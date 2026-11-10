using System;
using System.Text.Json;
using NGarage.Core;

static class StatusReportTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Bug report status: "+label);count++;}
        const string vin="KMHTEST0000123456";
        string json="""
        {"vehicleStatus":{"dateTime":"2026-11-09T12:00:00Z","evStatus":{"batteryStatus":78,"drvDistance":[{"rangeByFuel":{"totalAvailableRange":{"value":210,"unit":3}}}]},
          "doorLock":true,"tirePressure":{"all":0},"vehicleLocation":{"coord":{"lat":40.123456,"lon":-74.654321,"alt":12},"head":90,"speed":{"value":0}},
          "lat":40.1,"Longitude":-74.2},
         "vin":"KMHTEST0000123456","vehicleId":"abc-123","regId":"R1","id":"7","enrollmentId":"E2","masterProfileID":"M3",
         "owner":{"firstName":"Pat","lastName":"Example","email":"driver@example.invalid","phone":"555-0100","address":{"street":"1 Main St","zip":"10001"}},
         "notes":["Contact driver@example.invalid about KMHTEST0000123456","plain note"],
         "session":{"accessToken":"secret-token","sid":"S","pin":"1234","pAuth":"P"},
         "blob":"QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU2Nzg5","valid":true,"nickname":"Pat's car","deviceId":"DEV"}
        """;
        using var doc=JsonDocument.Parse(json);
        string report=StatusReport.Build(doc.RootElement,"nCTRL","2.16","Hyundai Bluelink");
        foreach(var leak in new[]{"123456","abc-123","R1\"","E2","M3","Pat","Example","driver@","555-0100","1 Main St","10001","40.123456","-74.654321","40.1,","-74.2",
            "secret-token","\"S\"","1234\"","\"P\"","QUJDREVG","Pat's car","DEV"})
            Check(!report.Contains(leak,StringComparison.Ordinal),"Leaves out "+leak);
        Check(report.StartsWith("nCTRL 2.16 · Hyundai Bluelink · last status, personal details removed\n\n"),"Starts with the app, version and service");
        Check(report.Contains("\"vin\": \"KMHTEST0000******\"")&&report.Contains("KMHTEST0000****** about")==false&&report.Contains("\"(removed)\""),
            "The VIN keeps its model part; a message mentioning it is removed with the email in it");
        Check(report.Contains("\"batteryStatus\": 78")&&report.Contains("\"doorLock\": true")&&report.Contains("\"value\": 210")&&
            report.Contains("\"valid\": true")&&report.Contains("\"(text, 10 characters)\"")&&report.Contains("\"dateTime\": \"2026-11-09T12:00:00Z\""),
            "Numbers, yes/no and dates stay; other text is replaced with its length");
        using var vinOnly=JsonDocument.Parse("{\"vin\":\""+vin+"\",\"message\":\"Car "+vin+" is asleep\"}");
        Check(!StatusReport.Redact(vinOnly.RootElement).Contains("123456"),"A VIN inside a message is removed with the message");
        // Fails closed: what a maker might add tomorrow.
        using var future=JsonDocument.Parse("""
            {"long":-74.5,"name":"Pat","vehicleName":"Pat's EV","state":"NJ","country":"US","sessionid":"s1","accountid":"a2","Accountid":"a3",
             "note":"Call 555 010 0199","short":"abc123def","free":"Pat","words":"parked at the office","code":"HT_533","flag":"ON","level":"78",
             "when":"20261109120000","day":"2026-11-09","doorLock":false,"odometer":1234.5,"isValid":true,
             "vehicleStatus":{"text":"KMHOTHER000654321 asleep"}}
            """);
        string f=StatusReport.Redact(future.RootElement);
        foreach(var leak in new[]{"-74.5","Pat","NJ","\"US\"","s1","a2","a3","555","abc123def","office","654321"})
            Check(!f.Contains(leak,StringComparison.Ordinal),"Unknown fields leave out "+leak);
        Check(f.Contains("\"HT_533\"")&&f.Contains("\"ON\"")&&f.Contains("\"78\"")&&f.Contains("\"20261109120000\"")&&f.Contains("\"2026-11-09\"")&&
            f.Contains("\"odometer\": 1234.5")&&f.Contains("\"isValid\": true")&&f.Contains("\"doorLock\": false"),"Codes, numbers, dates and yes/no stay");
        Check(StatusReport.SecretKey("vehicleNickName")&&StatusReport.SecretKey("mobilePhone")&&StatusReport.SecretKey("userID")&&!StatusReport.SecretKey("hybrid")&&!StatusReport.SecretKey("batSoc"),
            "Personal keys are recognized by their parts");
        Check(StatusReport.Vin("SHORT")=="(removed)","A malformed VIN is removed");
        using var parsed=JsonDocument.Parse(report[report.IndexOf('{')..]);
        Check(parsed.RootElement.GetProperty("vehicleStatus").GetProperty("evStatus").GetProperty("batteryStatus").GetInt32()==78,"The redacted status is still valid JSON");
        Console.WriteLine($"PASS: {count} bug report status checks.");
    }
}
