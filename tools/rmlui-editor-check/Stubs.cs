using MphRead.Mods.Network;
namespace MphRead { internal enum BeamType:sbyte { Power=0,Volt=1 } internal sealed class Scene { public long LifetimeGeneration=1;public ReplayPoseStream? ReplayPoses=new(); } }
namespace MphRead.Entities { internal static class PlayerEntity {public const int SlotCapacity=8;} }
namespace MphRead.Mods.Network {
internal static partial class DemoPlayback {
 internal static bool IsActive=true;internal static string? LogicalPath,PlaybackPath;private static object? _player=new();private static object _prepared=new();
 internal static void ReplaceOwner()=>_player=new();internal static Scene? PresentationScene=new();internal static bool Owns(Scene scene)=>ReferenceEquals(scene,PresentationScene);
 internal static IReadOnlyList<ReplayEvent> Events=Array.Empty<ReplayEvent>();internal static uint LastFrame=>Session.Transport.DurationFrames;
 internal static ReplayMetadata? Metadata=new();internal static ReplaySession Session=new();
}
internal sealed class ReplayMetadata { public IReadOnlyList<ReplayPlayerInfo> Players {get;init;}=Array.Empty<ReplayPlayerInfo>(); }
internal sealed class ReplaySession {public ReplayTransport Transport=new();}
internal sealed class ReplayTransport {
 internal uint CurrentFrame=50,DurationFrames=600;internal long SeekGeneration;internal uint? ClipIn=10,ClipOut=80;
 internal bool IsSeeking,IsPaused=true,AtEnd=false;internal void Seek(uint frame,bool resume){CurrentFrame=frame;SeekGeneration++;IsSeeking=true;IsPaused=!resume;}
 internal void SetMarkIn(uint frame){ClipIn=Math.Min(frame,ClipOut??DurationFrames);}internal void SetMarkOut(uint frame){ClipOut=Math.Max(frame,ClipIn??0);}
}
internal static class ReplayController {internal static uint CurrentFrame=>DemoPlayback.Session.Transport.CurrentFrame;internal static uint DurationFrames=>DemoPlayback.Session.Transport.DurationFrames;}
internal static class ReplayFormatV3 {internal const uint MaxFrame=10000000;}
internal readonly record struct ReplayCombatDiagnostic(int Value);
internal sealed class ReplayPoseStream {
 internal bool Available=true;internal int Reads;
 internal bool TryCombatDiagnostic(uint frame,int shooter,int victim,int weapon,int damage,bool lethal,bool headshot,int direction,out ReplayCombatDiagnostic result){Reads++;result=new((int)frame);return Available;}
}
internal static class SpectatorMode {internal static int Watched=-1;internal static void Watch(int slot)=>Watched=slot;}
}
namespace MphRead.Mods.Replay {
internal static class ReplayVirtualClips {public const string Extension=".ppclip";internal static string LogicalPath(string path)=>path;
 internal static bool TryLoad(string path,out ReplayVirtualClipDocument? document){document=System.Text.Json.JsonSerializer.Deserialize<ReplayVirtualClipDocument>(File.ReadAllBytes(path));return document is{Version:1};}}
internal static class ReplayHud {internal static bool ShowAnalytics,ShowNetworkDebug;internal static string Time(uint frame)=>frame.ToString();}
internal static class ReplayVideoExporter {internal static bool Rendering;}
internal static class ReplayCombatDiagnostics {
 internal readonly record struct Selection(int Value);internal static bool ShowHud,ShowRays;internal static Selection? Selected;
 internal static void ClearSelection()=>Selected=null;internal static void Select(in ReplayCombatDiagnostic diagnostic)=>Selected=new(diagnostic.Value);
 internal static string Describe(in ReplayCombatDiagnostic diagnostic)=>"ACTUAL DIAGNOSTIC "+diagnostic.Value;
}
}
namespace MphRead.Mods.Launcher.RmlUi.Render {
internal static partial class RmlUiOpenGlSmoke {
 private static bool GameplayLeaseCurrent=true;private static Scene _gameplayScene=new();
 internal static MphRead.Mods.Launcher.Core.ITheatreEditorBackend Fixture()=>new CurrentReplayEditor();
 internal static void ReplaceScene()=>_gameplayScene=new Scene{LifetimeGeneration=_gameplayScene.LifetimeGeneration+1};
}
}
