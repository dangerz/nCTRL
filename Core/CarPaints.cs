using System;
using System.Collections.Generic;
using System.Linq;

namespace NGarage.Core;

/// <summary>Paint presets for each maker's pictures (illustrative, picked by eye; not factory color matching), and
/// gtCTRL's app icon for a paint: one icon per Kia preset, built into the app as an alternate icon.</summary>
public static class CarPaints
{
    public static readonly IReadOnlyList<(string Name,string Hex)> Hyundai=new[]{
        ("Performance Blue","89b7f0"),("Atlas White","e4e8ed"),
        ("Ecotonic Gray","747c79"),("Phantom Black","202630"),
        ("Soultronic Orange","ee632f"),("Gravity Gold","b6ab8a")};
    public static readonly IReadOnlyList<(string Name,string Hex)> Kia=new[]{
        ("Interstellar Gray","525252"),("Yacht Blue","2b4f86"),
        ("Runway Red","a3222b"),("Moonscape","7a7f78"),
        ("Aurora Black Pearl","1c1f24"),("Snow White Pearl","e9ecee")};
    public static IReadOnlyList<(string Name,string Hex)> For(string maker)=>maker=="Kia"?Kia:Hyundai;
    public static IReadOnlyList<(string Name,string Hex)> For(CarModel model)=>For(model.Maker);

    /// <summary>The alternate icon's name for a Kia preset ("AppIcon-YachtBlue"); the first preset is the main icon.</summary>
    public static string IconName(string paintName)=>"AppIcon-"+string.Concat(paintName.Where(char.IsAsciiLetterOrDigit));
    /// <summary>gtCTRL's icon for a paint: the nearest Kia preset's, or "" for the main icon (Interstellar Gray).
    /// A custom color picks the closest preset, since iOS only switches between icons built into the app.</summary>
    public static string IconFor(string? hex)
    {
        if(AppTheme.Clean(hex) is not {} clean)return "";
        static (int R,int G,int B) Rgb(string h)=>(Convert.ToInt32(h[..2],16),Convert.ToInt32(h[2..4],16),Convert.ToInt32(h[4..6],16));
        var (r,g,b)=Rgb(clean);
        var nearest=Kia.OrderBy(p=>{var (pr,pg,pb)=Rgb(p.Hex);return (pr-r)*(pr-r)+(pg-g)*(pg-g)+(pb-b)*(pb-b);}).First();
        return nearest==Kia[0]?"":IconName(nearest.Name);
    }
}
