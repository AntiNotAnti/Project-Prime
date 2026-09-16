using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>How the optional in-renderer Nintendo DS lower screen is shown.</summary>
    public enum NativeBottomScreenMode
    {
        Off,
        Popup,
        AlwaysVisible
    }

    /// <summary>
    /// How the customizable HudOverlay binding owns a desktop virtual cursor
    /// session. Direct pen/finger contacts do not use this mode.
    /// </summary>
    public enum NativeBottomScreenActivationMode
    {
        Toggle,
        Hold
    }

    /// <summary>Visual and interaction layout used by the optional lower screen.</summary>
    public enum NativeBottomScreenStyle
    {
        /// <summary>
        /// DS-inspired touch surface: direct beam buttons across the top,
        /// weapon controls, a central aim area, and the Morph Ball button.
        /// </summary>
        ClassicDs,
        /// <summary>The previous always-open six-affinity selector.</summary>
        AffinitySelector
    }

    /// <summary>
    /// Ownership policy for the Classic DS panel's aim surface. Free keeps
    /// the historical global stylus/touch look path; TrueDs routes a contact
    /// started on the panel through the native bottom-screen path.
    /// </summary>
    public enum NativeBottomScreenAimMode
    {
        Free,
        TrueDs
    }

    /// <summary>Immutable route selected for one native pointer contact.</summary>
    public enum NativeBottomScreenPointerRoute
    {
        None,
        Control,
        Aim,
        Suppressed
    }

    /// <summary>
    /// Immutable canonical position of the native True DS aim contact for the
    /// current presentation frame. The position is expressed in the DS
    /// panel's 256x192 coordinate space rather than in window pixels.
    /// </summary>
    public readonly record struct NativeBottomScreenAimContactSnapshot(
        bool Active, Vector2 PositionDs);

    /// <summary>Interactive regions on the DS-inspired lower screen.</summary>
    public enum NativeBottomScreenRegion
    {
        None,
        Aim,
        PowerBeam,
        Missile,
        NextWeapon,
        WeaponSelect,
        AltForm,
        VoltDriver,
        Battlehammer,
        Imperialist,
        Judicator,
        Magmaul,
        ShockCoil
    }

    public readonly record struct NativeBottomScreenButton(
        NativeBottomScreenRegion Region, Vector2 Position, float Radius,
        string Label);

    /// <summary>
    /// Normalized positions for the five Classic DS controls. Radii and labels
    /// are intentionally fixed to preserve the native control proportions;
    /// only centers are user-adjustable.
    /// </summary>
    public readonly record struct NativeBottomScreenClassicLayoutOptions(
        float PowerBeamX, float PowerBeamY,
        float MissileX, float MissileY,
        float NextWeaponX, float NextWeaponY,
        float WeaponSelectX, float WeaponSelectY,
        float AltFormX, float AltFormY)
    {
        public const float PowerBeamRadius = 22;
        public const float MissileRadius = 20;
        public const float NextWeaponRadius = 30;
        public const float WeaponSelectRadius = 26;
        public const float AltFormRadius = 22;

        public static NativeBottomScreenClassicLayoutOptions Default => new(
            26f / 256, 26f / 192,
            80f / 256, 24f / 192,
            150f / 256, 30f / 192,
            222f / 256, 28f / 192,
            228f / 256, 166f / 192);

        public NativeBottomScreenClassicLayoutOptions Sanitized()
            => new(
                ClampX(PowerBeamX, Default.PowerBeamX, PowerBeamRadius),
                ClampY(PowerBeamY, Default.PowerBeamY, PowerBeamRadius),
                ClampX(MissileX, Default.MissileX, MissileRadius),
                ClampY(MissileY, Default.MissileY, MissileRadius),
                ClampX(NextWeaponX, Default.NextWeaponX, NextWeaponRadius),
                ClampY(NextWeaponY, Default.NextWeaponY, NextWeaponRadius),
                ClampX(WeaponSelectX, Default.WeaponSelectX, WeaponSelectRadius),
                ClampY(WeaponSelectY, Default.WeaponSelectY, WeaponSelectRadius),
                ClampX(AltFormX, Default.AltFormX, AltFormRadius),
                ClampY(AltFormY, Default.AltFormY, AltFormRadius));

        public NativeBottomScreenClassicLayoutSnapshot CreateLayout()
            => new(this);

        private static float ClampX(float value, float fallback, float radius)
            => float.IsFinite(value)
                ? Math.Clamp(value, radius / 256f, 1 - radius / 256f)
                : fallback;

        private static float ClampY(float value, float fallback, float radius)
            => float.IsFinite(value)
                ? Math.Clamp(value, radius / 192f, 1 - radius / 192f)
                : fallback;
    }

    /// <summary>
    /// Immutable, sanitized Classic DS geometry. The declaration order is
    /// stable and is used for deterministic overlap ties, drawing, exact hit
    /// testing, and directional swipe matching.
    /// </summary>
    public sealed class NativeBottomScreenClassicLayoutSnapshot
    {
        public const float DsWidth = 256;
        public const float DsHeight = 192;
        public const float SwipeDeadzone = 24;
        private const float SwipeCosine = .8191520443f; // cos(35 degrees)

        private readonly NativeBottomScreenButton[] _buttons;
        private readonly IReadOnlyList<NativeBottomScreenButton> _readOnlyButtons;

        internal NativeBottomScreenClassicLayoutSnapshot(
            NativeBottomScreenClassicLayoutOptions options)
        {
            Options = options.Sanitized();
            _buttons = new[]
            {
                new NativeBottomScreenButton(NativeBottomScreenRegion.PowerBeam,
                    new Vector2(Options.PowerBeamX * DsWidth,
                        Options.PowerBeamY * DsHeight),
                    NativeBottomScreenClassicLayoutOptions.PowerBeamRadius, "BEAM"),
                new NativeBottomScreenButton(NativeBottomScreenRegion.Missile,
                    new Vector2(Options.MissileX * DsWidth,
                        Options.MissileY * DsHeight),
                    NativeBottomScreenClassicLayoutOptions.MissileRadius, "MSL"),
                new NativeBottomScreenButton(NativeBottomScreenRegion.NextWeapon,
                    new Vector2(Options.NextWeaponX * DsWidth,
                        Options.NextWeaponY * DsHeight),
                    NativeBottomScreenClassicLayoutOptions.NextWeaponRadius, "WPN"),
                new NativeBottomScreenButton(NativeBottomScreenRegion.WeaponSelect,
                    new Vector2(Options.WeaponSelectX * DsWidth,
                        Options.WeaponSelectY * DsHeight),
                    NativeBottomScreenClassicLayoutOptions.WeaponSelectRadius, "SEL"),
                new NativeBottomScreenButton(NativeBottomScreenRegion.AltForm,
                    new Vector2(Options.AltFormX * DsWidth,
                        Options.AltFormY * DsHeight),
                    NativeBottomScreenClassicLayoutOptions.AltFormRadius, "ALT")
            };
            _readOnlyButtons = Array.AsReadOnly(_buttons);
        }

        public NativeBottomScreenClassicLayoutOptions Options { get; }
        public IReadOnlyList<NativeBottomScreenButton> Buttons => _readOnlyButtons;

        public NativeBottomScreenButton GetButton(NativeBottomScreenRegion region)
            => region switch
            {
                NativeBottomScreenRegion.PowerBeam => _buttons[0],
                NativeBottomScreenRegion.Missile => _buttons[1],
                NativeBottomScreenRegion.NextWeapon => _buttons[2],
                NativeBottomScreenRegion.WeaponSelect => _buttons[3],
                NativeBottomScreenRegion.AltForm => _buttons[4],
                _ => throw new ArgumentOutOfRangeException(nameof(region))
            };

        public NativeBottomScreenRegion RegionAt(Vector2 canonicalPoint)
        {
            if (!float.IsFinite(canonicalPoint.X) || !float.IsFinite(canonicalPoint.Y)
                || canonicalPoint.X < 0 || canonicalPoint.X > DsWidth
                || canonicalPoint.Y < 0 || canonicalPoint.Y > DsHeight)
            {
                return NativeBottomScreenRegion.None;
            }
            NativeBottomScreenRegion nearest = NativeBottomScreenRegion.None;
            float nearestDistance = float.MaxValue;
            foreach (NativeBottomScreenButton button in _buttons)
            {
                Vector2 delta = canonicalPoint - button.Position;
                float distance = delta.LengthSquared;
                if (distance <= button.Radius * button.Radius
                    && distance < nearestDistance)
                {
                    nearest = button.Region;
                    nearestDistance = distance;
                }
            }
            return nearest == NativeBottomScreenRegion.None
                ? NativeBottomScreenRegion.Aim : nearest;
        }

        public NativeBottomScreenRegion ResolveRegion(Vector2 canonicalPoint,
            Vector2 contactStart, bool directionalSwipeAssist)
        {
            NativeBottomScreenRegion exact = RegionAt(canonicalPoint);
            if (!directionalSwipeAssist
                || exact is not (NativeBottomScreenRegion.Aim
                    or NativeBottomScreenRegion.None))
            {
                return exact;
            }

            Vector2 displacement = canonicalPoint - contactStart;
            if (!float.IsFinite(displacement.X)
                || !float.IsFinite(displacement.Y)
                || displacement.LengthSquared <= SwipeDeadzone * SwipeDeadzone)
            {
                return exact;
            }
            Vector2 direction = displacement.Normalized();
            NativeBottomScreenRegion assisted = NativeBottomScreenRegion.None;
            float bestDot = SwipeCosine;
            foreach (NativeBottomScreenButton button in _buttons)
            {
                Vector2 toButton = button.Position - contactStart;
                if (!float.IsFinite(toButton.X) || !float.IsFinite(toButton.Y)
                    || toButton.LengthSquared <= 0.001f) continue;
                float dot = Vector2.Dot(direction, toButton.Normalized());
                // Strict comparison preserves declaration-order ties.
                if (dot > bestDot)
                {
                    bestDot = dot;
                    assisted = button.Region;
                }
            }
            return assisted == NativeBottomScreenRegion.None ? exact : assisted;
        }
    }

    /// <summary>
    /// Compatibility facade for callers that need the shipped Classic DS
    /// geometry. Scene-owned controllers use their immutable snapshot instead.
    /// </summary>
    public static class NativeBottomScreenClassicLayout
    {
        public const float DsWidth = NativeBottomScreenClassicLayoutSnapshot.DsWidth;
        public const float DsHeight = NativeBottomScreenClassicLayoutSnapshot.DsHeight;
        public static NativeBottomScreenClassicLayoutSnapshot Default { get; }
            = NativeBottomScreenClassicLayoutOptions.Default.CreateLayout();
        public static IReadOnlyList<NativeBottomScreenButton> Buttons
            => Default.Buttons;

        public static NativeBottomScreenClassicLayoutSnapshot Create(
            NativeBottomScreenClassicLayoutOptions options)
            => options.CreateLayout();

        public static NativeBottomScreenRegion RegionAt(Vector2 canonicalPoint)
            => Default.RegionAt(canonicalPoint);
    }

    /// <summary>
    /// Normalized centers for the overlay's affinity popup. This is separate
    /// from <see cref="NativeWeaponSelector"/>: the latter remains the native
    /// six-sector selector, while this geometry only belongs to the optional
    /// bottom-screen overlay.
    /// </summary>
    public readonly record struct NativeBottomScreenAffinityLayoutOptions(
        float VoltDriverX, float VoltDriverY,
        float BattlehammerX, float BattlehammerY,
        float ImperialistX, float ImperialistY,
        float JudicatorX, float JudicatorY,
        float MagmaulX, float MagmaulY,
        float ShockCoilX, float ShockCoilY,
        float PowerBeamX, float PowerBeamY,
        float MissileX, float MissileY,
        float AltFormX, float AltFormY)
    {
        public const float AffinityRadius = 16;
        public const float PowerBeamRadius = 16;
        public const float MissileRadius = 16;
        public const float AltFormRadius = 16;

        /// <summary>
        /// The six affinity centers are the native HUD positions. The three
        /// overlay-only controls use a free right-side cluster that does not
        /// overlap those icon footprints at the default layout.
        /// </summary>
        public static NativeBottomScreenAffinityLayoutOptions Default => new(
            201f / 256, 156f / 192,
            161f / 256, 152f / 192,
            122f / 256, 142f / 192,
            90f / 256, 109f / 192,
            81f / 256, 70f / 192,
            77f / 256, 32f / 192,
            230f / 256, 66f / 192,
            230f / 256, 106f / 192,
            190f / 256, 106f / 192);

        public NativeBottomScreenAffinityLayoutOptions Sanitized()
            => new(
                ClampX(VoltDriverX, Default.VoltDriverX),
                ClampY(VoltDriverY, Default.VoltDriverY),
                ClampX(BattlehammerX, Default.BattlehammerX),
                ClampY(BattlehammerY, Default.BattlehammerY),
                ClampX(ImperialistX, Default.ImperialistX),
                ClampY(ImperialistY, Default.ImperialistY),
                ClampX(JudicatorX, Default.JudicatorX),
                ClampY(JudicatorY, Default.JudicatorY),
                ClampX(MagmaulX, Default.MagmaulX),
                ClampY(MagmaulY, Default.MagmaulY),
                ClampX(ShockCoilX, Default.ShockCoilX),
                ClampY(ShockCoilY, Default.ShockCoilY),
                ClampX(PowerBeamX, Default.PowerBeamX),
                ClampY(PowerBeamY, Default.PowerBeamY),
                ClampX(MissileX, Default.MissileX),
                ClampY(MissileY, Default.MissileY),
                ClampX(AltFormX, Default.AltFormX),
                ClampY(AltFormY, Default.AltFormY));

        public NativeBottomScreenAffinityLayoutSnapshot CreateLayout()
            => new(this);

        private static float ClampX(float value, float fallback)
            => float.IsFinite(value)
                ? Math.Clamp(value, AffinityRadius / 256f,
                    1 - AffinityRadius / 256f) : fallback;

        private static float ClampY(float value, float fallback)
            => float.IsFinite(value)
                ? Math.Clamp(value, AffinityRadius / 192f,
                    1 - AffinityRadius / 192f) : fallback;
    }

    /// <summary>
    /// Immutable overlay-only affinity geometry. Declaration order is the
    /// native six-affinity order followed by Power Beam, Missile and Alt Form;
    /// strict nearest-distance comparisons preserve that order on ties.
    /// </summary>
    public sealed class NativeBottomScreenAffinityLayoutSnapshot
    {
        public const float DsWidth = NativeBottomScreenClassicLayoutSnapshot.DsWidth;
        public const float DsHeight = NativeBottomScreenClassicLayoutSnapshot.DsHeight;
        public const float SwipeDeadzone = NativeBottomScreenClassicLayoutSnapshot.SwipeDeadzone;
        private const float SwipeCosine = .8191520443f; // cos(35 degrees)
        private readonly NativeBottomScreenButton[] _buttons;
        private readonly IReadOnlyList<NativeBottomScreenButton> _readOnlyButtons;

        internal NativeBottomScreenAffinityLayoutSnapshot(
            NativeBottomScreenAffinityLayoutOptions options)
        {
            Options = options.Sanitized();
            _buttons = new[]
            {
                Button(NativeBottomScreenRegion.VoltDriver,
                    Options.VoltDriverX, Options.VoltDriverY, "VD"),
                Button(NativeBottomScreenRegion.Battlehammer,
                    Options.BattlehammerX, Options.BattlehammerY, "BH"),
                Button(NativeBottomScreenRegion.Imperialist,
                    Options.ImperialistX, Options.ImperialistY, "IMP"),
                Button(NativeBottomScreenRegion.Judicator,
                    Options.JudicatorX, Options.JudicatorY, "JUD"),
                Button(NativeBottomScreenRegion.Magmaul,
                    Options.MagmaulX, Options.MagmaulY, "MAG"),
                Button(NativeBottomScreenRegion.ShockCoil,
                    Options.ShockCoilX, Options.ShockCoilY, "COIL"),
                Button(NativeBottomScreenRegion.PowerBeam,
                    Options.PowerBeamX, Options.PowerBeamY, "BEAM"),
                Button(NativeBottomScreenRegion.Missile,
                    Options.MissileX, Options.MissileY, "MSL"),
                Button(NativeBottomScreenRegion.AltForm,
                    Options.AltFormX, Options.AltFormY, "ALT")
            };
            _readOnlyButtons = Array.AsReadOnly(_buttons);
        }

        public NativeBottomScreenAffinityLayoutOptions Options { get; }
        public IReadOnlyList<NativeBottomScreenButton> Buttons => _readOnlyButtons;

        public NativeBottomScreenButton GetButton(NativeBottomScreenRegion region)
            => region switch
            {
                NativeBottomScreenRegion.VoltDriver => _buttons[0],
                NativeBottomScreenRegion.Battlehammer => _buttons[1],
                NativeBottomScreenRegion.Imperialist => _buttons[2],
                NativeBottomScreenRegion.Judicator => _buttons[3],
                NativeBottomScreenRegion.Magmaul => _buttons[4],
                NativeBottomScreenRegion.ShockCoil => _buttons[5],
                NativeBottomScreenRegion.PowerBeam => _buttons[6],
                NativeBottomScreenRegion.Missile => _buttons[7],
                NativeBottomScreenRegion.AltForm => _buttons[8],
                _ => throw new ArgumentOutOfRangeException(nameof(region))
            };

        public NativeBottomScreenRegion RegionAt(Vector2 canonicalPoint)
        {
            if (!float.IsFinite(canonicalPoint.X) || !float.IsFinite(canonicalPoint.Y)
                || canonicalPoint.X < 0 || canonicalPoint.X > DsWidth
                || canonicalPoint.Y < 0 || canonicalPoint.Y > DsHeight)
            {
                return NativeBottomScreenRegion.None;
            }
            NativeBottomScreenRegion nearest = NativeBottomScreenRegion.None;
            float nearestDistance = float.MaxValue;
            foreach (NativeBottomScreenButton button in _buttons)
            {
                Vector2 delta = canonicalPoint - button.Position;
                float distance = delta.LengthSquared;
                if (distance <= button.Radius * button.Radius
                    && distance < nearestDistance)
                {
                    nearest = button.Region;
                    nearestDistance = distance;
                }
            }
            return nearest == NativeBottomScreenRegion.None
                ? NativeBottomScreenRegion.Aim : nearest;
        }

        public NativeBottomScreenRegion ResolveRegion(Vector2 canonicalPoint,
            Vector2 contactStart, bool directionalSwipeAssist)
        {
            NativeBottomScreenRegion exact = RegionAt(canonicalPoint);
            if (!directionalSwipeAssist
                || exact is not (NativeBottomScreenRegion.Aim
                    or NativeBottomScreenRegion.None))
            {
                return exact;
            }
            Vector2 displacement = canonicalPoint - contactStart;
            if (!float.IsFinite(displacement.X)
                || !float.IsFinite(displacement.Y)
                || displacement.LengthSquared <= SwipeDeadzone * SwipeDeadzone)
            {
                return exact;
            }
            Vector2 direction = displacement.Normalized();
            NativeBottomScreenRegion assisted = NativeBottomScreenRegion.None;
            float bestDot = SwipeCosine;
            foreach (NativeBottomScreenButton button in _buttons)
            {
                Vector2 toButton = button.Position - contactStart;
                if (!float.IsFinite(toButton.X) || !float.IsFinite(toButton.Y)
                    || toButton.LengthSquared <= 0.001f) continue;
                float dot = Vector2.Dot(direction, toButton.Normalized());
                if (dot > bestDot)
                {
                    bestDot = dot;
                    assisted = button.Region;
                }
            }
            return assisted == NativeBottomScreenRegion.None ? exact : assisted;
        }

        private static NativeBottomScreenButton Button(
            NativeBottomScreenRegion region, float x, float y, string label)
            => new(region, new Vector2(x * DsWidth, y * DsHeight),
                NativeBottomScreenAffinityLayoutOptions.AffinityRadius, label);
    }

    /// <summary>Normalized placement and size for the lower-screen panel.</summary>
    public readonly record struct NativeBottomScreenLayoutOptions(
        float Scale, float CenterX, float CenterY)
    {
        public static NativeBottomScreenLayoutOptions Default => new(1, .5f, .685f);

        public NativeBottomScreenLayoutOptions Sanitized()
            => new(
                float.IsFinite(Scale) ? Math.Clamp(Scale, .4f, 1) : 1,
                float.IsFinite(CenterX) ? Math.Clamp(CenterX, 0, 1) : .5f,
                float.IsFinite(CenterY) ? Math.Clamp(CenterY, 0, 1) : .685f);
    }

    /// <summary>
    /// Desktop virtual-cursor preferences. Start coordinates are normalized
    /// DS-panel coordinates so they remain stable when the window or panel
    /// geometry changes.
    /// </summary>
    public readonly record struct NativeBottomScreenCursorOptions(
        float Sensitivity, float StartX, float StartY)
    {
        public const float MinimumSensitivity = .1f;
        public const float MaximumSensitivity = 4f;

        public static NativeBottomScreenCursorOptions Default => new(1, .5f, .5f);

        public NativeBottomScreenCursorOptions Sanitized()
            => new(
                float.IsFinite(Sensitivity)
                    ? Math.Clamp(Sensitivity, MinimumSensitivity, MaximumSensitivity)
                    : 1,
                float.IsFinite(StartX) ? Math.Clamp(StartX, 0, 1) : .5f,
                float.IsFinite(StartY) ? Math.Clamp(StartY, 0, 1) : .5f);
    }

    /// <summary>A rectangle in the coordinate space of a pointer surface.</summary>
    public readonly record struct BottomScreenRect(float Left, float Top,
        float Right, float Bottom)
    {
        public float Width => MathF.Max(0, Right - Left);
        public float Height => MathF.Max(0, Bottom - Top);

        public bool Contains(float x, float y)
            => x >= Left && x <= Right && y >= Top && y <= Bottom;

        public BottomScreenRect Scale(float x, float y)
            => new(Left * x, Top * y, Right * x, Bottom * y);
    }

    /// <summary>
    /// Immutable logical/framebuffer geometry for the lower-screen panel. The
    /// panel remains 4:3 at every window aspect ratio.
    /// </summary>
    public readonly record struct NativeBottomScreenLayout(
        Vector2i LogicalSize, Vector2i FramebufferSize,
        BottomScreenRect PanelLogical, BottomScreenRect PanelFramebuffer)
    {
        public bool IsValid => PanelLogical.Width > 0 && PanelLogical.Height > 0
            && PanelFramebuffer.Width > 0 && PanelFramebuffer.Height > 0;

        public bool ContainsPanel(float x, float y) => PanelLogical.Contains(x, y);

        public Vector2 LogicalToDs(float x, float y)
        {
            if (!IsValid) return new Vector2(float.NaN, float.NaN);
            return new(
                (x - PanelLogical.Left) * 256f / PanelLogical.Width,
                (y - PanelLogical.Top) * 192f / PanelLogical.Height);
        }

        public Vector2 DsToLogical(float x, float y)
        {
            if (!IsValid) return new Vector2(float.NaN, float.NaN);
            return new(
                PanelLogical.Left + x * PanelLogical.Width / 256f,
                PanelLogical.Top + y * PanelLogical.Height / 192f);
        }

        public Vector2 DsToFramebuffer(float x, float y)
        {
            if (!IsValid) return new Vector2(float.NaN, float.NaN);
            return new(
                PanelFramebuffer.Left + x * PanelFramebuffer.Width / 256f,
                PanelFramebuffer.Top + y * PanelFramebuffer.Height / 192f);
        }

        public static NativeBottomScreenLayout Compute(Vector2i logicalSize,
            Vector2i framebufferSize)
            => Compute(logicalSize, framebufferSize,
                NativeBottomScreenLayoutOptions.Default);

        public static NativeBottomScreenLayout Compute(Vector2i logicalSize,
            Vector2i framebufferSize, NativeBottomScreenLayoutOptions options)
        {
            int logicalWidth = Math.Max(1, logicalSize.X);
            int logicalHeight = Math.Max(1, logicalSize.Y);
            int framebufferWidth = Math.Max(1, framebufferSize.X);
            int framebufferHeight = Math.Max(1, framebufferSize.Y);
            options = options.Sanitized();

            // Bound the panel by both width and height, then derive height
            // from width so no aspect-ratio stretch is introduced.
            float panelWidth = MathF.Min(logicalWidth * .72f,
                logicalHeight * .58f * (4f / 3f));
            panelWidth = MathF.Max(1, MathF.Min(panelWidth * options.Scale,
                logicalWidth));
            float panelHeight = panelWidth * .75f;
            float margin = MathF.Max(8, logicalHeight * .025f);
            float panelLeft = Place(options.CenterX * logicalWidth - panelWidth * .5f,
                panelWidth, logicalWidth, margin);
            float panelTop = Place(options.CenterY * logicalHeight - panelHeight * .5f,
                panelHeight, logicalHeight, margin);
            var panelLogical = new BottomScreenRect(panelLeft, panelTop,
                panelLeft + panelWidth, panelTop + panelHeight);

            float framebufferScaleX = framebufferWidth / (float)logicalWidth;
            float framebufferScaleY = framebufferHeight / (float)logicalHeight;
            return new NativeBottomScreenLayout(logicalSize, framebufferSize,
                panelLogical, panelLogical.Scale(framebufferScaleX, framebufferScaleY));
        }

        private static float Place(float requested, float extent, float available,
            float margin)
        {
            float minimum = MathF.Min(margin, MathF.Max(0, (available - extent) * .5f));
            float maximum = MathF.Max(minimum, available - extent - minimum);
            return Math.Clamp(requested, minimum, maximum);
        }
    }

    /// <summary>Exact six-affinity selector extracted from the native HUD path.</summary>
    public static class NativeWeaponSelector
    {
        private static readonly byte[] AffinityOrder = { 1, 3, 4, 5, 6, 7 };
        private static readonly Vector2[] AffinityPositions =
        {
            new(201, 156), new(161, 152), new(122, 142),
            new(90, 109), new(81, 70), new(77, 32)
        };

        public static int SlotCount => AffinityOrder.Length;

        public static byte WeaponAtSlot(int slot)
            => slot is >= 0 and < 6 ? AffinityOrder[slot]
                : throw new ArgumentOutOfRangeException(nameof(slot));

        public static Vector2 SlotPosition(int slot)
            => slot is >= 0 and < 6 ? AffinityPositions[slot]
                : throw new ArgumentOutOfRangeException(nameof(slot));

        public static int SlotOfWeapon(byte weapon)
        {
            for (int i = 0; i < AffinityOrder.Length; i++)
            {
                if (AffinityOrder[i] == weapon) return i;
            }
            return -1;
        }

        /// <summary>
        /// Uses the same 256x192 coordinates, deadzone, and fixed-point slope
        /// constants as PresentationPlayerHud.UpdateWeaponSelect.
        /// </summary>
        public static bool TrySelect(Vector2 canonicalPoint, int availableMask,
            out byte weapon)
        {
            return TrySelect(canonicalPoint, 1, 1, availableMask, out weapon);
        }

        public static bool TrySelect(Vector2 point, float ratioX, float ratioY,
            int availableMask, out byte weapon)
        {
            weapon = WeaponSelectionIntent.None;
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)
                || !float.IsFinite(ratioX) || !float.IsFinite(ratioY)
                || ratioX <= 0 || ratioY <= 0)
            {
                return false;
            }

            float distX = 224 * ratioX - point.X;
            float distY = point.Y - 38 * ratioY;
            if (distX <= 0 || distY <= 0
                || distX * distX + distY * distY
                    <= 20 * ratioY * 20 * ratioY)
            {
                return false;
            }

            float div = distX / distY;
            float scale = ratioX / ratioY;
            int slot;
            if (div >= 1060f / 3956f * scale)
            {
                if (div >= 2048f / 3547f * scale)
                {
                    if (div >= 2896f / 2896f * scale)
                    {
                        if (div >= 3547f / 2048f * scale)
                        {
                            slot = div >= 3956f / 1060f * scale ? 5 : 4;
                        }
                        else
                        {
                            slot = 3;
                        }
                    }
                    else
                    {
                        slot = 2;
                    }
                }
                else
                {
                    slot = 1;
                }
            }
            else
            {
                slot = 0;
            }

            byte candidate = AffinityOrder[slot];
            if ((availableMask & (1 << candidate)) == 0) return false;
            weapon = candidate;
            return true;
        }
    }

    public enum NativeBottomScreenPointerPhase
    {
        Down,
        Move,
        Up,
        Cancel
    }

    public readonly record struct NativeBottomScreenPointerEvent(
        NativeBottomScreenPointerPhase Phase, PointerSample Sample, long Generation);

    /// <summary>
    /// Scene-owned bottom-screen state. Platform threads submit neutral pointer
    /// samples; the scene thread consumes immutable events and decides whether
    /// a weapon intent is legal.
    /// </summary>
    public sealed partial class NativeBottomScreenController
    {
        private const int QueueCapacity = 128;
        public const long DesktopPointerId = int.MinValue + 1L;
        private readonly object _sync = new();
        private readonly Queue<NativeBottomScreenPointerEvent> _events = new();
        private readonly HashSet<long> _queuedPointers = new();
        private NativeBottomScreenMode _mode;
        private NativeBottomScreenStyle _style = NativeBottomScreenStyle.AffinitySelector;
        private NativeBottomScreenAimMode _aimMode;
        private NativeBottomScreenLayoutOptions _layoutOptions
            = NativeBottomScreenLayoutOptions.Default;
        private NativeBottomScreenClassicLayoutOptions _classicLayoutOptions
            = NativeBottomScreenClassicLayoutOptions.Default;
        private NativeBottomScreenClassicLayoutSnapshot _classicLayout
            = NativeBottomScreenClassicLayoutOptions.Default.CreateLayout();
        private NativeBottomScreenAffinityLayoutOptions _affinityLayoutOptions
            = NativeBottomScreenAffinityLayoutOptions.Default;
        private NativeBottomScreenAffinityLayoutSnapshot _affinityLayout
            = NativeBottomScreenAffinityLayoutOptions.Default.CreateLayout();
        private NativeBottomScreenLayout _layout;
        private long _generation;
        private long _interactionEpoch;
        private long _platformToken;
        private long? _capturedPointer;
        private NativeBottomScreenPointerRoute _capturedRoute;
        private NativeBottomScreenAimContactSnapshot _aimContact
            = new(false, Vector2.Zero);
        private bool _popupOpen;
        private bool _selectorOpen;
        private bool _desktopSessionActive;
        private NativeBottomScreenActivationMode _desktopActivationMode
            = NativeBottomScreenActivationMode.Toggle;
        private NativeBottomScreenCursorOptions _desktopCursorOptions
            = NativeBottomScreenCursorOptions.Default;
        private Vector2 _desktopCursorDs = new(128, 96);

        public NativeBottomScreenMode Mode { get { lock (_sync) return _mode; } }
        public NativeBottomScreenStyle Style { get { lock (_sync) return _style; } }
        public NativeBottomScreenAimMode AimMode { get { lock (_sync) return _aimMode; } }
        public long InteractionEpoch { get { lock (_sync) return _interactionEpoch; } }
        public NativeBottomScreenClassicLayoutSnapshot ClassicLayout
        {
            get { lock (_sync) return _classicLayout; }
        }
        public NativeBottomScreenAffinityLayoutSnapshot AffinityLayout
        {
            get { lock (_sync) return _affinityLayout; }
        }
        public NativeBottomScreenLayout Layout { get { lock (_sync) return _layout; } }
        public NativeBottomScreenAimContactSnapshot AimContact
        {
            get { lock (_sync) return _aimContact; }
        }
        public long Generation { get { lock (_sync) return _generation; } }
        public bool PopupOpen { get { lock (_sync) return _popupOpen; } }
        public bool SelectorOpen { get { lock (_sync) return _selectorOpen; } }
        public bool DesktopSessionActive
        {
            get { lock (_sync) return _desktopSessionActive; }
        }
        public NativeBottomScreenActivationMode DesktopActivationMode
        {
            get { lock (_sync) return _desktopActivationMode; }
        }
        public bool DesktopHoldContactActive
        {
            get
            {
                lock (_sync)
                {
                    return _desktopSessionActive
                        && _desktopActivationMode == NativeBottomScreenActivationMode.Hold;
                }
            }
        }
        public NativeBottomScreenCursorOptions DesktopCursorOptions
        {
            get { lock (_sync) return _desktopCursorOptions; }
        }
        public Vector2 DesktopCursorDs
        {
            get { lock (_sync) return _desktopCursorDs; }
        }
        public bool Visible
        {
            get
            {
                lock (_sync)
                    return _mode == NativeBottomScreenMode.AlwaysVisible || _popupOpen;
            }
        }

        /// <summary>
        /// True DS owns pen contacts while the visible Classic DS panel is
        /// active. The host uses this only to prevent an outside-panel pen
        /// contact from falling through to global stylus aiming.
        /// </summary>
        public bool SuppressGlobalStylus
        {
            get
            {
                lock (_sync)
                {
                    return _aimMode == NativeBottomScreenAimMode.TrueDs
                        && _style == NativeBottomScreenStyle.ClassicDs
                        && _mode != NativeBottomScreenMode.Off
                        && (_mode == NativeBottomScreenMode.AlwaysVisible || _popupOpen);
                }
            }
        }

        public bool TryGetVisiblePanel(out BottomScreenRect panel)
        {
            lock (_sync)
            {
                bool visible = _layout.IsValid
                    && _mode != NativeBottomScreenMode.Off
                    && (_mode == NativeBottomScreenMode.AlwaysVisible || _popupOpen);
                panel = _layout.PanelLogical;
                return visible;
            }
        }

        public void Configure(Vector2i logicalSize, Vector2i framebufferSize,
            NativeBottomScreenMode mode,
            NativeBottomScreenAimMode aimMode = NativeBottomScreenAimMode.Free)
        {
            if (!Enum.IsDefined(mode)) mode = NativeBottomScreenMode.Off;
            if (!Enum.IsDefined(aimMode)) aimMode = NativeBottomScreenAimMode.Free;
            lock (_sync)
            {
                NativeBottomScreenLayout layout = NativeBottomScreenLayout.Compute(
                    logicalSize, framebufferSize, _layoutOptions);
                if (mode == _mode && aimMode == _aimMode && layout == _layout) return;
                CancelQueuedLocked();
                _mode = mode;
                _aimMode = aimMode;
                _layout = layout;
                _popupOpen = mode == NativeBottomScreenMode.AlwaysVisible;
                _selectorOpen = false;
                _desktopSessionActive = false;
            }
        }

        public void UpdatePreferences(NativeBottomScreenMode mode,
            NativeBottomScreenStyle style, NativeBottomScreenLayoutOptions options,
            NativeBottomScreenClassicLayoutOptions? classicOptions = null,
            NativeBottomScreenAffinityLayoutOptions? affinityOptions = null,
            NativeBottomScreenAimMode aimMode = NativeBottomScreenAimMode.Free)
        {
            if (!Enum.IsDefined(mode)) mode = NativeBottomScreenMode.Off;
            if (!Enum.IsDefined(style)) style = NativeBottomScreenStyle.ClassicDs;
            if (!Enum.IsDefined(aimMode)) aimMode = NativeBottomScreenAimMode.Free;
            options = options.Sanitized();
            lock (_sync)
            {
                NativeBottomScreenClassicLayoutOptions sanitizedClassic
                    = (classicOptions ?? _classicLayoutOptions).Sanitized();
                NativeBottomScreenAffinityLayoutOptions sanitizedAffinity
                    = (affinityOptions ?? _affinityLayoutOptions).Sanitized();
                NativeBottomScreenLayout layout = NativeBottomScreenLayout.Compute(
                    _layout.LogicalSize, _layout.FramebufferSize, options);
                if (mode == _mode && style == _style && aimMode == _aimMode
                    && options == _layoutOptions
                    && layout == _layout && sanitizedClassic == _classicLayoutOptions
                    && sanitizedAffinity == _affinityLayoutOptions)
                {
                    return;
                }
                CancelQueuedLocked();
                _mode = mode;
                _style = style;
                _aimMode = aimMode;
                _layoutOptions = options;
                _classicLayoutOptions = sanitizedClassic;
                _classicLayout = sanitizedClassic.CreateLayout();
                _affinityLayoutOptions = sanitizedAffinity;
                _affinityLayout = sanitizedAffinity.CreateLayout();
                _layout = layout;
                _popupOpen = mode == NativeBottomScreenMode.AlwaysVisible;
                _selectorOpen = false;
                _desktopSessionActive = false;
            }
        }

        /// <summary>
        /// Updates cursor preferences without moving an active contact. A
        /// change during a session cancels that session so a layout/settings
        /// edit cannot commit against a stale start or sensitivity.
        /// </summary>
        public void UpdateDesktopCursorPreferences(float sensitivity,
            float startX, float startY)
            => UpdateDesktopCursorPreferences(0, 0, sensitivity, startX, startY);

        internal void UpdateDesktopCursorPreferences(long token, long generation,
            float sensitivity, float startX, float startY)
        {
            NativeBottomScreenCursorOptions options
                = new NativeBottomScreenCursorOptions(sensitivity, startX, startY)
                    .Sanitized();
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return;
                if (_desktopCursorOptions == options) return;
                CancelQueuedLocked();
                _desktopCursorOptions = options;
                _desktopSessionActive = false;
                _selectorOpen = false;
                _desktopCursorDs = StartCursorLocked();
            }
        }

        public void SetSelectorOpen(bool open)
        {
            lock (_sync)
            {
                open &= _style == NativeBottomScreenStyle.ClassicDs
                    && _mode != NativeBottomScreenMode.Off;
                if (_selectorOpen == open) return;
                CancelQueuedLocked();
                _selectorOpen = open;
            }
        }

        public long BeginPresentation()
        {
            lock (_sync)
            {
                _generation++;
                _events.Clear();
                _queuedPointers.Clear();
                _capturedPointer = null;
                _capturedRoute = NativeBottomScreenPointerRoute.None;
                _aimContact = new NativeBottomScreenAimContactSnapshot(false,
                    Vector2.Zero);
                _popupOpen = _mode == NativeBottomScreenMode.AlwaysVisible;
                _selectorOpen = false;
                _desktopSessionActive = false;
                _desktopActivationMode = NativeBottomScreenActivationMode.Toggle;
                _desktopCursorDs = StartCursorLocked();
                return _generation;
            }
        }

        public void EndPresentation(long generation)
        {
            lock (_sync)
            {
                if (generation != _generation) return;
                CancelQueuedLocked();
                _popupOpen = false;
                _selectorOpen = false;
                _desktopSessionActive = false;
            }
        }

        public bool TogglePopup()
        {
            lock (_sync)
            {
                if (_mode != NativeBottomScreenMode.Popup) return false;
                if (_popupOpen)
                {
                    CancelQueuedLocked();
                    _selectorOpen = false;
                    _desktopSessionActive = false;
                }
                _popupOpen = !_popupOpen;
                return _popupOpen;
            }
        }

        /// <summary>Dismiss an explicitly opened one-selection popup.</summary>
        public void DismissPopupAfterSelection()
        {
            lock (_sync)
            {
                _selectorOpen = false;
                if (_mode == NativeBottomScreenMode.Popup)
                {
                    _popupOpen = false;
                }
            }
        }

        /// <summary>
        /// Enter or toggle the scene-owned desktop cursor session. The
        /// activation mode is supplied by the settings layer; no platform
        /// state is retained outside this controller.
        /// </summary>
        public bool BeginDesktopSession(NativeBottomScreenActivationMode mode)
            => BeginDesktopSession(0, 0, mode);

        internal bool BeginDesktopSession(long token, long generation,
            NativeBottomScreenActivationMode mode)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)
                    || _mode == NativeBottomScreenMode.Off
                    || !Enum.IsDefined(mode)) return false;
                if (_desktopSessionActive)
                {
                    if (mode != NativeBottomScreenActivationMode.Toggle) return true;
                    EndDesktopSessionLocked();
                    return true;
                }
                CancelQueuedLocked();
                _desktopSessionActive = true;
                _desktopActivationMode = mode;
                _desktopCursorDs = StartCursorLocked();
                if (_mode == NativeBottomScreenMode.Popup) _popupOpen = true;
                if (mode == NativeBottomScreenActivationMode.Hold)
                {
                    _capturedPointer = DesktopPointerId;
                    _queuedPointers.Add(DesktopPointerId);
                    EnqueueLocked(NativeBottomScreenPointerPhase.Down,
                        DesktopSampleLocked());
                }
                return true;
            }
        }

        /// <summary>Leave the cursor session without committing its contact.</summary>
        public bool EndDesktopSession() => EndDesktopSession(0, 0);

        internal bool EndDesktopSession(long token, long generation)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return false;
                return EndDesktopSessionLocked();
            }
        }

        private bool EndDesktopSessionLocked()
        {
            if (!_desktopSessionActive) return false;
            CancelQueuedLocked();
            _desktopSessionActive = false;
            _selectorOpen = false;
            if (_mode == NativeBottomScreenMode.Popup) _popupOpen = false;
            return true;
        }

        /// <summary>
        /// A legal selection closes a Toggle session. A Hold session remains
        /// focused until its activation binding is released.
        /// </summary>
        public bool CompleteDesktopSelection()
            => CompleteDesktopSelection(0, 0);

        internal bool CompleteDesktopSelection(long token, long generation)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)
                    || !_desktopSessionActive) return false;
                if (_desktopActivationMode == NativeBottomScreenActivationMode.Toggle)
                    return EndDesktopSessionLocked();
                _selectorOpen = false;
                return true;
            }
        }

        /// <summary>Move the virtual cursor in logical-window pixels.</summary>
        public bool TryDesktopCursorMove(Vector2 delta)
            => TryDesktopCursorMove(0, 0, delta);

        internal bool TryDesktopCursorMove(long token, long generation,
            Vector2 delta)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)
                    || !_desktopSessionActive || !Finite(delta.X, delta.Y)
                    || !_layout.IsValid) return false;
                if (delta.LengthSquared <= 0) return true;
                float sensitivity = _desktopCursorOptions.Sensitivity;
                float dx = delta.X * sensitivity * 256f
                    / _layout.PanelLogical.Width;
                float dy = delta.Y * sensitivity * 192f
                    / _layout.PanelLogical.Height;
                _desktopCursorDs = new Vector2(
                    Math.Clamp(_desktopCursorDs.X + dx, 0, 256),
                    Math.Clamp(_desktopCursorDs.Y + dy, 0, 192));
                if (_capturedPointer == DesktopPointerId)
                {
                    EnqueueLocked(NativeBottomScreenPointerPhase.Move,
                        DesktopSampleLocked());
                }
                return true;
            }
        }

        public bool TryDesktopPointerDown() => TryDesktopPointerDown(0, 0);

        internal bool TryDesktopPointerDown(long token, long generation)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)
                    || !_desktopSessionActive || _mode == NativeBottomScreenMode.Off
                    || !_layout.IsValid
                    || _desktopActivationMode == NativeBottomScreenActivationMode.Hold
                    || _capturedPointer.HasValue
                        && _capturedPointer.Value != DesktopPointerId)
                    return false;
                PointerSample sample = DesktopSampleLocked();
                _capturedPointer = DesktopPointerId;
                _queuedPointers.Add(DesktopPointerId);
                EnqueueLocked(NativeBottomScreenPointerPhase.Down, sample);
                return true;
            }
        }

        public bool TryDesktopPointerUp() => TryDesktopPointerUp(0, 0);

        internal bool TryDesktopPointerUp(long token, long generation)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)
                    || _capturedPointer != DesktopPointerId) return false;
                EnqueueLocked(NativeBottomScreenPointerPhase.Up,
                    DesktopSampleLocked());
                _capturedPointer = null;
                return true;
            }
        }

        /// <summary>
        /// Opens the nested affinity selector while a Hold contact is still
        /// down. It deliberately preserves the synthetic contact and queued
        /// history so the eventual binding release can select an affinity.
        /// </summary>
        public bool OpenSelectorForDesktopDrag()
            => OpenSelectorForDesktopDrag(0, 0);

        internal bool OpenSelectorForDesktopDrag(long token, long generation)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)
                    || !_desktopSessionActive
                    || _desktopActivationMode != NativeBottomScreenActivationMode.Hold
                    || _style != NativeBottomScreenStyle.ClassicDs
                    || _mode == NativeBottomScreenMode.Off)
                {
                    return false;
                }
                _selectorOpen = true;
                return true;
            }
        }

        private PointerSample DesktopSampleLocked()
        {
            Vector2 logical = _layout.DsToLogical(_desktopCursorDs.X,
                _desktopCursorDs.Y);
            return new PointerSample(DesktopPointerId, PointerToolKind.Mouse,
                logical.X, logical.Y, 1, StylusButtons.None,
                Environment.TickCount64);
        }

        private Vector2 StartCursorLocked()
            => new(_desktopCursorOptions.StartX * NativeBottomScreenClassicLayout.DsWidth,
                _desktopCursorOptions.StartY * NativeBottomScreenClassicLayout.DsHeight);

        public bool TryPointerDown(in PointerSample sample)
            => RoutePointerDown(0, 0, sample) is NativeBottomScreenPointerRoute.Control
                or NativeBottomScreenPointerRoute.Aim;

        public NativeBottomScreenPointerRoute RoutePointerDown(in PointerSample sample)
            => RoutePointerDown(0, 0, sample);

        public bool TryPointerMove(in PointerSample sample)
            => TryPointerMove(0, 0, sample);

        public bool TryPointerUp(in PointerSample sample)
            => TryPointerUp(0, 0, sample);

        public bool CancelPointer(long pointerId)
            => CancelPointer(0, 0, pointerId);

        public void Cancel() => Cancel(0, 0);

        public IReadOnlyList<NativeBottomScreenPointerEvent> Consume(long generation)
        {
            lock (_sync)
            {
                if (generation != _generation || _events.Count == 0)
                    return Array.Empty<NativeBottomScreenPointerEvent>();
                var result = new List<NativeBottomScreenPointerEvent>(_events.Count);
                while (_events.Count > 0)
                {
                    NativeBottomScreenPointerEvent item = _events.Dequeue();
                    if (item.Generation == generation) result.Add(item);
                }
                _queuedPointers.Clear();
                return result;
            }
        }

        internal void AttachPlatformToken(long token)
        {
            lock (_sync) _platformToken = token;
        }

        internal void DetachPlatformToken(long token)
        {
            lock (_sync)
            {
                if (_platformToken == token) _platformToken = 0;
            }
        }

        internal NativeBottomScreenPointerRoute RoutePointerDown(long token,
            long generation, in PointerSample sample)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return NativeBottomScreenPointerRoute.None;
                if (_mode == NativeBottomScreenMode.Off
                    || !Finite(sample.X, sample.Y))
                    return NativeBottomScreenPointerRoute.None;
                if (_capturedPointer == sample.Id) return _capturedRoute;
                if (_capturedPointer.HasValue)
                {
                    // The first contact owns the native panel. Keep later
                    // contacts out of both the queue and the global stylus
                    // path until the owner lifts/cancels.
                    return _layout.ContainsPanel(sample.X, sample.Y)
                        ? NativeBottomScreenPointerRoute.Suppressed
                        : NativeBottomScreenPointerRoute.None;
                }
                if (_mode == NativeBottomScreenMode.Popup && !_popupOpen) return NativeBottomScreenPointerRoute.None;
                if (!_layout.ContainsPanel(sample.X, sample.Y)) return NativeBottomScreenPointerRoute.None;
                if (_style == NativeBottomScreenStyle.ClassicDs && !_selectorOpen)
                {
                    Vector2 canonical = _layout.LogicalToDs(sample.X, sample.Y);
                    NativeBottomScreenRegion region
                        = _classicLayout.RegionAt(canonical);
                    if (region is NativeBottomScreenRegion.None)
                    {
                        return NativeBottomScreenPointerRoute.None;
                    }
                    if (region is NativeBottomScreenRegion.Aim)
                    {
                        if (_aimMode != NativeBottomScreenAimMode.TrueDs)
                            return NativeBottomScreenPointerRoute.None;
                        _capturedPointer = sample.Id;
                        _capturedRoute = NativeBottomScreenPointerRoute.Aim;
                        bool aimContact = false;
                        Vector2 aimPosition = Vector2.Zero;
                        if (IsAimContactTool(sample.Tool)
                            && TryMapAimSampleLocked(sample,
                                out PointerSample mapped))
                        {
                            aimContact = true;
                            aimPosition = new Vector2(mapped.X, mapped.Y);
                        }
                        _aimContact = new NativeBottomScreenAimContactSnapshot(
                            aimContact, aimPosition);
                        return _capturedRoute;
                    }
                }
                _aimContact = new NativeBottomScreenAimContactSnapshot(false,
                    Vector2.Zero);
                _capturedPointer = sample.Id;
                _capturedRoute = NativeBottomScreenPointerRoute.Control;
                _queuedPointers.Add(sample.Id);
                EnqueueLocked(NativeBottomScreenPointerPhase.Down, sample);
                return _capturedRoute;
            }
        }

        internal PointerSample MapAimSample(long token, long generation,
            in PointerSample sample)
        {
            lock (_sync)
            {
                return !ValidTokenLocked(token, generation)
                    || !TryMapAimSampleLocked(sample,
                        out PointerSample mapped) ? sample : mapped;
            }
        }

        public PointerSample MapAimSample(in PointerSample sample)
            => MapAimSample(0, 0, sample);

        internal bool TryPointerDown(long token, long generation,
            in PointerSample sample)
            => RoutePointerDown(token, generation, sample)
                is NativeBottomScreenPointerRoute.Control
                    or NativeBottomScreenPointerRoute.Aim;

        internal bool TryPointerMove(long token, long generation,
            in PointerSample sample)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return false;
                if (_capturedPointer != sample.Id) return false;
                if (_capturedRoute == NativeBottomScreenPointerRoute.Aim)
                {
                    // Eligibility is fixed by the down sample. Do not let a
                    // later tool label reclassify a contact that was not
                    // eligible for the True DS aim snapshot.
                    if (_aimContact.Active
                        && TryMapAimSampleLocked(sample,
                            out PointerSample mapped))
                    {
                        _aimContact = new NativeBottomScreenAimContactSnapshot(
                            true, new Vector2(mapped.X, mapped.Y));
                    }
                    return true;
                }
                EnqueueLocked(NativeBottomScreenPointerPhase.Move, sample);
                return true;
            }
        }

        internal bool TryPointerUp(long token, long generation,
            in PointerSample sample)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return false;
                if (_capturedPointer != sample.Id) return false;
                if (_capturedRoute == NativeBottomScreenPointerRoute.Aim)
                {
                    _aimContact = new NativeBottomScreenAimContactSnapshot(false,
                        Vector2.Zero);
                    _capturedPointer = null;
                    _capturedRoute = NativeBottomScreenPointerRoute.None;
                    return true;
                }
                EnqueueLocked(NativeBottomScreenPointerPhase.Up, sample);
                _capturedPointer = null;
                _capturedRoute = NativeBottomScreenPointerRoute.None;
                return true;
            }
        }

        internal bool CancelPointer(long token, long generation, long pointerId)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return false;
                bool matched = _capturedPointer == pointerId
                    || _queuedPointers.Contains(pointerId);
                if (!matched) return false;
                if (_capturedPointer == pointerId
                    && _capturedRoute == NativeBottomScreenPointerRoute.Aim)
                {
                    _aimContact = new NativeBottomScreenAimContactSnapshot(false,
                        Vector2.Zero);
                    _capturedPointer = null;
                    _capturedRoute = NativeBottomScreenPointerRoute.None;
                    _interactionEpoch++;
                    return true;
                }
                QueueCancelLocked(pointerId);
                _capturedPointer = null;
                _capturedRoute = NativeBottomScreenPointerRoute.None;
                _queuedPointers.Remove(pointerId);
                _interactionEpoch++;
                return true;
            }
        }

        internal void Cancel(long token, long generation)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return;
                CancelQueuedLocked();
                _popupOpen = _mode == NativeBottomScreenMode.AlwaysVisible;
                _selectorOpen = false;
                _desktopSessionActive = false;
                _desktopCursorDs = StartCursorLocked();
            }
        }

        private bool ValidTokenLocked(long token, long generation)
            => (token == 0 || token == _platformToken)
                && (generation == 0 || generation == _generation);

        private static bool Finite(float x, float y)
            => float.IsFinite(x) && float.IsFinite(y);

        private void CancelQueuedLocked()
        {
            var ids = new HashSet<long>(_queuedPointers);
            if (_capturedPointer.HasValue) ids.Add(_capturedPointer.Value);
            foreach (long id in ids) QueueCancelLocked(id);
            _queuedPointers.Clear();
            _capturedPointer = null;
            _capturedRoute = NativeBottomScreenPointerRoute.None;
            _aimContact = new NativeBottomScreenAimContactSnapshot(false,
                Vector2.Zero);
            _interactionEpoch++;
        }

        private bool TryMapAimSampleLocked(in PointerSample sample,
            out PointerSample mapped)
        {
            mapped = sample;
            if (!_layout.IsValid || !Finite(sample.X, sample.Y)) return false;

            Vector2 canonical = _layout.LogicalToDs(sample.X, sample.Y);
            if (!Finite(canonical.X, canonical.Y)) return false;
            canonical = new Vector2(
                Math.Clamp(canonical.X,
                    0, NativeBottomScreenClassicLayoutSnapshot.DsWidth),
                Math.Clamp(canonical.Y,
                    0, NativeBottomScreenClassicLayoutSnapshot.DsHeight));
            mapped = sample with
            {
                Tool = PointerToolKind.Stylus,
                X = canonical.X,
                Y = canonical.Y,
                Pressure = 0,
                Buttons = StylusButtons.None,
                CoordinateKind = PointerCoordinateKind.Unknown,
                LogicalDisplayScale = 1,
                MappedExtentX = 0,
                MappedExtentY = 0
            };
            return true;
        }

        private static bool IsAimContactTool(PointerToolKind tool)
            => tool is PointerToolKind.Finger or PointerToolKind.Stylus
                or PointerToolKind.Eraser;

        private void QueueCancelLocked(long pointerId)
        {
            PointerSample sample = new(pointerId, PointerToolKind.Unknown,
                float.NaN, float.NaN, 0, StylusButtons.None,
                Environment.TickCount64);
            if (_events.Count >= QueueCapacity) _events.Clear();
            _events.Enqueue(new NativeBottomScreenPointerEvent(
                NativeBottomScreenPointerPhase.Cancel, sample, _generation));
        }

        private void EnqueueLocked(NativeBottomScreenPointerPhase phase,
            in PointerSample sample)
        {
            if (_events.Count >= QueueCapacity)
            {
                _events.Clear();
                // Preserve an explicit cancel at the boundary so a dropped
                // Up/Move can never leave the scene in a captured state.
                PointerSample cancelSample = new(sample.Id, sample.Tool,
                    float.NaN, float.NaN, 0, StylusButtons.None, sample.Timestamp);
                _events.Enqueue(new NativeBottomScreenPointerEvent(
                    NativeBottomScreenPointerPhase.Cancel, cancelSample, _generation));

                // Overflow terminates this gesture. Do not append the event
                // that crossed the bound: a later Up must not turn a gesture
                // whose history was discarded into a weapon commit.
                _queuedPointers.Remove(sample.Id);
                if (_capturedPointer == sample.Id) _capturedPointer = null;
                _capturedRoute = NativeBottomScreenPointerRoute.None;
                _interactionEpoch++;
                return;
            }
            _events.Enqueue(new NativeBottomScreenPointerEvent(phase, sample, _generation));
        }
    }

    public readonly record struct NativeBottomScreenPlatformRegistration(long Token,
        long Generation)
    {
        public bool IsValid => Token != 0;
    }

    /// <summary>
    /// Narrow platform boundary. It stores only the currently registered scene
    /// reference and token; all panel state remains in that scene's controller.
    /// </summary>
    public static class NativeBottomScreenPlatformBridge
    {
        private static readonly object Sync = new();
        private static NativeBottomScreenController? _current;
        private static long _token;
        private static long _nextToken;
        private static long _bridgeInteractionEpoch;
        private static long _observedControllerEpoch;

        public static NativeBottomScreenPlatformRegistration Register(
            NativeBottomScreenController controller)
        {
            ArgumentNullException.ThrowIfNull(controller);
            NativeBottomScreenController? previous;
            long previousToken;
            long token;
            lock (Sync)
            {
                previous = _current;
                previousToken = _token;
                token = ++_nextToken;
                _current = controller;
                _token = token;
                _observedControllerEpoch = controller.InteractionEpoch;
                _bridgeInteractionEpoch++;
            }
            if (previous != null)
            {
                previous.DetachPlatformToken(previousToken);
                previous.Cancel();
            }
            controller.AttachPlatformToken(token);
            return new NativeBottomScreenPlatformRegistration(token,
                controller.Generation);
        }

        public static void Unregister(NativeBottomScreenPlatformRegistration registration)
        {
            NativeBottomScreenController? controller = null;
            long token = 0;
            lock (Sync)
            {
                if (registration.Token == _token)
                {
                    controller = _current;
                    token = _token;
                    _current = null;
                    _token = 0;
                    _observedControllerEpoch = 0;
                    _bridgeInteractionEpoch++;
                }
            }
            if (controller != null)
            {
                controller.DetachPlatformToken(token);
                controller.Cancel();
            }
        }

        public static void Configure(Vector2i logicalSize, Vector2i framebufferSize,
            NativeBottomScreenMode mode,
            NativeBottomScreenAimMode aimMode = NativeBottomScreenAimMode.Free)
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return;
            controller!.ConfigureWithToken(token, generation, logicalSize,
                framebufferSize, mode, aimMode);
        }

        public static bool TryPointerDown(in PointerSample sample)
            => TryPointerDown(default, sample, requireRegistration: false);

        public static NativeBottomScreenPointerRoute RoutePointerDown(
            in PointerSample sample)
            => RoutePointerDown(default, sample, requireRegistration: false);

        public static NativeBottomScreenPointerRoute RoutePointerDown(
            in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample)
            => RoutePointerDown(registration, sample, requireRegistration: true);

        public static bool TryPointerDown(
            in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample)
            => TryPointerDown(registration, sample, requireRegistration: true);

        private static bool TryPointerDown(in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample, bool requireRegistration)
        {
            return RoutePointerDown(registration, sample, requireRegistration)
                is NativeBottomScreenPointerRoute.Control
                    or NativeBottomScreenPointerRoute.Aim;
        }

        private static NativeBottomScreenPointerRoute RoutePointerDown(
            in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample, bool requireRegistration)
        {
            if (!Snapshot(out NativeBottomScreenController? controller, out long token,
                out long generation, registration, requireRegistration))
                return NativeBottomScreenPointerRoute.None;
            return controller!.RoutePointerDown(token, generation, sample);
        }

        /// <summary>
        /// Convert a claimed True DS contact into canonical 256x192 absolute
        /// coordinates. The neutral sample deliberately carries no tip
        /// pressure or buttons: contact is drag-to-aim, never an implicit fire.
        /// </summary>
        public static PointerSample MapAimSample(in PointerSample sample)
            => MapAimSample(default, sample, requireRegistration: false);

        public static PointerSample MapAimSample(
            in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample)
            => MapAimSample(registration, sample, requireRegistration: true);

        private static PointerSample MapAimSample(
            in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample, bool requireRegistration)
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation, registration, requireRegistration))
                return sample;
            return controller!.MapAimSample(token, generation, sample);
        }

        public static bool TryPointerMove(in PointerSample sample)
            => TryPointerMove(default, sample, requireRegistration: false);

        public static bool TryPointerMove(
            in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample)
            => TryPointerMove(registration, sample, requireRegistration: true);

        private static bool TryPointerMove(in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample, bool requireRegistration)
        {
            if (!Snapshot(out NativeBottomScreenController? controller, out long token,
                out long generation, registration, requireRegistration)) return false;
            return controller!.TryPointerMove(token, generation, sample);
        }

        public static bool TryPointerUp(in PointerSample sample)
            => TryPointerUp(default, sample, requireRegistration: false);

        public static bool TryPointerUp(
            in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample)
            => TryPointerUp(registration, sample, requireRegistration: true);

        private static bool TryPointerUp(in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample, bool requireRegistration)
        {
            if (!Snapshot(out NativeBottomScreenController? controller, out long token,
                out long generation, registration, requireRegistration)) return false;
            return controller!.TryPointerUp(token, generation, sample);
        }

        public static bool BeginDesktopSession(
            NativeBottomScreenActivationMode mode)
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return false;
            return controller!.BeginDesktopSession(token, generation, mode);
        }

        public static bool EndDesktopSession()
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return false;
            return controller!.EndDesktopSession(token, generation);
        }

        public static bool CompleteDesktopSelection()
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return false;
            return controller!.CompleteDesktopSelection(token, generation);
        }

        public static bool TryDesktopCursorMove(Vector2 delta)
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return false;
            return controller!.TryDesktopCursorMove(token, generation, delta);
        }

        public static bool TryDesktopPointerDown()
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return false;
            return controller!.TryDesktopPointerDown(token, generation);
        }

        public static bool TryDesktopPointerUp()
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return false;
            return controller!.TryDesktopPointerUp(token, generation);
        }

        public static bool DesktopSessionActive
        {
            get
            {
                if (!Snapshot(out NativeBottomScreenController? controller,
                    out long token, out long generation)) return false;
                return controller!.IsDesktopSessionActive(token, generation);
            }
        }

        public static bool DesktopHoldContactActive
        {
            get
            {
                if (!Snapshot(out NativeBottomScreenController? controller,
                    out long token, out long generation)) return false;
                return controller!.IsDesktopHoldContactActive(token, generation);
            }
        }

        public static bool CancelPointer(long pointerId)
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return false;
            return controller!.CancelPointer(token, generation, pointerId);
        }

        public static bool SuppressGlobalStylus
        {
            get
            {
                lock (Sync) return _current?.SuppressGlobalStylus == true;
            }
        }

        public static long InteractionEpoch
        {
            get
            {
                lock (Sync)
                {
                    long controllerEpoch = _current?.InteractionEpoch ?? 0;
                    if (controllerEpoch != _observedControllerEpoch)
                    {
                        _observedControllerEpoch = controllerEpoch;
                        _bridgeInteractionEpoch++;
                    }
                    return _bridgeInteractionEpoch;
                }
            }
        }

        public static bool TryGetVisiblePanel(out BottomScreenRect panel)
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out _, out _))
            {
                panel = default;
                return false;
            }
            return controller!.TryGetVisiblePanel(out panel);
        }

        public static void Cancel()
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return;
            controller!.Cancel(token, generation);
        }

        private static bool Snapshot(out NativeBottomScreenController? controller,
            out long token, out long generation)
        {
            lock (Sync)
            {
                controller = _current;
                token = _token;
                generation = controller?.Generation ?? 0;
                return controller != null && token != 0;
            }
        }

        private static bool Snapshot(out NativeBottomScreenController? controller,
            out long token, out long generation,
            in NativeBottomScreenPlatformRegistration registration,
            bool requireRegistration)
        {
            lock (Sync)
            {
                controller = _current;
                token = _token;
                generation = controller?.Generation ?? 0;
                return controller != null && token != 0
                    && (!requireRegistration || registration.Token == token
                        && registration.Generation == generation);
            }
        }
    }

    // This internal entry point keeps the platform bridge's token check
    // inside the scene-owned controller without exposing the token to gameplay.
    public sealed partial class NativeBottomScreenController
    {
        internal bool IsDesktopSessionActive(long token, long generation)
        {
            lock (_sync)
            {
                return ValidTokenLocked(token, generation) && _desktopSessionActive;
            }
        }

        internal bool IsDesktopHoldContactActive(long token, long generation)
        {
            lock (_sync)
            {
                return ValidTokenLocked(token, generation)
                    && _desktopSessionActive
                    && _desktopActivationMode == NativeBottomScreenActivationMode.Hold;
            }
        }

        internal void ConfigureWithToken(long token, long generation,
            Vector2i logicalSize, Vector2i framebufferSize,
            NativeBottomScreenMode mode,
            NativeBottomScreenAimMode aimMode = NativeBottomScreenAimMode.Free)
        {
            if (!Enum.IsDefined(mode)) mode = NativeBottomScreenMode.Off;
            if (!Enum.IsDefined(aimMode)) aimMode = NativeBottomScreenAimMode.Free;
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return;
                NativeBottomScreenLayout layout = NativeBottomScreenLayout.Compute(
                    logicalSize, framebufferSize, _layoutOptions);
                if (mode == _mode && aimMode == _aimMode && layout == _layout) return;
                CancelQueuedLocked();
                _mode = mode;
                _aimMode = aimMode;
                _layout = layout;
                _popupOpen = mode == NativeBottomScreenMode.AlwaysVisible;
                _selectorOpen = false;
                _desktopSessionActive = false;
            }
        }
    }
}
