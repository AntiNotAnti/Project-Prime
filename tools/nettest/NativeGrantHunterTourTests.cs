using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Genuine factories/contact/player phases with declared native item placement
// and a stationary local authority body. No synthetic grant/source admissions,
// authored UDP, target impact, natural movement or rendered pixel claim.
internal static class NativeGrantHunterTourTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    private static readonly Hunter[] Hunters = { Hunter.Samus, Hunter.Kanden, Hunter.Spire,
        Hunter.Noxus, Hunter.Trace, Hunter.Sylux, Hunter.Weavel };
    private static readonly ItemType[] Types = { ItemType.DoubleDamage, ItemType.Cloak, ItemType.Deathalt };
    private sealed record Arm(Hunter Hunter, ItemType Item, TimedGrantProofMode Mode,
        PowerUpSpawnKey Spawn, ShotKey Life, ulong GrantSequence, uint ContactFrame,
        uint EffectiveFrame, uint ExpiryFrame, int ContactTimer, int EffectiveTimer,
        int LastActiveTimer, int ExpiredTimer, TimedShotNativeTests.NativeShot Shot,
        ulong NativeFrames, bool NativePresentation, string? CertificateKnowledge);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true,
        Converters = { new JsonStringEnumConverter() } };
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("Native grant hunter tour: " + label);
        _checks++;
    }
    private static object? Field(PlayerEntity owner, string name)
        => typeof(PlayerEntity).GetField(name, Hidden)!.GetValue(owner);
    private static int Timer(PlayerEntity owner, TimedPowerUp type)
        => Convert.ToInt32(Field(owner, type switch {
            TimedPowerUp.DoubleDamage => "_doubleDmgTimer", TimedPowerUp.Cloak => "_cloakTimer", _ => "_deathaltTimer" }));

    // Never pass OpenTK vectors to general object serialization: their computed
    // vector properties recursively expose further vectors. Preserve raw bytes
    // and scalar coordinates, without reflecting arbitrary engine objects.
    private static object KeyDto(ShotKey key) => new { key.AuthorityEpoch, key.MatchId,
        key.ShooterSlot, key.Generation, key.LifeId, key.ShotId };
    private static object SpawnDto(PowerUpSpawnKey spawn) => new { spawn.Epoch, spawn.Match,
        spawn.Room, spawn.SpawnId, spawn.Generation };
    private static object FireDto(FireEvent fire)
    {
        byte[] bytes = new byte[FireEvent.Size]; fire.Write(bytes);
        return new { fire.ShotId, fire.SourceFrame, fire.AckFrame, fire.AckSubFrame, fire.Kind,
            fire.Weapon, fire.Charge, fire.ContinuousPhase, fire.PoseFlags, fire.SourceLife,
            Origin = new[] { fire.Origin.X, fire.Origin.Y, fire.Origin.Z },
            Direction = new[] { fire.Direction.X, fire.Direction.Y, fire.Direction.Z },
            Aim = new[] { fire.Aim.X, fire.Aim.Y, fire.Aim.Z },
            View = new[] { fire.View.X, fire.View.Y, fire.View.Z },
            Reticle = new[] { fire.Reticle.X, fire.Reticle.Y },
            Target = new { fire.Target.EncodedSlot, fire.Target.Generation, fire.Target.LifeId },
            CanonicalBytes = Convert.ToHexString(bytes) };
    }
    private static object ArmDto(Arm arm) => new { arm.Hunter, arm.Item, arm.Mode,
        Spawn = SpawnDto(arm.Spawn), Life = KeyDto(arm.Life), arm.GrantSequence, arm.ContactFrame,
        arm.EffectiveFrame, arm.ExpiryFrame, arm.ContactTimer, arm.EffectiveTimer,
        arm.LastActiveTimer, arm.ExpiredTimer, arm.NativeFrames, arm.NativePresentation,
        arm.CertificateKnowledge, Shot = new { Key = KeyDto(arm.Shot.Key),
            Fire = FireDto(arm.Shot.Fire), arm.Shot.Outcome } };

    private static object ByteDifference(byte[] off, byte[] shadow)
    {
        int at = 0;
        while (at < Math.Min(off.Length, shadow.Length) && off[at] == shadow[at]) at++;
        bool same = at == off.Length && at == shadow.Length;
        int start = Math.Max(0, at - 8);
        string Context(byte[] bytes) => start >= bytes.Length ? "" : Convert.ToHexString(bytes.AsSpan(start, Math.Min(24, bytes.Length - start)));
        string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new { Equal = same, OffLength = off.Length, ShadowLength = shadow.Length,
            FirstDifference = same ? -1 : at, ContextStart = start, OffContext = Context(off),
            ShadowContext = Context(shadow), OffSha256 = Hash(off), ShadowSha256 = Hash(shadow) };
    }
    private static object IntentDto(string hex)
    {
        byte[] bytes = Convert.FromHexString(hex);
        IntentPacket intent = IntentPacket.Read(bytes);
        return new { intent.MatchId, intent.AuthorityEpoch, intent.SlotGeneration, intent.LifeId,
            intent.Frame, intent.AckFrame, intent.AckSubFrame, intent.Buttons, intent.WeaponSelect,
            intent.AmmoUa, intent.AmmoMissiles, intent.ChargeLevel, intent.BoostDamage, intent.ShotFlags,
            intent.ContinuousFireTick, intent.FireEventCount,
            Position = new[] { intent.Position.X, intent.Position.Y, intent.Position.Z },
            Aim = new[] { intent.Aim.X, intent.Aim.Y, intent.Aim.Z },
            Fires = Enumerable.Range(0, intent.FireEventCount).Select(i => FireDto(intent.FireEvents[i])).ToArray() };
    }
    private static object ReplayDifference(string off, string shadow)
    {
        using var first = JsonDocument.Parse(off); using var second = JsonDocument.Parse(shadow);
        int count = first.RootElement.GetArrayLength(), other = second.RootElement.GetArrayLength();
        Check(count <= 32 && other <= 32, "parity replay diagnostics stay bounded to real callback budget");
        int index = 0;
        while (index < Math.Min(count, other) && first.RootElement[index].GetRawText() == second.RootElement[index].GetRawText()) index++;
        object? Detail(JsonElement record) => new { Kind = record.GetProperty("Kind").Clone(),
            RecordingFrame = record.GetProperty("RecordingFrame").GetUInt32(), ServerTick = record.GetProperty("ServerTick").GetUInt32(),
            Marker = record.GetProperty("Marker").Clone(), PayloadBytes = record.GetProperty("Payload").GetString()!.Length / 2 };
        var a = index < count ? first.RootElement[index] : default;
        var b = index < other ? second.RootElement[index] : default;
        return new { OffCount = count, ShadowCount = other, FirstDifference = index == count && index == other ? -1 : index,
            Off = index < count ? Detail(a) : null, Shadow = index < other ? Detail(b) : null,
            PayloadDifference = index < count && index < other ? ByteDifference(Convert.FromHexString(a.GetProperty("Payload").GetString()!),
                Convert.FromHexString(b.GetProperty("Payload").GetString()!)) : null };
    }
    private static object PairDiagnostic(Arm off, Arm shadow) => new {
        off.Hunter, off.Item, OutcomeEqual = off.Shot.Outcome == shadow.Shot.Outcome,
        SpawnEqual = off.Spawn == shadow.Spawn, LifeEqual = off.Life == shadow.Life,
        OffSpawn = SpawnDto(off.Spawn), ShadowSpawn = SpawnDto(shadow.Spawn),
        OffLife = KeyDto(off.Life), ShadowLife = KeyDto(shadow.Life),
        OffGrant = off.GrantSequence, ShadowGrant = shadow.GrantSequence,
        OffContact = off.ContactFrame, ShadowContact = shadow.ContactFrame,
        OffTimers = new[] { off.ContactTimer, off.EffectiveTimer, off.LastActiveTimer, off.ExpiredTimer },
        ShadowTimers = new[] { shadow.ContactTimer, shadow.EffectiveTimer, shadow.LastActiveTimer, shadow.ExpiredTimer },
        OffFrames = off.NativeFrames, ShadowFrames = shadow.NativeFrames,
        OffShotKey = KeyDto(off.Shot.Key), ShadowShotKey = KeyDto(shadow.Shot.Key),
        OffFire = FireDto(off.Shot.Fire), ShadowFire = FireDto(shadow.Shot.Fire),
        OffDamage = off.Shot.Outcome.Damage, ShadowDamage = shadow.Shot.Outcome.Damage,
        OffLifespan = off.Shot.Outcome.Lifespan, ShadowLifespan = shadow.Shot.Outcome.Lifespan,
        OffAmmo = off.Shot.Outcome.Ammo, ShadowAmmo = shadow.Shot.Outcome.Ammo,
        OffCounts = new[] { off.Shot.Outcome.Fired, off.Shot.Outcome.Semantic, off.Shot.Outcome.ReplayWeaponFired, off.Shot.Outcome.ReplayShotFacts },
        ShadowCounts = new[] { shadow.Shot.Outcome.Fired, shadow.Shot.Outcome.Semantic, shadow.Shot.Outcome.ReplayWeaponFired, shadow.Shot.Outcome.ReplayShotFacts },
        CanonicalIntentDifference = ByteDifference(Convert.FromHexString(off.Shot.Outcome.IntentBytes), Convert.FromHexString(shadow.Shot.Outcome.IntentBytes)),
        OffIntent = IntentDto(off.Shot.Outcome.IntentBytes), ShadowIntent = IntentDto(shadow.Shot.Outcome.IntentBytes),
        ReplayDifference = ReplayDifference(off.Shot.Outcome.ReplayFacts, shadow.Shot.Outcome.ReplayFacts) };

    private static Arm RunArm(string room, Hunter hunter, ItemType itemType, TimedGrantProofMode mode)
    {
        using var fixture = new TimedShotNativeTests.Fixture(room, ShotProofMode.Off, mode, hunter);
        var owner = fixture.Owner;
        ulong frameBefore = fixture.Scene.FrameCount;
        Vector3 nativeSpawn = owner.Position;
        // Pose and zero speed are explicit fixture inputs for a timer tour.
        // Health, form and timer values are never assigned to manufacture a pass.
        fixture.BeforeStep = () => { owner.ModPlaceAt(nativeSpawn); owner.Speed = Vector3.Zero; };
        Check(owner.Hunter == hunter && !owner.IsBot && owner.Health > 0 && owner.ModIsInPlay,
            "native admitted actor has requested hunter " + hunter);
        Check(NetPowerUpLedger.TryType(itemType, out var type), "actual native timed item kind " + itemType);
        var item = ItemSpawnEntity.SpawnItem(itemType, owner.Position, owner.NodeRef, 6000, fixture.Scene)
            ?? throw new InvalidOperationException("Real native item factory refused " + itemType);
        Check(item.ItemType == itemType && item.OwningScene == fixture.Scene && item.Initialized
            && NetPowerUpLedger.TrySpawnKey(item, out _), "actual native item initialized and registered");
        NetPowerUpLedger.TrySpawnKey(item, out var spawn);
        var life = ShotKey.For(owner.SlotIndex, 0);
        Check(spawn.Epoch == life.AuthorityEpoch && spawn.Match == life.MatchId
            && spawn.Room != 0 && spawn.Generation != 0, "full native spawn incarnation shares actual admitted stream");
        uint contact = unchecked(NetSession.NetFrame + 1);
        TimedGrantRecord? grant = null;
        int contactTimer = -1, effectiveTimer = -1;

        void CheckObservation(uint frame, TimedGrantKnowledge expected)
        {
            var ledger = NetPowerUpLedger.Ledger!;
            Check(ledger.TryReadObservation(life, frame, type, out var answer, out uint authority)
                && authority == frame && answer.Knowledge == expected,
                $"{hunter}/{itemType} actual observed input {frame - contact} is {expected}");
            if (expected == TimedGrantKnowledge.Granted)
                Check(grant != null && answer.Sequence == grant.Sequence, "observed typed grant identifies exact native reservation");
            Check(!ledger.TryReadObservation(life with { LifeId = unchecked((ushort)(life.LifeId + 1)) },
                frame, type, out _, out _), "wrong-life query cannot borrow genuine native input truth");
        }
        void ContactCompleted()
        {
            Check(NetSession.NetFrame == contact && item.DespawnTimer == 0, "complete native contact consumes actual item once");
            grant = NetPowerUpLedger.Ledger!.History.Single(value => value.Spawn == spawn);
            Check(grant.Life == life && grant.Type == type && grant.ContactFrame == contact
                && grant.SourceContactFrame == contact && grant.EffectiveFrame == unchecked(contact + 1)
                && grant.ExpiresAtFrame == unchecked(contact + NetTimedGrants.Duration),
                "native reservation retains complete incarnation/life/contact chronology");
            contactTimer = Timer(owner, type);
            Check(contactTimer == (type == TimedPowerUp.DoubleDamage ? 1799 : 1800), "actual contact-frame native timer phase");
            CheckObservation(contact, TimedGrantKnowledge.NotGranted);
            if (type == TimedPowerUp.Cloak)
                Check(owner.Flags2.TestFlag(PlayerFlags2.Cloaking), "genuine Cloak contact activates native presentation flag");
            if (type == TimedPowerUp.DeathAlt)
                Check(owner.IsAltForm || owner.IsMorphing, "genuine Death Alt contact invokes native form transition");
        }
        void EffectiveCompleted()
        {
            Check(NetSession.NetFrame == unchecked(contact + 1), "following input completed at exact C+1");
            CheckObservation(unchecked(contact + 1), TimedGrantKnowledge.Granted);
            effectiveTimer = Timer(owner, type);
            Check(effectiveTimer == (type == TimedPowerUp.DoubleDamage ? 1798 : 1799), "actual C+1 native timer phase");
            if (type == TimedPowerUp.Cloak)
                Check(owner.Flags2.TestFlag(PlayerFlags2.Cloaking) && Convert.ToSingle(Field(owner, "_targetAlpha")) < 1,
                    "genuine Cloak reaches native translucent target alpha");
            if (type == TimedPowerUp.DeathAlt)
                Check((owner.IsAltForm || owner.IsMorphing) && Field(owner, "_deathaltEffect") != null,
                    "genuine Death Alt reaches native form and actual effect owner");
        }

        TimedShotNativeTests.NativeShot shot;
        if (type == TimedPowerUp.DeathAlt)
        {
            // Death Alt forbids normal firing after its forced morph. Fire in
            // the actual C input phase before contact, without forcing a biped
            // or pretending an alt-form event was an admitted PowerBeam.
            shot = fixture.Fire(ContactCompleted);
            EffectiveCompleted();
            Check(shot.Fire.SourceFrame == contact, "Death Alt shot precedes real contact within C input phase");
        }
        else
        {
            fixture.Step(); ContactCompleted();
            shot = fixture.Fire(EffectiveCompleted);
            Check(shot.Fire.SourceFrame == unchecked(contact + 1), "actual native C+1 shot follows real contact");
        }
        Check(shot.Outcome.Fired == 1 && shot.Outcome.Semantic == 1 && shot.Outcome.Damage > 0
            && shot.Outcome.ReplayWeaponFired == 1 && shot.Outcome.IntentBytes.Length > 0 && shot.Outcome.ReplayFacts.Length > 0,
            "native keyboard/spawn/commit/semantic/intent/replay positive shot denominators");
        string? certificateKnowledge = null;
        if (mode == TimedGrantProofMode.Shadow)
        {
            var expected = type == TimedPowerUp.DoubleDamage ? TimedGrantKnowledge.Granted : TimedGrantKnowledge.NotGranted;
            var certificate = TimedShotNativeTests.Certificate(shot, expected);
            certificateKnowledge = certificate.Knowledge.ToString();
            Check(certificate.Components > 0 && certificate.Lane == TimedShotSource.NativeInput && certificate.TrustedSource,
                "actual successful native component owns a trusted shot certificate");
            if (type == TimedPowerUp.DoubleDamage)
                Check(certificate.AuthorizedDouble && certificate.Funding is { } funding
                    && funding.Spawn == spawn && funding.Sequence == grant!.Sequence && funding.ContactFrame == contact,
                    "real C+1 Double shot is funded only by exact native item incarnation");
            else Check(!certificate.AuthorizedDouble && certificate.Funding == null,
                "genuine Cloak/Death Alt grants cannot become a Double multiplier");
            Check(!NetTimedShotProof.TryGet(shot.Key with { Generation = unchecked((ushort)(shot.Key.Generation + 1)) },
                shot.Fire.SourceFrame, out _), "wrong generation cannot borrow emitted certificate");
        }
        else Check(NetTimedShotProof.Stored == 0 && !NetTimedShotProof.TryGet(shot.Key, shot.Fire.SourceFrame, out _),
            "Off records no timed certificate despite successful native fire");

        fixture.Until(unchecked(contact + 1799));
        CheckObservation(unchecked(contact + 1799), TimedGrantKnowledge.Granted);
        int last = Timer(owner, type);
        Check(last == (type == TimedPowerUp.DoubleDamage ? 0 : 1), "actual native final authorized-input timer ordering");
        fixture.Until(unchecked(contact + 1800));
        CheckObservation(unchecked(contact + 1800), TimedGrantKnowledge.NotGranted);
        int expired = Timer(owner, type);
        TimedGrantRecord completedGrant = grant ?? throw new InvalidOperationException("Native contact has no reservation.");
        Check(expired == 0 && completedGrant.EndReason == TimedGrantEnd.Expired && completedGrant.EndFrame == unchecked(contact + 1800),
            "complete native timer reaches truthful terminal expiry without clock skipping");
        if (type == TimedPowerUp.DoubleDamage) Check(!owner.DoubleDamage, "native Double presentation ends");
        if (type == TimedPowerUp.Cloak)
            Check(!owner.Flags2.TestFlag(PlayerFlags2.Cloaking) && Convert.ToSingle(Field(owner, "_targetAlpha")) == 1,
                "native Cloak flag/alpha target ends at expiry");
        if (type == TimedPowerUp.DeathAlt)
        {
            fixture.Step();
            Check(Field(owner, "_deathaltEffect") == null, "native Death Alt effect owner unlinks in its next ordinary phase");
        }
        ulong frames = fixture.Scene.FrameCount - frameBefore;
        Check(frames == (type == TimedPowerUp.DeathAlt ? 1802u : 1801u) && owner.Health > 0 && owner.ModIsInPlay,
            "every ordinary native phase advances and the real admitted actor remains alive");
        return new(hunter, itemType, mode, spawn, life, completedGrant.Sequence, contact, completedGrant.EffectiveFrame,
            completedGrant.ExpiresAtFrame, contactTimer, effectiveTimer, last, expired, shot, frames, true, certificateKnowledge);
    }

    internal static int Run(string room, string? output)
    {
        _checks = 0;
        var oldProof = NetShotProof.Mode; var oldTimed = NetTimedShotProof.Mode; bool script = NetTestScript.Enabled;
        var arms = new List<Arm>();
        int parityPairs = 0;
        string status = "FAIL";
        string? failure = null;
        object? parityDiagnostic = null;
        if (output != null && File.Exists(output))
        { Console.Error.WriteLine("Native grant hunter tour output must be fresh."); return 1; }
        try
        {
            Check(ServerSim.Available(out string reason), reason);
            foreach (Hunter hunter in Hunters)
            foreach (ItemType type in Types)
            {
                var off = RunArm(room, hunter, type, TimedGrantProofMode.Off); arms.Add(off);
                var shadow = RunArm(room, hunter, type, TimedGrantProofMode.Shadow); arms.Add(shadow);
                bool parity = off.Shot.Outcome == shadow.Shot.Outcome && off.Spawn == shadow.Spawn && off.Life == shadow.Life
                    && off.GrantSequence == shadow.GrantSequence && off.ContactFrame == shadow.ContactFrame
                    && off.ContactTimer == shadow.ContactTimer && off.EffectiveTimer == shadow.EffectiveTimer
                    && off.LastActiveTimer == shadow.LastActiveTimer && off.ExpiredTimer == shadow.ExpiredTimer
                    && off.NativeFrames == shadow.NativeFrames;
                if (!parity)
                {
                    parityDiagnostic = PairDiagnostic(off, shadow);
                    Console.Error.WriteLine("NATIVE GRANT TOUR PARITY DIAGNOSTIC " + JsonSerializer.Serialize(parityDiagnostic, Json));
                }
                Check(parity,
                    $"{hunter}/{type} Off/Shadow preserves canonical damage/ammo/intent/all accepted replay bytes and native grant/timer chronology");
                parityPairs++;
                Console.WriteLine($"NATIVE GRANT TOUR PAIR PASS hunter={hunter} item={type} contact={shadow.ContactFrame} source={shadow.Shot.Fire.SourceFrame} grant={shadow.GrantSequence} certificate={shadow.CertificateKnowledge}");
            }
            Check(arms.Count == 42 && parityPairs == 21 && arms.Select(a => a.Hunter).Distinct().Count() == 7
                && arms.Select(a => a.Item).Distinct().Count() == 3, "complete seven-hunter/three-kind positive matrix");
            status = "PASS";
            Console.WriteLine($"NATIVE GRANT TOUR PASS hunters=7 itemKinds=3 arms={arms.Count} parityPairs={parityPairs} genuineContacts={arms.Count} nativeShots={arms.Count} nativeFrames={arms.Sum(a => (long)a.NativeFrames)} checks={_checks}");
            return 0;
        }
        catch (Exception error)
        {
            failure = error.GetType().Name + ": " + error.Message;
            Console.Error.WriteLine("NATIVE GRANT TOUR FAIL " + error);
            return 1;
        }
        finally
        {
            try
            {
                if (output != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    string assembly = typeof(Scene).Assembly.Location;
                    File.WriteAllText(output, JsonSerializer.Serialize(new { schema = 2, status, failure, room,
                    assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))).ToLowerInvariant(),
                    assemblyMvid = typeof(Scene).Module.ModuleVersionId, checks = _checks, parityPairs,
                    arms = arms.Select(ArmDto).ToArray(), parityDiagnostic,
                    scope = "real native factory/contact/player/input/emission with declared stationary authority body/item placement",
                    authoredUdp = "NOT_RUN", targetImpactDamage = "NOT_RUN", glAudioPixels = "NOT_RUN",
                    specialFamilyAcceptance = "NOT_RUN", naturalMovement = "NOT_RUN" }, Json) + Environment.NewLine);
                }
            }
            finally
            {
                NetSession.Stop(); NetShotProof.Mode = oldProof; NetTimedShotProof.Mode = oldTimed; NetTestScript.Enabled = script;
            }
        }
    }
}
