#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
namespace MphRead.Mods.Launcher;
internal enum StudioAudioFileTarget { MusicWave, CustomCueWave }
/// <summary>I/O effect only. The foreground presenter owns source/page/field-version checks
/// before assigning this result to a draft; choosing a file never saves or applies settings.</summary>
internal static class NativeStudioAudioFilePicker
{
    internal static Task<string?> Choose(StudioAudioFileTarget target,CancellationToken token)=>target switch
    {
        StudioAudioFileTarget.MusicWave=>NativeReplayProjectPicker.OpenFile("Choose Replay Studio music WAV","Music WAV","wav",token),
        StudioAudioFileTarget.CustomCueWave=>NativeReplayProjectPicker.OpenFile("Choose Replay Studio custom cue WAV","Custom cue WAV","wav",token),
        _=>throw new ArgumentOutOfRangeException(nameof(target))
    };
}
#endif
