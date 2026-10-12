using System;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;
using MphRead.Mods.Network;
using MphRead.Mods.Input;

namespace MphRead.NetTest;

// Asset-backed native contact fixture. Contact positions and authority clocks are
// explicit fixture inputs; this is not a UDP, spawner-quota or movement campaign.
internal static class PowerUpLedgerSceneTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool ok, string label)
    { _checks++; if (!ok) throw new InvalidOperationException("Power-up scene: " + label); }
    private static void Session(string name, object value)
        => typeof(NetSession).GetProperty(name)!.SetValue(null, value);
    private static readonly MethodInfo Contact = typeof(PlayerEntity).GetMethod("PickUpItems", Private)!;
    private static readonly ItemType[] Types = { ItemType.DoubleDamage, ItemType.Cloak, ItemType.Deathalt };
    private static readonly string[] Timers = { "_doubleDmgTimer", "_cloakTimer", "_deathaltTimer" };
    private static int Timer(PlayerEntity player, int type)
        => Convert.ToInt32(typeof(PlayerEntity).GetField(Timers[type], Private)!.GetValue(player));
    private static ItemInstanceEntity Spawn(PlayerEntity player, ItemType type)
        => ItemSpawnEntity.SpawnItem(type, player.Position, player.NodeRef, 6000, player.OwningScene)
            ?? throw new InvalidOperationException("Native fixture power-up spawn was disabled.");

    private static void Spawner(PlayerEntity owner)
    {
        var scene = owner.OwningScene;
        ItemSpawnEntity? authored = null;
        foreach (var candidate in scene.GetItemSpawnEntities()) { authored = candidate; break; }
        Check(authored != null, "map supplies authored spawner template");
        // Preserve authored layout and native construction, but configure a short
        // two-incarnation quota so this bounded fixture tests the real Process.
        object data = authored!.Data;
        void Set(string name, object value) => typeof(ItemSpawnEntityData).GetField(name)!.SetValue(data, value);
        Set(nameof(ItemSpawnEntityData.Header), new EntityDataHeader(authored.Data.Header.Type, 30000,
            owner.Position, Vector3.UnitY, Vector3.UnitZ));
        Set(nameof(ItemSpawnEntityData.ItemType), ItemType.DoubleDamage);
        Set(nameof(ItemSpawnEntityData.Enabled), (byte)1);
        Set(nameof(ItemSpawnEntityData.ParentId), -1);
        Set(nameof(ItemSpawnEntityData.NotifyEntityId), (short)-1);
        Set(nameof(ItemSpawnEntityData.CollectedMessage), Message.None);
        Set(nameof(ItemSpawnEntityData.SpawnDelay), (ushort)2);
        Set(nameof(ItemSpawnEntityData.SpawnInterval), (ushort)3);
        Set(nameof(ItemSpawnEntityData.MaxSpawnCount), (ushort)2);
        var spawner = new ItemSpawnEntity((ItemSpawnEntityData)data, "", scene);
        scene.AddEntity(spawner);
        for (int tick = 0; tick < 3; tick++) spawner.Process();
        Check(spawner.Item == null && spawner.ModHealthState.Cooldown == 1, "native doubled spawn delay has not fired early");
        spawner.Process();
        var first = spawner.Item ?? throw new InvalidOperationException("Native first spawner incarnation missing.");
        Check(NetPowerUpLedger.TrySpawnKey(first, out _)
            && spawner.ModHealthState.SpawnCount == 1, "native delayed incarnation initializes and registers");
        NetPowerUpLedger.TrySpawnKey(first, out var firstKey);
        // Direct contact calls below intentionally isolate spawner consumption;
        // timer phase assertions use complete ProcessPlayer above.
        owner.ModForceForm(false); owner.ModPlaceAt(first.Position); Contact.Invoke(owner, null);
        Check(first.DespawnTimer == 0, "native contact consumes authored-owner item");
        Check(!first.Process() && spawner.Item == null, "native expired item releases its spawner reference");
        first.Destroy(); scene.RemoveEntity(first);
        for (int tick = 0; tick < 5; tick++) spawner.Process();
        Check(spawner.Item == null && spawner.ModHealthState.Cooldown == 1, "native doubled cooldown blocks early respawn");
        spawner.Process();
        var second = spawner.Item ?? throw new InvalidOperationException("Native second spawner incarnation missing.");
        Check(NetPowerUpLedger.TrySpawnKey(second, out var secondKey)
            && secondKey.SpawnId == firstKey.SpawnId && secondKey.Generation > firstKey.Generation
            && spawner.ModHealthState.SpawnCount == 2, "native second incarnation has monotonic identity and consumes quota");
        owner.ModPlaceAt(second.Position); Contact.Invoke(owner, null);
        Check(second.DespawnTimer == 0 && !second.Process(), "second native item retires");
        second.Destroy(); scene.RemoveEntity(second);
        for (int tick = 0; tick < 8; tick++) spawner.Process();
        Check(spawner.Item == null && spawner.ModHealthState.SpawnCount == 2,
            "native max-spawn quota blocks third incarnation after cooldown");
        scene.RemoveEntity(spawner); spawner.Destroy();
        // Populate the independent ledger bound synthetically, then let the
        // native spawner/factory prove refusal happens before map quota use.
        var ledger = NetPowerUpLedger.Ledger!;
        PowerUpSpawnKey release = default;
        while (ledger.SpawnCount < NetTimedGrants.SpawnCapacity)
            Check(ledger.RegisterSpawn(-999, TimedPowerUp.DoubleDamage, out release), "fixture fills bounded spawn reservations");
        Set(nameof(ItemSpawnEntityData.Header), new EntityDataHeader(authored.Data.Header.Type, 30001,
            owner.Position, Vector3.UnitY, Vector3.UnitZ));
        Set(nameof(ItemSpawnEntityData.SpawnDelay), (ushort)0);
        Set(nameof(ItemSpawnEntityData.MaxSpawnCount), (ushort)1);
        var bounded = new ItemSpawnEntity((ItemSpawnEntityData)data, "", scene);
        scene.AddEntity(bounded);
        long refused = ledger.CapacityRefused;
        bounded.Process();
        Check(bounded.Item == null && bounded.ModHealthState.SpawnCount == 0
            && ledger.CapacityRefused == refused + 1, "native capacity refusal consumes no authored quota");
        ledger.RemoveSpawn(release);
        bounded.Process();
        Check(bounded.Item != null && bounded.ModHealthState.SpawnCount == 1,
            "native spawner recovers after reservation capacity becomes available");
        var recovered = bounded.Item!;
        recovered.DespawnTimer = 0; recovered.Process(); recovered.Destroy(); scene.RemoveEntity(recovered);
        scene.RemoveEntity(bounded); bounded.Destroy();

    }
    private static void ReplicaIsolation(PlayerEntity owner)
    {
        var foreground = NetPowerUpLedger.Ledger
            ?? throw new InvalidOperationException("Replica isolation requires populated authority ledger.");
        int spawns = foreground.SpawnCount;
        var history = foreground.History.Select(g => (g.Sequence, g.Spawn, g.Life, g.Type,
            g.ContactFrame, g.SourceContactFrame, g.EndFrame, g.EndReason)).ToArray();
        long refused = foreground.CapacityRefused;
        using var session = new ReplayPlaybackSession(new PassiveReplaySessionHost());
        var scene = new Scene(new(256, 192), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
            _ => { }, () => { }, new ReplaySceneServices(session, new ReplayReplicaState()), initializeRuntime: false);
        try
        {
            Check(scene.Services.IsReplica && NetSession.IsAuthority,
                "real private replica services coexist with foreground authority");
            foreach (ItemType type in Types)
            {
                var item = new ItemInstanceEntity(new ItemInstanceEntityData(owner.Position, type, 6000),
                    owner.NodeRef, scene);
                scene.AddEntity(item);
                Check(!NetPowerUpLedger.TrySpawnKey(item, out _) && NetPowerUpLedger.Ledger == foreground
                    && foreground.SpawnCount == spawns && foreground.CapacityRefused == refused,
                    "private replica native item initialization cannot register authority spawn " + type);
                item.Destroy(); scene.RemoveEntity(item);
                Check(NetPowerUpLedger.Ledger == foreground && foreground.SpawnCount == spawns
                    && foreground.History.Select(g => (g.Sequence, g.Spawn, g.Life, g.Type,
                        g.ContactFrame, g.SourceContactFrame, g.EndFrame, g.EndReason)).SequenceEqual(history),
                    "private replica item destruction cannot mutate foreground grant history " + type);
            }
        }
        finally { scene.DoCleanup(); }
    }

    private static void DisabledAndOffline(PlayerEntity owner)
    {
        var saved = NetSession.ServerSession;
        try
        {
            Session(nameof(NetSession.ServerSession), new SessionStatePacket
            {
                MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
                Phase = SessionPhase.InMatch, Match = new MatchDefinition { DisablePowerups = true }
            });
            foreach (ItemType type in Types)
                Check(ItemSpawnEntity.SpawnItem(type, owner.Position, owner.NodeRef, 6000, owner.OwningScene) == null,
                    "disabled-powerups native factory blocks " + type);
        }
        finally { typeof(NetSession).GetProperty(nameof(NetSession.ServerSession))!.SetValue(null, saved); }
        NetPowerUpLedger.Reset();
        var oldRole = NetSession.Role;
        bool oldAuthority = NetSession.IsAuthority;
        try
        {
            foreach (var role in new[] { NetRole.Offline, NetRole.Client })
            {
                NetPowerUpLedger.Reset();
                Session(nameof(NetSession.Role), role);
                Session(nameof(NetSession.IsAuthority), false);
                owner.ModForceForm(false);
                var item = Spawn(owner, ItemType.DoubleDamage);
                Contact.Invoke(owner, null);
                Check(Timer(owner, 0) == 1800 && item.DespawnTimer == 0 && NetPowerUpLedger.Ledger == null,
                    $"{role} native pickup/prediction remains functional without authority ledger");
            }
        }
        finally { Session(nameof(NetSession.Role), oldRole); Session(nameof(NetSession.IsAuthority), oldAuthority); }
    }

    public static int Run(string profile, string room)
    {
        _checks = 0;
        if (!ServerSim.Available(out string unavailable)) { Console.Error.WriteLine(unavailable); return 1; }
        var oldProof = NetShotProof.Mode;
        bool oldScript = NetTestScript.Enabled;
        var sim = new ServerSim();
        try
        {
            NetTestScript.Enabled = false;
            Check(sim.Start(room, GameMode.Battle, 2, _ => { }, () => { }), "native room starts");
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = room, Mode = (byte)GameMode.Battle }, false);
            var roster = RosterPacket.Create();
            roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1; roster.Count = 2;
            for (byte slot = 0; slot < 2; slot++)
            { roster.Slots[slot] = slot; roster.Generations[slot] = 1; roster.Hunters[slot] = (byte)Hunter.Samus; roster.Names[slot] = "A1 fixture " + slot; }
            NetSession.ApplyRoster(roster);
            for (int i = 0; i < 120; i++) sim.Step();
            var owner = PlayerEntity.Players[1]; var other = PlayerEntity.Players[0];
            var scene = owner.OwningScene;
            scene.GameState.BalancedMode = profile.Equals("balanced", StringComparison.OrdinalIgnoreCase);
            scene.GameState.EnhancedHunters = profile.Equals("enhanced", StringComparison.OrdinalIgnoreCase);
            scene.GameState.PrimeHunter = profile.Equals("prime-hunter", StringComparison.OrdinalIgnoreCase) ? owner.SlotIndex : -1;
            Session(nameof(NetSession.Role), NetRole.Host);
            Session(nameof(NetSession.LocalSlot), 1);
            owner.Health = other.Health = 199;
            Check(owner.ModIsInPlay && NetPlayerLifecycle.Get(1) != 0 && NetSession.IsAuthority,
                "real native authority player has matching admitted life");
            // Suppress incidental room pickup contacts without replacing native
            // entities or callbacks used for the spawned fixture items.
            foreach (var item in scene.GetItemInstanceEntities()) item.DespawnTimer = 0;
            uint frame = 1000;
            foreach (ShotProofMode proof in new[] { ShotProofMode.Off, ShotProofMode.Shadow, ShotProofMode.Enforce })
            {
                NetShotProof.Mode = proof;
                NetPowerUpLedger.Reset(); NetPowerUpLedger.BeginRoom();
                Session(nameof(NetSession.NetFrame), frame - 1);
                // Exercise the real remote-owner boolean seam. Temporarily mark
                // this actor remote, then restore the local source-clock seam.
                Session(nameof(NetSession.LocalSlot), 0);
                int authorityDouble = Timer(owner, 0);
                owner.ModSetShotState(0, 0, true, false);
                Check(Timer(owner, 0) == authorityDouble, "reported double-damage bit cannot create or renew authority timer");
                Session(nameof(NetSession.LocalSlot), 1);
                NetPowerUpLedger.ObserveInput(owner);
                var noContact = NetPowerUpLedger.Ledger!;
                var noContactLife = ShotKey.For(owner.SlotIndex, 0);
                Check(noContact.GrantedAt(noContactLife, frame - 1, TimedPowerUp.DoubleDamage, frame - 1).Knowledge
                    == TimedGrantKnowledge.NotGranted && noContact.HistoryCount == 0,
                    "reported double-damage bit without item contact never establishes ownership");
                // A native inherited timer is equally insufficient evidence.
                typeof(PlayerEntity).GetField("_cloakTimer", Private)!.SetValue(owner, (ushort)32);
                Session(nameof(NetSession.NetFrame), frame);
                NetPowerUpLedger.ObserveInput(owner);
                Check(noContact.GrantedAt(noContactLife, frame, TimedPowerUp.Cloak, frame).Knowledge
                    == TimedGrantKnowledge.NotGranted && noContact.HistoryCount == 0,
                    "inherited cloak timer without contact never establishes ownership");
                typeof(PlayerEntity).GetField("_cloakTimer", Private)!.SetValue(owner, (ushort)0);
                Session(nameof(NetSession.LocalSlot), 0); owner.ModSetShotState(0, 0, false, false);
                Check(Timer(owner, 0) == authorityDouble, "reported false bit cannot remove authority timer");
                Session(nameof(NetSession.LocalSlot), 1);
                // Keep the no-contact observation distinct from first contact.
                frame++;
                for (int type = 0; type < Types.Length; type++)
                {
                    owner.ModForceForm(false);
                    Session(nameof(NetSession.NetFrame), frame);
                    var item = Spawn(owner, Types[type]);
                    Check(NetPowerUpLedger.TrySpawnKey(item, out var spawn), "native initialized item registers " + Types[type]);
                    Check(owner.ProcessPlayer(), "actual native contact-frame player phases run");
                    var ledger = NetPowerUpLedger.Ledger!;
                    var grant = ledger.History.Single(g => g.Spawn == spawn);
                    Check(Timer(owner, type) == (type == 0 ? 1799 : 1800) && item.DespawnTimer == 0,
                        $"{proof} {Types[type]} contact-frame native phase preserves authored timer ordering");
                    Check(grant.Life == ShotKey.For(1, 0) && grant.ContactFrame == frame
                        && grant.SourceContactFrame == frame && grant.EffectiveFrame == frame + 1
                        && grant.ExpiresAtFrame == frame + 1800,
                        "grant matches real life and contact/input phase boundaries");
                    int count = ledger.HistoryCount;
                    other.ModPlaceAt(owner.Position); Contact.Invoke(other, null);
                    Check(ledger.HistoryCount == count, "same consumed item cannot grant a second contender");
                    ledger.ObserveInput(grant.Life, frame, frame);
                    Check(ledger.GrantedAt(grant.Life, frame, grant.Type, frame).Knowledge == TimedGrantKnowledge.NotGranted,
                        "contact-frame input is not retroactively empowered");
                    ledger.ObserveInput(grant.Life, frame + 1, frame + 1);
                    Check(ledger.GrantedAt(grant.Life, frame + 1, grant.Type, frame + 1).Knowledge == TimedGrantKnowledge.Granted,
                        "following observed input owns grant");
                    Session(nameof(NetSession.NetFrame), frame + 10);
                    owner.ModForceForm(false);
                    var renewal = Spawn(owner, Types[type]);
                    Check(NetPowerUpLedger.TrySpawnKey(renewal, out var renewedSpawn)
                        && renewedSpawn.Generation > spawn.Generation && renewedSpawn.SpawnId == spawn.SpawnId,
                        "dynamic incarnations have monotonic identities despite shared fallback spawn ID");
                    Check(owner.ProcessPlayer(), "actual native renewal-contact player phases run");
                    var renewed = ledger.History.Single(g => g.Spawn == renewedSpawn);
                    Check(Timer(owner, type) == (type == 0 ? 1799 : 1800) && renewed.Sequence > grant.Sequence
                        && grant.EndReason == TimedGrantEnd.Renewed && grant.EndFrame == frame + 11,
                        "same-life native renewal refreshes timer and closes prior history at next input");
                    Vector3 safePosition = owner.Position;
                    other.ModPlaceAt(safePosition + Vector3.UnitX * 20);
                    // Invoke actual native player phases, with fixture position
                    // held safe each tick. This tests timers, not locomotion.
                    for (uint elapsed = 1; elapsed <= 1800; elapsed++)
                    {
                        uint at = frame + 10 + elapsed;
                        Session(nameof(NetSession.NetFrame), at);
                        owner.ModPlaceAt(safePosition); owner.Health = 199;
                        Check(owner.ProcessPlayer(), "native timer tick keeps actor active");
                        if (elapsed == 1 || elapsed == 1799 || elapsed == 1800)
                        {
                            Check(Timer(owner, type) == Math.Max(0, (type == 0 ? 1799 : 1800) - (int)elapsed),
                                $"{Types[type]} actual native timer after {elapsed} ticks");
                            ledger.ObserveInput(renewed.Life, at, at);
                            Check(ledger.GrantedAt(renewed.Life, at, renewed.Type, at).Knowledge
                                == (elapsed == 1800 ? TimedGrantKnowledge.NotGranted : TimedGrantKnowledge.Granted),
                                "input ownership respects native phase (DD terminal decrement follows final authorized input)");
                        }
                    }
                    Check(renewed.EndReason == TimedGrantEnd.Expired,
                        "renewed history expires with native terminal timer");
                    frame += 2000;
                }
            }
            Spawner(owner);
            ReplicaIsolation(owner);
            DisabledAndOffline(owner);
            Check(sim.StepFailures == 0, "native setup has no simulation failures");
            Console.WriteLine($"PASS: {_checks} asset-backed power-up contact/ledger assertions ({profile})");
            Console.WriteLine("UNEXERCISED: full recorded replica playback, all no-powerup game modes, actual native input/UDP ownership reconciliation; timer poses and spawner delay/quota are fixture inputs.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { sim.Stop(); NetSession.Stop(); NetPowerUpLedger.Reset(); NetShotProof.Mode = oldProof; NetTestScript.Enabled = oldScript; }
    }
}
