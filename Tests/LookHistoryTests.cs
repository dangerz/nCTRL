using System;
using System.Linq;
using NGarage.Core;

static class LookHistoryTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool value,string label){if(!value)throw new Exception("Look around history: "+label);count++;}
        var t=new DateTimeOffset(2026,11,9,12,0,0,TimeSpan.Zero);
        var list=(System.Collections.Generic.IReadOnlyList<LookEntry>)Array.Empty<LookEntry>();
        var files=Enumerable.Range(0,12).Select(_=>LookHistory.NewFile()).ToList();
        Check(files.All(LookHistory.ValidFile)&&!LookHistory.ValidFile("../look-0123.jpg")&&!LookHistory.ValidFile("look-"+new string('g',32)+".jpg")&&!LookHistory.ValidFile("car.png"),
            "Only our own picture names are accepted");
        System.Collections.Generic.IReadOnlyList<string> removed=Array.Empty<string>();
        for(int i=0;i<12;i++)(list,removed)=LookHistory.Add(list,new LookEntry(files[i],"20261109"+i.ToString("D2"),t.AddMinutes(i)));
        Check(list.Count==LookHistory.Max&&list[0].File==files[11]&&list[^1].File==files[2],"Keeps the newest 10, newest first");
        Check(removed.SequenceEqual(new[]{files[1]}),"Says which picture fell off the end so its file is deleted");
        var back=LookHistory.Decode(LookHistory.Encode(list));
        Check(back.SequenceEqual(list),"The index round-trips");
        var fewer=LookHistory.Remove(list,files[5]);
        Check(fewer.Count==9&&fewer.All(e=>e.File!=files[5]),"Deletes one picture");
        Check(LookHistory.Decode("not json").Count==0&&LookHistory.Decode("{\"version\":1,\"pictures\":[{\"file\":\"../../etc/passwd\"},{\"file\":\""+files[0]+"\"},{\"file\":\""+files[0]+"\"}]}").Count==1,
            "A damaged index, foreign names and duplicates are dropped");
        Check(!LookHistory.Encode(list).Contains("lat",StringComparison.Ordinal),"No location is kept with the pictures");
        Console.WriteLine($"PASS: {count} Look around history checks.");
    }
}
