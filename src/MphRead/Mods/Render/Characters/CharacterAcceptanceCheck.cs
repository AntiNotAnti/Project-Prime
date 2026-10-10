#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Render.Characters;

/// <summary>Real simulation/render acceptance with scene-owned input, unaffected by desktop focus.</summary>
internal static class CharacterAcceptanceCheck
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static void Set(object obj, string field, object value) =>
        obj.GetType().GetField(field, Private)!.SetValue(obj, value);
    private static List<RenderItem> Items(Scene scene, string field) =>
        (List<RenderItem>)typeof(Scene).GetField(field, Private)!.GetValue(scene)!;
    private static long ResidentBytes(Scene scene) =>
        (typeof(Scene).GetField("_characterTextureAssets", Private)!.GetValue(scene) as TextureAssetManager)?.ResidentBytes ?? 0;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    // Match AnimateNode's S/Rx/Ry/Rz/T order using the native reference values.
    // ComputeNodeMatrices serves legacy static models and has a different M32
    // expression; it is not the character animation transform implementation.
    private static Matrix4 NativeRestTransform(Model model, int index)
    {
        var node = model.Nodes[index];
        Vector3 position = node.Position / model.Scale;
        Matrix4 local = Matrix4.CreateScale(node.Scale)
            * Matrix4.CreateRotationX(node.Angle.X) * Matrix4.CreateRotationY(node.Angle.Y)
            * Matrix4.CreateRotationZ(node.Angle.Z) * Matrix4.CreateTranslation(position);
        return node.ParentIndex < 0 ? local : local * NativeRestTransform(model, node.ParentIndex);
    }

    internal static int Run(string room, string output, bool lodSweep = false, bool morphSweep = false, bool materialSweep = false, Hunter hunter = Hunter.Samus, string? muzzleAudit = null, bool fidelitySweep = false)
    {
        string directory = Path.GetFullPath(output);
        Directory.CreateDirectory(directory);
        bool oldBright = RenderOptions.BrightSkins, oldForce = MapAudit.ForceEveryone;
        bool oldDetail = Features.MaxPlayerDetail;
        bool oldAdvanced = RenderOptions.AdvancedMaterials;
        bool oldHd = RenderOptions.CharacterModelReplacements;
        bool oldCosmetics = RenderOptions.ShowCustomCosmetics;
        var oldStyle = RenderOptions.BrightSkinStyle;
        Scene? scene = null, preview = null;
        var frames = new List<object>();
        var failures = new List<string>();
        var cases = new List<object>();
        Vector3? muzzleRestPoint = null;
        string? muzzleNativeBone = null;
        float maximumMuzzleError = 0;
        int muzzleCheckedFrames = 0;
        int authoredSurfaceCheckedFrames = 0, transparentSurfaceCheckedFrames = 0;
        bool teamRecolorsRequired = false;
        int teamRecolorCheckedFrames = 0;
        bool requireNativeBindIdentity = false;
        float maximumNativeBindError = 0;
        if (muzzleAudit != null)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(muzzleAudit));
            Require(document.RootElement.GetProperty("hunter").GetString() == hunter.ToString(), "Muzzle audit hunter differs.");
            var proof = document.RootElement.GetProperty("muzzleProof");
            requireNativeBindIdentity = document.RootElement.TryGetProperty("inheritedNativeBindScalePreserved", out var fullBind) && fullBind.GetBoolean();
            teamRecolorsRequired = document.RootElement.TryGetProperty("teamRecolorsRequired", out var teamRequired) && teamRequired.GetBoolean();
            var point = proof.GetProperty("restPoint").EnumerateArray().Select(n => n.GetSingle()).ToArray();
            muzzleRestPoint = new Vector3(point[0],point[1],point[2]);
            muzzleNativeBone = proof.GetProperty("nativeBone").GetString();
            var local = proof.GetProperty("nativeLocalPoint").EnumerateArray().Select(n => n.GetSingle()).ToArray();
            Require((new Vector3(local[0],local[1],local[2])-Metadata.MuzzleOffests[(int)hunter]).Length < .00001f,
                "Configured muzzle differs from authoritative native gameplay offset.");
        }
        try
        {
            GameSettings.Apply(GameState.LoadSettings());
            Require(RenderOptions.CharacterModelReplacements, "HD character models must already be enabled.");
            DebugLog.Force();
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new(800, 600);
            settings.CurrentMonitor = Monitors.GetPrimaryMonitor().Handle;
            settings.StartVisible = true;
            settings.StartFocused = false;
            settings.Title = $"Project Prime {hunter} acceptance";
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            try
            {
            Vector2i size = window.FramebufferSize;
            RenderOptions.ShowFps = false;
            RenderOptions.ShowCustomCosmetics = false;
            RenderOptions.BrightSkins = false;
            Features.MaxPlayerDetail = !lodSweep;
            MapAudit.ForceEveryone = true;
            Console.WriteLine($"CHARACTER ACCEPTANCE backend={GraphicsBackendPolicy.Resolved} room={room} "
                + (lodSweep ? "native distance LOD1 and threshold sweep" : "focus-independent scene input; every simulation step rendered; LOD0 forced"));
            preview = new Scene(size, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
            preview.SideScene = true;
            preview.OnLoad(); preview.OnResize();
            Preview(preview, window, directory, "launcher-before", failures, hunter);
            Scene.LauncherPreview = false; Scene.PreviewWanted = false;

            scene = new Scene(size, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
            scene.Random.SetRng1(123456);
            scene.AddPlayer(hunter == Hunter.Kanden ? Hunter.Samus : Hunter.Kanden);
            scene.AddPlayer(hunter);
            scene.Players.Items[0].IsBot = true;
            scene.Players.Items[0].BotLevel = 1;
            scene.Players.Items[1].IsBot = false;
            scene.AddRoom(room, GameMode.Battle, playerCount: 2);
            scene.OnLoad(); scene.OnResize();
            scene.GameState.PointGoal = 0; scene.GameState.MatchTime = -1;
            scene.GameState.SpawnProtection = false;
            scene.GameState.EnhancedHunters = false;
            scene.GameState.ShadowFreeze = false;
            var target = scene.Players.Items[1];
            target.IsBot = false;
            target.Controls.MouseAim = false;
            for (int i = 0; i < 300; i++) Step(scene, window);
            Require(target.Health > 0, $"{hunter} did not spawn.");
            scene.SetFreeCamera(true);

            var stages = new List<(string Name, int Count)>
            {
                ("idle",60), ("materials-on",60), ("materials-off",60), ("materials-restored",60),
                ("run",120), ("strafe",120), ("jump-fall-land",180),
                ("aim-turn",120), ("fire",90), ("damage",60), ("freeze",90), ("thaw",60),
                ("morph-move",120), ("unmorph",120), ("bright-skin",90),
                ("team-orange",60), ("team-green",60), ("double-damage",90), ("death-respawn",300)
            };
            if (lodSweep) stages.AddRange(new[] { ("lod-threshold-sweep",240), ("max-detail-override",60), ("lod1-restored",60), ("neutral-comparison",60) });
            if (morphSweep) stages.AddRange(new[] {
                ("ball-enter",60), ("ball-idle",60), ("ball-materials-on",60), ("ball-materials-off",60),
                ("ball-roll-forward",120), ("ball-roll-strafe",120), ("ball-boost",120), ("ball-stop",180), ("ball-bomb-jump",180),
                ("ball-damage",60), ("ball-freeze",90), ("ball-thaw",60), ("ball-bright",60),
                ("ball-team-orange",60), ("ball-team-green",60), ("ball-double-damage",90), ("ball-unmorph",120),
                ("airborne-morph",180), ("ball-unmorph-again",120), ("ball-reenter",60), ("ball-death-respawn",300) });
            if (materialSweep)
            {
                for (int suit = 0; suit < 6; suit++) stages.Add(("material-suit-" + suit,60));
                stages.AddRange(new[] { ("material-team-orange",60), ("material-team-green",60),
                    ("material-neutral-off",60), ("material-neutral-on",60), ("material-ball-enter",60) });
                for (int suit = 0; suit < 6; suit++) stages.Add(("material-ball-suit-" + suit,60));
                stages.AddRange(new[] { ("material-ball-neutral-off",60), ("material-ball-neutral-on",60), ("material-ball-unmorph",120),
                    ("material-revisit-suit-1",60),("material-revisit-suit-2",60),("material-residency-settle",180) });
            }
            CharacterRigidRenderModel? ball = null; ModelInstance? nativeBall = null;
            int ballFrames = 0; bool boosted = false, bombed = false, bombJumped = false, altAirborne = false, ballDied = false, ballRespawned = false;
            long textureBytesBeforeBall = 0, textureBytesAfterBall = 0;
            long peakCharacterTextureBytes=0;
            float ballRollTravel = 0, boostTravel = 0;
            int[] lodFrames = new int[2]; int lodTransitions = 0, previousLod = -1;
            var lodModels = new Dictionary<int, CharacterWeightedRenderModel>();
            var lodAuthored = new Dictionary<int, CharacterWeightedModelData>();
            long residencyBeforeSecondLod = 0, residencyAfterSecondLod = 0;
            bool airborne = false, falling = false, landed = false, alt = false, morphing = false, unmorphing = false;
            bool frozen = false, died = false, respawned = false, fired = false, doubled = false;
            var start = target.Position;
            float runTravel = 0, strafeTravel = 0, altTravel = 0;
            int submittedFrames = 0;
            CharacterWeightedRenderModel? weighted = null;
            CharacterWeightedModelData? authored = null;
            string[] checkedJoints = Array.Empty<string>();
            foreach (var stage in stages)
            {
                int submitted = 0, missing = 0, ballBefore = ballFrames;
                for (int age = 0; age < stage.Count; age++)
                {
                    target.Controls.ClearAll();
                    object input = typeof(PlayerEntity).GetProperty("Input", Private)!.GetValue(target)!;
                    input.GetType().GetProperty("HasInput")!.SetValue(input, true);
                    // Keep ordinary bot damage from killing the subject outside the death case.
                    if (stage.Name is not ("death-respawn" or "ball-death-respawn")) target.Health = 999;
                    if (age == 0)
                    {
                        if (stage.Name == "ball-death-respawn") Require(target.IsAltForm, "Ball death test must start in alternate form.");
                        Configure(stage.Name, scene, target);
                        if (materialSweep && stage.Name.StartsWith("material-",StringComparison.Ordinal)) ConfigureMaterial(stage.Name, target, scene);
                    }
                    if (lodSweep) Features.MaxPlayerDetail = stage.Name == "max-detail-override";
                    switch (stage.Name)
                    {
                        case "material-ball-enter": case "material-ball-unmorph":
                        case "ball-enter": case "ball-reenter":
                        case "ball-unmorph": case "ball-unmorph-again":
                            if (age == 0) Hold(target.Controls.Morph, true); break;
                        case "ball-roll-forward": Hold(age < 60 ? target.Controls.RollUp : target.Controls.RollDown); break;
                        case "ball-roll-strafe": Hold(age < 60 ? target.Controls.RolltLeft : target.Controls.RollRight); break;
                        case "ball-boost":
                            if (age < 45) Hold(target.Controls.Boost);
                            Hold(age < 70 ? target.Controls.RollUp : target.Controls.RollDown); break;
                        case "ball-bomb-jump": if (age % 60 == 0) Hold(target.Controls.AltAttack, true); break;
                        case "airborne-morph":
                            if (age < 8) Hold(target.Controls.Jump, age == 0);
                            if (age == 6) Hold(target.Controls.Morph, true); break;
                        case "lod-threshold-sweep": Hold(target.Controls.MoveUp); goto case "aim-turn";
                        case "run": Hold(age < 60 ? target.Controls.MoveUp : target.Controls.MoveDown); break;
                        case "strafe": Hold(age < 60 ? target.Controls.MoveLeft : target.Controls.MoveRight); break;
                        case "jump-fall-land": if (age < 12) Hold(target.Controls.Jump, age == 0); break;
                        case "aim-turn":
                            float yaw = age * MathF.PI / 30;
                            target.ModSetAim(new Vector3(MathF.Sin(yaw), MathF.Sin(age * MathF.PI / 40) * 2, MathF.Cos(yaw)).Normalized());
                            break;
                        case "fire": Hold(target.Controls.Shoot, age % 20 == 0); break;
                        case "morph-move":
                            if (age == 0) Hold(target.Controls.Morph, true);
                            if (age >= 55) Hold(age < 85 ? target.Controls.RollRight : target.Controls.RolltLeft);
                            break;
                        case "unmorph": if (age == 0) Hold(target.Controls.Morph, true); break;
                    }
                    Vector3 previous = target.Position;
                    // Keep the real AI opponent out of the inspection camera. It still
                    // simulates/fires, but cannot stand inside the subject's silhouette.
                    scene.Players.Items[0].Position = target.Position + new Vector3(8,0,8);
                    scene.OnSimulationFrame();
                    if (stage.Name.StartsWith("ball-roll", StringComparison.Ordinal)) ballRollTravel += (target.Position-previous).Length;
                    if (stage.Name == "ball-boost") { boostTravel += (target.Position-previous).Length; boosted |= target.Flags1.TestFlag(PlayerFlags1.Boosting); }
                    if (stage.Name == "ball-bomb-jump")
                    {
                        foreach (var entity in scene.Entities) if (entity is BombEntity bomb && bomb.Owner == target) bombed = true;
                        bombJumped |= !target.Flags1.TestFlag(PlayerFlags1.Grounded) && target.Speed.Y > .05f;
                    }
                    if (stage.Name == "airborne-morph") altAirborne |= target.IsAltForm && !target.Flags1.TestFlag(PlayerFlags1.Grounded);
                    if (stage.Name == "ball-death-respawn")
                    {
                        ballDied |= target.Health == 0;
                        ballRespawned |= ballDied && target.Health > 0 && !target.IsAltForm;
                    }
                    if (stage.Name == "run") runTravel += (target.Position - previous).Length;
                    if (stage.Name == "strafe") strafeTravel += (target.Position - previous).Length;
                    if (stage.Name == "jump-fall-land")
                    {
                        if (!target.Flags1.TestFlag(PlayerFlags1.Grounded)) airborne = true;
                        if (!target.Flags1.TestFlag(PlayerFlags1.Grounded) && target.Speed.Y < 0) falling = true;
                        if (airborne && target.Flags1.TestFlag(PlayerFlags1.Grounded)) landed = true;
                    }
                    if (stage.Name == "morph-move")
                    {
                        morphing |= target.IsMorphing; alt |= target.IsAltForm;
                        if (target.IsAltForm) altTravel += (target.Position - previous).Length;
                    }
                    if (stage.Name == "unmorph") unmorphing |= target.IsUnmorphing;
                    if (stage.Name == "freeze") frozen |= target.ModFrozen;
                    if (stage.Name == "fire")
                        foreach (var entity in scene.Entities)
                            if (entity is BeamProjectileEntity beam && beam.Owner == target && beam.Lifespan > 0) fired = true;
                    if (stage.Name == "double-damage") doubled |= target.DoubleDamage;
                    if (stage.Name == "death-respawn")
                    {
                        died |= target.Health == 0;
                        if (died && target.Health > 0) respawned = true;
                    }
                    if (materialSweep && stage.Name.StartsWith("material-",StringComparison.Ordinal)) Set(target,"_timeSinceDamage",(ushort)255);
                    if (stage.Name == "neutral-comparison") Set(target,"_timeSinceDamage",(ushort)255);
                    if (stage.Name is "bright-skin" or "team-orange" or "team-green")
                        Set(target,"_timeSinceDamage",(ushort)255);
                    if (stage.Name is "ball-bright" or "ball-team-orange" or "ball-team-green")
                        Set(target,"_timeSinceDamage",(ushort)255); // Isolate tint from the opponent's damage flashes.
                    Follow(scene, target);
                    DesktopGraphicsSession.Resize(window);
                    float lodDistance = stage.Name == "lod-threshold-sweep" ? 3 + .4f * MathF.Sin((age + .25f) * MathF.PI / 15) : 4;
                    if (lodSweep) scene.Players.Main.CameraInfo.Position = target.Position + new Vector3(0,0,lodDistance);
                    long textureBytesBeforeDraw = ResidentBytes(scene);
                    scene.OnDrawFrame();
                    int lod = target.Flags2.TestFlag(PlayerFlags2.Lod1) ? 1 : 0;
                    if (lodSweep)
                    {
                        float actualDistance = (target.Position - scene.Players.Main.CameraInfo.Position).Length;
                        Require(MathF.Abs(actualDistance - lodDistance) < .001f
                            && lod == (!Features.MaxPlayerDetail && actualDistance >= 3 ? 1 : 0),
                            $"Native distance LOD selection differs: {stage.Name}/{age}, requested={lodDistance}, actual={actualDistance}, selected={lod}.");
                    }
                    int expectedPackets = 0;
                    CharacterWeightedRenderModel? selectedWeighted = null;
                    if (CharacterModelRuntime.TryGetWeighted(scene, hunter, CharacterModelPart.Biped,
                        target.BipedModel2.Model, out var resolvedWeighted, lod))
                    {
                        selectedWeighted = resolvedWeighted;
                        expectedPackets = resolvedWeighted.Segments.Count;
                    }
                    var packets = Items(scene, "_decalItems").Concat(Items(scene, "_nonDecalItems")).Concat(Items(scene, "_translucentItems"))
                        .Distinct()
                        .Where(item => item.WeightedSkinning && selectedWeighted != null
                            && selectedWeighted.Segments.Any(s => s.ListId == item.ListId)).ToArray();
                    bool eligible = target.Health > 0 && !target.IsAltForm
                        && target.Flags2.TestFlag(PlayerFlags2.DrawnThirdPerson)
                        && !target.Flags2.TestFlag(PlayerFlags2.HideModel);
                    if (eligible)
                    {
                        if (expectedPackets == 0 || packets.Length != expectedPackets) { missing++; failures.Add($"{stage.Name}/{age}: biped drawn with {packets.Length}/{expectedPackets} Weighted4 packets"); }
                        else
                        {
                            submitted++; submittedFrames++;
                            if (!lodModels.TryGetValue(lod, out weighted))
                            {
                                Require(CharacterModelRuntime.TryGetWeighted(scene, hunter, CharacterModelPart.Biped,
                                    target.BipedModel2.Model, out weighted, lod), $"Installed {hunter} weighted model did not resolve.");
                                if (lodSweep && lodModels.Count == 1)
                                {
                                    residencyBeforeSecondLod = textureBytesBeforeDraw;
                                    residencyAfterSecondLod = ResidentBytes(scene);
                                    Require(residencyBeforeSecondLod > 0 && residencyBeforeSecondLod == residencyAfterSecondLod, "Loading the second LOD increased character texture residency.");
                                }
                                lodModels.Add(lod, weighted);
                                checkedJoints = weighted.Joints.Select(j => target.BipedModel2.Model.Nodes[j.NativeNodeIndex].Name).ToArray();
                                if (requireNativeBindIdentity)
                                {
                                    foreach (var joint in weighted.Joints)
                                    {
                                        Matrix4 identity = joint.InverseBind * NativeRestTransform(target.BipedModel2.Model, joint.NativeNodeIndex);
                                        for (int row = 0; row < 4; row++)
                                            for (int column = 0; column < 4; column++)
                                                maximumNativeBindError = MathF.Max(maximumNativeBindError,
                                                    MathF.Abs(identity[row, column] - (row == column ? 1 : 0)));
                                    }
                                    Require(maximumNativeBindError < .0001f,
                                        $"Inverse binds differ from authoritative native rest transforms: {maximumNativeBindError}");
                                }
                                authored = CharacterWeightedModelLoader.Load(weighted.Asset);
                                lodAuthored.Add(lod, authored);
                                Require(authored.Primitives.Count == weighted.Segments.Count,
                                    "Authored material primitive count differs from rendered segments.");
                            }
                            authored = lodAuthored[lod];
                            foreach (var segment in weighted.Segments)
                            {
                                var packet = packets.Single(p => p.ListId == segment.ListId);
                                if (segment.DoubleSided) Require(packet.CullingMode == CullingMode.Neither,
                                    "Authored double-sided character surface was culled.");
                            }
                            authoredSurfaceCheckedFrames++;

                            if (teamRecolorsRequired && (stage.Name is "team-orange" or "team-green"))
                            {
                                bool checkedVariant = false;
                                for (int i = 0; i < weighted.Segments.Count; i++)
                                {
                                    var segment = weighted.Segments[i];
                                    if (authored.Primitives[i].Albedo?.Recolors?.ContainsKey(target.Recolor) != true) continue;
                                    var packet = packets.Single(p => p.ListId == segment.ListId);
                                    Require(packet.TextureBindingId == segment.GetAlbedo(scene, target.Recolor)
                                        && packet.TextureBindingId != segment.AlbedoBinding, "Native team recolor texture missing.");
                                    checkedVariant = true;
                                }
                                Require(checkedVariant, "No team recolor variant was submitted.");
                                teamRecolorCheckedFrames++;
                            }
                            Require(weighted.Asset.Lod == lod && packets.All(p => weighted.Segments.Any(s => s.ListId == p.ListId)), "A native or stale LOD packet replaced the selected HD tier.");
                            lodFrames[lod]++;
                            if (previousLod >= 0 && previousLod != lod) lodTransitions++;
                            previousLod = lod;
                            // Inspect every primitive and native joint, including the new
                            // hips, knees and ankles, rather than only the shoulder proof.
                            foreach (var packet in packets)
                            {
                                Require(packet.MatrixStackCount == weighted.Joints.Count,
                                    "Weighted4 packet does not contain the complete native joint palette.");
                                for (int joint = 0; joint < weighted.Joints.Count; joint++)
                                {
                                    Matrix4 expected = weighted.Joints[joint].InverseBind
                                        * target.BipedModel2.Model.Nodes[weighted.Joints[joint].NativeNodeIndex].Animation;
                                    int offset = joint * 16;
                                    var actual = new Matrix4(packet.MatrixStack[offset],packet.MatrixStack[offset+1],packet.MatrixStack[offset+2],packet.MatrixStack[offset+3],
                                        packet.MatrixStack[offset+4],packet.MatrixStack[offset+5],packet.MatrixStack[offset+6],packet.MatrixStack[offset+7],
                                        packet.MatrixStack[offset+8],packet.MatrixStack[offset+9],packet.MatrixStack[offset+10],packet.MatrixStack[offset+11],
                                        packet.MatrixStack[offset+12],packet.MatrixStack[offset+13],packet.MatrixStack[offset+14],packet.MatrixStack[offset+15]);
                                    if (actual != expected)
                                        failures.Add($"{stage.Name}/{age}: stale palette for {checkedJoints[joint]}");
                                }
                                if (packet.MatrixStack.Take(packet.MatrixStackCount*16).Any(v => !float.IsFinite(v)))
                                    failures.Add($"{stage.Name}/{age}: non-finite native joint palette");
                            }
                            if (muzzleRestPoint.HasValue)
                            {
                                int joint = Array.IndexOf(checkedJoints, muzzleNativeBone);
                                Require(joint >= 0, "Muzzle joint is absent from skin.");
                                var node = target.BipedModel2.Model.Nodes[weighted.Joints[joint].NativeNodeIndex];
                                Vector3 expectedMuzzle = Matrix.Vec3MultMtx4(Metadata.MuzzleOffests[(int)hunter], node.Animation);
                                Vector3 actualMuzzle = Matrix.Vec3MultMtx4(muzzleRestPoint.Value,
                                    weighted.Joints[joint].InverseBind * node.Animation);
                                float error = (actualMuzzle-expectedMuzzle).Length;
                                maximumMuzzleError = MathF.Max(maximumMuzzleError,error);
                                Require(float.IsFinite(error) && error < .0001f, $"Animated weapon muzzle drift: {stage.Name}/{age}: {error}.");
                                muzzleCheckedFrames++;
                            }
                            if (stage.Name.StartsWith("materials-", StringComparison.Ordinal) || (materialSweep && stage.Name.StartsWith("material-", StringComparison.Ordinal)))
                            {
                                for (int segmentIndex = 0; segmentIndex < weighted.Segments.Count; segmentIndex++)
                                {
                                    var segment = weighted.Segments[segmentIndex];
                                    var maps = authored!.Primitives[segmentIndex].MaterialMaps;
                                    var packet = packets.Single(p => p.ListId == segment.ListId);
                                    Require(packet.TextureBindingId == segment.GetAlbedo(scene,target.Recolor) && packet.TexgenMode == TexgenMode.Texcoord && packet.TexcoordMatrix == Matrix4.Identity,"Authored suit albedo/UV selection failed.");
                                    if (segment.Transparent)
                                    {
                                        Require(packet.RenderMode == RenderMode.Translucent,
                                            "Source alpha surface was submitted as opaque.");
                                        transparentSurfaceCheckedFrames++;
                                    }
                                    if (RenderOptions.AdvancedMaterials)
                                    {
                                        // Companions are optional in glTF. Check precisely
                                        // the authored channels, including partial map sets.
                                        Require((segment.MaterialMaps.Normal != 0) == (maps?.Normal != null)
                                            && (segment.MaterialMaps.Specular != 0) == (maps?.MetallicRoughness != null)
                                            && (segment.MaterialMaps.Emissive != 0) == (maps?.Emissive != null),
                                            "Authored material companion upload incomplete or unexpected.");
                                        Require(packet.CosmeticMaterial.NormalBinding == segment.MaterialMaps.Normal
                                            && packet.CosmeticMaterial.SpecularBinding == segment.MaterialMaps.Specular
                                            && packet.CosmeticMaterial.EmissiveBinding == segment.MaterialMaps.Emissive,
                                            "Embedded material companions were not submitted after enabling them.");
                                    }
                                    else Require(packet.CosmeticMaterial == default,
                                        "Embedded material companions remained active with advanced materials off.");
                                }
                            }
                        }
                    }
                    if (morphSweep && target.Health > 0 && target.IsAltForm
                        && target.Flags2.TestFlag(PlayerFlags2.DrawnThirdPerson) && !target.Flags2.TestFlag(PlayerFlags2.HideModel))
                    {
                        nativeBall ??= (ModelInstance)typeof(PlayerEntity).GetField("_altModel", Private)!.GetValue(target)!;
                        if (ball == null)
                        {
                            Require(CharacterModelRuntime.TryGetRigid(scene, hunter, CharacterModelPart.AlternateForm,
                                nativeBall.Model, out ball), "Source rigid Morph Ball did not resolve.");
                            Require(ball.Segments.Count == 3 && ball.IndexCount / 3 == 2060, "Unexpected Morph Ball geometry.");
                            textureBytesBeforeBall = textureBytesBeforeDraw; textureBytesAfterBall = ResidentBytes(scene);
                            Require(textureBytesBeforeBall > 0 && textureBytesBeforeBall == textureBytesAfterBall, "Morph Ball duplicated body atlases.");
                        }
                        var all = Items(scene,"_nonDecalItems").Concat(Items(scene,"_translucentItems")).Distinct().ToArray();
                        foreach (var segment in ball.Segments)
                        {
                            var node = nativeBall.Model.Nodes[segment.NativeNodeIndex];
                            var material = nativeBall.Model.Materials[segment.NativeMaterialIndex];
                            var packet = all.SingleOrDefault(p => p.ListId == segment.ListId);
                            bool expected = node.Enabled && nativeBall.Model.NodeParentsEnabled(node) && material.CurrentAlpha > 0;
                            Require((packet != null) == expected, $"{stage.Name}/{age}: missing/stale ball primitive {material.Name}.");
                            if (packet == null) continue;
                            Require(!packet.WeightedSkinning && packet.Transform == node.Animation && packet.BillboardMode == node.BillboardMode,
                                $"{stage.Name}/{age}: incorrect ball/glow transform or billboard.");
                            if (segment.AlbedoBinding.HasValue && packet.TextureBindingId == segment.GetAlbedo(scene,target.Recolor))
                                Require(packet.TexgenMode == TexgenMode.Texcoord && packet.TexcoordMatrix == Matrix4.Identity, "Morph Ball authored UVs lost.");
                            if ((stage.Name.StartsWith("ball-materials",StringComparison.Ordinal) || (materialSweep && stage.Name.StartsWith("material-ball",StringComparison.Ordinal))) && segment.AlbedoBinding.HasValue)
                                Require(packet.TextureBindingId == segment.GetAlbedo(scene,target.Recolor)
                                    && packet.CosmeticMaterial.NormalBinding == (RenderOptions.AdvancedMaterials ? segment.MaterialMaps.Normal : 0)
                                    && packet.CosmeticMaterial.SpecularBinding == (RenderOptions.AdvancedMaterials ? segment.MaterialMaps.Specular : 0)
                                    && packet.CosmeticMaterial.EmissiveBinding == (RenderOptions.AdvancedMaterials ? segment.MaterialMaps.Emissive : 0), "Morph Ball material toggle or atlas submission failed.");
                            if (stage.Name is "ball-bright" or "ball-team-orange" or "ball-team-green")
                            {
                                Vector4? expectedColor = BrightSkins.GetColor(target);
                                Require(expectedColor.HasValue && packet.OverrideColor == expectedColor
                                    && packet.TexturedPlayerSkin && packet.PaletteOverride == null,
                                    "Morph Ball bright/team tint missing or masked by a status palette.");
                            }
                        }
                        ballFrames++;
                    }
                    peakCharacterTextureBytes=Math.Max(peakCharacterTextureBytes,ResidentBytes(scene));
                    Require(scene.OnRenderFrame(), "Render stopped.");
                    if (age == 0 || age == 15 || age == 40 || age == stage.Count-1)
                        ScreenCapture.Save(scene, Path.Combine(directory, $"{stage.Name}-{age:D3}.png"));
                    if (stage.Name == "idle" && age == stage.Count - 1)
                    {
                        foreach (bool hd in new[] { false, true })
                        {
                            RenderOptions.CharacterModelReplacements = hd;
                            scene.OnDrawFrame(); Require(scene.OnRenderFrame(), "Biped comparison render stopped.");
                            ScreenCapture.Save(scene, Path.Combine(directory, hd ? "same-pose-source-biped.png" : "same-pose-native-biped.png"));
                            DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                        }
                    }
                    frames.Add(new { stage = stage.Name, age, frame = scene.FrameCount, health = target.Health,
                        alt = target.IsAltForm, morph = target.IsMorphing, unmorph = target.IsUnmorphing,
                        frozen = target.ModFrozen, doubleDamage = target.DoubleDamage,
                        legAnimation = typeof(PlayerEntity).GetProperty("Biped1Anim", Private)!.GetValue(target)!.ToString(),
                        torsoAnimation = typeof(PlayerEntity).GetProperty("Biped2Anim", Private)!.GetValue(target)!.ToString(),
                        weightedPackets = packets.Length, eligible, lod, lodDistance, boosting = target.Flags1.TestFlag(PlayerFlags1.Boosting), x=target.Position.X,y=target.Position.Y,z=target.Position.Z });
                    DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                    if (stage.Name == "ball-materials-off" && age == stage.Count - 1)
                    {
                        // Compare the two assets in the same rolling pose, without a simulation step.
                        Set(target,"_timeSinceDamage",(ushort)255);
                        foreach (bool hd in new[] { false, true })
                        {
                            RenderOptions.CharacterModelReplacements = hd;
                            scene.OnDrawFrame(); Require(scene.OnRenderFrame(), "Morph comparison render stopped.");
                            ScreenCapture.Save(scene, Path.Combine(directory, hd ? "same-pose-source-ball.png" : "same-pose-native-ball.png"));
                            DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                        }
                    }
                }
                cases.Add(new { stage = stage.Name, frames = stage.Count, weightedFrames = submitted, missingWeightedFrames = missing, rigidBallFrames = ballFrames - ballBefore });
                Console.WriteLine($"CHARACTER ACCEPTANCE {stage.Name}: {submitted} weighted frames, {ballFrames-ballBefore} rigid ball frames, {missing} missing");
            }
            Require(runTravel > .5f && strafeTravel > .5f, $"Run/strafe controls did not move {hunter}.");
            Require(airborne && falling && landed, "Jump/fall/land not observed.");
            Require(fired && frozen && doubled, "Fire/freeze/double-damage states not observed.");
            Require(morphing && alt && unmorphing && !target.IsAltForm && altTravel > .2f, "Morph/move/unmorph incomplete.");
            Require(died && respawned, "Death/respawn incomplete.");
            if (lodSweep)
            {
                Require(lodFrames[0] > 60 && lodFrames[1] > 1000 && lodTransitions >= 12, "Both tiers and repeated native LOD transitions must be observed.");
                var a = lodModels[0]; var b = lodModels[1];
                Require(b.IndexCount / 3 is >= 3500 and <= 5500 && b.IndexCount < a.IndexCount, "LOD1 triangle budget invalid.");
                foreach (var segment in a.Segments)
                {
                    var peer = b.Segments.Single(s => s.NativeMaterialIndex == segment.NativeMaterialIndex && s.AlbedoBinding == segment.AlbedoBinding);
                    Require(segment.AlbedoBinding == peer.AlbedoBinding && segment.MaterialMaps == peer.MaterialMaps, "LOD tiers duplicated identical embedded material residency.");
                }
                // Paired captures without advancing simulation isolate geometry switching.
                Features.MaxPlayerDetail = false;
                for (int tier = 0; tier < 2; tier++)
                {
                    scene.Players.Main.CameraInfo.Position = target.Position + new Vector3(0,0,tier == 0 ? 2.9f : 3.1f);
                    scene.OnDrawFrame(); Require(scene.OnRenderFrame(), "Comparison render stopped.");
                    ScreenCapture.Save(scene, Path.Combine(directory, $"same-pose-lod{tier}.png"));
                    DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
                }
            }
            if (morphSweep) Require(ballFrames > 1200 && ballRollTravel > .5f && boostTravel > .2f && boosted && bombed && bombJumped && altAirborne && ballDied && ballRespawned,
                $"Morph acceptance coverage incomplete: frames={ballFrames}, boosted={boosted}, bombed={bombed}, bombJumped={bombJumped}, altAirborne={altAirborne}.");
            long residencyAfterMaterialSweep = ResidentBytes(scene);
            long variantEvictions=scene.CharacterVariantEvictions;
            int remainingVariantTextures=scene.CharacterVariantResidentCount;
            if (materialSweep) Require(variantEvictions>0 && remainingVariantTextures==0,
                "Unused suit variants must evict, revisits must redraw, and the default settle must release every optional variant.");
            scene.DoCleanup(); scene.UnloadGl(); scene = null;
            Preview(preview, window, directory, "launcher-after", failures, hunter);
            if (fidelitySweep)
            {
                float oldYaw = Mods.Cosmetics.CosmeticPreview.Yaw;
                bool beforePreviewAdvanced = RenderOptions.AdvancedMaterials;
                double beforeNextStep = (double)typeof(Scene).GetField("_launcherPreviewNextStep", Private)!.GetValue(preview)!;
                try
                {
                    RenderOptions.BrightSkins = false;
                    Scene.LauncherSuit = 0;
                    // Freeze the same idle pose across every angle and map toggle.
                    // Only this diagnostic scene's pacing is changed.
                    Set(preview, "_launcherPreviewNextStep", double.MaxValue);
                    foreach (int angle in new[] { 0, 45, 90, 135, 180, 225, 270, 315 })
                    {
                        Mods.Cosmetics.CosmeticPreview.Yaw = MathHelper.DegreesToRadians(angle);
                        foreach (bool advanced in new[] { false, true })
                        {
                            RenderOptions.AdvancedMaterials = advanced;
                            Require(preview.ModDrawPreviewAlone(window.FramebufferSize), "Fidelity preview fallback.");
                            var packets = Items(preview, "_previewItems").Where(p => p.Type == RenderItemType.Mesh).ToArray();
                            Require(packets.Length > 0 && packets.All(p => p.WeightedSkinning), "Fidelity preview contains native fallback meshes.");
                            Require(packets.All(p => p.CullingMode == CullingMode.Neither),
                                "Source fidelity preview culled a double-sided surface.");
                            Require(ScreenCapture.SaveWindow(preview, Path.Combine(directory, $"fidelity-yaw-{angle}-maps-{advanced}.png")), "Fidelity capture failed.");
                        }
                        // A neutral geometry view exposes dark albedo features and
                        // permits silhouette/shading comparison with Blender Solid.
                        RenderOptions.AdvancedMaterials = false;
                        Set(preview, "_showTextures", false);
                        try
                        {
                            Require(preview.ModDrawPreviewAlone(window.FramebufferSize), "Neutral geometry preview fallback.");
                            Require(ScreenCapture.SaveWindow(preview, Path.Combine(directory, $"fidelity-yaw-{angle}-solid.png")), "Neutral geometry capture failed.");
                        }
                        finally { Set(preview, "_showTextures", true); }
                    }
                }
                finally
                {
                    Set(preview, "_launcherPreviewNextStep", beforeNextStep);
                    Mods.Cosmetics.CosmeticPreview.Yaw = oldYaw;
                    RenderOptions.AdvancedMaterials = beforePreviewAdvanced;
                }
            }
            if (materialSweep)
            {
                float oldYaw = Mods.Cosmetics.CosmeticPreview.Yaw;
                bool beforePreviewAdvanced = RenderOptions.AdvancedMaterials;
                try
                {
                    RenderOptions.BrightSkins=false;
                    for (int suit=0; suit<6; suit++)
                    {
                        Scene.LauncherSuit=suit;
                        foreach (int angle in new[] {0,180})
                        {
                            Mods.Cosmetics.CosmeticPreview.Yaw=MathHelper.DegreesToRadians(angle);
                            foreach (bool advanced in new[] {false,true})
                            {
                                RenderOptions.AdvancedMaterials=advanced;
                                preview.ModStepPreview();
                                Require(preview.ModDrawPreviewAlone(window.FramebufferSize),"Material preview fallback.");
                                Require(ScreenCapture.SaveWindow(preview,Path.Combine(directory,$"preview-suit-{suit}-yaw-{angle}-maps-{advanced}.png")),"Material preview capture failed.");
                            }
                        }
                    }
                }
                finally { Mods.Cosmetics.CosmeticPreview.Yaw=oldYaw; RenderOptions.AdvancedMaterials=beforePreviewAdvanced; }
            }
            File.WriteAllText(Path.Combine(directory, "acceptance.json"), JsonSerializer.Serialize(new
            {
                hunter = hunter.ToString(), backend = GraphicsBackendPolicy.Resolved.ToString(), room, submittedFrames, lodFrames, lodTransitions,
                materialSweep, fidelitySweep, morphSweep, ballFrames, boosted, bombed, bombJumped, altAirborne, ballDied, ballRespawned, ballRollTravel, boostTravel, textureBytesBeforeBall, textureBytesAfterBall, residencyAfterMaterialSweep,
                mobileTextureTier=CharacterModelPack.ForceMobileTierForCheck, textureCompression="Rgba8",
                peakCharacterTextureBytes,variantEvictions,remainingVariantTextures,
                sharedLodTextureBindings = lodSweep, residencyBeforeSecondLod, residencyAfterSecondLod, runTravel, strafeTravel,
                airborne, falling, landed, fired, frozen, doubled, morphing, alt, altTravel, unmorphing, died, respawned,
                cases, failures, checkedJoints, authoredSurfaceCheckedFrames, transparentSurfaceCheckedFrames, muzzleCheckedFrames, maximumMuzzleError, teamRecolorCheckedFrames, requireNativeBindIdentity, maximumNativeBindError,
                testedModelSha256 = weighted == null ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(weighted.Asset.ModelPath))).ToLowerInvariant(),
                embeddedMaterialSegments = weighted?.Segments.Count(s => s.MaterialMaps.Any) ?? 0,
                scope = (fidelitySweep ? "Twenty-four fixed-pose launcher fidelity captures at eight angles: Source companion maps off/on and texture-free geometry. " : "") + (materialSweep ? "All six native suit albedos for biped and ball, regular Textured bright/team colors, complete companion toggles, and 24 clean launcher material views. " : "") + (morphSweep ? "Rigid Source Morph Ball: native rolling/glow transforms, billboard, authored UV/material toggles, boost, bombs/jump, airborne morph, extra cycles and shared images. " : "") + (lodSweep ? "Native 3-unit distance switch, repeated LOD0/1 sweeps and max-detail override. Same embedded texture bindings across both tiers. " : "LOD0 forced. ") + $"Real scene simulation/render with one AI bot and scripted {hunter} controls. Custom cosmetic equipment disabled to inspect the embedded Source materials. Damage, freeze and double damage are injected. Every primitive's complete native joint palette is checked. Embedded material submission is checked through on/off/on toggles. Visual capture review is separate from packet assertions."
            }, Json));
            File.WriteAllText(Path.Combine(directory, "frames.json"), JsonSerializer.Serialize(frames, Json));
            Console.WriteLine($"CHARACTER ACCEPTANCE {(failures.Count == 0 ? "PASS" : "FAIL")} {directory}");
            return failures.Count == 0 ? 0 : 1;
            }
            finally
            {
                scene?.DoCleanup(); scene?.UnloadGl();
                preview?.UnloadGl();
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(directory,"failure.txt"),error.ToString());
            File.WriteAllText(Path.Combine(directory,"frames.json"),JsonSerializer.Serialize(frames,Json));
            Console.Error.WriteLine("CHARACTER ACCEPTANCE FAIL " + error); return 1;
        }
        finally
        {
            // Scene cleanup must happen while the graphics context is alive; normal path above does so.
            RenderOptions.BrightSkins = oldBright; RenderOptions.BrightSkinStyle = oldStyle;
            Features.MaxPlayerDetail = oldDetail; MapAudit.ForceEveryone = oldForce;
            RenderOptions.AdvancedMaterials = oldAdvanced;
            RenderOptions.CharacterModelReplacements = oldHd;
            RenderOptions.ShowCustomCosmetics = oldCosmetics;
        }
    }

    private static void ConfigureMaterial(string name, PlayerEntity target, Scene scene)
    {
        scene.GameState.Teams = false; RenderOptions.BrightSkins = false;
        RenderOptions.BrightSkinStyle = PlayerSkinStyle.Textured;
        Set(target,"_doubleDmgTimer",(ushort)0); target.ModSetFrozen(false);
        if (name.Contains("suit-",StringComparison.Ordinal))
        {
            target.Recolor = int.Parse(name[(name.LastIndexOf('-')+1)..]);
            if (target.Recolor >= 4) { scene.GameState.Teams = true; target.TeamIndex = target.Recolor-4; TeamVisuals.Apply(target); }
        }
        else if (name is "material-team-orange" or "material-team-green")
        {
            scene.GameState.Teams = true; target.TeamIndex = name.EndsWith("green",StringComparison.Ordinal) ? 1 : 0;
            TeamVisuals.Apply(target); RenderOptions.BrightSkins = true;
        }
        else { target.Recolor = 0; }
        if (name.EndsWith("-off",StringComparison.Ordinal)) RenderOptions.AdvancedMaterials = false;
        if (name.EndsWith("-on",StringComparison.Ordinal)) RenderOptions.AdvancedMaterials = true;
    }

    private static void Hold(Keybind key, bool press = false) { key.IsDown = true; key.IsPressed = press; }
    private static void Configure(string name, Scene scene, PlayerEntity target)
    {
        switch (name)
        {
            case "ball-enter":
                scene.GameState.Teams = false; target.Recolor = 0; RenderOptions.BrightSkins = false;
                Set(target,"_doubleDmgTimer",(ushort)0); target.ModSetFrozen(false); break;
            case "ball-materials-on": RenderOptions.AdvancedMaterials = true; break;
            case "ball-materials-off": RenderOptions.AdvancedMaterials = false; break;
            case "ball-damage": Configure("damage",scene,target); break;
            case "ball-freeze": Configure("freeze",scene,target); break;
            case "ball-thaw": Configure("thaw",scene,target); break;
            case "ball-bright": Configure("bright-skin",scene,target); break;
            case "ball-team-orange": case "ball-team-green":
                RenderOptions.BrightSkinStyle = PlayerSkinStyle.HighContrastTextured;
                Configure(name == "ball-team-orange" ? "team-orange" : "team-green",scene,target); break;
            case "ball-double-damage": Configure("double-damage",scene,target); break;
            case "ball-death-respawn": Configure("death-respawn",scene,target); break;
            case "neutral-comparison":
                scene.GameState.Teams = false; target.Recolor = 0; RenderOptions.BrightSkins = false;
                Set(target,"_doubleDmgTimer",(ushort)0); target.ModSetFrozen(false); break;
            case "materials-on": case "materials-restored": RenderOptions.AdvancedMaterials = true; break;
            case "materials-off": RenderOptions.AdvancedMaterials = false; break;
            case "damage": target.TakeDamage(10, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, scene.Players.Items[0]); break;
            case "freeze": target.ModSetFrozen(true); break;
            case "thaw": target.ModSetFrozen(false); break;
            case "bright-skin": RenderOptions.BrightSkins = true; RenderOptions.BrightSkinStyle = PlayerSkinStyle.Textured; break;
            case "team-orange": case "team-green":
                scene.GameState.Teams = true; target.TeamIndex = name == "team-orange" ? 0 : 1; TeamVisuals.Apply(target); break;
            case "double-damage": Set(target, "_doubleDmgTimer", (ushort)180); break;
            case "death-respawn": target.TakeDamage(10000, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, scene.Players.Items[0]); break;
        }
    }
    private static void Follow(Scene scene, PlayerEntity target)
    {
        Vector3 center = target.Position + new Vector3(0,.9f,0);
        Vector3 camera = center + new Vector3(2,1,4);
        // A fixed offset can go through a room wall after a movement/jump case.
        // Choose an unobstructed orbit view, checking the head, torso and feet.
        for (int step = 0; step < 24; step++)
        {
            float angle = MathF.Atan2(4,2) + step * MathF.PI / 12;
            Vector3 candidate = center + new Vector3(3.5f*MathF.Cos(angle),.7f,3.5f*MathF.Sin(angle));
            bool clear = true;
            foreach (float height in new[] { -.8f, 0, .8f })
            {
                CollisionResult hit = default;
                if (CollisionDetection.CheckBetweenPoints(candidate,center+new Vector3(0,height,0),TestFlags.None,scene,ref hit))
                { clear=false; break; }
            }
            if (clear) { camera=candidate; break; }
        }
        Vector3 facing = (center-camera).Normalized();
        Set(scene,"_cameraPosition",camera);
        Set(scene,"_cameraFacing",facing); Set(scene,"_cameraUp",Vector3.UnitY);
        Set(scene,"_cameraFov",MathHelper.DegreesToRadians(55));
    }
    private static void Step(Scene scene, NativeWindow window)
    {
        scene.OnSimulationFrame(); DesktopGraphicsSession.Resize(window);
        scene.OnDrawFrame(); scene.OnRenderFrame(); DesktopGraphicsSession.Present(window); scene.AfterRenderFrame();
    }
    private static void Preview(Scene scene, NativeWindow window, string directory, string label, List<string> failures, Hunter hunter)
    {
        Scene.LauncherPreview = true; Scene.LauncherHunter = hunter; Scene.LauncherSuit = 0;
        Scene.PreviewWanted = true; Scene.PreviewLeft = .1f; Scene.PreviewTop = .1f;
        Scene.PreviewRight = .9f; Scene.PreviewBottom = .9f;
        int drawn = 0;
        var previewFrames = new List<object>();
        for (int frame = 0; frame < 60; frame++)
        {
            // Advance explicitly; do not let accelerated diagnostics depend on wall-clock preview pacing.
            scene.ModStepPreview();
            if (scene.ModDrawPreviewAlone(window.FramebufferSize))
            {
                var packets = Items(scene, "_previewItems").Where(p => p.Type == RenderItemType.Mesh).ToArray();
                var entity = (Mods.Render.HunterPreviewEntity?)typeof(Scene).GetField("_preview",Private)!.GetValue(scene);
                var instance = entity == null ? null : (ModelInstance?)typeof(Mods.Render.HunterPreviewEntity).GetField("_model",Private)!.GetValue(entity);
                bool weightedFrame = instance != null && entity!.Shown == hunter
                    && CharacterModelRuntime.TryGetWeighted(scene,hunter,CharacterModelPart.Biped,instance.Model,out var model)
                    && packets.Length == model.Segments.Count
                    && packets.All(p => p.WeightedSkinning && model.Segments.Any(s => s.ListId == p.ListId));
                if (weightedFrame) drawn++;
                previewFrames.Add(new { frame, weightedFrame, weightedPackets=packets.Count(p => p.WeightedSkinning), nativeMeshPackets=packets.Count(p => !p.WeightedSkinning) });
            }
        }
        if (drawn < 60) failures.Add($"{label}: {drawn}/60 launcher preview frames drawn");
        File.WriteAllText(Path.Combine(directory,label+".json"),JsonSerializer.Serialize(new { hunter=hunter.ToString(), requestedFrames=60, weightedFrames=drawn, fallbackFrames=60-drawn, frames=previewFrames },Json));
        ScreenCapture.SaveWindow(scene,Path.Combine(directory,label+".png"));
        Console.WriteLine($"CHARACTER ACCEPTANCE {label}: {drawn}/60 drawn");
    }
}
#endif
