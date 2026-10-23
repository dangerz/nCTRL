using System;
using System.Globalization;

namespace NGarage.Core;

// A liability acknowledgement, not an authorization secret. AccountKey is the VehicleLink
// SHA-256 account hash (never a raw email); empty means accepted but not yet bound to an account.
public sealed record DisclaimerAcceptance(int Version,string AccountKey,DateTimeOffset AcceptedAt);

public static class Disclaimer
{
    // Bump when the text changes materially: every account must accept again.
    public const int Version=1;
    public const string Phrase="I Agree";
    public const string Text=
        "nCTRL is an independent, third-party app. It is not made, endorsed, supported or approved by Hyundai Motor Company, " +
        "Hyundai Motor America or any of their affiliates. It is completely unsupported: no one provides help, fixes or guarantees.\n\n" +
        "By using nCTRL you understand and accept that:\n\n" +
        "• It controls your real vehicle through Hyundai's servers using an unofficial, community-documented protocol. " +
        "Commands can fail, arrive late, or behave in ways you did not intend. This could unlock your vehicle, drain its 12-volt " +
        "or high-voltage battery, change charging or climate settings, or otherwise damage or disable your vehicle.\n\n" +
        "• Hyundai may detect unofficial use and limit, suspend or permanently ban your Bluelink account or its access to Hyundai's API.\n\n" +
        "• Hyundai can change or shut down its API at any time without notice. That may break some or all of nCTRL, " +
        "possibly in unexpected ways.\n\n" +
        "• Vehicle status, location and command results may be delayed, incomplete or wrong.\n\n" +
        "• You alone are responsible for following Hyundai's terms and all applicable laws, and for using nCTRL safely. " +
        "Never use it while driving.\n\n" +
        "nCTRL is provided \"as is\", without warranty of any kind. You use it entirely at your own risk and you accept every risk " +
        "of using it. To the fullest extent permitted by law, you agree that its developers, contributors and distributors are not " +
        "liable for any damage, loss, cost, account action or injury of any kind, and you hold them harmless.\n\n" +
        "If you do not agree, do not use nCTRL.\n\n" +
        "Your acceptance applies to the Hyundai account you sign in with. Signing out, or signing in with a different account, " +
        "requires accepting again.";

    // Case and surrounding/repeated spaces are ignored; the words must match exactly.
    public static bool PhraseMatches(string typed)=>
        string.Equals(string.Join(' ',typed.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)),Phrase,StringComparison.OrdinalIgnoreCase);
    public static bool IsCurrent(DisclaimerAcceptance? acceptance)=>acceptance?.Version==Version;
    public static bool Allows(DisclaimerAcceptance? acceptance,string accountKey)=>
        IsCurrent(acceptance)&&(acceptance!.AccountKey.Length==0||acceptance.AccountKey==accountKey);
    public static DisclaimerAcceptance Accept(string accountKey,DateTimeOffset now)=>new(Version,accountKey,now);
    // An unbound acceptance belongs to the next account that signs in successfully.
    public static DisclaimerAcceptance? Bind(DisclaimerAcceptance? acceptance,string accountKey)=>
        IsCurrent(acceptance)&&acceptance!.AccountKey.Length==0&&IsKey(accountKey)?acceptance with {AccountKey=accountKey}:acceptance;
    // Stored preferences are untrusted: anything malformed or outdated requires accepting again.
    public static DisclaimerAcceptance? Restore(int version,string accountKey,string acceptedAt)=>
        version==Version&&(accountKey.Length==0||IsKey(accountKey))&&
        DateTimeOffset.TryParse(acceptedAt,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var at)
            ?new(version,accountKey,at):null;
    private static bool IsKey(string key)
    {
        if(key.Length!=64)return false;
        foreach(char c in key)if(!char.IsAsciiHexDigitUpper(c)&&!char.IsAsciiDigit(c))return false;
        return true;
    }
}
