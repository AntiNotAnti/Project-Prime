#if !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
namespace MphRead.Mods.Launcher.Core;

public sealed record TheatreWorkspaceCameraValues(float X, float Y, float Z, float Yaw, float Pitch, float Roll, float Fov)
{ public bool ValidFov=>float.IsFinite(Fov)&&Fov is >=10 and <=150; }
public sealed record TheatreWorkspacePreview(bool GameHud, bool ReplayOverlay, bool CameraPath);
/// <summary>Optional direct presentation controls; authored persistence stays with reviewed camera edits.</summary>
public sealed record TheatreWorkspaceCameraChoices(int Interpolation,int Ease,int LookAt,int PlayerSlot,ImmutableArray<int> PlayerSlots);
public interface ITheatreWorkspaceCameraChoicesBackend
{
    TheatreWorkspaceCameraChoices ReadChoices(TheatreCameraContext context);
    void ApplyChoices(TheatreCameraContext context,int interpolation,int ease,int lookAt,int playerSlot);
}
public interface ITheatreWorkspaceOverviewBackend
{
    void SetOverview(TheatreCameraContext context);
}
public interface ITheatreWorkspaceCameraBackend
{
    TheatreWorkspaceCameraValues ReadDirectCamera(TheatreCameraContext context);
    TheatreWorkspacePreview ReadPreview(TheatreCameraContext context);
    void ApplyDirectCamera(TheatreCameraContext context, TheatreWorkspaceCameraValues values);
    void SetPreview(TheatreCameraContext context, TheatreWorkspacePreview preview);
}
#endif
