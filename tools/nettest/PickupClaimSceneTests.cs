using System;
using System.Linq;
using System.Reflection;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Asset-backed adapter fixtures. Positions, actors and clocks are fixture inputs;
// these checks do not constitute real UDP or independent movement proof.
internal static class PickupClaimSceneTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly ItemType[] Types = { ItemType.DoubleDamage, ItemType.Cloak, ItemType.Deathalt };
    private static readonly string[] Timers = { "_doubleDmgTimer", "_cloakTimer", "_deathaltTimer" };
    private static readonly MethodInfo Contact = typeof(PlayerEntity).GetMethod("PickUpItems", Hidden)!;
    private static int _checks;
    private static uint _sequence;
    private static void Check(bool value, string label)
    { _checks++; if (!value) throw new InvalidOperationException("Pickup claim scene: " + label); }
    private static void Session(string property, object value)
        => typeof(NetSession).GetProperty(property)!.SetValue(null, value);
    private static void Advance(uint count = 1) => Session(nameof(NetSession.NetFrame), unchecked(NetSession.NetFrame + count));
    private static int Timer(PlayerEntity player, int type)
        => Convert.ToInt32(typeof(PlayerEntity).GetField(Timers[type], Hidden)!.GetValue(player));
    private static void Field(PlayerEntity player, string name, int value)
    {
        var field = typeof(PlayerEntity).GetField(name, Hidden)!;
        field.SetValue(player, Convert.ChangeType(value, field.FieldType));
    }
    private static ItemInstanceEntity Spawn(PlayerEntity player, ItemType type)
        => ItemSpawnEntity.SpawnItem(type, player.Position, player.NodeRef, 6000, player.OwningScene)
            ?? throw new InvalidOperationException("Native power-up factory did not create fixture item.");
    private static PickupClaim Claim(PlayerEntity player, ItemInstanceEntity item, uint source)
    {
        Check(NetPowerUpLedger.TrySpawnKey(item, out var spawn), "actual item factory registers monotonic spawn identity");
        Check(NetPowerUpLedger.TryType(item.ItemType, out var type), "native timed item type resolves");
        var life = ShotKey.For(player.SlotIndex, 0);
        return new(new(life.AuthorityEpoch, life.MatchId, player.SlotIndex, life.Generation, life.LifeId, ++_sequence),
            spawn, type, source, player.Position);
    }
    private static PickupVerdict Judge(PlayerEntity player, PickupClaim claim, Scene? scene = null)
        => NetPowerUpLedger.JudgeClaim(claim, player.SlotIndex, NetSession.NetFrame, 150, 20, scene ?? player.OwningScene);
    private static void Retire(ItemInstanceEntity item)
    { item.Destroy(); item.OwningScene.RemoveEntity(item); }
    private static void RemotePose(PlayerEntity player)
    {
        int slot = player.SlotIndex;
        NetSession.RemoteIntents[slot] = new IntentPacket { MatchId = NetSession.CurrentMatchId,
            AuthorityEpoch = NetSession.AuthorityEpoch, SlotGeneration = NetPlayerLifecycle.Generation(slot),
            LifeId = NetPlayerLifecycle.Get(slot), Frame = NetSession.NetFrame, Position = player.Position };
        NetSession.RemoteIntentValid[slot] = true;
        NetSession.RemoteIntentArrived[slot] = NetSession.NetFrame;
    }

    private static void TimedTypes(PlayerEntity owner)
    {
        for (int type = 0; type < Types.Length; type++)
        {
            Advance(10); owner.ModForceForm(false); owner.Health = 199;
            var item = Spawn(owner, Types[type]);
            uint proof = NetSession.NetFrame;
            NetPowerUpLedger.ObserveContact(owner);
            var claim = Claim(owner, item, proof);
            // Move the item after observation: proof must retain native geometry,
            // while the actual grant and timer start at the decision clock.
            item.Position += new Vector3(20, 0, 0);
            Advance(3); uint decision = NetSession.NetFrame;
            var verdict = Judge(owner, claim);
            Check(verdict.Result == PickupResult.Grant && verdict.ProofFrame == proof
                && verdict.DecisionFrame == decision && verdict.GrantFrame == decision
                && verdict.ExpiresAtFrame == unchecked(decision + 1800),
                "historical captured position proves contact without backdating grant " + Types[type]);
            Check(Timer(owner, type) == 1800 && item.DespawnTimer == 0,
                "claim invokes exact native timer and item consumption " + Types[type]);
            var recorded = NetPowerUpLedger.Ledger!.History.Single(g => g.Spawn == claim.Spawn);
            Check(recorded.Sequence == verdict.GrantSequence && recorded.Life == claim.Key.LifeKey,
                "claim verdict refers to actual adapter grant rather than fabricated history");
            Field(owner, Timers[type], 1234); Field(owner, "_timeSincePickup", 47);
            Advance(2); var retry = Judge(owner, claim);
            Check(retry == verdict && Timer(owner, type) == 1234
                && Convert.ToInt32(typeof(PlayerEntity).GetField("_timeSincePickup", Hidden)!.GetValue(owner)) == 47,
                "retry preserves exact verdict and does not rerun native feedback/timer side effects");
            Check(NetPowerUpLedger.Ledger.History.Count(g => g.Spawn == claim.Spawn) == 1,
                "retry cannot duplicate a grant");
            Retire(item);
        }
    }

    private static void QueuedNativePhase(PlayerEntity owner)
    {
        // Hold fixture actors clear of map pickups and locomotion hazards. This
        // exercises the real player phases, not the direct JudgeClaim adapter.
        foreach (var mapItem in owner.OwningScene.GetItemInstanceEntities()) mapItem.DespawnTimer = 0;
        for (int type = 0; type < Types.Length; type++)
        {
            // Earlier direct-adapter scenarios intentionally leave live grants.
            // Native timer field resets do not retire authority grant history.
            NetPowerUpLedger.BeginRoom();
            Advance(10); owner.ModForceForm(false); owner.Health = 199;
            foreach (string timer in Timers) Field(owner, timer, 0);
            var item = Spawn(owner, Types[type]);
            uint proof = NetSession.NetFrame;
            NetPowerUpLedger.ObserveContact(owner);
            var claim = Claim(owner, item, proof);
            item.Position += Vector3.UnitX * 20;
            Check(NetPickupNetwork.Receive(owner.SlotIndex, claim, 150, 20), "queued historical claim admitted");
            Advance(3); uint decision = NetSession.NetFrame;
            Vector3 safePosition = owner.Position;
            Check(owner.ProcessPlayer(), "queued claim runs actual native player pickup phase");
            Check(NetPowerUpLedger.Claims!.TryTerminal(claim.Key, out var verdict)
                && verdict.Result == PickupResult.Grant && verdict.ProofFrame == proof
                && verdict.GrantFrame == decision && verdict.DecisionFrame == decision,
                "native phase settles past contact at current authority clock " + Types[type]);
            int initial = type == 0 ? 1799 : 1800;
            Check(Timer(owner, type) == initial && item.DespawnTimer == 0,
                "queued grant preserves native contact-frame decrement ordering " + Types[type]);
            var ledger = NetPowerUpLedger.Ledger!;
            var grant = ledger.History.Single(g => g.Spawn == claim.Spawn);
            Check(grant.ContactFrame == decision && grant.EffectiveFrame == unchecked(decision + 1)
                && grant.ExpiresAtFrame == unchecked(decision + 1800), "queued grant keeps input-phase lifetime");
            Check(ledger.GrantedAt(grant.Life, decision, grant.Type, decision).Knowledge
                == TimedGrantKnowledge.NotGranted, "queued contact cannot empower already-observed current input");
            Retire(item);
            for (uint elapsed = 1; elapsed <= 1800; elapsed++)
            {
                Advance(); owner.ModPlaceAt(safePosition); owner.Health = 199;
                if (!owner.ProcessPlayer()) throw new InvalidOperationException("Queued fixture actor stopped processing.");
                if (elapsed == 1 || elapsed == 1799 || elapsed == 1800)
                {
                    Check(Timer(owner, type) == Math.Max(0, initial - (int)elapsed),
                        "queued native timer lifetime " + Types[type] + " tick " + elapsed);
                    Check(ledger.GrantedAt(grant.Life, NetSession.NetFrame, grant.Type, NetSession.NetFrame).Knowledge
                        == (elapsed == 1800 ? TimedGrantKnowledge.NotGranted : TimedGrantKnowledge.Granted),
                        "queued input ownership respects native timer boundary " + Types[type] + " tick " + elapsed);
                }
            }
            Check(ledger.History.Count(g => g.Spawn == claim.Spawn) == 1, "queued processing grants once");
        }
        NetPickupNetwork.Reset();
    }

    private static void CurrentCameraBlock(PlayerEntity owner)
    {
        NetPowerUpLedger.BeginRoom(); Advance(10); owner.ModForceForm(false); owner.Health = 199;
        foreach (string timer in Timers) Field(owner, timer, 0);
        var scene = owner.OwningScene;
        var cameraField = typeof(Scene).GetField("_cameraMode", Hidden)!;
        object previousMode = cameraField.GetValue(scene)!;
        int previousMain = scene.Players.MainPlayerIndex;
        var previousSequence = scene.CameraSequences.Current;
        var item = Spawn(owner, ItemType.DoubleDamage);
        try
        {
            scene.Players.MainPlayerIndex = owner.SlotIndex;
            cameraField.SetValue(scene, CameraMode.Player);
            scene.CameraSequences.Current = null;
            Check(owner.IsMainPlayer && owner.ModPickupContactPose(NetSession.NetFrame, NetSession.NetFrame).CanPickup,
                "camera fixture starts with native pickup eligibility");
            NetPowerUpLedger.ObserveContact(owner);
            var claim = Claim(owner, item, NetSession.NetFrame);
            Check(NetPickupNetwork.Receive(owner.SlotIndex, claim, 150, 20), "camera fixture queues eligible historical contact");
            // Construct the native camera object without loading or playing an
            // asset sequence: only its real BlockInput gate is under test.
            var constructor = typeof(CameraSequence).GetConstructors(Hidden).Single();
            var sequence = (CameraSequence)constructor.Invoke(new object[] { -1, "A2 camera fixture", scene,
                default(CameraSequenceHeader), Array.Empty<RawCameraSequenceKeyframe>() });
            sequence.Flags = CamSeqFlags.BlockInput;
            scene.CameraSequences.Current = sequence;
            Advance(3);
            Check(!owner.ModPickupContactPose(0, NetSession.NetFrame).CanPickup, "current native camera gate blocks pickup");
            Contact.Invoke(owner, null);
            Check(NetPowerUpLedger.Claims!.TryTerminal(claim.Key, out var verdict) && verdict.Result == PickupResult.Invalid,
                "queued historical contact cannot bypass current native camera gate");
            Check(item.DespawnTimer != 0 && Timer(owner, 0) == 0 && !NetPowerUpLedger.Ledger!.History.Any(),
                "camera refusal leaves native item, timer and authority grant history untouched");
        }
        finally
        {
            scene.CameraSequences.Current = previousSequence;
            scene.Players.MainPlayerIndex = previousMain; cameraField.SetValue(scene, previousMode);
            Retire(item); NetPickupNetwork.Reset();
        }
    }

    private static void QueuedFollowingNativeContact(PlayerEntity owner)
    {
        NetPowerUpLedger.BeginRoom(); Advance(10); owner.ModForceForm(false); owner.Health = 199;
        foreach (string timer in Timers) Field(owner, timer, 0);
        Vector3 contactPosition = owner.Position;
        var item = Spawn(owner, ItemType.DoubleDamage);
        uint offered = NetSession.NetFrame;
        var claim = Claim(owner, item, offered);
        owner.ModPlaceAt(contactPosition + Vector3.UnitX * 20);
        NetPowerUpLedger.ObserveContact(owner);
        Check(NetPickupNetwork.Receive(owner.SlotIndex, claim, 150, 20), "phase fixture queues original post-step contact claim");
        Check(owner.ProcessPlayer() && !NetPowerUpLedger.Claims!.TryTerminal(claim.Key, out _)
            && item.DespawnTimer != 0 && !NetPowerUpLedger.Ledger!.History.Any(),
            "bad exact pre-contact sample waits without granting or terminal refusal");
        Advance(); owner.ModPlaceAt(contactPosition); owner.Health = 199;
        Check(owner.ProcessPlayer(), "following actual native contact player phase runs");
        var ledger = NetPowerUpLedger.Ledger!;
        var native = ledger.History.Single(g => g.Spawn == claim.Spawn);
        Check(NetPowerUpLedger.Claims!.TryTerminal(claim.Key, out var verdict)
            && verdict.Result == PickupResult.Grant && verdict.Claim == claim
            && verdict.ProofFrame == NetSession.NetFrame && verdict.GrantFrame == NetSession.NetFrame
            && verdict.GrantSequence == native.Sequence && native.SourceContactFrame == unchecked(offered + 1),
            "following captured pose acknowledges exact original claim and actual native grant");
        Check(item.DespawnTimer == 0 && Timer(owner, 0) == 1799 && ledger.History.Count() == 1,
            "following native win consumes once with native DD timer phase");
        Field(owner, "_doubleDmgTimer", 1234); Field(owner, "_timeSincePickup", 47);
        Check(Judge(owner, claim) == verdict && Timer(owner, 0) == 1234
            && Convert.ToInt32(typeof(PlayerEntity).GetField("_timeSincePickup", Hidden)!.GetValue(owner)) == 47
            && ledger.History.Count() == 1, "following-phase retry never reruns native pickup effects");
        Retire(item); NetPickupNetwork.Reset();
    }

    private static void NativeWinAndRace(PlayerEntity owner, PlayerEntity other)
    {
        Advance(10); owner.ModForceForm(false); owner.Health = other.Health = 199;
        var item = Spawn(owner, ItemType.DoubleDamage);
        other.ModPlaceAt(owner.Position); RemotePose(other);
        NetPowerUpLedger.ObserveContact(owner); NetPowerUpLedger.ObserveContact(other);
        var winner = Claim(owner, item, NetSession.NetFrame);
        var loser = Claim(other, item, NetSession.NetFrame);
        Contact.Invoke(owner, null);
        Check(item.DespawnTimer == 0, "real native pickup wins before owner claim arrives");
        var record = NetPowerUpLedger.Ledger!.History.Single(g => g.Spawn == winner.Spawn);
        Field(owner, Timers[0], 1111); Field(owner, "_timeSincePickup", 39);
        Advance(); var ack = Judge(owner, winner);
        Check(ack.Result == PickupResult.Grant && ack.GrantSequence == record.Sequence
            && ack.GrantFrame == record.ContactFrame && Timer(owner, 0) == 1111,
            "late owner claim acknowledges native grant without renewal");
        var race = Judge(other, loser);
        Check(race.Result == PickupResult.Taken
            && NetPowerUpLedger.Ledger.History.Count(g => g.Spawn == winner.Spawn) == 1,
            "second player's captured overlapping contact cannot win the same incarnation");
        Retire(item); other.ModPlaceAt(owner.Position + new Vector3(30, 0, 0));
    }

    private static void AuthoredRespawn(PlayerEntity owner)
    {
        Advance(10); owner.ModForceForm(false);
        ItemSpawnEntity? template = null;
        foreach (var candidate in owner.OwningScene.GetItemSpawnEntities()) { template = candidate; break; }
        Check(template != null, "native room provides authored spawner template");
        object data = template!.Data;
        void Set(string name, object value) => typeof(ItemSpawnEntityData).GetField(name)!.SetValue(data, value);
        Set(nameof(ItemSpawnEntityData.Header), new EntityDataHeader(template.Data.Header.Type, 30001,
            owner.Position, Vector3.UnitY, Vector3.UnitZ));
        Set(nameof(ItemSpawnEntityData.ItemType), ItemType.DoubleDamage);
        Set(nameof(ItemSpawnEntityData.Enabled), (byte)1); Set(nameof(ItemSpawnEntityData.ParentId), -1);
        Set(nameof(ItemSpawnEntityData.NotifyEntityId), (short)-1); Set(nameof(ItemSpawnEntityData.CollectedMessage), Message.None);
        Set(nameof(ItemSpawnEntityData.SpawnDelay), (ushort)0); Set(nameof(ItemSpawnEntityData.SpawnInterval), (ushort)1);
        Set(nameof(ItemSpawnEntityData.MaxSpawnCount), (ushort)2);
        var scene = owner.OwningScene;
        var spawner = new ItemSpawnEntity((ItemSpawnEntityData)data, "", scene); scene.AddEntity(spawner); spawner.Process();
        var first = spawner.Item ?? throw new InvalidOperationException("Native authored item missing.");
        owner.ModPlaceAt(first.Position); NetPowerUpLedger.ObserveContact(owner);
        var claim = Claim(owner, first, NetSession.NetFrame);
        ushort quota = spawner.ModHealthState.SpawnCount, cooldown = spawner.ModHealthState.Cooldown;
        Check(Judge(owner, claim).Result == PickupResult.Grant && first.DespawnTimer == 0
            && spawner.ModHealthState.SpawnCount == quota && spawner.ModHealthState.Cooldown == cooldown,
            "native claim consumption leaves spawn quota and established cooldown intact");
        Advance(); Judge(owner, claim);
        Check(spawner.ModHealthState.SpawnCount == quota && spawner.ModHealthState.Cooldown == cooldown,
            "duplicate cannot restart native spawner cooldown or alter quota");
        Check(!first.Process() && spawner.Item == null, "native consumed item releases owner reference");
        Retire(first); spawner.Process(); spawner.Process();
        var second = spawner.Item ?? throw new InvalidOperationException("Native respawn did not occur.");
        Check(NetPowerUpLedger.TrySpawnKey(second, out var key) && key.SpawnId == claim.Spawn.SpawnId
            && key.Generation > claim.Spawn.Generation && spawner.ModHealthState.SpawnCount == 2,
            "same authored spawner uses a new monotonic incarnation");
        var oldIdentity = claim with { Key = claim.Key with { Sequence = ++_sequence } };
        Judge(owner, oldIdentity);
        Check(second.DespawnTimer != 0 && spawner.Item == second
            && NetPowerUpLedger.Ledger!.History.Count(g => g.Spawn == key) == 0,
            "fresh claim sequence with old incarnation cannot consume native respawn");
        Retire(second); scene.RemoveEntity(spawner);
    }

    private static void UnavailableAndIsolation(PlayerEntity owner)
    {
        Advance(10); owner.ModForceForm(false); owner.Health = 199;
        var item = Spawn(owner, ItemType.Cloak); var claim = Claim(owner, item, NetSession.NetFrame);
        Check(Judge(owner, claim).Result == PickupResult.HistoryUnavailable && item.DespawnTimer != 0,
            "unobserved owner pose cannot authorize pickup");
        Advance(); owner.Health = 0; NetPowerUpLedger.ObserveContact(owner);
        var dead = Claim(owner, item, NetSession.NetFrame);
        Check(Judge(owner, dead).Result == PickupResult.Invalid && item.DespawnTimer != 0,
            "actual dead owner contact cannot authorize pickup");
        owner.Health = 199; Advance(); NetPowerUpLedger.ObserveContact(owner);
        var alive = Claim(owner, item, NetSession.NetFrame);
        var wrongLife = alive with { Key = alive.Key with { Life = (ushort)(alive.Key.Life + 1), Sequence = ++_sequence } };
        Check(Judge(owner, wrongLife).Result == PickupResult.Invalid && item.DespawnTimer != 0,
            "wrong-life claim cannot mutate current item");
        var ledger = NetPowerUpLedger.Ledger!; int grants = ledger.History.Count();
        var role = NetSession.Role; bool authority = NetSession.IsAuthority;
        try
        {
            Session(nameof(NetSession.Role), NetRole.Client); Session(nameof(NetSession.IsAuthority), false);
            Check(Judge(owner, alive).Result == PickupResult.Invalid && item.DespawnTimer != 0
                && ledger.History.Count() == grants, "ordinary client cannot settle authority claim");
        }
        finally { Session(nameof(NetSession.Role), role); Session(nameof(NetSession.IsAuthority), authority); }
        using var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
        var replica = new Scene(new(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
            _ => { }, () => { }, new ReplaySceneServices(session, new ReplayReplicaState()), initializeRuntime: false);
        try
        {
            Check(Judge(owner, alive, replica).Result == PickupResult.Invalid
                && ReferenceEquals(NetPowerUpLedger.Ledger, ledger) && ledger.History.Count() == grants,
                "private replica cannot authorize a foreground grant");
        }
        finally { replica.DoCleanup(); }
        Retire(item);
    }

    internal static int Run(string room)
    {
        _checks = 0; _sequence = 0;
        bool script = NetTestScript.Enabled; var proof = NetShotProof.Mode;
        var sim = new ServerSim();
        try
        {
            if (!ServerSim.Available(out string reason)) { Console.Error.WriteLine(reason); return 1; }
            NetTestScript.Enabled = false;
            Check(sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }), "asset-backed authority room starts");
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte slot = 0; slot < 2; slot++)
            { roster.Slots[slot] = slot; roster.Generations[slot] = 1; roster.Hunters[slot] = (byte)Hunter.Samus; roster.Names[slot] = "A2 fixture " + slot; }
            NetSession.ApplyRoster(roster);
            for (int step = 0; step < 120; step++) sim.Step();
            var owner = PlayerEntity.Players[1]; var other = PlayerEntity.Players[0];
            Session(nameof(NetSession.Role), NetRole.Host); Session(nameof(NetSession.LocalSlot), 1);
            owner.Health = other.Health = 199; NetShotProof.Mode = ShotProofMode.Off;
            var scene = owner.OwningScene; scene.GameState.BalancedMode = false; scene.GameState.EnhancedHunters = false;
            scene.GameState.PrimeHunter = owner.SlotIndex;
            Check(owner.ModIsInPlay && NetSession.IsAuthority, "prime-hunter local fixture retains admitted native identity");
            TimedTypes(owner); QueuedNativePhase(owner); CurrentCameraBlock(owner); QueuedFollowingNativeContact(owner); NativeWinAndRace(owner, other); AuthoredRespawn(owner); UnavailableAndIsolation(owner);
            Check(sim.StepFailures == 0, "native fixture warmup has no simulation failures");
            Console.WriteLine($"PASS: pickup claim scene {_checks} native adapter assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { sim.Stop(); NetTestScript.Enabled = script; NetShotProof.Mode = proof; }
    }
}
