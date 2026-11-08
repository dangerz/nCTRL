using Godot;
using NGarage.Core;
using System.Collections.Generic;
using System.Linq;

// The current app color theme (Core/Theme.cs) as Godot colors, shared by every screen and drawing.
public static class Skin
{
    private static Dictionary<ThemeRole,Color> colors=Build(AppTheme.Default);
    public static string Hex {get; private set;}=AppTheme.DefaultHex;
    public static void Use(string? hex)
    {
        Hex=AppTheme.Clean(hex)??AppTheme.DefaultHex;
        colors=Build(AppTheme.Derive(Hex));
        RenderingServer.SetDefaultClearColor(colors[ThemeRole.Clear]);
    }
    private static Dictionary<ThemeRole,Color> Build(IReadOnlyDictionary<ThemeRole,string> theme)=>theme.ToDictionary(r=>r.Key,r=>new Color(r.Value));
    public static IEnumerable<Color> All=>colors.Values;
    public static Color Accent=>colors[ThemeRole.Accent];
    public static Color AccentHover=>colors[ThemeRole.AccentHover];
    public static Color AccentPressed=>colors[ThemeRole.AccentPressed];
    public static Color Text=>colors[ThemeRole.Text];
    public static Color Muted=>colors[ThemeRole.Muted];
    public static Color Back=>colors[ThemeRole.Back];
    public static Color Header=>colors[ThemeRole.Header];
    public static Color Nav=>colors[ThemeRole.Nav];
    public static Color NavLine=>colors[ThemeRole.NavLine];
    public static Color Hero=>colors[ThemeRole.Hero];
    public static Color Card=>colors[ThemeRole.Card];
    public static Color PanelFill=>colors[ThemeRole.PanelFill];
    public static Color PanelEdge=>colors[ThemeRole.PanelEdge];
    public static Color Stripe=>colors[ThemeRole.Stripe];
    public static Color Selected=>colors[ThemeRole.Selected];
    public static Color Button=>colors[ThemeRole.Button];
    public static Color ButtonEdge=>colors[ThemeRole.ButtonEdge];
    public static Color ButtonHover=>colors[ThemeRole.ButtonHover];
    public static Color ButtonPressed=>colors[ThemeRole.ButtonPressed];
    public static Color Notice=>colors[ThemeRole.Notice];
    public static Color BarOff=>colors[ThemeRole.BarOff];
    // The motorsport details (header stripe, panel corners, selected tab): the flavor's own color (nCTRL's N red),
    // or the app color when the flavor has none (gtCTRL).
    public static Color Detail=>Brand.Flavor.Detail is {} hex?new Color(hex):Accent;
    public static Color DetailDark=>Brand.Flavor.DetailDark is {} hex?new Color(hex):AccentPressed;
}
