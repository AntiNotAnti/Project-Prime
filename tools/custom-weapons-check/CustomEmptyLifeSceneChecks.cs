using System.Diagnostics;
using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

// Reuses the real dedicated UDP fixture through reflection without changing its
// developer-loadout assertions. Native load ACKs, actor lane/health setup and the
// manually authored native FireEvent are explicit fixture seams. No custom grant,
// funding, certificate, private state or lifecycle table is injected.
internal static class CustomEmptyLifeSceneChecks
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed record Watch(ShotKey Key, FireEvent Fire);
    private sealed record Observation(WeaponOwnerContext Owner, uint Input, bool Source, CustomNativeSelection Selection,
        bool Bound, bool Private, bool HasWeapon, WeaponStateSnapshot State, uint Settled,
        bool NativeLedger, ShotDisposition Disposition, bool NativeProof, bool NativeBeam,
        ulong CustomEmissions, ulong Payments, ulong Contacts, long StepFailures);
    private static object Get(object value, string name)
        => value.GetType().GetField(name, All)?.GetValue(value)
            ?? value.GetType().GetProperty(name, All)?.GetValue(value)
            ?? throw new InvalidOperationException("Missing fixture member " + name);
    private static object? Call(object value, string name, params object?[] arguments)
    {
        var method = value.GetType().GetMethods(All).Single(m => m.Name == name);
        var parameters = method.GetParameters();
        var supplied = new object?[parameters.Length];
        for (int i = 0; i < supplied.Length; i++) supplied[i] = i < arguments.Length ? arguments[i] : parameters[i].DefaultValue;
        try { return method.Invoke(value, supplied); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static CustomStateMessage? State(object probe)
        => probe.GetType().GetField("State", All)!.GetValue(probe) is CustomStateMessage state ? state : null;

    internal static int Run(string data)
    {
        string cwd = Directory.GetCurrentDirectory();
        bool force = MapAudit.ForceEveryone, developer = NetSession.DeveloperCustomWeaponReadiness;
        bool playback = NetFireEvents.AcceptedPlaybackEnabled;
        var proof = NetShotProof.Mode; var timed = NetTimedShotProof.Mode;
        object? fixture = null;
        FieldInfo? sinkField = null;
        SnapshotSink? previousSink = null;
        int retiring = 0, checks = 0;
        Observation? observed = null;
        Watch? watch = null;
        try
        {
            Directory.SetCurrentDirectory(Path.GetFullPath(data)); Paths.UpdatePaths(); Paths.ChooseMphPath();
            Headless.Enter(); MapAudit.ForceEveryone = true; NetSession.DeveloperCustomWeaponReadiness = false;
            void Check(bool value, string label)
            { if (!value) throw new InvalidOperationException(label); checks++; Console.WriteLine("EMPTY LIFE PASS " + label); }
            var type = typeof(NetLobbyTest).GetNestedType("ActionTransportFixture", BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Actual dedicated action fixture missing.");
            var constructor = type.GetConstructors(All).Single();
            var parameters = constructor.GetParameters();
            object?[] args = parameters.Select(p => p.DefaultValue).ToArray();
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].Name == "completeRoom") args[i] = false;
                if (parameters[i].Name == "publicAvailability") args[i] = true;
            }
            // completeRoom:false deliberately avoids the old four/sixteen
            // WaitInitialStates assertion; we qualify the actual empty path.
            fixture = constructor.Invoke(args);
            var rig = Get(fixture, "Rig"); var server = (DedicatedServer)Get(rig, "Server");
            var first = Get(fixture, "First"); var second = Get(fixture, "Second");
            var a = Get(fixture, "A"); var b = Get(fixture, "B");
            var firstReady = Get(fixture, "FirstReady"); var secondReady = Get(fixture, "SecondReady");
            int firstSlot = (int)Get(first, "Slot"), secondSlot = (int)Get(second, "Slot");
            var scene = (Scene)Get(fixture, "_scene"); var sim = (ServerSim)Get(fixture, "_sim");
            void Wait(Func<bool> condition, string label, int ms = 6000) => Call(rig, "Wait", condition, label, ms);
            void Owner(Action<Scene> apply, string label) => Call(fixture, "Mutate", apply, label);
            void Send(object client, PacketType kind, byte[] bytes) => Call(client, "Send", kind, bytes);
            object Actor(object client) => Get(Get(fixture, "Latest"), ReferenceEquals(client, first) ? "First" : "Second");
            uint Next(object client)
            {
                var latest = Get(fixture, "Latest");
                return Math.Max((uint)Get(latest, "World") + 1, (uint)Get(Actor(client), "InputFrame") + 1);
            }
            void Fresh(object client) => Call(fixture, "Observe", client, Next(client), false);

            sinkField = typeof(NetSession).GetField("_snapshotSink", All)!;
            previousSink = (SnapshotSink)sinkField.GetValue(null)!;
            SnapshotSink observer = payload =>
            {
                previousSink(payload);
                if (Volatile.Read(ref retiring) != 0 || scene.CustomCombat is not { IsEligible: true } root
                    || NetPlayerLifecycle.Generation(firstSlot) == 0 || NetPlayerLifecycle.Get(firstSlot) == 0) return;
                var player = scene.Players.Items[firstSlot];
                var owner = new WeaponOwnerContext(NetSession.CurrentMatchId, NetSession.AuthorityEpoch, (byte)firstSlot,
                    NetPlayerLifecycle.Generation(firstSlot), NetPlayerLifecycle.Get(firstSlot));
                uint input = NetSession.RemoteIntents[firstSlot].Frame;
                bool source = root.TryGetSourceObservation(owner, input, out _);
                bool metadata = root.TryStateMetadata(player, out _, out uint settled, out _);
                bool canonical = root.TryPrivateState(player, out var state, out bool owns);
                var tracked = Volatile.Read(ref watch);
                bool ledger = false, nativeProof = false, beam = false; ShotDisposition disposition = default;
                if (tracked != null)
                {
                    ledger = NetFireEvents.CaptureLedger(firstSlot).TryGet(tracked.Key, out _, out disposition, out _);
                    nativeProof = NetTimedShotProof.TryGet(tracked.Key, tracked.Fire.SourceFrame, out _);
                    beam = player.EquipInfo.Beams.Any(p => p.ModLaunchKey == tracked.Key && p.ModComponentId != 0);
                }
                Volatile.Write(ref observed, new(owner, input, source, root.ReadNativeSourceSelection(owner, input),
                    metadata && root.IsActionLifeActive(owner), canonical, owns, state, settled,
                    ledger, disposition, nativeProof, beam, root.AcceptedEmissions, root.PositivePayments,
                    root.TerminalContacts, sim.StepFailures));
            };
            sinkField.SetValue(null, observer);
            var originalSecond = (Action<ReceivedPacket>?)second.GetType().GetField("PacketReceived", All)!.GetValue(second);
            var relays = new List<IntentPacket>();
            second.GetType().GetField("PacketReceived", All)!.SetValue(second, (Action<ReceivedPacket>)(packet =>
            {
                originalSecond?.Invoke(packet);
                if (packet.Type == PacketType.SlotIntent && packet.Payload.Length > 1 && packet.Payload[0] == firstSlot
                    && CompactIntentCodec.TryRead(packet.Payload[1..], out var intent)) relays.Add(intent);
            }));
            var privateLengths = new List<int>();
            var originalFirst = (Action<ReceivedPacket>?)first.GetType().GetField("PacketReceived", All)!.GetValue(first);
            first.GetType().GetField("PacketReceived", All)!.SetValue(first, (Action<ReceivedPacket>)(packet =>
            {
                originalFirst?.Invoke(packet);
                if (packet.Type == PacketType.CustomWeaponState) privateLengths.Add(packet.Payload.Length);
            }));

            Call(secondReady, "Send");
            Wait(() => (int)Get(secondReady, "Confirmations") > 0, "second genuine UDP peer confirms custom readiness");
            Fresh(first); Fresh(second);
            Wait(() => State(a) is { HasWeapon: false } && State(b) is { HasWeapon: false }
                && server.TryGetCustomReplicaReady(firstSlot, out bool ar, out _) && ar
                && server.TryGetCustomReplicaReady(secondSlot, out bool br, out _) && br,
                "both real UDP endpoints receive empty private states after durable recipient baselines");
            Wait(() => Volatile.Read(ref observed) is { Source: true, Bound: true, Private: true, HasWeapon: false,
                Selection: CustomNativeSelection.Native }, "empty actual life retains source history and sealed native selection");
            var emptyA = State(a)!.Value; var emptyB = State(b)!.Value;
            Check(!server.DeveloperCustomWeaponReadiness && !server.DeveloperCustomWeaponCombat
                && !NetSession.DeveloperCustomWeaponReadiness && server.RiftlineM7Available,
                "frozen public availability uses no developer capability or loadout override");
            Check(emptyA.State.Owner == Volatile.Read(ref observed)!.Owner && emptyA.State.Magazine == 0
                && emptyA.State.Reserve == 0 && emptyA.State.Phase == CustomWeaponPhase.Invalid
                && !emptyA.Selected.IsValid && emptyB.State.Magazine == 0 && emptyB.State.Reserve == 0
                && privateLengths.Count > 0 && privateLengths.All(n => n == 114),
                "actual private publication is114bytes with full current owners and explicit absence");
            var ready = (CustomWeaponReady)Get(firstReady, "Ready");
            var wire = new CustomReadinessWire(ready.Bootstrap, ready.Slot);
            var client = new CustomClientCombat(wire);
            byte[] stateBytes = new byte[CustomStateMessage.Size];
            // Re-encode the exact received authority value for detached producer
            // checks; the preceding UDP observation proves its actual origin.
            CustomCombatResults.Write(emptyA, stateBytes, wire.Manifest, wire.Definition);
            Check(client.Receive(PacketType.CustomWeaponState, stateBytes, out _)
                && client.InputProjection is { HasWeapon: false, Selected: false },
                "production client producer consumes the actual public empty state");
            uint sequence = 0;
            foreach (var kind in new[] { CustomWeaponActionKind.Equip, CustomWeaponActionKind.Fire, CustomWeaponActionKind.Reload })
            {
                Check(!client.TrySubmitControl(kind, 0, ++sequence, out var refused) && refused == default,
                    "empty producer refuses public control without inventing inventory: " + kind);
                var request = new CustomControlRequest(ready.Bootstrap, ready.Manifest, ready.Slot, emptyA.State.Owner.Life,
                    new(10), sequence, kind, 0);
                byte[] bytes = new byte[CustomControlRequest.Size]; request.Write(bytes);
                Send(first, PacketType.CustomWeaponControl, bytes); Fresh(first); Fresh(second);
            }
            var retention = Stopwatch.StartNew();
            Wait(() => retention.ElapsedMilliseconds >= 200, "actual empty admission remains unchanged across UDP processing");
            var untouched = Volatile.Read(ref observed)!;
            Check(untouched is { Bound: true, Private: true, HasWeapon: false, Settled: 0, CustomEmissions: 0, Payments: 0, Contacts: 0 }
                && State(a) is { HasWeapon: false, SettledSequence: 0 }
                && ((System.Collections.IDictionary)Get(a, "Receipts")).Count == 0,
                "manual protocol controls cannot grant ownership, settle a funded action or produce custom damage");

            Owner(_ => { NetFireEvents.AcceptedPlaybackEnabled = true; NetShotProof.Mode = ShotProofMode.Shadow;
                NetTimedShotProof.Mode = TimedGrantProofMode.Shadow; },
                "explicit fixture enables ordinary accepted native playback and Shadow proof observation");
            Fresh(first); Fresh(second);
            var actor = Actor(first); var other = Actor(second); var ownerNow = (WeaponOwnerContext)Get(actor, "Owner");
            uint sourceFrame = Next(first), ackFrame = (uint)Get(a, "SnapshotWorld");
            var body = (Vector3)Get(actor, "Position");
            var aim = ((Vector3)Get(other, "Position") + Vector3.UnitY * .2f - (body + Vector3.UnitY * .5f)).Normalized();
            var fire = new FireEvent(1, sourceFrame, ackFrame, 0, FireEventKind.PressFire, (byte)BeamType.PowerBeam, 0, 0,
                FireEvent.FlagPose, body + Vector3.UnitY * .5f, aim, aim, aim, SourceLife: ownerNow.Life);
            var key = new ShotKey(ownerNow.AuthorityEpoch, ownerNow.MatchId, firstSlot, ownerNow.Generation, ownerNow.Life, 1);
            Volatile.Write(ref watch, new(key, fire));
            var carrier = new IntentPacket { MatchId = ownerNow.MatchId, AuthorityEpoch = ownerNow.AuthorityEpoch,
                SlotGeneration = ownerNow.Generation, LifeId = ownerNow.Life, Frame = sourceFrame, AckFrame = ackFrame,
                Position = body, Aim = aim, Buttons = IntentButtons.InPlayState | IntentButtons.Shoot,
                WeaponSelect = (byte)BeamType.PowerBeam, HasState = true, HasFireEvents = true, HasAnalogMove = true,
                HasContinuousFireTick = true, FireEventCount = 1, ShotInputCount = 2 };
            carrier.FireEvents[0] = fire;
            carrier.ShotInputs[0] = ShotInputHistory.Sample(fire.Weapon, true, false, true);
            carrier.ShotInputs[1] = ShotInputHistory.Sample(fire.Weapon, false, false, true);
            byte[] nativeBytes = new byte[CompactIntentCodec.Size(carrier)]; CompactIntentCodec.Write(carrier, nativeBytes);
            Send(first, PacketType.Intent, nativeBytes);
            Wait(() => Volatile.Read(ref observed) is { NativeLedger: true, Disposition: ShotDisposition.Fired, NativeProof: true, NativeBeam: true }
                && relays.Any(p => Enumerable.Range(0, p.FireEventCount).Any(i => p.FireEvents[i].ShotId == 1)),
                "empty public life admits a real native event into ledger/proof/projectile and recipient relay");
            Check(Volatile.Read(ref observed) is { HasWeapon: false, Selection: CustomNativeSelection.Native, CustomEmissions: 0, Payments: 0 },
                "native fire does not create custom ownership or enter custom payment stores");

            var oldOwner = emptyA.State.Owner;
            Call(fixture, "RespawnFirst");
            Fresh(first); Fresh(second);
            Wait(() => State(a) is { HasWeapon: false } next && next.State.Owner != oldOwner
                && Volatile.Read(ref observed) is { Source: true, Bound: true, HasWeapon: false, Selection: CustomNativeSelection.Native },
                "actual canonical death and native Spawn publish a new empty private life and native source history");
            var respawn = State(a)!.Value;
            Check(respawn.State.Owner.Generation == oldOwner.Generation && respawn.State.Owner.Life != oldOwner.Life
                && respawn.Revision > emptyA.Revision && respawn.State.Magazine == 0 && respawn.State.Reserve == 0
                && !respawn.Selected.IsValid && Volatile.Read(ref observed)!.StepFailures == 0,
                "respawn never refills public inventory and actual dedicated engine reports zero step failures");
            Console.WriteLine($"PASS {checks} actual dedicated public empty-life checks.");
            Console.WriteLine("SCOPE: real UDP+DedicatedServer+ServerSim public-empty publication/source/native fire/respawn. Reflection reuses existing fixture; native scene ACKs, actor lane setup, authored native event and Shadow mode are declared seams. No custom funding, world pickup acquisition, rendered client or input UI is claimed. Old public four/sixteen combat fixture requires real pickup integration before it can qualify again.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("EMPTY LIFE SCENE FAIL " + error); return 1; }
        finally
        {
            Volatile.Write(ref retiring, 1);
            if (sinkField != null && previousSink != null) sinkField.SetValue(null, previousSink);
            (fixture as IDisposable)?.Dispose(); NetSession.Stop();
            NetSession.DeveloperCustomWeaponReadiness = developer; MapAudit.ForceEveryone = force;
            NetFireEvents.AcceptedPlaybackEnabled = playback; NetShotProof.Mode = proof; NetTimedShotProof.Mode = timed;
            Directory.SetCurrentDirectory(cwd);
        }
    }
}
