using System;
using System.Collections.Generic;
using MphRead.Mods.Input;
using OpenTK.Mathematics;
using SDL;

namespace MphRead;

/// <summary>
/// Host-thread owner for SDL pen identity, contact transitions, and capture.
/// SDL polling stays in <see cref="SdlGameHost"/>; external bottom-screen
/// routing and stylus configuration enter through narrow callbacks.
/// </summary>
internal sealed class SdlPointerHub : IDisposable
{
    private readonly Dictionary<ulong, SdlPointerState> _pens = new();
    private readonly Dictionary<long, NativeBottomScreenPointerRoute>
        _bottomContacts = new();
    private readonly Dictionary<SdlFingerKey, long> _fingerPointerIds = new();
    // SDL IDs are unsigned 64-bit values while the neutral path uses a signed
    // long. Values that do not fit are assigned a negative host token for the
    // lifetime of that pen; values that do fit retain their exact identity.
    private readonly Dictionary<ulong, long> _oversizedPointerIds = new();
    private readonly HashSet<long> _syntheticPointerIds = new();
    private long _nextSyntheticPointerId = long.MinValue + 1;
    private readonly StylusInput _stylus = DesktopStylusInput.Input;
    private readonly Action<StylusInput> _configureStylus;
    private readonly Func<PointerSample, NativeBottomScreenPointerRoute>
        _routeBottomDown;
    private readonly Func<PointerSample, PointerSample> _mapAimSample;
    private readonly Func<PointerSample, bool> _tryBottomMove;
    private readonly Func<PointerSample, bool> _tryBottomUp;
    private readonly Func<long, bool> _cancelBottomPointer;
    private readonly Func<bool> _suppressOutsidePanelStylus;
    private readonly Func<long> _bottomInteractionEpoch;
    private readonly Func<float> _logicalDisplayScale;
    private long _capturedBottomEpoch;
    private ulong? _capturedPenId;
    private bool _disposed;

    internal SdlPointerHub(Action<StylusInput> configureStylus,
        Func<PointerSample, NativeBottomScreenPointerRoute> routeBottomDown,
        Func<PointerSample, PointerSample> mapAimSample,
        Func<PointerSample, bool> tryBottomMove,
        Func<PointerSample, bool> tryBottomUp,
        Func<long, bool> cancelBottomPointer,
        Func<bool> suppressOutsidePanelStylus,
        Func<long> bottomInteractionEpoch,
        Func<float> logicalDisplayScale)
    {
        _configureStylus = configureStylus
            ?? throw new ArgumentNullException(nameof(configureStylus));
        _routeBottomDown = routeBottomDown
            ?? throw new ArgumentNullException(nameof(routeBottomDown));
        _mapAimSample = mapAimSample
            ?? throw new ArgumentNullException(nameof(mapAimSample));
        _tryBottomMove = tryBottomMove
            ?? throw new ArgumentNullException(nameof(tryBottomMove));
        _tryBottomUp = tryBottomUp
            ?? throw new ArgumentNullException(nameof(tryBottomUp));
        _cancelBottomPointer = cancelBottomPointer
            ?? throw new ArgumentNullException(nameof(cancelBottomPointer));
        _suppressOutsidePanelStylus = suppressOutsidePanelStylus
            ?? throw new ArgumentNullException(nameof(suppressOutsidePanelStylus));
        _bottomInteractionEpoch = bottomInteractionEpoch
            ?? throw new ArgumentNullException(nameof(bottomInteractionEpoch));
        _logicalDisplayScale = logicalDisplayScale
            ?? throw new ArgumentNullException(nameof(logicalDisplayScale));
    }

    internal bool StylusActive => _stylus.Active;

    internal void HandleProximity(SDL_PenProximityEvent evt, bool entered)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReconcileBottomScreenEpoch();
        ulong id = (ulong)evt.which;
        if (!entered)
        {
            SdlPointerState state = GetPenState(id);
            if (TryEndBottomContact(PenSample(id, state, evt.timestamp),
                cancel: true))
            {
                _pens.Remove(id);
                ReleasePointerId(id);
                return;
            }

            bool ended = state.InProximity
                && _stylus.PointerProximityExit(PenSample(id, state, 0));
            if (_capturedPenId == id)
            {
                // A malformed event stream may omit the matching proximity
                // sample. Only the captured pen may use fallback cancellation;
                // another pen leaving must not cancel its owner.
                if (!ended) _stylus.Cancel();
                _capturedPenId = null;
            }
            _pens.Remove(id);
            ReleasePointerId(id);
            return;
        }

        PointerCoordinateKind coordinateKind = PenCoordinateKind(evt.which);
        _pens[id] = new SdlPointerState
        {
            Tool = PointerToolKind.Stylus,
            InProximity = true,
            CoordinateKind = coordinateKind,
            // SDL reports direct pen positions in a window-relative coordinate
            // stream. Keep the known logical-pixel scale in the neutral sample.
            LogicalDisplayScale = coordinateKind == PointerCoordinateKind.Direct
                ? _logicalDisplayScale() : 1
        };
    }

    internal void HandleTouch(SDL_PenTouchEvent evt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReconcileBottomScreenEpoch();
        ulong id = (ulong)evt.which;
        SdlPointerState state = GetPenState(id);
        state.Contact = evt.down;
        state.InProximity = true;
        state.X = evt.x;
        state.Y = evt.y;
        state.Tool = evt.eraser ? PointerToolKind.Eraser : PenTool(evt.pen_state);
        state.Buttons = PenButtons(evt.pen_state);
        _pens[id] = state;
        _configureStylus(_stylus);
        PointerSample sample = PenSample(id, state, evt.timestamp);
        if (state.Contact)
        {
            if (TryBeginBottomContact(sample)) return;
            EnsureStylusContact(id, state.Contact, sample);
        }
        else if (TryEndBottomContact(sample, cancel: false))
        {
            return;
        }
        else if (_capturedPenId == id)
        {
            _stylus.PointerUp(sample);
            _capturedPenId = null;
        }
    }

    internal void HandleMotion(SDL_PenMotionEvent evt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReconcileBottomScreenEpoch();
        ulong id = (ulong)evt.which;
        SdlPointerState state = GetPenState(id);
        state.X = evt.x;
        state.Y = evt.y;
        state.Contact = (evt.pen_state & SDL_PenInputFlags.SDL_PEN_INPUT_DOWN) != 0;
        state.InProximity = true;
        state.Tool = PenTool(evt.pen_state);
        state.Buttons = PenButtons(evt.pen_state);
        _pens[id] = state;
        PointerSample sample = PenSample(id, state, evt.timestamp);
        if (!state.Contact)
        {
            _stylus.PointerProximityMove(sample);
            return;
        }
        if (TryMoveBottomContact(sample)) return;
        if (_capturedPenId == id && _stylus.Active)
        {
            _stylus.PointerMove(sample);
            return;
        }
        if (TryBeginBottomContact(sample)) return;
        _configureStylus(_stylus);
        if (!EnsureStylusContact(id, state.Contact, sample)) return;
        _stylus.PointerMove(sample);
    }

    internal void HandleButton(SDL_PenButtonEvent evt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReconcileBottomScreenEpoch();
        ulong id = (ulong)evt.which;
        SdlPointerState state = GetPenState(id);
        state.X = evt.x;
        state.Y = evt.y;
        state.Contact = (evt.pen_state & SDL_PenInputFlags.SDL_PEN_INPUT_DOWN) != 0;
        state.InProximity = true;
        state.Tool = PenTool(evt.pen_state);
        state.Buttons = PenButtons(evt.pen_state);
        _pens[id] = state;
        PointerSample sample = PenSample(id, state, evt.timestamp);
        if (TryUpdateBottomButtons(sample)) return;
        _configureStylus(_stylus);
        if (!state.Contact || EnsureStylusContact(id, state.Contact, sample))
            _stylus.UpdateButtonState(sample);
    }

    internal void HandleAxis(SDL_PenAxisEvent evt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReconcileBottomScreenEpoch();
        ulong id = (ulong)evt.which;
        SdlPointerState state = GetPenState(id);
        state.X = evt.x;
        state.Y = evt.y;
        state.Contact = (evt.pen_state & SDL_PenInputFlags.SDL_PEN_INPUT_DOWN) != 0;
        state.InProximity = true;
        state.Tool = PenTool(evt.pen_state);
        state.Buttons = PenButtons(evt.pen_state);
        if (evt.axis == SDL_PenAxis.SDL_PEN_AXIS_PRESSURE)
            state.Pressure = Math.Clamp(evt.value, 0, 1);
        _pens[id] = state;
        PointerSample sample = PenSample(id, state, evt.timestamp);
        if (TryUpdateBottomButtons(sample)) return;
        _configureStylus(_stylus);
        if (!state.Contact || EnsureStylusContact(id, state.Contact, sample))
            _stylus.UpdateButtonState(sample);
    }

    internal void CancelInteraction()
    {
        foreach (long id in new List<long>(_bottomContacts.Keys))
        {
            _cancelBottomPointer(id);
        }
        _bottomContacts.Clear();
        _stylus.Cancel();
        _capturedPenId = null;
    }

    internal void Reset()
    {
        CancelInteraction();
        _pens.Clear();
        _oversizedPointerIds.Clear();
        _fingerPointerIds.Clear();
        _syntheticPointerIds.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }

    /// <summary>
    /// Route one native SDL finger stream. SDL coordinates are normalized to
    /// the window and deliberately remain unclamped so panel hit testing can
    /// reject contacts that begin outside the rendered lower screen.
    /// </summary>
    internal void HandleFinger(SDL_TouchFingerEvent evt, Vector2i logicalSize,
        bool cancel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReconcileBottomScreenEpoch();
        var key = new SdlFingerKey((ulong)evt.touchID, (ulong)evt.fingerID);
        bool down = evt.type == SDL_EventType.SDL_EVENT_FINGER_DOWN;
        bool up = evt.type == SDL_EventType.SDL_EVENT_FINGER_UP;

        if (down)
        {
            if (_fingerPointerIds.ContainsKey(key)) return;
            long pointerId = AllocateSyntheticPointerId();
            _fingerPointerIds[key] = pointerId;
            PointerSample sample = FingerSample(pointerId, evt, logicalSize);
            if (!TryBeginBottomContact(sample)) ReleaseFingerPointerId(key);
            return;
        }

        if (!_fingerPointerIds.TryGetValue(key, out long existingId)) return;
        PointerSample current = FingerSample(existingId, evt, logicalSize);
        if (cancel)
        {
            TryEndBottomContact(current, cancel: true);
            ReleaseFingerPointerId(key);
        }
        else if (up)
        {
            TryEndBottomContact(current, cancel: false);
            ReleaseFingerPointerId(key);
        }
        else
        {
            TryMoveBottomContact(current);
        }
    }

    internal static Vector2 MapFingerPosition(float normalizedX,
        float normalizedY, Vector2i logicalSize)
        => new(normalizedX * logicalSize.X, normalizedY * logicalSize.Y);

    private bool TryBeginBottomContact(in PointerSample sample)
    {
        if (_bottomContacts.ContainsKey(sample.Id)) return true;
        NativeBottomScreenPointerRoute route = _routeBottomDown(sample);
        if (route == NativeBottomScreenPointerRoute.None
            && _suppressOutsidePanelStylus())
        {
            // True DS mode intentionally has no global stylus fallback. An
            // outside-panel contact is consumed as an inert contact instead
            // of unexpectedly aiming the top screen.
            route = NativeBottomScreenPointerRoute.Suppressed;
        }
        if (route == NativeBottomScreenPointerRoute.None) return false;

        _bottomContacts[sample.Id] = route;
        _capturedBottomEpoch = _bottomInteractionEpoch();
        if (route == NativeBottomScreenPointerRoute.Aim)
        {
            _configureStylus(_stylus);
            if (!_stylus.PointerDown(_mapAimSample(sample)))
            {
                _cancelBottomPointer(sample.Id);
                _bottomContacts.Remove(sample.Id);
            }
        }
        return true;
    }

    private bool TryMoveBottomContact(in PointerSample sample)
    {
        if (!_bottomContacts.TryGetValue(sample.Id,
            out NativeBottomScreenPointerRoute route)) return false;
        if (route == NativeBottomScreenPointerRoute.Aim)
        {
            if (!_tryBottomMove(sample))
            {
                _stylus.Cancel();
                _cancelBottomPointer(sample.Id);
                _bottomContacts.Remove(sample.Id);
                return true;
            }
            if (!_stylus.PointerMove(_mapAimSample(sample)))
            {
                _stylus.Cancel();
                _cancelBottomPointer(sample.Id);
                _bottomContacts.Remove(sample.Id);
            }
            return true;
        }
        if (route == NativeBottomScreenPointerRoute.Control)
            _tryBottomMove(sample);
        return true;
    }

    private bool TryUpdateBottomButtons(in PointerSample sample)
    {
        if (!_bottomContacts.TryGetValue(sample.Id,
            out NativeBottomScreenPointerRoute route)) return false;
        if (route == NativeBottomScreenPointerRoute.Aim)
        {
            if (!_tryBottomMove(sample))
            {
                _stylus.Cancel();
                _cancelBottomPointer(sample.Id);
                _bottomContacts.Remove(sample.Id);
                return true;
            }
            PointerSample mapped = _mapAimSample(sample) with
            {
                // Barrel/pressure bindings remain explicit. The contact
                // samples themselves are neutralized by MapAimSample.
                Pressure = sample.Pressure,
                Buttons = sample.Buttons
            };
            _stylus.UpdateButtonState(mapped);
        }
        else if (route == NativeBottomScreenPointerRoute.Control)
        {
            _tryBottomMove(sample);
        }
        return true;
    }

    private bool TryEndBottomContact(in PointerSample sample,
        bool cancel)
    {
        if (!_bottomContacts.TryGetValue(sample.Id,
            out NativeBottomScreenPointerRoute route)) return false;
        if (cancel)
        {
            _cancelBottomPointer(sample.Id);
            if (route == NativeBottomScreenPointerRoute.Aim)
                _stylus.Cancel();
        }
        else
        {
            bool accepted = _tryBottomUp(sample);
            if (route == NativeBottomScreenPointerRoute.Aim)
            {
                if (accepted) _stylus.PointerUp(_mapAimSample(sample));
                else _stylus.Cancel();
            }
        }
        _bottomContacts.Remove(sample.Id);
        return true;
    }

    private bool EnsureStylusContact(ulong id, bool contact,
        in PointerSample sample)
    {
        if (_capturedPenId == id && _stylus.Active) return true;
        if (!ShouldBeginStylusContact(contact, _stylus.Active)) return false;
        _capturedPenId = null;
        _configureStylus(_stylus);
        if (!_stylus.PointerDown(sample)) return false;
        _capturedPenId = id;
        return true;
    }

    private SdlPointerState GetPenState(ulong id)
        => _pens.TryGetValue(id, out SdlPointerState state) ? state
            : new SdlPointerState { Tool = PointerToolKind.Stylus };

    private PointerSample PenSample(ulong id, SdlPointerState state,
        ulong timestampNanoseconds)
        => new(NeutralPointerId(id), state.Tool, state.X, state.Y,
            state.Pressure, state.Buttons,
            (long)Math.Min(timestampNanoseconds / 1_000_000UL,
                (ulong)long.MaxValue), state.CoordinateKind,
            state.LogicalDisplayScale);

    private static PointerSample FingerSample(long pointerId,
        SDL_TouchFingerEvent evt, Vector2i logicalSize)
    {
        Vector2 logical = MapFingerPosition(evt.x, evt.y, logicalSize);
        return new PointerSample(pointerId, PointerToolKind.Finger,
            logical.X, logical.Y, Math.Clamp(evt.pressure, 0, 1),
            StylusButtons.None,
            (long)Math.Min(evt.timestamp / 1_000_000UL, (ulong)long.MaxValue),
            PointerCoordinateKind.Direct, 1,
            logicalSize.X, logicalSize.Y);
    }

    private long NeutralPointerId(ulong nativeId)
    {
        if (nativeId <= long.MaxValue) return (long)nativeId;
        if (_oversizedPointerIds.TryGetValue(nativeId, out long pointerId))
            return pointerId;
        pointerId = AllocateSyntheticPointerId();
        _oversizedPointerIds[nativeId] = pointerId;
        return pointerId;
    }

    private void ReleasePointerId(ulong nativeId)
    {
        if (nativeId > long.MaxValue
            && _oversizedPointerIds.Remove(nativeId, out long pointerId))
        {
            _syntheticPointerIds.Remove(pointerId);
        }
    }

    private long AllocateSyntheticPointerId()
    {
        long pointerId;
        do
        {
            pointerId = _nextSyntheticPointerId++;
        }
        while (pointerId == NativeBottomScreenController.DesktopPointerId
            || !_syntheticPointerIds.Add(pointerId));
        return pointerId;
    }

    internal void ReconcileBottomScreenEpoch()
    {
        long current = _bottomInteractionEpoch();
        if (_bottomContacts.Count > 0 && _capturedBottomEpoch != current)
        {
            bool cancelStylus = false;
            foreach ((long id, NativeBottomScreenPointerRoute route)
                in _bottomContacts)
            {
                _cancelBottomPointer(id);
                cancelStylus |= route == NativeBottomScreenPointerRoute.Aim;
            }
            _bottomContacts.Clear();
            if (cancelStylus) _stylus.Cancel();
            _capturedBottomEpoch = current;
        }
    }

    private void ReleaseFingerPointerId(in SdlFingerKey key)
    {
        if (_fingerPointerIds.Remove(key, out long pointerId))
            _syntheticPointerIds.Remove(pointerId);
    }

    private static PointerCoordinateKind PenCoordinateKind(SDL_PenID id)
        => SDL3.SDL_GetPenDeviceType(id) switch
        {
            SDL_PenDeviceType.SDL_PEN_DEVICE_TYPE_DIRECT => PointerCoordinateKind.Direct,
            SDL_PenDeviceType.SDL_PEN_DEVICE_TYPE_INDIRECT => PointerCoordinateKind.Indirect,
            _ => PointerCoordinateKind.Unknown
        };

    private static PointerToolKind PenTool(SDL_PenInputFlags flags)
        => (flags & SDL_PenInputFlags.SDL_PEN_INPUT_ERASER_TIP) != 0
            ? PointerToolKind.Eraser : PointerToolKind.Stylus;

    private static StylusButtons PenButtons(SDL_PenInputFlags flags)
    {
        StylusButtons result = StylusButtons.None;
        if ((flags & SDL_PenInputFlags.SDL_PEN_INPUT_BUTTON_1) != 0)
            result |= StylusButtons.Primary;
        if ((flags & (SDL_PenInputFlags.SDL_PEN_INPUT_BUTTON_2
            | SDL_PenInputFlags.SDL_PEN_INPUT_BUTTON_3)) != 0)
            result |= StylusButtons.Secondary;
        return result;
    }

    internal static bool ShouldBeginStylusContact(bool contact, bool stylusActive)
        => contact && !stylusActive;

    internal static bool ShouldOfferPenToBottomScreen(bool stylusActive)
        => !stylusActive;

    private struct SdlPointerState
    {
        public float X, Y, Pressure;
        public StylusButtons Buttons;
        public PointerToolKind Tool;
        public bool Contact, InProximity;
        public PointerCoordinateKind CoordinateKind;
        public float LogicalDisplayScale;
    }

    private readonly record struct SdlFingerKey(ulong TouchId, ulong FingerId);
}
