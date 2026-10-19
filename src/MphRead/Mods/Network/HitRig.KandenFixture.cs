using System;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class HitRig
{
    private static readonly bool KandenFixtureEnabled = Environment.GetEnvironmentVariable("PRIME_NET2_KANDEN_SETUP") == "1";
    private static ulong _kandenFixtureEpoch;
    private static ushort _kandenFixtureMatch;
    private static Scene? _kandenFixtureScene;
    private static Scene? _kandenGeometryScene;
    private static KandenFixtureGeometry? _kandenGeometry;
    private static bool _kandenFixtureAuthorityReady, _kandenFixtureClientReady, _kandenFixtureSettled, _kandenFixtureFailed;
    private static long _kandenFixtureWaitAt, _kandenFixturePreparedAt;
    private static bool _kandenFixtureClientAdopted;
    private static readonly ShotKey[] KandenFixtureIdentities = new ShotKey[2];
    private static readonly Vector3[] KandenFixturePositions = new Vector3[2];
    private static readonly bool[] KandenFixtureConfirmed = new bool[2];
    internal readonly record struct KandenFixtureGeometry(Vector3 Center, float Floor, int Candidates,
        int SupportedPatches, int FloorQueries, int ClearanceQueries);

    private static void KandenFixtureStream()
    {
        if (_kandenFixtureEpoch == NetSession.AuthorityEpoch && _kandenFixtureMatch == NetSession.CurrentMatchId) return;
        _kandenFixtureEpoch = NetSession.AuthorityEpoch; _kandenFixtureMatch = NetSession.CurrentMatchId;
        _kandenFixtureScene = _kandenGeometryScene = null; _kandenGeometry = null;
        _kandenFixtureAuthorityReady = _kandenFixtureClientReady = _kandenFixtureSettled = _kandenFixtureClientAdopted = _kandenFixtureFailed = false;
        _kandenFixtureWaitAt = Environment.TickCount64;
        Array.Clear(KandenFixtureIdentities); Array.Clear(KandenFixturePositions); Array.Clear(KandenFixtureConfirmed);
    }
    private static void FailKandenFixture(string reason)
    {
        if (!_kandenFixtureFailed) NetLog.Event("Kanden initial fixture FAILED: " + reason);
        _kandenFixtureFailed = true;
        _kandenFixtureScene = _kandenGeometryScene = null; _kandenGeometry = null;
    }
    private static bool FiniteKandenFixtureVector(Vector3 v) => Single.IsFinite(v.X) && Single.IsFinite(v.Y) && Single.IsFinite(v.Z);
    private static bool CurrentKandenFixturePlayer(PlayerEntity player, int slot)
        => slot is >= 0 and <= 1 && player.SlotIndex == slot && PlayerEntity.Players.Count > slot
        && ReferenceEquals(PlayerEntity.Players[slot], player) && !player.SceneServices.IsReplica
        && !player.IsBot && player.Hunter == Hunter.Kanden && player.Health > 0
        && player.LoadFlags.TestFlag(LoadFlags.Active) && player.LoadFlags.TestFlag(LoadFlags.Spawned)
        && !player.Flags2.TestFlag(PlayerFlags2.Spectating) && !player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing
        && NetSession.AuthorityEpoch != 0 && NetSession.CurrentMatchId != 0
        && NetPlayerLifecycle.Generation(slot) != 0 && NetPlayerLifecycle.Get(slot) != 0;
    private static bool CurrentKandenFixtureIdentities()
    {
        if (_kandenFixtureScene == null || PlayerEntity.Players.Count < 2) return false;
        for (int slot = 0; slot < 2; slot++)
            if (!ReferenceEquals(PlayerEntity.Players[slot].OwningScene, _kandenFixtureScene)
                || KandenFixtureIdentities[slot] != ShotKey.For(slot, 0)) return false;
        return true;
    }
    private static bool KandenFixtureIntentMatches(int slot)
    {
        if (!NetSession.RemoteIntentValid[slot]) return false;
        var intent = NetSession.RemoteIntents[slot]; var key = KandenFixtureIdentities[slot];
        return intent.MatchId == key.MatchId && intent.AuthorityEpoch == key.AuthorityEpoch
            && intent.SlotGeneration == key.Generation && intent.LifeId == key.LifeId;
    }

    // Geometry census only: actual collision queries, no player/world mutation.
    internal static bool TryFindKandenEncounterGeometry(Scene scene, out KandenFixtureGeometry geometry)
    {
        geometry = default;
        if (scene.Services.IsReplica || Metadata.GetRoomById(scene.RoomId, noThrow: true)?.Name != "MP3 PROVING GROUND") return false;
        int floors = 0, clears = 0, candidates = 0, supported = 0;
        float lowest = Single.MaxValue; Vector3 selected = default;
        bool Floor(Vector3 p, out float height)
        {
            floors++; CollisionResult hit = default;
            bool found = CollisionDetection.CheckBetweenPoints(p.WithY(32), p.WithY(-8), TestFlags.Players, scene, ref hit);
            height = hit.Position.Y;
            return found && hit.Plane.Y > .9f && Single.IsFinite(height);
        }
        bool Clear(Vector3 from, Vector3 to)
        {
            clears++; CollisionResult hit = default;
            return !CollisionDetection.CheckBetweenPoints(from, to, TestFlags.Players, scene, ref hit);
        }
        for (int z = -12; z <= 12; z += 4)
            for (int x = -12; x <= 12; x += 4)
            {
                candidates++; Vector3 candidate = new(x, 0, z);
                if (!Floor(candidate, out float floor)) continue;
                bool valid = Clear(candidate.WithY(floor + .1f), candidate.WithY(floor + 2.5f));
                for (int ring = 0; ring < 2 && valid; ring++)
                    for (int sample = 0; sample < 12 && valid; sample++)
                    {
                        float angle = sample * MathF.PI / 6, radius = ring == 0 ? 2 : 3.8f;
                        Vector3 p = candidate + new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
                        valid = Floor(p, out float other) && MathF.Abs(other - floor) < .1f
                            && Clear(p.WithY(floor + .1f), p.WithY(floor + 2.5f))
                            && Clear(candidate.WithY(floor + .3f), p.WithY(floor + .3f));
                    }
                for (int slot = 0; slot < 2 && valid; slot++)
                {
                    Vector3 p = candidate + Vector3.UnitX * (slot == 0 ? -.875f : .875f);
                    valid = Floor(p, out float other) && MathF.Abs(other - floor) < .1f
                        && Clear(p.WithY(floor + .1f), p.WithY(floor + 2.5f));
                }
                if (!valid) continue;
                supported++;
                if (floor < lowest) { lowest = floor; selected = candidate.WithY(floor + .5f); }
            }
        if (supported == 0) return false;
        geometry = new(selected, lowest, candidates, supported, floors, clears);
        return true;
    }

    internal static bool TrySetupKandenEncounterFixture(PlayerEntity owner, PlayerEntity victim,
        out KandenFixtureGeometry geometry)
    {
        geometry = default; KandenFixtureStream();
        if (!KandenFixtureEnabled || TriangleFixtureEnabled || _kandenFixtureFailed || _kandenFixtureAuthorityReady
            || !NetSession.IsAuthority || NetSession.SemanticLegacyPlayback || !NetRoomChange.GameplayReady
            || !CurrentKandenFixturePlayer(owner, 0) || !CurrentKandenFixturePlayer(victim, 1)
            || !ReferenceEquals(owner.OwningScene, victim.OwningScene)
            || owner.OwningScene.Bombs.LiveCount != 0 || NetBombs.PendingCount != 0) return false;
        for (int slot = 0; slot < 2; slot++) KandenFixtureIdentities[slot] = ShotKey.For(slot, 0);
        if (!KandenFixtureIntentMatches(0) || !KandenFixtureIntentMatches(1)
            || !TryFindKandenEncounterGeometry(owner.OwningScene, out geometry)) return false;
        _kandenFixtureScene = owner.OwningScene;
        // Only initial pose/velocity/controls. Identity, form, inventory, health,
        // protection, cooldowns and all simulation clocks retain native values.
        for (int slot = 0; slot < 2; slot++)
        {
            var player = slot == 0 ? owner : victim;
            Vector3 position = geometry.Center + Vector3.UnitX * (slot == 0 ? -.875f : .875f);
            player.ModPlaceAt(position); player.ModSetSpawnFacing(slot == 0 ? Vector3.UnitX : -Vector3.UnitX);
            player.Speed = Vector3.Zero; player.Controls.ClearAll(); KandenFixturePositions[slot] = position;
        }
        _kandenFixtureAuthorityReady = true; _kandenFixtureWaitAt = Environment.TickCount64;
        NetLog.Event($"Kanden initial fixture authority ready epoch={_kandenFixtureEpoch} match={_kandenFixtureMatch} keys={KandenFixtureIdentities[0]}/{KandenFixtureIdentities[1]} geometry={geometry}");
        return true;
    }
    private static void SetupNetworkKandenFixtureOnce()
    {
        if (!KandenFixtureEnabled || !NetSession.IsAuthority) return;
        KandenFixtureStream();
        if (_kandenFixtureFailed || _kandenFixtureAuthorityReady) return;
        if (TriangleFixtureEnabled) { FailKandenFixture("conflicting explicit fixtures"); return; }
        if (PlayerEntity.Players.Count < 2 || !CurrentKandenFixturePlayer(PlayerEntity.Players[0], 0)
            || !CurrentKandenFixturePlayer(PlayerEntity.Players[1], 1)) return;
        if (!NetSession.RemoteIntentValid[0] || !NetSession.RemoteIntentValid[1]) return;
        if (!TrySetupKandenEncounterFixture(PlayerEntity.Players[0], PlayerEntity.Players[1], out _))
            FailKandenFixture("initial native actors, intent identity, bomb-empty or collision guard refused");
    }
    private static bool HoldKandenFixtureRemoteInput(PlayerEntity player)
    {
        if (!KandenFixtureEnabled || !NetSession.IsAuthority || player.SlotIndex is < 0 or > 1) return false;
        KandenFixtureStream();
        // The initial handshake retires permanently for this stream. Later native
        // death/respawn or a replacement Scene must never hold controls or re-place.
        if (_kandenFixtureSettled) return false;
        if (!_kandenFixtureFailed && Environment.TickCount64 - _kandenFixtureWaitAt >= 10000)
            FailKandenFixture("initial pose confirmation timed out");
        if (_kandenFixtureAuthorityReady && !CurrentKandenFixtureIdentities()) FailKandenFixture("actor identity or scene replaced during setup");
        if (!_kandenFixtureFailed && _kandenFixtureAuthorityReady)
        {
            int slot = player.SlotIndex; var intent = NetSession.RemoteIntents[slot];
            if (KandenFixtureIntentMatches(slot) && (intent.Position - KandenFixturePositions[slot]).LengthSquared < .25f
                && (intent.Buttons & IntentButtons.AltFormState) == 0)
                KandenFixtureConfirmed[slot] = true;
            if (KandenFixtureConfirmed[0] && KandenFixtureConfirmed[1])
            {
                _kandenFixtureSettled = true; _kandenFixtureScene = null;
                NetLog.Event("Kanden initial fixture both exact native poses confirmed; initial setup retired, ordinary controls released");
                return false;
            }
        }
        player.Controls.ClearAll(); return true;
    }
    private static bool KandenFixtureClientObserved(PlayerEntity player)
    {
        if (!KandenFixtureEnabled || Mode != RigMode.BombKanden || player.SlotIndex is < 0 or > 1) return true;
        KandenFixtureStream();
        // Own initial adoption is complete. Subsequent native lives/Scenes use
        // ordinary controls/snapshots; this fixture never adopts a second pose.
        if (_kandenFixtureClientReady) return true;
        if (_kandenFixtureFailed) return false;
        AimDeltaX = AimDeltaY = 0;
        if (_kandenFixtureClientAdopted)
        {
            if (!CurrentKandenFixtureIdentities())
            { FailKandenFixture("client actor identity or Scene replaced during neutral preparation"); return false; }
            // Disclosed fixed preparation, not an authority ACK. Ordinary
            // neutral native inputs keep repeating the adopted pose naturally.
            if (Environment.TickCount64 - _kandenFixturePreparedAt < 2000) return false;
            _kandenFixtureClientReady = true;
            _kandenFixtureScene = _kandenGeometryScene = null; _kandenGeometry = null;
            NetLog.Event("Kanden initial fixture fixed2.0s neutral preparation retired; ordinary controls released (not server confirmation)");
            return true;
        }
        if (!NetSession.WorldAdmissionConfirmed) { _kandenFixtureWaitAt = Environment.TickCount64; return false; }
        if (Environment.TickCount64 - _kandenFixtureWaitAt > 10000) { FailKandenFixture("initial authority snapshot timed out"); return false; }
        if (TriangleFixtureEnabled || !CurrentKandenFixturePlayer(player, player.SlotIndex)
            || !NetSession.RemoteStateValid[0] || !NetSession.RemoteStateValid[1]) return false;
        var scene = player.OwningScene;
        if (!ReferenceEquals(_kandenGeometryScene, scene))
        {
            if (!TryFindKandenEncounterGeometry(scene, out var found)) { FailKandenFixture("client collision census refused"); return false; }
            _kandenGeometryScene = scene; _kandenGeometry = found;
        }
        var geometry = _kandenGeometry!.Value;
        for (int slot = 0; slot < 2; slot++)
        {
            var state = NetSession.RemoteStates[slot]; Vector3 expected = geometry.Center + Vector3.UnitX * (slot == 0 ? -.875f : .875f);
            if (state.SlotIndex != slot || !NetPlayerLifecycle.Matches(slot, state.SlotGeneration, state.LifeId)
                || state.Health == 0 || (state.Flags & PlayerState.FlagSpawned) == 0 || (state.Flags & PlayerState.FlagAltForm) != 0
                || !state.Form.IsValid || state.Form.Phase != FormPhase.Biped
                || !FiniteKandenFixtureVector(state.Position) || !FiniteKandenFixtureVector(state.Speed) || !FiniteKandenFixtureVector(state.Facing)
                || (state.Position - expected).LengthSquared > .25f
                || MathF.Abs(state.Facing.Y) > .01f || MathF.Abs(state.Facing.Z) > .01f
                || (slot == 0 ? state.Facing.X < .99f : state.Facing.X > -.99f)) return false;
        }
        var own = NetSession.RemoteStates[player.SlotIndex];
        // Adopt this explicit initial authority snapshot once; never force form.
        player.ModPlaceAt(own.Position); player.Speed = own.Speed; player.ModSetSpawnFacing(own.Facing);
        _kandenFixtureScene = scene;
        for (int slot = 0; slot < 2; slot++) KandenFixtureIdentities[slot] = ShotKey.For(slot, 0);
        _kandenFixtureClientAdopted = true; _kandenFixturePreparedAt = Environment.TickCount64;
        NetLog.Event($"Kanden initial fixture client adopted; fixed2.0s neutral preparation begins epoch={_kandenFixtureEpoch} match={_kandenFixtureMatch} slot={player.SlotIndex} keys={KandenFixtureIdentities[0]}/{KandenFixtureIdentities[1]} geometry={geometry}");
        return false;
    }
}
