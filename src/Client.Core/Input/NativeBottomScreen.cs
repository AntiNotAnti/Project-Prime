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
        BottomScreenRect PanelLogical, BottomScreenRect PanelFramebuffer,
        BottomScreenRect TabLogical, BottomScreenRect TabFramebuffer)
    {
        public bool IsValid => PanelLogical.Width > 0 && PanelLogical.Height > 0
            && PanelFramebuffer.Width > 0 && PanelFramebuffer.Height > 0;

        public bool ContainsPanel(float x, float y) => PanelLogical.Contains(x, y);
        public bool ContainsTab(float x, float y) => TabLogical.Contains(x, y);

        public Vector2 LogicalToDs(float x, float y)
        {
            if (!IsValid) return new Vector2(float.NaN, float.NaN);
            return new(
                (x - PanelLogical.Left) * 256f / PanelLogical.Width,
                (y - PanelLogical.Top) * 192f / PanelLogical.Height);
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
        {
            int logicalWidth = Math.Max(1, logicalSize.X);
            int logicalHeight = Math.Max(1, logicalSize.Y);
            int framebufferWidth = Math.Max(1, framebufferSize.X);
            int framebufferHeight = Math.Max(1, framebufferSize.Y);

            // Bound the panel by both width and height, then derive height
            // from width so no aspect-ratio stretch is introduced.
            float panelWidth = MathF.Min(logicalWidth * .72f,
                logicalHeight * .58f * (4f / 3f));
            panelWidth = MathF.Max(1, MathF.Min(panelWidth, logicalWidth));
            float panelHeight = panelWidth * .75f;
            float margin = MathF.Max(8, logicalHeight * .025f);
            float panelBottom = MathF.Max(panelHeight + margin,
                logicalHeight - margin);
            float panelTop = panelBottom - panelHeight;
            float panelLeft = (logicalWidth - panelWidth) * .5f;
            var panelLogical = new BottomScreenRect(panelLeft, panelTop,
                panelLeft + panelWidth, panelBottom);

            float tabHeight = MathF.Min(48, MathF.Max(24, logicalHeight * .075f));
            float tabWidth = MathF.Min(panelWidth * .36f, logicalWidth * .45f);
            float tabLeft = (logicalWidth - tabWidth) * .5f;
            float tabBottom = MathF.Min(logicalHeight - 2, panelTop - 2);
            float tabTop = MathF.Max(0, tabBottom - tabHeight);
            var tabLogical = new BottomScreenRect(tabLeft, tabTop,
                tabLeft + tabWidth, tabBottom);

            float framebufferScaleX = framebufferWidth / (float)logicalWidth;
            float framebufferScaleY = framebufferHeight / (float)logicalHeight;
            return new NativeBottomScreenLayout(logicalSize, framebufferSize,
                panelLogical, panelLogical.Scale(framebufferScaleX, framebufferScaleY),
                tabLogical, tabLogical.Scale(framebufferScaleX, framebufferScaleY));
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
        private readonly object _sync = new();
        private readonly Queue<NativeBottomScreenPointerEvent> _events = new();
        private readonly HashSet<int> _queuedPointers = new();
        private NativeBottomScreenMode _mode;
        private NativeBottomScreenLayout _layout;
        private long _generation;
        private long _platformToken;
        private int? _capturedPointer;
        private int? _tabPointer;
        private bool _popupOpen;

        public NativeBottomScreenMode Mode { get { lock (_sync) return _mode; } }
        public NativeBottomScreenLayout Layout { get { lock (_sync) return _layout; } }
        public long Generation { get { lock (_sync) return _generation; } }
        public bool PopupOpen { get { lock (_sync) return _popupOpen; } }
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
            NativeBottomScreenLayout layout = NativeBottomScreenLayout.Compute(
                logicalSize, framebufferSize);
            lock (_sync)
            {
                if (mode == _mode && layout == _layout) return;
                CancelQueuedLocked();
                _mode = mode;
                _layout = layout;
                _popupOpen = mode == NativeBottomScreenMode.AlwaysVisible;
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
                _tabPointer = null;
                _popupOpen = _mode == NativeBottomScreenMode.AlwaysVisible;
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
            }
        }

        public bool TogglePopup()
        {
            lock (_sync)
            {
                if (_mode != NativeBottomScreenMode.Popup) return false;
                if (_popupOpen) CancelQueuedLocked();
                _popupOpen = !_popupOpen;
                return _popupOpen;
            }
        }

        /// <summary>Popup is a one-selection surface for touch-only clients.</summary>
        public void DismissPopupAfterSelection()
        {
            lock (_sync)
            {
                if (_mode == NativeBottomScreenMode.Popup)
                {
                    _popupOpen = false;
                    _tabPointer = null;
                }
            }
        }

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
                if (_mode == NativeBottomScreenMode.Popup && !_popupOpen)
                {
                    if (!_layout.ContainsTab(sample.X, sample.Y)) return false;
                    _popupOpen = true;
                    _tabPointer = sample.Id;
                    return true;
                }
                if (!_layout.ContainsPanel(sample.X, sample.Y)) return false;
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
                if (_tabPointer == sample.Id) return true;
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
                if (_tabPointer == sample.Id)
                {
                    _tabPointer = null;
                    return true;
                }
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
                if (_tabPointer == pointerId) _tabPointer = null;
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
            _tabPointer = null;
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
        internal void ConfigureWithToken(long token, long generation,
            Vector2i logicalSize, Vector2i framebufferSize,
            NativeBottomScreenMode mode)
        {
            if (!Enum.IsDefined(mode)) mode = NativeBottomScreenMode.Off;
            NativeBottomScreenLayout layout = NativeBottomScreenLayout.Compute(
                logicalSize, framebufferSize);
            lock (_sync)
            {
                if (!ValidTokenLocked(token, generation)) return;
                if (mode == _mode && layout == _layout) return;
                CancelQueuedLocked();
                _mode = mode;
                _layout = layout;
                _popupOpen = mode == NativeBottomScreenMode.AlwaysVisible;
            }
        }
    }
}
