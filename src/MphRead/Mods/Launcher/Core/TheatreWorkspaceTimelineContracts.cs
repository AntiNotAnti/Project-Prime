#if !MPHREAD_SERVER
namespace MphRead.Mods.Launcher.Core;
public interface ITheatreWorkspaceTimelineBackend
{
    void SetReviewedTimelineRange(TheatreEditorContext context,uint start,uint end);
}
#endif
