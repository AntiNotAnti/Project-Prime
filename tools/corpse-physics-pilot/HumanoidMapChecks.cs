using System;
using System.Linq;
using MphRead.CorpsePhysics;

// Named-tree mapping math only. These synthetic metadata fixtures do not qualify
// skin weights, native art, affine bind offsets or physical ragdoll retargeting.
public static class HumanoidMapChecks
{
    private static (string[] Names,int[] Parents) NativeShape()=>
    (new[]{"Dummy_Root","Skeleton_Root","Pelvis","L_hip","L_knee","L_ankle","R_hip","R_knee","R_ankle","Spine_1","Spine_2","Head_1","L_shoulder","L_elbow","L_wrist","L_varias2_SDK","R_shoulder","R_elbow","R_varias2_SDK"},
     new[]{-1,0,1,2,3,4,2,6,7,1,9,10,10,12,13,10,10,16,10});
    public static void Run(Action<bool,string> check)
    {
        var (names,parents)=NativeShape();var originalNames=(string[])names.Clone();var originalParents=(int[])parents.Clone();var output=Enumerable.Repeat((byte)255,132).ToArray();
        byte[] expected={0,0,0,9,10,11,12,13,14,1,1,2,3,4,5,1,6,7,1};
        check(CorpseHumanoidMap.TryMap(names,parents,output)&&output.Take(19).SequenceEqual(expected)&&output.Skip(19).All(b=>b==255),
            "named native-like tree maps15 physical categories without equating node IDs and body IDs");
        check(names.SequenceEqual(originalNames)&&parents.SequenceEqual(originalParents),"mapping never rewrites supplied immutable-name/tree inputs");
        var smallNames=new[]{"Root","Spine_2","Head_1","L_shoulder","L_elbow","R_shoulder","R_elbow","L_hip","L_knee","L_ankle","R_hip","R_knee","R_ankle","LeftHandAttachment","RightHandAttachment"};
        var smallParents=new[]{-1,0,1,1,3,1,5,0,7,8,0,10,11,4,6};
        check(CorpseHumanoidMap.TryMap(smallNames,smallParents,output)&&output[0]==0&&output[7]==9&&output[10]==12&&output[13]==4&&output[14]==7,
            "missing Pelvis/wrist nodes retain root/hip identities and forearm attachment fallback without invented wrists");
        var manyNames=names.Concat(Enumerable.Range(19,109).Select(i=>"UniqueHelper"+i)).ToArray();var manyParents=parents.Concat(Enumerable.Repeat(0,109)).ToArray();
        check(CorpseHumanoidMap.TryMap(manyNames,manyParents,output)&&output.Take(19).SequenceEqual(expected)&&output.Skip(19).Take(109).All(b=>b==0),
            "inclusive128-node named tree maps bounded helpers to their actual nearest ancestor");
        var longName=(string[])names.Clone();longName[15]=new string('x',80);
        check(CorpseHumanoidMap.TryMap(longName,parents,output),"unrecognized helper name at inclusive80 character limit is valid metadata");
        void Reject(string[] n,int[] p,int destinationLength,string label)
        {Array.Fill(output,(byte)255);check(!CorpseHumanoidMap.TryMap(n,p,output.AsSpan(0,destinationLength))&&output.All(b=>b==255),label+" refuses atomically");}
        Reject(Array.Empty<string>(),Array.Empty<int>(),output.Length,"empty tree");
        Reject(manyNames.Append("OverBudget").ToArray(),manyParents.Append(0).ToArray(),output.Length,"129-node tree");
        Reject(names,parents[..^1],output.Length,"mismatched parent span");
        Reject(names,parents,names.Length-1,"short destination span");
        foreach(string? malformed in new string?[]{null,"",new('x',81),names[0]})
        {var n=(string[])names.Clone();n[15]=malformed!;Reject(n,parents,output.Length,"null/empty/oversized/duplicate exact helper name");}
        foreach(int index in new[]{2,3,4,5,6,7,8,10,11,12,13,16,17})
        {
            if(index==2)continue; // Pelvis is explicitly optional.
            var n=(string[])names.Clone();n[index]="MissingRequired"+index;Reject(n,parents,output.Length,"missing required anchor "+names[index]);
        }
        var wrongCase=(string[])names.Clone();wrongCase[11]="head_1";Reject(wrongCase,parents,output.Length,"required names are ordinal/case-sensitive");
        foreach(var change in new[]{(0,0),(1,-1),(4,4),(4,5),(4,-1),(4,6),(5,3),(7,3),(13,10),(14,12),(17,10)})
        {var p=(int[])parents.Clone();p[change.Item1]=change.Item2;Reject(names,p,output.Length,"root/ordered-tree/direct-leg/direct-arm chain violation");}
        foreach(var change in new[]{(11,13),(12,11),(16,1)})
        {var p=(int[])parents.Clone();p[change.Item1]=change.Item2;Reject(names,p,output.Length,"head/shoulder route crosses another anchor or misses torso");}
        // Exclusive helpers on head/left/right branches must move with that body.
        var helperNames=new[]{"Root","Spine_2","Neck","Head_1","LeftCollar","L_shoulder","L_elbow","RightCollar","R_shoulder","R_elbow","L_hip","L_knee","L_ankle","R_hip","R_knee","R_ankle"};
        var helperParents=new[]{-1,0,1,2,1,4,5,1,7,8,0,10,11,0,13,14};
        check(CorpseHumanoidMap.TryMap(helperNames,helperParents,output)&&output[2]==2&&output[4]==3&&output[7]==6,
            "unshared helper chains above head and shoulders map to their exclusive body category");
        check(CorpseHumanoidMap.TryMap(helperNames.Concat(new[]{"HeadSocketSibling","LeftCollarBadge"}).ToArray(),helperParents.Concat(new[]{2,4}).ToArray(),output)
            &&output[16]==2&&output[17]==3,"unnamed side branches of exclusive helper inherit that helper body instead of detaching to torso");
        for(int warm=0;warm<2000;warm++)CorpseHumanoidMap.TryMap(names,parents,output);
        long allocated=GC.GetAllocatedBytesForCurrentThread();int successes=0;
        for(int sample=0;sample<100_000;sample++)if(CorpseHumanoidMap.TryMap(names,parents,output))successes++;
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        check(successes==100_000&&allocated==0,"100k warmed named-tree mappings allocate0B and remain nonvacuous");

        // Exact shape of the shared Collar helper in extracted native Trace/Noxus
        // metadata. This is a layout regression, not a claim to load native assets.
        string[] sharedNames={"Dummy_Root","Skeleton_Root","L_hip","L_knee","L_ankle","R_hip","R_knee","R_ankle","Spine_1","Spine_2","Collar","L_collar","L_shoulder","L_elbow","Neck_1","Head_1","R_collar","R_shoulder","R_elbow"};
        int[] sharedParents={-1,0,1,2,3,1,5,6,1,8,9,10,11,12,10,14,10,16,17};
        check(CorpseHumanoidMap.TryMap(sharedNames,sharedParents,output)&&output[10]==1&&output[11]==3&&output[14]==2&&output[16]==6,
            "actual native-like shared Collar stays torso while exclusive left/neck/right branches retain their own body mapping");
        check(CorpseHumanoidMap.TryMap(sharedNames.Append("SharedCollarBadge").ToArray(),sharedParents.Append(10).ToArray(),output)&&output[19]==1,
            "side attachment below shared Collar inherits torso rather than an arbitrary first route");
    }
}
