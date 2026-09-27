using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render.Hud;

public enum HudMode { Classic, ProjectPrime, Custom }
public enum HudAnchor { TopLeft, TopCenter, TopRight, CenterLeft, Center, CenterRight, BottomLeft, BottomCenter, BottomRight }
public enum HudVisibility { Always, Multiplayer, Spectator, Damaged, AmmoNotFull, Objective, Combat }
[Flags]
public enum HudContext { Playing = 1, SpectatorPov = 2, SpectatorFree = 4, Replay = 8, All = 15 }

public sealed class HudElementLayout
{
    public bool Enabled { get; set; } = true;
    public HudAnchor Anchor { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Scale { get; set; } = 1;
    public float Opacity { get; set; } = 1;
    public string Color { get; set; } = "#FFFFFF";
    public int Layer { get; set; }
    public bool Locked { get; set; }
    public HudVisibility Visibility { get; set; }
    public HudContext Contexts { get; set; } = HudContext.All;
    internal void Validate()
    {
        if (!Enum.IsDefined(Anchor)) Anchor = HudAnchor.TopLeft;
        if (!Enum.IsDefined(Visibility)) Visibility = HudVisibility.Always;
        OffsetX = HudProfile.Clamp(OffsetX, -3840, 3840, 0);
        OffsetY = HudProfile.Clamp(OffsetY, -2160, 2160, 0);
        Scale = HudProfile.Clamp(Scale, .1f, 8, 1);
        Opacity = HudProfile.Clamp(Opacity, 0, 1, 1);
        Contexts &= HudContext.All;
        Layer = Math.Clamp(Layer, 0, 3);
        Color = HudColor.Normalize(Color);
    }
}

/// <summary>Local presentation values only. Never referenced by simulation or networking.</summary>
public sealed class HudProfile
{
    public const int CurrentSchema = 1;
    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Name { get; set; } = "Project Prime";
    public string BasePreset { get; set; } = "Project Prime";
    public HudMode Mode { get; set; } = HudMode.ProjectPrime;
    public float GlobalScale { get; set; } = 1;
    public float GlobalOpacity { get; set; } = 1;
    public float TextScale { get; set; } = 1;
    public float IconScale { get; set; } = 1;
    public bool ReduceMotion { get; set; }
    public bool ReduceTransparency { get; set; }
    public float SafeArea { get; set; }
    public bool IndependentGauges { get; set; }
    public bool FixedWeapon { get; set; } = true;
    public bool RadarBackground { get; set; }
    public bool RadarOutlines { get; set; } = true;
    public float NativeReticleOpacity { get; set; } = 1;
    public HudMeterProfile Health { get; set; } = new();
    public HudMeterProfile Ammo { get; set; } = new() { Warning = .5f, Danger = .2f };
    public HudRadarProfile Radar { get; set; } = new();
    public HudHitMarkerProfile HitMarker { get; set; } = new();
    public HudInventoryProfile Inventory { get; set; } = new();
    public HudNotificationProfile Notifications { get; set; } = new();
    public HudKillFeedProfile KillFeed { get; set; } = new();
    public CrosshairProfile Crosshair { get; set; } = new();
    public CrosshairProfile?[] WeaponCrosshairs { get; set; } = new CrosshairProfile?[9];
    public CrosshairProfile? ZoomCrosshair { get; set; }
    public Dictionary<string, HudElementLayout> Elements { get; set; } = HudProfileDefaults.Elements();

    public HudProfile DeepClone() => HudProfileStore.Parse(HudProfileStore.Serialize(this));
    public void DetachGauges()
    {
        if(IndependentGauges) return;
        for(int i=0;i<2;i++)
        {
            var parent=Elements[i==0 ? "core.health" : "core.ammo"];
            var gauge=Elements[i==0 ? "core.healthGauge" : "core.ammoGauge"];
            gauge.Anchor=parent.Anchor; gauge.Scale=parent.Scale; gauge.Opacity=parent.Opacity;
            gauge.OffsetX=parent.OffsetX+11.25f*GlobalScale*parent.Scale;
            gauge.OffsetY=parent.OffsetY+90*GlobalScale*parent.Scale;
        }
        IndependentGauges=true;
    }
    public void ResetElement(string id)
    {
        var defaults = HudProfileDefaults.Create(BasePreset);
        if (!defaults.Elements.TryGetValue(id, out var layout)) return;
        Elements[id] = layout;
        switch (id)
        {
            case "core.crosshair": Crosshair=defaults.Crosshair; WeaponCrosshairs=defaults.WeaponCrosshairs; ZoomCrosshair=null; HitMarker=defaults.HitMarker; break;
            case "core.health": Health=defaults.Health; break;
            case "core.ammo": Ammo=defaults.Ammo; break;
            case "core.weapons": Inventory=defaults.Inventory; break;
            case "core.radar": Radar=defaults.Radar; RadarBackground=defaults.RadarBackground; RadarOutlines=defaults.RadarOutlines; break;
            case "combat.notifications": Notifications=defaults.Notifications; break;
            case "combat.killFeed": KillFeed=defaults.KillFeed; break;
        }
    }
    public void Validate()
    {
        if (SchemaVersion != CurrentSchema) throw new FormatException("Unsupported HUD profile version.");
        Name = string.IsNullOrWhiteSpace(Name) ? "Custom" : Name.Trim();
        if (Name.Length > 64) Name = Name[..64];
        if (!Enum.IsDefined(Mode)) Mode = HudMode.Custom;
        if (Array.IndexOf(HudProfileDefaults.Presets, BasePreset) < 0) BasePreset = "Project Prime";
        GlobalScale = Clamp(GlobalScale, .1f, 8, 1);
        GlobalOpacity = Clamp(GlobalOpacity, 0, 1, 1);
        TextScale = Clamp(TextScale,.5f,3,1); IconScale = Clamp(IconScale,.5f,3,1);
        Notifications ??= new(); Notifications.Validate();
        SafeArea = Clamp(SafeArea, 0, .2f, 0);
        NativeReticleOpacity = Clamp(NativeReticleOpacity, 0, 1, 1);
        Health ??= new(); Health.Validate();
        Ammo ??= new() { Warning = .5f, Danger = .2f }; Ammo.Validate();
        Radar ??= new(); Radar.Validate();
        HitMarker ??= new(); HitMarker.Validate();
        Inventory ??= new(); Inventory.Validate();
        KillFeed ??= new(); KillFeed.Validate();
        Crosshair ??= new(); Crosshair.Validate();
        if (WeaponCrosshairs == null) WeaponCrosshairs = new CrosshairProfile?[9];
        var weapons = WeaponCrosshairs; Array.Resize(ref weapons, 9); WeaponCrosshairs = weapons;
        foreach (var weapon in WeaponCrosshairs) weapon?.Validate();
        ZoomCrosshair?.Validate();
        Elements ??= new();
        // A bounded registry, not an executable extension format or an asset loader.
        var clean = HudProfileDefaults.Elements();
        foreach (string id in HudProfileDefaults.ElementIds)
            if (Elements.TryGetValue(id, out var value) && value != null)
            { value.Validate(); clean[id] = value; }
        Elements = clean;
    }
    internal static float Clamp(float value, float min, float max, float fallback)
        => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
