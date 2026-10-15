#if MPHREAD_STUDIO
using System;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Sound;

namespace MphRead.Mods.StudioReplay;

/// <summary>Only the actively playing private presentation owns the shared output device.</summary>
internal sealed class StudioReplayPreviewAudio : IDisposable
{
    private readonly PassiveReplayPlayer _player;
    private readonly ReplayCombatPresenter _feedback=new();
    private ulong _version;
    internal string? Error { get; private set; }
    internal StudioReplayPreviewAudio(PassiveReplayPlayer player)
    {
        StudioReplayAudioRuntime.Configure();
        _player=player;player.Stepped+=AfterStep;player.Replaced+=Replaced;
    }
    internal void Update(bool enabled)
    {
        if(!enabled||!_player.Ready||_player.Transport.IsPaused||_player.Transport.AtEnd){Release();return;}
        var scene=_player.Current.Scene;
        try
        {
            if(Sfx.Instance is null)Sfx.Load(scene);
            if(Sfx.Instance is not SfxInstance){Error="Audio device is unavailable.";return;}
            if(_version==0||!ReplayAudioOwner.Owns(scene))_version=ReplayAudioOwner.Acquire(scene,null);
            Error=null;
        }
        catch(Exception ex)when(ex is not OutOfMemoryException and not StackOverflowException){Error=ex.Message;Release();}
    }
    private void AfterStep(Scene scene)
    {
        if(_version==0||!ReplayAudioOwner.Owns(scene)||_player.Transport.IsSeeking)return;
        scene.StudioReplayApplyPresentationCamera();
        _feedback.Update(scene,_player.Current.Session);
        Sfx.Update(1f/60);
    }
    private void Replaced(Scene previous,PassiveReplayScene replacement){Release();_feedback.Reset();}
    private void Release()
    {
        if(_version!=0&&ReplayAudioOwner.Owns(_player.Current.Scene))Mods.Sound.CombatFeedbackAudio.StopPlayback();
        ReplayAudioOwner.Release(_version);_version=0;
    }
    public void Dispose(){Release();_player.Stepped-=AfterStep;_player.Replaced-=Replaced;}
}

#endif
