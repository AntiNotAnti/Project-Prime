using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.MapEditor;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Replay;
using ProjectPrime.Studio.Settings;

namespace ProjectPrime.Studio.Shell;

public sealed record StudioSearchResult(string Category,string Title,string Detail,Action Activate)
{
    public override string ToString()=>Category+"  ·  "+Title+"\n"+Detail;
}

public sealed class StudioGlobalSearchWindow : Window
{
    public StudioGlobalSearchWindow(StudioDocumentHost documents,StudioCommandRouter commands,StudioSettings settings)
    {
        Title="Search Studio";Width=760;Height=500;MinWidth=480;MinHeight=300;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel { Margin=new Thickness(18) };
        var query=new TextBox { Name="StudioGlobalSearchQuery",PlaceholderText="Search commands, objects, assets, replay events or camera keys…",Margin=new Thickness(0,0,0,12) };
        var list=new ListBox { Name="StudioGlobalSearchResults" };
        DockPanel.SetDock(query,Dock.Top);root.Children.Add(query);root.Children.Add(list);Content=root;
        void Filter()
        {
            list.ItemsSource=Search(documents,commands,settings,query.Text??"").Take(100).ToArray();
            if (list.ItemCount>0) list.SelectedIndex=0;
        }
        void Activate()
        {
            if(list.SelectedItem is not StudioSearchResult result)return;
            Close();result.Activate();
        }
        query.TextChanged+=(_,_)=>Filter();
        query.KeyDown+=(_,e)=>
        {
            if(e.Key==Key.Enter){Activate();e.Handled=true;}
            else if(e.Key==Key.Down){list.SelectedIndex=Math.Min(list.ItemCount-1,list.SelectedIndex+1);e.Handled=true;}
            else if(e.Key==Key.Up){list.SelectedIndex=Math.Max(0,list.SelectedIndex-1);e.Handled=true;}
            else if(e.Key==Key.Escape){Close();e.Handled=true;}
        };
        list.DoubleTapped+=(_,_)=>Activate();Opened+=(_,_)=>query.Focus();Filter();
    }
    public static string CommandLabel(StudioCommand command)=>Regex.Replace(command.ToString(),"(?<=[a-z])([A-Z])"," $1");
    public static IEnumerable<StudioSearchResult> Search(StudioDocumentHost documents,StudioCommandRouter commands,
        StudioSettings settings,string query)
    {
        bool Match(string text)=>text.Contains(query,StringComparison.OrdinalIgnoreCase);
        foreach(var command in commands.AvailableCommands)
        {
            string label=CommandLabel(command);
            if(Match(label)&&commands[command].CanExecute(null))
                yield return new("Command",label,StudioHotkeys.Binding(settings,command),()=>commands[command].Execute(null));
        }
        foreach(var document in documents.Documents)
        {
            if(Match(document.Title+" "+document.Path))yield return new("Document",document.Title,document.Path??"Unsaved project",()=>documents.Select(document));
            if(document is MapStudioDocument {Host.Document: { } map} mapDocument)
            {
                foreach(var item in MapObjects.All(map.Project.Definition))
                {
                    if(!Match(item.Label+" "+item.Kind))continue;
                    Guid id=item.Id;
                    yield return new("Map object",item.Label,mapDocument.Title+" · "+item.Kind,()=>
                    {
                        documents.Select(document);map.Selection.Clear();map.Selection.Add(id);map.ActiveObjectId=id;map.SelectionChanged();mapDocument.Host.ShowPanel("Inspector");
                    });
                }
                foreach(var asset in map.Project.Definition.Assets)
                    if(Match(asset.Path+" "+asset.Kind))yield return new("Asset",asset.Path,mapDocument.Title+" · "+asset.Kind,
                        ()=>{documents.Select(document);mapDocument.Host.ShowPanel("Assets & music");});
                foreach(var material in map.Project.Definition.Materials)
                    if(Match(material.Name))yield return new("Material",material.Name,mapDocument.Title,
                        ()=>{documents.Select(document);mapDocument.Host.ShowPanel("Materials");});
                foreach (var source in map.Project.Definition.ModelSources)
                    if (Match(source.Source ?? "")) yield return new("Source file",source.Source ?? "Model source",mapDocument.Title,
                        ()=>{documents.Select(document);mapDocument.Host.ShowPanel("Assets & music");});
            }
            if(document is ReplayStudioDocument {Session: { } replay})
            {
                foreach (var player in replay.Player.Analytics())
                    if (Match(player.Name+" Player "+(player.Slot+1))) yield return new("Replay player",player.Name,
                        document.Title+" · "+player.Kills+" kills · "+player.Deaths+" deaths",()=>
                        { documents.Select(document);replay.PlayerSlot=player.Slot;replay.Camera=MphRead.Mods.StudioReplay.StudioReplayCameraMode.Player;replay.SavePresentation(); });
                foreach(var marker in replay.Player.Markers)
                    if(Match(marker.Name+" "+marker.Track))yield return new("Replay marker",marker.Name,document.Title+" · frame "+marker.StartFrame,
                        ()=>{documents.Select(document);replay.Player.Seek(marker.StartFrame);});
                foreach(var key in replay.Player.CameraKeys)
                {
                    string label="Camera key "+key.Frame;
                    if(Match(label))yield return new("Camera",label,document.Title,()=>{documents.Select(document);replay.Player.Seek(key.Frame);});
                }
                if(query.Length<2)continue;
                foreach(var item in replay.Player.Events)
                {
                    string label=item.Type+" · frame "+item.Frame;
                    if(Match(label))yield return new("Replay event",label,document.Title+" · actor "+item.Actor,
                        ()=>{documents.Select(document);replay.Player.Seek(item.Frame);});
                }
            }
        }
    }
}
