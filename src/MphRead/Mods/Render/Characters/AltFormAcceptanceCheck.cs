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

/// <summary>Opt-in native alternate-form simulation and render acceptance for the six non-Samus hunters.</summary>
internal static class AltFormAcceptanceCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name,Private)!.GetValue(owner)!;
    private static void Set(object owner, string name, object value) => owner.GetType().GetField(name,Private)!.SetValue(owner,value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static RenderItem[] Items(Scene scene) => Field<List<RenderItem>>(scene,"_decalItems")
        .Concat(Field<List<RenderItem>>(scene,"_nonDecalItems")).Concat(Field<List<RenderItem>>(scene,"_translucentItems")).Distinct().ToArray();
    private static void Hold(Keybind key, bool press=false) { key.IsDown=true; key.IsPressed=press; }
    private static bool Finite(Matrix4 matrix)
    {
        for (int row=0; row<4; row++) for (int column=0; column<4; column++)
            if (!float.IsFinite(matrix[row,column])) return false;
        return true;
    }
    private static Matrix4 NativeRest(Model model, int index)
    {
        var node=model.Nodes[index];
        Matrix4 local=Matrix4.CreateScale(node.Scale)*Matrix4.CreateRotationX(node.Angle.X)
            *Matrix4.CreateRotationY(node.Angle.Y)*Matrix4.CreateRotationZ(node.Angle.Z)
            *Matrix4.CreateTranslation(node.Position/model.Scale);
        return node.ParentIndex < 0 ? local : local*NativeRest(model,node.ParentIndex);
    }
    private static Matrix4 PaletteMatrix(RenderItem packet, int joint)
    {
        int i=joint*16; var p=packet.MatrixStack;
        return new(p[i],p[i+1],p[i+2],p[i+3],p[i+4],p[i+5],p[i+6],p[i+7],
            p[i+8],p[i+9],p[i+10],p[i+11],p[i+12],p[i+13],p[i+14],p[i+15]);
    }

    internal static int Run(string room, string output, Hunter hunter, string sourceAudit,
        string sourceAuditSha256, string? sourceGlb=null)
    {
        string directory=Path.GetFullPath(output);
        Directory.CreateDirectory(directory);
        File.Delete(Path.Combine(directory,"acceptance.json"));
        File.Delete(Path.Combine(directory,"failure.txt"));
        bool oldHd=RenderOptions.CharacterModelReplacements, oldAdvanced=RenderOptions.AdvancedMaterials;
        bool oldBright=RenderOptions.BrightSkins, oldCosmetics=RenderOptions.ShowCustomCosmetics;
        bool oldForce=MapAudit.ForceEveryone, oldDetail=Features.MaxPlayerDetail, oldFps=RenderOptions.ShowFps;
        var oldStyle=RenderOptions.BrightSkinStyle;
        Scene? scene=null, preview=null;
        var frames=new List<object>(); var cases=new List<object>();
        int eligibleFrames=0, submittedFrames=0, fallbackFrames=0, hiddenFrames=0, nativeSupplementFrames=0,nativeSupplementPackets=0;
        int materialFrames=0, teamFrames=0, brightFrames=0, damageFrames=0, doubleDamageFrames=0, iceFrames=0;
        int abilityFrames=0, nativePoseFrames=0, bombFrames=0, bombEffectFrames=0, nativeParticleFrames=0;
        bool morph=false, unmorph=false, died=false, respawned=false, frozen=false, attack=false, bombs=false;
        int altEntryEdges=0, altExitEdges=0; float moveTravel=0, strafeTravel=0;
        float maximumNativeBindError=0, maximumLoadedWeightSumError=0, maximumSpecialPoseError=0;
        CharacterWeightedRenderModel? weighted=null; CharacterRigidRenderModel? rigid=null;
        CharacterWeightedModelData? weightedData=null; CharacterRigidModelData? rigidData=null;
        string testedHash="", expectedHash="", originalHash="";
        string[] nativeJointNames=Array.Empty<string>();
        var animations=new HashSet<int>(); var abilityAnimations=new HashSet<int>();
        var bombTypes=new HashSet<string>(); var nativeEffectIds=new HashSet<int>();
        try
        {
            Require(hunter is Hunter.Kanden or Hunter.Noxus or Hunter.Spire or Hunter.Sylux or Hunter.Trace or Hunter.Weavel,
                "Use -morphballacceptancecheck for Samus; this command accepts the other six hunters.");
            string auditPath=Path.GetFullPath(sourceAudit);
            Require(sourceAuditSha256.Length == 64 && Sha256(auditPath).Equals(sourceAuditSha256,StringComparison.OrdinalIgnoreCase),
                "Source conversion audit hash differs from the reviewed audit.");
            using var audit=JsonDocument.Parse(File.ReadAllBytes(auditPath));
            Require(audit.RootElement.GetProperty("hunter").GetString() == hunter.ToString()
                && audit.RootElement.GetProperty("pass").GetBoolean(), "Alternate-form source audit hunter/pass differs.");
            if (audit.RootElement.TryGetProperty("part",out var auditPart))
                Require(auditPart.GetString()?.Equals("alternateForm",StringComparison.OrdinalIgnoreCase) == true,
                    "Source audit describes a different character part.");
            bool mobile=CharacterModelPack.ForceMobileTierForCheck;
            expectedHash=audit.RootElement.GetProperty(mobile && audit.RootElement.TryGetProperty("shippingMobileGlbSha256",out _)
                ? "shippingMobileGlbSha256" : "shippingGlbSha256").GetString()!;
            Require(expectedHash.Length == 64, "Source audit needs an exact shipping GLB SHA-256.");
            if (sourceGlb != null)
            {
                originalHash=Sha256(sourceGlb);
                string recorded=audit.RootElement.GetProperty(audit.RootElement.TryGetProperty("sourceGlbSha256",out _)
                    ? "sourceGlbSha256" : "sourceSha256").GetString()!;
                Require(originalHash.Equals(recorded,StringComparison.OrdinalIgnoreCase), "Original Source GLB hash differs from its conversion audit.");
            }
            bool teamRecolors=audit.RootElement.TryGetProperty("teamRecolorsRequired",out var teamRequired) && teamRequired.GetBoolean();
            var pack=CharacterModelPack.LoadDefault(out var packIssue);
            Require(packIssue == null && pack.TryResolve(hunter,CharacterModelPart.AlternateForm,out var asset),
                "The installed pack does not contain the reviewed alternate form.");
            // Resolve again because Require cannot convey out-variable assignment to the compiler.
            Require(pack.TryResolve(hunter,CharacterModelPart.AlternateForm,out asset),"Alternate-form entry is missing.");
            string[] auditedSupplements=audit.RootElement.TryGetProperty("nativeSupplementMaterials",out var supplements)
                ? supplements.EnumerateArray().Select(x=>x.GetString()!).OrderBy(x=>x,StringComparer.Ordinal).ToArray() : Array.Empty<string>();
            Require(asset.NativeSupplementMaterials.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(auditedSupplements),
                "Installed native supplement materials differ from the hash-bound source audit.");
            string manifestPath=Path.Combine(CharacterModelPack.DefaultDirectory,"characters.json"),manifestHash=Sha256(manifestPath);
            testedHash=Sha256(asset.ModelPath);
            Require(testedHash.Equals(expectedHash,StringComparison.OrdinalIgnoreCase),"Installed alternate form differs from the reviewed GLB.");
            if (hunter == Hunter.Weavel)
                Require(!pack.TryResolve(hunter,CharacterModelPart.Halfturret,out _),
                    "This alt-only acceptance keeps Weavel's native turret; test a replacement turret with the separate turret harness.");
            if (asset.Skinning == CharacterSkinningMode.Weighted4) weightedData=CharacterWeightedModelLoader.Load(asset);
            else rigidData=CharacterRigidModelLoader.Load(asset);

            GameSettings.Apply(GameState.LoadSettings());
            Require(RenderOptions.CharacterModelReplacements,"HD character models must already be enabled.");
            DebugLog.Force();
            RenderOptions.BrightSkins=false; RenderOptions.AdvancedMaterials=true;
            RenderOptions.ShowCustomCosmetics=false; RenderOptions.ShowFps=false;
            Features.MaxPlayerDetail=true; MapAudit.ForceEveryone=true;
            var settings=DesktopGlContext.Settings(background:true);
            settings.ClientSize=new(800,600); settings.CurrentMonitor=Monitors.GetPrimaryMonitor().Handle;
            settings.StartVisible=true; settings.StartFocused=false; settings.Title=$"Project Prime {hunter} alternate-form acceptance";
            using var window=new NativeWindow(settings);
            using var graphics=new DesktopGraphicsSession(window);
            try
            {
                preview=new Scene(window.FramebufferSize,SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{});
                preview.SideScene=true; preview.OnLoad(); preview.OnResize();
                Preview(preview,window,pack,hunter,directory,"launcher-before");
                Scene.LauncherPreview=false; Scene.PreviewWanted=false;
                scene=new Scene(window.FramebufferSize,SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{});
                scene.Random.SetRng1(123456); scene.AddPlayer(hunter == Hunter.Kanden ? Hunter.Samus : Hunter.Kanden); scene.AddPlayer(hunter);
                scene.Players.Items[0].IsBot=true; scene.Players.Items[0].BotLevel=1;
                scene.Players.Items[1].IsBot=false;
                scene.AddRoom(room,GameMode.Battle,playerCount:2); scene.OnLoad(); scene.OnResize();
                scene.GameState.PointGoal=0; scene.GameState.MatchTime=-1; scene.GameState.SpawnProtection=false;
                scene.GameState.EnhancedHunters=false; scene.GameState.ShadowFreeze=false;
                var target=scene.Players.Items[1]; target.Controls.MouseAim=false;
                void Present()
                {
                    DesktopGraphicsSession.Resize(window); scene.OnDrawFrame(); Require(scene.OnRenderFrame(),"Render stopped.");
                    DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                }
                for (int i=0;i<300;i++) { Set(target,"_spawnInvulnTimer",(ushort)2); scene.OnSimulationFrame(); Present(); }
                Require(target.Health > 0 && target.Hunter == hunter,"Requested hunter did not spawn.");
                scene.SetFreeCamera(true);
                var native=Field<ModelInstance>(target,"_altModel");
                Require(CharacterModelPack.ValidateNativeRig(asset,native.Model,out var rigIssue),rigIssue ?? "Native alternate-form rig rejected.");
                var supplementMeshes=native.Model.Nodes.SelectMany(node=>Enumerable.Range(node.MeshId/2,node.MeshCount)
                    .Select(index=>(Node:node,Mesh:native.Model.Meshes[index])))
                    .Where(x=>asset.NativeSupplementMaterials.Contains(native.Model.Materials[x.Mesh.MaterialId].Name)).ToArray();
                var supplementLists=supplementMeshes.Select(x=>x.Mesh.ListId).ToHashSet();
                var nativeLists=native.Model.Meshes.Where(m=>m.ListId != 0 && !supplementLists.Contains(m.ListId)).Select(m=>m.ListId).ToHashSet();
                var ice=Field<ModelInstance>(target,"_altIceModel");
                var iceLists=ice.Model.Meshes.Where(m=>m.ListId != 0).Select(m=>m.ListId).ToHashSet();
                if (weightedData != null)
                {
                    nativeJointNames=weightedData.Joints.Select(j=>j.TargetNode).ToArray();
                    Require(nativeJointNames.Distinct().Count() == nativeJointNames.Length
                        && nativeJointNames.All(name=>native.Model.Nodes.Any(n=>n.Name == name)),"Alternate skin must use unique native joints only.");
                    foreach (var joint in weightedData.Joints)
                    {
                        int index=Array.FindIndex(native.Model.Nodes.ToArray(),n=>n.Name == joint.TargetNode);
                        Matrix4 identity=joint.InverseBind*NativeRest(native.Model,index);
                        for (int row=0;row<4;row++) for (int column=0;column<4;column++)
                            maximumNativeBindError=MathF.Max(maximumNativeBindError,MathF.Abs(identity[row,column]-(row == column ? 1 : 0)));
                    }
                    Require(maximumNativeBindError < .0001f,"Alternate inverse binds differ from authoritative native rest transforms.");
                    foreach (var vertex in weightedData.Primitives.SelectMany(p=>p.Vertices))
                    {
                        var w=vertex.Weights; var j=CharacterWeightedModelLoader.UnpackJoints(vertex.PackedJoints);
                        Require(float.IsFinite(w.X) && float.IsFinite(w.Y) && float.IsFinite(w.Z) && float.IsFinite(w.W)
                            && w.X >= 0 && w.Y >= 0 && w.Z >= 0 && w.W >= 0,"Invalid loaded alternate weights.");
                        maximumLoadedWeightSumError=MathF.Max(maximumLoadedWeightSumError,MathF.Abs(w.X+w.Y+w.Z+w.W-1));
                        Require(new[] {j.J0,j.J1,j.J2,j.J3}.All(index=>index >= 0 && index < nativeJointNames.Length),"Alternate joint index exceeds native palette.");
                    }
                    Require(maximumLoadedWeightSumError < .00001f,"Loaded alternate weights are not normalized.");
                }
                else
                {
                    nativeJointNames=rigidData!.Primitives.Select(p=>p.TargetNode).Distinct().ToArray();
                    Require(nativeJointNames.All(name=>native.Model.Nodes.Any(n=>n.Name == name)),"Rigid alternate contains a non-native target node.");
                }

                int attackCount=hunter == Hunter.Noxus ? Math.Max(360,target.Values.AltAttackStartup*2+90) : 360;
                var stages=new (string Name,int Count)[] { ("biped-idle",60), ("morph",120), ("alt-idle",60),
                    ("materials-on",60), ("materials-off",60), ("materials-restored",60),
                    ("move",120), ("strafe",120), ("aim-turn",120), ("ability",attackCount),
                    ("ability-settle",180), ("damage",60), ("freeze",90), ("thaw",60),
                    ("bright",90), ("team-orange",60), ("team-green",60), ("double-damage",90),
                    ("neutral",60), ("unmorph",120), ("biped-return",60), ("reenter",120),
                    ("second-move",120), ("alt-death-respawn",420), ("respawn",60),
                    ("third-enter",120), ("third-alt-idle",60), ("final-unmorph",120), ("final-biped",60) };
                var stageAltFrames=new Dictionary<string,int>();
                foreach (var stage in stages)
                {
                    int before=submittedFrames, beforeEligible=eligibleFrames, beforeFallback=fallbackFrames;
                    int beforeAbility=abilityFrames, beforeIce=iceFrames, beforeBomb=bombFrames;
                    long beforeParticles=scene.ModEffectParticles;
                    if (stage.Name is "alt-idle" or "reenter" or "third-enter" or "neutral")
                    {
                        scene.GameState.Teams=false; target.Recolor=0; RenderOptions.BrightSkins=false;
                        Set(target,"_doubleDmgTimer",(ushort)0); target.ModSetFrozen(false);
                    }
                    if (stage.Name is "materials-on" or "materials-restored") RenderOptions.AdvancedMaterials=true;
                    if (stage.Name == "materials-off") RenderOptions.AdvancedMaterials=false;
                    if (stage.Name == "damage") target.TakeDamage(10,DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln,null,scene.Players.Items[0]);
                    if (stage.Name == "freeze") target.ModSetFrozen(true);
                    if (stage.Name == "thaw") target.ModSetFrozen(false);
                    if (stage.Name == "bright") { RenderOptions.BrightSkins=true; RenderOptions.BrightSkinStyle=PlayerSkinStyle.Textured; }
                    if (stage.Name is "team-orange" or "team-green")
                    { scene.GameState.Teams=true; target.TeamIndex=stage.Name == "team-orange" ? 0 : 1; TeamVisuals.Apply(target); }
                    if (stage.Name == "double-damage") Set(target,"_doubleDmgTimer",(ushort)240);
                    if (stage.Name == "alt-death-respawn")
                    {
                        Require(target.IsAltForm,"Alternate death case must begin in the replaced form.");
                        target.TakeDamage(10000,DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln,null,scene.Players.Items[0]);
                    }
                    for (int age=0;age<stage.Item2;age++)
                    {
                        target.Controls.ClearAll(); target.Controls.MouseAim=false;
                        object input=typeof(PlayerEntity).GetProperty("Input",Private)!.GetValue(target)!;
                        input.GetType().GetProperty("HasInput")!.SetValue(input,true);
                        if (stage.Name != "alt-death-respawn") target.Health=999;
                        if ((stage.Name is "morph" or "unmorph" or "reenter" or "third-enter" or "final-unmorph") && age == 0)
                            Hold(target.Controls.Morph,true);
                        bool forward=age < stage.Item2/2;
                        if (stage.Name is "move" or "second-move") Move(target,forward ? 0 : 1);
                        if (stage.Name == "strafe") Move(target,forward ? 2 : 3);
                        if (stage.Name == "aim-turn" && target.Values.AltFormStrafe != 0)
                            target.ModSetAim(new Vector3(MathF.Sin(age*.1f),MathF.Sin(age*.08f)*.5f,MathF.Cos(age*.1f)).Normalized());
                        if (stage.Name == "ability")
                        {
                            if (hunter == Hunter.Noxus) { if (age < stage.Item2-60) Hold(target.Controls.AltAttack,age == 0); }
                            else if (age%90 == 0) Hold(target.Controls.AltAttack,true);
                            if (hunter is Hunter.Kanden or Hunter.Sylux) Move(target,age/45%4);
                        }
                        if (stage.Name is "bright" or "team-orange" or "team-green")
                        { Move(target,forward ? 2 : 3); Set(target,"_timeSinceDamage",(ushort)255); }
                        scene.Players.Items[0].Position=target.Position+new Vector3(8,0,8);
                        Vector3 previous=target.Position; bool previousAlt=target.IsAltForm;
                        scene.OnSimulationFrame();
                        if (!previousAlt && target.IsAltForm) altEntryEdges++;
                        if (previousAlt && !target.IsAltForm) altExitEdges++;
                        morph |= target.IsMorphing; unmorph |= target.IsUnmorphing; frozen |= target.ModFrozen;
                        died |= stage.Name == "alt-death-respawn" && target.Health == 0;
                        respawned |= died && target.Health > 0 && !target.IsAltForm;
                        if (stage.Name is "move" or "second-move") moveTravel+=(target.Position-previous).Length;
                        if (stage.Name == "strafe") strafeTravel+=(target.Position-previous).Length;
                        if (stage.Name is "bright" or "team-orange" or "team-green") Set(target,"_timeSinceDamage",(ushort)255);
                        Follow(scene,target);
                        DesktopGraphicsSession.Resize(window); scene.OnDrawFrame();
                        var all=Items(scene);
                        int replacementPackets=0, expectedReplacementPackets=0, nativeFallbackPackets=0,supplementPackets=0,expectedSupplementPackets=0;
                        bool eligible=target.Health > 0 && target.IsAltForm && target.CurAlpha > 0
                            && target.Flags2.TestFlag(PlayerFlags2.DrawnThirdPerson) && !target.Flags2.TestFlag(PlayerFlags2.HideModel);
                        if (eligible)
                        {
                            eligibleFrames++;
                            if (asset.Skinning == CharacterSkinningMode.Weighted4)
                                Require(CharacterModelRuntime.TryGetWeighted(scene,hunter,CharacterModelPart.AlternateForm,native.Model,out weighted),"Weighted alternate did not resolve.");
                            else Require(CharacterModelRuntime.TryGetRigid(scene,hunter,CharacterModelPart.AlternateForm,native.Model,out rigid),"Rigid alternate did not resolve.");
                            var sourceLists=weighted != null ? weighted.Segments.Select(s=>s.ListId).ToHashSet() : rigid!.Segments.Select(s=>s.ListId).ToHashSet();
                            var sourcePackets=all.Where(p=>p.Type == RenderItemType.Mesh && sourceLists.Contains(p.ListId)).ToArray();
                            int expected=weighted?.Segments.Count ?? rigid!.Segments.Count(s=>native.Model.Nodes[s.NativeNodeIndex].Enabled
                                && native.Model.NodeParentsEnabled(native.Model.Nodes[s.NativeNodeIndex]));
                            int nativePackets=all.Count(p=>p.Type == RenderItemType.Mesh && nativeLists.Contains(p.ListId));
                            replacementPackets=sourcePackets.Length; expectedReplacementPackets=expected; nativeFallbackPackets=nativePackets;
                            var expectedSupplements=supplementMeshes.Where(x=>x.Mesh.Visible && x.Node.Enabled
                                && native.Model.NodeParentsEnabled(x.Node)).ToArray();
                            var extraPackets=all.Where(p=>p.Type == RenderItemType.Mesh && supplementLists.Contains(p.ListId)).ToArray();
                            supplementPackets=extraPackets.Length;expectedSupplementPackets=expectedSupplements.Length;
                            Require(supplementPackets == expectedSupplementPackets,"Configured native alternate overlay disappeared or was duplicated.");
                            foreach (var extra in extraPackets)
                            {
                                var original=expectedSupplements.Single(x=>x.Mesh.ListId == extra.ListId);
                                InspectNativeSupplement(scene,target,native,original.Node,original.Mesh,extra);
                            }
                            if (supplementPackets > 0) {nativeSupplementFrames++;nativeSupplementPackets+=supplementPackets;}
                            bool fallback=sourcePackets.Length != expected || nativePackets != 0;
                            if (fallback) fallbackFrames++;
                            Require(!fallback,$"{stage.Name}/{age}: alternate packets {sourcePackets.Length}/{expected}, native fallback packets={nativePackets}.");
                            Require(expected > 0,"No native-enabled replacement surface exists in this eligible alternate frame.");
                            foreach (var packet in sourcePackets)
                            {
                                if (weighted != null)
                                {
                                    Require(packet.WeightedSkinning && packet.MatrixStackCount == weighted.Joints.Count,"Incomplete alternate native joint palette.");
                                    for (int joint=0;joint<weighted.Joints.Count;joint++)
                                    {
                                        var j=weighted.Joints[joint]; Matrix4 expectedMatrix=j.InverseBind*native.Model.Nodes[j.NativeNodeIndex].Animation;
                                        Require(Finite(expectedMatrix) && PaletteMatrix(packet,joint) == expectedMatrix,"Stale/non-finite alternate Weighted4 palette.");
                                    }
                                    var segment=weighted.Segments.Single(s=>s.ListId == packet.ListId);
                                    int index=weighted.Segments.ToList().IndexOf(segment);
                                    InspectSurface(scene,target,packet,native.Model.Materials[segment.NativeMaterialIndex],segment.GetAlbedo(scene,target.Recolor),
                                        segment.AlbedoBinding,segment.MaterialMaps,segment.WrapS,segment.WrapT,segment.DoubleSided,segment.Transparent,
                                        weightedData!.Primitives[index].Albedo,weightedData.Primitives[index].MaterialMaps,teamRecolors,stage.Name);
                                }
                                else
                                {
                                    var segment=rigid!.Segments.Single(s=>s.ListId == packet.ListId); var node=native.Model.Nodes[segment.NativeNodeIndex];
                                    Require(!packet.WeightedSkinning && packet.MatrixStackCount == 0 && Finite(packet.Transform)
                                        && packet.Transform == node.Animation && packet.BillboardMode == node.BillboardMode,"Stale native rigid alternate transform/billboard.");
                                    int index=rigid.Segments.ToList().IndexOf(segment);
                                    InspectSurface(scene,target,packet,native.Model.Materials[segment.NativeMaterialIndex],segment.GetAlbedo(scene,target.Recolor),
                                        segment.AlbedoBinding,segment.MaterialMaps,segment.WrapS,segment.WrapT,segment.DoubleSided,segment.Transparent,
                                        rigidData!.Primitives[index].Albedo,rigidData.Primitives[index].MaterialMaps,teamRecolors,stage.Name);
                                }
                            }
                            if (teamRecolors && (stage.Name is "team-orange" or "team-green"))
                            {
                                var albedos=weightedData != null ? weightedData.Primitives.Select(p=>p.Albedo) : rigidData!.Primitives.Select(p=>p.Albedo);
                                Require(albedos.Any(a=>a?.Recolors?.ContainsKey(target.Recolor) == true),
                                    "The reviewed alternate form requires a native team variant, but none is authored.");
                            }
                            submittedFrames++; materialFrames++;
                            if (stage.Name is "team-orange" or "team-green") teamFrames++;
                            if (stage.Name == "bright" && sourcePackets.Any(p=>p.OverrideColor.HasValue && p.TexturedPlayerSkin)) brightFrames++;
                            if (stage.Name == "damage" && sourcePackets.Any(p=>p.PaletteOverride == Metadata.RedPalette)) damageFrames++;
                            if (stage.Name == "double-damage" && target.DoubleDamage) doubleDamageFrames++;
                            if (Field<ushort>(target,"_frozenGfxTimer") > 0)
                            {
                                Require(all.Any(p=>p.Type == RenderItemType.Mesh && iceLists.Contains(p.ListId)),"Native alternate freeze overlay was lost.");
                                iceFrames++;
                            }
                            animations.Add(native.AnimInfo.Index[0]);
                            if (stage.Name == "ability") abilityAnimations.Add(native.AnimInfo.Index[0]);
                            if (target.Flags2.TestFlag(PlayerFlags2.AltAttack)) { attack=true; abilityFrames++; }
                            if (hunter == Hunter.Kanden)
                            {
                                var poses=Field<Matrix4[]>(target,"_kandenSegMtx");
                                Require(poses.Length <= native.Model.Nodes.Count,"Native Stinglarva segment array differs.");
                                for (int index=0;index<poses.Length;index++)
                                    Require(native.Model.Nodes[index].Animation == poses[index],"Stinglarva replacement lost a native segment transform.");
                                nativePoseFrames++;
                            }
                            if (hunter == Hunter.Spire && target.Flags2.TestFlag(PlayerFlags2.AltAttack))
                            {
                                var pose=target.ModSpireAltCollisionPose();
                                float error=MathF.Max((native.Model.GetNodeByName("L_Rock01")!.Animation.Row3.Xyz-pose.Left).Length,
                                    (native.Model.GetNodeByName("R_Rock01")!.Animation.Row3.Xyz-pose.Right).Length);
                                maximumSpecialPoseError=MathF.Max(maximumSpecialPoseError,error);
                                Require(float.IsFinite(error) && error < .0001f,"Dialanche visuals detached from native attacking rock collision positions.");
                                nativePoseFrames++;
                            }
                        }
                        else hiddenFrames++;
                        if (!target.IsAltForm && (weighted != null || rigid != null))
                        {
                            var knownLists=weighted != null ? weighted.Segments.Select(s=>s.ListId).ToHashSet() : rigid!.Segments.Select(s=>s.ListId).ToHashSet();
                            Require(!all.Any(p=>p.Type == RenderItemType.Mesh && knownLists.Contains(p.ListId)),
                                $"{stage.Name}/{age}: stale alternate geometry remained after the native form switch.");
                            Require(!all.Any(p=>p.Type == RenderItemType.Mesh && supplementLists.Contains(p.ListId)),
                                "Native alternate overlay remained after the form switch.");
                        }
                        foreach (var bomb in scene.Entities.OfType<BombEntity>().Where(b=>b.Owner == target))
                        {
                            bombs=true; bombFrames++; bombTypes.Add(bomb.BombType.ToString());
                            if (bomb.Effect != null && !bomb.Effect.IsFinished)
                            { bombEffectFrames++; nativeEffectIds.Add(bomb.Effect.EffectId); }
                        }
                        foreach (var element in Field<List<EffectElementEntry>>(scene,"_activeElements"))
                        {
                            Require(Finite(element.OwnTransform) && Finite(element.Transform),"Non-finite native effect transform during alternate acceptance.");
                            nativeEffectIds.Add(element.EffectId);
                        }
                        if (all.Any(p=>p.Type == RenderItemType.Particle)) nativeParticleFrames++;
                        Require(scene.OnRenderFrame(),"Alternate render stopped.");
                        if (age is 0 or 15 or 40 || age == stage.Item2-1)
                            Require(ScreenCapture.Save(scene,Path.Combine(directory,$"{stage.Name}-{age:D3}.png")),"Alternate capture failed.");
                        frames.Add(new {stage=stage.Name,age,frame=scene.FrameCount,health=target.Health,alt=target.IsAltForm,
                            morph=target.IsMorphing,unmorph=target.IsUnmorphing,frozen=target.ModFrozen,doubleDamage=target.DoubleDamage,
                            nativeAnimation=native.AnimInfo.Index[0],nativeAnimationFrame=native.AnimInfo.Frame[0],
                            altAttack=target.Flags2.TestFlag(PlayerFlags2.AltAttack),eligible,recolor=target.Recolor,
                            replacementPackets,expectedReplacementPackets,nativeFallbackPackets,supplementPackets,expectedSupplementPackets,
                            x=target.Position.X,y=target.Position.Y,z=target.Position.Z});
                        DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                    }
                    stageAltFrames.Add(stage.Name,submittedFrames-before);
                    cases.Add(new {stage=stage.Name,frames=stage.Item2,eligibleFrames=eligibleFrames-beforeEligible,
                        submittedFrames=submittedFrames-before,fallbackFrames=fallbackFrames-beforeFallback,
                        abilityFrames=abilityFrames-beforeAbility,iceFrames=iceFrames-beforeIce,bombFrames=bombFrames-beforeBomb,
                        nativeParticlesCreated=scene.ModEffectParticles-beforeParticles});
                    Console.WriteLine($"ALT ACCEPTANCE {hunter} {stage.Name}: {submittedFrames-before} replacement frames, {fallbackFrames-beforeFallback} fallbacks");
                }
                Require(submittedFrames > 1200 && fallbackFrames == 0,"Alternate rendering coverage/fallback check incomplete.");
                if (asset.NativeSupplementMaterials.Count > 0) Require(nativeSupplementFrames > 0,"Configured native alternate overlay was never submitted.");
                Require(morph && unmorph && altEntryEdges >= 3 && altExitEdges >= 3 && !target.IsAltForm,
                    "Three native form entries, two unmorphs and alternate death/respawn were not observed.");
                Require(moveTravel > .5f && strafeTravel > .5f,"Native alternate movement/strafe input did not move the subject.");
                Require(frozen && iceFrames > 0 && damageFrames > 0 && brightFrames > 0 && teamFrames > 0 && doubleDamageFrames > 0,
                    "Alternate damage/freeze/bright/team/double-damage presentation coverage incomplete.");
                Require(died && respawned && target.Health > 0,"Alternate death/native biped respawn incomplete.");
                foreach (string name in new[] {"alt-idle","materials-on","materials-off","materials-restored","move","strafe","aim-turn",
                    "ability","damage","freeze","thaw","bright","team-orange","team-green","double-damage","second-move","third-alt-idle"})
                    Require(stageAltFrames[name] > 0,$"{name}: no eligible replacement frames.");
                if (hunter is Hunter.Kanden or Hunter.Sylux)
                    Require(bombs,"The hunter's native alternate bomb ability was not observed.");
                else Require(attack && abilityFrames > 0,"The hunter's native alternate attack was not observed.");
                if (hunter == Hunter.Kanden) Require(nativePoseFrames > 0 && animations.Contains((int)KandenAltAnim.TailOut)
                    && animations.Contains((int)KandenAltAnim.TailIn),"Stinglarva tail-out/tail-in cycle not observed.");
                if (hunter == Hunter.Sylux) Require(bombEffectFrames > 0,"Native Lockjaw bomb effects were not observed.");
                if (hunter == Hunter.Spire) Require(nativePoseFrames > 0,"Native Dialanche attacking rock pose not observed.");
                if (hunter is Hunter.Trace or Hunter.Weavel) Require(abilityAnimations.Contains(1),"Native alternate lunge animation was not observed.");
                Require(Sha256(asset.ModelPath) == testedHash && Sha256(auditPath).Equals(sourceAuditSha256,StringComparison.OrdinalIgnoreCase),
                    "Tested alternate model or source audit changed during acceptance.");
                Require(Sha256(manifestPath) == manifestHash,"Installed alternate manifest changed during acceptance.");
                if (sourceGlb != null) Require(Sha256(sourceGlb) == originalHash,"Original Source GLB changed during acceptance.");
                scene.DoCleanup(); scene.UnloadGl(); scene=null;
                Preview(preview,window,pack,hunter,directory,"launcher-after");
                File.WriteAllText(Path.Combine(directory,"acceptance.json"),JsonSerializer.Serialize(new {
                    pass=true,hunter=hunter.ToString(),part="alternateForm",room,backend=GraphicsBackendPolicy.Resolved.ToString(),
                    skinning=asset.Skinning.ToString(),testedModelPath=asset.ModelPath,testedModelSha256=testedHash,
                    sourceAuditPath=auditPath,sourceAuditSha256=sourceAuditSha256.ToLowerInvariant(),sourceGlb,sourceGlbSha256=originalHash,
                    mobileTextureTier=mobile,manifestSha256=manifestHash,eligibleFrames,submittedFrames,fallbackFrames,hiddenFrames,
                    nativeSupplementMaterials=auditedSupplements,nativeSupplementFrames,nativeSupplementPackets,
                    nativeJointNames,maximumNativeBindError,maximumLoadedWeightSumError,maximumSpecialPoseError,
                    vertices=weightedData?.VertexCount ?? rigidData!.VertexCount,triangles=(weightedData?.IndexCount ?? rigidData!.IndexCount)/3,
                    morph,unmorph,altEntryEdges,altExitEdges,moveTravel,strafeTravel,died,respawned,frozen,
                    materialFrames,teamFrames,brightFrames,damageFrames,doubleDamageFrames,iceFrames,
                    attack,abilityFrames,nativePoseFrames,bombs,bombFrames,bombEffectFrames,bombTypes,
                    nativeParticleFrames,nativeEffectIds,animations,abilityAnimations,cases,
                    scope="Native scene simulation and render with a bot opponent and scene-owned hunter controls. Only alternate-form LOD0 is accepted. Weighted4 checks normalized loaded weights, exact native inverse binds and every palette; RigidNodes checks each enabled native node transform and billboard. Explicit hash-bound native supplement mesh materials alone are allowed beside HD geometry and their original node poses, palette, animated material/UV/binding/alpha/culling/status are checked; other native alternate meshes remain prohibited. Raw authoring weights and Source-fit geometry fidelity remain exporter/offline-audit responsibilities. Native attack inputs vary by hunter; bombs are required only for Kanden/Sylux. Kanden segment matrices and Spire attacking rock collision positions are checked explicitly. Native freeze overlay, bombs/trails, particles and unrelated effects remain in the engine. Damage/freeze/double damage are injected. Weavel's native halfturret is retained and excluded from replacement acceptance. Preexisting biped launcher previews are checked separately. Visual clipping and Source resemblance require capture review; exhaustive ability balance/physics and Android display performance are not established."
                },Json));
                File.WriteAllText(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(frames,Json));
                Console.WriteLine($"ALT ACCEPTANCE PASS {hunter} {directory}");
                return 0;
            }
            finally { scene?.DoCleanup(); scene?.UnloadGl(); preview?.UnloadGl(); }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(directory,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(frames,Json));
            Console.Error.WriteLine("ALT ACCEPTANCE FAIL "+error); return 1;
        }
        finally
        {
            RenderOptions.CharacterModelReplacements=oldHd; RenderOptions.AdvancedMaterials=oldAdvanced;
            RenderOptions.BrightSkins=oldBright; RenderOptions.BrightSkinStyle=oldStyle;
            RenderOptions.ShowCustomCosmetics=oldCosmetics; RenderOptions.ShowFps=oldFps;
            Features.MaxPlayerDetail=oldDetail; MapAudit.ForceEveryone=oldForce;
        }
    }

    private static void Move(PlayerEntity target, int direction)
    {
        if (target.Values.AltFormStrafe != 0)
            Hold(direction switch {0=>target.Controls.MoveUp,1=>target.Controls.MoveDown,2=>target.Controls.MoveLeft,_=>target.Controls.MoveRight});
        else Hold(direction switch {0=>target.Controls.RollUp,1=>target.Controls.RollDown,2=>target.Controls.RolltLeft,_=>target.Controls.RollRight});
    }

    private static void InspectSurface(Scene scene, PlayerEntity target, RenderItem packet, Material material,
        int? albedo, int? baseAlbedo, MaterialMapBindings maps, RepeatMode wrapS, RepeatMode wrapT,
        bool doubleSided, bool transparent, CharacterEmbeddedAlbedo? authoredAlbedo, CharacterEmbeddedMaterialMaps? authoredMaps,
        bool requireTeamRecolors, string stage)
    {
        bool flash=Field<ushort>(target,"_timeSinceDamage") < target.Values.DamageFlashTime*2;
        bool buff=target.DoubleDamage && material.Lighting > 0;
        int? expected=buff ? target.DoubleDmgBindingId : albedo;
        Require(expected.HasValue && packet.HasTexture && packet.TextureBindingId == expected,"Alternate albedo/status texture mismatch.");
        Require(packet.PaletteOverride == (flash ? Metadata.RedPalette : (Vector4?)null),"Alternate native damage palette mismatch.");
        Require(packet.Diffuse == material.CurrentDiffuse && packet.Ambient == material.CurrentAmbient
            && packet.Specular == material.CurrentSpecular,"Alternate native material animation mismatch.");
        Require(MathF.Abs(packet.Alpha-material.CurrentAlpha*target.CurAlpha) < .00001f,
            "Alternate native material/player alpha mismatch.");
        if (doubleSided) Require(packet.CullingMode == CullingMode.Neither,"Alternate Source double-sided surface was culled.");
        bool source=packet.TextureBindingId == albedo;
        if (source)
        {
            Require(packet.TexgenMode == TexgenMode.Texcoord && packet.TexcoordMatrix == Matrix4.Identity
                && packet.XRepeat == wrapS && packet.YRepeat == wrapT,"Alternate authored UV/sampler state mismatch.");
            if (transparent) Require(packet.RenderMode == RenderMode.Translucent,"Alternate Source alpha surface was opaque.");
        }
        if (RenderOptions.AdvancedMaterials)
            Require((maps.Normal != 0) == (authoredMaps?.Normal != null)
                && (maps.Specular != 0) == (authoredMaps?.MetallicRoughness != null)
                && (maps.Emissive != 0) == (authoredMaps?.Emissive != null),"Alternate authored companion map upload incomplete.");
        Require(packet.CosmeticMaterial.NormalBinding == (source && RenderOptions.AdvancedMaterials ? maps.Normal : 0)
            && packet.CosmeticMaterial.SpecularBinding == (source && RenderOptions.AdvancedMaterials ? maps.Specular : 0)
            && packet.CosmeticMaterial.EmissiveBinding == (source && RenderOptions.AdvancedMaterials ? maps.Emissive : 0),
            "Alternate material toggle/status companion map mismatch.");
        if (buff) Require(packet.TexgenMode == TexgenMode.Normal && packet.Emission == Metadata.EmissionGray,"Alternate native double-damage effect mismatch.");
        else if (target.Team is Team.Orange or Team.Green)
            Require(packet.Emission == (target.Team == Team.Orange ? Metadata.EmissionOrange : Metadata.EmissionGreen),"Alternate native team emission mismatch.");
        Vector4? color=flash ? null : BrightSkins.ForMaterial(BrightSkins.GetColor(target),material.TextureId != -1,packet.Alpha,scene.ShowTextures);
        Require(packet.OverrideColor == color,"Alternate bright/team/status surface color mismatch.");
        if (color.HasValue) Require(packet.TexturedPlayerSkin && packet.PaletteOverride == null,"Alternate textured bright/team presentation lost.");
        if (requireTeamRecolors && (stage is "team-orange" or "team-green") && authoredAlbedo?.Recolors?.ContainsKey(target.Recolor) == true)
            Require(packet.TextureBindingId == albedo && albedo != baseAlbedo,"Alternate native team texture variant was not submitted.");
    }

    private static void InspectNativeSupplement(Scene scene,PlayerEntity target,ModelInstance native,Node node,Mesh mesh,RenderItem packet)
    {
        Material material=native.Model.Materials[mesh.MaterialId];float scale=Field<float>(target,"_drawScale");
        Matrix4 expected=node.Animation;
        for (int row=0;row<3;row++) for (int column=0;column<3;column++) expected[row,column]*=scale;
        Require(!packet.WeightedSkinning && packet.Transform == expected && Finite(expected)
            && packet.BillboardMode == node.BillboardMode && packet.MatrixStackCount == native.Model.NodeMatrixIds.Count,
            "Native supplement lost its node transform/billboard/palette count.");
        for (int index=0;index<native.Model.MatrixStackValues.Count;index++)
        {
            int element=index%16;float factor=element < 12 && element%4 != 3 ? scale : 1;
            Require(packet.MatrixStack[index] == native.Model.MatrixStackValues[index]*factor && float.IsFinite(packet.MatrixStack[index]),
                "Native supplement multi-joint palette changed.");
        }
        int? binding=(int?)typeof(PlayerEntity).GetMethod("GetBindingOverride",Private)!.Invoke(target,new object[] {native,material,mesh.MaterialId});
        Matrix4 coordinates=(Matrix4)typeof(PlayerEntity).GetMethod("GetTexcoordMatrix",Private)!.Invoke(target,new object[] {native,material,mesh.MaterialId,node,-1})!;
        Vector3 emission=(Vector3)typeof(PlayerEntity).GetMethod("GetEmission",Private)!.Invoke(target,new object[] {native,material,mesh.MaterialId})!;
        Require(packet.HasTexture == (binding.HasValue || material.TextureId != -1)
            && (!packet.HasTexture || packet.TextureBindingId == (binding ?? material.TextureBindingId))
            && packet.TexcoordMatrix == coordinates && packet.TexgenMode == (binding.HasValue ? TexgenMode.Normal : material.TexgenMode)
            && packet.XRepeat == (binding.HasValue ? RepeatMode.Mirror : material.XRepeat)
            && packet.YRepeat == (binding.HasValue ? RepeatMode.Mirror : material.YRepeat),
            "Native supplement binding/animated coordinates changed.");
        bool flash=Field<ushort>(target,"_timeSinceDamage") < target.Values.DamageFlashTime*2;
        Vector4? color=flash ? null : BrightSkins.ForMaterial(BrightSkins.GetColor(target),material.TextureId != -1,packet.Alpha,scene.ShowTextures);
        Require(packet.CullingMode == material.Culling && packet.RenderMode == material.RenderMode && packet.PolygonMode == material.PolygonMode
            && MathF.Abs(packet.Alpha-material.CurrentAlpha*target.CurAlpha) < .00001f
            && packet.Diffuse == material.CurrentDiffuse && packet.Ambient == material.CurrentAmbient && packet.Specular == material.CurrentSpecular
            && packet.Lighting == (material.Lighting != 0) && packet.Emission == emission
            && packet.OverrideColor == color && packet.PaletteOverride == (flash ? Metadata.RedPalette : (Vector4?)null)
            && packet.PlayerOutlineColor == (flash ? null : BrightSkins.GetOutlineColor(target)),
            "Native supplement alpha/culling/material/owner status changed.");
        Require(packet.CosmeticMaterial == default && packet.Cosmetics == target.CosmeticMaterial(false),
            "Native supplement inherited an authored HD companion/cosmetic state.");
    }

    private static void Follow(Scene scene, PlayerEntity target)
    {
        Vector3 center=target.Position+new Vector3(0,.5f,0), camera=center+new Vector3(2,1,3);
        for (int step=0;step<24;step++)
        {
            float angle=.6f+step*MathF.PI/12;
            Vector3 candidate=center+new Vector3(3.5f*MathF.Cos(angle),.7f,3.5f*MathF.Sin(angle));
            bool clear=true;
            foreach (float height in new[] {-.4f,0,.6f})
            {
                CollisionResult hit=default;
                if (CollisionDetection.CheckBetweenPoints(candidate,center+new Vector3(0,height,0),TestFlags.None,scene,ref hit)) { clear=false; break; }
            }
            if (clear) { camera=candidate; break; }
        }
        Set(scene,"_cameraPosition",camera); Set(scene,"_cameraFacing",(center-camera).Normalized());
        Set(scene,"_cameraUp",Vector3.UnitY); Set(scene,"_cameraFov",MathHelper.DegreesToRadians(55));
    }

    private static void Preview(Scene scene, NativeWindow window, CharacterModelPack pack, Hunter hunter, string directory, string label)
    {
        Scene.LauncherPreview=true; Scene.LauncherHunter=hunter; Scene.LauncherSuit=0; Scene.PreviewWanted=true;
        Scene.PreviewLeft=.1f; Scene.PreviewTop=.1f; Scene.PreviewRight=.9f; Scene.PreviewBottom=.9f;
        bool expectedHd=pack.TryResolve(hunter,CharacterModelPart.Biped,out var biped);
        int drawn=0; var frames=new List<object>();
        for (int frame=0;frame<60;frame++)
        {
            scene.ModStepPreview(); Require(scene.ModDrawPreviewAlone(window.FramebufferSize),"Launcher return preview failed.");
            var packets=Field<List<RenderItem>>(scene,"_previewItems").Where(p=>p.Type == RenderItemType.Mesh).ToArray();
            var entity=Field<HunterPreviewEntity>(scene,"_preview"); var model=Field<ModelInstance>(entity,"_model");
            Require(entity.Shown == hunter && packets.Length > 0,"Launcher preview selected the wrong hunter.");
            if (expectedHd)
            {
                if (biped.Skinning == CharacterSkinningMode.Weighted4)
                    Require(CharacterModelRuntime.TryGetWeighted(scene,hunter,CharacterModelPart.Biped,model.Model,out var weighted)
                        && packets.Length == weighted.Segments.Count && packets.All(p=>p.WeightedSkinning && weighted.Segments.Any(s=>s.ListId == p.ListId)),
                        "Preexisting weighted biped launcher preview fell back after alternate-form acceptance.");
                else Require(CharacterModelRuntime.TryGetRigid(scene,hunter,CharacterModelPart.Biped,model.Model,out var rigid)
                    && packets.All(p=>!p.WeightedSkinning && rigid.Segments.Any(s=>s.ListId == p.ListId)),
                    "Preexisting rigid biped launcher preview fell back after alternate-form acceptance.");
            }
            else Require(packets.All(p=>!p.WeightedSkinning),"One-entry alt pack unexpectedly changed the native biped preview.");
            drawn++; frames.Add(new {frame,expectedHd,meshPackets=packets.Length});
        }
        File.WriteAllText(Path.Combine(directory,label+".json"),JsonSerializer.Serialize(new {hunter=hunter.ToString(),expectedHd,drawnFrames=drawn,fallbackFrames=0,frames},Json));
        Require(ScreenCapture.SaveWindow(scene,Path.Combine(directory,label+".png")),"Launcher preview capture failed.");
    }
}
#endif
