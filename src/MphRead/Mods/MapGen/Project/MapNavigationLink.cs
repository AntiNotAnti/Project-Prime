using System;

namespace MphRead.Mods.MapGen
{
    public enum MapNavigationLinkKind { Jump, Drop, JumpPad, Teleporter, Platform, Manual }

    /// <summary>
    /// Optional semantic hint for either endpoint of an authored navigation link.
    /// Auto preserves geometry-derived classification and lets the traversal kind
    /// provide a conservative default. The remaining values map directly to the
    /// native MPH node types consumed by PlayerAi.
    /// </summary>
    public enum MapNavigationAnchorKind
    {
        Auto,
        Navigation,
        Special,
        Aerial,
        Vantage,
        AltForm,
        Hazard
    }

    public sealed class MapNavigationLink
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public MapNavigationLinkKind Kind { get; set; } = MapNavigationLinkKind.Manual;
        public float[] From { get; set; } = new float[3];
        public float[] To { get; set; } = new float[3];
        public bool Bidirectional { get; set; }
        public MapNavigationAnchorKind FromNodeKind { get; set; } = MapNavigationAnchorKind.Auto;
        public MapNavigationAnchorKind ToNodeKind { get; set; } = MapNavigationAnchorKind.Auto;
    }
}
