#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System.Threading;
using MphRead.Mods.Replay;
namespace MphRead.Mods.Launcher.RmlUi.Render;
/// <summary>Reviewed preference transfers bind durable recording bytes/range, never old cache witnesses.</summary>
internal sealed record ReplayProjectPreferencesSnapshot(ReplayStudioAudioSettingsReview Audio,
    ReplayStudioPresentationPreferencesReview Presentation);
internal static class RmlUiReplayProjectPreferences
{
    internal static ReplayProjectPreferencesSnapshot Capture(string logical,byte[] sourceHash,uint start,uint end,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var audio=ReplayStudioAudioSettingsStore.ReadForCopy(logical,ReplayStudioAudioSettingsStore.CreateBinding(sourceHash,start,end));
        var presentation=ReplayStudioPresentationPreferencesStore.ReadForCopy(logical,ReplayStudioPresentationPreferencesStore.CreateBinding(sourceHash,start,end));
        return new(audio,presentation);
    }
    internal static void Stage(string staged,ReplayProjectPreferencesSnapshot? reviewed,byte[] sourceHash,uint start,uint end,CancellationToken token)
    {
        if(reviewed==null)return;token.ThrowIfCancellationRequested();
        ReplayStudioAudioSettingsStore.StageRebound(staged,reviewed.Audio,ReplayStudioAudioSettingsStore.CreateBinding(sourceHash,start,end),token);
        ReplayStudioPresentationPreferencesStore.StageRebound(staged,reviewed.Presentation,ReplayStudioPresentationPreferencesStore.CreateBinding(sourceHash,start,end),token);
    }
    internal static void Validate(ReplayProjectPreferencesSnapshot? reviewed)
    {
        if(reviewed==null)return;
        ReplayReviewedMetadata.Validate(reviewed.Audio.Reviewed);ReplayReviewedMetadata.Validate(reviewed.Presentation.Reviewed);
    }
}
#endif
