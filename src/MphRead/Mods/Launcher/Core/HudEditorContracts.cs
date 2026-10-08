using System;
using System.Collections.Immutable;
using System.Numerics;
using MphRead.Mods.Render.Hud;

namespace MphRead.Mods.Launcher.Core;

public enum HudEditorAction
{
    Open, Use, Cancel, NextPreset, Undo, Redo, LockAll, UnlockAll, AlignLeft, AlignTop,
    NativeElements, ResetHud, ToggleVisible, ToggleLock, NextAnchor, NextVisibility,
    ResetElement, ResetSection, NextAspect, NextHunter, NextScenario, NextGrid, ToggleGuides,
    SaveNamed, LoadNamed, ExportJson, ImportJson, PropertyPrevious, PropertyNext, PropertyApply,
    PropertyReset, NextPalette, NextCrosshairTarget, ToggleCrosshairOverride, NextCrosshairPreset,
    ShareCrosshair, ImportCrosshair, NextRadarPreset, NudgeLeft, NudgeRight, NudgeUp, NudgeDown,
    ScaleDown, ScaleUp, ApplyLayout
}
public readonly record struct HudEditorRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Vector2 Center => new(X + Width / 2, Y + Height / 2);
    public bool Contains(Vector2 point, float padding = 0) => point.X >= X-padding && point.X <= Right+padding && point.Y >= Y-padding && point.Y <= Bottom+padding;
}
public sealed record HudEditorProperty(string Path, string Label, string Value, string Kind, ImmutableArray<string> Choices);
public sealed record HudEditorElement(int Index, string Id, HudEditorRect Bounds, bool Enabled, bool Locked, bool Selected, float Opacity, string Color);
public sealed record HudEditorSnapshot
{
    public Guid Lifetime { get; init; }
    public long Revision { get; init; }
    public string ProfileJson { get; init; } = "";
    public string Name { get; init; } = "";
    public string BasePreset { get; init; } = "";
    public bool CanUndo { get; init; }
    public bool CanRedo { get; init; }
    public bool Closed { get; init; }
    public int Selected { get; init; }
    public ImmutableArray<HudEditorElement> Elements { get; init; } = ImmutableArray<HudEditorElement>.Empty;
    public ImmutableArray<HudEditorProperty> Properties { get; init; } = ImmutableArray<HudEditorProperty>.Empty;
    public int PropertyPage { get; init; }
    public int PropertyPageCount { get; init; }
    public int PropertyIndex { get; init; }
    public HudEditorProperty? Property { get; init; }
    public string X { get; init; } = "";
    public string Y { get; init; } = "";
    public string Scale { get; init; } = "";
    public string Opacity { get; init; } = "";
    public string Color { get; init; } = "";
    public HudAnchor Anchor { get; init; }
    public HudVisibility Visibility { get; init; }
    public int GridSize { get; init; }
    public bool SnapGuides { get; init; }
    public float PreviewWidth { get; init; }
    public float PreviewHeight { get; init; }
    public float CanvasWidth { get; init; }
    public float CanvasHeight { get; init; }
    public int PreviewHunter { get; init; }
    public HudPreviewScenario Scenario { get; init; }
    public int CrosshairTarget { get; init; }
    public bool CrosshairOverride { get; init; }
    public string JsonText { get; init; } = "";
    public long JsonRevision { get; init; }
    public string Status { get; init; } = "";
    public string Error { get; init; } = "";
    public HudEditorRect Surface { get; init; }
    public float? GuideX { get; init; }
    public float? GuideY { get; init; }
}
public interface IHudEditorBackend
{
    string? Warning { get; }
    void SaveNamed(string name, HudProfile profile);
    HudProfile LoadNamed(string name);
}
public sealed class HudEditorEngineBackend : IHudEditorBackend
{
    public string? Warning => HudProfiles.LoadWarning;
    public void SaveNamed(string name, HudProfile profile) => HudProfiles.SaveNamed(name, profile);
    public HudProfile LoadNamed(string name) => HudProfiles.LoadNamed(name);
}
