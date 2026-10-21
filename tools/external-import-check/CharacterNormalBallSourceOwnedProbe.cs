using System.Buffers.Binary;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;
using MphRead.Mods.Render.Characters;
using V = System.Numerics.Vector3;
using V2 = System.Numerics.Vector2;
using V4 = System.Numerics.Vector4;

// Explicit owned-source CPU proof. No actor, graphics context, GPU or media output.
internal static class CharacterNormalBallSourceOwnedProbe
{
    private const string Id = "darksamus-echoes", Stem = Id + ".ball", Profile = "darksamus-echoes-ball";
    private const string GeometrySha = "eca17407d4933e3a1d829bc931de02561321c185d9bbaa3a574538d57d8851c9";
    private const string AncsSha = "46ddcf531037bd4ae3f1949f0ecc7febe6deede70e5d19ddd720e5a7cfa21ad8";
    private const string CmdlSha = "211e3048ab042395eb4f0835e68452e15a4fb8a7dceac15f833a19092e06b950";
    private const string CskrSha = "dce1ee91aff2334520e8cea066d4421d62d82a6d373c13b3bf6d8ed1404fd5ea";
    private const string CinfSha = "08ff767eecccfa572cf9424862ad8a059128eaf035bcc65c8f9faa77fc09cbce";
    private const string ReadySha = "792bb9d335da503cf1fe0eb59c65a01f7c47ee2aebb48385e5d44c77736e470e";
    private static readonly Dictionary<string,string> OldBallPins = new()
    {
        [".ancs-graph.json"] = "5685f7f5eff0efdfe4659e848305ceb7045f94065eab15b90dd26ba256dfbc8b",
        [".ancs.bin"] = AncsSha, [".cinf.bin"] = CinfSha, [".source.glb"] = GeometrySha,
        [".animations.bin"] = "a5e6e99d1b9fee0d9c7c82537811281373159756be58e6c9e76b409f02c38b12",
        [".source.json"] = "7afe18290250fe745579a931a968204f3dd136da123c7ae3f89def8f6c2a924a",
        [".obj"] = "780ee45bc5dfbe27059043e00078a8ecafe69970ec1ba8cde3265b0a7c5c42d0",
        [".motions.json"] = "cbbc830f19ea4a1281e94b3d274ce8892471db0656896005fcbb85527fcc2164",
        [".skeleton.json"] = "292b63dc593f3ece31049dd7c499126899f44d77c24239eb3f479687f22fbb71",
        [".weights.json"] = "5deb2ea870a6cc58e9ba7baec4fc42f70872576df1cab30b180800bab79d4e81"
    };
    private static readonly Dictionary<uint,string> TexturePins = new()
    {
        [0x38785ed6] = "1720ba3856789206f266430b8200da38f6a16c61ac80b673890759cb72bdbf46",
        [0x99e37db0] = "5dade4d6b7ce1bb721f52f53dc85e99f613484c0f0783f508bfb26a142321314",
        [0xfae8e58b] = "91a9721749eee7bc004981babd9fcf4dadae497ef5e86b304138467379236432",
        [0x4926cb2c] = "d3df77d007815e5b2f3e4eca7348a3b411277c7ddb6070f4fc6d801224568bdf"
    };

    internal static int Run(string rvz)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-normal-ball-source-" + Guid.NewGuid().ToString("N")), imports = Path.Combine(temporary, "imports");
        string core = typeof(CharacterModelPack).Assembly.Location, tool = typeof(CharacterNormalBallSourceOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool); var caches = Caches(); uint rng1 = Rng.Rng1, rng2 = Rng.Rng2;
        string? before = null, after = null, failed = null, error = null, errorSha = null; int checks = 0, refusals = 0;
        bool cleanup = false, componentsUnchanged = false, cachesUnchanged = false, admitted = false; object? metadata = null; var cases = new List<string>();
        void Check(bool value, string why) { if (!value) { failed = why; throw new InvalidDataException(why); } checks++; }
        void Reject(Action action, string why)
        {
            try { action(); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or ArgumentException or JsonException) { refusals++; cases.Add(why); return; }
            throw new InvalidDataException("Expected normal source ball refusal: " + why);
        }
        try
        {
            Check(OperatingSystem.IsWindows(), "Exclusive source-file lock acceptance uses this Windows host."); before = HashFile(rvz);
            using var resources = new Resources(rvz);
            RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imports, selectedIds: [Id,"darksamus-overcharged-echoes"]);
            var spec = RetroCharacterPreviewExporter.BundleSpec(Id); var state = LocalImportBundle.Inspect(imports, Id, spec);
            Check(spec.Version == 7 && state.State == ImportBundleState.Ready && state.Receipt != null, "Normal bundle7 is completely committed.");
            var parts = RetroCharacterPreviewExporter.InspectParts(Id, state); var ballPart = parts.Single(p => p.Id == "ball");
            Check(parts.Single(p => p.Id == "body").Version == 5 && ballPart.Version == 4 && ballPart.State == ImportBundleState.Ready
                && ballPart.Components.Count == 11 && ballPart.Components.All(c => c.State == ImportBundleState.Ready), "Normal body5 remains stable while ball4 requires all11 independent authoring roles.");
            string folder = Path.GetDirectoryName(state.Files.Single(f => Path.GetFileName(f) == "characters.json"))!;
            Check(Path.GetFileName(folder) == state.Receipt!.Generation && state.Files.All(f => Path.GetDirectoryName(f) == folder), "Body, cannon and ball companions share the same immutable generation.");
            var componentHashes = state.Files.ToDictionary(f => f, HashFile);
            foreach (var pin in OldBallPins) Check(HashFile(Path.Combine(folder, Stem + pin.Key)) == pin.Value, "Previously qualified raw ball component remains byte-identical: " + pin.Key);
            byte[] ancs = resources.Read(0x654a3f97), cinf = resources.Read(0xc7dbc44a), cmdl = resources.Read(0x2f2c1e6f), cskr = resources.Read(0x4403b1b9), ready = resources.Read(0x3bf596d0);
            Check(Sha(ancs) == AncsSha && Sha(cinf) == CinfSha && Sha(cmdl) == CmdlSha && Sha(cskr) == CskrSha && Sha(ready) == ReadySha, "Original source ball identities retain literal preimplementation pins.");
            var refs = RetroCharacterSource.FindCharacter(ancs, "Dark_Samus_MorphBall");
            Check(refs.Cmdl == 0x2f2c1e6f && refs.Cskr == 0x4403b1b9 && refs.Cinf == 0xc7dbc44a, "Source ball comes from its own ANCS, never body ANCS39900a11.");
            var raw = Cmdl.Load(resources.Read, refs.Cmdl, refs.Cskr); var skeleton = RetroCharacterSource.ReadSkeleton(cinf);
            Check(raw.Version == 4 && raw.PositionCount == 1553 && raw.TriangleCount == 2220 && raw.Materials.Length == 1 && skeleton.Bones.Count == 2
                && skeleton.Bones[0].Parent == 97 && skeleton.Bones[0].Name == "root" && skeleton.Bones[1].Parent == 0 && skeleton.Bones[1].Name == "Skeleton_Root",
                "Whole original ball retains2220 triangles and the complete2bone root0/external97 rig.");
            Check(raw.Skin!.All(g => g.Count(i => i.Weight > 0) == 1 && g.Any(i => i.Bone == 1 && i.Weight == 1)), "All original ball positions are rigidly weighted to source bone1.");
            Check(raw.Materials[0].TexGens.Select(g => g.Src).SequenceEqual(new[] {4,5,6,1}) && raw.Triangles.All(c =>
                c.T0 >= 0 && c.T1 >= 0 && c.T2 >= 0 && raw.Uv0[c.T0*2] == raw.Uv0[c.T1*2] && raw.Uv0[c.T0*2+1] == raw.Uv0[c.T1*2+1]
                && raw.Uv0[c.T0*2] == raw.Uv0[c.T2*2] && raw.Uv0[c.T0*2+1] == raw.Uv0[c.T2*2+1]), "Every original T0/T1/T2 alias is exact before the existing GX UV0 transport is accepted.");
            byte[] archiveBytes = File.ReadAllBytes(Path.Combine(folder, Stem + ".materials.bin")); var archive = RetroCharacterMaterialArchive.Read(archiveBytes);
            Check(BinaryPrimitives.ReadUInt32LittleEndian(archiveBytes.AsSpan(4)) == 1 && BinaryPrimitives.ReadUInt32LittleEndian(archiveBytes.AsSpan(16)) == 7
                && Sha(archiveBytes) == "fadaf0787fa1c9df6b0cf5765db78547953908dc1b24c9b1cba76217c16746b5"
                && archive.Binding.CharacterId == Profile && archive.Binding.Cmdl == refs.Cmdl && archive.Binding.Cskr == refs.Cskr && archive.Binding.Cinf == refs.Cinf
                && archive.Binding.Ancs == 0x654a3f97 && archive.Binding.SourceFingerprint == spec.SourceFingerprint && archive.CmdlSha256 == CmdlSha
                && archive.CskrSha256 == CskrSha && archive.GeometrySha256 == GeometrySha && archive.Programs.Count == 1 && archive.Textures.Count == 4,
                "Closed material profile7 binds exact ownball source resources, geometry and source edition.");
            // Independent big-endian source directory walk; compare original program bytes, not a re-encoded material DTO.
            uint sections = BinaryPrimitives.ReadUInt32BigEndian(cmdl.AsSpan(0x24)); int materialStart = checked((int)((0x2c + sections*4 + 31) & ~31u));
            int materialBytes = checked((int)BinaryPrimitives.ReadUInt32BigEndian(cmdl.AsSpan(0x2c))); var materialSet = cmdl.AsSpan(materialStart, materialBytes);
            int textureCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(materialSet)); int countAt = 4 + textureCount*4;
            int materialCount = checked((int)BinaryPrimitives.ReadUInt32BigEndian(materialSet[countAt..]));
            int firstEnd = checked((int)BinaryPrimitives.ReadUInt32BigEndian(materialSet[(countAt+4)..])), firstStart = countAt+4+materialCount*4;
            Check(materialCount == 1 && archive.MaterialSetSha256 == Sha(materialSet) && archive.RawProgram(0).SequenceEqual(materialSet.Slice(firstStart, firstEnd)),
                "Raw archive material set and sole GX record preserve every original byte including uninterpreted tails.");
            foreach (var pin in TexturePins)
            {
                byte[] source = resources.Read(pin.Key); var texture = archive.Textures.Single(t => t.ResourceId == pin.Key);
                Check(Sha(source) == pin.Value && texture.SourceSha256 == pin.Value && archive.RawTexture(pin.Key).SequenceEqual(source), "Every original TXTR including unused stages and mip bytes survives exactly.");
                var image = RetroTextureSource.DecodeTop(source); var captured = archive.DecodeTexture(pin.Key);
                Check(captured.Pixels.SequenceEqual(image.Pixels) && texture.RgbaSha256 == Sha(image.Pixels) && image.Layout.Format == 10
                    && image.Layout.Width == (pin.Key == 0x4926cb2c ? 64 : 128) && image.Layout.MipCount == (pin.Key == 0x4926cb2c ? 4 : 5), "Owned source mip layout and decoded pixels match the captured archive independently.");
            }
            metadata = new { Id, SourcePart = "ball", Profile, GeometrySha256 = GeometrySha, MaterialArchiveSha256 = Sha(archiveBytes), MaterialSetSha256 = archive.MaterialSetSha256,
                RawProgramSha256 = Sha(archive.RawProgram(0)), TriangleCount = 2220, FullRigJoints = 2, ReadyOrdinal = 1, ReadyResource = "3bf596d0", ReadySha256 = ReadySha,
                TextureResourceIds = TexturePins.Keys.Order().Select(n => n.ToString("x8")), Generation = state.Receipt.Generation };
            Console.WriteLine(JsonSerializer.Serialize(new { Kind = "normal-ball-source-identities", MetadataOutputOnly = true, Profile = metadata }));

            var viewer = CharacterSourcePreviewSelection.Load(Id,CancellationToken.None,imports);
            var selection = CharacterSourcePreviewSelection.LoadForLocalBody(Id, CancellationToken.None, imports); var pack = selection.Pack;
            Check(viewer.HasBall && !viewer.RuntimeBall && selection.HasBall && selection.RuntimeBall && selection.Generation == viewer.Generation,
                "Viewer ball availability is distinct from explicit local actor ball authority.");
            Check(pack.TryResolvePrivate(Hunter.Samus, selection.SelectedAppearance, CharacterModelPart.AlternateForm, 0, out var ball), "Normal selected appearance resolves exact AlternateForm asset.");
            Check(pack.TryResolvePrivate(Hunter.Samus, selection.SelectedAppearance, CharacterModelPart.Biped, 0, out var body)
                && pack.TryCreateSourcePreviewLease(body, out _), "Ball fixture retains genuine ownbody capability.");
            Check(pack.HasSourceBall(ball) && !pack.HasSourceBall(body), "Ball capability belongs to the exact alternate-form asset, never its own biped.");
            Check(pack.TryCreateSourcePreviewLease(body, out var bodyLease) && pack.TryCreateSourcePreviewLease(ball, out _), "Body and ball receive independently issued current leases.");
            Check(pack.TryCreateSourcePreviewLease(ball, out var lease) && lease.SourceCannon == null && !pack.HasSourceCannon(ball)
                && lease.CacheKey != bodyLease.CacheKey && lease.ModelSha256 == GeometrySha && lease.SourceMaterials != null && lease.SourceAnimations != null,
                "Ball has its own fullsource pose/material owner and cache identity without acquiring cannon authority.");
            var ballMaterials = lease.SourceMaterials!;
            Check(!ReferenceEquals(ballMaterials, bodyLease.SourceMaterials) && ballMaterials.Archive.Sha256 == Sha(archiveBytes)
                && ballMaterials.Archive.Binding.CharacterId == Profile && ballMaterials.Programs.Count == 1
                && ballMaterials.Programs[0].DrawPass == CharacterSourceGxProgram.Pass.Opaque && ballMaterials.Programs[0].InitialRegister1 == V4.Zero
                && ballMaterials.Programs[0].PresentationAlphaScale == 1, "Ball uses independent raw opaque GX authority, never body materials/C1 or Final shell alpha.");
            var program = CharacterSourceGxProgram.Compile(raw.Materials[0]); var loadedProgram = ballMaterials.Programs[0];
            Check(program.Vertex120 == loadedProgram.Vertex120 && program.Fragment120 == loadedProgram.Fragment120 && program.Vertex300 == loadedProgram.Vertex300
                && program.Fragment300 == loadedProgram.Fragment300, "All desktop/ES ball program source is compiled from its original raw GX record.");
            Check(lease.SourceAnimations!.RootBoneId == 0 && lease.SourceAnimations.ExternalParentId == 97 && lease.SourceAnimations.Skeleton.Bones.Count == 2
                && lease.SourceAnimations.Data.Roots.Count == 2 && lease.SourceAnimations.Data.Roots.All(r => r.CharacterDeclared && r.StrictPlay), "Ball archive uses its exact two-root/two-bone contract, not body's39rig/79roots.");
            var geometry = CharacterWeightedModelLoader.LoadSourcePreview(ball, lease);
            Check(geometry.SourceRig?.Nodes.Count == 3 && geometry.Joints.Count == 2 && geometry.IndexCount == 2220*3 && geometry.SourceCannon == null
                && geometry.Primitives.All(p => p.SourceMaterialOrdinal == 0) && geometry.Primitives.SelectMany(p => p.SourceTriangleIds!).Order().SequenceEqual(Enumerable.Range(0,2220)),
                "Loaded ball retains complete source hierarchy, whole source IDs and sole original material.");
            foreach (var p in geometry.Primitives)
            for (int t = 0; t < p.SourceTriangleIds!.Length; t++)
            for (int c = 0; c < 3; c++)
            {
                var corner = raw.Triangles[p.SourceTriangleIds[t]*3+c]; var v = p.Vertices[p.Indices[t*3+c]];
                V position = new(-raw.Positions[corner.P*3],raw.Positions[corner.P*3+2],raw.Positions[corner.P*3+1]);
                V normal = V.Normalize(new(-raw.Normals[corner.N*3],raw.Normals[corner.N*3+2],raw.Normals[corner.N*3+1]));
                V2 uv = new(raw.Uv0[corner.T0*2],raw.Uv0[corner.T0*2+1]); int global = p.JointPalette![v.PackedJoints & 31];
                Check(v.Position == position && V.Distance(v.Normal, normal) < .000001f && v.Texcoord == uv && v.Weights == new V4(1,0,0,0)
                    && geometry.SourceRig!.Nodes[geometry.SourceRig.JointNodes[global]].Name == "source_bone_1_Skeleton_Root",
                    "Independent raw source corner preserves converted position/normal/rawUV, winding and exact fulljoint influence.");
            }
            var clip = CharacterSourceClip.Load(lease, ball, geometry.SourceRig!, 1);
            Check(clip.Name == "B_ready_Dark_Samus_MorphBall" && clip.Ordinal == 1 && clip.ResourceId == 0x3bf596d0 && clip.ContentSha256 == ReadySha
                && clip.KeyCount == 2 && clip.Sample(0).Count == 3 && clip.MotionPolicy == "source-raw-v1", "Only exact original rigid Ready1/time0 provides the source ball's fixed pose.");
            var originalReady = Anim.Load(ready, true); originalReady.Sample(0,out var r0,out var t0); originalReady.Sample(originalReady.Duration,out var r1,out var t1);
            Check(r0.All(p => p.Value.SequenceEqual(r1[p.Key])) && t0.All(p => p.Value.SequenceEqual(t1[p.Key])), "Owned ready source clip is genuinely constant, with no invented rolling or root travel.");
            var rawWorld = new Dictionary<uint,System.Numerics.Matrix4x4>();
            System.Numerics.Matrix4x4 OriginalWorld(uint bone)
            {
                if(rawWorld.TryGetValue(bone,out var known)) return known;var b=skeleton.Bones[bone];double[] bind=[b.X,b.Y,b.Z];
                if(skeleton.Bones.TryGetValue(b.Parent,out var parent)){bind[0]-=parent.X;bind[1]-=parent.Y;bind[2]-=parent.Z;}
                var q=r0.GetValueOrDefault(bone);var p=t0.GetValueOrDefault(bone)??bind;
                var basis=new System.Numerics.Matrix4x4(-1,0,0,0,0,0,1,0,0,1,0,0,0,0,0,1);
                var rotation=q==null?System.Numerics.Matrix4x4.Identity:basis*System.Numerics.Matrix4x4.CreateFromQuaternion(new((float)q[0],(float)q[1],(float)q[2],(float)q[3]))*basis;
                var local=rotation*System.Numerics.Matrix4x4.CreateTranslation((float)-p[0],(float)p[2],(float)p[1]);
                return rawWorld[bone]=skeleton.Bones.ContainsKey(b.Parent)?local*OriginalWorld(b.Parent):local;
            }
            var b1=skeleton.Bones[1];var independentSkin=System.Numerics.Matrix4x4.CreateTranslation(b1.X,-b1.Z,-b1.Y)*OriginalWorld(1);
            var posed=Enumerable.Range(0,raw.PositionCount).Select(i=>V.Transform(new V(-raw.Positions[i*3],raw.Positions[i*3+2],raw.Positions[i*3+1]),independentSkin)).ToArray();
            V minimum=new(posed.Min(p=>p.X),posed.Min(p=>p.Y),posed.Min(p=>p.Z)),maximum=new(posed.Max(p=>p.X),posed.Max(p=>p.Y),posed.Max(p=>p.Z));
            V center=(minimum+maximum)/2;float radius=posed.Max(p=>V.Distance(p,center));
            foreach(float nativeRadius in new[]{.25f,.5f,1f})
            {
                var fit=CharacterSourceBallPose.Fit(geometry,clip.Sample(0),nativeRadius);
                var expected=System.Numerics.Matrix4x4.CreateTranslation(-center)*System.Numerics.Matrix4x4.CreateScale(nativeRadius/radius);
                Check(MatrixDifference(fit,expected)<.00002 && Math.Abs(posed.Max(p=>V.Transform(p,ToNumerics(fit)).Length())-nativeRadius)<.00002,
                    "Owned ball sphere fit matches independently posed original CINF/ANIM vertices and exact native radius.");
            }
            int reads = CharacterSourceClip.CapturedFileReads; string held = PoseHash(clip.Sample(0));
            using (var locked = new FileLocks(state.Files)) for (int i = 0; i < 100; i++) Check(PoseHash(clip.Sample(0)) == held, "Frozen source ball pose samples with all generation files exclusively locked.");
            Check(CharacterSourceClip.CapturedFileReads == reads, "No lazy clip/graph/proof IO occurs during fixed ball sampling.");
            Check(!pack.TryCreateSourcePreviewLease(ball with { }, out _), "Copied AlternateForm DTO cannot mint source ball capability.");
            Reject(() => CharacterWeightedModelLoader.LoadSourcePreview(ball,bodyLease), "body-lease-cannot-load-ball"); Reject(() => CharacterWeightedModelLoader.LoadSourcePreview(body,lease), "ball-lease-cannot-load-body");
            foreach (int ordinal in new[] {0,2,32,int.MaxValue}) Reject(() => CharacterSourceClip.Load(lease,ball,geometry.SourceRig!,ordinal), "ball-refuses-nonReady-source-root");
            foreach (double time in new[] {double.Epsilon,-.1,.1,double.NaN,double.PositiveInfinity}) Reject(() => clip.Sample(time), "ball-refuses-nonzero-or-nonfinite-phase");
            Reject(() => ballMaterials.ForFinalCannon(), "ball-cannot-acquire-Final-cannon-alpha-policy");
            var final = CharacterSourcePreviewSelection.LoadForLocalBody("darksamus-overcharged-echoes",CancellationToken.None,imports);
            Check(!final.HasBall && !final.RuntimeBall && !final.Pack.TryResolvePrivate(Hunter.Samus,final.SelectedAppearance,CharacterModelPart.AlternateForm,0,out _), "Final has no borrowed normal source ball capability.");

            // Negative proof fixtures use only a disposable copy, never the committed generation.
            string mutable = Path.Combine(temporary,"mutable"); Directory.CreateDirectory(mutable); foreach (string f in state.Files) File.Copy(f,Path.Combine(mutable,Path.GetFileName(f)));
            byte[] manifest = File.ReadAllBytes(Path.Combine(mutable,"characters.json"));
            void Load()
            {
                var p = CharacterModelPack.Load(mutable);
                if (!p.TryResolvePrivate(Hunter.Samus,selection.SelectedAppearance,CharacterModelPart.AlternateForm,0,out var asset)) throw new InvalidDataException("Exact ball part required.");
                p.CaptureSourceBallMaterials(asset);
                if (!p.TryCreateSourcePreviewLease(asset,out var proof)) throw new InvalidDataException("Exact ball lease required.");
                var model = CharacterWeightedModelLoader.LoadSourcePreview(asset,proof); _ = CharacterSourceClip.Load(proof,asset,model.SourceRig!,1).Sample(0);
            }
            Load();
            void Manifest(string label, Action<JsonObject> edit)
            {
                var doc = JsonNode.Parse(manifest)!.AsObject(); var entry = doc["models"]!.AsArray().Single(n => n!["part"]!.GetValue<string>() == "AlternateForm")!.AsObject();
                try { edit(entry); File.WriteAllText(Path.Combine(mutable,"characters.json"),doc.ToJsonString()); Reject(Load,label); }
                finally { File.WriteAllBytes(Path.Combine(mutable,"characters.json"),manifest); }
            }
            Manifest("wrong-ball-Hunter",e=>e["hunter"]="Weavel"); Manifest("wrong-ball-part",e=>e["part"]="ViewModel");
            Manifest("wrong-ball-appearance",e=>e["skinSource"]="MP2DarkSamusFinal"); Manifest("wrong-ball-lod",e=>e["lod"]=1);
            Manifest("ball-cannot-alias-body-geometry",e=>e["model"]=Id+".source.glb"); Manifest("ball-cannot-alias-body-animation-bank",e=>e["sourceAnimationArchive"]=Id+".animations.bin");
            foreach (string suffix in new[] {".source.glb",".source.json",".cinf.bin",".ancs.bin",".ancs-graph.json",".animations.bin",".materials.bin"})
            {
                string path = Path.Combine(mutable,Stem+suffix); byte[] saved = File.ReadAllBytes(path), changed = (byte[])saved.Clone(); changed[^1] ^= 1;
                try { File.WriteAllBytes(path,changed); Reject(Load,"changed-ball-"+suffix); } finally { File.WriteAllBytes(path,saved); }
                try { File.Delete(path); Reject(Load,"missing-ball-"+suffix); } finally { File.WriteAllBytes(path,saved); }
            }
            string materialsPath = Path.Combine(mutable,Stem+".materials.bin"); byte[] own = File.ReadAllBytes(materialsPath);
            try { File.Copy(Path.Combine(folder,Id+".materials.bin"),materialsPath,true); Reject(Load,"body-materials-cannot-substitute-ball"); } finally { File.WriteAllBytes(materialsPath,own); }
            Load(); componentsUnchanged = componentHashes.All(p=>HashFile(p.Key)==p.Value); Check(componentsUnchanged,"All committed body/cannon/ball components remain byte-identical after isolated proof tests."); admitted = true;
        }
        catch (Exception ex) { error = ex.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(ex.Message)); Console.Error.WriteLine("Normal ball source CPU failure: "+ex); }
        finally
        {
            try { if (Directory.Exists(temporary)) Directory.Delete(temporary,true); cleanup = !Directory.Exists(temporary); } catch(Exception ex) { error ??= ex.GetType().Name; }
            try { after = HashFile(rvz); } catch(Exception ex) { error ??= ex.GetType().Name; }
            var final = Caches(); cachesUnchanged = caches.All(p=>final[p.Key].SequenceEqual(p.Value));
        }
        bool rngUnchanged = rng1==Rng.Rng1&&rng2==Rng.Rng2;
        bool passed = error==null&&admitted&&componentsUnchanged&&cleanup&&before==after&&coreBefore==HashFile(core)&&toolBefore==HashFile(tool)&&cachesUnchanged&&rngUnchanged;
        Console.WriteLine(JsonSerializer.Serialize(new {Passed=passed,Checks=checks,Refusals=refusals,FailedCheck=failed,ErrorType=error,ErrorMessageSha256=errorSha,
            CpuOnly=true,RenderingActivated=false,GameplayActivated=false,FinalBallQualified=false,Profile=metadata,RefusalCases=cases,
            SourceSha256Before=before,SourceSha256After=after,CoreSha256=coreBefore,ToolSha256=toolBefore,ComponentsUnchanged=componentsUnchanged,
            CachesUnchanged=cachesUnchanged,NativeRngUnchanged=rngUnchanged,TemporaryDerivedPayloadsDeleted=cleanup}));
        return passed?0:1;
    }

    private sealed class Resources : IDisposable
    {
        private readonly GameCubeDiscReader _disc; private readonly List<RetroPakReader> _paks=new();
        internal Resources(string path)
        {
            _disc=GameCubeDiscReader.Open(path);
            try
            {
                if(_disc.GameId!="G2ME01"||_disc.Revision!=0||_disc.DiscNumber!=0||ExternalSourceRequirements.VerifyKnown(_disc).Any(r=>r.Status!=ExternalArchiveStatus.Verified)) throw new InvalidDataException("Exact verified Echoes source required.");
                foreach(var r in ExternalSourceRequirements.For(_disc)) _paks.Add(new(_disc.OpenRead(r.Path),leaveOpen:false));
            }
            catch { Dispose(); throw; }
        }
        internal byte[] Read(uint id)=>_paks.First(p=>p.Contains(id)).ReadResource(id);
        public void Dispose(){foreach(var p in _paks)p.Dispose();_disc.Dispose();}
    }
    private sealed class FileLocks : IDisposable
    {
        private readonly List<FileStream> _files=new();
        internal FileLocks(IEnumerable<string> paths){try{foreach(string p in paths)_files.Add(new(p,FileMode.Open,FileAccess.Read,FileShare.None));}catch{Dispose();throw;}}
        public void Dispose(){foreach(var f in _files)f.Dispose();}
    }
    private static Dictionary<string,object[]> Caches()=>new[]{"_modelCache","_fhModelCache","_retiredRoomModels","_effects","_particleDefs"}.ToDictionary(n=>n,n=>((IEnumerable)typeof(Read).GetField(n,BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().ToArray());
    private static string PoseHash(IReadOnlyList<OpenTK.Mathematics.Matrix4> matrices)
    {
        using var stream=new MemoryStream();using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)) foreach(var m in matrices) foreach(float v in new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44}) writer.Write(v);
        return Sha(stream.ToArray());
    }
    private static string HashFile(string path){using var f=File.OpenRead(path);return Convert.ToHexStringLower(SHA256.HashData(f));}
    private static System.Numerics.Matrix4x4 ToNumerics(OpenTK.Mathematics.Matrix4 m)=>new(m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44);
    private static double MatrixDifference(OpenTK.Mathematics.Matrix4 actual,System.Numerics.Matrix4x4 expected)
    {
        var a=ToNumerics(actual);
        return new[]{a.M11-expected.M11,a.M12-expected.M12,a.M13-expected.M13,a.M14-expected.M14,a.M21-expected.M21,a.M22-expected.M22,a.M23-expected.M23,a.M24-expected.M24,
            a.M31-expected.M31,a.M32-expected.M32,a.M33-expected.M33,a.M34-expected.M34,a.M41-expected.M41,a.M42-expected.M42,a.M43-expected.M43,a.M44-expected.M44}.Max(v=>Math.Abs(v));
    }
    private static string Sha(ReadOnlySpan<byte> bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
}
