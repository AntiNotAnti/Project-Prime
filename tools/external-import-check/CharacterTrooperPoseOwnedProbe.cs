using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Import;
using MphRead.Mods.Render.Characters;
using M = OpenTK.Mathematics.Matrix4;
using N = System.Numerics.Matrix4x4;
using Q = System.Numerics.Quaternion;
using V = System.Numerics.Vector3;

// Explicit CPU-only owned-source pose acceptance. The oracle reads raw bank
// poses/CINF and uses independent System.Numerics composition. No game payload,
// captured image, graphics context, actor, gameplay or native save is produced.
internal static class CharacterTrooperPoseOwnedProbe
{
    private static readonly string[] Ids = ["trooper-power", "trooper-ice", "trooper-wave", "trooper-plasma"];
    private static readonly Dictionary<PlayerAnimation, string> Lower = new()
    {
        [PlayerAnimation.Idle] = "B_ready_pirate", [PlayerAnimation.Turn] = "B_ready_pirate",
        [PlayerAnimation.Shoot] = "B_ready_pirate", [PlayerAnimation.Charge] = "B_ready_pirate", [PlayerAnimation.ChargeShoot] = "B_ready_pirate",
        [PlayerAnimation.WalkForward] = "B_runloop_pirate", [PlayerAnimation.WalkBackward] = "B_backward_pirate",
        [PlayerAnimation.WalkLeft] = "B_strafleft_pirate", [PlayerAnimation.WalkRight] = "B_strafright_pirate"
    };
    private static readonly Dictionary<PlayerAnimation, string> Upper = new()
    {
        [PlayerAnimation.Shoot] = "A_standshot_pirate", [PlayerAnimation.ChargeShoot] = "A_standshot_pirate",
        [PlayerAnimation.Charge] = "A_gunloop_pirate"
    };
    private static readonly string[] Required = ["B_ready_pirate", "B_runloop_pirate", "B_backward_pirate", "B_strafleft_pirate", "B_strafright_pirate", "A_standshot_pirate", "A_gunloop_pirate"];
    private static int _linearBlends, _sphericalBlends;
    private sealed record Captured(string Id, CharacterSourceRig Rig, RetroNativeSkeleton Skeleton,
        Dictionary<string, CharacterSourceClip> Clips, Dictionary<string, OracleClip> Oracle);

    internal static int Run(string rvz)
    {
        _linearBlends = _sphericalBlends = 0;
        string temp = Path.Combine(Path.GetTempPath(), "prime-trooper-pose-" + Guid.NewGuid().ToString("N")), imports = Path.Combine(temp, "imports");
        string core = typeof(CharacterSourceClip).Assembly.Location, tool = typeof(CharacterTrooperPoseOwnedProbe).Assembly.Location;
        string coreBefore = HashFile(core), toolBefore = HashFile(tool); var caches = Caches();
        string? before = null, after = null, failed = null, error = null, errorSha = null;
        bool cleanup = false, filesUnchanged = false, cacheUnchanged = false, rngUnchanged = false;
        int checks = 0, refusals = 0, lockedFiles = 0, constructorCount = 0, poseSamples = 0;
        int initialReads = 0, finalReads = 0; long? proofBefore = null, proofAfter = null;
        int numericalBranches = 0; double maximumNumericalBranchRadians = 0, maximumUnadjustedTransitionDifference = 0, maximumNumericalEndpointDifference = 0;
        var numericalBranchNodes = new HashSet<string>(StringComparer.Ordinal);
        double maxError = 0; var observations = new List<object>(); var captures = new List<Captured>();
        Dictionary<string, string>? components = null;
        void Check(bool condition, string why) { if (!condition) { failed = why; throw new InvalidDataException(why); } checks++; }
        void Reject(Action action, string why)
        {
            try { action(); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or NotSupportedException) { refusals++; return; }
            failed = why; throw new InvalidDataException("Expected refusal: " + why);
        }
        void Equal(IReadOnlyList<M> actual, IReadOnlyList<N> expected, string why)
        {
            double difference = Distance(actual, expected); maxError = Math.Max(maxError, difference); poseSamples++;
            if (difference >= .0004)
            {
                int worst = Enumerable.Range(0, Math.Min(actual.Count, expected.Count)).MaxBy(i => Distance(ToN(actual[i]), expected[i]));
                Console.WriteLine(JsonSerializer.Serialize(new { Diagnostic = "pose-matrix-difference", Why = why, WorstNode = worst,
                    Difference = difference, ActualDeterminant = ToN(actual[worst]).GetDeterminant(), ExpectedDeterminant = expected[worst].GetDeterminant() }));
            }
            Check(actual.Count == 36 && difference < .0004, why);
        }
        try
        {
            Check(OperatingSystem.IsWindows(), "Owned pose no-IO file-lock acceptance requires Windows.");
            before = HashFile(rvz);
            using (var disc = GameCubeDiscReader.Open(rvz))
                Check(disc.GameId == "GM8E01" && disc.Revision == 2 && disc.DiscNumber == 0
                    && ExternalSourceRequirements.VerifyKnown(disc).All(r => r.Status == ExternalArchiveStatus.Verified), "Exact owned Prime USA Rev2 source and both pinned archives verify.");
            var exported = RetroCharacterPreviewExporter.ExportFromOwnedGame(rvz, imports, selectedIds: Ids);
            Check(exported.Count == 4, "All four private source appearances export into this disposable tree.");
            foreach (string id in Ids)
            {
                var selection = CharacterSourcePreviewSelection.Load(id, CancellationToken.None, imports);
                Check(selection.NativeHunter == Hunter.Weavel && selection.HasCannon && !selection.RuntimeCannon && selection.Clips.Count == 113,
                    "Owned pose capture uses the exact Weavel source profile with cannon asset capability and no runtime gun activation.");
                Check(selection.Pack.TryResolvePrivate(Hunter.Weavel, selection.SelectedAppearance, CharacterModelPart.Biped, 0, out var asset)
                    && selection.Pack.TryCreateSourcePreviewLease(asset, out _), "Actual private source asset and lease resolve.");
                Check(selection.Pack.TryCreateSourcePreviewLease(asset, out var lease), "Exact source pose lease captured.");
                var geometry = CharacterWeightedModelLoader.LoadSourcePreview(asset, lease); var rig = geometry.SourceRig!;
                var bank = lease.SourceAnimations!;
                Check(geometry.Joints.Count == 35 && rig.Nodes.Count == 36 && bank.RootBoneId == 3 && bank.ExternalParentId == 2,
                    "Owned source pose preserves full35 rig and root3/external-parent2 contract.");
                var clips = new Dictionary<string, CharacterSourceClip>(StringComparer.Ordinal); var oracle = new Dictionary<string, OracleClip>(StringComparer.Ordinal);
                foreach (string name in Required)
                {
                    int ordinal = selection.Clips.Single(c => c.Name == name).Ordinal;
                    var raw = bank.Data.OpenStrictPrimitive(ordinal); var clip = CharacterSourceClip.Load(lease, asset, rig, ordinal);
                    Check(clip.MotionPolicy == "source-raw-v1" && clip.ResourceId == raw.Source.Id && clip.ContentSha256 == raw.Source.Sha256
                        && clip.KeyCount == raw.KeyCount && clip.TickSeconds == raw.Tick, "Raw source clip identity/timing/policy remain exact before actor derivation.");
                    clips.Add(name, clip); oracle.Add(name, new(raw, bank.Skeleton, rig));
                }
                captures.Add(new(id, rig, bank.Skeleton, clips, oracle));
            }
            Check(CharacterSourceTrooperPose.RequiredClips.SequenceEqual(Required), "Required source clip names/order are exact, including B_strafright_pirate.");
            var snapshot = Files(imports); components = snapshot; var locks = new List<FileStream>();
            try
            {
                foreach (string path in Directory.EnumerateFiles(imports, "*", SearchOption.AllDirectories))
                    locks.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));
                lockedFiles = locks.Count; initialReads = CharacterSourceClip.CapturedFileReads; proofBefore = ProofReads();
                uint rng1 = Rng.Rng1, rng2 = Rng.Rng2;
                foreach (var captured in captures)
                {
                    string rawBefore = RawHash(captured.Clips); var rigBefore = PoseHash(captured.Rig.Nodes.Select(n => n.Local).ToArray());
                    var states = Enum.GetValues<PlayerAnimation>().Concat(new[] { (PlayerAnimation)(-128), (PlayerAnimation)127 }).Distinct().ToArray();
                    foreach (var lower in states)
                    {
                        Lower.TryGetValue(lower, out string? expectedLower); Upper.TryGetValue(lower, out string? expectedUpper);
                        Check(CharacterSourceTrooperPose.LowerClip(lower) == expectedLower && CharacterSourceTrooperPose.UpperClip(lower) == expectedUpper,
                            "Pure lower/upper mapping admits only the closed donor grounded clip table.");
                        foreach (var upper in states)
                            Check(CharacterSourceTrooperPose.Supports(lower, upper) == (Lower.ContainsKey(lower) && Lower.ContainsKey(upper)),
                                "Every known/unknown lower-upper state pair has exact complete-pose admission.");
                    }
                    CharacterSourceTrooperPose New() { constructorCount++; return new(captured.Rig, captured.Clips); }
                    foreach (var entry in captured.Clips)
                    {
                        var pinned = entry.Value.WithPinnedTrooperRootMotion(); var reference = captured.Oracle[entry.Key];
                        Check(pinned.MotionPolicy == "trooper-pinned-root-pelvis-v1" && entry.Value.MotionPolicy == "source-raw-v1"
                            && pinned.ResourceId == entry.Value.ResourceId && pinned.ContentSha256 == entry.Value.ContentSha256,
                            "Private pinned derivation retains source identity while leaving the raw viewer clip policy unchanged.");
                        Reject(() => pinned.WithPinnedTrooperRootMotion(), "A pinned clip cannot be derived and filtered twice.");
                        V sum = V.Zero; V? first = null, last = null;
                        for (int key = 0; key < entry.Value.KeyCount; key++)
                        {
                            double t = key * entry.Value.TickSeconds; var actual = pinned.Sample(t); var expected = reference.Sample(t);
                            Equal(actual, expected, "Every derived temporal key matches independent raw-bank RootPin and full-rig composition.");
                            V at = ToN(actual[reference.Pelvis]).Translation - reference.PelvisBind;
                            sum += new V(at.X, 0, at.Z); first ??= at; last = at;
                            Check(Distance(ToN(actual[reference.Root]), reference.RootBind) < .00002,
                                "Pinned root rotation and translation equal source bind at every key.");
                            Check(Math.Abs(ToN(actual[reference.Pelvis]).Translation.Y - expected[reference.Pelvis].Translation.Y) < .00002,
                                "Independent pinning preserves the original sampled pelvis height.");
                        }
                        Check((sum / entry.Value.KeyCount).Length() < .00002
                            && new V(last!.Value.X - first!.Value.X, 0, last.Value.Z - first.Value.Z).Length() < .00002,
                            "Actual pinned pelvis horizontal sway has zero mean and no first-to-last travel.");
                        foreach (double t in new[] { 0d, entry.Value.Duration * .173, entry.Value.Duration * .537, entry.Value.Duration })
                            Equal(pinned.Sample(t), reference.Sample(t), "Pinned off-grid/end samples match the independently filtered temporal channels.");
                    }
                    foreach (var lower in Lower.Keys)
                    foreach (var upper in Lower.Keys)
                    {
                        ICharacterSourceBodyPose pose = New();
                        Check(pose.Supports(lower, upper), "Shared body-pose interface exposes the same grounded admission contract.");
                        for (int tick = 0; tick < 65; tick++)
                        {
                            float aim = tick % 3 == 0 ? 0 : tick % 3 == 1 ? .9f : -.9f;
                            Equal(pose.Sample(lower, tick, upper, tick, aim), Expected(captured, lower, tick / 60d, upper, tick / 60d, aim),
                                "Every supported state pair follows independently composed lower, looped upper, full torso mask and clamped once-only aim.");
                        }
                    }
                    var invalid = New();
                    foreach (var pair in new[] { (PlayerAnimation.LandNeutral, PlayerAnimation.Idle), (PlayerAnimation.Idle, PlayerAnimation.Spawn),
                        (PlayerAnimation.JumpForward, PlayerAnimation.Idle), (PlayerAnimation.Morph, PlayerAnimation.Idle), ((PlayerAnimation)127, PlayerAnimation.Idle) })
                        Reject(() => invalid.Sample(pair.Item1, 0, pair.Item2, 0, 0), "Unsupported source state refuses a partial body pose.");
                    Reject(() => invalid.Sample(PlayerAnimation.Idle, -1, PlayerAnimation.Idle, 0, 0), "Negative lower frame refuses.");
                    Reject(() => invalid.Sample(PlayerAnimation.Idle, 0, PlayerAnimation.Idle, -1, 0), "Negative upper frame refuses.");
                    foreach (float aim in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                        Reject(() => invalid.Sample(PlayerAnimation.Idle, 0, PlayerAnimation.Idle, 0, aim), "Nonfinite aim refuses.");
                    Transition(PlayerAnimation.WalkForward, PlayerAnimation.Idle, PlayerAnimation.WalkLeft, PlayerAnimation.Charge, false, false);
                    Transition(PlayerAnimation.Idle, PlayerAnimation.Shoot, PlayerAnimation.Turn, PlayerAnimation.ChargeShoot, false, false);
                    Transition(PlayerAnimation.WalkForward, PlayerAnimation.Charge, PlayerAnimation.WalkForward, PlayerAnimation.Charge, true, false);
                    Transition(PlayerAnimation.WalkBackward, PlayerAnimation.Shoot, PlayerAnimation.WalkBackward, PlayerAnimation.Shoot, false, true);
                    void Transition(PlayerAnimation oldLower, PlayerAnimation oldUpper, PlayerAnimation newLower, PlayerAnimation newUpper, bool restartLower, bool restartUpper)
                    {
                        var pose = New(); for (int k = 0; k < 12; k++) pose.Sample(oldLower, k, oldUpper, k, .6f);
                        N[] prior = Expected(captured, oldLower, 11 / 60d, oldUpper, 11 / 60d, 0);
                        var priorActual = ((M[])typeof(CharacterSourceTrooperPose).GetField("_last", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pose)!).ToArray();
                        for (int k = 0; k < 8; k++)
                        {
                            bool lowerReset = restartLower || oldLower != newLower, upperReset = restartUpper || oldUpper != newUpper;
                            int lf = lowerReset ? k : 12 + k, uf = upperReset ? k : 12 + k;
                            N[] target = Expected(captured, newLower, (lowerReset ? k : 12 + k) / 60d, newUpper, (upperReset ? k : 12 + k) / 60d, 0);
                            N[] expected = k < 6 ? Mix(prior, target, (k + 1) / 6f) : target;
                            float aim = k % 2 == 0 ? -.8f : .8f;
                            var actual = pose.Sample(newLower, lf, newUpper, uf, aim);
                            if (k < 6)
                            {
                                var history = (M[])typeof(CharacterSourceTrooperPose).GetField("_last", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pose)!;
                                var from = (M[])typeof(CharacterSourceTrooperPose).GetField("_from", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pose)!;
                                var privateClips = (IReadOnlyDictionary<string, CharacterSourceClip>)typeof(CharacterSourceTrooperPose).GetField("_clips", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(pose)!;
                                var observedTarget = ObservedTarget(captured.Rig, privateClips, newLower, (lowerReset ? k : 12 + k) / 60d,
                                    newUpper, (upperReset ? k : 12 + k) / 60d);
                                foreach (int node in Enumerable.Range(0, expected.Length).Where(i => Distance(ToN(history[i]), expected[i]) >= .0004))
                                {
                                    Console.WriteLine(JsonSerializer.Serialize(new { Diagnostic = "pose-nearidentity-candidate", oldLower, oldUpper, newLower, newUpper,
                                        restartLower, restartUpper, Tick = k, Node = captured.Rig.Nodes[node].Name, Difference = Distance(ToN(history[node]), expected[node]) }));
                                    // Shipped OpenTK Slerp returns endpoint A when its float dot rounds to 1.
                                    // The independent raw-bank endpoint can differ by one ULP and fall below 1.
                                    // Admit only this proven branch in a six-tick local blend, with strict
                                    // independent endpoints/translation and <=0.0008 rad (0.046 degrees) error.
                                    M endpoint = observedTarget[node];
                                    var ar = from[node].ExtractRotation(); var br = endpoint.ExtractRotation();
                                    float dot = ar.W * br.W + (ar.X * br.X + ar.Y * br.Y + ar.Z * br.Z);
                                    var branch = ToN(M.CreateFromQuaternion(ar));
                                    branch.Translation = V.Lerp(prior[node].Translation, target[node].Translation, (k + 1) / 6f);
                                    var runtimeBlend = M.CreateFromQuaternion(OpenTK.Mathematics.Quaternion.Slerp(ar, br, (k + 1) / 6f));
                                    runtimeBlend.Row3.Xyz = OpenTK.Mathematics.Vector3.Lerp(from[node].Row3.Xyz, endpoint.Row3.Xyz, (k + 1) / 6f);
                                    double angle = RotationAngle(branch, expected[node]);
                                    Check(dot >= 1 && Distance(ToN(from[node]), prior[node]) < .000002
                                        && Distance(ToN(endpoint), target[node]) < .000002
                                        && Distance(ToN(priorActual[node]), prior[node]) < .000002
                                        && Distance(ToN(history[node]), branch) < .000002
                                        && Distance(ToN(history[node]), ToN(runtimeBlend)) < .000002
                                        && V.Distance(ToN(history[node]).Translation, expected[node].Translation) < .000002
                                        && angle <= .0008,
                                        "Nearidentity OpenTK endpoint-return branch is independently bounded without relaxing raw, steady or other transition poses.");
                                    numericalBranches++; maximumNumericalBranchRadians = Math.Max(maximumNumericalBranchRadians, angle);
                                    numericalBranchNodes.Add(captured.Rig.Nodes[node].Name);
                                    maximumNumericalEndpointDifference = Math.Max(maximumNumericalEndpointDifference,
                                        Math.Max(Distance(ToN(from[node]), prior[node]), Distance(ToN(endpoint), target[node])));
                                    maximumUnadjustedTransitionDifference = Math.Max(maximumUnadjustedTransitionDifference, Distance(ToN(history[node]), expected[node]));
                                    expected[node] = branch;
                                }
                            }
                            ApplyAim(captured.Rig, expected, aim);
                            Equal(actual, expected,
                                "State/alias/frame-restart transition uses exactly six ticks of unaimed local history before applying current pitch once.");
                        }
                        pose.Reset(); var fresh = New();
                        for (int k = 0; k < 5; k++)
                            Check(PoseHash(pose.Sample(newLower, k, newUpper, k, .2f)) == PoseHash(fresh.Sample(newLower, k, newUpper, k, .2f)),
                                "Reset clears time, restart and crossfade history exactly to a fresh owner.");
                    }
                    var paused = New(); var control = New(); M[]? held = null;
                    for (int k = 0; k < 7; k++) { held = paused.Sample(PlayerAnimation.WalkRight, k, PlayerAnimation.Shoot, k, .3f); control.Sample(PlayerAnimation.WalkRight, k, PlayerAnimation.Shoot, k, .3f); }
                    string heldHash = PoseHash(held!);
                    for (int k = 0; k < 1000; k++) _ = ((ICharacterSourceBodyPose)paused).Supports(PlayerAnimation.Idle, PlayerAnimation.Charge);
                    Check(PoseHash(held!) == heldHash && PoseHash(paused.Sample(PlayerAnimation.WalkRight, 7, PlayerAnimation.Shoot, 7, .3f))
                        == PoseHash(control.Sample(PlayerAnimation.WalkRight, 7, PlayerAnimation.Shoot, 7, .3f)),
                        "Omitted simulation samples and repeated Supports calls do not advance held pose or the next tick (CPU pause-equivalent scope).");
                    var detached = New(); var detachedControl = New();
                    for (int k = 0; k < 8; k++)
                    {
                        var result = detached.Sample(PlayerAnimation.Idle, k, PlayerAnimation.Idle, k, .4f);
                        detachedControl.Sample(PlayerAnimation.Idle, k, PlayerAnimation.Idle, k, .4f);
                        result[0] = M.CreateTranslation(100, 100, 100);
                    }
                    Check(PoseHash(detached.Sample(PlayerAnimation.WalkBackward, 0, PlayerAnimation.Charge, 0, -.4f))
                        == PoseHash(detachedControl.Sample(PlayerAnimation.WalkBackward, 0, PlayerAnimation.Charge, 0, -.4f)),
                        "Caller mutation of returned matrices cannot alter later private crossfade history.");
                    var one = New(); var two = New(); var referenceOne = New(); var referenceTwo = New();
                    for (int k = 0; k < 40; k++)
                    {
                        string first = PoseHash(one.Sample(PlayerAnimation.WalkForward, k, PlayerAnimation.Charge, k, .5f));
                        for (int j = 0; j < 2; j++)
                            Check(PoseHash(two.Sample(PlayerAnimation.WalkBackward, k * 2 + j, PlayerAnimation.Shoot, k * 2 + j, -.5f))
                                == PoseHash(referenceTwo.Sample(PlayerAnimation.WalkBackward, k * 2 + j, PlayerAnimation.Shoot, k * 2 + j, -.5f)), "Second owner advances independently without shared pose or pin buffers.");
                        Check(first == PoseHash(referenceOne.Sample(PlayerAnimation.WalkForward, k, PlayerAnimation.Charge, k, .5f)),
                            "First owner remains exact despite interleaved extra samples on another owner.");
                    }
                    Check(RawHash(captured.Clips) == rawBefore && PoseHash(captured.Rig.Nodes.Select(n => n.Local).ToArray()) == rigBefore,
                        "All derived owners preserve every raw clip sample and shared source bind matrix.");
                    observations.Add(new { captured.Id, RequiredClips = Required.Length, StatePairs = Lower.Count * Lower.Count,
                        FramesPerPair = 65, PinnedSourceKeys = captured.Clips.Values.Sum(c => c.KeyCount),
                        IndependentFullRig = true, PrivateMotionPolicy = "trooper-pinned-root-pelvis-v1", RawMotionPolicy = "source-raw-v1" });
                }
                finalReads = CharacterSourceClip.CapturedFileReads; proofAfter = ProofReads();
                Check(initialReads == finalReads && proofBefore == proofAfter,
                    "Constructing, resetting, querying and sampling all source pose owners performs no lazy captured/proof IO while every imported file is exclusively locked.");
                rngUnchanged = Rng.Rng1 == rng1 && Rng.Rng2 == rng2;
                Check(rngUnchanged, "Pure source-pose construction and sampling never consume native RNG state.");
            }
            finally { foreach (var handle in locks) handle.Dispose(); }
            filesUnchanged = Same(snapshot, Files(imports)); Check(filesUnchanged, "All imported source component/receipt bytes remain unchanged after CPU pose acceptance.");
        }
        catch (Exception ex) { error = ex.GetType().Name; errorSha = Sha(Encoding.UTF8.GetBytes(ex.Message)); Console.Error.WriteLine("TROOPER POSE failure: " + ex); }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); cleanup = !Directory.Exists(temp); }
            catch (Exception ex) { error ??= ex.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(ex.Message)); }
            try { after = HashFile(rvz); } catch (Exception ex) { error ??= ex.GetType().Name; errorSha ??= Sha(Encoding.UTF8.GetBytes(ex.Message)); }
            var finalCaches = Caches(); cacheUnchanged = caches.All(p => finalCaches[p.Key].SequenceEqual(p.Value));
        }
        bool passed = error == null && before == after && coreBefore == HashFile(core) && toolBefore == HashFile(tool)
            && cleanup && filesUnchanged && cacheUnchanged && rngUnchanged && observations.Count == 4;
        Console.WriteLine(JsonSerializer.Serialize(new { Passed = passed, Checks = checks, Refusals = refusals, FailedCheck = failed, ErrorType = error, ErrorMessageSha256 = errorSha,
            CpuOnly = true, GpuRenderingActivated = false, ActorGameplayQualified = false, PhysicalInputQualified = false,
            Profiles = observations, PoseComparisons = poseSamples, MaximumMatrixDifference = maxError, Constructors = constructorCount,
            QuaternionBlendPolicy = "Independent normalized-linear branch at absdot>=0.99, matching the shipped OpenTK4.9.4 Slerp numerical contract; spherical otherwise",
            NearParallelQuaternionBlends = _linearBlends, SphericalQuaternionBlends = _sphericalBlends,
            NearIdentityEndpointReturnBranches = numericalBranches, MaximumEndpointReturnErrorRadians = maximumNumericalBranchRadians,
            NearIdentityEndpointReturnNodes = numericalBranchNodes.OrderBy(n => n, StringComparer.Ordinal),
            MaximumEndpointReturnOracleEndpointDifference = maximumNumericalEndpointDifference,
            MaximumUnadjustedTransitionDifference = maximumUnadjustedTransitionDifference,
            EndpointReturnScope = "Only six-tick local nearidentity OpenTK dot>=1 early return; independent endpoints/translation <2e-6, angle<=0.0008rad; all raw/steady/other poses retain matrix<0.0004",
            ExclusivelyLockedImportedFiles = lockedFiles, CapturedFileReadsBefore = initialReads, CapturedFileReadsAfter = finalReads,
            ProofReadsBefore = proofBefore, ProofReadsAfter = proofAfter, ImportedComponentsUnchanged = filesUnchanged,
            ImportedComponentHashes = components?.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { FileId = p.Key, Sha256 = p.Value }),
            SourceSha256Before = before, SourceSha256After = after, SourceUnchanged = before != null && before == after,
            CoreSha256 = coreBefore, ToolSha256 = toolBefore, CachesUnchanged = cacheUnchanged, NativeRngUnchanged = rngUnchanged, TemporaryDerivedPayloadsDeleted = cleanup,
            PauseScope = "CPU call inventory only: no Sample while paused; actual Scene scheduling belongs to the actor probe" }));
        return passed ? 0 : 1;
    }

    // Independent raw-bank key filter: source XY becomes presentation XZ.
    // Runtime channels stay raw; this oracle owns separate doubles/matrices.
    private sealed class OracleClip
    {
        private readonly RetroCharacterAnimationArchive.Clip _raw; private readonly RetroNativeSkeleton _skeleton;
        private readonly CharacterSourceRig _rig; private readonly double[][] _pelvis; private readonly Dictionary<uint, int> _nodes;
        internal int Root { get; } internal int Pelvis { get; } internal N RootBind { get; } internal V PelvisBind { get; }
        internal double Duration => _raw.Duration;
        internal OracleClip(RetroCharacterAnimationArchive.Clip raw, RetroNativeSkeleton skeleton, CharacterSourceRig rig)
        {
            (_raw, _skeleton, _rig) = (raw, skeleton, rig);
            _nodes = skeleton.Bones.Values.ToDictionary(b => b.Id, b => Enumerable.Range(0, rig.Nodes.Count).Single(i => rig.Nodes[i].Name == $"source_bone_{b.Id}_{b.Name}"));
            Root = _nodes[3]; Pelvis = _nodes[4]; double[] rb = Bind(3), pb = Bind(4); RootBind = N.CreateTranslation((float)-rb[0], (float)rb[2], (float)rb[1]);
            PelvisBind = new((float)-pb[0], (float)pb[2], (float)pb[1]);
            var travel = new double[raw.KeyCount][]; var heights = new double[raw.KeyCount];
            for (int k = 0; k < raw.KeyCount; k++)
            {
                var pose = raw.Sample(k * raw.Tick); var r = pose.Translations.TryGetValue(3, out var rt) ? rt : rb;
                var p = pose.Translations.TryGetValue(4, out var pt) ? pt : pb;
                travel[k] = [r[0] + p[0], r[1] + p[1]]; heights[k] = p[2];
            }
            var sway = new double[raw.KeyCount][];
            for (int k = 0; k < raw.KeyCount; k++)
            {
                double fraction = k / (double)(raw.KeyCount - 1);
                sway[k] = [travel[k][0] - travel[0][0] * (1 - fraction) - travel[^1][0] * fraction,
                    travel[k][1] - travel[0][1] * (1 - fraction) - travel[^1][1] * fraction];
            }
            double sx = sway.Sum(v => v[0]) / raw.KeyCount, sy = sway.Sum(v => v[1]) / raw.KeyCount;
            _pelvis = Enumerable.Range(0, raw.KeyCount).Select(k => new[] { pb[0] + sway[k][0] - sx, pb[1] + sway[k][1] - sy, heights[k] }).ToArray();
        }
        private double[] Bind(uint id)
        {
            var b = _skeleton.Bones[id]; double[] result = [b.X, b.Y, b.Z];
            if (_skeleton.Bones.TryGetValue(b.Parent, out var parent)) { result[0] -= parent.X; result[1] -= parent.Y; result[2] -= parent.Z; }
            return result;
        }
        internal N[] Sample(double seconds)
        {
            var source = _raw.Sample(seconds); var result = Enumerable.Repeat(N.Identity, _rig.Nodes.Count).ToArray();
            double t = Math.Clamp(seconds, 0, _raw.Duration); if (t >= 1e-7) t -= 1e-7;
            double offset = t / _raw.Tick; int lo = Math.Min((int)offset, _raw.KeyCount - 2); double fraction = Math.Clamp(offset - lo, 0, 1);
            foreach (var bone in _skeleton.Bones.Values)
            {
                double[] bind = Bind(bone.Id); var translation = source.Translations.TryGetValue(bone.Id, out var p) ? p : bind;
                if (bone.Id == 3) { result[_nodes[bone.Id]] = RootBind; continue; }
                if (bone.Id == 4) translation = Enumerable.Range(0, 3).Select(i => _pelvis[lo][i] * (1 - fraction) + _pelvis[lo + 1][i] * fraction).ToArray();
                N rotation = N.Identity;
                if (source.Rotations.TryGetValue(bone.Id, out var q))
                {
                    N basis = new(-1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1);
                    rotation = basis * N.CreateFromQuaternion(new Q((float)q[0], (float)q[1], (float)q[2], (float)q[3])) * basis;
                }
                result[_nodes[bone.Id]] = rotation * N.CreateTranslation((float)-translation[0], (float)translation[2], (float)translation[1]);
            }
            return result;
        }
    }
    private static N[] Expected(Captured data, PlayerAnimation lower, double lowerSeconds, PlayerAnimation upper, double upperSeconds, float aim)
    {
        var low = data.Oracle[Lower[lower]]; N[] result = low.Sample(lowerSeconds % low.Duration);
        if (Upper.TryGetValue(upper, out string? name))
        {
            var top = data.Oracle[name]; var torso = top.Sample(upperSeconds % top.Duration);
            N[] lowerGlobal = Globals(data.Rig, result), upperGlobal = Globals(data.Rig, torso); int spine = Node(data.Rig, "source_bone_30_Spine_1");
            N full = upperGlobal[spine]; full.Translation = lowerGlobal[spine].Translation;
            int parent = data.Rig.Nodes[spine].Parent; N.Invert(lowerGlobal[parent], out var inverse); result[spine] = full * inverse;
            for (int i = 0; i < result.Length; i++) if (i != spine && Under(data.Rig, i, spine)) result[i] = torso[i];
        }
        ApplyAim(data.Rig, result, aim); return result;
    }
    private static void ApplyAim(CharacterSourceRig rig, N[] result, float aim)
    {
        int spine = Node(rig, "source_bone_30_Spine_1"), parent = rig.Nodes[spine].Parent; var world = Globals(rig, result);
        float pitch = MathF.Asin(Math.Clamp(aim, -2896 / 4096f, 2896 / 4096f));
        V pivot = world[spine].Translation; N desired = world[spine]; desired.Translation = V.Zero;
        desired *= N.CreateRotationX(-pitch); desired.Translation = pivot;
        N.Invert(world[parent], out var inverse); result[spine] = desired * inverse;
    }
    // Diagnostic intermediate only: endpoints are still checked against the independent
    // raw-bank oracle before this can explain the shipped Slerp rounding branch.
    private static M[] ObservedTarget(CharacterSourceRig rig, IReadOnlyDictionary<string, CharacterSourceClip> clips,
        PlayerAnimation lower, double lowerSeconds, PlayerAnimation upper, double upperSeconds)
    {
        var low = clips[Lower[lower]]; M[] result = low.Sample(lowerSeconds % low.Duration).ToArray();
        if (Upper.TryGetValue(upper, out string? name))
        {
            var top = clips[name]; var torso = top.Sample(upperSeconds % top.Duration);
            M[] World(IReadOnlyList<M> local)
            {
                var world = new M[local.Count]; var done = new bool[local.Count];
                M Resolve(int n) { if (!done[n]) { int p = rig.Nodes[n].Parent; world[n] = p < 0 ? local[n] : local[n] * Resolve(p); done[n] = true; } return world[n]; }
                for (int i = 0; i < local.Count; i++) Resolve(i); return world;
            }
            int spine = Node(rig, "source_bone_30_Spine_1"), parent = rig.Nodes[spine].Parent;
            M[] lowerWorld = World(result), upperWorld = World(torso);
            M full = upperWorld[spine].ClearTranslation(); full.Row3 = lowerWorld[spine].Row3;
            result[spine] = parent < 0 ? full : full * lowerWorld[parent].Inverted();
            for (int i = 0; i < result.Length; i++) if (i != spine && Under(rig, i, spine)) result[i] = torso[i];
        }
        return result;
    }
    private static double RotationAngle(N a, N b)
    {
        N.Decompose(a, out _, out Q ar, out _); N.Decompose(b, out _, out Q br, out _);
        double[] x = [ar.X, ar.Y, ar.Z, ar.W], y = [br.X, br.Y, br.Z, br.W];
        double nx = Math.Sqrt(x.Sum(v => v * v)), ny = Math.Sqrt(y.Sum(v => v * v));
        for (int i = 0; i < 4; i++) { x[i] /= nx; y[i] /= ny; }
        if (x.Zip(y, (u, v) => u * v).Sum() < 0) for (int i = 0; i < 4; i++) y[i] = -y[i];
        double minus = Math.Sqrt(x.Zip(y, (u, v) => (u - v) * (u - v)).Sum());
        double plus = Math.Sqrt(x.Zip(y, (u, v) => (u + v) * (u + v)).Sum());
        return 4 * Math.Atan2(minus, plus);
    }
    private static N[] Mix(N[] from, N[] to, float fraction) => from.Select((a, i) =>
    {
        N.Decompose(a, out _, out var ar, out var at); N.Decompose(to[i], out _, out var br, out var bt);
        return N.CreateFromQuaternion(BlendRotation(ar, br, fraction)) * N.CreateTranslation(V.Lerp(at, bt, fraction));
    }).ToArray();
    // OpenTK4.9.4 uses normalized linear interpolation above a .99 dot
    // threshold. System.Numerics.Slerp chooses a different cutoff. Keep this
    // oracle independent while comparing the actual specified math contract,
    // rather than hiding a library approximation difference in a loose bound.
    private static Q BlendRotation(Q a, Q b, float fraction)
    {
        float dot = Q.Dot(a, b);
        if (dot < 0) { b = new(-b.X, -b.Y, -b.Z, -b.W); dot = -dot; }
        if (dot >= .99f)
        {
            _linearBlends++;
            return Q.Normalize(new Q(a.X * (1 - fraction) + b.X * fraction, a.Y * (1 - fraction) + b.Y * fraction,
                a.Z * (1 - fraction) + b.Z * fraction, a.W * (1 - fraction) + b.W * fraction));
        }
        _sphericalBlends++; return Q.Normalize(Q.Slerp(a, b, fraction));
    }
    private static N[] Globals(CharacterSourceRig rig, IReadOnlyList<N> local)
    {
        var result = new N[local.Count]; var done = new bool[local.Count];
        N Resolve(int n) { if (!done[n]) { int parent = rig.Nodes[n].Parent; result[n] = parent < 0 ? local[n] : local[n] * Resolve(parent); done[n] = true; } return result[n]; }
        for (int i = 0; i < local.Count; i++) Resolve(i); return result;
    }
    private static bool Under(CharacterSourceRig rig, int node, int ancestor) { for (int n = node; n >= 0; n = rig.Nodes[n].Parent) if (n == ancestor) return true; return false; }
    private static int Node(CharacterSourceRig rig, string name) => Enumerable.Range(0, rig.Nodes.Count).Single(i => rig.Nodes[i].Name == name);
    private static N ToN(M m) => new(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);
    private static double Distance(IReadOnlyList<M> actual, IReadOnlyList<N> expected)
        => actual.Count != expected.Count ? double.PositiveInfinity : actual.Select((m, i) => Distance(ToN(m), expected[i])).DefaultIfEmpty(double.PositiveInfinity).Max();
    private static double Distance(N a, N b) => Values(a).Zip(Values(b), (x, y) => Math.Abs(x - y)).Max();
    private static float[] Values(N m) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
    private static string PoseHash(IReadOnlyList<M> matrices)
    { using var bytes = new MemoryStream(); using var w = new BinaryWriter(bytes); foreach (var m in matrices) foreach (float v in Values(ToN(m))) w.Write(v); return Sha(bytes.ToArray()); }
    private static string RawHash(Dictionary<string, CharacterSourceClip> clips) => Sha(Encoding.UTF8.GetBytes(string.Join("\n", clips.OrderBy(p => p.Key).Select(p =>
        p.Key + ":" + string.Join(":", new[] { 0d, p.Value.Duration * .375, p.Value.Duration }.Select(t => PoseHash(p.Value.Sample(t))))))));
    private static long? ProofReads() => typeof(CharacterModelPack).Assembly.GetType("MphRead.Mods.Render.Characters.CharacterSourceProofIO")?.GetField("OpenedReads", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as long?;
    private static Dictionary<string, object[]> Caches() => new[] { "_modelCache", "_fhModelCache", "_retiredRoomModels", "_effects", "_particleDefs" }.ToDictionary(n => n,
        n => ((IEnumerable)typeof(Read).GetField(n, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!).Cast<object>().ToArray());
    private static Dictionary<string, string> Files(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(root, p), HashFile, StringComparer.Ordinal);
    private static bool Same(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var hash) && hash == p.Value);
    private static string HashFile(string path) { using var file = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(file)); }
    private static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
