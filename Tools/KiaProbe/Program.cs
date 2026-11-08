// Kia Connect (US) read-only probe for gtCTRL, Phase 1.
// Port of hyundai_kia_connect_api KiaUvoApiUSA.py at bd93ccd38e8f582cbca03a786bb73a02159502ab (MIT).
// Sends: sign-in, one-time code (if Kia asks), vehicle list, one cached status read. No commands, no wake.
// Prints values only; never tokens, the VIN, the vehicle key or coordinates.
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const string Api = "https://api.owners.kia.com/apigw/v1/";

string stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gtctrl-probe");
Directory.CreateDirectory(stateDir);
string deviceFile = Path.Combine(stateDir, "device-id");
// A stable device id (not secret) so Kia sees the same "phone" on each run.
string deviceId = File.Exists(deviceFile) ? File.ReadAllText(deviceFile).Trim() : Guid.NewGuid().ToString().ToUpperInvariant();
File.WriteAllText(deviceFile, deviceId);

Console.WriteLine("gtCTRL Kia Connect probe (read-only)");
if (Console.IsInputRedirected) { Console.WriteLine("Run this in the Terminal app so the password can be typed hidden."); return 1; }
Console.Write("Kia Connect email: ");
string email = (Console.ReadLine() ?? "").Trim();
Console.Write("Kia Connect password (hidden): ");
string password = ReadHidden();
if (email.Length == 0 || password.Length == 0) { Console.WriteLine("Email and password are required."); return 1; }

HttpClient? http = null;
foreach (var (label, handler) in Handlers())
{
    http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    try
    {
        var (resp, body) = await Send(HttpMethod.Post, "prof/authUser", LoginBody(true), null);
        Console.WriteLine($"[tls] {label}: connected (HTTP {(int)resp.StatusCode})");
        return await Continue(resp, body);
    }
    catch (HttpRequestException e) when (e.InnerException is AuthenticationException or IOException)
    {
        Console.WriteLine($"[tls] {label}: handshake failed ({e.InnerException!.GetType().Name}); trying next");
        http.Dispose();
    }
}
Console.WriteLine("RESULT: could not connect to Kia with any TLS setting.");
return 2;

async Task<int> Continue(HttpResponseMessage resp, JsonNode? body)
{
    string? sid = Header(resp, "sid");
    if (sid is null)
    {
        var payload = body?["payload"];
        if (payload?["otpKey"] is null)
        {
            Console.WriteLine($"RESULT: sign-in refused. {StatusText(body)}");
            return 3;
        }
        Console.WriteLine("Kia wants a one-time code.");
        string otpKey = payload["otpKey"]!.ToString(), xid = Header(resp, "xid") ?? "";
        bool hasEmail = payload["hasEmail"]?.GetValue<bool>() ?? false, hasSms = payload["hasPhone"]?.GetValue<bool>() ?? false;
        Console.WriteLine($"  Destinations: email {(hasEmail ? payload["email"] : "none")}, text {(hasSms ? payload["phone"] : "none")}");
        string type = hasEmail && hasSms ? (Ask("Send the code by (e)mail or (t)ext? ").StartsWith("t", StringComparison.OrdinalIgnoreCase) ? "SMS" : "EMAIL") : hasSms ? "SMS" : "EMAIL";
        var (sendResp, sendBody) = await Send(HttpMethod.Post, "cmm/sendOTP", new JsonObject(), null, ("otpkey", otpKey), ("notifytype", type), ("xid", xid));
        Console.WriteLine($"  sendOTP: HTTP {(int)sendResp.StatusCode}, {StatusText(sendBody)}");
        string code = Ask("Code: ").Trim();
        var (verResp, verBody) = await Send(HttpMethod.Post, "cmm/verifyOTP", new JsonObject { ["otp"] = code }, null, ("otpkey", otpKey), ("xid", xid));
        string? tmpSid = Header(verResp, "sid"), rmtoken = Header(verResp, "rmtoken");
        if (tmpSid is null || rmtoken is null) { Console.WriteLine($"RESULT: code not accepted. {StatusText(verBody)}"); return 4; }
        Console.WriteLine("  Code accepted; remember token received (not shown, not saved).");
        var (finResp, finBody) = await Send(HttpMethod.Post, "prof/authUser", LoginBody(false), null, ("sid", tmpSid), ("rmtoken", rmtoken));
        sid = Header(finResp, "sid");
        if (sid is null) { Console.WriteLine($"RESULT: final sign-in step failed. {StatusText(finBody)}"); return 5; }
    }
    Console.WriteLine("Signed in: session received.");

    var (gvlResp, gvl) = await Send(HttpMethod.Get, "ownr/gvl", null, sid);
    var vehicles = gvl?["payload"]?["vehicleSummary"]?.AsArray();
    if (vehicles is null || vehicles.Count == 0) { Console.WriteLine($"RESULT: no vehicles. HTTP {(int)gvlResp.StatusCode}, {StatusText(gvl)}"); return 6; }
    Console.WriteLine($"Vehicles: {vehicles.Count}");
    foreach (var v in vehicles)
        Console.WriteLine($"  - {v?["nickName"]} | model \"{v?["modelName"]}\" | year {v?["modelYear"]} | fuelType {v?["fuelType"]} | key present: {v?["vehicleKey"] is not null}");
    var car = vehicles[0]!;

    var gviBody = JsonNode.Parse("""
        {"vehicleConfigReq":{"airTempRange":"0","maintenance":"1","seatHeatCoolOption":"0","vehicle":"1","vehicleFeature":"0"},
         "vehicleInfoReq":{"drivingActivty":"0","dtc":"1","enrollment":"1","functionalCards":"0","location":"1","vehicleStatus":"1","weather":"0"},
         "vinKey":[]}
        """)!;
    gviBody["vinKey"]!.AsArray().Add(car["vehicleKey"]!.ToString());
    var (gviResp, gvi) = await Send(HttpMethod.Post, "cmm/gvi", gviBody, sid, ("vinkey", car["vehicleKey"]!.ToString()));
    var info = gvi?["payload"]?["vehicleInfoList"]?[0];
    var s = info?["lastVehicleInfo"]?["vehicleStatusRpt"]?["vehicleStatus"];
    if (s is null) { Console.WriteLine($"RESULT: status read failed. HTTP {(int)gviResp.StatusCode}, {StatusText(gvi)}"); return 7; }
    var ev = s["evStatus"];
    Console.WriteLine("Cached status (as Kia last reported it):");
    Console.WriteLine($"  Reported at (UTC):  {s["syncDate"]?["utc"]}");
    Console.WriteLine($"  Battery:            {ev?["batteryStatus"]}%");
    var range = ev?["drvDistance"]?[0]?["rangeByFuel"]?["evModeRange"];
    Console.WriteLine($"  Range:              {range?["value"]} (unit code {range?["unit"]})");
    Console.WriteLine($"  Doors locked:       {s["doorLock"]}");
    Console.WriteLine($"  Climate on:         {s["climate"]?["airCtrl"]}");
    Console.WriteLine($"  Plugged / charging: {ev?["batteryPlugin"]} / {ev?["batteryCharge"]}");
    Console.WriteLine($"  Charge targets:     {ev?["targetSOC"]?.ToJsonString()}");
    Console.WriteLine($"  12V battery:        {s["batteryStatus"]?["stateOfCharge"]}%");
    Console.WriteLine($"  Location included:  {info?["lastVehicleInfo"]?["location"]?["coord"]?["lat"] is not null}");
    Console.WriteLine($"  Odometer:           {info?["vehicleConfig"]?["vehicleDetail"]?["vehicle"]?["mileage"]}");
    Console.WriteLine($"  Status fields:      {string.Join(", ", s.AsObject().Select(p => p.Key))}");
    Console.WriteLine("RESULT: OK. Kia Connect works from .NET on this Mac.");
    return 0;
}

JsonObject LoginBody(bool first)
{
    var b = new JsonObject { ["deviceKey"] = deviceId, ["deviceType"] = 2, ["userCredential"] = new JsonObject { ["userId"] = email, ["password"] = password } };
    if (first) b["tncFlag"] = 1;
    return b;
}

async Task<(HttpResponseMessage, JsonNode?)> Send(HttpMethod method, string path, JsonNode? json, string? sid, params (string, string)[] extra)
{
    var req = new HttpRequestMessage(method, Api + path);
    foreach (var (k, v) in BaseHeaders()) req.Headers.TryAddWithoutValidation(k, v);
    req.Headers.Date = DateTimeOffset.UtcNow;
    if (sid is not null) req.Headers.TryAddWithoutValidation("sid", sid);
    foreach (var (k, v) in extra) req.Headers.TryAddWithoutValidation(k, v);
    // Kia requires content-type on every request, GETs included (otherwise errorCode 9200
    // "Missing mandatory data in header"). .NET only sends it with content, so GETs carry an empty body.
    req.Content = json is not null ? new StringContent(json.ToJsonString(), Encoding.UTF8) : new ByteArrayContent(Array.Empty<byte>());
    req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json;charset=utf-8");
    var resp = await http!.SendAsync(req);
    string text = await resp.Content.ReadAsStringAsync();
    JsonNode? node = null;
    try { node = text.Length > 0 ? JsonNode.Parse(text) : null; } catch (JsonException) { }
    return (resp, node);
}

IEnumerable<(string, string)> BaseHeaders()
{
    int offset = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalHours;
    return new[] {
        ("accept", "application/json"), ("accept-language", "en-US,en;q=0.9"), ("accept-charset", "utf-8"),
        ("apptype", "L"), ("appversion", "7.22.0"), ("clientid", "SPACL716-APL"), ("clientuuid", Uuid5Dns(deviceId)),
        ("from", "SPA"), ("language", "0"), ("offset", offset.ToString()), ("ostype", "iOS"), ("osversion", "15.8.5"),
        ("phonebrand", "iPhone"), ("secretkey", "sydnat-9kykci-Kuhtep-h5nK"), ("to", "APIGW"), ("tokentype", "A"),
        ("user-agent", "KIAPrimo_iOS/37 CFNetwork/1335.0.3.4 Darwin/21.6.0"), ("deviceid", deviceId)
    };
}

IEnumerable<(string, HttpMessageHandler)> Handlers()
{
    yield return ("default TLS", new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All });
    yield return ("TLS 1.2 only", new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All,
        SslOptions = new() { EnabledSslProtocols = SslProtocols.Tls12 } });
}

static string? Header(HttpResponseMessage r, string name) =>
    r.Headers.TryGetValues(name, out var v) ? v.FirstOrDefault(x => !string.IsNullOrEmpty(x)) : null;

static string StatusText(JsonNode? body)
{
    var st = body?["status"];
    return st is null ? "no status in reply" : $"statusCode {st["statusCode"]}, errorType {st["errorType"]}, errorCode {st["errorCode"]}, \"{st["errorMessage"]}\"";
}

static string Ask(string prompt) { Console.Write(prompt); return Console.ReadLine() ?? ""; }

static string ReadHidden()
{
    var sb = new StringBuilder();
    while (true)
    {
        var k = Console.ReadKey(intercept: true);
        if (k.Key == ConsoleKey.Enter) { Console.WriteLine(); return sb.ToString(); }
        if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
        else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
    }
}

// RFC 4122 version 5 UUID in the DNS namespace, matching Python's uuid.uuid5(uuid.NAMESPACE_DNS, name).
static string Uuid5Dns(string name)
{
    byte[] ns = Convert.FromHexString("6ba7b8109dad11d180b400c04fd430c8");
    byte[] hash = SHA1.HashData(ns.Concat(Encoding.UTF8.GetBytes(name)).ToArray());
    hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
    hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
    string h = Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    return $"{h[..8]}-{h[8..12]}-{h[12..16]}-{h[16..20]}-{h[20..32]}";
}
