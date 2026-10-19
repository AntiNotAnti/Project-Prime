#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Core;

/// <summary>One immutable reviewed audio snapshot, reusable by mix and queued export owners.</summary>
public sealed record TheatreStudioAudioSettings(bool Enabled=true,bool GameEvents=true,bool CombatFeedback=false,
    float GameGain=1,float CombatGain=1,float ReplayGain=1,float MusicGain=.5f,string MusicFile="",
    IReadOnlyList<TheatreAudioCue>? Cues=null);
public interface ITheatreStudioAudioSettingsBackend
{
    TheatreStudioAudioSettings ReadAudioSettings(TheatreEditorContext context);
    void SaveAudioSettings(TheatreEditorContext context,TheatreStudioAudioSettings settings);
}
#endif
