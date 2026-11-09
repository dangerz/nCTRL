using System;
using System.Linq;
using NGarage.Core;

// nCTRL (the original) and gtCTRL (the Kia EV6 GT build) from one codebase, and the Hyundai/Kia service choice.
static class FlavorTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Flavor: "+label);count++;}
        static int Spread(string hex){var c=new[]{0,2,4}.Select(i=>Convert.ToInt32(hex.Substring(i,2),16)).ToArray();return c.Max()-c.Min();}
        try
        {
            // nCTRL: unchanged defaults, Hyundai first, Kia as a choice.
            Brand.Use(Brand.NCtrl);
            Check(Brand.App=="nCTRL"&&Brand.ServiceId==Brand.Hyundai&&Brand.ChoosesService&&Brand.Maker=="Hyundai","nCTRL starts on Hyundai and offers Kia");
            Check(AppTheme.DefaultHex=="8ecbfa"&&AppTheme.Name(null)=="Performance Blue"&&CarModels.Default.Id=="ioniq5n","nCTRL keeps Performance Blue and the IONIQ 5 N");
            Check(Disclaimer.Version==1&&Disclaimer.Text.StartsWith("nCTRL is an independent")&&Disclaimer.Text.Contains("Hyundai Motor America"),"nCTRL's Hyundai disclaimer is the original version 1 text");
            Check(CarModels.Offered.Count==CarModels.All.Count&&CarModels.SupportFor(CarModels.ById("ev6gt"))==CarSupport.PictureOnly,"Hyundai accounts see every picture; Kia's are pictures only");
            Brand.ServiceId=Brand.Kia;
            Check(Brand.Maker=="Kia"&&Brand.Service.OneTimeCode&&!Brand.Service.NeedsPin&&CarModels.Default.Maker=="Kia","Choosing Kia switches the service and a Kia default picture");
            Check(Disclaimer.Version==2&&Disclaimer.Text.Contains("Kia America")&&Disclaimer.Text.Contains("nCTRL")&&!Disclaimer.Text.Contains("gtCTRL"),"Kia has its own disclaimer, named for the build");
            Check(CarModels.Offered.All(m=>m.Maker=="Kia")&&CarModels.SupportFor(CarModels.ById("ev6"))==CarSupport.Electric,"Kia accounts see only Kia pictures, which connect");
            Check(Brand.Connect(new RequestBudget(new MemoryBudgetStore())) is KiaUsClient,"Kia connects with the Kia client");
            // Canada: the same makers through Bluelink Canada and Kia Connect Canada, sharing each maker's disclaimer.
            Brand.ServiceId=Brand.HyundaiCanada;
            Check(Brand.Maker=="Hyundai"&&Brand.Service.ServiceName=="Bluelink Canada"&&Brand.Service.NeedsPin&&Brand.Service.OneTimeCode&&!Brand.Service.SendToCar&&
                Disclaimer.Version==1&&Brand.Connect(new RequestBudget(new MemoryBudgetStore())) is CanadaClient {CarBrand: CanadaBrand.Hyundai},"Hyundai Canada: Hyundai's disclaimer, the Canada client");
            Brand.ServiceId=Brand.KiaCanada;
            Check(Brand.Maker=="Kia"&&Brand.Service.ServiceName=="Kia Connect Canada"&&Disclaimer.Version==2&&CarModels.Offered.All(m=>m.Maker=="Kia")&&
                Brand.Connect(new RequestBudget(new MemoryBudgetStore())) is CanadaClient {CarBrand: CanadaBrand.Kia},"Kia Canada: Kia's disclaimer and pictures, the Canada client");
            Check(Brand.InCanada(Brand.KiaCanada)&&!Brand.InCanada(Brand.Kia)&&Brand.Known(Brand.HyundaiCanada)&&!Brand.Known("toyota"),"Canada services are known and marked");
            Brand.ServiceId="nope";
            Check(Brand.ServiceId==Brand.Hyundai,"An unknown service falls back to the flavor's first");
            Check(Brand.Connect(new RequestBudget(new MemoryBudgetStore())) is HyundaiUsClient,"Hyundai connects with the Hyundai client");

            // gtCTRL: Kia only, GT Neon, EV6 GT.
            Brand.Use(Brand.GtCtrl);
            Check(Brand.App=="gtCTRL"&&Brand.ServiceId==Brand.Kia&&Brand.Flavor.Services.SequenceEqual(new[]{Brand.Kia,Brand.KiaCanada}),"gtCTRL is Kia only: Kia US first, Kia Canada as a choice");
            Brand.ServiceId=Brand.Hyundai;
            Check(Brand.ServiceId==Brand.Kia,"gtCTRL can't switch to Hyundai");
            Brand.ServiceId=Brand.HyundaiCanada;
            Check(Brand.ServiceId==Brand.Kia,"gtCTRL can't switch to Hyundai Canada either");
            Check(AppTheme.DefaultHex==AppTheme.NeonHex&&AppTheme.Name(null)=="GT Neon"&&AppTheme.Name(AppTheme.BaseHex)=="Ice Blue"&&
                ReferenceEquals(AppTheme.Derive(null),AppTheme.Neon)&&ReferenceEquals(AppTheme.Derive(AppTheme.BaseHex),AppTheme.Base),"GT Neon is the default; the blue is Ice Blue");
            Check(AppTheme.Shift(null)==(0,0)&&AppTheme.Shift(AppTheme.BaseHex)==(0,1),"GT Neon takes the color out of picture backdrops");
            var roles=Enum.GetValues<ThemeRole>();var n=AppTheme.Neon;
            Check(roles.All(n.ContainsKey)&&n.Where(r=>r.Key is not (ThemeRole.Accent or ThemeRole.AccentHover or ThemeRole.AccentPressed)).All(r=>Spread(r.Value)<=10),"GT Neon's panels and text are graphite");
            foreach(var bg in new[]{ThemeRole.Back,ThemeRole.Card,ThemeRole.Button,ThemeRole.Header,ThemeRole.Nav,ThemeRole.Hero,ThemeRole.PanelFill})
                Check(AppTheme.Contrast(n[ThemeRole.Text],n[bg])>=7,"GT Neon text on "+bg+" is 7:1 or better");
            foreach(var (fg,bg) in new[]{(ThemeRole.Muted,ThemeRole.Card),(ThemeRole.Muted,ThemeRole.Back),(ThemeRole.Accent,ThemeRole.Back),(ThemeRole.Accent,ThemeRole.Card),
                (ThemeRole.Back,ThemeRole.Accent),(ThemeRole.Back,ThemeRole.AccentHover),(ThemeRole.Back,ThemeRole.AccentPressed),(ThemeRole.Text,ThemeRole.Notice),(ThemeRole.Text,ThemeRole.Selected)})
                Check(AppTheme.Contrast(n[fg],n[bg])>=4.5,$"GT Neon {fg} on {bg} is 4.5:1 or better");
            Check(CarModels.Default.Id=="ev6gt"&&CarModels.Default.ShortName=="EV6 GT"&&CarModels.Offered.Select(m=>m.Id).SequenceEqual(new[]{"ev6gt","ev6","ev9"}),"gtCTRL offers the Kia pictures, EV6 GT first");
            Check(CarModels.Detect("EV6")==null&&CarModels.Detect("EV6 GT")?.Id=="ev6gt"&&CarModels.Detect("EV9")?.Id=="ev9","Kia reports an EV6 GT as EV6, which keeps the EV6 GT default");
            Check(Disclaimer.Version==2&&Disclaimer.Text.StartsWith("gtCTRL is an independent")&&!Disclaimer.Text.Contains("nCTRL"),"gtCTRL's disclaimer");
            Check(Brand.Flavor.DefaultPaintHex=="525252"&&Brand.Flavor.Detail==null&&!Brand.Flavor.Demo,"Gray paint, details follow the app color, no demo");
            Check(Brand.ById("gtctrl")==Brand.GtCtrl&&Brand.ById("other")==Brand.NCtrl&&Brand.ById(null)==Brand.NCtrl,"Flavor ids");

            // The saved login remembers its service; older logins are Hyundai's.
            var kia=SavedAccount.Decode(new SavedAccount("a@example.invalid","pw","","r","d",Brand.Kia).Encode());
            var old=SavedAccount.Decode("{\"version\":1,\"email\":\"a@example.invalid\",\"password\":\"pw\",\"pin\":\"1234\"}");
            Check(kia.Service==Brand.Kia&&old.Service==Brand.Hyundai&&kia.WithRemember("r2","d2").Service==Brand.Kia,"Saved logins keep their service");
            bool refused=false;try{_=new SavedAccount("a@example.invalid","pw","","","","toyota");}catch(VaultException){refused=true;}
            Check(refused,"An unknown service is refused");
            var canada=SavedAccount.Decode(new SavedAccount("a@example.invalid","pw","1234","r","d",Brand.HyundaiCanada).Encode());
            Check(canada.Service==Brand.HyundaiCanada&&canada.Pin=="1234","A Canada login keeps its service and PIN");
        }
        finally { Brand.Use(Brand.NCtrl); }
        Console.WriteLine($"PASS: {count} flavor and service checks.");
    }
}
