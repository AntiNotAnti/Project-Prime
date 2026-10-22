#if !MPHREAD_SERVER
namespace MphRead.Mods.Launcher.Core;
public sealed record TheatreComparisonPreviewState(bool Active,bool Ready,uint Frame,string Status);
/// <summary>A private comparison that follows the foreground playhead without advancing it.</summary>
public interface ITheatreWorkspaceComparisonPreviewBackend
{
    void BeginComparisonPreview(TheatreEditorContext context,string path);
    void StopComparisonPreview();
    TheatreComparisonPreviewState ReadComparisonPreview(TheatreEditorContext context);
    object? CurrentComparisonPreviewScene(TheatreEditorContext context);
}
#endif
