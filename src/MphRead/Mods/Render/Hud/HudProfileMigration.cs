namespace MphRead.Mods.Render.Hud;

public static class HudProfileMigration
{
    public static HudProfile FromLegacy()
    {
        var p = HudProfileDefaults.Create(Features.ProHud ? "Project Prime" : "Classic");
        p.Crosshair = CrosshairProfile.FromLegacy(Crosshair.Style, Crosshair.Size);
        p.FixedWeapon = Features.ProHudFixedWeapon;
        p.NativeReticleOpacity = Features.ReticleOpacity;
        p.Elements["core.radar"].Enabled = Radar.Enabled;
        p.RadarBackground = Radar.ShowBackground; p.RadarOutlines = Radar.ShowOutlines;
        p.Elements["combat.killFeed"].Enabled = Features.KillFeedEnabled;
        p.Validate(); return p;
    }
}

/// <summary>Publication happens only on load or explicit settings changes, never during rendering.</summary>
public static class HudProfiles
{
    private static HudProfile _current = new();
    private static HudRuntimeProfile _runtime = new(_current);
    private static HudProfileStore? _store;
    public static HudRuntimeProfile Runtime => System.Threading.Volatile.Read(ref _runtime);
    public static int Generation { get; private set; }
    public static string? LoadWarning { get; private set; }
    public static HudProfile CopyCurrent() => _current.DeepClone();
    public static void Load(string directory)
    {
        _store = new(directory);
        HudProfile profile;
        try { profile = _store.Load("active"); LoadWarning = null; }
        catch (System.Exception e) when (HudProfileStore.Recoverable(e))
        {
            profile = HudProfileMigration.FromLegacy();
            bool exists = System.IO.File.Exists(System.IO.Path.Combine(directory, "active.json"));
            if (exists) LoadWarning = "Could not load HUD profile; using legacy settings: " + e.Message;
            else
                try { _store.Save("active", profile); }
                catch (System.Exception ex) when (HudProfileStore.Recoverable(ex)) { LoadWarning = "Could not save HUD migration: " + ex.Message; }
        }
        Publish(profile);
    }
    public static void Publish(HudProfile profile)
    {
        var copy = profile.DeepClone();
        var runtime = new HudRuntimeProfile(copy);
        _current = copy;
        System.Threading.Volatile.Write(ref _runtime, runtime);
        Generation++;
        Features.ProHud = copy.Mode != HudMode.Classic;
        Features.ProHudFixedWeapon = copy.FixedWeapon;
        Features.ReticleOpacity = copy.NativeReticleOpacity;
        Radar.Enabled = copy.Elements["core.radar"].Enabled;
        Radar.ShowBackground = copy.RadarBackground; Radar.ShowOutlines = copy.RadarOutlines;
        Features.KillFeedEnabled = copy.Elements["combat.killFeed"].Enabled;
    }
    public static void Save(HudProfile profile)
    {
        if (_store == null) throw new System.InvalidOperationException("HUD profile storage is not initialized.");
        _store.Save("active", profile); Publish(profile);
    }
    public static void SaveNamed(string name, HudProfile profile)
    {
        if (_store == null) throw new System.InvalidOperationException("HUD profile storage is not initialized.");
        if (string.Equals(name.Trim(), "active", System.StringComparison.OrdinalIgnoreCase))
            throw new System.ArgumentException("The name active is reserved. Choose a name for your library profile.");
        var copy = profile.DeepClone(); copy.Name = name; _store.Save(name, copy);
    }
    public static HudProfile LoadNamed(string name, string? directory = null)
        => (_store ?? (directory != null ? new HudProfileStore(directory) : throw new System.InvalidOperationException("HUD profile storage is not initialized."))).Load(name);
}
