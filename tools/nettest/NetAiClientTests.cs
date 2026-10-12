using System;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Real asset-free scene/player constructors and the production driver policy.
// No packet results, paid hits, or network success are manufactured here.
internal static class NetAiClientTests
{
    private static int _checks;
    private static void Check(bool value, string label)
    {
        _checks++;
        if (!value) throw new InvalidOperationException("AI client: " + label);
    }
    private static void Role(NetRole role, int slot)
    {
        typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, role);
        typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!.SetValue(null, slot);
    }
    private static void Rig(HitRig.RigMode mode)
        => typeof(HitRig).GetProperty(nameof(HitRig.Mode))!.SetValue(null, mode);
    private static Scene Scene(ISceneServices? services = null)
        => new(new Vector2i(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
            _ => { }, () => { }, services, initializeRuntime: false);
    private static JsonElement Report() => JsonSerializer.SerializeToElement(NetAiClient.Capture());

    internal static int Run()
    {
        _checks = 0;
        var previousRole = NetSession.Role; int previousSlot = NetSession.LocalSlot;
        var previousRig = HitRig.Mode;
        string? previousSoak = Environment.GetEnvironmentVariable("PRIME_NET2_SOAK");
        var previousGame = GameState.Current; var previousRegistry = PlayerEntity.LegacyRegistry;
        var previousRandom = Rng.Current;
        bool[] previousBots = (bool[])NetSession.SlotIsBot.Clone();
        try
        {
            Rig(HitRig.RigMode.Off); Environment.SetEnvironmentVariable("PRIME_NET2_SOAK", null);
            Configure(); EligibilityAndDisabledControl(); RandomIsolation(); DamageFeedbackEligibility(); PositionEvidence();
            Console.WriteLine($"PASS: AI client {_checks} assertions; configuration, ordinary-client eligibility, disabled controls, private RNG scopes and finite spatial evidence.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            NetAiClient.Reset(); Role(previousRole, previousSlot); Rig(previousRig);
            Environment.SetEnvironmentVariable("PRIME_NET2_SOAK", previousSoak);
            previousBots.CopyTo(NetSession.SlotIsBot, 0);
            GameState.Current = previousGame; PlayerEntity.LegacyRegistry = previousRegistry; Rng.Current = previousRandom;
        }
    }

    private static void Configure()
    {
        Check(NetAiClient.Configure(false, null, null, out var error) && error == "" && !NetAiClient.Enabled,
            "ordinary launch leaves AI disabled");
        Check(NetAiClient.HasLocalActivity, "disabled driver imposes no activity prerequisite");
        Check(!NetAiClient.Configure(false, null, "19", out error) && error.Contains("requires") && !NetAiClient.Enabled,
            "seed cannot silently enable an ordinary launch");
        Check(NetAiClient.Configure(true, null, null, out error) && NetAiClient.Level == 2 && NetAiClient.Seed == 1729,
            "explicit enable has documented default difficulty and seed");
        for (int level = 0; level <= 3; level++)
            Check(NetAiClient.Configure(true, level.ToString(), "4294967295", out error)
                && NetAiClient.Level == level && NetAiClient.Seed == UInt32.MaxValue,
                $"difficulty {level} and maximum uint seed accepted");
        foreach (string level in new[] { "-1", "4", "", " 2", "2 ", "+2", "2.0", "9999999999999999" })
        {
            Check(!NetAiClient.Configure(true, level, "19", out error) && error.Contains("0..3"),
                $"invalid difficulty '{level}' rejected");
            Check(!NetAiClient.Enabled && NetAiClient.Level == 0 && NetAiClient.Seed == 0,
                "failed reconfiguration retires the previous driver");
        }
        foreach (string seed in new[] { "0", "-1", "4294967296", "", " 19", "19 ", "+19", "0x13" })
            Check(!NetAiClient.Configure(true, "2", seed, out error) && error.Contains("nonzero uint") && !NetAiClient.Enabled,
                $"invalid seed '{seed}' rejected");
        Rig(HitRig.RigMode.Duel);
        Check(!NetAiClient.Configure(true, null, null, out error) && error.Contains("HitRig") && !NetAiClient.Enabled,
            "scripted HitRig cannot masquerade as ordinary AI clients");
        Check(NetAiClient.Configure(false, null, null, out error), "disabled control remains valid with HitRig configured");
        Rig(HitRig.RigMode.Off); Environment.SetEnvironmentVariable("PRIME_NET2_SOAK", "1");
        Check(!NetAiClient.Configure(true, null, null, out error) && error.Contains("soak") && !NetAiClient.Enabled,
            "scripted soak and ordinary AI cannot be combined");
        Environment.SetEnvironmentVariable("PRIME_NET2_SOAK", null);
    }

    private static void PositionEvidence()
    {
        Role(NetRole.Client, 0);
        Check(NetAiClient.Configure(true, "2", "19", out _), "configure spatial observation fixture");
        Check(NetAiClient.ObservePosition(Vector3.Zero)
            && NetAiClient.ObservePosition(new Vector3(3, 4, 0)), "finite native samples are accepted");
        Check(Report().GetProperty("Travel").GetDouble() == 5,
            "native 3-4-5 movement contributes exactly five units");

        NetAiClient.Configure(true, "2", "19", out _);
        var enormous = new Vector3(Single.MaxValue, Single.MaxValue, Single.MaxValue);
        Check(NetAiClient.ObservePosition(-enormous) && NetAiClient.ObservePosition(enormous),
            "finite extreme coordinates avoid single-precision subtraction overflow");
        double travel = Report().GetProperty("Travel").GetDouble();
        double expected = 2 * (double)Single.MaxValue * Math.Sqrt(3);
        Check(Double.IsFinite(travel) && Math.Abs(travel - expected) <= expected * 1e-12,
            "extreme finite movement retains accurate finite double distance");
        Check(Report().GetProperty("InvalidPositionSamples").GetInt64() == 0
            && Report().GetProperty("InvalidDistanceSamples").GetInt64() == 0,
            "extreme finite vectors do not invent invalid observations");

        foreach (var invalid in new[] { new Vector3(Single.NaN, 0, 0),
            new Vector3(0, Single.PositiveInfinity, 0), new Vector3(0, 0, Single.NegativeInfinity) })
        {
            NetAiClient.Configure(true, "2", "19", out _);
            NetAiClient.ObservePosition(Vector3.Zero);
            NetAiClient.ObservePosition(new Vector3(3, 4, 0));
            // Policy-only frame thresholds: this fixture does not claim live input,
            // network activity, or a gameplay campaign result.
            const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
            typeof(NetAiClient).GetField("_inputs", hidden)!.SetValue(null, 60L);
            typeof(NetAiClient).GetField("_decisions", hidden)!.SetValue(null, 60L);
            Check(NetAiClient.HasLocalActivity, "valid observed movement meets the isolated activity policy fixture");
            Check(!NetAiClient.ObservePosition(invalid), "nonfinite native position is refused");
            // Default strict JSON serialization must succeed without allowing NaN.
            var report = Report();
            Check(report.GetProperty("InvalidPositionSamples").GetInt64() == 1
                && report.GetProperty("InvalidDistanceSamples").GetInt64() == 0
                && Double.IsFinite(report.GetProperty("Travel").GetDouble())
                && report.GetProperty("Travel").GetDouble() == 5,
                "invalid native position leaves finite JSON evidence and retained failure count");
            Check(!report.GetProperty("ActivityPassed").GetBoolean() && !NetAiClient.HasLocalActivity,
                "a spatial failure vetoes activity even after sufficient movement and frame counts");
            Check(NetAiClient.ObservePosition(new Vector3(1000, 1000, 0))
                && Report().GetProperty("Travel").GetDouble() == 5,
                "first repaired sample starts a new baseline without phantom bridge travel");
            Check(NetAiClient.ObservePosition(new Vector3(1003, 1004, 0))
                && Report().GetProperty("Travel").GetDouble() == 10
                && !Report().GetProperty("ActivityPassed").GetBoolean(),
                "subsequent finite movement resumes counting while the original failure remains sticky");
            NetAiClient.Reset();
            report = Report();
            Check(!report.GetProperty("Enabled").GetBoolean()
                && report.GetProperty("Travel").GetDouble() == 0
                && report.GetProperty("InvalidPositionSamples").GetInt64() == 0
                && report.GetProperty("InvalidDistanceSamples").GetInt64() == 0,
                "reset clears retained spatial failures and movement baseline");
        }
    }

    private static void EligibilityAndDisabledControl()
    {
        NetAiClient.Reset(); Role(NetRole.Offline, 2);
        var scene = Scene(); var other = Scene();
        using var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
        var state = ((PassiveReplaySessionHost)session.Host).State;
        var replica = Scene(new ReplaySceneServices(session, state));
        var local = scene.Players.Items[2]; var remote = scene.Players.Items[3];
        local.Controls.Shoot.IsDown = true; local.Controls.Shoot.IsPressed = true;
        local.BotLevel = 1; NetSession.SlotIsBot[2] = false;
        Role(NetRole.Client, 2);
        NetAiClient.Bind(scene); NetAiClient.ApplyInput(local); NetAiClient.Decide(local);
        Check(!NetAiClient.Drives(local) && local.Controls.Shoot.IsDown && local.Controls.Shoot.IsPressed,
            "disabled driver preserves real controls without calling AI");
        Check(local.BotLevel == 1 && !local.IsBot && !NetSession.SlotIsBot[2]
            && Report().GetProperty("InputFrames").GetInt64() == 0 && Report().GetProperty("DecisionFrames").GetInt64() == 0,
            "disabled launch preserves bot identity, difficulty and activity counters");
        TransparentRandom(scene, "disabled RNG scope preserves ordinary gameplay draws");
        Check(NetAiClient.Configure(true, "3", "19", out _), "enable ordinary local driver");
        NetAiClient.Bind(scene);
        Check(NetAiClient.Drives(local) && !NetAiClient.Drives(remote), "only assigned local ordinary slot is driven");
        Check(!local.IsBot && !NetSession.SlotIsBot[2], "AI binding does not turn local client into a server bot");
        Check(!NetAiClient.HasLocalActivity, "enabled driver requires measured local activity");
        Check(!NetAiClient.Drives(other.Players.Items[2]), "unbound scene cannot borrow local AI ownership");
        TransparentRandom(other, "unbound scene preserves gameplay RNG");
        local.IsBot = true;
        Check(!NetAiClient.Drives(local), "authority bot actor cannot be driven as an ordinary client");
        local.IsBot = false; NetSession.SlotIsBot[2] = true;
        NetAiClient.ApplyInput(local); NetAiClient.Decide(local);
        Check(!NetAiClient.Drives(local) && !local.IsBot && NetSession.SlotIsBot[2],
            "accepted roster bot identity fences AI before actor flag synchronization");
        Check(local.Controls.Shoot.IsDown && local.Controls.Shoot.IsPressed
            && Report().GetProperty("InputFrames").GetInt64() == 0 && Report().GetProperty("DecisionFrames").GetInt64() == 0,
            "roster bot transition cannot consume native local controls or AI decisions");
        NetSession.SlotIsBot[2] = false;
        Check(NetAiClient.Drives(local), "ordinary roster identity restores local input eligibility");
        foreach (var role in new[] { NetRole.Offline, NetRole.Host, NetRole.Server })
        {
            Role(role, 2); NetAiClient.Bind(other);
            Check(!NetAiClient.Drives(local) && !NetAiClient.Drives(other.Players.Items[2]),
                $"{role} cannot own local client AI");
            TransparentRandom(scene, $"{role} RNG scope preserves authority gameplay draws");
        }
        Role(NetRole.Client, -1);
        Check(!NetAiClient.Drives(local), "unassigned client slot is ineligible");
        Role(NetRole.Client, 2); NetAiClient.Bind(replica);
        Check(!NetAiClient.Drives(replica.Players.Items[2]) && NetAiClient.Drives(local),
            "replica binding is rejected without replacing live driver ownership");
        TransparentRandom(replica, "replica RNG scope preserves historical gameplay draws");
        NetAiClient.Bind(other);
        Check(!NetAiClient.Drives(local) && NetAiClient.Drives(other.Players.Items[2]),
            "new world binding retires the previous scene");
        NetAiClient.Reset();
        Check(!NetAiClient.Drives(other.Players.Items[2]), "reset removes local scene ownership");
    }

    private static void TransparentRandom(Scene scene, string label)
    {
        scene.Random.SetRng1(123456); scene.Random.SetRng2(654321);
        var reference = new MatchRandom(); reference.SetRng1(123456); reference.SetRng2(654321);
        uint expected1 = reference.GetRandomInt1(1000u), expected2 = reference.GetRandomInt2(1000u);
        uint actual1, actual2;
        using (NetAiClient.UseRandom(scene))
        { actual1 = scene.Random.GetRandomInt1(1000u); actual2 = scene.Random.GetRandomInt2(1000u); }
        Check(actual1 == expected1 && actual2 == expected2
            && scene.Random.Rng1 == reference.Rng1 && scene.Random.Rng2 == reference.Rng2, label);
    }

    private static void DamageFeedbackEligibility()
    {
        NetAiClient.Reset(); Role(NetRole.Client, 0);
        var scene = Scene();
        var local = scene.Players.Items[0];
        var remote = scene.Players.Items[1];
        var bot = scene.Players.Items[2];
        local.IsBot = remote.IsBot = false; bot.IsBot = true;
        scene.InsertEntity(local); scene.InsertEntity(remote); scene.InsertEntity(bot);
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var hitsField = local.AiData.GetType().GetField("_slotHits", hidden)!;
        int Hits(PlayerEntity player) => Convert.ToInt32(((Array)hitsField.GetValue(player.AiData)!).GetValue(local.SlotIndex));
        int localBefore = Hits(local), remoteBefore = Hits(remote), botBefore = Hits(bot);
        local.AiData.OnTakeDamage(7, remote, attacker: null);
        Check(Hits(local) == localBefore && Hits(remote) == remoteBefore && Hits(bot) == botBefore + 1,
            "disabled damage feedback preserves ordinary humans and existing server bots");
        Check(NetAiClient.Configure(true, "2", "19", out _), "configure local damage feedback fixture");
        NetAiClient.Bind(scene);
        uint rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2;
        local.AiData.OnTakeDamage(7, remote, attacker: null);
        Check(Hits(local) == localBefore + 1 && Hits(remote) == remoteBefore && Hits(bot) == botBefore + 2,
            "damage feedback reaches local non-bot AI while remote human ownership stays excluded");
        Check(scene.Random.Rng1 == rng1 && scene.Random.Rng2 == rng2,
            "damage bookkeeping does not draw gameplay or AI randomness");
        NetAiClient.Reset();
    }

    private static void RandomIsolation()
    {
        NetAiClient.Reset(); Role(NetRole.Client, 0);
        var scene = Scene(); scene.Random.SetRng1(123456); scene.Random.SetRng2(654321);
        Check(NetAiClient.Configure(true, "2", "19", out _), "configure reproducible RNG fixture");
        NetAiClient.Bind(scene);
        Check(scene.Random.Rng1 == 123456 && scene.Random.Rng2 == 654321, "bind never seeds authoritative gameplay RNG");
        var reference = new MatchRandom(); reference.SetRng1(19);
        reference.SetRng2(Report().GetProperty("InitialRng2").GetUInt32());
        uint a, b, c, d;
        using (NetAiClient.UseRandom(scene))
        {
            a = scene.Random.GetRandomInt1(1000u);
            using (NetAiClient.UseRandom(scene)) b = scene.Random.GetRandomInt2(1000u);
            c = scene.Random.GetRandomInt1(1000u);
        }
        Check(a == reference.GetRandomInt1(1000u) && b == reference.GetRandomInt2(1000u)
            && c == reference.GetRandomInt1(1000u), "nested calls consume one continuous AI stream");
        Check(scene.Random.Rng1 == 123456 && scene.Random.Rng2 == 654321, "outer scope restores both gameplay words");
        scene.Random.SetRng1(777); scene.Random.SetRng2(888); // authority snapshot update between decisions
        using (NetAiClient.UseRandom(scene)) d = scene.Random.GetRandomInt1(1000u);
        Check(d == reference.GetRandomInt1(1000u), "AI stream resumes across gameplay RNG synchronization");
        Check(scene.Random.Rng1 == 777 && scene.Random.Rng2 == 888, "updated authority words survive the next AI decision");
        try
        {
            using var scope = NetAiClient.UseRandom(scene);
            Check(scene.Random.GetRandomInt2(1000u) == reference.GetRandomInt2(1000u), "exception fixture consumes AI draw");
            throw new InvalidOperationException("intentional AI scope failure");
        }
        catch (InvalidOperationException ex) when (ex.Message == "intentional AI scope failure") { }
        Check(scene.Random.Rng1 == 777 && scene.Random.Rng2 == 888, "exception unwinding restores gameplay RNG");
        default(NetAiClient.AiRandomScope).Dispose();
        using (NetAiClient.UseRandom(scene))
            Check(scene.Random.GetRandomInt1(1000u) == reference.GetRandomInt1(1000u),
                "exception and default disposal preserve retained AI stream and nesting depth");
        NetAiClient.Configure(true, "2", "19", out _); NetAiClient.Bind(scene);
        using (NetAiClient.UseRandom(scene)) Check(scene.Random.GetRandomInt1(1000u) == a, "same seed reproduces initial AI decisions");
        NetAiClient.Configure(true, "2", "20", out _); NetAiClient.Bind(scene);
        using (NetAiClient.UseRandom(scene)) Check(scene.Random.GetRandomInt1(1000u) != a, "different seed changes AI decisions");
        NetAiClient.Reset(); TransparentRandom(scene, "reset driver restores ordinary RNG control");
    }
}
