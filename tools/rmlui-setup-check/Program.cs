using MphRead.Mods.Launcher.Core;
int checks=0;
void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;Console.WriteLine("RMLSETUP PASS "+message);}
void Complete(SetupController controller){var until=DateTime.UtcNow.AddSeconds(3);while(controller.Busy&&DateTime.UtcNow<until){controller.Tick();Thread.Sleep(1);}controller.Tick();Check(!controller.Busy,"background operation completes through owner-thread polling");}
var backend=new Backend();
using(var controller=new SetupController(backend,required:true)){
 Check(!controller.CanLeave,"first setup cannot leave before usable game files");
 Check(!controller.RequestRom("")&&!controller.RequestRom("bad\npath"),"empty and multiline ROM paths rejected");
 Check(controller.RequestRom("own.nds")&&controller.Snapshot().ConfirmingRom&&backend.Extracts==0,"ROM replacement requires explicit confirmation");
 controller.CancelConfirmation();Check(!controller.Snapshot().ConfirmingRom&&backend.Extracts==0,"cancelling confirmation never extracts");
 controller.RequestRom("own.nds");backend.Block=true;controller.ConfirmRom();
 Check(controller.Busy&&!controller.CanLeave&&!controller.LoadReleases()&&!controller.RequestRom("other.nds"),"active extraction blocks navigation and competing work");
 backend.Gate.Set();Complete(controller);Check(backend.Extracts==1&&controller.Snapshot().Files.Ready&&controller.CanLeave,"successful extraction refreshes readiness");
 backend.FailExtract=true;controller.RequestRom("bad.nds");controller.ConfirmRom();Complete(controller);
 Check(controller.Snapshot().Files.Ready&&controller.Snapshot().Error.Contains("rejected"),"failed extraction stays failed even when older files are ready");
 controller.LoadReleases();Complete(controller);Check(controller.Snapshot().Releases.Count==2&&controller.Snapshot().SelectedRelease==0,"published releases populate immutable selection");
 Check(!controller.SelectRelease(-1)&&!controller.SelectRelease(30),"stale release indices rejected");
 controller.SelectRelease(1);controller.RequestPrepare();Check(backend.Prepares==0&&controller.Snapshot().ConfirmingUpdate,"release switch waits for confirmation");
 backend.Permission=false;Check(!controller.ConfirmPrepare()&&backend.Prepares==0&&!controller.Busy,"permission request runs before background download");
 backend.Permission=true;controller.RequestPrepare();controller.ConfirmPrepare();Complete(controller);
 Check(controller.Snapshot().Prepared&&backend.Installs==0,"prepared package waits for final install action");
 Check(controller.InstallPrepared()&&backend.Installs==1&&controller.ExitRequested,"explicit final install requests platform exit");
}
using(var controller=new SetupController(new Backend(),inGame:true)){
 Check(!controller.PickRom()&&!controller.RequestRom("own.nds")&&!controller.ConfigurePath("AMHE1","files"),"in-game setup cannot replace live game paths");
 controller.LoadReleases();Complete(controller);Check(!controller.RequestPrepare(),"in-game releases can be inspected but cannot replace running app");
}
var android=new Backend(){Exit=false};using(var controller=new SetupController(android)){
 controller.LoadReleases();Complete(controller);controller.RequestPrepare();controller.ConfirmPrepare();Complete(controller);controller.InstallPrepared();
 Check(controller.Busy&&!controller.ExitRequested,"platform installer keeps owner alive until system response");
 android.Finished=new(false,"cancelled by player");controller.Tick();Check(!controller.Busy&&controller.Snapshot().Error=="cancelled by player","installer cancellation restores actionable page");
}
using(var controller=new SetupController(new Backend())) {
 var releases=new List<SetupRelease>{new("v3","3.0","asset","notes","https://example.test",true,false)};
 controller.PresentPublishedReleases(releases,"versions");releases[0]=releases[0] with{Tag="mutated"};
 Check(controller.Snapshot().Releases[0].Tag=="v3","release snapshots detach backend mutable collections");
 Check(((IList<SetupRelease>)controller.Snapshot().Releases).IsReadOnly,"published release list cannot be mutated by snapshot consumers");
 bool rejected=Task.Run(()=>{try{controller.Tick();return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();
 Check(rejected,"worker cannot publish setup completion outside the owner thread");
}
Console.WriteLine($"RMLSETUP CHECK PASS {checks} checks");
sealed class Backend:ISetupBackend{
 public bool Ready,FailExtract,Block,Permission=true,Exit=true;public int Extracts,Prepares,Installs;public ManualResetEventSlim Gate=new();public SetupResult? Finished;
 public SetupFiles Inspect()=>new(Ready,Ready?"ready":"missing","data","AMHE1",true,true,false);
 public Task<string?> PickRom(CancellationToken cancel)=>Task.FromResult<string?>("own.nds");
 public SetupResult Extract(string path,Action<string> report){Extracts++;if(Block)Gate.Wait(TimeSpan.FromSeconds(2));report("Validating ROM");if(FailExtract)return new(false,"ROM rejected");Ready=true;return new(true,"ready");}
 public SetupResult ConfigureExtractedPath(string revision,string path)=>new(true,"configured");
 public SetupResult VerifyInstallation()=>new(true,"verified");public SetupResult RenderPreviews(Action<string> report)=>new(true,"rendered");
 public SetupResult CheckLatest(CancellationToken cancel)=>LoadReleases(cancel);
 public SetupResult LoadReleases(CancellationToken cancel)=>new(true,"versions",new[]{new SetupRelease("v2","2.0.0","asset","notes","https://example.test",true,false),new SetupRelease("v1","1.0.0","asset","notes","https://example.test",true,true)});
 public SetupResult? RequestPreparePermission(int index)=>Permission?null:new(false,"allow installs");
 public SetupResult PrepareRelease(int index,Action<float> progress){Prepares++;progress(.5f);return new(true,"prepared",Prepared:true);}
 public SetupResult InstallPrepared(out bool exit){Installs++;exit=Exit;return new(true,"installing");}
 public SetupResult OpenRelease(int index)=>new(true,"opened");public SetupResult OpenDataFolder()=>new(true,"opened");
 public bool TryTakeInstallerResult(out SetupResult result){result=Finished!;if(Finished==null)return false;Finished=null;return true;}
}
