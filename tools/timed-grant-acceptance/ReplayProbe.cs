using System.Collections;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Network;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

// Opens actual recordings through PassiveReplayScene/PassiveReplayPlayer. No
// manually seeded decoder or made-up recording substitutes for missing files.
internal static class ReplayProbe
{
    internal static int[] Run(string path, string kind, Scene live, Acceptance.Report report)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Required genuine replay evidence is missing.", path);
        string fileHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        object? linear = null, seeker = null;
        var hashes = new SortedDictionary<uint, (string Gameplay, CaptureHashes? Pixels)>();
        var positive = new int[3];
        long nativeComparisons = 0;
        object livePresentation = Reflection.Get(Reflection.Type("Mods.Network.NetGrantNetwork"), "Presentation")!;
        var originalState = Reflection.Get(typeof(GameState), "Current"); var originalPlayers = Reflection.Get(typeof(PlayerEntity), "LegacyRegistry"); var originalRandom = Reflection.Get(typeof(Rng), "Current");
        try
        {
            linear = Reflection.Create("PassiveReplayScene", path, new Vector2i(640, 480));
            Scene scene = (Scene)Reflection.Get(linear, "Scene")!;
            object state = Reflection.Get(linear, "State")!, session = Reflection.Get(linear, "Session")!;
            uint last = Convert.ToUInt32(Reflection.Get(session, "LastFrame"));
            if (last > 60000) throw new InvalidOperationException("Native replay fixture exceeds its explicit 60,000-frame bound.");
            bool[] captured = new bool[3];
            int steps = 0;
            while ((bool)Reflection.Call(linear, "Step")!)
            {
                if (++steps > 60001) throw new InvalidOperationException("Native recorded replay failed to reach its bounded end.");
                uint frame = Convert.ToUInt32(Reflection.Get(session, "CurrentFrame"));
                uint recordingFrame = Convert.ToUInt32(Reflection.Get(session, "RecordingFrame"));
                object presentation = Reflection.Get(state, "TimedGrants")!;
                object? match = Reflection.Get(state, "Match");
                if (match is { } scope && Reflection.Bool(presentation, "HasCoverage"))
                {
                    for (int slot = 0; slot < scene.Players.Items.Count; slot++)
                    {
                        var method = state.GetType().GetMethod("TryGetPlayer", Reflection.All)!;
                        object?[] arguments = { slot, null };
                        if (!(bool)method.Invoke(state, arguments)!) continue;
                        object sample = arguments[1]!;
                        var life = new ShotKey(Convert.ToUInt64(Reflection.Get(scope, "AuthorityEpoch")),
                            Convert.ToUInt16(Reflection.Get(scope, "MatchId")), slot,
                            Convert.ToUInt16(Reflection.Get(sample, "SlotGeneration")), Convert.ToUInt16(Reflection.Get(sample, "LifeId")), 0);
                        var actor = scene.Players.Items[slot];
                        for (int type = 0; type < 3; type++)
                        {
                            object effect = Reflection.Enum("TimedPowerUp", new[] { "DoubleDamage", "Cloak", "DeathAlt" }[type]);
                            object view = Reflection.Call(presentation, "View", life, effect, recordingFrame)!;
                            bool activeActor = actor.Health > 0 && Reflection.Bool(actor, "ModIsInPlay");
                            // Historical/partially covered replicas deliberately
                            // preserve native timers until this exact life/type
                            // has A3 coverage. Absence of a fact is not a grant.
                            if (activeActor && !Reflection.Bool(view, "Covered")) continue;
                            int expected = activeActor ? Reflection.Int(view, "Remaining") : 0;
                            int actual = Convert.ToInt32(Reflection.Call(actor, "ModTimedVisualRemaining", effect));
                            if (actual != expected)
                                throw new InvalidOperationException($"Native {kind} timer differs at {frame}, slot {slot}, type {type}: {actual}/{expected}.");
                            nativeComparisons++;
                            if (activeActor && Reflection.Int(view, "ConfirmedRemaining") > 0)
                            {
                                positive[type]++;
                                if (!captured[type])
                                {
                                    captured[type] = true;
                                    string label = kind + "-type" + type + "-frame" + frame;
                                    if (!hashes.ContainsKey(frame))
                                    {
                                        ReplayReadbackDiagnostics.Save(linear, label + "-before-draw", report);
                                        var pixels = Capture(scene, label, report);
                                        ReplayReadbackDiagnostics.Save(linear, label + "-after-draw", report);
                                        var repeat = Capture(scene, label + "-repeat", report);
                                        ReplayReadbackDiagnostics.Save(linear, label + "-after-repeat", report);
                                        report.Stages.Add(new { stage = "native-same-state-render-counterfactual", label,
                                            exact = repeat == pixels, first = pixels, repeated = repeat });
                                        if (repeat != pixels) throw new InvalidOperationException("Genuine native linear same-state repeat readback differs at " + frame + ".");
                                        hashes[frame] = (GameplayHash(scene, frame), pixels);
                                    }
                                    report.Stages.Add(new { stage = "native-recorded-replay-positive", kind, frame, recordingFrame,
                                        slot, type, actual, expected, sourcePath = path, sourceSha256 = fileHash, view = view.ToString() });
                                }
                            }
                        }
                    }
                }
                if (!ReferenceEquals(Reflection.Get(typeof(GameState), "Current"), originalState) || !ReferenceEquals(Reflection.Get(typeof(PlayerEntity), "LegacyRegistry"), originalPlayers)
                    || !ReferenceEquals(Reflection.Get(typeof(Rng), "Current"), originalRandom)
                    || !ReferenceEquals(Reflection.Get(Reflection.Type("Mods.Network.NetGrantNetwork"), "Presentation"), livePresentation))
                    throw new InvalidOperationException("Actual private replay mutated a live facade or live grant presentation.");
            }
            uint finalFrame = Convert.ToUInt32(Reflection.Get(session, "CurrentFrame"));
            if (!hashes.ContainsKey(finalFrame)) hashes[finalFrame] = (GameplayHash(scene, finalFrame), null);
            ((IDisposable)linear).Dispose(); linear = null;

            // Real memory/durable seek machinery; compare to independently
            // stepped native playback at observed positive timed-effect frames.
            object options = Reflection.Create("ReplayPlayerOptions", true, true, true);
            seeker = Reflection.Create("PassiveReplayPlayer", path, new Vector2i(640, 480), options);
            int comparisons = 0;
            foreach (var expected in hashes.Reverse())
            {
                Reflection.Call(seeker, "Seek", expected.Key, false);
                int updates = 0;
                while (Reflection.Bool(Reflection.Get(seeker, "Transport")!, "IsSeeking"))
                {
                    if (++updates > 60001) throw new InvalidOperationException("Actual recorded replay seek did not complete.");
                    Reflection.Call(seeker, "Update", 120, double.PositiveInfinity);
                }
                object current = Reflection.Get(seeker, "Current")!;
                Scene restored = (Scene)Reflection.Get(current, "Scene")!;
                object restoredSession = Reflection.Get(current, "Session")!;
                uint frame = Convert.ToUInt32(Reflection.Get(restoredSession, "CurrentFrame"));
                if (frame != expected.Key || GameplayHash(restored, frame) != expected.Value.Gameplay)
                    throw new InvalidOperationException($"Genuine native {kind} seek gameplay differs at {expected.Key}.");
                string label = kind + "-seek-" + frame;
                ReplayReadbackDiagnostics.Save(current, label + "-before-draw", report);
                var actualPixels = Capture(restored, label, report);
                ReplayReadbackDiagnostics.Save(current, label + "-after-draw", report);
                var repeat = Capture(restored, label + "-repeat", report);
                ReplayReadbackDiagnostics.Save(current, label + "-after-repeat", report);
                report.Stages.Add(new { stage = "native-same-state-render-counterfactual", label,
                    exact = repeat == actualPixels, first = actualPixels, repeated = repeat });
                if (repeat != actualPixels) throw new InvalidOperationException("Genuine native seek same-state repeat readback differs at " + frame + ".");
                if (expected.Value.Pixels is { } expectedPixels && actualPixels != expectedPixels)
                    throw new InvalidOperationException($"Genuine native {kind} seek color/HUD/depth differs at {expected.Key}; exact parity is required.");
                comparisons++;
            }
            report.Stages.Add(new { stage = "native-recorded-replay-seek-summary", kind, sourcePath = path,
                sourceSha256 = fileHash, positiveByType = positive, nativeComparisons, seekComparisons = comparisons,
                rejectedCheckpoints = Reflection.Get(seeker, "RejectedCheckpoints"),
                rejectedDurableCheckpoints = Reflection.Get(seeker, "RejectedDurableCheckpoints"),
                checkpointBytes = Reflection.Get(seeker, "CheckpointBytes") });
            report.Missing.Add($"NOT_RUN: {kind} player-perspective gain/loop/expiry audio; this probe keeps private camera/audio policy. Native timers, gameplay and exact captured color/HUD/depth seek parity are exercised only at recorded positive frames.");
        }
        finally
        {
            (seeker as IDisposable)?.Dispose(); (linear as IDisposable)?.Dispose();
            if (Reflection.Get(typeof(GameState), "Current") != originalState || Reflection.Get(typeof(PlayerEntity), "LegacyRegistry") != originalPlayers || Reflection.Get(typeof(Rng), "Current") != originalRandom)
                throw new InvalidOperationException("Native private replay cleanup changed live facade identities.");
        }
        return positive;
    }
    private static string GameplayHash(Scene scene, uint frame)
        => (string)Reflection.Static("ReplayStateHash", "Compute", scene, frame)!;
    private readonly record struct CaptureHashes(string World, string Hud, string Depth);
    private static CaptureHashes Capture(Scene scene, string label, Acceptance.Report report)
    {
        scene.OnDrawFrame(); scene.OnRenderFrame(); GL.Finish();
        string world = Path.Combine(report.Output, label + "-world.png"), hud = Path.Combine(report.Output, label + "-hud.png");
        if (!ScreenCapture.Save(scene, world)
            || !ScreenCapture.SaveWindow(scene, hud)
            || GL.GetError() != ErrorCode.NoError)
            throw new InvalidOperationException("Genuine native private replay GL readback failed: " + label);
        int previous = GL.GetInteger(GetPName.ReadFramebufferBinding);
        var size = scene.RenderSize; var depth = new float[size.X * size.Y];
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, Reflection.Int(scene, "_frameBuffer"));
            GL.ReadPixels(0, 0, size.X, size.Y, PixelFormat.DepthComponent, PixelType.Float, depth);
        }
        finally { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previous); }
        if (depth.Any(v => !float.IsFinite(v)) || GL.GetError() != ErrorCode.NoError)
            throw new InvalidOperationException("Genuine native private replay depth readback failed: " + label);
        byte[] bytes = MemoryMarshal.AsBytes(depth.AsSpan()).ToArray();
        File.WriteAllBytes(Path.Combine(report.Output, label + "-depth-float32.bin"), bytes);
        var hashes = new CaptureHashes(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(world))),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(hud))), Convert.ToHexString(SHA256.HashData(bytes)));
        report.Stages.Add(new { stage = "native-private-replay-readback", label, hashes, width = size.X, height = size.Y });
        return hashes;
    }
}
