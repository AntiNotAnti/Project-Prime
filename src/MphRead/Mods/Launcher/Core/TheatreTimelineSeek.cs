#if !MPHREAD_SERVER
namespace MphRead.Mods.Launcher.Core;
/// <summary>Exact-frame seek borrowed from the SAME reviewed Editor backend and transport.</summary>
public interface ITheatreTimelineSeek { void SeekTimelineFrame(TheatreEditorContext reviewed,uint frame); }
#endif
