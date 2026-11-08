using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NGarage.Core;

/// <summary>Each color the app's look is built from. Colors that carry a meaning (red for locked and
/// alerts, yellow for charging, amber for warnings, the heat and cool tints) are not part of the theme.</summary>
public enum ThemeRole
{
    Accent, AccentHover, AccentPressed, Text, Muted, Back, Header, Nav, NavLine, Hero, Card, PanelFill, PanelEdge,
    Stripe, Selected, Button, ButtonEdge, ButtonHover, ButtonPressed, Notice, BarOff, Clear
}

/// <summary>App color themes from one picked color. The base look (Performance Blue, nCTRL's default) is a family
/// of blues within a few degrees of the accent's hue; a theme keeps each color's hue offset from the accent, its
/// brightness and (scaled by how vivid the pick is) its saturation, and turns the whole family to the picked hue.
/// GT Neon (gtCTRL's default) is its own look: graphite panels with the EV6 GT's neon-lime accent.</summary>
public static class AppTheme
{
    // The base family's accent (Performance Blue); other picks are measured from it.
    public const string BaseHex="8ecbfa";
    // GT Neon.
    public const string NeonHex="d4f53c";
    /// <summary>The flavor's first-run pick.</summary>
    public static string DefaultHex=>Brand.Flavor.DefaultTheme;
    public static IReadOnlyList<(string Name,string Hex)> Presets=>DefaultHex==NeonHex?
        new[]{("GT Neon",NeonHex),("Ice Blue",BaseHex),("Teal","8efaec"),("Green","8efaa9"),("Purple","bb8efa"),
            ("Magenta","fa8edf"),("Orange","fac18e"),("Red","fa8e97"),("Graphite","d0d0d0")}:
        new[]{("Performance Blue",BaseHex),("Teal","8efaec"),("Green","8efaa9"),("Purple","bb8efa"),
            ("Magenta","fa8edf"),("Orange","fac18e"),("Red","fa8e97"),("Graphite","d0d0d0"),("GT Neon",NeonHex)};
    /// <summary>The flavor's default look.</summary>
    public static IReadOnlyDictionary<ThemeRole,string> Default=>DefaultHex==NeonHex?Neon:Base;
    // Graphite and neon, after the EV6 GT's dark trim and lime calipers and stitching.
    public static readonly IReadOnlyDictionary<ThemeRole,string> Neon=new Dictionary<ThemeRole,string>{
        [ThemeRole.Accent]=NeonHex,[ThemeRole.AccentHover]="e4fa80",[ThemeRole.AccentPressed]="bcdc2a",
        [ThemeRole.Text]="f0f2f1",[ThemeRole.Muted]="a4aaa8",[ThemeRole.Back]="0d0f10",[ThemeRole.Header]="131617",
        [ThemeRole.Nav]="111314",[ThemeRole.NavLine]="353a3c",[ThemeRole.Hero]="15181a",[ThemeRole.Card]="191c1e",
        [ThemeRole.PanelFill]="171a1c",[ThemeRole.PanelEdge]="3a4043",[ThemeRole.Stripe]="24282b",[ThemeRole.Selected]="22272a",
        [ThemeRole.Button]="15181a",[ThemeRole.ButtonEdge]="41484b",[ThemeRole.ButtonHover]="242a2c",[ThemeRole.ButtonPressed]="2b3134",
        [ThemeRole.Notice]="22282a",[ThemeRole.BarOff]="2d3234",[ThemeRole.Clear]="090a0b"};
    // The base family: Performance Blue.
    public static readonly IReadOnlyDictionary<ThemeRole,string> Base=new Dictionary<ThemeRole,string>{
        [ThemeRole.Accent]="8ecbfa",[ThemeRole.AccentHover]="b0ddff",[ThemeRole.AccentPressed]="72b6e8",
        [ThemeRole.Text]="eef5fb",[ThemeRole.Muted]="9ab0c6",[ThemeRole.Back]="08121d",[ThemeRole.Header]="0c1825",
        [ThemeRole.Nav]="0b1520",[ThemeRole.NavLine]="294053",[ThemeRole.Hero]="101820",[ThemeRole.Card]="0e2031",
        [ThemeRole.PanelFill]="0d1e2e",[ThemeRole.PanelEdge]="284760",[ThemeRole.Stripe]="172e46",[ThemeRole.Selected]="142b40",
        [ThemeRole.Button]="0c1c2c",[ThemeRole.ButtonEdge]="31526c",[ThemeRole.ButtonHover]="18334b",[ThemeRole.ButtonPressed]="1d3a53",
        [ThemeRole.Notice]="183749",[ThemeRole.BarOff]="243446",[ThemeRole.Clear]="060b11"};
    // How much more vivid than the default blue a pick can make the theme.
    public const double MaxVivid=1.5;

    /// <summary>Six hex digits, lowercase, without '#'; null when it isn't a color.</summary>
    public static string? Clean(string? hex)
    {
        hex=hex?.Trim().TrimStart('#').ToLowerInvariant();
        return hex is {Length: 6}&&hex.All(Uri.IsHexDigit)?hex:null;
    }
    /// <summary>Every role's color for a picked color. Anything that isn't a color gives the flavor's default;
    /// the base accent gives exactly the base look and GT Neon its own.</summary>
    public static IReadOnlyDictionary<ThemeRole,string> Derive(string? picked)
    {
        if(Clean(picked) is not {} hex)return Default;
        if(hex==BaseHex)return Base;
        if(hex==NeonHex)return Neon;
        var (shift,vivid)=Shift(hex);
        (double H,double S,double V) Turn(string original,double most)
        {
            var (rh,rs,rv)=Hsv(original);
            return ((rh+shift+360)%360,Math.Min(rs*Math.Min(vivid,most),1),rv);
        }
        var b=Turn(Base[ThemeRole.Back],MaxVivid);string back=Hex(b.H,b.S,b.V);
        return Base.ToDictionary(r=>r.Key,r=>{
            if(Hsv(r.Value).V<0.7){var dark=Turn(r.Value,MaxVivid);return Hex(dark.H,dark.S,dark.V);}
            // Light colors (text and the accent) are never more vivid than the default, and lose some color
            // where a hue is too dark (deep blue, purple) to keep 7:1 against the background.
            var (nh,ns,nv)=Turn(r.Value,1);string hex=Hex(nh,ns,nv);
            while(ns>0&&Contrast(hex,back)<MinLightContrast){ns=Math.Max(0,ns-0.02);hex=Hex(nh,ns,nv);}
            return hex;});
    }
    public const double MinLightContrast=7;
    /// <summary>How far a pick turns the default hue (degrees) and how vivid it makes the dark colors compared
    /// with the base. The car pictures' backdrops are turned the same way (Assets/Paint.gdshader);
    /// GT Neon's graphite takes the color out of the (navy) backdrops.</summary>
    public static (double HueShift,double Vivid) Shift(string? picked)
    {
        if((Clean(picked)??DefaultHex) is var hex&&hex==NeonHex)return (0,0);
        if(hex==BaseHex)return (0,1);
        var (h,s,_)=Hsv(hex);var (baseHue,baseSat,_)=Hsv(BaseHex);
        return ((h-baseHue+360)%360,Math.Min(s/baseSat,MaxVivid));
    }
    /// <summary>The preset's name, or "Custom color".</summary>
    public static string Name(string? hex)=>Presets.FirstOrDefault(p=>p.Hex==(Clean(hex)??DefaultHex)).Name??"Custom color";

    public static (double H,double S,double V) Hsv(string hex)
    {
        var (r,g,b)=Rgb(hex);
        double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;
        double h=d==0?0:max==r?60*(((g-b)/d+6)%6):max==g?60*((b-r)/d+2):60*((r-g)/d+4);
        return (h,max==0?0:d/max,max);
    }
    public static string Hex(double h,double s,double v)
    {
        double c=v*s,x=c*(1-Math.Abs(h/60%2-1)),m=v-c;
        var (r,g,b)=(h%360) switch {<60=>(c,x,0d),<120=>(x,c,0d),<180=>(0d,c,x),<240=>(0d,x,c),<300=>(x,0d,c),_=>(c,0d,x)};
        static int B(double n)=>(int)Math.Round(Math.Clamp(n,0,1)*255);
        return $"{B(r+m):x2}{B(g+m):x2}{B(b+m):x2}";
    }
    /// <summary>WCAG contrast ratio between two colors (1 to 21).</summary>
    public static double Contrast(string a,string b)
    {
        static double L(string hex){var (r,g,b)=Rgb(hex);static double C(double c)=>c<=0.03928?c/12.92:Math.Pow((c+0.055)/1.055,2.4);return 0.2126*C(r)+0.7152*C(g)+0.0722*C(b);}
        double la=L(a),lb=L(b);
        return (Math.Max(la,lb)+0.05)/(Math.Min(la,lb)+0.05);
    }
    private static (double R,double G,double B) Rgb(string hex)=>(
        int.Parse(hex[..2],NumberStyles.HexNumber)/255.0,int.Parse(hex[2..4],NumberStyles.HexNumber)/255.0,int.Parse(hex[4..6],NumberStyles.HexNumber)/255.0);
}
