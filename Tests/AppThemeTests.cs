using System;
using System.Linq;
using NGarage.Core;

static class AppThemeTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Theme: "+label);count++;}
        double HueGap(double a,double b)=>Math.Abs((a-b+540)%360-180);
        var roles=Enum.GetValues<ThemeRole>();

        Check(roles.All(AppTheme.Default.ContainsKey)&&ReferenceEquals(AppTheme.Derive(AppTheme.DefaultHex),AppTheme.Default)&&
            ReferenceEquals(AppTheme.Derive("#8ECBFA"),AppTheme.Default),"The default color gives exactly the original look");
        Check(new[]{null,"","zzzzzz","12345","#1234567"}.All(h=>ReferenceEquals(AppTheme.Derive(h),AppTheme.Default)),"Anything that isn't a color falls back to the default");
        Check(AppTheme.Clean(" #ABCDEF ")=="abcdef"&&AppTheme.Clean("abcdeg")==null,"Colors are cleaned to six lowercase hex digits");
        Check(AppTheme.Hex(206,0.43,0.98)=="8ecbfa"&&AppTheme.Hsv("ff0000")==(0,1,1),"Color conversions round-trip");
        // The default family really is one hue: every role within a few degrees of the accent.
        var (accentHue,_,_)=AppTheme.Hsv(AppTheme.Default[ThemeRole.Accent]);
        Check(AppTheme.Default.Values.All(h=>HueGap(AppTheme.Hsv(h).H,accentHue)<10),"The default colors sit within 10° of the accent's hue");

        foreach(var (name,hex) in AppTheme.Presets.Skip(1)) {
            var theme=AppTheme.Derive(hex);var (pickHue,pickSat,_)=AppTheme.Hsv(hex);
            Check(theme.Count==AppTheme.Default.Count&&theme.Values.Distinct().Count()>1,name+" gives every role");
            foreach(var role in roles) {
                var (h,s,v)=AppTheme.Hsv(theme[role]);var (dh,ds,dv)=AppTheme.Hsv(AppTheme.Default[role]);
                Check(Math.Abs(v-dv)<0.01,name+" keeps "+role+"'s brightness");
                if(pickSat>0)Check(s<0.08||HueGap(h,pickHue+dh-accentHue)<4,name+" turns "+role+" by its original offset");
                else Check(s==0,name+" (gray) makes "+role+" neutral");
            }
        }
        // Contrast holds whichever hue and vividness is picked.
        double worstText=99,worstOther=99;
        for(int hue=0;hue<360;hue+=10)foreach(double sat in new[]{0,0.43,1}) {
            var t=AppTheme.Derive(AppTheme.Hex(hue,sat,0.9));
            foreach(var bg in new[]{ThemeRole.Back,ThemeRole.Card,ThemeRole.Button,ThemeRole.Header,ThemeRole.Nav,ThemeRole.Hero,ThemeRole.PanelFill})
                worstText=Math.Min(worstText,AppTheme.Contrast(t[ThemeRole.Text],t[bg]));
            foreach(var (fg,bg) in new[]{(ThemeRole.Muted,ThemeRole.Card),(ThemeRole.Muted,ThemeRole.Back),(ThemeRole.Accent,ThemeRole.Back),(ThemeRole.Accent,ThemeRole.Card),
                (ThemeRole.Back,ThemeRole.Accent),(ThemeRole.Back,ThemeRole.AccentHover),(ThemeRole.Back,ThemeRole.AccentPressed),(ThemeRole.Text,ThemeRole.Notice),(ThemeRole.Text,ThemeRole.Selected)})
                worstOther=Math.Min(worstOther,AppTheme.Contrast(t[fg],t[bg]));
        }
        Check(worstText>=7,$"Text on backgrounds stays at 7:1 or better (worst {worstText:0.0})");
        Check(worstOther>=4.5,$"Muted text, the accent and button text stay at 4.5:1 or better (worst {worstOther:0.0})");
        Check(AppTheme.Shift(null)==(0,1)&&AppTheme.Shift(AppTheme.DefaultHex)==(0,1)&&AppTheme.Shift("d0d0d0").Vivid==0&&
            Math.Abs(AppTheme.Shift("bb8efa").HueShift-59)<1&&AppTheme.Shift("ff0000").Vivid==AppTheme.MaxVivid,"Picture backdrops turn by the same hue and vividness");
        Check(AppTheme.Name("8efaec")=="Teal"&&AppTheme.Name(null)=="Performance Blue"&&AppTheme.Name("123456")=="Custom color","Preset names");
        Console.WriteLine($"PASS: {count} app color theme checks.");
    }
}
