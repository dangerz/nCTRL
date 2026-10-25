using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NGarage.Core;

// What nCTRL can do with a model. Electric models get every control; gas models connect through the same
// Hyundai USA service, but nCTRL's climate and charging controls are built for electric models.
public enum CarSupport { Electric, Gas, PictureOnly }
public sealed record CarModel(string Id,string Name,string Picture,CarSupport Support)
{
    /// <summary>The top-down picture for the map marker (front up, transparent), named after the side picture.</summary>
    public string TopPicture=>Picture.Replace(".png","_top.png");
}

/// <summary>The car pictures nCTRL can show. The picture only changes how the car looks; status and
/// controls always come from the enrolled vehicle.</summary>
public static class CarModels
{
    public const int MaxName=20;
    public static readonly IReadOnlyList<CarModel> All=new CarModel[] {
        new("ioniq5n","IONIQ 5 N","ioniq_pixel.png",CarSupport.Electric),
        new("ioniq5","IONIQ 5","hyundai_ioniq_5_pixel.png",CarSupport.Electric),
        new("ioniq6n","IONIQ 6 N","hyundai_ioniq_6_n_pixel.png",CarSupport.Electric),
        new("ioniq6","IONIQ 6","hyundai_ioniq_6_pixel.png",CarSupport.Electric),
        new("konan","Kona N","hyundai_kona_n_pixel.png",CarSupport.Gas),
        new("elantran","Elantra N","hyundai_elantra_n_pixel.png",CarSupport.Gas),
        new("velostern","Veloster N","hyundai_veloster_n_pixel.png",CarSupport.Gas),
        new("genesis","Genesis","hyundai_genesis_pixel.png",CarSupport.Gas),
        // Not sold in the US, or not on Hyundai USA's service: pictures only.
        new("i20n","i20 N","hyundai_i20_n_pixel.png",CarSupport.PictureOnly),
        new("i30n","i30 N","hyundai_i30_n_pixel.png",CarSupport.PictureOnly),
        new("i30fastbackn","i30 Fastback N","hyundai_i30_fastback_n_pixel.png",CarSupport.PictureOnly),
        new("ev6","Kia EV6","kia_ev6_pixel.png",CarSupport.PictureOnly),
        new("ev6gt","Kia EV6 GT","kia_ev6_gt_pixel.png",CarSupport.PictureOnly),
        new("ev9","Kia EV9","kia_ev9_pixel.png",CarSupport.PictureOnly)
    };
    public static CarModel Default=>All[0];
    public static CarModel ById(string id)=>All.FirstOrDefault(m=>m.Id==id)??Default;

    /// <summary>The closest picture for Hyundai's model code, or null when the code names none of them.
    /// Best effort: Hyundai's codes aren't documented, so the user can always pick another.</summary>
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
