namespace MphRead.CorpsePhysics;

// Maps a validated named biped tree into ReferenceScene's fifteen part indices.
// It does not qualify a model, invent skin weights or create collision shapes.
// Missing wrist nodes use the forearm; missing Pelvis uses the two hip anchors.
public static class CorpseHumanoidMap
{
    public static bool TryMap(ReadOnlySpan<string> names, ReadOnlySpan<int> parents, Span<byte> destination)
    {
        if (names.Length is < 1 or > CorpseAffineBinding.MaximumNodes || parents.Length != names.Length
            || destination.Length < names.Length || parents[0] != -1) return false;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrEmpty(names[i]) || names[i].Length > 80 || i > 0 && (parents[i] < 0 || parents[i] >= i)) return false;
            for (int j = 0; j < i; j++) if (string.Equals(names[i], names[j], StringComparison.Ordinal)) return false;
        }
        Span<int> anchors = stackalloc int[15]; anchors.Fill(-1);
        for (int i = 0; i < names.Length; i++)
        {
            int body = names[i] switch
            {
                "Pelvis" => 0, "Spine_2" => 1, "Head_1" => 2,
                "L_shoulder" => 3, "L_elbow" => 4, "L_wrist" => 5,
                "R_shoulder" => 6, "R_elbow" => 7, "R_wrist" => 8,
                "L_hip" => 9, "L_knee" => 10, "L_ankle" => 11,
                "R_hip" => 12, "R_knee" => 13, "R_ankle" => 14, _ => -1
            };
            if (body >= 0) anchors[body] = i;
        }
        for (int body = 1; body < 15; body++) if (body is not (5 or 8) && anchors[body] < 0) return false;
        if (parents[anchors[10]] != anchors[9] || parents[anchors[11]] != anchors[10]
            || parents[anchors[13]] != anchors[12] || parents[anchors[14]] != anchors[13]
            || parents[anchors[4]] != anchors[3] || parents[anchors[7]] != anchors[6]
            || anchors[5] >= 0 && parents[anchors[5]] != anchors[4]
            || anchors[8] >= 0 && parents[anchors[8]] != anchors[7]) return false;
        // Helper paths may share a Collar (Trace/Noxus). A shared ancestor
        // remains torso; only exclusive branches follow head/shoulder bodies.
        Span<byte> owners = stackalloc byte[names.Length]; owners.Clear();
        for(int route=0;route<3;route++)
        {
            int body=route==0?2:route==1?3:6;
            int at=parents[anchors[body]];
            while(at>=0&&at!=anchors[1])
            {
                if(anchors.Contains(at))return false;
                owners[at]|=(byte)(1<<route);at=parents[at];
            }
            if(at!=anchors[1])return false;
        }
        Span<byte> map = stackalloc byte[names.Length];
        for(int i=0;i<names.Length;i++)
        {
            map[i]=0;
            for(int at=i;at>=0;at=parents[at])
            {
                int body=anchors.IndexOf(at);
                if(body>=0){map[i]=(byte)body;break;}
                if(owners[at]!=0)
                {map[i]=owners[at] switch{1=>2,2=>3,4=>6,_=>1};break;}
                if(names[at]=="Spine_1"){map[i]=1;break;}
            }
        }
        map.CopyTo(destination); return true;
    }
}
