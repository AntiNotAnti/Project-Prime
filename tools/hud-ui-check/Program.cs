using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Input;

int checks=0;
void Check(bool ok,string message) { checks++; if(!ok) throw new Exception(message); }
Check(GuiLauncher.EnsureSetup(requireDisplay:false),"UI initialization");
string? chosenColor=null;
var colorEditor=new HudColorEditor("Test","#FFFFFF",v=>chosenColor=v);
Check(colorEditor.Apply(" 0af ") && chosenColor=="#00AAFF","pasted short hex normalizes");
Check(!colorEditor.Apply("not a color") && chosenColor=="#00AAFF","invalid color leaves prior value");
Check(colorEditor.Apply(HudColorEditor.Swatches[6]) && chosenColor=="#FF0000","palette color applies immediately");
if(Environment.GetEnvironmentVariable("HUD_NATIVE_ASSET_ROOT") is {} nativeRoot)
{
    MphRead.Paths.SetPath("AMHE1",nativeRoot); MphRead.Paths.MphKey="AMHE1";
    using var sprites=new HudNativePreview();
    for(int hunter=0;hunter<8;hunter++)
    {
        var objects=MphRead.Hud.HudElements.HunterObjects[hunter];
        Check(sprites.Frame(objects.Reticle,0)!=null,"native reticle decoded "+hunter);
        Check(sprites.Frame(objects.SniperReticle,0)!=null,"native zoom reticle decoded "+hunter);
        Check(sprites.Frame(objects.HealthBarA,4)!=null,"native health decoded "+hunter);
        Check(sprites.Frame(objects.AmmoBar,4)!=null,"native ammo decoded "+hunter);
    }
}
string directory=Path.Combine(Path.GetTempPath(),"prime-hud-ui-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory); HudProfiles.Load(directory);
var original=HudProfiles.CopyCurrent();

// Regression: the stock HUD keeps the match timer tied to the scoreboard,
// while a Custom HUD may promote match.timer into the normal gameplay pass.
var timerPolicy=original.DeepClone();
timerPolicy.Mode=HudMode.ProjectPrime; HudProfiles.Publish(timerPolicy);
Check(!MphRead.Entities.PlayerEntity.DrawCustomMatchTimerDuringGameplay,
    "stock HUD does not persist match timer");
timerPolicy.Mode=HudMode.Custom; timerPolicy.Notifications.Spacing=7; HudProfiles.Publish(timerPolicy);
Check(MphRead.Entities.PlayerEntity.DrawCustomMatchTimerDuringGameplay,
    "custom HUD persists match timer");

// Regression: custom combat notifications must start at the same authored
// origin that combat.notifications moves/scales around. Stock collision lanes
// must not rewrite that origin before the custom element transform is applied.
var notificationQueue=new MphRead.Entities.PlayerEntity.HudMessage[]
{
    new() { CombatLines=1 },
    new() { CombatLines=1 }
};
MphRead.Entities.PlayerEntity.LayoutCustomCombatNotifications(notificationQueue,new[]{0,1});
Check(notificationQueue[0].Position.X==MphRead.Entities.PlayerEntity.CombatNotificationLayoutX
    && notificationQueue[0].Position.Y==MphRead.Entities.PlayerEntity.CombatNotificationLayoutY,
    "custom streak lane uses HUD editor origin");
Check(notificationQueue[0].Scale>0 && notificationQueue[1].Position.Y>notificationQueue[0].Position.Y,
    "custom streak lane keeps scale and spacing");
HudProfiles.Publish(original);

var dynamicAim=original.DeepClone(); dynamicAim.Mode=HudMode.ProjectPrime; dynamicAim.FixedWeapon=false; HudProfiles.Publish(dynamicAim);
Check(Features.FixedCrosshair && !Features.FixedAimCamera && Features.ResponsiveAimCamera,
    "Pro HUD dynamic weapon keeps eased visual state with responsive aim camera");
dynamicAim.FixedWeapon=true; HudProfiles.Publish(dynamicAim);
Check(Features.FixedAimCamera && Features.ResponsiveAimCamera,
    "Pro HUD static weapon welds visual state and keeps responsive aim camera");
HudProfiles.Publish(original);
HudProfile? accepted=null; bool closed=false;
var view=new HudStudioView(original,p=>{accepted=p;closed=true;});
var window=new Window { Width=1280,Height=720,Content=view }; window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
var canvas=view.GetVisualDescendants().OfType<HudStudioCanvas>().Single();
var point = canvas.TranslatePoint(canvas.ElementBounds(1).Center, window)!.Value;
float originalX=canvas.History.Draft.Elements["core.health"].OffsetX;
window.MouseDown(point, MouseButton.Left); window.MouseMove(point+new Avalonia.Vector(24,0)); window.MouseUp(point+new Avalonia.Vector(24,0), MouseButton.Left);
Check(canvas.Selected==1 && canvas.History.Draft.Elements["core.health"].OffsetX!=originalX,"pointer selection and drag");
canvas.History.Undo(); Check(canvas.History.Draft.Elements["core.health"].OffsetX==originalX,"one undo restores entire drag");
// Guide snapping uses rendered bounds, so test the actual arranged canvas.
var snapBefore=canvas.History.Capture();
foreach (var element in canvas.History.Draft.Elements.Values) element.Enabled=false;
var health=canvas.History.Draft.Elements["core.health"];
health.Enabled=true; health.Anchor=HudAnchor.Center; health.OffsetX=3; health.OffsetY=100;
canvas.Selected=1; canvas.SnapToGuides();
Check(Math.Abs(health.OffsetX)<.01f,"center guide snaps an element edge");
canvas.History.Commit(snapBefore); canvas.History.Undo();
var lockAll=view.GetVisualDescendants().OfType<PrimeButton>().Single(b=>b.Label=="Lock all");
FocusNavigator.Key(lockAll,Key.Enter);
Check(canvas.History.Draft.Elements.Values.All(e=>e.Locked),"lock all elements");
canvas.History.Undo();
Check(!canvas.History.Draft.Elements["core.health"].Locked,"lock all is undoable");
canvas.Selection.Clear(); canvas.Selection.Add(1); canvas.Selection.Add(2); canvas.Selected=1;
string beforeAlign=canvas.History.Capture();
canvas.AlignSelection(true);
Check(Math.Abs(canvas.ElementBounds(1).Left-canvas.ElementBounds(2).Left)<.01,"group left alignment");
canvas.History.Undo(); Check(canvas.History.Capture()==beforeAlign,"group alignment is one undo");
canvas.Selection.Clear();
canvas.Selected=1; canvas.Focus();
Check(FocusNavigator.Key(canvas,Key.Right),"keyboard nudge handled");
Check(canvas.History.Draft.Elements["core.health"].OffsetX==original.Elements["core.health"].OffsetX+1,"nudge moves selected element");
Check(canvas.History.Draft.Mode==HudMode.Custom,"nudge activates custom layout");
canvas.History.Undo(); Check(canvas.History.Draft.Elements["core.health"].OffsetX==original.Elements["core.health"].OffsetX,"undo restores");
canvas.History.Redo(); Check(canvas.History.Draft.Elements["core.health"].OffsetX==original.Elements["core.health"].OffsetX+1,"redo restores");
Check(HudProfiles.CopyCurrent().Elements["core.health"].OffsetX==original.Elements["core.health"].OffsetX,"draft does not leak");
Check(view.HandleController(UiAction.NextTab),"controller element selection");
Check(canvas.Selected==2,"controller selects next");
canvas.Focus(); Check(view.HandleController(UiAction.Accept),"controller starts move");
float x=canvas.History.Draft.Elements["core.ammo"].OffsetX;
view.HandleController(UiAction.Right); Check(canvas.History.Draft.Elements["core.ammo"].OffsetX==x+1,"controller movement");
view.HandleController(UiAction.Back); Check(!closed,"back exits move mode");
var neutral=new GamepadSnapshot("hud-test",new GamepadState { Connected=true },100);
view.HandleControllerAxes(neutral,0); view.HandleControllerAxes(neutral,16);
float stickStart=canvas.History.Draft.Elements["core.ammo"].OffsetX;
var stick=neutral with { State=new GamepadState { Connected=true,LeftX=1,RightY=1 } };
view.HandleControllerAxes(stick,32); view.HandleControllerAxes(stick,48); view.HandleControllerAxes(neutral,64);
Check(canvas.History.Draft.Elements["core.ammo"].OffsetX>stickStart,"physical stick moves element");
canvas.History.Undo(); Check(canvas.History.Draft.Elements["core.ammo"].OffsetX==stickStart,"stick gesture coalesced");
view.HandleControllerAxes(neutral with { State=new GamepadState { Connected=true,Buttons=GamepadButtons.Y } },80);
Check(!canvas.History.Draft.Elements["core.ammo"].Enabled,"controller Y toggles visibility");
view.HandleControllerAxes(neutral,96); canvas.History.Undo();
// The actual UI must survive repeated inspector replacement without parenting errors.
var chooser=view.GetVisualDescendants().OfType<ComboBox>().First(c=>c.ItemsSource==HudProfileDefaults.ElementIds);
for(int i=0;i<HudProfileDefaults.ElementIds.Length;i++){chooser.SelectedIndex=i;window.UpdateLayout();Dispatcher.UIThread.RunJobs();}
chooser.SelectedIndex=0; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
string shots=Path.Combine(directory,"shots"); Directory.CreateDirectory(shots);
foreach(var (w,h) in new[]{(1280,720),(960,600),(420,900)})
{
 window.Width=w;window.Height=h;window.UpdateLayout();Dispatcher.UIThread.RunJobs();
 Check(canvas.Bounds.Width>0 && canvas.Bounds.Height>0,"canvas visible "+w);
 using var bitmap=new RenderTargetBitmap(new PixelSize(w,h),new Vector(96,96));bitmap.Render(view);bitmap.Save(Path.Combine(shots,$"hud-{w}x{h}.png"));
}
if(Environment.GetEnvironmentVariable("HUD_NATIVE_ASSET_ROOT")!=null)
{
    canvas.History.Edit(p=> { p.Mode=HudMode.Custom;p.Health.Native=true;p.Ammo.Native=true;p.Inventory.Native=true;p.Crosshair.Native=true; });
    canvas.Refresh(); window.Width=1280;window.Height=720;window.UpdateLayout();Dispatcher.UIThread.RunJobs();
    using var nativeBitmap=new RenderTargetBitmap(new PixelSize(1280,720),new Vector(96,96));nativeBitmap.Render(view);nativeBitmap.Save(Path.Combine(shots,"native-hud.png"));
}
view.HandleController(UiAction.Back); Check(closed && accepted==null,"cancel");
Check(HudProfiles.CopyCurrent().Elements["core.health"].OffsetX==original.Elements["core.health"].OffsetX,"cancel restores original");
window.Close();
closed=false;
var saveView=new HudStudioView(original,p=>{accepted=p;closed=true;});
var saveWindow=new Window { Width=960,Height=600,Content=saveView }; saveWindow.Show(); saveWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
var saveCanvas=saveView.GetVisualDescendants().OfType<HudStudioCanvas>().Single(); saveCanvas.Selected=4; saveCanvas.Focus(); FocusNavigator.Key(saveCanvas,Key.Right);
var use=saveView.GetVisualDescendants().OfType<PrimeButton>().Single(b=>b.Label=="Use in settings"); FocusNavigator.Key(use,Key.Enter);
Check(closed && accepted != null && accepted.Mode==HudMode.Custom,"use draft returns edited profile");
Check(HudProfiles.CopyCurrent().Elements["core.radar"].OffsetX==original.Elements["core.radar"].OffsetX,"use in settings waits for Apply");
HudProfiles.Save(accepted!); Check(HudProfiles.CopyCurrent().Elements["core.radar"].OffsetX==accepted!.Elements["core.radar"].OffsetX,"save publishes accepted draft");
saveWindow.Close();
var targets = new[] { new MphRead.Mods.Input.AimAssist.AimAssistTarget(1,1,new(1,.2f),new(.3f,.4f),15,true,true) };
var assistProfile = MphRead.Mods.Input.AimAssist.AimAssistWeaponProfile.For(MphRead.Mods.Input.AimAssist.AimAssistWeaponClass.Standard,false);
MphRead.Mods.Input.AimAssist.AimAssistResult Sample()
{
    var state = new MphRead.Mods.Input.AimAssist.AimAssistState();
    MphRead.Mods.Input.AimAssist.AimAssistResult result=default;
    for(int i=0;i<60;i++) result=MphRead.Mods.Input.AimAssist.AimAssist.Apply(state,targets,new System.Numerics.Vector2(.1f,.01f),.5f,0,1f/60,true,assistProfile);
    return result;
}
var normal=Sample(); var huge=HudProfiles.CopyCurrent(); huge.Crosshair.Scale=8; HudProfiles.Publish(huge);
Check(Sample()==normal,"800 percent crosshair does not change aim assist");
huge.Crosshair.Enabled=false; huge.Elements["core.crosshair"].Enabled=false; HudProfiles.Publish(huge);
Check(Sample()==normal,"hidden crosshair does not disable aim assist");
checks += RadarUiChecks.Run();
Console.WriteLine($"HUD UI checks passed: {checks}. Screenshots: {shots}");

// Replay timeline input must remain precise even over dense event markers and trim handles.
var timeline = new ReplayTimeline();
uint requestedFrame = uint.MaxValue;
int seekRequests = 0, trimRequests = 0;
timeline.FrameRequested = frame => { requestedFrame = frame; seekRequests++; };
timeline.MarkInRequested = _ => trimRequests++;
timeline.Update(600, 0, 300, 500,
    new[] { new MphRead.Mods.Network.ReplayEvent(447, MphRead.Mods.Network.ReplayEventType.Kill) },
    Array.Empty<MphRead.Mods.Replay.ReplayHighlight>());
var timelineWindow = new Window { Width = 600, Height = 160, Content = timeline };
timelineWindow.Show(); timelineWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
Point TimelinePoint(double fraction, double y) => timeline.TranslatePoint(
    new Point(timeline.Bounds.Width * fraction, y), timelineWindow)!.Value;
window = timelineWindow;
window.MouseDown(TimelinePoint(.5, 30), MouseButton.Left);
window.MouseMove(TimelinePoint(.75, 30));
window.MouseUp(TimelinePoint(.75, 30), MouseButton.Left);
Check(requestedFrame == 450 && trimRequests == 0, "timeline lanes scrub without grabbing trim handles");
Check(seekRequests == 2, "pointer release does not restart the same seek");
window.MouseDown(TimelinePoint(.25, 30), MouseButton.Right);
window.MouseUp(TimelinePoint(.25, 30), MouseButton.Right);
Check(seekRequests == 2, "right click does not seek");
window.MouseWheel(TimelinePoint(.5, 30), new Avalonia.Vector(0, 1));
Check(requestedFrame == 510 && timeline.Zoom == 1, "wheel scrubs one second without zooming");
timelineWindow.Close();
Console.WriteLine("Replay timeline input checks passed.");

Check(MphRead.Mods.Replay.ReplayInput.OverTimeline(128f / 256, 174f / 192), "replay HUD bar hit test");
Check(!MphRead.Mods.Replay.ReplayInput.OverTimeline(.5f, .5f), "world clicks are outside replay scrub bar");
Check(MphRead.Mods.Replay.ReplayInput.FrameAt(51f / 256, 600) == 0
    && MphRead.Mods.Replay.ReplayInput.FrameAt(128f / 256, 600) == 300
    && MphRead.Mods.Replay.ReplayInput.FrameAt(205f / 256, 600) == 600,
    "replay HUD scrub endpoints and midpoint");
Check(MphRead.Mods.Replay.ReplayInput.FrameAt(-1, 600) == 0
    && MphRead.Mods.Replay.ReplayInput.FrameAt(2, 600) == 600,
    "replay HUD drag clamps outside the window");
var theatre = new TheatreWorkspace(manageStorage: false);
theatre.ShowEditor(() => {}, () => {});
var theatreWindow = new Window { Width = 1200, Height = 800, Content = theatre };
theatreWindow.Show(); theatreWindow.UpdateLayout(); Dispatcher.UIThread.RunJobs();
var picture = theatre.GetVisualDescendants().OfType<ReplayViewport>().Single();
Check(picture.Bounds.Width > 0 && picture.Bounds.Height > 0, "studio reserves a real replay viewport");
var pictureOrigin = picture.TranslatePoint(default, theatreWindow)!.Value;
Check(pictureOrigin.Y > 0 && picture.Bounds.Width < theatreWindow.ClientSize.Width
    && pictureOrigin.Y + picture.Bounds.Height < theatreWindow.ClientSize.Height,
    "replay viewport excludes header, sidebar and footer controls");
theatreWindow.Close(); theatre.Dispose();
Console.WriteLine("Replay HUD scrubbing and viewport checks passed.");
