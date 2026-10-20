using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Campaign;
using Source=MphRead.Mods.Campaign.CampaignCreditsSource;

internal static class OwnedProbe
{
    internal static int Run(string ownedRoot)
    {
        string root=Path.GetFullPath(ownedRoot);
        string marmPath=Path.Combine(root,"frontend","metroidhunters.bin");
        string textPath=Path.Combine(root,"frontend","metroidhunters_text_en.bin");
        // These are the only two files read as payloads. Model/animation paths
        // below are existence metadata only; nothing is copied or decoded.
        byte[] marm=BoundedRead(marmPath),text=BoundedRead(textPath);
        string marmBefore=Hash(marm),textBefore=Hash(text);
        var document=Source.Read(marm,text);
        int items=document.Pages.Sum(p=>p.Items.Count),states=document.Pages.Sum(p=>p.Items.Sum(i=>i.States.Count));
        string[] paths=document.Widgets.Values.SelectMany(w=>w.Files).Where(f=>f.Path!=null).Select(f=>f.Path!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        int missing=0;
        foreach(string relative in paths)
        {
            string full=Path.GetFullPath(Path.Combine(root,relative));
            if(!full.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                throw new InvalidDataException("Admitted source asset escaped supplied root.");
            if(!File.Exists(full))missing++;
        }
        string marmAfter=Hash(BoundedRead(marmPath)),textAfter=Hash(BoundedRead(textPath));
        var actions=document.Pages.SelectMany(p=>p.Actions.Concat(p.Items.SelectMany(i=>i.Actions)).Concat(p.Timers.Select(t=>t.Action))).ToArray();
        var callbacks=actions.SelectMany(a=>a.Calls).Distinct().OrderBy(c=>c.A).ThenBy(c=>c.B).ToArray();
        var timers=document.Pages.SelectMany(p=>p.Timers).ToArray();
        bool passed=document.GlobalPageCount==67&&document.GlobalWidgetCount==4699&&document.GlobalStringCount==629
            &&document.Pages.Select(p=>p.Index).SequenceEqual(new[]{6,7,8,9,10,11,12,49,50})&&items==74&&states==213
            &&document.Widgets.Count==39&&document.Strings.Count==48&&paths.Length==25&&missing==0
            &&actions.Length==18&&timers.Length==7
            &&timers.Select(t=>t.DelayFx).SequenceEqual(new[]{140,140,140,140,170,140,60}.Select(t=>t*4096))
            &&callbacks.SequenceEqual(new[]{new Source.Call(0,11),new Source.Call(0,12),new Source.Call(0,206)})
            &&document.Pages.Sum(p=>p.Items.Sum(i=>i.States.Count(s=>s.Kind==0)))==39
            &&marm.Length==512232&&text.Length==30604
            &&marmBefore=="1140e510b420a9da5f4547154f0c164511a697ccaf5a249c0652625806a9d059"
            &&textBefore=="742e6819be3e9f8b55be65e66cc8a4d9a8aefc0a93d2f1e4813bb663d262d114"
            &&document.MarmSha256==marmBefore&&document.TextSha256==textBefore
            &&marmBefore==marmAfter&&textBefore==textAfter;
        var report=new {
            Schema=1,Passed=passed,SourceDataAdmissionOnly=true,PresentationActivated=false,SceneCreated=false,
            ModelOrAnimationPayloadsRead=false,UiAudioOrSaveTouched=false,SourcePayloadsWritten=false,
            SourceUnchanged=marmBefore==marmAfter&&textBefore==textAfter,
            MarmBytes=marm.Length,TextBytes=text.Length,MarmSha256Before=marmBefore,MarmSha256After=marmAfter,
            TextSha256Before=textBefore,TextSha256After=textAfter,document.GlobalPageCount,document.GlobalWidgetCount,document.GlobalStringCount,
            SelectedPages=document.Pages.Select(p=>new {p.Index,Items=p.Items.Count,States=p.Items.Sum(i=>i.States.Count),
                Actions=p.Actions.Count+p.Items.Sum(i=>i.Actions.Count),TimerDelayFx=p.Timers.Select(t=>t.DelayFx).ToArray()}).ToArray(),
            SelectedItemCount=items,SelectedStateCount=states,SelectedWidgetCount=document.Widgets.Count,SelectedStringCount=document.Strings.Count,
            SelectedWidgetIds=document.Widgets.Keys.Order().ToArray(),SelectedStringIds=document.Strings.Keys.Order().ToArray(),
            WidgetFilePathCount=paths.Length,MissingWidgetFilePaths=missing,ActionCountIncludingTimers=actions.Length,TimerCount=timers.Length,
            CallbackPairs=callbacks,
            document.HeaderField4,document.HeaderField6,document.HeaderField7,document.HeaderField16,document.PointerAndRecordVisits
        };
        Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        return passed?0:1;
    }
    private static byte[] BoundedRead(string path)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        long length=stream.Length;
        if(length<1||length>Source.MaxInputBytes)throw new InvalidDataException("Owned frontend input exceeds bounded admission extent.");
        byte[] bytes=new byte[(int)length];stream.ReadExactly(bytes);
        if(stream.ReadByte()!=-1||stream.Length!=length)throw new InvalidDataException("Owned frontend input changed extent during read.");
        return bytes;
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
