using System.Reflection;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Fresh-process asset-backed offline acceptance. Synthetic keyboard samples enter
// the actual fixed step and PlayerEntity.PickUpItems; this test never calls Grant,
// funding, ProcessPickupContact, or changes FrameCount. Explicit activation and
// placement/orientation at an authored native spawn are declared fixture seams.
// No GL, physical device, Android, network pickup or full cooldown-expiry claim.
internal static class StockPickupSceneChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Action<KeyboardState, Keys, bool> Key = typeof(KeyboardState)
        .GetMethod("SetKeyState", Hidden | BindingFlags.Public)!
        .CreateDelegate<Action<KeyboardState, Keys, bool>>();
    private readonly record struct Approach(string Room, int NativeSpawnId, Vector3 Direction, Vector3i Centre4096);
    private readonly record struct NativePlacement(bool PlayerSpawn, int Id, int Type, Vector3 Position);

    // Independent fixture coordinates/approaches established by extracted-room
    // collision and actual native walking before production StockRiftlinePickups
    // existed. Kept here explicitly: do not derive expected centres from runtime.
    // See artifacts/custom-weapons/CW-05m-stock-placement/allmaps-v8-fixed-final.
    private static readonly Approach[] Approaches =
    [
        new("AD1 TRANSFER LOCK BT",0,new(-0.16261694f,0f,0.98668927f),new(163486,68039,-121219)),
        new("AD1 TRANSFER LOCK DM",0,new(-0.99859554f,0f,-0.05298172f),new(14840,23389,58500)),
        new("AD2 ALINOS PERCH",0,new(0f,0f,-1f),new(-89714,2662,63181)),
        new("AD2 MAGMA VENTS",0,new(0f,0f,-1f),new(-89714,2662,63181)),
        new("CTF1 FAULT LINE - EXPANDED",2,new(0.7547054f,0f,0.6560639f),new(-235371,62054,-41226)),
        new("CTF1_FAULT LINE",2,new(0.7547054f,0f,0.6560639f),new(-235371,62054,-41226)),
        new("E3 FIRST HUNT",12,new(0.9487566f,0f,0.31600797f),new(-73649,12902,-82863)),
        new("Gorea Prison",1,new(0.7071068f,0f,0.7071068f),new(-45061,38679,-45842)),
        new("MP1 SANCTORUS",0,new(-1f,0f,-4.371139e-08f),new(6449,18398,-72283)),
        new("MP10 OVERLOAD",11,new(0f,0f,-1f),new(-24391,2662,579)),
        new("MP11 BREAKTHROUGH",15,new(-0.36651444f,0f,-0.93041235f),new(-35166,23770,21634)),
        new("MP12 SIC TRANSIT",0,new(1f,0f,0f),new(-51102,2869,-74017)),
        new("MP13 ACCELERATOR",9,new(-0.7603587f,0f,-0.6495035f),new(11066,-50599,-74397)),
        new("MP14 OUTER REACH",0,new(0.6729888f,0f,0.73965275f),new(-43100,21753,-42917)),
        new("MP2 HARVESTER",7,new(0.7071068f,0f,0.7071068f),new(-92478,78179,-206504)),
        new("MP3 PROVING GROUND",4,new(-0.3533436f,0f,0.9354937f),new(5044,2662,-82087)),
        new("MP4 HIGHGROUND",18,new(0.99287456f,0f,-0.11916449f),new(-49118,21215,57350)),
        new("MP4 HIGHGROUND - EXPANDED",5,new(0.2284947f,0f,0.9735451f),new(54578,50155,-55448)),
        new("MP5 FUEL SLUICE",0,new(-1f,0f,0f),new(35019,2662,18431)),
        new("MP6 HEADSHOT",29,new(-0.7071068f,0f,-0.7071068f),new(153618,100175,154010)),
        new("MP7 PROCESSOR CORE",1,new(0.70710677f,0f,-0.70710677f),new(24333,10667,44694)),
        new("MP8 FIRE CONTROL",3,new(-0.728981f,0f,-0.68453395f),new(31673,19113,38992)),
        new("MP9 CRYOCHASM",16,new(0.6959094f,0f,-0.71812963f),new(-34537,84382,24170)),
        new("UNIT 3 VESPER STARPORT",1,new(0f,0f,-1f),new(-14,31323,-147739)),
        new("UNIT 4 ARCTERRA BASE",1,new(-0.7071068f,0f,0.7071068f),new(61410,79859,-51152)),
        new("UNIT1 ALINOS LANDFALL",2,new(1f,0f,-4.371139e-08f),new(109473,8116,-120339)),
        new("UNIT2 LANDING BAY",0,new(-0.7740334f,0f,-0.63314474f),new(61451,2662,62762)),
    ];

    internal static int Run(string data, string room)
    {
        Scene? scene = null;
        string directory = Directory.GetCurrentDirectory();
        bool force = MapAudit.ForceEveryone, script = NetTestScript.Enabled;
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            checks++;
            Console.WriteLine("STOCK PICKUP PASS " + message);
        }
        try
        {
            int index = Array.FindIndex(Approaches, item => StringComparer.OrdinalIgnoreCase.Equals(item.Room, room));
            Check(index >= 0, "requested room has an independent authored approach fixture");
            var fixture = Approaches[index];
            Directory.SetCurrentDirectory(Path.GetFullPath(data));
            Paths.UpdatePaths(); Paths.ChooseMphPath(); Headless.Enter();
            MapAudit.ForceEveryone = true; NetTestScript.Enabled = false;
            Check(!NetSession.Active, "fixture starts offline without an authenticated network role");
            var keyboard = SyntheticInput.CreateKeyboard();
            var mouse = SyntheticInput.CreateMouse();
            scene = new(new Vector2i(256,192), keyboard, mouse, _ => { }, () => { });
            scene.AddPlayer(Hunter.Samus);
            scene.AddRoom(fixture.Room, GameMode.Battle, playerCount: 1, nodeLayerMask: 0);
            scene.OnLoad();
            // Native headless uses Roam. All admits the same local input samples
            // used by existing native movement fixtures, without creating GL.
            var mode = typeof(Scene).GetField("_inputMode", Hidden)!;
            mode.SetValue(scene, Enum.Parse(mode.FieldType, "All"));
            scene.GameState.MatchTime = -1; scene.GameState.PointGoal = 10000;
            var player = scene.Players.Main; player.IsBot = false;
            foreach (var button in player.Controls.All) { button.Type = ButtonType.Key; button.Key = Keys.Unknown; }
            player.Controls.Shoot.Key = Keys.Enter;
            player.Controls.MoveUp.Key = Keys.W; player.Controls.MoveDown.Key = Keys.S;
            player.Controls.CustomWeapon.Key = Keys.C;
            player.Controls.MouseAim = false; player.Controls.KeyboardAim = false;
            var nativeBefore = CaptureNativePlacements(scene);
            var runtime = scene.EnableOfflineRifleAvailability(true);
            var ledger = runtime.Pickups ?? throw new InvalidOperationException("Public activation has no pickup ledger.");
            Check(ledger.Layout.Count == 1, "production adds exactly one custom station");
            var placement = ledger.Layout.Placements[0];
            Check(placement.SpawnId == 1 && placement.Weapon.Value == 10
                && placement.X4096 == fixture.Centre4096.X && placement.Y4096 == fixture.Centre4096.Y
                && placement.Z4096 == fixture.Centre4096.Z,
                "production fixed-point centre exactly matches independent authored fixture");
            Check(placement.InitialDelayTicks == 0 && placement.RespawnTicks == 1800 && placement.ReserveRefill == 16,
                "production initial delay, thirty-second cooldown and reserve refill match the authored policy");
            var world = ledger.World;
            Check(world.IsValid && world.Start.AuthorityEpoch != 0 && world.Layout == ledger.Layout.Digest,
                "public world has a complete nonzero scene identity and frozen layout");
            Key(keyboard, Keys.Enter, true);
            for (int i = 0; i < 600 && !player.LoadFlags.TestFlag(LoadFlags.Spawned); i++) Step();
            Key(keyboard, Keys.Enter, false); scene.CameraSequences.Current?.End();
            for (int i = 0; i < 180; i++) Step();
            scene.CameraSequences.Current?.End();
            Check(player.ModIsInPlay && player.Health > 0, "actual native local human spawns alive");
            Check(!runtime.TryPeek(player, out _, out _) && ledger.Revision == 0,
                "public spawn starts without custom ownership or resources");
            var nativeWeapon = player.CurrentWeapon;
            Key(keyboard, Keys.C, true); Step(); Key(keyboard, Keys.C, false); Step();
            Check(!runtime.TryPeek(player, out _, out _) && ledger.Revision == 0 && player.CurrentWeapon == nativeWeapon,
                "custom selection key cannot grant an unowned M7 or replace the native weapon");
            Check(ledger.TryState(0, out var initial) && initial.Available && initial.Revision == 0
                && initial.Key.Incarnation == 1, "world item is initially available incarnation one");
            PlayerSpawnEntity? approach = null;
            foreach (var spawn in scene.GetPlayerSpawnEntities())
                if (spawn.Id == fixture.NativeSpawnId) approach = spawn;
            Check(approach != null, "independent authored native approach spawn exists");
            var start = approach!.Position;
            CollisionResult floor = default;
            Check(CollisionDetection.CheckBetweenPoints(start + Vector3.UnitY * 2, start - Vector3.UnitY * 6,
                TestFlags.Players, scene, ref floor), "approach has real extracted native floor collision");
            start.Y = floor.Position.Y;
            player.Reposition(start.AddY(.5f), fixture.Direction, player.NodeRef);
            player.ModSetAim(fixture.Direction); player.Speed = Vector3.Zero;
            for (int i = 0; i < 15; i++) Step();
            Check(ledger.Revision == 0 && !runtime.TryPeek(player, out _, out _),
                "authored approach setup remains outside the station and unowned");
            int health = player.Health;
            var centre = placement.Position;
            bool collected = false;
            Key(keyboard, Keys.W, true);
            for (int i = 0; i < 90; i++)
            {
                Step();
                if (ledger.Revision > 0) { collected = true; break; }
                if ((player.Position - centre).WithY(0).Length <= .5f || player.Health <= 0) break;
            }
            Key(keyboard, Keys.W, false);
            for (int i = 0; i < 90 && !collected; i++) { Step(); collected = ledger.Revision > 0; }
            // Sloped rooms can coast beyond the contact height while descending.
            // Actual backwards input reapproaches after settling; no pose/floor or
            // lifecycle adjustment occurs after the declared initial setup.
            if (!collected && player.Health > 0)
            {
                Key(keyboard, Keys.S, true);
                for (int i = 0; i < 90; i++)
                {
                    Step();
                    if (ledger.Revision > 0) { collected = true; break; }
                    if (player.Health <= 0) break;
                }
                Key(keyboard, Keys.S, false);
            }
            Check(collected && ledger.Revision == 1,
                "actual native movement and PickUpItems contact consume exactly one custom world item");
            uint contactWorld = ledger.AuthorityFrame;
            Check(ledger.TryState(0, out var consumed) && !consumed.Available && consumed.Revision == 1
                && consumed.Key.Incarnation == 2, "real contact retains revision one and unavailable incarnation two");
            Check(consumed.ReadyAt == unchecked(contactWorld + 1800),
                "cooldown starts at actual contact world with no accelerated clock");
            Step();
            bool ownedNow = runtime.TryPeek(player, out var owned, out bool selected);
            Check(ownedNow && owned.Tick == unchecked(contactWorld + 1),
                "ownership appears on the next actual fixed step");
            Check(owned.Magazine == 4 && owned.Reserve == 16 && owned.LastAcceptedShotId == 0 && owned.Optic == 0,
                "actual first pickup funds four and sixteen without shooting or changing optics");
            Check(owned.Weapon.Kind == WeaponIdentityKind.Custom
                && owned.Weapon.ContentId == Guid.Parse("9a776851-f39f-4a76-8c13-c5d88875ef61")
                && owned.Owner.IsValid && owned.Owner.MatchId == world.Start.MatchId
                && owned.Owner.AuthorityEpoch == world.Start.AuthorityEpoch && owned.Owner.Slot == player.SlotIndex,
                "funded custom identity retains this exact nonzero public world owner");
            Check(selected, "eligible first acquisition auto-selects through the next fixed input phase");
            for (int i = 0; i < 10; i++) Step();
            Check(ledger.Revision == 1 && ledger.TryState(0, out var later) && !later.Available
                && later.Key == consumed.Key && later.ReadyAt == consumed.ReadyAt,
                "repeated contact cannot consume another incarnation or shorten its cooldown");
            Check(runtime.TryPeek(player, out owned, out _) && owned.Magazine == 4 && owned.Reserve == 16
                && runtime.AcceptedShots == 0, "repeated physical contact cannot duplicate funding or fire");
            Check(player.Health == health, "native walking and pickup contact leave actual health unchanged");
            Check(nativeBefore.SequenceEqual(CaptureNativePlacements(scene)),
                "native authored item and player-spawn counts, identities, types and positions remain unchanged");
            Check(ledger.World == world && !NetSession.Active,
                "entire pickup keeps one exact offline scene world and never joins network authority");
            Console.WriteLine($"PASS {checks} stock pickup scene checks for {fixture.Room}; owner={owned.Owner}; "
                + $"contact={contactWorld}, ready={consumed.ReadyAt}, health={health}->{player.Health}");
            Console.WriteLine("SCOPE: real offline activation/input/collision/PickUpItems/funding. "
                + "NOT_RUN: full1800-step respawn, dead-owner reacquisition, GL/art, devices, Android or online claims.");
            return 0;
            void Step() => scene.OnSimulationFrame();
        }
        catch (Exception error) { Console.Error.WriteLine("STOCK PICKUP FAIL " + error); return 1; }
        finally
        {
            try { NetSession.Stop(); scene?.DoCleanup(); }
            finally
            {
                MapAudit.ForceEveryone = force; NetTestScript.Enabled = script;
                Directory.SetCurrentDirectory(directory);
            }
        }
    }

    private static NativePlacement[] CaptureNativePlacements(Scene scene)
    {
        var rows = new List<NativePlacement>();
        foreach (var spawn in scene.GetPlayerSpawnEntities()) rows.Add(new(true, spawn.Id, 0, spawn.Position));
        foreach (var item in scene.GetItemSpawnEntities()) rows.Add(new(false, item.Id, (int)item.Data.ItemType, item.Position));
        return rows.OrderBy(row => row.PlayerSpawn).ThenBy(row => row.Id).ToArray();
    }
}
