using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Rendering;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;
using SkiaSharp;

internal static partial class Program
{
    private sealed record NativeModal(bool Visible,bool OwnsWindow,bool ModalRegistered,long NativeHandle,string? Focus,
        string[] Text,Dictionary<string,string?> Fields,int PreviewViewports,int Surfaces,int Worlds,int StagedPreviews,int Width,int Height,bool ActionsVisible);
    private static MapDocument? _nativeScopedMap;

    private static async Task<bool> TryHandleNativeExtraCommandAsync(StudioWindow window,StudioPaths paths,string command)
    {
        if(command.StartsWith("replay-seek-job-check ",StringComparison.Ordinal))
            Console.WriteLine("REPLAY-SEEK-JOBS "+JsonSerializer.Serialize(await RunNativeReplaySeekJobCheckAsync(window,command["replay-seek-job-check ".Length..]),new JsonSerializerOptions{IncludeFields=true}));
        else if(command.StartsWith("capture-replay-job-world ",StringComparison.Ordinal))
            Console.WriteLine("REPLAY-JOB-WORLD "+JsonSerializer.Serialize(CaptureNativeReplayJobWorld(window,command["capture-replay-job-world ".Length..]),new JsonSerializerOptions{IncludeFields=true}));
        else if(command.StartsWith("dump-replay-capsule ",StringComparison.Ordinal))
        {
            CaptureReplayCapsule(((ProjectPrime.Studio.Replay.ReplayStudioDocument)window.Documents.ActiveDocument!).Session!.Player,command[20..]);
            Console.WriteLine("REPLAY-CAPSULE-DUMPED");
        }
        else if(await TryHandleNativeAdmissionCommandAsync(window,command))return true;
        else if(await TryHandleNativeAudioCommandAsync(window,paths,command))return true;
        else if(command.StartsWith("modal-primitive ",StringComparison.Ordinal))
        {
            ((MapStudioDocument)window.Documents.ActiveDocument!).Host.ShowPrimitiveDialog(command[16..]);
            Console.WriteLine("MODAL-OPENED");
        }
        else if(command.StartsWith("modal-model ",StringComparison.Ordinal))
        {
            ((MapStudioDocument)window.Documents.ActiveDocument!).Host.ShowModelImportDialog(command[12..]);
            Console.WriteLine("MODAL-OPENED");
        }
        else if(command is "modal-status" || command.StartsWith("modal-capture ",StringComparison.Ordinal))
        {
            var modal=(window.Documents.ActiveDocument as MapStudioDocument)?.ModalWindow;
            await Task.Delay(50);modal?.UpdateLayout();
            int width=0,height=0;
            if(command.StartsWith("modal-capture ",StringComparison.Ordinal)&&modal is not null)
            {
                double scale=modal.RenderScaling;
                using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new((int)Math.Round(modal.ClientSize.Width*scale),(int)Math.Round(modal.ClientSize.Height*scale)),new Vector(96*scale,96*scale));
                bitmap.Render(modal);bitmap.Save(command[14..],new Avalonia.Media.Imaging.PngBitmapEncoderOptions());width=bitmap.PixelSize.Width;height=bitmap.PixelSize.Height;
            }
            // Avalonia modal input blocking calls the native platform SetEnabled;
            // Window.IsEnabled remains the authored property. Its modal subscription
            // records the actual ShowDialog lifetime, including native modal creation.
            bool registered=modal is not null&&typeof(Window).GetField("_modalSubscription",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(modal) is not null;
            bool actionsVisible=modal is not null&&modal.GetVisualDescendants().OfType<Control>().Where(control=>control.Tag is MapStudioDialogAction)
                .All(control=>control.TranslatePoint(default,modal) is {} origin&&origin.X>=0&&origin.Y>=0
                    &&origin.X+control.Bounds.Width<=modal.ClientSize.Width+1&&origin.Y+control.Bounds.Height<=modal.ClientSize.Height+1);
            Console.WriteLine("MODAL "+JsonSerializer.Serialize(new NativeModal(modal?.IsVisible==true,ReferenceEquals(modal?.Owner,window),registered,
                modal?.TryGetPlatformHandle()?.Handle.ToInt64()??0,(modal?.FocusManager?.GetFocusedElement() as Control)?.Name,
                modal?.GetVisualDescendants().OfType<TextBlock>().Select(block=>block.Text??"").ToArray()??[],
                modal?.GetVisualDescendants().OfType<TextBox>().Where(box=>box.Name is not null).ToDictionary(box=>box.Name!,box=>box.Text)??[],
                modal?.GetVisualDescendants().Count(control=>control.GetType().Name=="MapViewport")??0,StudioGraphicsHost.NativeSurfaceCount,StudioGraphicsHost.ResidentWorldCount,
                Directory.Exists(paths.StagingDirectory)?Directory.GetDirectories(paths.StagingDirectory,"model-preview-*").Length:0,width,height,actionsVisible)));
        }
        else if(command.StartsWith("modal-fields ",StringComparison.Ordinal))
        {
            var modal=((MapStudioDocument)window.Documents.ActiveDocument!).ModalWindow!;
            var values=JsonSerializer.Deserialize<Dictionary<string,string>>(command[13..])!;
            foreach(var value in values)modal.GetVisualDescendants().OfType<TextBox>().Single(box=>box.Name==value.Key).Text=value.Value;
            foreach(var combo in modal.GetVisualDescendants().OfType<ComboBox>())
                if(combo.Items.Cast<object>().FirstOrDefault(item=>item.ToString()=="None") is {} none)combo.SelectedItem=none;
            Console.WriteLine("MODAL-FIELDS");
        }
        else if(command.StartsWith("modal-preview-capture ",StringComparison.Ordinal))
        {
            var modal=((MapStudioDocument)window.Documents.ActiveDocument!).ModalWindow!;
            var viewport=modal.GetVisualDescendants().OfType<MphRead.Mods.Launcher.Gui.MapViewport>().Single();
            var capture=viewport.CaptureStudioViewport()??throw new InvalidOperationException("Owned native model preview did not capture its GPU target.");
            MphRead.Mods.StudioReplay.StudioReplayPlayer.SavePng(command[22..],new(capture.Width,capture.Height,capture.Rgba));
            Console.WriteLine("MODAL-PREVIEW-CAPTURE "+JsonSerializer.Serialize(new{capture.Width,capture.Height}));
        }
        else if(command.StartsWith("modal-key ",StringComparison.Ordinal))
        {
            var modal=((MapStudioDocument)window.Documents.ActiveDocument!).ModalWindow!;
            modal.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Enum.Parse<Key>(command[10..]),Source=modal.FocusManager?.GetFocusedElement()??modal});
            Console.WriteLine("MODAL-ACTION");
        }
        else if(command=="modal-scroll-bottom")
        {
            var modal=((MapStudioDocument)window.Documents.ActiveDocument!).ModalWindow!;modal.Height=300;
            await Task.Delay(100);modal.UpdateLayout();
            var scroll=modal.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset=new(0,Math.Max(0,scroll.Extent.Height-scroll.Viewport.Height));modal.UpdateLayout();
            Console.WriteLine("MODAL-SCROLLED");
        }
        else if(command=="modal-cancel"||command=="modal-titlebar")
        {
            var modal=((MapStudioDocument)window.Documents.ActiveDocument!).ModalWindow!;
            if(command=="modal-titlebar")modal.Close();
            else
            {
                var button=modal.GetLogicalDescendants().OfType<Control>().Single(control=>control.Tag is MapStudioDialogAction action&&!action.IsDefault);
                ((MapStudioDialogAction)button.Tag!).Invoke();
            }
            Console.WriteLine("MODAL-ACTION");
        }
        else if(command is "map-undo" or "map-redo")
        {
            var host=((MapStudioDocument)window.Documents.ActiveDocument!).Host;if(command=="map-undo")host.Undo();else host.Redo();
            Console.WriteLine("MAP-HISTORY");
        }
        else if(command=="modal-close-document")
        {
            var document=(MapStudioDocument)window.Documents.ActiveDocument!;var modal=document.ModalWindow;
            bool closed=await window.Documents.RequestCloseAsync(document,_=>Task.FromResult(StudioCloseDecision.Discard),_=>Task.FromResult<string?>(null));
            Console.WriteLine("MODAL-DOCUMENT-CLOSED "+JsonSerializer.Serialize(new{Closed=closed,ModalReleased=document.ModalWindow is null,Visible=modal?.IsVisible==true,
                Surfaces=StudioGraphicsHost.NativeSurfaceCount,Worlds=StudioGraphicsHost.ResidentWorldCount,
                Staging=Directory.Exists(paths.StagingDirectory)?Directory.GetDirectories(paths.StagingDirectory,"model-preview-*").Length:0}));
        }
        else if(command=="modal-close-owner")
        {
            var document=(MapStudioDocument)window.Documents.ActiveDocument!;var modal=document.ModalWindow;Task<bool> closing=window.TryCloseAsync();
            var prompt=window.OwnedWindows.FirstOrDefault(owned=>owned.Title=="Unsaved changes");
            prompt?.GetVisualDescendants().OfType<Button>().Single(button=>button.Content?.ToString()=="Discard").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bool closed=await closing;
            Console.WriteLine("MODAL-OWNER-CLOSED "+JsonSerializer.Serialize(new{Closed=closed,ModalReleased=document.ModalWindow is null,Visible=modal?.IsVisible==true,
                Surfaces=StudioGraphicsHost.NativeSurfaceCount,Worlds=StudioGraphicsHost.ResidentWorldCount,
                Staging=Directory.Exists(paths.StagingDirectory)?Directory.GetDirectories(paths.StagingDirectory,"model-preview-*").Length:0}));
            Console.Out.Flush();if(closed)window.Close();
        }
        else if(command=="map-metrics-scope")
        {
            _nativeScopedMap=((MapStudioDocument)window.Documents.ActiveDocument!).Host.Document!;
            bool measured=StudioGraphicsHost.GetMapMetrics(_nativeScopedMap) is not null;
            var unrendered=new MapStudioDocument(paths,window,window.Jobs);unrendered.NewProject("UNRENDERED_METRIC_SOURCE");var empty=unrendered.Host.Document!;
            bool unknown=StudioGraphicsHost.GetMapMetrics(empty) is null;await unrendered.DisposeAsync();bool released=StudioGraphicsHost.GetMapMetrics(empty) is null;
            StudioGraphicsHost.Device.SimulateDeviceLossForDiagnostics();bool lost=StudioGraphicsHost.GetMapMetrics(_nativeScopedMap) is null;
            Console.WriteLine("MAP-METRICS-SCOPE "+JsonSerializer.Serialize(new{Measured=measured,UnrenderedUnknown=unknown,ReleasedUnknown=released,LostUnknown=lost}));
        }
        else if(command=="map-metrics-current")
        {
            Console.WriteLine("MAP-METRICS-CURRENT "+JsonSerializer.Serialize(new{Metrics=_nativeScopedMap is null?null:StudioGraphicsHost.GetMapMetrics(_nativeScopedMap),Generation=StudioGraphicsHost.DeviceGeneration}));
        }
        else if(command.StartsWith("replay-camera-reference ",StringComparison.Ordinal))
        {
            var document=(ProjectPrime.Studio.Replay.ReplayStudioDocument)window.Documents.ActiveDocument!;
            var player=document.Session!.Player;
            var viewport=document.Host.GetVisualDescendants().OfType<ProjectPrime.Studio.Replay.ReplayViewportHost>().First();
            var factory=viewport.ViewFactory;
            try
            {
                viewport.ViewFactory=(width,height)=>new(width,height,MphRead.Mods.StudioReplay.StudioReplayCameraMode.Authored,Fov:65,GameHud:false,ReplayOverlay:false,
                    PresentationFrame:0,PresentationAlpha:1);
                var capture=viewport.Capture(320,180)??throw new InvalidOperationException("Native authored camera reference capture failed.");
                MphRead.Mods.StudioReplay.StudioReplayPlayer.SavePng(command[24..],capture);
                Console.WriteLine("REPLAY-CAMERA-REFERENCE "+Convert.ToBase64String(player.ExportCameraSidecarState()));
            }
            finally{viewport.ViewFactory=factory;}
        }
        else if(command=="replay-mutate-camera")
        {
            var player=((ProjectPrime.Studio.Replay.ReplayStudioDocument)window.Documents.ActiveDocument!).Session!.Player;
            player.PutCameraKey(new(0,new(20,20,20),System.Numerics.Quaternion.Identity,Fov:110));
            await player.FlushCameraEditsAsync();Console.WriteLine("REPLAY-CAMERA-MUTATED");
        }
        else return false;
        Console.Out.Flush();return true;
    }

    private static async Task CheckNativeMapDialogsAsync(string directory,string output)
    {
        output=Path.GetFullPath(output);Directory.CreateDirectory(output);
        string source=Path.Combine(directory,"native-dialog-map.json");MapProjectSerializer.Save(MapTemplates.Create("NATIVE_DIALOG_ACCEPTANCE",true),source);
        byte[] immutable=File.ReadAllBytes(source);string profile=Path.Combine(directory,"native-dialog-profile");
        string model=Path.Combine(directory,"NativeDialogModel.obj"),material=Path.Combine(directory,"NativeDialogModel.mtl"),texture=Path.Combine(directory,"NativeDialogTexture.png");
        using(var bitmap=new SKBitmap(16,16)){for(int y=0;y<16;y++)for(int x=0;x<16;x++)bitmap.SetPixel(x,y,new SKColor((byte)(x*15),60,(byte)(y*15)));using var image=SKImage.FromBitmap(bitmap);using var bytes=image.Encode(SKEncodedImageFormat.Png,100);using var file=File.Create(texture);bytes.SaveTo(file);}
        File.WriteAllText(material,"newmtl NativeDialogMaterial\nKd 1 1 1\nmap_Kd NativeDialogTexture.png\n");
        File.WriteAllText(model,"mtllib NativeDialogModel.mtl\no NativeDialogQuad\nv -1 0 -1\nv 1 0 -1\nv 1 0 1\nv -1 0 1\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nusemtl NativeDialogMaterial\nf 1/1 2/2 3/3 4/4\n");
        var originals=new[]{model,material,texture}.ToDictionary(path=>path,File.ReadAllBytes);
        using Process process=await StartNativeProbeAsync(profile,["--map",source]);
        try
        {
            var before=(await NativeStatusAsync(process)).Map!;
            await NativeCommandAsync(process,"modal-primitive Box","MODAL-OPENED");
            var modal=await CaptureNativeModalAsync(process,Path.Combine(output,"native-primitive.png"));
            Check(modal is {Visible:true,OwnsWindow:true,ModalRegistered:true,NativeHandle: not 0,Focus:"PrimitiveName"},"actual primitive dialog has native owner, ShowDialog modal lifetime and initial text focus");
            await NativeCommandAsync(process,"modal-fields "+JsonSerializer.Serialize(new{PrimitiveScale="0, 1, 1"}),"MODAL-FIELDS");
            await NativeCommandAsync(process,"modal-key Enter","MODAL-ACTION");modal=await NativeModalStatusAsync(process);
            Check(modal.Visible&&modal.Text.Any(text=>text.Contains("positive numbers",StringComparison.Ordinal))&&(await NativeStatusAsync(process)).Map!.State==before.State,
                "native default action validates primitive scale and retains document state on failure");
            await NativeCommandAsync(process,"modal-fields "+JsonSerializer.Serialize(new{PrimitiveName="Acceptance Native Box",PrimitivePosition="20, 3, 20",PrimitiveScale="2, 2, 2"}),"MODAL-FIELDS");
            await NativeCommandAsync(process,"modal-key Enter","MODAL-ACTION");await WaitNativeModalClosedAsync(process);
            var created=(await NativeStatusAsync(process)).Map!;
            Check(created.CommandCount==before.CommandCount+1&&created.State!=before.State&&created.Dirty&&created.Selection.Length==1&&created.ActiveObject==created.Selection[0],
                "native Enter creates canonical primitive in one history command and selects its exact object");
            await NativeCommandAsync(process,"map-undo","MAP-HISTORY");
            Check((await NativeStatusAsync(process)).Map!.DefinitionHash==before.DefinitionHash,"one canonical Undo restores every native primitive authoring field");
            await NativeCommandAsync(process,"map-redo","MAP-HISTORY");
            foreach((string kind,string action) in new[]{("Wedge","modal-key Escape"),("Prism","modal-titlebar"),("Box","modal-cancel")})
            {
                var unchanged=(await NativeStatusAsync(process)).Map!;await NativeCommandAsync(process,"modal-primitive "+kind,"MODAL-OPENED");
                await NativeCommandAsync(process,action,"MODAL-ACTION");await WaitNativeModalClosedAsync(process);
                Check((await NativeStatusAsync(process)).Map!.DefinitionHash==unchanged.DefinitionHash&&(await NativeStatusAsync(process)).Map!.State==unchanged.State,
                    "native primitive "+action+" cancels without any canonical edit");
            }
            var beforeModel=(await NativeStatusAsync(process)).Map!;
            await OpenNativeModelPreviewAsync(process,model,Path.Combine(output,"native-model-options.png"),Path.Combine(output,"native-model-preview.png"));
            await NativeCommandAsync(process,"modal-cancel","MODAL-ACTION");await WaitNativeModalClosedAsync(process);
            modal=await NativeModalStatusAsync(process);
            Check((await NativeStatusAsync(process)).Map!.State==beforeModel.State&&modal.StagedPreviews==0&&modal.Surfaces==1&&modal.Worlds==1,
                "native model preview cancel releases staged texture files and GPU preview surface/world without authoring changes");
            await OpenNativeModelPreviewAsync(process,model,null,Path.Combine(output,"native-model-preview-accept.png"));
            await NativeCommandAsync(process,"modal-scroll-bottom","MODAL-SCROLLED");
            var reachable=await CaptureNativeModalAsync(process,Path.Combine(output,"native-model-preview-small-scrolled.png"));
            Check(reachable.ActionsVisible,"native small preview window scrolls both Import and Cancel buttons into the actual client bounds");
            await NativeCommandAsync(process,"modal-key Enter","MODAL-ACTION");await WaitNativeModalClosedAsync(process);
            var imported=(await NativeStatusAsync(process)).Map!;
            Check(imported.CommandCount==beforeModel.CommandCount+1&&imported.State!=beforeModel.State&&imported.DefinitionHash!=beforeModel.DefinitionHash,
                "native model preview default action applies geometry, dependencies and materials in one canonical history command");
            await NativeCommandAsync(process,"map-undo","MAP-HISTORY");
            Check((await NativeStatusAsync(process)).Map!.DefinitionHash==beforeModel.DefinitionHash,"one Undo restores all native model import fields");
            await NativeCommandAsync(process,"map-redo","MAP-HISTORY");
            await OpenNativeModelPreviewAsync(process,model,null,null);
            using(var closed=JsonDocument.Parse((await NativeCommandAsync(process,"modal-close-document","MODAL-DOCUMENT-CLOSED "))[22..]))CheckNativeModalCleanup(closed.RootElement,"document closure");
            using(Process forwarded=StartStudioExecutable(profile,["--map",source])){await forwarded.WaitForExitAsync();Check(forwarded.ExitCode==0,"fresh map reopens through authenticated production forwarding after native dialog document closure");}
            await WaitForNativeDocumentAsync(process,source);await WaitForNativeMapMetricsAsync(process,1);
            await OpenNativeModelPreviewAsync(process,model,null,null);
            using(var closed=JsonDocument.Parse((await NativeCommandAsync(process,"modal-close-owner","MODAL-OWNER-CLOSED "))[19..]))CheckNativeModalCleanup(closed.RootElement,"owner application closure");
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));Check(process.ExitCode==0,"native app exits cleanly while an owned model preview is open");
            Check(immutable.SequenceEqual(File.ReadAllBytes(source))&&originals.All(pair=>pair.Value.SequenceEqual(File.ReadAllBytes(pair.Key))),
                "native primitive/model modal workflows preserve source map and external model/material/texture bytes");
        }
        finally{if(!process.HasExited){process.Kill(entireProcessTree:true);await process.WaitForExitAsync();}}
    }

    private static void CheckNativeModalCleanup(JsonElement value,string scope)
        =>Check(value.GetProperty("Closed").GetBoolean()&&value.GetProperty("ModalReleased").GetBoolean()&&!value.GetProperty("Visible").GetBoolean()
            &&value.GetProperty("Surfaces").GetInt32()==0&&value.GetProperty("Worlds").GetInt32()==0&&value.GetProperty("Staging").GetInt32()==0,
            "native "+scope+" closes owned preview dialog and drains staging plus all GPU surfaces/worlds");
    private static async Task OpenNativeModelPreviewAsync(Process process,string source,string? optionsCapture,string? previewCapture)
    {
        await NativeCommandAsync(process,"modal-model "+source,"MODAL-OPENED");
        var options=optionsCapture is null?await NativeModalStatusAsync(process):await CaptureNativeModalAsync(process,optionsCapture);
        Check(options.Visible&&options.OwnsWindow&&options.ModalRegistered&&options.NativeHandle!=0&&options.Focus=="ModelImportScale","actual model options dialog owns native modal lifetime and initial scale focus");
        await NativeCommandAsync(process,"modal-fields "+JsonSerializer.Serialize(new{ModelImportScale="1"}),"MODAL-FIELDS");
        await NativeCommandAsync(process,"modal-key Enter","MODAL-ACTION");var clock=Stopwatch.StartNew();NativeModal preview;
        do{preview=await NativeModalStatusAsync(process);if(preview.Visible&&preview.PreviewViewports==1&&preview.Surfaces==2)break;await Task.Delay(10);}while(clock.Elapsed<TimeSpan.FromSeconds(20));
        Check(preview.Visible&&preview.OwnsWindow&&preview.ModalRegistered&&preview.NativeHandle!=0&&preview.PreviewViewports==1&&preview.Surfaces==2&&preview.Worlds==2&&preview.StagedPreviews>0,
            "actual model preview uses a second native GPU viewport/world and captured staged texture dependencies");
        if(previewCapture is not null)
        {
            await CaptureNativeModalAsync(process,previewCapture);
            string gpu=Path.Combine(Path.GetDirectoryName(previewCapture)!,Path.GetFileNameWithoutExtension(previewCapture)+"-gpu.png");
            using var size=JsonDocument.Parse((await NativeCommandAsync(process,"modal-preview-capture "+gpu,"MODAL-PREVIEW-CAPTURE "))[22..]);
            CheckNativePng(gpu,size.RootElement.GetProperty("Width").GetInt32(),size.RootElement.GetProperty("Height").GetInt32(),
                "owned native model preview explicitly captures its actual textured GPU geometry independently of Avalonia child airspace");
        }
    }
    private static async Task<NativeModal> CaptureNativeModalAsync(Process process,string path)
    {
        var modal=JsonSerializer.Deserialize<NativeModal>((await NativeCommandAsync(process,"modal-capture "+path,"MODAL "))[6..])!;
        CheckNativePng(path,modal.Width,modal.Height,"owned native modal renders readable Avalonia controls; GPU child pixels are validated separately by surface/world ownership");
        File.WriteAllText(Path.ChangeExtension(path,".json"),JsonSerializer.Serialize(modal,new JsonSerializerOptions{WriteIndented=true}));return modal;
    }
    private static async Task<NativeModal> NativeModalStatusAsync(Process process)=>JsonSerializer.Deserialize<NativeModal>((await NativeCommandAsync(process,"modal-status","MODAL "))[6..])!;
    private static async Task WaitNativeModalClosedAsync(Process process)
    {
        var clock=Stopwatch.StartNew();while(clock.Elapsed<TimeSpan.FromSeconds(20)){if(!(await NativeModalStatusAsync(process)).Visible)return;await Task.Delay(10);}
        throw new TimeoutException("Owned native modal did not close after canonical action.");
    }
    private static async Task<string> NativeCommandAsync(Process process,string command,string prefix)
    {await process.StandardInput.WriteLineAsync(command);await process.StandardInput.FlushAsync();return await ReadNativeLineAsync(process,prefix);}
}
