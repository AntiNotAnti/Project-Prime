#if !MPHREAD_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
namespace MphRead.Mods.Launcher.Core;
public interface ITheatreProjectSave : IDisposable { string Destination { get; } }
/// <summary>Prepare detached files, then publish only after the foreground owner is revalidated.</summary>
public interface ITheatreProjectFilesBackend
{
    Task<ITheatreProjectSave> PrepareProjectSaveAs(TheatreEditorContext context,string destination,CancellationToken token);
    string PublishProjectSaveAs(TheatreEditorContext context,ITheatreProjectSave prepared);
}
#endif
