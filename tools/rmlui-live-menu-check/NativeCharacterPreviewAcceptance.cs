using MphRead;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Import;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;

internal static class NativeCharacterPreviewAcceptance
{
    internal static void Run(MenuSettings settings)
    {
        string root = RetroCharacterPreviewExporter.DefaultRoot;
        Check(!Directory.Exists(root), "fixture inventory starts empty in isolated user data");
        try
        {
            Requests(settings);
            CancellationOutcomes(settings);
            SourcePreviewRequests(settings);
            SourcePreviewPages(settings);
            using (var dirty = new NativeFullSettingsController(settings))
            {
                string[] engine = dirty.Rows.Select(r => r.Read()).ToArray();
                var row = dirty.Rows.Single(r => r.Category == "Audio" && r.Label == "Sfx Volume (%)");
                Check(dirty.Stage(row, row.Value == "65" ? "35" : "65") && dirty.Dirty, "fixture stages detached settings draft");
                dirty.RequestCharacterImport(false); dirty.RequestRemoveCharacterPreview();
                Check(!dirty.Busy && !dirty.ArenaImportRequested && dirty.Confirmation.Length == 0, "dirty draft refuses import/removal");
                dirty.CheckCharacterPreview(); Wait(dirty);
                Check(dirty.Dirty && dirty.Rows.Select(r => r.Read()).SequenceEqual(engine), "read-only check preserves draft and engine owners");
                dirty.Discard();
            }
            var sources = RetroCharacterPreviewExporter.Sources.Take(2).ToArray();
            foreach (var source in sources)
            {
                var spec = RetroCharacterPreviewExporter.BundleSpec(source.Id);
                LocalImportBundle.Publish(root, spec, spec.RequiredComponents.ToDictionary(c => c, c => Encoding.UTF8.GetBytes("synthetic fixture: " + c)));
            }
            using (var parts = new NativeFullSettingsController(settings))
            {
                var source = sources.Single(source => source.Id == "darksamus-echoes");
                while (parts.CharacterPreviewId != source.Id) parts.CycleCharacterPreview();
                parts.CheckCharacterPreview(); Wait(parts);
                Check(parts.Status.Contains("body v4: Ready", StringComparison.Ordinal)
                    && parts.Status.Contains("ball v3: Ready", StringComparison.Ordinal)
                    && parts.Status.Contains("Source authoring bundle;", StringComparison.Ordinal)
                    && parts.Status.Contains("normal Echoes supports an animated diagnostic preview", StringComparison.Ordinal)
                    && parts.Status.Contains("playable animation/morph remain unqualified.", StringComparison.Ordinal),
                    "complete verified synthetic v5 bundle reports body-v4 and ball-v3 Ready with explicit diagnostic-only and gameplay qualification limits");
                var spec = RetroCharacterPreviewExporter.BundleSpec(source.Id);
                foreach(var component in new[]{(".obj","geometry"),(".cinf.bin","skeleton-source"),(".animations.bin","animation-resources"),(".source.glb","source-rig-geometry"),(".source.json","generated-provenance")})
                {
                    var installed = LocalImportBundle.Inspect(root, source.Id, spec);
                    string name = RetroCharacterPreviewExporter.PartStem(source.Id, "ball") + component.Item1;
                    File.Delete(installed.Files.Single(file => Path.GetFileName(file) == name));
                    parts.CheckCharacterPreview(); Wait(parts);
                    Check(parts.Status.Contains("body v4: Ready", StringComparison.Ordinal)
                        && parts.Status.Contains("ball v3: Incomplete", StringComparison.Ordinal)
                        && parts.Status.Contains(component.Item2+" v1 Incomplete", StringComparison.Ordinal)
                        && parts.Status.Contains("Source authoring bundle;", StringComparison.Ordinal)
                        && parts.Status.Contains("playable animation/morph remain unqualified.", StringComparison.Ordinal),
                        "missing required ball component leaves body Ready and identifies exact incomplete role without playable qualification: "+component.Item2);
                    LocalImportBundle.Publish(root, spec, spec.RequiredComponents.ToDictionary(c => c, c => Encoding.UTF8.GetBytes("synthetic fixture: " + c)));
                    parts.CheckCharacterPreview(); Wait(parts);
                    Check(parts.Status.Contains("body v4: Ready", StringComparison.Ordinal)
                        && parts.Status.Contains("ball v3: Ready", StringComparison.Ordinal),
                        "complete republish repairs v5 synthetic component fixture before removal and snapshot checks: "+component.Item2);
                }
            }
            using(var manifestOwner=new NativeFullSettingsController(settings))
            {
                const string normal="darksamus-echoes";
                while(manifestOwner.CharacterPreviewId!=normal)manifestOwner.CycleCharacterPreview();
                var spec=RetroCharacterPreviewExporter.BundleSpec(normal);
                foreach (var component in new[] { ("characters.json", "source-preview-manifest v2"), ("darksamus-echoes.cannon-source.json", "source-cannon-recipe v1") })
                {
                var installed=LocalImportBundle.Inspect(root,normal,spec);
                File.Delete(installed.Files.Single(file=>Path.GetFileName(file)==component.Item1));
                manifestOwner.CheckCharacterPreview();Wait(manifestOwner);
                Check(manifestOwner.Status.Contains("body v4: Incomplete",StringComparison.Ordinal)
                    &&manifestOwner.Status.Contains(component.Item2+" Incomplete",StringComparison.Ordinal)
                    &&manifestOwner.Status.Contains("ball v3: Ready",StringComparison.Ordinal),
                    "missing normal manifest identifies incomplete body without hiding verified ball components");
                LocalImportBundle.Publish(root,spec,spec.RequiredComponents.ToDictionary(c=>c,c=>Encoding.UTF8.GetBytes("synthetic fixture: "+c)));
                manifestOwner.CheckCharacterPreview();Wait(manifestOwner);
                Check(manifestOwner.Status.Contains("body v4: Ready",StringComparison.Ordinal)&&manifestOwner.Status.Contains("ball v3: Ready",StringComparison.Ordinal),
                    "normal component repair restores complete synthetic inventory before removal checks");
                }
            }
            var all = Snapshot(root);
            using (var remove = new NativeFullSettingsController(settings))
            {
                string captured = remove.CharacterPreviewId;
                remove.RequestRemoveCharacterPreview();
                Check(remove.Confirmation.Contains(captured), "confirmation names captured source identity");
                remove.CycleCharacterPreview();
                Check(remove.CharacterPreviewId != captured, "direct cycle exercises confirmation identity race");
                Check(!remove.Operation(243) && Same(all, Snapshot(root)), "direct removal action requires confirmation dispatch");
                Check(remove.Operation(24) && remove.Confirmation.Length == 0 && Same(all, Snapshot(root)), "confirmation cancel deletes nothing");
                Check(!remove.Operation(23), "cancelled confirmation cannot be replayed");
                // Return to initial appearance, then change selection after capture.
                while (remove.CharacterPreviewId != captured) remove.CycleCharacterPreview();
                remove.RequestRemoveCharacterPreview(); remove.CycleCharacterPreview();
                Check(remove.Operation(23), "confirmed removal dispatched"); Wait(remove);
                Check(LocalImportBundle.Inspect(root, captured).State == ImportBundleState.NotInstalled
                    && LocalImportBundle.Inspect(root, remove.CharacterPreviewId).State == ImportBundleState.Ready,
                    "confirmed removal uses captured identity, leaves newly selected source active");
                Check(all.Where(p => !p.Key.EndsWith("current.json", StringComparison.Ordinal)).All(p => Snapshot(root).GetValueOrDefault(p.Key) == p.Value),
                    "removal retains every immutable generation byte");
            }
            using (var retired = new NativeFullSettingsController(settings))
            {
                retired.CycleCharacterPreview(); retired.RequestRemoveCharacterPreview();
                var before = Snapshot(root); retired.Dispose();
                Check(!retired.Operation(23) && !retired.Operation(243) && retired.Confirmation.Length == 0,
                    "disposed owner cannot confirm or dispatch retained removal");
                retired.CheckCharacterPreview(); retired.RequestRemoveCharacterPreview(); retired.RequestCharacterImport(true); retired.RequestArenaImport();
                retired.BeginArenaImport("missing-owned-source.iso");
                Check(!retired.Busy && !retired.ArenaImportRequested && Same(before, Snapshot(root)), "retired route cannot publish new work or files");
            }
            using (var closed = new NativeFullSettingsController(settings))
            {
                Check(closed.Back() && closed.Closed, "clean controller closes");
                closed.RequestCharacterImport(true); closed.RequestArenaImport(); closed.CheckCharacterPreview(); closed.RequestRemoveCharacterPreview();
                Check(!closed.Busy && !closed.ArenaImportRequested && !closed.Operation(243), "closed owner refuses new requests and direct operation");
            }
            using (var running = new NativeFullSettingsController(settings))
            using (var cancellation = new CancellationTokenSource())
            {
                var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                Set(running, "_operation", completion.Task); Set(running, "_arenaCancellation", cancellation);
                string id = running.CharacterPreviewId; var before = Snapshot(root); string[] engine = running.Rows.Select(r => r.Read()).ToArray();
                running.CycleCharacterPreview(); running.CheckCharacterPreview(); running.RequestRemoveCharacterPreview(); running.RequestCharacterImport(true);
                Check(running.CharacterPreviewId == id && ReferenceEquals(Get(running,"_operation"), completion.Task)
                    && running.Confirmation.Length == 0 && !running.ArenaImportRequested, "busy owner cannot replace work, selection or confirmation");
                running.Dispose(); Check(cancellation.IsCancellationRequested, "route teardown cancels active import token");
                completion.SetResult("late detached worker completion");
                Check(running.Rows.Select(r => r.Read()).SequenceEqual(engine) && Same(before, Snapshot(root)), "late worker completion publishes no settings or inventory state");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void SourcePreviewRequests(MenuSettings settings)
    {
        using var owner=new NativeFullSettingsController(settings);
        string[] engine=owner.Rows.Select(r=>r.Read()).ToArray();
        var row=owner.Rows.Single(r=>r.Category=="Audio"&&r.Label=="Sfx Volume (%)");
        Check(owner.Stage(row,row.Value=="65"?"35":"65")&&owner.Dirty,"source preview fixture stages a detached dirty draft");
        string[] draft=owner.Rows.Select(r=>r.Value).ToArray();string id=owner.CharacterPreviewId;
        owner.RequestCharacterSourcePreview();owner.CycleCharacterPreview();
        Check(owner.CharacterPreviewId!=id&&owner.TakeCharacterSourcePreview(out string captured)&&captured==id
            &&!owner.TakeCharacterSourcePreview(out _)&&owner.Dirty&&owner.Rows.Select(r=>r.Value).SequenceEqual(draft)
            &&owner.Rows.Select(r=>r.Read()).SequenceEqual(engine),"read-only preview consumes captured canonical ID once after selection changes without committing dirty draft");
        var pending=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(owner,"_operation",pending.Task);owner.RequestCharacterSourcePreview();
        Check(!owner.TakeCharacterSourcePreview(out _),"busy owner refuses source preview request/take");
        Set(owner,"_operation",null!);owner.RequestCharacterSourcePreview();Set(owner,"_operation",pending.Task);
        Check(!owner.TakeCharacterSourcePreview(out _),"work beginning after request fences preview consumption");
        Set(owner,"_operation",null!);Check(owner.TakeCharacterSourcePreview(out _),"finished work preserves captured source request");
        var binding=owner.Rows.First(r=>r.IsBinding);owner.BeginCapture(binding);owner.RequestCharacterSourcePreview();
        Check(owner.Capturing&&!owner.TakeCharacterSourcePreview(out _),"active binding capture refuses source preview request");
        owner.SuspendInput("fixture cancels capture");owner.RequestCharacterSourcePreview();owner.BeginCapture(binding);
        Check(!owner.TakeCharacterSourcePreview(out _),"capture beginning after request still fences preview consumption");
        owner.SuspendInput("fixture resumes draft");Check(owner.TakeCharacterSourcePreview(out _),"captured request resumes once capture ends");
        owner.RequestCharacterSourcePreview();Set(owner,"Confirmation","invented outstanding confirmation");
        Check(!owner.TakeCharacterSourcePreview(out _),"confirmation beginning after request fences preview consumption");
        Set(owner,"Confirmation","");Check(owner.TakeCharacterSourcePreview(out _),"confirmation completion preserves the original request");
        owner.RequestCharacterSourcePreview();Set(owner,"_videoBefore",LauncherPrefs.WindowMode);
        Check(!owner.TakeCharacterSourcePreview(out _),"video confirmation beginning after request fences preview consumption");
        owner.RequestCharacterSourcePreview();Set(owner,"_videoBefore",null!);
        Check(owner.TakeCharacterSourcePreview(out _),"video trial completion preserves captured request without applying the draft");
        Set(owner,"_videoBefore",LauncherPrefs.WindowMode);owner.RequestCharacterSourcePreview();
        Check(!owner.TakeCharacterSourcePreview(out _),"active video trial refuses new preview request");
        Set(owner,"_videoBefore",null!);Check(!owner.TakeCharacterSourcePreview(out _),"refused trial request is not queued for later");
        Check(owner.Dirty&&owner.Rows.Select(r=>r.Value).SequenceEqual(draft)&&owner.Rows.Select(r=>r.Read()).SequenceEqual(engine),
            "all preview request gates preserve original settings engine and dirty draft");
        owner.Dispose();owner.RequestCharacterSourcePreview();Check(!owner.TakeCharacterSourcePreview(out _),"disposed controller cannot issue a source preview");
        using var closed=new NativeFullSettingsController(settings);closed.RequestCharacterSourcePreview();Check(closed.Back(),"clean source-request controller closes");
        Check(!closed.TakeCharacterSourcePreview(out _),"closed controller cannot consume a retained preview request");
    }
    private static void SourcePreviewPages(MenuSettings settings)
    {
        var native=new PreviewBridge();using var host=new RmlUiHost(native);
        Check(host.Initialize(640,480,1,".",RmlUiRenderBackend.DrawList),"invented CPU bridge initializes actual managed preview page ownership");
        using var pages=new RmlUiPageManager(host);
        using(var page=new NativeFullSettingsPage(host,pages,settings))
        {
            page.Open();var token=page.Document;var controller=(NativeFullSettingsController)Get(page,"_controller")!;
            string id=controller.CharacterPreviewId;
            Check(!page.Handle(new(RmlUiIntentKind.SettingsAction,245,new(token.Generation,token.DocumentId+1000),1))
                &&!page.TryTakeCharacterSourcePreview(out _),"stale document cannot enqueue preview action245");
            Check(page.Handle(new(RmlUiIntentKind.SettingsAction,245,token,1)),"live native Settings accepts explicit preview action245");
            controller.CycleCharacterPreview();
            var modal=pages.OpenModal(new("preview-fixture-modal","own-modal.rml"));
            Check(!page.TryTakeCharacterSourcePreview(out _),"covered Settings cannot consume captured preview request");
            Check(pages.CloseModal()&&page.TryTakeCharacterSourcePreview(out string captured)&&captured==id
                &&!page.TryTakeCharacterSourcePreview(out _),"foreground restoration consumes the original selection once");
            controller.RequestCharacterSourcePreview();page.SuspendView();
            Check(!page.TryTakeCharacterSourcePreview(out _),"retired Settings document cannot consume retained preview request");
            page.ResumeView();
            Check(page.Document!=token&&!page.Handle(new(RmlUiIntentKind.SettingsAction,245,token,2)),"old document intent stays stale after Settings recreates its view");
        }
        using(var restricted=new NativeFullSettingsPage(host,pages,settings,allowGameFiles:false))
        {
            restricted.Open();var controller=(NativeFullSettingsController)Get(restricted,"_controller")!;
            Check(restricted.Handle(new(RmlUiIntentKind.SettingsAction,245,restricted.Document,1))
                &&!restricted.TryTakeCharacterSourcePreview(out _)&&!controller.TakeCharacterSourcePreview(out _),
                "Settings with game-file access disabled cannot route preview action245");
            controller.RequestCharacterSourcePreview();Check(!restricted.TryTakeCharacterSourcePreview(out _),"game-file restriction fences even a direct pending controller request");
        }
        // Actual worker paths need no commercial data: canonical normal is absent,
        // and Trooper has no supported runtime source profile.
        foreach(string id in new[]{"darksamus-echoes","trooper-power"})
        {
            using var preview=new NativeCharacterSourcePreviewPage(host,pages,id,asModal:false);preview.Open();
            Task task=(Task)Get(preview,"_load")!;Await(task);preview.Tick();
            string status=(string)Get(preview,"_status")!;
            Check(preview.Request==null&&Get(preview,"_selection")==null
                &&status.Contains(id=="darksamus-echoes"?"Update the selected source bundle":"supports normal Echoes",StringComparison.Ordinal),
                "missing/unsupported source returns bounded error with no render request: "+id);
            Check(preview.Handle(new(RmlUiIntentKind.HunterCancel,0,preview.Document,1))&&preview.Closed,"Back remains usable after source load refusal: "+id);
        }
        using(var covered=new NativeCharacterSourcePreviewPage(host,pages,"trooper-power",asModal:false))
        {
            covered.Open();Task completed=(Task)Get(covered,"_load")!;Await(completed);
            string prior=(string)Get(covered,"_status")!;pages.OpenModal(new("cover-preview","own-modal.rml"));
            int publications=native.Publications;covered.Tick();
            Check(ReferenceEquals(Get(covered,"_load"),completed)&&(string)Get(covered,"_status")! ==prior
                &&covered.Request==null&&native.Publications==publications,"covered source page defers completed worker adoption and native publication");
            pages.CloseModal();covered.Tick();
            Check(Get(covered,"_load")==null&&((string)Get(covered,"_status")!).Contains("supports normal Echoes",StringComparison.Ordinal),
                "restored source page adopts only its own completed worker result");
        }
        foreach(string retirement in new[]{"back","dispose","dead-document"})
        {
            using var preview=new NativeCharacterSourcePreviewPage(host,pages,"trooper-power",asModal:false);preview.Open();
            Await((Task)Get(preview,"_load")!);
            Type resultType=preview.GetType().GetNestedType("LoadResult",BindingFlags.NonPublic)!;
            object completion=Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(resultType))!;
            Task held=(Task)completion.GetType().GetProperty("Task")!.GetValue(completion)!;Set(preview,"_load",held);
            string prior=(string)Get(preview,"_status")!;
            if(retirement=="back")preview.Back();
            else if(retirement=="dispose")preview.Dispose();
            else host.CloseDocument(preview.Document);
            int publications=native.Publications;
            object result=Activator.CreateInstance(resultType,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object?[]{null,"late detached worker result"},null)!;
            completion.GetType().GetMethod("SetResult")!.Invoke(completion,new[]{result});preview.Tick();
            Check(preview.Closed&&preview.Request==null&&Get(preview,"_selection")==null
                &&(string)Get(preview,"_status")! ==prior&&native.Publications==publications,
                "late worker completion cannot publish or adopt after "+retirement);
        }
    }
    private static void Await(Task task)
    {
        Check(SpinWait.SpinUntil(()=>task.IsCompleted,TimeSpan.FromSeconds(5)),"source preview CPU worker completes within fixture bound");
        if(task.IsFaulted)throw task.Exception!;
    }
    private sealed class PreviewBridge:IRmlUiNativeBridge
    {
        private readonly HashSet<ulong> documents=new();private readonly Dictionary<(ulong,string),string> fields=new();
        private ulong next=1;private string focused="";internal int Publications;
        public uint ProtocolVersion()=>1;public ulong Generation()=>1;public ulong HomeDocument()=>1;
        public int Initialize(int w,int h,float density,string root)=>InitializeBackend(w,h,density,root,1);
        public int InitializeBackend(int w,int h,float density,string root,int backend){documents.Add(1);return 1;}
        public void Shutdown()=>documents.Clear();public void Resize(int w,int h,float density){}public void Update(){}public void Render(int w,int h){}
        public int TakeIntent(ref RmlUiNativeIntent intent)=>0;public int TakeAction(byte[] bytes,int capacity)=>0;
        public ulong OpenDocument(string path,int layer){ulong id=++next;documents.Add(id);return id;}
        public int CloseDocument(ulong id)=>documents.Remove(id)?1:0;public int ShowDocument(ulong id,int show)=>documents.Contains(id)?1:0;
        public int FocusDocument(ulong id,string element){if(!documents.Contains(id))return 0;focused=element;return 1;}
        public int DocumentSetText(ulong id,string name,string value){if(!documents.Contains(id))return 0;Publications++;return 1;}
        public int DocumentSetBool(ulong id,string name,int value){if(!documents.Contains(id))return 0;Publications++;return 1;}
        public int DocumentSetField(ulong id,string name,string value){if(!documents.Contains(id))return 0;fields[(id,name)]=value;Publications++;return 1;}
        public int DocumentReadField(ulong id,string name,byte[] bytes,int capacity)=>Copy(fields.GetValueOrDefault((id,name),""),bytes,capacity);
        private static int Copy(string value,byte[] bytes,int capacity){byte[] encoded=Encoding.UTF8.GetBytes(value);int count=Math.Min(encoded.Length,Math.Min(bytes.Length,capacity)-1);if(count<0)return 0;encoded.AsSpan(0,count).CopyTo(bytes);bytes[count]=0;return count;}
        public void SetText(string name,string value){}public void SetBool(string name,int value){}public void SetField(string name,string value){}public int ReadField(string name,byte[] bytes,int capacity)=>0;
        public void SetLobbyAnchor(int slot,float x,float y){}public int Back()=>0;public int MouseMove(int x,int y,int modifiers)=>0;public int MouseButton(int button,int down,int modifiers)=>0;public int MouseWheel(float delta,int modifiers)=>0;public int Key(int key,int down,int modifiers)=>0;public int Text(uint codepoint)=>0;public void FocusLost(){}public int TextInputActive()=>0;
        public int FocusedElement(byte[] bytes,int capacity)=>Copy(focused,bytes,capacity);public void SetClipboard(string text){}public int ReadClipboard(byte[] bytes,int capacity)=>0;public int TextInputState(ref RmlUiNativeTextInputState state)=>0;public int Composition(ulong generation,ulong document,ulong epoch,int stage,string text,int cursor,int selectionLength)=>0;public int HoveredElement(byte[] bytes,int capacity)=>0;
    }
    private static void CancellationOutcomes(MenuSettings settings)
    {
        using var owner = new NativeFullSettingsController(settings);
        var partial = new ImportBundleBatchCanceledException(new[] { "trooper-power" },
            new OperationCanceledException("synthetic second-appearance cancellation"), CancellationToken.None);
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(owner, "_operation", pending.Task);
        owner.Tick(false); Check(owner.Busy, "held import remains pending before cancellation result");
        pending.SetException(partial); owner.Tick(false);
        Check(!owner.Busy && owner.Status == partial.Message && owner.Status.Contains("trooper-power", StringComparison.Ordinal)
            && !owner.Status.Contains("Previous bundle is unchanged", StringComparison.Ordinal),
            "partial Update-all cancellation displays exact committed appearance outcome");
        var failure = new ImportBundleBatchException(new[] { "trooper-power" }, new IOException("synthetic later preparation failure"));
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Set(owner,"_operation",pending.Task);
        pending.SetException(failure); owner.Tick(false);
        Check(!owner.Busy && owner.Status == "Operation failed: " + failure.Message && owner.Status.Contains("trooper-power", StringComparison.Ordinal),
            "later batch failure retains already-committed appearance in displayed outcome");
        pending = new(TaskCreationOptions.RunContinuationsAsynchronously); Set(owner,"_operation",pending.Task);
        pending.SetException(new OperationCanceledException("synthetic precommit cancellation")); owner.Tick(false);
        Check(!owner.Busy && owner.Status == "Local import cancelled. Previous bundle is unchanged.",
            "single-bundle precommit cancellation retains unchanged-bundle message");
    }
    private static void Requests(MenuSettings settings)
    {
        using var owner = new NativeFullSettingsController(settings);
        owner.RequestCharacterImport(false);
        var first = (string[])Get(owner, "_characterImportIds")!;
        Check(first.SequenceEqual(new[] { "darksamus-echoes" }) && owner.Busy && owner.TakeArenaImport() && !owner.TakeArenaImport(),
            "selected picker consumes one detached canonical request");
        owner.BeginArenaImport(null);
        Check(!owner.Busy && Get(owner,"_characterImportIds") == null && Get(owner,"_operation") == null, "picker cancellation clears captured IDs without work");
        owner.RequestCharacterImport(true); var echoes = (string[])Get(owner,"_characterImportIds")!;
        Check(echoes.SequenceEqual(new[] { "darksamus-echoes", "darksamus-overcharged-echoes" }) && !ReferenceEquals(first, echoes), "Echoes all selects both distinct supported source appearances in fresh array");
        owner.TakeArenaImport(); owner.BeginArenaImport(null); owner.CycleCharacterPreview(); owner.RequestCharacterImport(true);
        var troopers = (string[])Get(owner,"_characterImportIds")!;
        Check(troopers.SequenceEqual(new[] { "trooper-power", "trooper-ice", "trooper-wave", "trooper-plasma" }), "Prime all captures four Troopers without Echoes");
        owner.TakeArenaImport(); owner.BeginArenaImport(null); owner.RequestCharacterImport(false);
        Check(((string[])Get(owner,"_characterImportIds")!).SequenceEqual(new[] { "trooper-power" }), "selected Trooper request excludes other appearances");
        owner.Dispose(); owner.BeginArenaImport("missing-owned-source.iso");
        Check(!owner.Busy && !owner.TakeArenaImport() && Get(owner,"_characterImportIds") == null && Get(owner,"_operation") == null,
            "dispose retires pending picker and ignores late callback");
    }
    private static object? Get(object owner,string field) => owner.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner);
    private static void Set(object owner,string field,object? value)
    {
        var member=owner.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic);
        if(member!=null)member.SetValue(owner,value);
        else owner.GetType().GetProperty(field,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.SetValue(owner,value);
    }
    private static void Wait(NativeFullSettingsController owner)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (owner.Busy && timer.Elapsed < TimeSpan.FromSeconds(5)) { owner.Tick(false); Thread.Sleep(1); }
        Check(!owner.Busy && !owner.Status.StartsWith("Operation failed",StringComparison.Ordinal), "background check/removal completes successfully");
    }
    private static Dictionary<string,string> Snapshot(string root) => !Directory.Exists(root) ? new() : Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories)
        .ToDictionary(f=>Path.GetRelativePath(root,f),f=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f))));
    private static bool Same(Dictionary<string,string> a,Dictionary<string,string> b) => a.Count==b.Count && a.All(p=>b.GetValueOrDefault(p.Key)==p.Value);
    private static void Check(bool ok,string why) { if(!ok)throw new InvalidOperationException("Native character preview: "+why); Console.WriteLine("Native character preview PASS: "+why); }
}
