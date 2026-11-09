using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NGarage.Core;

// What the app can do with a model. Electric models get every control; gas models connect through the
// maker's service, but the climate and charging controls are built for electric models.
public enum CarSupport { Electric, Gas, PictureOnly }
// Maker: whose service the model connects through ("Kia", "Hyundai"). Each build offers its own maker's models.
public sealed record CarModel(string Id,string Name,string Picture,CarSupport Support,string Maker)
{
    /// <summary>The top-down picture for the map marker (front up, transparent), named after the side picture.</summary>
    public string TopPicture=>Picture.Replace(".png","_top.png");
    /// <summary>The name without the maker ("EV6 GT" for "Kia EV6 GT"), for the header and the maker's own picker.</summary>
    public string ShortName=>Name.StartsWith(Maker+" ",System.StringComparison.Ordinal)?Name[(Maker.Length+1)..]:Name;
}

/// <summary>The car pictures nCTRL can show. The picture only changes how the car looks; status and
/// controls always come from the enrolled vehicle.</summary>
public static class CarModels
{
    public const int MaxName=20;
    public static readonly IReadOnlyList<CarModel> All=new CarModel[] {
        // Hyundai USA.
        new("ioniq5n","IONIQ 5 N","ioniq_pixel.png",CarSupport.Electric,"Hyundai"),
        new("ioniq5","IONIQ 5","hyundai_ioniq_5_pixel.png",CarSupport.Electric,"Hyundai"),
        new("ioniq6n","IONIQ 6 N","hyundai_ioniq_6_n_pixel.png",CarSupport.Electric,"Hyundai"),
        new("ioniq6","IONIQ 6","hyundai_ioniq_6_pixel.png",CarSupport.Electric,"Hyundai"),
        new("konan","Kona N","hyundai_kona_n_pixel.png",CarSupport.Gas,"Hyundai"),
        new("elantran","Elantra N","hyundai_elantra_n_pixel.png",CarSupport.Gas,"Hyundai"),
        new("velostern","Veloster N","hyundai_veloster_n_pixel.png",CarSupport.Gas,"Hyundai"),
        new("genesis","Genesis","hyundai_genesis_pixel.png",CarSupport.Gas,"Hyundai"),
        // Not sold in the US: pictures only.
        new("i20n","i20 N","hyundai_i20_n_pixel.png",CarSupport.PictureOnly,"Hyundai"),
        new("i30n","i30 N","hyundai_i30_n_pixel.png",CarSupport.PictureOnly,"Hyundai"),
        new("i30fastbackn","i30 Fastback N","hyundai_i30_fastback_n_pixel.png",CarSupport.PictureOnly,"Hyundai"),
        // Kia Connect (US).
        new("ev6","Kia EV6","kia_ev6_pixel.png",CarSupport.Electric,"Kia"),
        new("ev6gt","Kia EV6 GT","kia_ev6_gt_pixel.png",CarSupport.Electric,"Kia"),
        new("ev9","Kia EV9","kia_ev9_pixel.png",CarSupport.Electric,"Kia")
    };
    /// <summary>What a model can do with the service in use: another maker's model is a picture only.</summary>
    public static CarSupport SupportFor(CarModel model)=>model.Maker==Brand.Maker?model.Support:CarSupport.PictureOnly;
    /// <summary>The pictures offered: every one for Hyundai (other makers' as pictures only, as before Kia support),
    /// only Kia's for Kia. gtCTRL lists the EV6 GT first.</summary>
    public static IReadOnlyList<CarModel> Offered=>Brand.Maker=="Kia"?
        All.Where(m=>m.Maker=="Kia").OrderBy(m=>m.Id==Brand.Flavor.DefaultCar?0:1).ToList():All;
    /// <summary>The flavor's default picture (IONIQ 5 N for nCTRL, EV6 GT for gtCTRL), or the service maker's first
    /// when the flavor's default is another maker's.</summary>
    public static CarModel Default=>All.First(m=>m.Id==Brand.Flavor.DefaultCar) is var d&&d.Maker==Brand.Maker?d:All.First(m=>m.Maker==Brand.Maker);
    public static CarModel ById(string id)=>All.FirstOrDefault(m=>m.Id==id)??Default;

    /// <summary>The closest picture for the service's model name or code, or null when it names none of them.
    /// Best effort: the codes aren't documented, so the user can always pick another. Kia reports an EV6 GT as
    /// just "EV6", so a plain EV6 picks nothing and keeps the default (the EV6 GT).</summary>
    public static CarModel? Detect(string? modelCode)
    {
        if(string.IsNullOrWhiteSpace(modelCode))return null;
        var tokens=new List<string>();var word=new StringBuilder();
        foreach(char c in modelCode.ToUpperInvariant()) {
            if(char.IsAsciiLetterOrDigit(c))word.Append(c);
            else if(word.Length>0){tokens.Add(word.ToString());word.Clear();}
        }
        if(word.Length>0)tokens.Add(word.ToString());
        string code=string.Concat(tokens);
        // "N" on its own or at the end, but not N Line trims.
        bool n=!code.Contains("NLINE",StringComparison.Ordinal)&&(tokens.Contains("N")||code.EndsWith("N",StringComparison.Ordinal));
        if(code.Contains("EV6",StringComparison.Ordinal))return code.Contains("EV6GT",StringComparison.Ordinal)?ById("ev6gt"):null;
        if(code.Contains("EV9",StringComparison.Ordinal))return ById("ev9");
        if(code.Contains("IONIQ6",StringComparison.Ordinal))return ById(n?"ioniq6n":"ioniq6");
        if(code.Contains("IONIQ5",StringComparison.Ordinal))return ById(n?"ioniq5n":"ioniq5");
        if(code.Contains("KONA",StringComparison.Ordinal))return ById("konan");
        if(code.Contains("ELANTRA",StringComparison.Ordinal)||code.Contains("AVANTE",StringComparison.Ordinal))return ById("elantran");
        if(code.Contains("VELOSTER",StringComparison.Ordinal))return ById("velostern");
        if(code.Contains("GENESIS",StringComparison.Ordinal))return ById("genesis");
        return null;
    }
    // Names are shown in the header: one line, no control characters, at most MaxName characters.
    public static string CleanName(string name)
    {
        var sb=new StringBuilder();
        foreach(char c in name)sb.Append(char.IsControl(c)?' ':c);
        string clean=string.Join(' ',sb.ToString().Split(' ',StringSplitOptions.RemoveEmptyEntries));
        return clean.Length>MaxName?clean[..MaxName].TrimEnd():clean;
    }
}
