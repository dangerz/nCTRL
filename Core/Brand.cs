using System;
using System.Collections.Generic;
using System.Linq;

namespace NGarage.Core;

/// <summary>One build of the app: its name, which car services it offers and its first-run look. nCTRL is the
/// original (Hyundai Bluelink by default, Kia Connect as a choice); gtCTRL is the Kia EV6 GT build (Kia only).
/// An export picks gtCTRL with the custom feature tag "gtctrl"; desktop runs can pass --flavor=gtctrl.</summary>
/// <param name="Detail">The motorsport detail color (header stripe, panel corners, selected tab); null follows the app color.</param>
public sealed record Flavor(string Id,string App,IReadOnlyList<string> Services,string DefaultTheme,string DefaultCar,
    string DefaultPaintName,string DefaultPaintHex,bool Demo,string? Detail,string? DetailDark);

/// <summary>The app's visible name and the car service in use. Screens and messages read names from here, so
/// nothing hard-codes a maker. Internal identifiers (namespace, Keychain items, file names) are deliberately not
/// branded; see README.</summary>
public static class Brand
{
    public const string Hyundai="hyundai",Kia="kia";
    public static readonly Flavor NCtrl=new("nctrl","nCTRL",new[]{Hyundai,Kia},AppTheme.BaseHex,"ioniq5n",
        "Performance Blue","89b7f0",Demo:true,Detail:"f24a4d",DetailDark:"c63842");
    public static readonly Flavor GtCtrl=new("gtctrl","gtCTRL",new[]{Kia},AppTheme.NeonHex,"ev6gt",
        "Interstellar Gray","525252",Demo:false,Detail:null,DetailDark:null);

    public static Flavor Flavor { get; private set; }=NCtrl;
    private static string service=Hyundai;
    /// <summary>The service sign-in uses ("hyundai" or "kia"); always one the flavor offers.</summary>
    public static string ServiceId
    {
        get=>service;
        set=>service=Flavor.Services.Contains(value)?value:Flavor.Services[0];
    }
    public static ServiceInfo Service=>ServiceFor(ServiceId);
    public static ServiceInfo ServiceFor(string id)=>id==Kia?KiaUsClient.Info:HyundaiUsClient.Info;
    public static string App=>Flavor.App;
    public static string Maker=>Service.Maker;
    public static bool ChoosesService=>Flavor.Services.Count>1;

    /// <summary>Switches flavor (at startup, or in tests) and resets the service to the flavor's first.</summary>
    public static void Use(Flavor flavor){Flavor=flavor;service=flavor.Services[0];}
    public static Flavor ById(string? id)=>id==GtCtrl.Id?GtCtrl:NCtrl;

    /// <summary>A new, signed-out connection to the service in use.</summary>
    public static IVehicleConnection Connect(RequestBudget budget)=>ServiceId==Kia?new KiaUsClient(budget):new HyundaiUsClient(budget);
}
