using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using MphRead.Sound;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

// No MphRead type is touched before the resolver is installed. Deployment can
// place only this tool's small files beside immutable reused candidate libraries.
internal static class Program
{
    private static int Main(string[] args)
    {
        string Value(string key) => args.ElementAtOrDefault(Array.IndexOf(args, key) + 1)
            is { } value && Array.IndexOf(args, key) >= 0 ? value : throw new ArgumentException("Required " + key);
        string directory = Path.GetFullPath(Value("--binary-dir"));
        AssemblyLoadContext.Default.Resolving += (_, name) =>
            File.Exists(Path.Combine(directory, name.Name + ".dll"))
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : null;
        return RunCandidate(args, directory);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunCandidate(string[] args, string directory) => Acceptance.Run(args, directory);
}

internal static class Reflection
{
    internal const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static Type Type(string name) => typeof(Scene).Assembly.GetType("MphRead." + name, throwOnError: true)!;
    internal static object Enum(string name, string value) => System.Enum.Parse(Type("Mods.Network." + name), value);
    internal static object Create(string name, params object?[] arguments)
        => Activator.CreateInstance(Type("Mods.Network." + name), All, null, arguments, null)!;
    internal static object? Call(object target, string method, params object?[] arguments)
    {
        Type type = target as Type ?? target.GetType();
        var candidates = type.GetMethods(All).Where(m => m.Name == method && m.GetParameters().Length == arguments.Length).ToArray();
        if (candidates.Length != 1) throw new MissingMethodException(type.FullName, method + " (unique arity required)");
        return candidates[0].Invoke(target is Type ? null : target, arguments);
    }
    internal static object? Static(string type, string method, params object?[] arguments)
        => Call(Type("Mods.Network." + type), method, arguments);
    internal static object? Get(object target, string name)
    {
        Type type = target as Type ?? target.GetType(); object? owner = target is Type ? null : target;
        return type.GetField(name, All)?.GetValue(owner) ?? type.GetProperty(name, All)?.GetValue(owner);
    }
    internal static void Set(object target, string name, object? value)
    {
        Type type = target as Type ?? target.GetType(); object? owner = target is Type ? null : target;
        if (type.GetField(name, All) is { } field)
            field.SetValue(owner, value == null || field.FieldType.IsInstanceOfType(value) ? value : Convert.ChangeType(value, field.FieldType));
        else if (type.GetProperty(name, All) is { } property) property.SetValue(owner, value);
        else throw new MissingMemberException(type.FullName, name);
    }
    internal static int Int(object target, string name) => Convert.ToInt32(Get(target, name));
    internal static bool Bool(object target, string name) => Convert.ToBoolean(Get(target, name));
    internal static void Session(string name, object? value) => Set(typeof(NetSession), name, value);
}

internal static class Acceptance
{
    internal static int Run(string[] args, string directory)
    {
        string Value(string key, string? fallback = null)
        { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback ?? throw new ArgumentException("Required " + key); }
        string output = Path.GetFullPath(Value("--output"));
        if (Directory.Exists(output) && Directory.EnumerateFiles(output).Any())
            throw new ArgumentException("Use a fresh empty evidence directory; existing results are never replaced.");
        Directory.CreateDirectory(output);
        string expected = Value("--expected-client-sha256");
        string client = Path.Combine(directory, "ProjectPrime.dll");
        string Hash() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(client))).ToLowerInvariant();
        string before = Hash();
        var report = new Report { StartedUtc = DateTimeOffset.UtcNow, ClientPath = client, ClientSha256 = before,
            ClientMvid = typeof(Scene).Module.ModuleVersionId.ToString(), Output = output };
        try
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(expected, before)
                || Path.GetFullPath(typeof(Scene).Assembly.Location) != Path.GetFullPath(client))
                throw new InvalidOperationException("Loaded client path/hash does not match the exact candidate.");
            Reflection.Type("ConsoleSetup").GetMethod("Run", Reflection.All)!.Invoke(null, null);
            Directory.SetCurrentDirectory(Path.GetFullPath(Value("--assets")));
            Paths.UpdatePaths(); Paths.ChooseMphPath(); Paths.ChooseFhPath();
            if (Reflection.Bool(Reflection.Type("Mods.Headless"), "Active"))
                throw new InvalidOperationException("Headless process cannot establish client visual/audio acceptance.");
            GraphicsBackendPolicy.LoadPreference();
            using var window = new AcceptanceWindow(Value("--room", "MP1 SANCTORUS"), report,
                Value("--replay", ""), Value("--clip", ""));
            window.Run();
            if (report.Error != null) throw new InvalidOperationException(report.Error);
            report.Status = report.Missing.Count == 0 ? "PASS" : "INCOMPLETE";
        }
        catch (Exception error)
        {
            report.Status = "FAIL";
            report.Error = (error is TargetInvocationException invocation ? invocation.InnerException ?? error : error).ToString();
        }
        finally
        {
            report.FinishedUtc = DateTimeOffset.UtcNow; report.ClientHashAfter = Hash();
            if (report.ClientHashAfter != before) { report.Status = "FAIL"; report.Error += " Frozen client changed during fixture."; }
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Console.WriteLine("TIMED ACCEPTANCE " + report.Status + " " + Path.Combine(output, "result.json"));
        }
        return report.Status == "PASS" ? 0 : report.Status == "INCOMPLETE" ? 2 : 1;
    }
    internal sealed class Report
    {
        public string Status { get; set; } = "NOT_RUN";
        public DateTimeOffset StartedUtc { get; set; }
        public DateTimeOffset FinishedUtc { get; set; }
        public string ClientPath { get; set; } = "";
        public string ClientSha256 { get; set; } = "";
        public string ClientHashAfter { get; set; } = "";
        public string ClientMvid { get; set; } = "";
        public string Output { get; set; } = "";
        public string? Error { get; set; }
        public List<string> Checks { get; } = new();
        public List<string> Missing { get; } = new();
        public List<object> Stages { get; } = new();
        public List<object> AudioCalls { get; } = new();
        public string Scope => "True client GL/OpenAL with explicit injected authority/prediction facts; no live UDP, authority payment or physical audibility claim.";
    }
}

internal sealed class AcceptanceWindow : RenderWindow
{
    private readonly Acceptance.Report _report;
    private readonly string _replayPath, _clipPath;
    private PlayerEntity _owner = null!, _victim = null!;
    private object _presentation = null!;
    private TracedAudio _audio = null!;
    private SfxInstance _originalAudio = null!;
    private uint _frame = 100, _spawn = 1;
    private static readonly string[] Timers = { "_doubleDmgTimer", "_cloakTimer", "_deathaltTimer" };
    private static readonly string[] Types = { "DoubleDamage", "Cloak", "DeathAlt" };

    internal AcceptanceWindow(string room, Acceptance.Report report, string replayPath, string clipPath)
    {
        _report = report; _replayPath = replayPath; _clipPath = clipPath;
        ClientSize = new Vector2i(640, 480); IsVisible = true;
        Title = "Project Prime A3 timed grant acceptance";
        Reflection.Set(Scene, "_keyboardState", SyntheticInput.CreateKeyboard());
        Reflection.Set(Scene, "_mouseState", SyntheticInput.CreateMouse());
        Scene.Players.MaxPlayers = 2;
        AddPlayer(Hunter.Samus); AddPlayer(Hunter.Samus);
        Scene.Players.PlayerCount = 2; Scene.Players.MainPlayerIndex = 0;
        foreach (var p in Scene.Players.Items) p.IsBot = false;
        AddRoom(room, GameMode.Battle, playerCount: 2);
    }
    private void Check(bool value, string name)
    { if (!value) throw new InvalidOperationException(name); _report.Checks.Add(name); }
    private List<T> Entities<T>() where T : EntityBase
    {
        var values = new List<T>();
        foreach (var entity in Scene.Entities) if (entity is T typed) values.Add(typed);
        return values;
    }
    private object Effect(int type) => Reflection.Enum("TimedPowerUp", Types[type]);
    private ShotKey Life => ShotKey.For(_owner.SlotIndex, 0);
    private void Frame(uint frame) { _frame = frame; Reflection.Session(nameof(NetSession.NetFrame), frame); }
    private void Project(bool quiet = false)
        => Reflection.Call(_owner, "ModProjectTimedGrants", _presentation, Life, _frame, quiet);
    private void Reset()
    {
        Reflection.Static("NetPickupNetwork", "Reset");
        Reflection.Static("NetGrantNetwork", "Reset");
        _presentation = Reflection.Get(Reflection.Type("Mods.Network.NetGrantNetwork"), "Presentation")!;
        Project(quiet: true); _owner.Health = _victim.Health = 199;
        Reflection.Call(_owner, "ModForceForm", false);
        Reflection.Set(_owner, "_timeSincePickup", 55); Reflection.Set(_owner, "_ammoBarChangedColor", false);
        Reflection.Call(_owner, "UpdateAmmoBar");
    }
    private object Claim(int type, uint id, uint? sequence = null)
    {
        var key = Reflection.Create("PickupClaimKey", Life.AuthorityEpoch, Life.MatchId, Life.ShooterSlot, Life.Generation, Life.LifeId, sequence ?? id);
        var spawn = Reflection.Create("PowerUpSpawnKey", Life.AuthorityEpoch, Life.MatchId, 1u, -1, id);
        return Reflection.Create("PickupClaim", key, spawn, Effect(type), _frame, _owner.Position);
    }
    private object NativeContact(int type, uint id)
    {
        var key = Reflection.Create("PowerUpSpawnKey", Life.AuthorityEpoch, Life.MatchId, 1u, -1, id);
        var birth = Reflection.Create("PickupItemFact", key, Effect(type), _frame, true, _owner.Position);
        Check((bool)Reflection.Static("NetPickupNetwork", "ReceiveItem", birth)!, "authority item fact admitted " + Types[type]);
        Reflection.Static("TimedPickupProjection", "ApplyDynamic", Scene);
        var item = Entities<ItemInstanceEntity>().Single(i => Equals(Reflection.Get(i, "ModPickupSpawn"), key));
        Reflection.Static("NetPickupNetwork", "OwnerContact", _owner, item);
        var view = Reflection.Call(_presentation, "View", Life, Effect(type), _frame)!;
        uint sequence = Convert.ToUInt32(Reflection.Get(view, "PredictionSequence"));
        Check(sequence != 0 && Reflection.Bool(item, "ModPickupHidden"), "real owner contact predicts and hides exact item " + Types[type]);
        return Claim(type, id, sequence);
    }
    private object Fact(int type, uint id, uint revision, uint contact, string reason = "Active", uint? end = null, uint room = 1)
        => Reflection.Create("TimedGrantFact", Life, room, revision, Effect(type), (ulong)id, -1, id,
            contact, end ?? contact + 1800, Reflection.Enum("TimedGrantEnd", reason), _frame);
    private void Accept(object fact)
        => Check((bool)Reflection.Call(_presentation, "Accept", fact, _frame)!, "exact authority fact accepted");
    private object Verdict(object claim, string result, uint sequence = 0, uint grantFrame = 0)
        => Reflection.Create("PickupVerdict", claim, Reflection.Enum("PickupResult", result), _frame, _frame,
            (ulong)sequence, grantFrame, sequence == 0 ? 0u : grantFrame + 1800);

    protected override void OnLoad()
    {
        base.OnLoad();
        try
        {
            Check(!Reflection.Bool(Reflection.Type("Mods.Headless"), "Active"), "runtime is true client, not headless");
            Check(Sfx.Instance is SfxInstance, "real decoded OpenAL SfxInstance loaded; silent fallback is forbidden");
            _originalAudio = (SfxInstance)Sfx.Instance;
            _owner = Scene.Players.Items[0]; _victim = Scene.Players.Items[1];
            for (int i = 0; i < 120; i++) Scene.OnSimulationFrame();
            Scene.GameState.PrimeHunter = -1; Scene.GameState.BalancedMode = false; Scene.GameState.EnhancedHunters = false;
            NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                RoomKey = Scene.Room!.Meta.Name, Mode = (byte)GameMode.Battle, PlayerCount = 2,
                Flags = 1, TimeRemaining = -1, PointGoal = 0 }, false);
            Reflection.Session(nameof(NetSession.Role), Reflection.Enum("NetRole", "Host"));
            Reflection.Session(nameof(NetSession.IsAuthority), true);
            foreach (var p in new[] { _owner, _victim }) { NetPlayerLifecycle.SetOccupant(p.SlotIndex, 1); NetPlayerLifecycle.OnSpawn(p); }
            Reflection.Session(nameof(NetSession.Role), Reflection.Enum("NetRole", "Client"));
            Reflection.Session(nameof(NetSession.IsAuthority), false); Reflection.Session(nameof(NetSession.LocalSlot), 0);
            Reflection.Session(nameof(NetSession.ServerSession), null);
            Check(Life.Generation != 0 && Life.LifeId != 0 && Reflection.Bool(_owner, "ModIsInPlay"), "fixture has actual active native life");
            Reflection.Set(Scene, "_cameraMode", CameraMode.Player); Reflection.Call(_owner, "SetUpHud");
            Check(_owner.HudReady && _owner.IsMainPlayer, "real owner HUD and player camera initialized");
            Reflection.Call(_victim, "ModPlaceAt", _owner.Position + new Vector3(20, 0, 0));
            _audio = new(_originalAudio, _report.AudioCalls); Reflection.Set(typeof(Sfx), nameof(Sfx.Instance), _audio);
            for (int type = 0; type < Types.Length; type++) Exercise(type);
            if (_replayPath.Length == 0) _report.Missing.Add("NOT_RUN: no genuine recorded A3 replay supplied for native linear/seek checks.");
            else ReplayProbe.Run(Path.GetFullPath(_replayPath), "recording", Scene, _report);
            if (_clipPath.Length == 0) _report.Missing.Add("NOT_RUN: no genuine exported A3 clip supplied for native linear/seek checks.");
            else ReplayProbe.Run(Path.GetFullPath(_clipPath), "clip", Scene, _report);
            _report.Missing.Add("NOT_RUN: historical capsule native audiovisual compatibility; current recording/clip paths cannot establish older format acceptance.");
            _report.Missing.Add("NOT_RUN: live owner/observer UDP settlement, authority damage payment and profile matrix; injected native fixture facts cannot establish those gates.");
        }
        catch (Exception error) { _report.Error = error.ToString(); }
        finally
        {
            if (_originalAudio != null) Reflection.Set(typeof(Sfx), nameof(Sfx.Instance), _originalAudio);
            Close();
        }
    }

    private void Exercise(int type)
    {
        Frame(100); Reset(); Capture(type, "baseline");
        float baseBeam = Fire(); int baseBomb = Bomb();
        uint id = _spawn++;
        int gainCue = type == 1 ? (int)SfxId.CLOAK_POWER_UP : (int)SfxId.DOUBLE_DAMAGE_POWER_UP;
        int cues = _audio.FreeCalls(gainCue);
        object claim = NativeContact(type, id);
        Project();
        Check(Reflection.Int(_owner, Timers[type]) == 1800, "actual provisional timer " + Types[type]);
        Check(Reflection.Int(_owner, "_timeSincePickup") == 0, "native first pickup HUD flash restored " + Types[type]);
        Reflection.Call(_owner, "UpdateAmmoBar");
        Check(Reflection.Int(_owner, "_ammoBarPalette") == 1, "native HUD pickup palette flashes " + Types[type]);
        Check(_audio.FreeCalls(gainCue) == cues + 1, "one actual backend gain cue " + Types[type]);
        if (type == 0) Check(!_owner.DoubleDamage && Fire() == baseBeam && Bomb() == baseBomb, "provisional actual beam/bomb damage unchanged");
        if (type == 2) Check(_owner.IsAltForm || _owner.IsMorphing, "DeathAlt native provisional form entry");
        Capture(type, "provisional");
        Reflection.Set(_owner, "_timeSincePickup", 11);
        uint formStarted = Convert.ToUInt32(Reflection.Get(_owner, "_modFormStartedFrame"));
        Vector3 formSpeed = _owner.Speed;
        object fact = Fact(type, id, 1, 100); Accept(fact); Project();
        Reflection.Call(_presentation, "Settle", Verdict(claim, "Grant", id, 100), _frame); Project();
        Check(Reflection.Int(_owner, "_timeSincePickup") == 11 && _audio.FreeCalls(gainCue) == cues + 1,
            "delta/verdict do not duplicate native pickup flash or cue " + Types[type]);
        Check(!(bool)Reflection.Call(_owner, "ModConfirmedTimedEffect", Effect(type))!, "contact input remains unconfirmed " + Types[type]);
        if (type == 2)
        {
            Check(Convert.ToUInt32(Reflection.Get(_owner, "_modFormStartedFrame")) == formStarted && _owner.Speed == formSpeed,
                "DeathAlt delta/verdict do not repeat native morph or impulse");
            Check(CollisionDebit() == 0, "provisional DeathAlt native collision cannot damage victim");
        }
        Frame(101); Project();
        Check((bool)Reflection.Call(_owner, "ModConfirmedTimedEffect", Effect(type))!, "following native input confirmed " + Types[type]);
        if (type == 0) Check(Fire() == baseBeam * 2 && Bomb() == baseBomb * 2, "confirmed actual beam/bomb damage doubles");
        if (type == 2) Check(CollisionDebit() > 0, "confirmed DeathAlt actual native victim collision causes client predicted debit");
        Capture(type, "confirmed");

        // Pending renewal is independent of durable truth. Refusal removes just
        // that overlay and must not subtract the original grant.
        Frame(200); object renewal = Claim(type, _spawn++);
        Check((bool)Reflection.Call(_presentation, "Predict", renewal, _frame)!, "renewal prediction admitted " + Types[type]);
        Project(); Capture(type, "renewal-pending");
        Reflection.Call(_presentation, "Settle", Verdict(renewal, "Taken"), _frame); Project();
        Check(Reflection.Int(_owner, Timers[type]) == 1700, "refused renewal restores original grant timer " + Types[type]);
        Capture(type, "renewal-refused");
        uint renewed = _spawn++; Accept(Fact(type, renewed, 2, 200)); Project(); Capture(type, "renewed");
        int previousCalls = _audio.FreeCalls(gainCue); Reflection.Set(_owner, "_timeSincePickup", 55); Project(quiet: true);
        Check(_audio.FreeCalls(gainCue) == previousCalls && Reflection.Int(_owner, "_timeSincePickup") == 55,
            "quiet Keep never restarts pickup flash/cue " + Types[type]);
        foreach (uint frame in new[] { 1580u, 1760u, 1999u, 2000u })
        {
            Frame(frame); Project(); Capture(type, "expiry-" + (2000 - frame));
            Check(Reflection.Int(_owner, Timers[type]) == 2000 - frame, "native stage/expiry timer exact " + Types[type]);
        }
        Check(!(bool)Reflection.Call(_owner, "ModConfirmedTimedEffect", Effect(type))!, "native effect authorization expires " + Types[type]);
        Frame(2100); uint resumed = _spawn++; object resume = Fact(type, resumed, 3, 2100);
        Reflection.Set(_owner, "_timeSincePickup", 55);
        Reflection.Set(resume, "Resume", true); Accept(resume); int beforeResume = _audio.FreeCalls(gainCue); Project(quiet: true);
        Check(Reflection.Int(_owner, "_timeSincePickup") == 55 && _audio.FreeCalls(gainCue) == beforeResume,
            "quiet Resume never fabricates pickup flash/cue " + Types[type]);
        Capture(type, "quiet-resume");
        _owner.Health = 0; Project(quiet: true);
        Check(Reflection.Int(_owner, Timers[type]) == 0, "native dead actor clears effect " + Types[type]);
        Capture(type, "dead"); _owner.Health = 199;
        NetPlayerLifecycle.SetOccupant(_owner.SlotIndex, (ushort)(Life.Generation + 1));
        Reflection.Session(nameof(NetSession.Role), Reflection.Enum("NetRole", "Host")); Reflection.Session(nameof(NetSession.IsAuthority), true);
        NetPlayerLifecycle.OnSpawn(_owner);
        Reflection.Session(nameof(NetSession.Role), Reflection.Enum("NetRole", "Client")); Reflection.Session(nameof(NetSession.IsAuthority), false);
        Project(quiet: true);
        Check(Reflection.Int(_owner, Timers[type]) == 0, "new actual slot/life cannot inherit old grant " + Types[type]);
        Frame(2200);
        Check((bool)Reflection.Call(_presentation, "ObserveRoom", Life.AuthorityEpoch, Life.MatchId, 2u)!, "new room discovery fence admitted");
        Project(quiet: true);
        Check(!Reflection.Bool(_presentation, "HasCoverage") && Reflection.Int(_owner, Timers[type]) == 0,
            "discovery room fence removes old native effect without fabricating grant coverage " + Types[type]);
        Capture(type, "room-fence-no-coverage");
        uint next = _spawn++; Accept(Fact(type, next, 1, 2200, room: 2)); Project(quiet: true);
        object stale = fact; Check(!(bool)Reflection.Call(_presentation, "Accept", stale, _frame)!, "old room fact cannot restore old ownership " + Types[type]);
        Capture(type, "new-room-fact-projection");
        Frame(2300); Reset();
        object refused = NativeContact(type, _spawn++); Capture(type, "fresh-prediction");
        Reflection.Call(_presentation, "Settle", Verdict(refused, "Taken"), _frame); Project();
        Check(Reflection.Int(_owner, Timers[type]) == 0, "fresh refused prediction removes native overlay " + Types[type]);
        Capture(type, "fresh-refusal");
    }

    private int CollisionDebit()
    {
        // Exercise the actual CheckPlayerCollision -> TakeDamage -> client
        // prediction path. Positions/lives are fixture inputs, not a movement
        // or authority-payment claim. Do not change role to make the grant pass.
        Reflection.Static("NetHitPrediction", "Reset");
        int health = _victim.Health; Vector3 victimPosition = _victim.Position;
        try
        {
            Reflection.Call(_owner, "ModForceForm", true); Reflection.Call(_victim, "ModForceForm", false);
            _victim.Health = 399;
            Reflection.Set(_victim, "_damageInvulnTimer", 0);
            Reflection.Set(_victim, "_spawnInvulnTimer", 0);
            Reflection.Call(_victim, "ModSetSpawnProtectionFromAuthority", false);
            Reflection.Call(_victim, "ModPlaceAt", _owner.Position + Vector3.UnitX * .1f);
            Reflection.Set(_owner, "_volume", CollisionVolume.Move((CollisionVolume)Reflection.Get(_owner, "_volumeUnxf")!, _owner.Position));
            Reflection.Set(_victim, "_volume", CollisionVolume.Move((CollisionVolume)Reflection.Get(_victim, "_volumeUnxf")!, _victim.Position));
            Check(Vector3.Distance(_owner.Volume.SpherePosition, _victim.Volume.SpherePosition)
                < _owner.Volume.SphereRadius + _victim.Volume.SphereRadius, "actual loaded native victim volumes overlap");
            int before = Convert.ToInt32(Reflection.Static("NetHitPrediction", "HealthFor", _victim.SlotIndex, _victim.Health));
            Reflection.Call(_owner, "CheckPlayerCollision");
            int after = Convert.ToInt32(Reflection.Static("NetHitPrediction", "HealthFor", _victim.SlotIndex, _victim.Health));
            _report.Stages.Add(new { stage = "native-DeathAlt-victim-collision", fixtureFrame = _frame,
                role = NetSession.Role.ToString(), before, after, rawHealth = _victim.Health,
                prediction = Reflection.Static("NetHitPrediction", "Capture"), authorityPayment = "NOT_RUN" });
            return before - after;
        }
        finally
        {
            Reflection.Static("NetHitPrediction", "Reset"); _victim.Health = health;
            Reflection.Call(_victim, "ModPlaceAt", victimPosition);
            Reflection.Set(_victim, "_volume", CollisionVolume.Move((CollisionVolume)Reflection.Get(_victim, "_volumeUnxf")!, victimPosition));
        }
    }

    private float Fire()
    {
        Reflection.Call(_owner, "ModForceForm", false); Reflection.Call(_owner, "ModArmWeapon", BeamType.PowerBeam);
        _owner.EquipInfo.ChargeLevel = 0; Reflection.Set(_owner, "_timeSinceShot", 1000);
        Reflection.Set(_owner, "_muzzlePos", _owner.Position + Vector3.UnitY);
        Reflection.Set(_owner, "_aimPosition", _owner.Position + Vector3.UnitY + Vector3.UnitZ * 10);
        var before = Entities<BeamProjectileEntity>().ToHashSet();
        Check((bool)Reflection.Call(_owner, "TryFireWeapon")!, "actual native fixture beam emitted");
        var beam = Entities<BeamProjectileEntity>().Single(b => !before.Contains(b) && b.Owner == _owner);
        float value = beam.Damage; beam.Destroy(); Scene.RemoveEntity(beam); return value;
    }
    private int Bomb()
    {
        var type = Reflection.Type("Mods.Network.NetBombs"); bool old = Reflection.Bool(type, "Admitting");
        try
        {
            Reflection.Set(type, "Admitting", true); Reflection.Set(_owner, "_bombAmmo", 3);
            var bomb = (BombEntity?)Reflection.Call(_owner, "SpawnBomb"); Check(bomb != null, "actual native fixture bomb emitted");
            int value = bomb!.Damage; bomb.Destroy(); Scene.RemoveEntity(bomb); return value;
        }
        finally { Reflection.Set(type, "Admitting", old); }
    }
    private void Capture(int type, string stage)
    {
        // With no transport, NetSession.Update cannot invent a new authority
        // clock. Run the entire actual client simulation phase at the explicit
        // fixture observation clock, including native effects/HUD/audio updates.
        Scene.OnSimulationFrame();
        Check(NetSession.NetFrame == _frame, "native phase retains explicit injected authority observation clock");
        if (_owner.Health > 0 && Reflection.Int(_owner, Timers[type]) > 0 && type < 2)
        {
            int handle = Reflection.Int(_owner, type == 0 ? "_dblDamageSfxHandle" : "_cloakSfxHandle");
            Check(_audio.NativeHandlePlaying(handle), "actual OpenAL timed stage source is playing " + Types[type] + " " + stage);
            if (type == 0)
            {
                int remaining = Reflection.Int(_owner, Timers[type]);
                Check(Reflection.Int(_owner, "_doubleDamageSpeed") == (remaining <= 240 ? 3 : remaining <= 420 ? 2 : 1),
                    "native double-damage HUD animation stage exact");
            }
        }
        Scene.OnDrawFrame(); Scene.OnRenderFrame(); GL.Finish();
        string prefix = Types[type] + "-" + stage;
        Check(ScreenCapture.Save(Scene, Path.Combine(_report.Output, prefix + "-world.png"))
            && ScreenCapture.SaveWindow(Scene, Path.Combine(_report.Output, prefix + "-hud.png")), "actual GL color readback " + prefix);
        int framebuffer = Reflection.Int(Scene, "_frameBuffer"); var size = Scene.RenderSize;
        int previous = GL.GetInteger(GetPName.ReadFramebufferBinding);
        var depth = new float[size.X * size.Y];
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GL.ReadPixels(0, 0, size.X, size.Y, PixelFormat.DepthComponent, PixelType.Float, depth);
        }
        finally { GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previous); }
        Check(depth.All(float.IsFinite) && depth.Any(v => v < 1), "actual covered finite world depth " + prefix);
        Check(GL.GetError() == ErrorCode.NoError, "GL readback without error " + prefix);
        byte[] bytes = MemoryMarshal.AsBytes(depth.AsSpan()).ToArray();
        File.WriteAllBytes(Path.Combine(_report.Output, prefix + "-depth-float32.bin"), bytes);
        _report.Stages.Add(new { effect = Types[type], stage, fixtureFrame = _frame, sceneFrame = Scene.FrameCount,
            nativeTimer = Reflection.Int(_owner, Timers[type]), pickupFlash = Reflection.Int(_owner, "_timeSincePickup"),
            ammoPalette = Reflection.Int(_owner, "_ammoBarPalette"), alpha = Reflection.Get(_owner, "_targetAlpha"),
            _owner.IsAltForm, _owner.IsMorphing, confirmed = Reflection.Call(_owner, "ModConfirmedTimedEffect", Effect(type)),
            depthSha256 = Convert.ToHexString(SHA256.HashData(bytes)), width = size.X, height = size.Y,
            audio = _audio.NativeSources(), view = Reflection.Call(_presentation, "View", Life, Effect(type), _frame)?.ToString() });
    }
}
