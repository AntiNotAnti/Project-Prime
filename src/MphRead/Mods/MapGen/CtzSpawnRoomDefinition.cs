using System;
using System.Linq;
using OpenTK.Mathematics;
namespace MphRead.Mods.MapGen;
public sealed class CtzSpawnRoomDefinition
{
    public int Side { get; set; }
    public float[] Min { get; set; } = [0,0,0];
    public float[] Max { get; set; } = [1,1,1];
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Valid
    {
        get
        {
            if(Side is not (0 or 1) || Min?.Length!=3 || Max?.Length!=3) return false;
            for(int i=0;i<3;i++) if(!float.IsFinite(Min[i]) || !float.IsFinite(Max[i]) || Min[i]>=Max[i]) return false;
            return true;
        }
    }
    public bool Contains(Vector3 p) => Valid && float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)
        && p.X>=Min[0] && p.Y>=Min[1] && p.Z>=Min[2] && p.X<=Max[0] && p.Y<=Max[1] && p.Z<=Max[2];
}
