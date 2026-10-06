#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Render.Characters;

/// <summary>Opt-in acceptance of Weavel's separate native form-owned turret.</summary>
internal static class HalfturretAcceptanceCheck
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Json=new() { WriteIndented=true };
    private static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Private)!.GetValue(owner)!;
    private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private)!.SetValue(owner,value);
    private static void Require(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Sha(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Hold(Keybind key) { key.IsDown=true; key.IsPressed=true; }
    private static RenderItem[] Items(Scene scene)=>Field<List<RenderItem>>(scene,"_decalItems")
        .Concat(Field<List<RenderItem>>(scene,"_nonDecalItems")).Concat(Field<List<RenderItem>>(scene,"_translucentItems")).Distinct().ToArray();
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    private static bool Finite(Matrix4 m)
    { for (int row=0;row<4;row++) for (int col=0;col<4;col++) if (!float.IsFinite(m[row,col])) return false; return true; }
    private static Matrix4 NativeRest(Model model,int index)
    {
        Node n=model.Nodes[index];
        Matrix4 local=Matrix4.CreateScale(n.Scale)*Matrix4.CreateRotationX(n.Angle.X)*Matrix4.CreateRotationY(n.Angle.Y)
            *Matrix4.CreateRotationZ(n.Angle.Z)*Matrix4.CreateTranslation(n.Position/model.Scale);
        return n.ParentIndex < 0 ? local : local*NativeRest(model,n.ParentIndex);
    }
    private static Matrix4 Palette(RenderItem packet,int joint)
    {
        int i=joint*16; var p=packet.MatrixStack;
        return new(p[i],p[i+1],p[i+2],p[i+3],p[i+4],p[i+5],p[i+6],p[i+7],
            p[i+8],p[i+9],p[i+10],p[i+11],p[i+12],p[i+13],p[i+14],p[i+15]);
    }

    internal static int Run(string room,string output,string sourceAudit,string sourceAuditSha256,string sourceGlb)
    {
        string directory=Path.GetFullPath(output); Directory.CreateDirectory(directory);
        File.Delete(Path.Combine(directory,"acceptance.json")); File.Delete(Path.Combine(directory,"failure.txt"));
        GameSettings.Apply(GameState.LoadSettings());
        bool oldHd=RenderOptions.CharacterModelReplacements, oldAdvanced=RenderOptions.AdvancedMaterials;
        bool oldBright=RenderOptions.BrightSkins,oldCosmetics=RenderOptions.ShowCustomCosmetics,oldFps=RenderOptions.ShowFps;
        bool oldDetail=Features.MaxPlayerDetail,oldForce=MapAudit.ForceEveryone;
        var oldStyle=RenderOptions.BrightSkinStyle; var oldOutline=RenderOptions.PlayerOutline;
        Scene? scene=null,preview=null;
        var frames=new List<object>(); var cases=new List<object>();
        var animations=new HashSet<int>(); var usedNativeNodes=new HashSet<string>();
        int eligibleFrames=0,submittedFrames=0,fallbackFrames=0,suppressedFrames=0,disabledNodeFrames=0;
        int materialOnFrames=0,materialOffFrames=0,brightFrames=0,outlineFrames=0,damageFrames=0;
        int doubleDamageFrames=0,freezeFrames=0,ownerStatusFrames=0,teamFrames=0,shotFrames=0,projectiles=0;
        int ownerFreezeFrames=0,ownerDamageFrames=0,ownerAlphaFrames=0,ownerCloakFrames=0,turretAlphaFrames=0;
        var suitRecolors=new HashSet<int>();
        int poseFrames=0,fallFrames=0,groundedFrames=0,entryEdges=0,exitEdges=0;
        float maximumBindError=0,maximumWeightError=0,maximumPoseError=0,maximumProjectileOriginError=0,maximumAimChange=0;
        bool morph=false,unmorph=false,turretDied=false,ownerDied=false,respawned=false;
        CharacterWeightedModelData? weightedData=null; CharacterRigidModelData? rigidData=null;
        CharacterWeightedRenderModel? weighted=null; CharacterRigidRenderModel? rigid=null;
        Node? disabledNode=null; bool savedEnabled=true;
        try
        {
            Require(oldHd,"HD character models must already be enabled.");
            string auditPath=Path.GetFullPath(sourceAudit),sourcePath=Path.GetFullPath(sourceGlb);
            Require(sourceAuditSha256.Length == 64 && Sha(auditPath).Equals(sourceAuditSha256,StringComparison.OrdinalIgnoreCase),
                "Turret conversion audit differs from its reviewed SHA-256.");
            using var audit=JsonDocument.Parse(File.ReadAllBytes(auditPath)); var a=audit.RootElement;
            Require(a.GetProperty("pass").GetBoolean() && a.GetProperty("hunter").GetString() == "Weavel"
                && a.GetProperty("part").GetString()?.Equals("halfturret",StringComparison.OrdinalIgnoreCase) == true,
                "Expected a passing Weavel halfturret conversion audit.");
            string sourceHash=Sha(sourcePath);
            string auditedSource=a.GetProperty(a.TryGetProperty("sourceGlbSha256",out _) ? "sourceGlbSha256" : "sourceSha256").GetString()!;
            Require(sourceHash.Equals(auditedSource,StringComparison.OrdinalIgnoreCase),"Original Source GLB differs from the turret audit.");
            bool mobile=CharacterModelPack.ForceMobileTierForCheck;
            string expectedHash=a.GetProperty(mobile && a.TryGetProperty("shippingMobileGlbSha256",out _)
                ? "shippingMobileGlbSha256" : "shippingGlbSha256").GetString()!;
            bool teamRequired=a.TryGetProperty("teamRecolorsRequired",out var t) && t.GetBoolean();
            var pack=CharacterModelPack.LoadDefault(out var packIssue);
            Require(packIssue == null,"Character pack rejected: "+packIssue);
            Require(pack.TryResolve(Hunter.Weavel,CharacterModelPart.Halfturret,out var asset),"Reviewed turret entry is missing.");
            string shippingHash=Sha(asset.ModelPath);
            string manifestPath=Path.Combine(CharacterModelPack.DefaultDirectory,"characters.json"),manifestHash=Sha(manifestPath);
            Require(expectedHash.Length == 64 && shippingHash.Equals(expectedHash,StringComparison.OrdinalIgnoreCase),
                "Installed turret differs from the audited shipping GLB.");
            if (asset.Skinning == CharacterSkinningMode.Weighted4) weightedData=CharacterWeightedModelLoader.Load(asset);
            else rigidData=CharacterRigidModelLoader.Load(asset);
            var albedos=weightedData != null ? weightedData.Primitives.Select(p=>p.Albedo) : rigidData!.Primitives.Select(p=>p.Albedo);
            Require(albedos.Any(x=>x != null),"The replacement contains no embedded Source albedo.");
            if (teamRequired) Require(albedos.Any(x=>x?.Recolors?.ContainsKey(4) == true)
                && albedos.Any(x=>x?.Recolors?.ContainsKey(5) == true),"Required native orange/green turret variants are absent.");
            DebugLog.Force(); RenderOptions.BrightSkins=false; RenderOptions.PlayerOutline=PlayerOutlineStyle.Off;
            RenderOptions.AdvancedMaterials=true; RenderOptions.ShowCustomCosmetics=false; RenderOptions.ShowFps=false;
            Features.MaxPlayerDetail=true; MapAudit.ForceEveryone=true;
            var settings=DesktopGlContext.Settings(background:true);
            settings.ClientSize=new(800,600); settings.CurrentMonitor=Monitors.GetPrimaryMonitor().Handle;
            settings.StartVisible=true;settings.StartFocused=false;settings.Title="Project Prime Weavel halfturret acceptance";
            using var window=new NativeWindow(settings); using var graphics=new DesktopGraphicsSession(window);
            try
            {
                preview=new Scene(window.FramebufferSize,SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{});
                preview.SideScene=true;preview.OnLoad();preview.OnResize();
                string bipedHash=Preview(preview,window,pack,directory,"launcher-before");
                Scene.LauncherPreview=false;Scene.PreviewWanted=false;
                scene=new Scene(window.FramebufferSize,SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{});
                scene.Random.SetRng1(123456);scene.AddPlayer(Hunter.Kanden);scene.AddPlayer(Hunter.Weavel);
                scene.Players.Items[0].IsBot=true;scene.Players.Items[0].BotLevel=1;scene.Players.Items[1].IsBot=false;
                scene.AddRoom(room,GameMode.Battle,playerCount:2);scene.OnLoad();scene.OnResize();
                scene.GameState.PointGoal=0;scene.GameState.MatchTime=-1;scene.GameState.SpawnProtection=false;
                scene.GameState.EnhancedHunters=false;scene.GameState.ShadowFreeze=false;
                var owner=scene.Players.Items[1];var opponent=scene.Players.Items[0];
                for (int tick=0;tick<300;tick++)
                {
                    Set(owner,"_spawnInvulnTimer",(ushort)2);scene.OnSimulationFrame();
                    DesktopGraphicsSession.Resize(window);scene.OnDrawFrame();Require(scene.OnRenderFrame(),"Warm-up render stopped.");
                    DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
                }
                Require(owner.Health > 0 && owner.Hunter == Hunter.Weavel,"Weavel did not spawn.");
                scene.SetFreeCamera(true);
                var turret=owner.Halfturret;var native=turret.GetModels()[0];
                Require(CharacterModelPack.ValidateNativeRig(asset,native.Model,out var rigIssue),rigIssue ?? "Turret native rig rejected.");
                var nativeLists=native.Model.Meshes.Where(m=>m.ListId != 0).Select(m=>m.ListId).ToHashSet();
                var ice=Field<ModelInstance>(turret,"_altIceModel");
                var iceLists=ice.Model.Meshes.Where(m=>m.ListId != 0).Select(m=>m.ListId).ToHashSet();
                if (weightedData != null)
                {
                    string[] names=weightedData.Joints.Select(j=>j.TargetNode).ToArray();
                    Require(names.Distinct().Count() == names.Length && names.All(n=>native.Model.Nodes.Any(node=>node.Name == n)),
                        "Weighted turret must contain unique native joints only.");
                    foreach (var joint in weightedData.Joints)
                    {
                        int index=Array.FindIndex(native.Model.Nodes.ToArray(),n=>n.Name == joint.TargetNode);
                        Matrix4 closure=joint.InverseBind*NativeRest(native.Model,index);
                        for (int row=0;row<4;row++) for (int col=0;col<4;col++)
                            maximumBindError=MathF.Max(maximumBindError,MathF.Abs(closure[row,col]-(row == col ? 1 : 0)));
                    }
                    foreach (var v in weightedData.Primitives.SelectMany(p=>p.Vertices))
                    {
                        float[] weights={v.Weights.X,v.Weights.Y,v.Weights.Z,v.Weights.W};
                        var j=CharacterWeightedModelLoader.UnpackJoints(v.PackedJoints);int[] indices={j.J0,j.J1,j.J2,j.J3};
                        Require(weights.All(x=>float.IsFinite(x) && x >= 0) && indices.All(x=>x >= 0 && x < names.Length),"Invalid turret weights/joint indices.");
                        maximumWeightError=MathF.Max(maximumWeightError,MathF.Abs(weights.Sum()-1));
                        for (int slot=0;slot<4;slot++) if (weights[slot] > 0) usedNativeNodes.Add(names[indices[slot]]);
                    }
                    Require(maximumBindError < .0001f && maximumWeightError < .00001f,"Turret inverse binds or normalized loaded weights failed.");
                    Require(CharacterModelRuntime.TryGetWeighted(scene,Hunter.Weavel,CharacterModelPart.Halfturret,native.Model,out weighted),"Weighted turret did not resolve.");
                }
                else
                {
                    foreach (var p in rigidData!.Primitives) usedNativeNodes.Add(p.TargetNode);
                    Require(usedNativeNodes.All(n=>native.Model.Nodes.Any(node=>node.Name == n)),"Rigid turret contains a non-native target node.");
                    Require(CharacterModelRuntime.TryGetRigid(scene,Hunter.Weavel,CharacterModelPart.Halfturret,native.Model,out rigid),"Rigid turret did not resolve.");
                }
                Require(usedNativeNodes.Any(n=>n is "Root" or "Calf" or "Foot" or "Thighs")
                    && usedNativeNodes.Any(n=>n is "TurretBase" or "TurretMid" or "TurretTip"),
                    "Source lower body and barrel must follow their respective native transform domains.");
                var sourceLists=weighted != null ? weighted.Segments.Select(s=>s.ListId).ToHashSet() : rigid!.Segments.Select(s=>s.ListId).ToHashSet();
                Vector3 firstAim=Vector3.Zero;bool observedActive=false;
                var stages=new List<(string Name,int Count)> {("biped-idle",60),("split",120),("turret-idle",90),
                    ("fire",180),("aim-sweep",180),("materials-on",60),("materials-off",60),("materials-restored",60),
                    ("alpha",60),("node-disabled",30),("node-restored",60)};
                for (int suit=0;suit<6;suit++) stages.Add(("suit-"+suit,60));
                stages.AddRange(new[] {("bright",90),("outline-red",60),("team-orange",60),("team-green",60),
                    ("double-damage",90),("turret-damage",60),("turret-freeze",90),("turret-thaw",90),
                    ("owner-damage",60),("owner-freeze",90),("owner-thaw",90),("owner-cloak",60),("owner-visible",60),
                    ("owner-alpha",60),("neutral",60),("fall-collision",180),("unmorph",120),("biped-return",60),
                    ("resplit",120),("turret-death",120),("unmorph-after-turret-death",120),("third-split",120),
                    ("owner-death-respawn",420),("final-biped",60)});
                foreach (var stage in stages)
                {
                    int before=submittedFrames,beforeFallback=fallbackFrames,beforeShots=shotFrames,beforeSuppressed=suppressedFrames;
                    if (stage.Name is "turret-idle" or "neutral" or "resplit" or "third-split")
                    {
                        scene.GameState.Teams=false;owner.Team=Team.None;owner.Recolor=0;
                        RenderOptions.BrightSkins=false;RenderOptions.PlayerOutline=PlayerOutlineStyle.Off;
                        Set(owner,"_doubleDmgTimer",(ushort)0);owner.ModSetFrozen(false);turret.Alpha=1;
                        owner.Flags2 &= ~PlayerFlags2.Cloaking;Set(owner,"_cloakTimer",(ushort)0);Set(owner,"_curAlpha",1f);Set(owner,"_targetAlpha",1f);
                    }
                    if (stage.Name is "materials-on" or "materials-restored") RenderOptions.AdvancedMaterials=true;
                    if (stage.Name == "materials-off") RenderOptions.AdvancedMaterials=false;
                    if (stage.Name == "alpha") turret.Alpha=.4f;
                    if (stage.Name == "node-disabled")
                    {
                        turret.Alpha=1;
                        if (rigid != null)
                        {
                            Node selected=native.Model.Nodes[rigid.Segments[0].NativeNodeIndex];
                            disabledNode=selected.ParentIndex >= 0 ? native.Model.Nodes[selected.ParentIndex] : selected;
                            savedEnabled=disabledNode.Enabled;disabledNode.Enabled=false;
                        }
                    }
                    if (stage.Name == "node-restored" && disabledNode != null) { disabledNode.Enabled=savedEnabled;disabledNode=null; }
                    if (stage.Name.StartsWith("suit-",StringComparison.Ordinal)) owner.Recolor=int.Parse(stage.Name[5..]);
                    if (stage.Name == "bright") {RenderOptions.BrightSkins=true;RenderOptions.BrightSkinStyle=PlayerSkinStyle.Textured;}
                    if (stage.Name == "outline-red") RenderOptions.PlayerOutline=PlayerOutlineStyle.Red;
                    if (stage.Name is "team-orange" or "team-green")
                    {
                        scene.GameState.Teams=true;owner.TeamIndex=stage.Name == "team-orange" ? 0 : 1;opponent.TeamIndex=1-owner.TeamIndex;
                        TeamVisuals.Apply(owner);TeamVisuals.Apply(opponent);RenderOptions.PlayerOutline=PlayerOutlineStyle.Team;
                    }
                    if (stage.Name == "double-damage") Set(owner,"_doubleDmgTimer",(ushort)240);
                    if (stage.Name == "turret-damage")
                    {Set(owner,"_doubleDmgTimer",(ushort)0);owner.TakeDamage(10,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln|DamageFlags.Halfturret,null,opponent);}
                    if (stage.Name == "turret-freeze") {Set(turret,"_timeSinceFrozen",(ushort)255);turret.OnFrozen();}
                    if (stage.Name == "turret-thaw") Set(turret,"_freezeTimer",(ushort)0);
                    if (stage.Name == "owner-damage") owner.TakeDamage(10,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,opponent);
                    if (stage.Name == "owner-freeze") owner.ModSetFrozen(true);
                    if (stage.Name == "owner-thaw") owner.ModSetFrozen(false);
                    if (stage.Name == "owner-cloak") {Set(owner,"_cloakTimer",(ushort)240);owner.Flags2 |= PlayerFlags2.Cloaking;}
                    if (stage.Name == "owner-visible") {owner.Flags2 &= ~PlayerFlags2.Cloaking;Set(owner,"_cloakTimer",(ushort)0);}
                    if (stage.Name == "owner-alpha") {Set(owner,"_curAlpha",.7f);Set(owner,"_targetAlpha",.7f);}
                    if (stage.Name == "fall-collision") {turret.Position=turret.Position.AddY(1.2f);turret.ResetGroundedState();}
                    if (stage.Name == "turret-death") turret.Die();
                    if (stage.Name == "owner-death-respawn") owner.TakeDamage(10000,DamageFlags.IgnoreInvuln|DamageFlags.NoDmgInvuln,null,opponent);
                    for (int age=0;age<stage.Count;age++)
                    {
                        owner.Controls.ClearAll();owner.Controls.MouseAim=false;
                        object input=typeof(PlayerEntity).GetProperty("Input",Private)!.GetValue(owner)!;
                        input.GetType().GetProperty("HasInput")!.SetValue(input,true);
                        if (stage.Name != "owner-death-respawn") owner.Health=999;
                        if (stage.Name != "owner-death-respawn" && turret.Health > 0
                            && owner.Flags2.TestFlag(PlayerFlags2.Halfturret)) turret.Health=999;
                        if ((stage.Name is "split" or "unmorph" or "resplit" or "unmorph-after-turret-death" or "third-split") && age == 0) Hold(owner.Controls.Morph);
                        if (stage.Name is "bright" or "outline-red" or "team-orange" or "team-green") Set(owner,"_timeSinceDamage",(ushort)255);
                        bool activeBefore=turret.Health > 0 && owner.Flags2.TestFlag(PlayerFlags2.Halfturret) && scene.Entities.Contains(turret);
                        // Deployment may insert and process the turret in this
                        // same native tick. Start its cooldown at zero so firing
                        // begins only once a pre-tick turret pose is observable.
                        if (!activeBefore) owner.TimeSinceShot=0;
                        Vector3 previousPosition=turret.Position;
                        // Beam Age is seconds, not ticks. Snapshot the reused pool
                        // so a previous shot cannot be mistaken for this spawn.
                        var previousBeams=owner.EquipInfo.Beams.ToDictionary(b=>b,
                            b=>(b.Owner,b.Age,b.Lifespan,b.SpawnPosition,b.ModShotId));
                        ushort freezeBefore=Field<ushort>(turret,"_freezeTimer");float animationBefore=native.AnimInfo.Frame[0];
                        if (stage.Name == "aim-sweep") opponent.Position=turret.Position+new Vector3(5*MathF.Cos(age*.05f),.5f+MathF.Sin(age*.08f),5*MathF.Sin(age*.05f));
                        else opponent.Position=(activeBefore ? turret.Position : owner.Position)+new Vector3(7,0,7);
                        if (opponent.Health > 0) opponent.Health=999;
                        scene.OnSimulationFrame();
                        bool active=turret.Health > 0 && owner.Flags2.TestFlag(PlayerFlags2.Halfturret) && scene.Entities.Contains(turret);
                        if (!observedActive && active) entryEdges++;if (observedActive && !active) exitEdges++;
                        observedActive=active;
                        morph |= owner.IsMorphing;unmorph |= owner.IsUnmorphing;
                        turretDied |= stage.Name == "turret-death" && turret.Health == 0;
                        ownerDied |= stage.Name == "owner-death-respawn" && owner.Health == 0;
                        respawned |= ownerDied && owner.Health > 0 && !owner.IsAltForm;
                        if (stage.Name == "fall-collision")
                        { if (turret.Position.Y < previousPosition.Y-.00001f) fallFrames++;if (Field<bool>(turret,"_grounded")) groundedFrames++; }
                        if (freezeBefore > 0) Require(native.AnimInfo.Frame[0] == animationBefore,"Frozen native turret animation advanced.");
                        var fresh=owner.EquipInfo.Beams.Where(b=>b.Owner == turret && b.Age <= scene.FrameTime+.00001f
                            && (previousBeams[b].Owner != turret || b.Age < previousBeams[b].Age
                                || b.Lifespan > previousBeams[b].Lifespan+.00001f || b.SpawnPosition != previousBeams[b].SpawnPosition
                                || b.ModShotId != previousBeams[b].ModShotId)).ToArray();
                        if (activeBefore && freezeBefore > 0) Require(fresh.Length == 0,"Frozen turret spawned a new projectile.");
                        if (fresh.Length > 0)
                        {
                            Require(activeBefore && freezeBefore == 0,"Inactive/frozen turret spawned a gameplay projectile.");
                            foreach (var beam in fresh)
                            {
                                float error=(beam.SpawnPosition-previousPosition.AddY(.4f)).Length;
                                maximumProjectileOriginError=MathF.Max(maximumProjectileOriginError,error);
                                Require(Finite(beam.SpawnPosition) && error < .0001f,"Turret projectile left the native Position.AddY(0.4) firing origin.");
                                projectiles++;
                            }
                            shotFrames++;
                        }
                        Follow(scene,active ? turret.Position : owner.Position);
                        DesktopGraphicsSession.Resize(window);scene.OnDrawFrame();
                        var all=Items(scene);var packets=all.Where(p=>p.Type == RenderItemType.Mesh && sourceLists.Contains(p.ListId)).ToArray();
                        int expected=weighted?.Segments.Count ?? rigid!.Segments.Count(s=>native.Model.Nodes[s.NativeNodeIndex].Enabled
                            && native.Model.NodeParentsEnabled(native.Model.Nodes[s.NativeNodeIndex]));
                        bool visible=turret.ScanVisible();bool eligible=active && visible && turret.Alpha > 0 && expected > 0;
                        int nativePackets=all.Count(p=>p.Type == RenderItemType.Mesh && nativeLists.Contains(p.ListId));
                        if (active && visible)
                        {
                            Require(turret.Recolor == owner.Recolor,"Active turret did not follow its owner's native suit/team recolor.");
                            bool fallback=packets.Length != expected || nativePackets != 0;
                            if (fallback) fallbackFrames++;
                            Require(!fallback,$"{stage.Name}/{age}: turret packets {packets.Length}/{expected}, native fallback packets={nativePackets}.");
                            maximumPoseError=MathF.Max(maximumPoseError,InspectNativePose(turret,native));poseFrames++;
                            Vector3 aim=Field<Vector3>(turret,"_aimVector");
                            Require(Finite(aim) && MathF.Abs(aim.Length-1) < .0001f,"Native turret aim is non-finite/unnormalized.");
                            if (firstAim == Vector3.Zero) firstAim=aim;
                            maximumAimChange=MathF.Max(maximumAimChange,(aim-firstAim).Length);
                            foreach (var packet in packets)
                            {
                                if (weighted != null)
                                {
                                    Require(packet.WeightedSkinning && packet.MatrixStackCount == weighted.Joints.Count,"Turret native palette incomplete.");
                                    for (int joint=0;joint<weighted.Joints.Count;joint++)
                                    { var j=weighted.Joints[joint];Matrix4 matrix=j.InverseBind*native.Model.Nodes[j.NativeNodeIndex].Animation;
                                      Require(Finite(matrix) && Palette(packet,joint) == matrix,"Stale native Weighted4 turret matrix."); }
                                    var s=weighted.Segments.Single(s=>s.ListId == packet.ListId);int i=weighted.Segments.ToList().IndexOf(s);
                                    InspectSurface(scene,owner,turret,packet,native.Model.Materials[s.NativeMaterialIndex],s.GetAlbedo(scene,turret.Recolor),s.AlbedoBinding,
                                        s.MaterialMaps,s.WrapS,s.WrapT,s.DoubleSided,s.Transparent,weightedData!.Primitives[i].MaterialMaps);
                                }
                                else
                                {
                                    var s=rigid!.Segments.Single(s=>s.ListId == packet.ListId);var node=native.Model.Nodes[s.NativeNodeIndex];
                                    Require(!packet.WeightedSkinning && packet.MatrixStackCount == 0 && packet.Transform == node.Animation
                                        && Finite(packet.Transform) && packet.BillboardMode == node.BillboardMode,"Stale native rigid turret matrix/billboard.");
                                    int i=rigid.Segments.ToList().IndexOf(s);
                                    InspectSurface(scene,owner,turret,packet,native.Model.Materials[s.NativeMaterialIndex],s.GetAlbedo(scene,turret.Recolor),s.AlbedoBinding,
                                        s.MaterialMaps,s.WrapS,s.WrapT,s.DoubleSided,s.Transparent,rigidData!.Primitives[i].MaterialMaps);
                                }
                            }
                            if (teamRequired && (stage.Name is "team-orange" or "team-green"))
                                Require(weighted != null
                                    ? weighted.Segments.Any(s=>s.GetAlbedo(scene,turret.Recolor) != s.AlbedoBinding)
                                    : rigid!.Segments.Any(s=>s.GetAlbedo(scene,turret.Recolor) != s.AlbedoBinding),
                                    "Required native team turret variant failed to upload/select.");
                            animations.Add(native.AnimInfo.Index[0]);
                            if (eligible)
                            {
                                eligibleFrames++;submittedFrames++;
                                if (RenderOptions.AdvancedMaterials) materialOnFrames++;else materialOffFrames++;
                                if (packets.Any(p=>p.OverrideColor.HasValue && p.TexturedPlayerSkin)) brightFrames++;
                                if (packets.Any(p=>p.PlayerOutlineColor.HasValue)) outlineFrames++;
                                if (turret.TimeSinceDamage < owner.Values.DamageFlashTime*2) damageFrames++;
                                if (owner.DoubleDamage) doubleDamageFrames++;
                                if (scene.GameState.Teams) teamFrames++;
                                if (owner.BrightSkinStatusOverride || owner.BrightSkinFrozenOverlay || owner.Flags2.TestFlag(PlayerFlags2.Cloaking)) ownerStatusFrames++;
                                if (owner.ModFrozen) ownerFreezeFrames++;
                                if (Field<ushort>(owner,"_timeSinceDamage") < owner.Values.DamageFlashTime*2) ownerDamageFrames++;
                                if (owner.CurAlpha < 1) ownerAlphaFrames++;
                                if (owner.Flags2.TestFlag(PlayerFlags2.Cloaking)) ownerCloakFrames++;
                                if (turret.Alpha < 1) turretAlphaFrames++;
                                if (stage.Name.StartsWith("suit-",StringComparison.Ordinal)) suitRecolors.Add(turret.Recolor);
                            }
                            else if (expected == 0) disabledNodeFrames++;
                            if (Field<ushort>(turret,"_freezeTimer") > 0)
                            {Require(all.Any(p=>p.Type == RenderItemType.Mesh && iceLists.Contains(p.ListId)),"Native turret ice overlay lost.");freezeFrames++;}
                        }
                        else
                        {
                            Require(packets.Length == 0 && nativePackets == 0,"Inactive/invisible/dead turret emitted stale geometry.");
                            suppressedFrames++;
                        }
                        foreach (var element in Field<List<EffectElementEntry>>(scene,"_activeElements"))
                            Require(Finite(element.OwnTransform) && Finite(element.Transform),"Native turret scene effect transform is non-finite.");
                        Require(scene.OnRenderFrame(),"Turret render stopped.");
                        if (age is 0 or 15 or 40 || age == stage.Count-1)
                            Require(ScreenCapture.Save(scene,Path.Combine(directory,$"{stage.Name}-{age:D3}.png")),"Turret capture failed.");
                        frames.Add(new {stage=stage.Name,age,frame=scene.FrameCount,active,visible,eligible,turretHealth=turret.Health,
                            ownerHealth=owner.Health,ownerAlt=owner.IsAltForm,ownerRecolor=owner.Recolor,turretRecolor=turret.Recolor,
                            turretFreeze=Field<ushort>(turret,"_freezeTimer"),ownerFrozen=owner.ModFrozen,nativeAnimation=native.AnimInfo.Index[0],
                            nativeAnimationFrame=native.AnimInfo.Frame[0],replacementPackets=packets.Length,expectedReplacementPackets=active && visible ? expected : 0,
                            nativeFallbackPackets=nativePackets,sourceListIds=sourceLists.ToArray()});
                        DesktopGraphicsSession.Present(window);scene.AfterRenderFrame();
                    }
                    cases.Add(new {name=stage.Name,pictures=stage.Count,submittedFrames=submittedFrames-before,fallbackFrames=fallbackFrames-beforeFallback,
                        shotFrames=shotFrames-beforeShots,suppressedFrames=suppressedFrames-beforeSuppressed});
                    Console.WriteLine($"HALFTURRET ACCEPTANCE {stage.Name}: {submittedFrames-before} frames, {fallbackFrames-beforeFallback} fallbacks");
                }
                Require(submittedFrames > 1400 && fallbackFrames == 0,"Turret replacement coverage/fallback check incomplete.");
                Require(materialOnFrames > 0 && materialOffFrames > 0 && brightFrames > 0 && outlineFrames > 0
                    && damageFrames > 0 && doubleDamageFrames > 0 && freezeFrames > 0 && ownerStatusFrames > 0 && teamFrames > 0,
                    "Turret owner/material/status/outline coverage incomplete.");
                Require(ownerFreezeFrames > 0 && ownerDamageFrames > 0 && ownerAlphaFrames > 0 && ownerCloakFrames > 0
                    && turretAlphaFrames > 0 && suitRecolors.Count == 6,"Turret independent owner freeze/damage/alpha/cloak and suit coverage incomplete.");
                Require(morph && unmorph && entryEdges >= 3 && exitEdges >= 3 && !owner.IsAltForm,
                    "Native split/unmorph/turret-death/owner-death transitions incomplete.");
                Require(turretDied && ownerDied && respawned,"Native turret destruction and owner death/respawn were not observed.");
                Require(shotFrames > 0 && projectiles > 0 && animations.Contains(0) && animations.Contains(1)
                    && maximumAimChange > .1f && poseFrames > 0,"Native aim/firing/deploy animation coverage incomplete.");
                Require(fallFrames > 0 && groundedFrames > 0,"Native turret falling/sphere collision did not settle.");
                if (rigid != null) Require(disabledNodeFrames > 0,"Native enabled-node/parent suppression was not exercised.");
                Require(Sha(asset.ModelPath) == shippingHash && Sha(sourcePath) == sourceHash && Sha(auditPath).Equals(sourceAuditSha256,StringComparison.OrdinalIgnoreCase),
                    "Tested turret/source/audit changed during acceptance.");
                Require(Sha(manifestPath) == manifestHash,"Installed turret manifest changed during acceptance.");
                scene.DoCleanup();scene.UnloadGl();scene=null;
                Require(Preview(preview,window,pack,directory,"launcher-after") == bipedHash,"Preexisting biped changed between launcher previews.");
                File.WriteAllText(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(frames,Json));
                File.WriteAllText(Path.Combine(directory,"acceptance.json"),JsonSerializer.Serialize(new {pass=true,hunter="Weavel",part="halfturret",room,
                    backend=GraphicsBackendPolicy.Resolved.ToString(),mobileTextureTier=mobile,skinning=asset.Skinning.ToString(),
                    testedModelPath=asset.ModelPath,testedModelSha256=shippingHash,sourceAuditPath=auditPath,sourceAuditSha256=sourceAuditSha256.ToLowerInvariant(),
                    manifestSha256=manifestHash,boneMap=asset.BoneMap,
                    sourceGlb=sourcePath,sourceGlbSha256=sourceHash,usedNativeNodes,maximumBindError,maximumWeightError,maximumPoseError,maximumProjectileOriginError,
                    eligibleFrames,submittedFrames,fallbackFrames,suppressedFrames,disabledNodeFrames,materialOnFrames,materialOffFrames,brightFrames,outlineFrames,
                    damageFrames,doubleDamageFrames,freezeFrames,ownerStatusFrames,teamFrames,shotFrames,projectiles,poseFrames,maximumAimChange,
                    ownerFreezeFrames,ownerDamageFrames,ownerAlphaFrames,ownerCloakFrames,turretAlphaFrames,suitRecolors,
                    fallFrames,groundedFrames,entryEdges,exitEdges,morph,unmorph,turretDied,ownerDied,respawned,animations,cases,
                    scope="Separate native Weavel halfturret entity in real scene simulation/render with a bot opponent. Source/audit/shipping hashes are bound exactly. Native lower-body and ballistic-aim barrel transform domains, two-pass node poses, complete skin palettes/inverse binds/normalized loaded weights or enabled rigid node hierarchy, Source samplers/alpha/culling/maps, all owner suits/team/bright/outline/status transitions and native ice overlay are checked. Native team emission remains absent, as authored. Gameplay projectile SpawnPosition is checked against pre-simulation native Position.AddY(0.4), before that tick's turret gravity; TurretTip is a visual joint, not a gameplay muzzle. Falling/collision, turret death, owner death/respawn and launcher biped return are exercised. Statuses/target positions are diagnostic inputs. Companion-map bytes and Source-fit fidelity remain offline-audit responsibilities; visible clipping/resemblance require capture review. No physical Android/display/memory or exhaustive combat-balance claim. The turret itself has no launcher preview."
                },Json));
                Console.WriteLine("HALFTURRET ACCEPTANCE PASS "+directory);return 0;
            }
            finally {if (disabledNode != null) disabledNode.Enabled=savedEnabled;scene?.DoCleanup();scene?.UnloadGl();preview?.UnloadGl();}
        }
        catch (Exception error)
        {
            File.Delete(Path.Combine(directory,"acceptance.json"));File.WriteAllText(Path.Combine(directory,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(frames,Json));
            Console.Error.WriteLine("HALFTURRET ACCEPTANCE FAIL "+error);return 1;
        }
        finally
        {
            RenderOptions.CharacterModelReplacements=oldHd;RenderOptions.AdvancedMaterials=oldAdvanced;RenderOptions.BrightSkins=oldBright;
            RenderOptions.BrightSkinStyle=oldStyle;RenderOptions.PlayerOutline=oldOutline;RenderOptions.ShowCustomCosmetics=oldCosmetics;
            RenderOptions.ShowFps=oldFps;Features.MaxPlayerDetail=oldDetail;MapAudit.ForceEveryone=oldForce;
        }
    }

    private static void InspectSurface(Scene scene,PlayerEntity owner,HalfturretEntity turret,RenderItem packet,Material material,
        int? albedo,int? baseAlbedo,MaterialMapBindings maps,RepeatMode wrapS,RepeatMode wrapT,bool doubleSided,bool transparent,
        CharacterEmbeddedMaterialMaps? authoredMaps)
    {
        bool damage=turret.TimeSinceDamage < owner.Values.DamageFlashTime*2;
        bool frozen=Field<ushort>(turret,"_freezeTimer") > 0;
        bool buff=owner.DoubleDamage && material.Lighting > 0;
        int? expected=buff ? owner.DoubleDmgBindingId : albedo;
        if (expected.HasValue) Require(packet.HasTexture && packet.TextureBindingId == expected,"Turret Source/native status binding mismatch.");
        else Require(packet.HasTexture == (material.TextureId != -1) && (!packet.HasTexture || packet.TextureBindingId == material.TextureBindingId),"Turret native untextured/effect material mismatch.");
        Require(packet.PaletteOverride == (damage ? Metadata.RedPalette : (Vector4?)null),"Turret native damage palette mismatch.");
        Require(packet.Diffuse == material.CurrentDiffuse && packet.Ambient == material.CurrentAmbient && packet.Specular == material.CurrentSpecular
            && packet.Lighting == (material.Lighting != 0),"Turret native animated material/lighting mismatch.");
        Require(MathF.Abs(packet.Alpha-material.CurrentAlpha*turret.Alpha) < .00001f,"Turret native material/entity alpha mismatch.");
        Require(packet.CullingMode == (doubleSided ? CullingMode.Neither : material.Culling),"Turret Source/native culling mismatch.");
        bool source=albedo.HasValue && packet.TextureBindingId == albedo && !buff;
        Require(packet.RenderMode == (source && transparent ? RenderMode.Translucent : material.RenderMode),"Turret alpha presentation mismatch.");
        if (source) Require(packet.TexgenMode == TexgenMode.Texcoord && packet.TexcoordMatrix == Matrix4.Identity
            && packet.XRepeat == wrapS && packet.YRepeat == wrapT,"Turret authored UV/sampler state mismatch.");
        if (RenderOptions.AdvancedMaterials) Require((maps.Normal != 0) == (authoredMaps?.Normal != null)
            && (maps.Specular != 0) == (authoredMaps?.MetallicRoughness != null)
            && (maps.Emissive != 0) == (authoredMaps?.Emissive != null),"Turret companion upload incomplete.");
        Require(packet.CosmeticMaterial.NormalBinding == (source && RenderOptions.AdvancedMaterials ? maps.Normal : 0)
            && packet.CosmeticMaterial.SpecularBinding == (source && RenderOptions.AdvancedMaterials ? maps.Specular : 0)
            && packet.CosmeticMaterial.EmissiveBinding == (source && RenderOptions.AdvancedMaterials ? maps.Emissive : 0),"Turret map toggle/native status priority mismatch.");
        Require(packet.Emission == (buff ? Metadata.EmissionGray : Vector3.Zero),"Turret native emission changed (it has no team-emission override).");
        if (buff) Require(packet.TexgenMode == TexgenMode.Normal,"Turret double-damage coordinates changed.");
        Vector4? color=damage ? null : BrightSkins.ForMaterial(BrightSkins.GetColor(owner),material.TextureId != -1,packet.Alpha,scene.ShowTextures);
        Vector4? outline=damage || frozen ? null : BrightSkins.GetOutlineColor(owner);
        Require(packet.OverrideColor == color && packet.PlayerOutlineColor == outline,"Turret owner bright/outline/status mismatch.");
        bool textured=color.HasValue && RenderOptions.BrightSkins && RenderOptions.BrightSkinStyle != PlayerSkinStyle.Solid;
        Require(packet.TexturedPlayerSkin == textured,"Turret textured bright-skin flag mismatch.");
        Require(packet.Cosmetics == (frozen || damage ? default : owner.CosmeticMaterial(false)),"Turret native damage/freeze cosmetic suppression mismatch.");
        if (owner.Recolor >= 4 && albedo != baseAlbedo) Require(packet.TextureBindingId == (buff ? owner.DoubleDmgBindingId : albedo),"Turret team variant did not reach the draw packet.");
    }

    private static float InspectNativePose(HalfturretEntity turret,ModelInstance native)
    {
        // Replay the authoritative two-pass native schedule at this SAME current
        // animation frame, then restore all draw poses. No animation is advanced.
        Model model=native.Model;Node barrel=model.GetNodeByName("TurretBase")!;Node parent=model.Nodes[barrel.ParentIndex];
        var saved=model.Nodes.Select(n=>n.Animation).ToArray();float error=0;
        bool oldIgnoreChild=parent.AnimIgnoreChild,oldIgnoreParent=barrel.AnimIgnoreParent;Matrix4? oldBefore=barrel.BeforeTransform;
        try
        {
            Require(!oldIgnoreChild && !oldIgnoreParent && !oldBefore.HasValue,"Native turret retained temporary aim hierarchy flags.");
            parent.AnimIgnoreChild=true;
            model.AnimateNodes2(0,false,EntityBase.GetTransformMatrix(turret.FacingVector,Vector3.UnitY),Vector3.One,native.AnimInfo);
            parent.AnimIgnoreChild=false;
            barrel.BeforeTransform=EntityBase.GetTransformMatrix(Field<Vector3>(turret,"_aimVector"),Vector3.UnitY,parent.Animation.Row3.Xyz);
            barrel.AnimIgnoreParent=true;model.AnimateNodes2(parent.ChildIndex,false,Matrix4.Identity,Vector3.One,native.AnimInfo);
            barrel.AnimIgnoreParent=false;barrel.BeforeTransform=null;
            Matrix4 root=Matrix4.CreateTranslation(turret.Position.AddY(-.45f));
            for (int i=0;i<model.Nodes.Count;i++)
            {
                Matrix4 expected=model.Nodes[i].Animation*root;
                Require(Finite(expected) && Finite(saved[i]),"Non-finite native turret pose.");
                for (int row=0;row<4;row++) for (int col=0;col<4;col++) error=MathF.Max(error,MathF.Abs(expected[row,col]-saved[i][row,col]));
            }
            Require(error < .00001f,"Turret lower-body/barrel native aim schedule was not preserved.");return error;
        }
        finally
        {
            for (int i=0;i<model.Nodes.Count;i++) model.Nodes[i].Animation=saved[i];
            parent.AnimIgnoreChild=oldIgnoreChild;barrel.AnimIgnoreParent=oldIgnoreParent;barrel.BeforeTransform=oldBefore;
        }
    }

    private static void Follow(Scene scene,Vector3 position)
    {
        Vector3 center=position.AddY(.35f),camera=center+new Vector3(2,1,3);
        for (int step=0;step<24;step++)
        {
            float angle=.6f+step*MathF.PI/12;Vector3 candidate=center+new Vector3(3.5f*MathF.Cos(angle),.8f,3.5f*MathF.Sin(angle));
            bool clear=true;
            foreach (float h in new[] {-.35f,0,.65f})
            {CollisionResult hit=default;if (CollisionDetection.CheckBetweenPoints(candidate,center.AddY(h),TestFlags.None,scene,ref hit)) {clear=false;break;}}
            if (clear) {camera=candidate;break;}
        }
        Set(scene,"_cameraPosition",camera);Set(scene,"_cameraFacing",(center-camera).Normalized());Set(scene,"_cameraUp",Vector3.UnitY);
        Set(scene,"_cameraFov",MathHelper.DegreesToRadians(55));
    }

    private static string Preview(Scene scene,NativeWindow window,CharacterModelPack pack,string directory,string label)
    {
        Scene.LauncherPreview=true;Scene.LauncherHunter=Hunter.Weavel;Scene.LauncherSuit=0;Scene.PreviewWanted=true;
        Scene.PreviewLeft=.1f;Scene.PreviewTop=.1f;Scene.PreviewRight=.9f;Scene.PreviewBottom=.9f;
        bool hd=pack.TryResolve(Hunter.Weavel,CharacterModelPart.Biped,out var biped);string hash=hd ? Sha(biped.ModelPath) : "";
        int drawn=0;
        for (int frame=0;frame<60;frame++)
        {
            scene.ModStepPreview();Require(scene.ModDrawPreviewAlone(window.FramebufferSize),"Biped launcher return render failed.");
            var packets=Field<List<RenderItem>>(scene,"_previewItems").Where(p=>p.Type == RenderItemType.Mesh).ToArray();
            var entity=Field<HunterPreviewEntity>(scene,"_preview");var model=Field<ModelInstance>(entity,"_model");
            Require(entity.Shown == Hunter.Weavel && packets.Length > 0,"Launcher returned to the wrong biped.");
            if (hd && biped.Skinning == CharacterSkinningMode.Weighted4)
                Require(CharacterModelRuntime.TryGetWeighted(scene,Hunter.Weavel,CharacterModelPart.Biped,model.Model,out var weighted)
                    && packets.Length == weighted.Segments.Count && packets.All(p=>p.WeightedSkinning && weighted.Segments.Any(s=>s.ListId == p.ListId)),"Preexisting Weavel biped launcher fell back.");
            else if (hd) Require(CharacterModelRuntime.TryGetRigid(scene,Hunter.Weavel,CharacterModelPart.Biped,model.Model,out var rigid)
                && packets.All(p=>rigid.Segments.Any(s=>s.ListId == p.ListId)),"Preexisting rigid Weavel biped launcher fell back.");
            else Require(packets.All(p=>!p.WeightedSkinning),"One-entry turret pack unexpectedly changed the native biped preview.");
            drawn++;
        }
        if (hd) Require(Sha(biped.ModelPath) == hash,"Preexisting biped changed during turret preview.");
        File.WriteAllText(Path.Combine(directory,label+".json"),JsonSerializer.Serialize(new {hunter="Weavel",part="biped",expectedHd=hd,
            testedModelSha256=hash,drawnFrames=drawn,fallbackFrames=0,scope="Turrets are not launcher assets; this verifies the preexisting biped preview before/after the turret scene."},Json));
        Require(ScreenCapture.SaveWindow(scene,Path.Combine(directory,label+".png")),"Biped launcher capture failed.");
        return hash;
    }
}
#endif
