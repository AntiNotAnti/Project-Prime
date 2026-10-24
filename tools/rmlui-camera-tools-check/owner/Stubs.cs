using MphRead.Mods.Replay;
using OpenTK.Mathematics;
namespace MphRead {
public partial class Scene {public long LifetimeGeneration=1;public Players Players=new();internal void RestoreNativeReplayMemoryCamera(ReplayCameraKeyframe key){} internal void UseNativeReplayFreeCamera(ReplayCameraKeyframe pose){} }
public sealed class Players {public int MainPlayerIndex=0;public MphRead.Entities.PlayerEntity[] Items=new[]{new MphRead.Entities.PlayerEntity{SlotIndex=1},new MphRead.Entities.PlayerEntity{SlotIndex=3}};}
}
namespace MphRead.Mods.Network {
internal sealed class ReplayTransport {public long SeekGeneration;public uint CurrentFrame=50,DurationFrames=600;public bool IsSeeking,IsPaused=true;public void Seek(uint frame){CurrentFrame=frame;SeekGeneration++;IsSeeking=true;}}
internal sealed class ReplaySession {public ReplayTransport Transport=new();}
internal readonly record struct ReplayPlayerInfo(byte Slot,string DisplayName);
internal enum ReplayEventType {Damage,Kill}
internal readonly record struct ReplayEvent(uint Frame,ReplayEventType Type);
internal sealed class ReplayMetadata {public IReadOnlyList<ReplayPlayerInfo> Players=new[]{new ReplayPlayerInfo(1,"One"),new ReplayPlayerInfo(3,"Three")};}
internal static class DemoPlayback {
public static object PlaybackOwner=new();public static ReplaySession Session=new();public static bool IsActive=true;public static Scene? PresentationScene=new();
private static string? _logical;public static int LogicalReads;public static string? LogicalPath {get{LogicalReads++;return _logical;}set=>_logical=value;}
public static string? PlaybackPath;public static bool Owns(Scene scene)=>ReferenceEquals(scene,PresentationScene);
public static IReadOnlyList<ReplayEvent> Events=new[]{new ReplayEvent(45,ReplayEventType.Damage),new ReplayEvent(55,ReplayEventType.Kill)};public static ReplayMetadata? Metadata=new();
}
internal static class ReplayController {public static bool IsPaused=>DemoPlayback.Session.Transport.IsPaused;public static bool IsSeeking=>DemoPlayback.Session.Transport.IsSeeking;public static uint CurrentFrame=>DemoPlayback.Session.Transport.CurrentFrame;public static uint DurationFrames=>DemoPlayback.Session.Transport.DurationFrames;}
internal static class ReplayFormatV3{public const uint MaxFrame=10000000;}
}
namespace MphRead.Mods.Replay {
public enum ReplayCameraMode{FirstPerson,Chase,Free,Orbit}public enum ReplayPresentationProfile{Faithful,Presentation}
public static partial class ReplayCamera {
public static ReplayCameraMode Mode;public static ReplayPresentationProfile Profile;public static float FieldOfView=80,Roll,Distance=4,Height=1.5f;public static int LookAtSlot=-1;
public static bool Director,PlayTrack,TrackConstantSpeed=true,TrackCollisionAvoidance=true;internal static bool Changed,BookmarkRequested=false,RestoreRequested=false;
internal static ReplayCameraInterpolation TrackInterpolation=ReplayCameraInterpolation.Spline;internal static ReplayCameraEase TrackEase=ReplayCameraEase.InOut;
internal static readonly ReplayCameraTrack Track=new();private static string? _trackPath;private static ReplayCameraKeyframe? _restoreKey;public static string EditStatus{get;private set;}="";
private static void ClearSelection(){}private static void SetSingleSelection(uint frame){}
internal static void EnsureTrack(){string? logical=MphRead.Mods.Network.DemoPlayback.LogicalPath;if(_trackPath==logical)return;_trackPath=logical;if(logical!=null)Track.Load(logical);}
public static void SetMode(ReplayCameraMode value){Mode=value;PlayTrack=false;}public static void SetProfile(ReplayPresentationProfile value)=>Profile=value;
public static void AdjustLens(float fov,float roll){FieldOfView+=fov;Roll+=roll;}public static void ToggleTrackPlayback()=>PlayTrack=!PlayTrack;
public static void ToggleConstantSpeed()=>TrackConstantSpeed=!TrackConstantSpeed;
public static void SelectKeyframe(uint frame)=>MphRead.Mods.Network.DemoPlayback.Session.Transport.Seek(frame);
}
internal static class ReplayVideoExporter{public static bool Active=false;}
internal static partial class ReplayAnnotations{public const string Extension=".studio.json";}
internal static partial class ReplayReels{public const string Extension=".reel.json";}
internal static class ReplayVirtualClips{public const string Extension=".ppclip";internal static bool TryLoad(string path,out ReplayVirtualClipDocument? clip){clip=System.Text.Json.JsonSerializer.Deserialize<ReplayVirtualClipDocument>(File.ReadAllBytes(path));return clip is{Version:1};}}
}
namespace MphRead.Mods.Launcher.RmlUi.Render {
internal static partial class RmlUiOpenGlSmoke {
private static bool GameplayLeaseCurrent=true;private static Scene _gameplayScene=new();
internal static MphRead.Mods.Launcher.Core.ITheatreCameraToolsBackend Fixture()=>new CurrentReplayCamera();
}
}

namespace MphRead.Entities {
 [System.Flags]public enum LoadFlags{Active=1,Spawned=2}
 public sealed class PlayerEntity{public int SlotIndex;public LoadFlags LoadFlags=LoadFlags.Active|LoadFlags.Spawned;}
 internal static class LoadFlagsFixtureExtensions{internal static bool TestFlag(this LoadFlags value,LoadFlags flag)=>(value&flag)==flag;}
}
