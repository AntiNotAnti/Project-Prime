using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

/// <summary>Exact source-space camera curve positions for successive Studio viewport draws.</summary>
internal sealed class ReplayCameraPathSamples
{
    internal const int SampleCount=129;
    private ReplayCameraTrack? _owner;
    private ReplayCameraKeyframe[] _keys=Array.Empty<ReplayCameraKeyframe>();
    private Vector3[] _points=Array.Empty<Vector3>();
    internal int Builds { get; private set; }

    internal IReadOnlyList<Vector3> Positions(ReplayCameraTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);
        if(ReferenceEquals(_owner,track)&&_keys.SequenceEqual(track.Keys))return _points;
        _owner=track;_keys=track.Keys.ToArray();
        if(_keys.Length<2){_points=Array.Empty<Vector3>();Builds++;return _points;}
        double first=_keys[0].Frame,last=_keys[^1].Frame;
        var points=new Vector3[SampleCount];
        for(int i=0;i<SampleCount;i++)
        {
            if(!track.Sample(first+(last-first)*i/(SampleCount-1),out var sample))
            {_points=Array.Empty<Vector3>();Builds++;return _points;}
            points[i]=sample.Position;
        }
        _points=points;Builds++;return _points;
    }
}
