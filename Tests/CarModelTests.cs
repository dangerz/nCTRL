using System;
using System.IO;
using System.Linq;
using NGarage.Core;

static class CarModelTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Car models: "+label);count++;}
        string? root=Directory.GetCurrentDirectory();
        while(root!=null&&!File.Exists(Path.Combine(root,"project.godot")))root=Path.GetDirectoryName(root);
        Check(root!=null&&CarModels.All.All(m=>File.Exists(Path.Combine(root,"Assets",m.Picture))),"Every model's picture is in Assets");
        Check(CarModels.All.Select(m=>m.Id).Distinct().Count()==CarModels.All.Count&&CarModels.All.Select(m=>m.Name).Distinct().Count()==CarModels.All.Count,"Ids and names are unique");
        Check(CarModels.Default.Id=="ioniq5n"&&CarModels.ById("nope")==CarModels.Default&&CarModels.ById("ev9").Name=="Kia EV9","Unknown ids fall back to the IONIQ 5 N");
        Check(CarModels.All.Where(m=>m.Id.StartsWith("ev")||m.Id.StartsWith("i2")||m.Id.StartsWith("i3")).All(m=>m.Support==CarSupport.PictureOnly),"Kia and Europe-only models are pictures only");

        string? Id(string code)=>CarModels.Detect(code)?.Id;
        Check(Id("IONIQ 5 N")=="ioniq5n"&&Id("IONIQ5N")=="ioniq5n"&&Id("Ioniq 5")=="ioniq5"&&Id("IONIQ 5 N LINE")=="ioniq5","IONIQ 5 and 5 N codes, N Line is not N");
        Check(Id("IONIQ6N")=="ioniq6n"&&Id("IONIQ 6")=="ioniq6","IONIQ 6 and 6 N codes");
        Check(Id("IONIQ5NLINE")=="ioniq5"&&Id("KONA ELECTRIC")=="konan"&&Id("ELANTRA HEV")=="elantran"&&Id("Veloster")=="velostern"&&Id("GENESIS")=="genesis",
            "Other Hyundai models get the closest picture");
        Check(Id("")==null&&Id(null!)==null&&Id("SANTA FE")==null&&Id("TUCSON")==null,"Unknown or missing codes pick nothing");

        Check(CarModels.CleanName("  Blue   Thunder ")=="Blue Thunder"&&CarModels.CleanName("Line\nbreak\t")=="Line break","Names are one tidy line");
        Check(CarModels.CleanName(new string('x',40)).Length==CarModels.MaxName&&CarModels.CleanName("   ")=="","Names are capped, and blank stays blank");
        Console.WriteLine($"PASS: {count} car-model checks.");
    }
}
