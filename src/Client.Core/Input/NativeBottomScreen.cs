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

    /// <summary>Interactive regions on the DS-inspired lower screen.</summary>
    public enum NativeBottomScreenRegion
    {
        None,
        Aim,
        PowerBeam,
        Missile,
        NextWeapon,
        WeaponSelect,
        AltForm
    }

    public readonly record struct NativeBottomScreenButton(
        NativeBottomScreenRegion Region, Vector2 Position, float Radius,
        string Label);

    /// <summary>
    /// Canonical 256x192 button placement based on the original lower-screen
    /// arrangement. Everything outside a button is the touch aiming surface.
    /// </summary>
    public static class NativeBottomScreenClassicLayout
    {
        public const float DsWidth = 256;
        public const float DsHeight = 192;

        public static readonly NativeBottomScreenButton[] Buttons =
        {
            new(NativeBottomScreenRegion.PowerBeam, new Vector2(26, 26), 22, "BEAM"),
            new(NativeBottomScreenRegion.Missile, new Vector2(80, 24), 20, "MSL"),
            new(NativeBottomScreenRegion.NextWeapon, new Vector2(150, 28), 30, "WPN"),
            new(NativeBottomScreenRegion.WeaponSelect, new Vector2(222, 28), 26, "SEL"),
            new(NativeBottomScreenRegion.AltForm, new Vector2(228, 166), 22, "ALT")
        };

        public static NativeBottomScreenRegion RegionAt(Vector2 canonicalPoint)
        {
            if (!float.IsFinite(canonicalPoint.X) || !float.IsFinite(canonicalPoint.Y)
                || canonicalPoint.X < 0 || canonicalPoint.X > DsWidth
                || canonicalPoint.Y < 0 || canonicalPoint.Y > DsHeight)
            {
                return NativeBottomScreenRegion.None;
            }
            foreach (NativeBottomScreenButton button in Buttons)
            {
                Vector2 delta = canonicalPoint - button.Position;
                if (delta.LengthSquared <= button.Radius * button.Radius)
                    return button.Region;
            }
            return NativeBottomScreenRegion.Aim;
        }
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
        public const int DesktopPointerId = int.MinValue + 1;
        private readonly object _sync = new();
        private readonly Queue<NativeBottomScreenPointerEvent> _events = new();
        private readonly HashSet<int> _queuedPointers = new();
        private NativeBottomScreenMode _mode;
        private NativeBottomScreenStyle _style = NativeBottomScreenStyle.AffinitySelector;
        private NativeBottomScreenLayoutOptions _layoutOptions
            = NativeBottomScreenLayoutOptions.Default;
        private NativeBottomScreenLayout _layout;
        private long _generation;
        private long _platformToken;
        private int? _capturedPointer;
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
        public NativeBottomScreenLayout Layout { get { lock (_sync) return _layout; } }
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

        public void Configure(Vector2i logicalSize, Vector2i framebufferSize,
            NativeBottomScreenMode mode)
        {
            if (!Enum.IsDefined(mode)) mode = NativeBottomScreenMode.Off;
            lock (_sync)
            {
                NativeBottomScreenLayout layout = NativeBottomScreenLayout.Compute(
                    logicalSize, framebufferSize, _layoutOptions);
                if (mode == _mode && layout == _layout) return;
                CancelQueuedLocked();
                _mode = mode;
                _layout = layout;
                _popupOpen = mode == NativeBottomScreenMode.AlwaysVisible;
                _selectorOpen = false;
                _desktopSessionActive = false;
            }
        }

        public void UpdatePreferences(NativeBottomScreenMode mode,
            NativeBottomScreenStyle style, NativeBottomScreenLayoutOptions options)
        {
            if (!Enum.IsDefined(mode)) mode = NativeBottomScreenMode.Off;
            if (!Enum.IsDefined(style)) style = NativeBottomScreenStyle.ClassicDs;
            options = options.Sanitized();
            lock (_sync)
            {
                NativeBottomScreenLayout layout = NativeBottomScreenLayout.Compute(
                    _layout.LogicalSize, _layout.FramebufferSize, options);
                if (mode == _mode && style == _style && options == _layoutOptions
                    && layout == _layout)
                {
                    return;
                }
                CancelQueuedLocked();
                _mode = mode;
                _style = style;
                _layoutOptions = options;
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
            => TryPointerDown(0, 0, sample);

        public bool TryPointerMove(in PointerSample sample)
            => TryPointerMove(0, 0, sample);

        public bool TryPointerUp(in PointerSample sample)
            => TryPointerUp(0, 0, sample);

        public bool CancelPointer(int pointerId)
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

        internal bool TryPointerDown(long token, long generation,
            in PointerSample sample)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return false;
                if (_mode == NativeBottomScreenMode.Off
                    || !Finite(sample.X, sample.Y)
                    || _capturedPointer.HasValue && _capturedPointer.Value != sample.Id)
                    return false;
                if (_mode == NativeBottomScreenMode.Popup && !_popupOpen) return false;
                if (!_layout.ContainsPanel(sample.X, sample.Y)) return false;
                if (_style == NativeBottomScreenStyle.ClassicDs && !_selectorOpen)
                {
                    Vector2 canonical = _layout.LogicalToDs(sample.X, sample.Y);
                    NativeBottomScreenRegion region
                        = NativeBottomScreenClassicLayout.RegionAt(canonical);
                    // The original lower screen's middle is an aiming surface.
                    // Leave it unclaimed so the existing stylus/touch look path
                    // remains the only owner of camera movement.
                    if (region is NativeBottomScreenRegion.None
                        or NativeBottomScreenRegion.Aim)
                    {
                        return false;
                    }
                }
                _capturedPointer = sample.Id;
                _queuedPointers.Add(sample.Id);
                EnqueueLocked(NativeBottomScreenPointerPhase.Down, sample);
                return true;
            }
        }

        internal bool TryPointerMove(long token, long generation,
            in PointerSample sample)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return false;
                if (_capturedPointer != sample.Id) return false;
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
                EnqueueLocked(NativeBottomScreenPointerPhase.Up, sample);
                _capturedPointer = null;
                return true;
            }
        }

        internal bool CancelPointer(long token, long generation, int pointerId)
        {
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return false;
                bool matched = _capturedPointer == pointerId
                    || _queuedPointers.Contains(pointerId);
                if (!matched) return false;
                QueueCancelLocked(pointerId);
                _capturedPointer = null;
                _queuedPointers.Remove(pointerId);
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
            var ids = new HashSet<int>(_queuedPointers);
            if (_capturedPointer.HasValue) ids.Add(_capturedPointer.Value);
            foreach (int id in ids) QueueCancelLocked(id);
            _queuedPointers.Clear();
            _capturedPointer = null;
        }

        private void QueueCancelLocked(int pointerId)
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
                }
            }
            if (controller != null)
            {
                controller.DetachPlatformToken(token);
                controller.Cancel();
            }
        }

        public static void Configure(Vector2i logicalSize, Vector2i framebufferSize,
            NativeBottomScreenMode mode)
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return;
            controller!.ConfigureWithToken(token, generation, logicalSize,
                framebufferSize, mode);
        }

        public static bool TryPointerDown(in PointerSample sample)
            => TryPointerDown(default, sample, requireRegistration: false);

        public static bool TryPointerDown(
            in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample)
            => TryPointerDown(registration, sample, requireRegistration: true);

        private static bool TryPointerDown(in NativeBottomScreenPlatformRegistration registration,
            in PointerSample sample, bool requireRegistration)
        {
            if (!Snapshot(out NativeBottomScreenController? controller, out long token,
                out long generation, registration, requireRegistration)) return false;
            return controller!.TryPointerDown(token, generation, sample);
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

        public static bool CancelPointer(int pointerId)
        {
            if (!Snapshot(out NativeBottomScreenController? controller,
                out long token, out long generation)) return false;
            return controller!.CancelPointer(token, generation, pointerId);
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
            NativeBottomScreenMode mode)
        {
            if (!Enum.IsDefined(mode)) mode = NativeBottomScreenMode.Off;
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return;
                NativeBottomScreenLayout layout = NativeBottomScreenLayout.Compute(
                    logicalSize, framebufferSize, _layoutOptions);
                if (mode == _mode && layout == _layout) return;
                CancelQueuedLocked();
                _mode = mode;
                _layout = layout;
                _popupOpen = mode == NativeBottomScreenMode.AlwaysVisible;
                _selectorOpen = false;
                _desktopSessionActive = false;
            }
        }
    }
}
