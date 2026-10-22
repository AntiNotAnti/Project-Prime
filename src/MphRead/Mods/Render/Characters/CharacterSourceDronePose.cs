using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

// Presentation over native post-step burst/laser state/frame. No gameplay events or clock.
// Clip choices/Snap/held return fade adapted from SecondHunt DroneBody and
// NativeBodyAnimator, 41452f2d (MIT, Copyright2020 NoneGiven; LICENSE retained).
internal sealed class CharacterSourceDronePose
{
    internal static IReadOnlyList<int> RequiredOrdinals {get;}=Array.AsReadOnly(new[]{13,75,73,74,29,4,3,2,5,6,1,18});
    internal const double ReturnBlendSeconds=.12d;
    private readonly IReadOnlyDictionary<int,CharacterSourceClip> _clips;
    private Matrix4[]? _last,_from;
    private int _previousState=-1,_fadeSteps;
    internal int CurrentOrdinal {get;private set;}=13;
    internal double ClipSeconds {get;private set;}
    internal float BlendAmount {get;private set;}=1;
    internal long ReadyCompletedSteps {get;private set;}
    internal long FreeCompletedSteps {get;private set;}
    internal IReadOnlyList<Matrix4> CurrentLocalPose => Array.AsReadOnly(_last??Array.Empty<Matrix4>());
    internal CharacterSourceClip CurrentClip => _clips[CurrentOrdinal];
    internal IReadOnlyDictionary<int,CharacterSourceClip> Clips => _clips;
    internal CharacterSourceDronePose(IReadOnlyDictionary<int,CharacterSourceClip> clips)
    {
        if(clips.Count!=12||RequiredOrdinals.Any(i=>!clips.TryGetValue(i,out var clip)||clip.Ordinal!=i
            ||clip.MotionPolicy!="drone-pinned-root-pelvis-v1"
            ||!CharacterGeneratedDroneProfile.MatchesClip(i,clip.ResourceId,clip.ContentSha256)))
            throw new InvalidDataException("Exact twelve independently derived drone primitives required.");
        _clips=new ReadOnlyDictionary<int,CharacterSourceClip>(clips.ToDictionary(p=>p.Key,p=>p.Value));
    }
    internal static bool Supports(int state,int frame) => state switch
    {0=>frame==0,1=>frame is >=0 and <32,2=>frame is >=0 and <150,3=>frame is >=0 and <24,
        4=>frame is >=0 and <134,>=5 and <=10=>frame==0,11=>frame is >=4 and <72,_=>false};
    internal static int OrdinalForState(int state) => state switch
    {0=>13,1=>75,2=>73,3=>74,4=>29,5=>4,6=>3,7=>2,8=>5,9=>6,10=>1,11=>18,
        _=>throw new ArgumentOutOfRangeException(nameof(state))};
    internal static int StateForFreeOrdinal(int ordinal) => ordinal switch
    {13=>0,4=>5,3=>6,2=>7,5=>8,6=>9,1=>10,_=>throw new ArgumentOutOfRangeException(nameof(ordinal))};
    private static bool IsFree(int state) => state==0||state is >=5 and <=10;
    internal void Reset()
    {
        _last=_from=null;_previousState=-1;_fadeSteps=0;ReadyCompletedSteps=FreeCompletedSteps=0;
        CurrentOrdinal=13;ClipSeconds=0;BlendAmount=1;
    }
    // Full native fallback interrupts the blend. Re-entry into a running burst
    // samples its current native phase, never a stale paused arm animation.
    internal void Suspend(){_last=_from=null;_fadeSteps=0;BlendAmount=1;}
    internal IReadOnlyList<Matrix4> Sample(int state,int nativeFrame)
    {
        if(!Supports(state,nativeFrame))throw new ArgumentOutOfRangeException(nameof(nativeFrame));
        if(state!=_previousState)
        {
            if(IsFree(state))
            {
                if(state==0&&_previousState>0)ReadyCompletedSteps=0;
                FreeCompletedSteps=0;
                _from=_last?.ToArray();_fadeSteps=0;
            }
            else {_from=null;_fadeSteps=0;} // Snap burst/laser and post-flash hit entry.
        }
        CurrentOrdinal=OrdinalForState(state);var clip=CurrentClip;
        if(IsFree(state))FreeCompletedSteps++;
        // Native counters describe the post-step state. Advance the first
        // authored phase once on entry, matching the source animator policy;
        // native frame sampling also catches up after full fallback pictures.
        ClipSeconds=state switch
        {
            0=>(++ReadyCompletedSteps/60d)%clip.Duration,
            1=>clip.Duration*Math.Min((nativeFrame+1)/32d,1),
            2=>((nativeFrame+1)/60d)%clip.Duration,
            3=>clip.Duration*Math.Min((nativeFrame+1)/24d,1),
            // Public laser frame is the next native frame, including windup and
            // recovery. Body pose never consumes source damage/sound events.
            4=>clip.Duration*Math.Min((nativeFrame+1)/134d,1),
            >=5 and <=10=>(FreeCompletedSteps/60d)%clip.Duration,
            // The native damage age already includes the hidden flash steps.
            // Never restart a hit at zero after fallback or emit source events.
            11=>nativeFrame/60d,
            _=>throw new InvalidOperationException()
        };
        var pose=clip.Sample(ClipSeconds).ToArray();BlendAmount=1;
        if(_from!=null)
        {
            BlendAmount=(float)Math.Min(++_fadeSteps/(ReturnBlendSeconds*60d),1);
            for(int n=0;n<pose.Length;n++)pose[n]=CharacterSourceBodyPose.Mix(_from[n],pose[n],BlendAmount);
            if(BlendAmount==1)_from=null;
        }
        _previousState=state;_last=pose;
        return Array.AsReadOnly(pose);
    }
}
