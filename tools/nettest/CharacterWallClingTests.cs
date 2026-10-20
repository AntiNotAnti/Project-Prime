using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Fresh-process, source-free actual owner-hook qualification. Scene, registry,
// actor, RoomEntity and collision tables are genuinely constructed. Only the
// metadata normally loaded during Initialize is supplied by the fixture.
// Calls the existing hooks and native HandleCollision; never copies mechanics.
// Does not run Initialize/OnLoad/full simulation, access game files, draw, or
// qualify real arena traversal, combat, graphics or imported actor playability.
internal static class CharacterWallClingTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _checks;
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        _checks++;
        Console.WriteLine("WALL CLING OWNER PASS " + label);
    }
    private static void Set(object owner, string name, object value)
    {
        var property = owner.GetType().GetProperty(name, Hidden);
        if (property != null) property.SetValue(owner, value);
        else owner.GetType().GetField(name, Hidden)!.SetValue(owner, value);
    }
    private static object? Hook(PlayerEntity actor, string name, params object[] args)
        => typeof(PlayerEntity).GetMethod(name, Hidden)!.Invoke(actor, args);
    private static void Near(float actual, float expected, string label)
        => Check(float.IsFinite(actual) && MathF.Abs(actual - expected) < .00001f, label + $" ({actual})");

    private sealed class Fixture
    {
        internal Scene Scene { get; }
        internal PlayerEntity Player { get; }
        internal Fixture(Hunter hunter = Hunter.Weavel, bool bind = true, bool replica = false)
        {
            Scene = new Scene(new Vector2i(32, 32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { }, replica ? new ReplicaServices() : null, initializeRuntime: false);
            Player = Scene.Players.Items[0];
            Player.Hunter = hunter;
            Set(Player, "Values", Metadata.PlayerValues[(int)hunter]);
            Player.Health = 100;
            Player.LoadFlags = LoadFlags.Active | LoadFlags.SlotActive | LoadFlags.Spawned;
            Player.IsBot = false;
            Player.Position = Vector3.Zero;
            // The actual Scene constructor installs live facades. Replica setup
            // deliberately leaves those facades pointing at a different owner.
            if (bind) Check(Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "actual registered preinitialize Weavel binds explicit TrooperV1");
            Wall(Vector3.UnitX);
        }
        internal void Wall(Vector3 n, float low = -20, float high = 20, CollisionFlags flags = 0)
        {
            Vector3 side = new(-n.Z, 0, n.X);
            Vector3[] points = { side * -20 + Vector3.UnitY * low, side * 20 + Vector3.UnitY * low,
                side * 20 + Vector3.UnitY * high, side * -20 + Vector3.UnitY * high };
            object header = default(CollisionHeader);
            Set(header, "PartsX", 16); Set(header, "PartsY", 16); Set(header, "PartsZ", 16);
            Set(header, "MinPosition", new Vector3Fx(-32 * 4096, -32 * 4096, -32 * 4096));
            var entries = new CollisionGridEntry[16 * 16 * 16];
            Array.Fill(entries, new CollisionGridEntry(1, 0));
            int axis = MathF.Abs(n.X) >= MathF.Abs(n.Z) ? 0 : 2;
            var source = new PrimeCollisionInfo((CollisionHeader)header, new Vector3Fx[4], new Vector4Fx[1],
                new[] { 0, 1, 2, 3 }, new[] { new CollisionFace(0, 0, flags, (ushort)axis, 0, 4, 0) }, new[] { 0 }, entries, Array.Empty<Portal>());
            for (int i = 0; i < points.Length; i++) ((IList<Vector3>)source.Points)[i] = points[i];
            ((IList<Vector4>)source.Planes)[0] = new Vector4(n, 0);
            var room = new RoomEntity(Scene);
            ((List<CollisionInstance>)room.RoomCollision).Add(new CollisionInstance("synthetic-wall", source, false));
            Set(Scene, "_room", room);
        }
        internal void Standing()
        {
            Set(Player, "Flags1", PlayerFlags1.Standing | PlayerFlags1.Grounded);
            Hook(Player, "WallClingAfterCollision");
            Set(Player, "Flags1", PlayerFlags1.None);
        }
        internal void Step() => Hook(Player, "WallClingAfterCollision");
        internal void Touch(Vector3 n, bool face = true, float into = .1f)
        {
            float radius = Fixed.ToFloat(Player.Values.BipedColRadius);
            Player.Position = n * (radius - .01f) + Vector3.UnitY * Player.Position.Y;
            Player.PrevPosition = Player.Position;
            Player.Speed = -n * into;
            Hook(Player, "WallClingHold");
            if (face)
                Player.HandleCollision(new CollisionResult { Field0 = 0, Plane = new Vector4(n, 0) });
            else
            {
                // Native edge response itself must pass face:false to the hook.
                Player.HandleCollision(new CollisionResult { Field0 = 1, Plane = new Vector4(n, 0),
                    EdgePoint1 = new Vector3(0, -20, 0), EdgePoint2 = new Vector3(0, 20, 0) });
            }
            Step();
        }
        internal void Start(Vector3? normal = null)
        {
            Hook(Player, "WallClingNoteJump");
            Touch(normal ?? Vector3.UnitX);
            Check(Player.WallClinging && Player.WallClingTimer == 150 && Player.Speed == Vector3.Zero,
                "native face collision plus three actual collision probes admits 150-step hold and zero velocity");
        }
        internal void Release() => Hook(Player, "ClingRelease");
    }
    private sealed class ReplicaServices : ISceneServices
    {
        public bool IsReplica => true;
        public bool MayEndOnScore => false;
        public bool ShouldLeaveAfterMatch => false;
        public bool SuppressDamage => true;
        public bool AllowsPresentationSideEffects => false;
        public IPlayerReplicationHost PlayerReplication => LivePlayerReplicationHost.Instance;
        public bool DisablePowerups => false;
        public MatchWorldProfile? NetworkWorldProfile => null;
        public bool ReplicatesHealthSpawns => false;
        public bool TryGetHealthSpawn(short id, out HealthSpawnState state) { state = default; return false; }
    }

    internal static int Run()
    {
        var oldState = GameState.Current; var oldPlayers = PlayerEntity.LegacyRegistry; var oldRandom = Rng.Current;
        var oldHunterSfx = Metadata.HunterSfx; var oldRole = NetSession.Role;
        try
        {
            _checks = 0;
            Check(!NetSession.Active, "fresh offline process, no network owner");
            Headless.Enter();
            // Synthetic no-sound table replaces asset-loaded metadata only.
            var silent = new int[8, 17];
            for (int r = 0; r < 8; r++) for (int c = 0; c < 17; c++) silent[r, c] = -1;
            typeof(Metadata).GetProperty(nameof(Metadata.HunterSfx))!.SetValue(null, silent);
            Binding(); Boundaries(); Admission(); NativeOwners(); Lifetimes();
            Console.WriteLine($"WALL CLING OWNER PASS {_checks} assertions; source-free actual owner hooks and synthetic collision only; no whole-scene/asset/graphics/network qualification.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("WALL CLING OWNER FAIL " + ex); return 1; }
        finally
        {
            GameState.Current = oldState; PlayerEntity.LegacyRegistry = oldPlayers; Rng.Current = oldRandom;
            typeof(Metadata).GetProperty(nameof(Metadata.HunterSfx))!.SetValue(null, oldHunterSfx);
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, oldRole);
        }
    }

    private static void Binding()
    {
        foreach (string blocked in new[] { "balanced", "enhanced", "momentum", "quake3" })
        {
            var candidate = new Fixture(bind: false);
            candidate.Scene.GameState.BalancedMode = blocked == "balanced";
            candidate.Scene.GameState.EnhancedHunters = blocked == "enhanced";
            candidate.Scene.GameState.MovementModel = blocked == "momentum" ? MovementModel.Momentum
                : blocked == "quake3" ? MovementModel.Quake3 : MovementModel.Native;
            Check(!candidate.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), blocked + " explicitly refuses initial variant bind");
            Check(!candidate.Scene.PrivateCharacterGameplayUnsupported, blocked + " refusal does not set private gameplay replay latch");
            candidate.Scene.GameState.BalancedMode = false; candidate.Scene.GameState.EnhancedHunters = false;
            candidate.Scene.GameState.MovementModel = MovementModel.Native;
            Check(candidate.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), blocked + " rejected proposal does not consume available bind");
            Check(candidate.Scene.PrivateCharacterGameplayUnsupported, "accepted private gameplay sets Scene replay exclusion latch");
            candidate.Player.ModRetireCharacterGameplay();
            Check(candidate.Scene.PrivateCharacterGameplayUnsupported, "replay exclusion stays latched after rule retirement");
        }
        var f = new Fixture(bind: false);
        Check(!f.Player.ModTryBindCharacterGameplay(new CharacterGameplayRules(2, CharacterGameplayVariant.TrooperV1)), "unknown rule version refuses binding");
        Check(!f.Player.ModTryBindCharacterGameplay(new CharacterGameplayRules(1, (CharacterGameplayVariant)255)), "unknown rule variant refuses binding");
        Check(f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "invalid proposals do not consume one-shot bind");
        Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "same-rule second bind refuses");
        Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.Native), "native replacement second bind refuses");
        f = new Fixture(bind: false);
        Set(f.Player, "_modCharacterGameplayInitializationStarted", true);
        Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "initialization-start fence refuses binding without loading game assets");
        f = new Fixture(bind: false);
        bool wrongThread = Task.Run(() => f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1)).GetAwaiter().GetResult();
        Check(!wrongThread, "non-foreground thread refuses binding");
        Check(f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "wrong-thread refusal preserves available bind");
        f = new Fixture(bind: false);
        var detached = new PlayerEntity(0, f.Scene) { Hunter = Hunter.Weavel };
        Check(!detached.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "genuinely constructed detached actor cannot borrow occupied slot");
        f = new Fixture(bind: false, replica: true);
        Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "replica Scene refuses binding");
        foreach (string owner in new[] { "SideScene", "IsReplayLab", "_exiting" })
        {
            f = new Fixture(bind: false); Set(f.Scene, owner, true);
            Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), owner + " refuses preinitialize bind");
        }
        f = new Fixture(bind: false);
        var registry = PlayerEntity.LegacyRegistry; PlayerEntity.LegacyRegistry = new ScenePlayerRegistry();
        Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "wrong legacy registry independently refuses bind");
        PlayerEntity.LegacyRegistry = registry;
        Check(f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "restoring correct registry permits first bind");
        f = new Fixture(bind: false);
        var roleProperty = typeof(NetSession).GetProperty(nameof(NetSession.Role))!;
        roleProperty.SetValue(null, NetRole.Server);
        try { Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "synthetic active-network role refuses binding without starting a session"); }
        finally { roleProperty.SetValue(null, NetRole.Offline); }
        Check(f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "network-gate refusal preserves available offline bind");
        f = new Fixture(bind: false); _ = new Fixture(bind: false);
        Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), "retired foreground Scene cannot bind old registered actor");
    }

    private static void Boundaries()
    {
        var f = new Fixture(); f.Start();
        f.Player.Speed = new Vector3(4, 5, 6); Hook(f.Player, "WallClingHold");
        Check(f.Player.Speed == Vector3.Zero, "actual hold cancels external velocity");
        for (int i = 1; i <= 149; i++) f.Step();
        Check(f.Player.WallClinging && f.Player.WallClingTimer == 1 && f.Player.WallClingTimeouts == 0, "hold remains through step149");
        f.Step();
        Check(!f.Player.WallClinging && f.Player.WallClingTimeouts == 1 && f.Player.WallClingLongest == 150, "hold expires exactly step150");
        f = new Fixture(); f.Start(); f.Player.Controls.Jump.IsPressed = true;
        Check((bool)Hook(f.Player, "WallClingInput")!, "actual jump input owner accepts wall jump");
        Check(!f.Player.WallClinging && f.Player.WallJumps == 1 && f.Player.Flags1.TestFlag(PlayerFlags1.UsedJump), "wall jump releases and owns native UsedJump flag");
        Near(f.Player.Speed.X, .3f, "wall jump horizontal impulse is .3");
        Near(f.Player.Speed.Y, Fixed.ToFloat(f.Player.Values.JumpSpeed), "wall jump retains native Hunter vertical impulse");
        f.Player.Controls.Jump.IsPressed = false;
        for (int i = 0; i < 14; i++)
        {
            var steer = (Vector3)Hook(f.Player, "WallClingSteer", new Vector3(-.2f, .4f, .25f))!;
            Check(steer == new Vector3(0, .4f, .25f), "kick steering suppresses only inward component at step" + i);
            f.Step();
        }
        Check((Vector3)Hook(f.Player, "WallClingSteer", new Vector3(-.2f, .4f, .25f))! == new Vector3(-.2f, .4f, .25f), "kick steering restores at step14");
        f = new Fixture(); f.Start(); f.Release(); f.Wall(-Vector3.UnitX);
        for (int i = 1; i <= 19; i++) f.Touch(-Vector3.UnitX);
        Check(!f.Player.WallClinging && f.Player.WallClingChainCount == 1, "release wait refuses other wall through step19");
        f.Touch(-Vector3.UnitX);
        Check(f.Player.WallClinging && f.Player.WallClingChainCount == 2, "release wait admits other wall exactly step20");
        f.Release(); f.Wall(Vector3.UnitZ);
        for (int i = 0; i < 20; i++) f.Touch(Vector3.UnitZ);
        Check(f.Player.WallClinging && f.Player.WallClingChainCount == 3, "third distinct wall is admitted");
        f.Release(); f.Wall(Vector3.UnitX);
        for (int i = 0; i < 21; i++) f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging && f.Player.WallClingChainCount == 3, "fourth airborne cling refuses after full release wait");
        f.Standing(); Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
        Check(f.Player.WallClinging && f.Player.WallClingChainCount == 1, "standing resets chain and previous-wall fence");
        f = new Fixture(); f.Start(); f.Release();
        for (int i = 0; i < 25; i++) f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging && f.Player.WallClingChainCount == 1, "same-wall contact refuses after release wait");
    }

    private static void Admission()
    {
        var f = new Fixture(); f.Standing(); f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging, "walking into wall without jump or fall refuses");
        f.Player.Position = f.Player.Position.WithY(3); f.Step();
        f.Player.Position = f.Player.Position.WithY(1.001f); f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging, "1.999-unit fall refuses");
        f.Player.Position = f.Player.Position.WithY(1); f.Touch(Vector3.UnitX);
        Check(f.Player.WallClinging && !f.Player.WallClingStartRose && f.Player.WallClingStartDrop == 2, "exact2-unit fall admits without jump note");
        foreach (float speed in new[] { -.1f, 0, .019f, .02f })
        {
            f = new Fixture(); Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX, into: speed);
            Check(!f.Player.WallClinging, "non-inward or threshold speed refuses " + speed);
        }
        f = new Fixture(); Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX, into: .0201f);
        Check(f.Player.WallClinging, "strictly greater than .02 inward speed admits");
        f = new Fixture(); f.Start(); f.Standing();
        Check(!f.Player.WallClinging && f.Player.WallClingChainCount == 0 && !f.Player.WallClingRose,
            "standing releases active hold and clears jump/chain history");
        f = new Fixture(); Set(f.Player, "Flags1", PlayerFlags1.Grounded);
        Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging, "grounded owner refuses contact despite jump note");
        f = new Fixture(); Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX, face: false);
        Check(!f.Player.WallClinging, "actual native edge contact refuses cling");
        foreach (Vector3 n in new[] { new Vector3(1, -.0176f, 0), new Vector3(1, .0873f, 0), new Vector3(float.NaN, 0, 0), new Vector3(1, float.PositiveInfinity, 0), Vector3.Zero })
        {
            f = new Fixture(); Hook(f.Player, "WallClingNoteJump"); f.Player.Speed = -Vector3.UnitX * .1f;
            Hook(f.Player, "WallClingTouch", n, true); f.Step();
            Check(!f.Player.WallClinging, "actual touch refuses disallowed slope/degenerate/nonfinite normal " + n);
        }
        foreach (float y in new[] { -.0175f, .0872f })
        {
            f = new Fixture(); var n = new Vector3(MathF.Sqrt(1 - y * y), y, 0); f.Wall(n);
            Hook(f.Player, "WallClingNoteJump"); f.Touch(n);
            Check(f.Player.WallClinging, "inclusive authored slope boundary admits " + y);
        }
        foreach (float dot in new[] { .699f, .701f })
        {
            f = new Fixture(); f.Start(); f.Release();
            var n = new Vector3(dot, 0, MathF.Sqrt(1 - dot * dot)); f.Wall(n);
            for (int i = 0; i < 20; i++) f.Touch(n);
            Check(f.Player.WallClinging == (dot < .7f), "previous-wall normal threshold comparison " + dot);
        }
        f = new Fixture(); f.Wall(new Vector3(.94f, 0, MathF.Sqrt(1 - .94f * .94f)));
        Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging && f.Player.WallClingTooShort == 1, "actual probes refuse face orientation mismatch");
        f = new Fixture(); f.Wall(Vector3.UnitX, -20, f.Player.ModBipedTop - .13f);
        Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging && f.Player.WallClingTooShort == 1, "actual head probe rejects low wall");
        f = new Fixture(); f.Wall(Vector3.UnitX, f.Player.ModBipedBottom + .13f, 20);
        Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging && f.Player.WallClingTooShort == 1, "actual foot probe rejects raised wall");
        f = new Fixture(); f.Wall(-Vector3.UnitX);
        Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging && f.Player.WallClingTooShort == 1, "actual probe refuses back-facing wall even with a face contact");
        f = new Fixture(); f.Wall(Vector3.UnitX, flags: CollisionFlags.IgnorePlayers);
        Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
        Check(!f.Player.WallClinging && f.Player.WallClingTooShort == 1, "actual probes honor IgnorePlayers collision mask");
    }

    private static void NativeOwners()
    {
        foreach (Hunter hunter in new[] { Hunter.Samus, Hunter.Kanden, Hunter.Trace, Hunter.Sylux, Hunter.Noxus, Hunter.Spire, Hunter.Weavel })
        {
            var f = new Fixture(hunter, bind: false);
            Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
            Check(!f.Player.WallClinging && f.Player.WallClings == 0 && f.Player.ModCharacterGameplay == CharacterGameplayRules.Native,
                hunter + " defaults native without any binding");
            if (hunter != Hunter.Weavel) Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), hunter + " cannot bind Trooper mechanics");
            Check(f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.Native), hunter + " admits explicit native rule");
            Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
            Check(!f.Player.WallClinging && f.Player.WallClings == 0, hunter + " stays native on identical jump/contact hooks");
        }
        foreach (string blocker in new[] { "freeze", "pad", "pad-lock", "biped-lock", "biped-stuck", "alt", "morph", "unmorph", "dead", "bot", "spectator", "inactive" })
        {
            var f = new Fixture(); f.Start();
            switch (blocker)
            {
                case "freeze": Set(f.Player, "_frozenTimer", (ushort)2); break;
                case "pad": Set(f.Player, "Flags1", PlayerFlags1.UsedJumpPad); break;
                case "pad-lock": Set(f.Player, "_jumpPadControlLock", (ushort)2); break;
                case "biped-lock": Set(f.Player, "Flags2", PlayerFlags2.BipedLock); break;
                case "biped-stuck": Set(f.Player, "Flags2", PlayerFlags2.BipedStuck); break;
                case "alt": Set(f.Player, "Flags1", PlayerFlags1.AltForm); break;
                case "morph": Set(f.Player, "Flags1", PlayerFlags1.Morphing); break;
                case "unmorph": Set(f.Player, "Flags1", PlayerFlags1.Unmorphing); break;
                case "dead": f.Player.Health = 0; break;
                case "bot": f.Player.IsBot = true; break;
                case "spectator": Set(f.Player, "Flags2", PlayerFlags2.Spectating); break;
                case "inactive": f.Player.LoadFlags = LoadFlags.None; break;
            }
            Vector3 ownedVelocity = f.Player.Speed = new Vector3(.7f, .8f, .9f);
            Hook(f.Player, "WallClingHold");
            Check(!f.Player.WallClinging && f.Player.Speed == ownedVelocity, blocker + " owner releases cling and preserves its velocity");
            f.Player.Controls.Jump.IsPressed = true;
            Check(!(bool)Hook(f.Player, "WallClingInput")! && f.Player.Speed == ownedVelocity, blocker + " cannot manufacture wall-jump impulse");
        }
    }

    private static void Lifetimes()
    {
        var life = new Fixture(); life.Start();
        Hook(life.Player, "ModResetWallClingForLife");
        Check(!life.Player.WallClinging && life.Player.WallClingChainCount == 0 && !life.Player.WallClingRose
            && life.Player.ModCharacterGameplay == CharacterGameplayRules.TrooperV1,
            "actual life reset clears cling state while retaining captured valid gameplay rule");
        life.Start();
        life.Player.Hunter = Hunter.Samus; Hook(life.Player, "ModResetWallClingForLife");
        Check(!life.Player.WallClinging && life.Player.ModCharacterGameplay == CharacterGameplayRules.Native,
            "actual life reset retires rule after native Hunter owner changes");
        foreach (string fence in new[] { "balanced", "enhanced", "momentum", "quake3", "hunter", "slot", "scene-lifetime", "foreground", "registry", "network", "replica", "side", "replay", "exit" })
        {
            var f = new Fixture(); f.Start();
            switch (fence)
            {
                case "balanced": f.Scene.GameState.BalancedMode = true; break;
                case "enhanced": f.Scene.GameState.EnhancedHunters = true; break;
                case "momentum": f.Scene.GameState.MovementModel = MovementModel.Momentum; break;
                case "quake3": f.Scene.GameState.MovementModel = MovementModel.Quake3; break;
                case "hunter": f.Player.Hunter = Hunter.Samus; break;
                case "slot": f.Scene.Players.Values[0] = new PlayerEntity(0, f.Scene) { Hunter = Hunter.Weavel }; break;
                case "scene-lifetime": Set(f.Player, "_modCharacterGameplayScene", f.Scene.LifetimeGeneration + 1); break;
                case "foreground": _ = new Fixture(bind: false); break;
                case "registry": PlayerEntity.LegacyRegistry = new ScenePlayerRegistry(); break;
                case "network": typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Server); break;
                case "replica": Set(f.Scene, "Services", new ReplicaServices()); break;
                case "side": f.Scene.SideScene = true; break;
                case "replay": Set(f.Scene, "IsReplayLab", true); break;
                case "exit": Set(f.Scene, "_exiting", true); break;
            }
            Check(!f.Player.WallClinging && f.Player.ModCharacterGameplay == CharacterGameplayRules.Native && f.Player.WallClingChainCount == 0,
                fence + " invalidates captured gameplay capability and clears active mechanics");
            // Restore every original owner after retirement. Reacquiring the
            // same facade/slot may never revive a stale gameplay capability.
            typeof(NetSession).GetProperty(nameof(NetSession.Role))!.SetValue(null, NetRole.Offline);
            f.Scene.GameState.BalancedMode = false; f.Scene.GameState.EnhancedHunters = false; f.Scene.GameState.MovementModel = MovementModel.Native; f.Player.Hunter = Hunter.Weavel;
            GameState.Current = f.Scene.GameState; PlayerEntity.LegacyRegistry = f.Scene.Players; Rng.Current = f.Scene.Random;
            f.Scene.Players.Values[0] = f.Player; f.Scene.SideScene = false;
            Set(f.Scene, "Services", LiveSceneServices.Instance); Set(f.Scene, "IsReplayLab", false); Set(f.Scene, "_exiting", false);
            Set(f.Player, "_modCharacterGameplayScene", f.Scene.LifetimeGeneration);
            Hook(f.Player, "WallClingNoteJump"); f.Touch(Vector3.UnitX);
            Check(!f.Player.WallClinging && f.Player.ModCharacterGameplay == CharacterGameplayRules.Native,
                fence + " capability remains retired after all original owners are restored");
            Check(!f.Player.ModTryBindCharacterGameplay(CharacterGameplayRules.TrooperV1), fence + " retirement cannot rebind same actor");
        }
    }
}
