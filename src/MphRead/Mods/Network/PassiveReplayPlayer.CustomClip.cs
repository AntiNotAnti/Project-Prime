using System;
using System.Diagnostics;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

internal sealed partial class PassiveReplayPlayer
{
    private readonly ReplayTimelineClip? _customClip;
    internal static PassiveReplayPlayer OpenCustomClip(ReplayTimelineClip clip, PreparedReplayWeaponPackage resources, Vector2i size)
        => new(clip, resources, size);
    private PassiveReplayPlayer(ReplayTimelineClip clip, PreparedReplayWeaponPackage resources, Vector2i size)
    {
        _customArchive = true; _customClip = clip; _customThread = Environment.CurrentManagedThreadId;
        _options = ReplayPlayerOptions.Linear; _firstFrame = clip.StartRecordingFrame;
        _open = () => throw new InvalidOperationException("A frozen custom clip owns a linear private player.");
        Current = PassiveReplayScene.OpenCustomClip(clip, resources, size);
        Transport.SeekingEnabled = false; Transport.Begin(); Transport.ContinueSeek(clip.StartRecordingFrame, resume: true);
    }
    private int UpdateCustomClip(int maximumSteps, double maximumMilliseconds)
    {
        long began = Stopwatch.GetTimestamp();
        bool seeking = Transport.IsSeeking;
        int due = Math.Min(maximumSteps, Transport.FramesDue()), steps = 0;
        while (steps < due && (!seeking || steps == 0 || Stopwatch.GetElapsedTime(began).TotalMilliseconds < maximumMilliseconds))
        {
            if (!Current.StepCustomClip()) { Transport.AfterFrame(); break; }
            Transport.AfterFrame(); steps++; Stepped?.Invoke(Current.Scene);
            if (seeking && !Transport.IsSeeking) break;
        }
        return steps;
    }
}
