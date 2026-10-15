using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio.Jobs;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Rendering;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Diagnostics;

/// <summary>Device-free checks of the shipped desktop host and canonical engine.
/// This deliberately does not certify physical OpenGL parity or external tools.</summary>
internal static class StudioAcceptanceChecks
{
    public sealed class CheckApp : Application
    {
        public override void Initialize() => Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }
    public static int Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"prime-studio-host-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        int checks=0;
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks++;}
        void Complete(Task task)
        {
            while(!task.IsCompleted){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}
            task.GetAwaiter().GetResult();
        }
        try
        {
            StudioGameAssets.InitializeAuthoringProcess();
            StudioGraphicsHost.Initialize(true);
            AppBuilder.Configure<CheckApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont().SetupWithoutStarting();
            var paths=new StudioPaths(AppContext.BaseDirectory,root);
            string texture=Path.Combine(root,"test.tex");
            using(var output=new BinaryWriter(File.Create(texture)))
            {
                output.Write(System.Text.Encoding.ASCII.GetBytes("FPTX"));output.Write((ushort)1);output.Write((ushort)1);
                output.Write((ushort)0);output.Write((ushort)8);output.Write((ushort)8);output.Write((ushort)1);output.Write((ushort)0);
                output.Write((ushort)32767);output.Write(new byte[64]);
            }
            var definition=new MapDefinition{Name="STUDIO_HOST_CHECK",BaseDirectory=root,MapId=Guid.NewGuid(),FormatVersion=2};
            definition.Materials.Add(new(){Texture="test.tex"});definition.Assets.Add(new(){Path="test.tex"});
            definition.Geometry.Add(new MapBox{Transform=new(){Position=new[]{0f,-1,0},Scale=new[]{8f,1,8}}});
            definition.Spawns.Add(new(){Position=new[]{0f,2,0}});
            string source=Path.Combine(root,"source.json");MapProjectExport.Save(definition,source);
            var jobs=new StudioJobManager();var window=new Window();
            var documents=new StudioDocumentHost();
            for(int i=0;i<10;i++)
            {
                var map=new MapStudioDocument(paths,window,jobs);
                map.Host.ConfigureSourceOwnership(path=>documents.CanOpenSource(map,path),path=>documents.RequireSaveTarget(map,path));
                Complete(map.OpenAsync(source));documents.Add(map);
                Check(map.Host.Document!=null&&!map.Dirty,"canonical map document opens cleanly");
                var doc=map.Host.Document!;
                string? original=doc.Project.Definition.Description;
                doc.Edit("Acceptance edit",d=>d.Description="edited "+i);
                Check(map.Dirty&&doc.History.CanUndo,"host observes canonical editing history");
                map.Host.Undo();Check(doc.Project.Definition.Description==original&&!map.Dirty,"undo restores save state");
                map.Host.Redo();Check(map.Dirty,"redo restores edit");
                Complete(map.FlushAutosaveAsync());
                Check(map.RecoveryPath is {} recovery && File.Exists(recovery),"dirty map retains recovery snapshot");
                string target=Path.Combine(root,"saved-"+i+".json");Complete(map.SaveAsync(target,default));
                Check(!map.Dirty&&File.Exists(target),"Studio saves canonical source atomically");
                if(i==0)
                {
                    string secondSource=Path.Combine(root,"second.json");File.Copy(target,secondSource);
                    var second=new MapStudioDocument(paths,window,jobs);
                    second.Host.ConfigureSourceOwnership(path=>documents.CanOpenSource(second,path),path=>documents.RequireSaveTarget(second,path));
                    Complete(second.OpenAsync(secondSource));documents.Add(second);
                    string alias=Path.Combine(root,"source-alias.json");File.CreateSymbolicLink(alias,target);
                    Check(ReferenceEquals(documents.Find(StudioDocumentKind.Map,alias),map),"physical source aliases resolve to the owning tab");
                    var secondDocument=second.Host.Document;
                    Complete(second.Host.OpenAsync(alias));
                    Check(ReferenceEquals(second.Host.Document,secondDocument)&&ReferenceEquals(documents.ActiveDocument,map),"hosted inner open selects source owner without replacing another tab");
                    byte[] unchanged=File.ReadAllBytes(target);
                    bool refused=false;
                    try{Complete(second.SaveAsync(alias,default));}catch(IOException){refused=true;}
                    Check(refused&&File.ReadAllBytes(target).SequenceEqual(unchanged),"hosted inner Save As refuses an alias owned by another tab");
                    refused=false;
                    try{Complete(documents.SaveAsync(second,target));}catch(IOException){refused=true;}
                    Check(refused,"shell Save As shares source ownership protection");
                    Complete(documents.RequestCloseAsync(second,_=>Task.FromResult(StudioCloseDecision.Discard),_=>Task.FromResult<string?>(null)));
                    File.Delete(alias);
                    window.Content=map.Host;window.Width=1280;window.Height=800;window.Show();
                    Dispatcher.UIThread.RunJobs();
                    map.Host.Measure(new Size(1280,800));map.Host.Arrange(new Rect(0,0,1280,800));
                    Check(map.Host.DesiredSize.Width>0,"canonical host lays out real fonts and assets");
                    map.Host.ToggleFourViews();map.Host.Measure(new Size(1280,800));map.Host.Arrange(new Rect(0,0,1280,800));
                    map.Host.ToggleFourViews();
                    Check(map.Host.Document==doc,"single/four-view reparent retains canonical document");
                    doc.Edit("Leave open",d=>d.Description="unsaved");
                    bool closed=false;
                    Complete(documents.RequestCloseAsync(map,_=>Task.FromResult(StudioCloseDecision.Cancel),_=>Task.FromResult<string?>(null)).ContinueWith(t=>closed=t.Result));
                    Check(!closed&&documents.Documents.Contains(map),"cancel keeps editing document open");
                    doc.History.Undo();
                    var build=map.BuildScheduler.PackageAsync(MapBuildSnapshot.Capture(doc.Project),Path.Combine(root,"offline.ppmap"));
                    Complete(build);
                    var identity=MapContentIdentity.FromPackage(build.Result);
                    Check(identity.MapId==doc.Project.Definition.MapId&&!identity.PackageHash.IsZero,"closed-Game offline packaging retains exact identity");
                    window.Content=null;window.Close();
                }
                Complete(documents.RequestCloseAsync(map,_=>Task.FromResult(StudioCloseDecision.Discard),_=>Task.FromResult<string?>(null)));
                Check(map.State==StudioDocumentState.Closed&&documents.Documents.Count==0,"map lifecycle drains and closes");
            }
            Check(!StudioGraphicsHost.HasDevice&&StudioGraphicsHost.NativeSurfaceCount==0,"Safe Mode creates no native surfaces over ten lifecycles");
            Complete(documents.DisposeAsync().AsTask());Complete(jobs.DisposeAsync().AsTask());
            Console.WriteLine($"Studio host: {checks} checks passed (offline map editing/save/recovery/package; 10 document lifecycles; Safe Mode). Physical OpenGL and external services not exercised.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{StudioGraphicsHost.Shutdown();Directory.Delete(root,true);}
    }
}
