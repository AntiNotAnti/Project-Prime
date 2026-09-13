using System;
using System.Collections.Generic;
using MphRead.Mods.Input;
using SDL;

namespace MphRead;

/// <summary>
/// Host-thread owner for SDL pen identity, contact transitions, and capture.
/// SDL polling stays in <see cref="SdlGameHost"/>; external bottom-screen
/// routing and stylus configuration enter through narrow callbacks.
/// </summary>
internal sealed class SdlPointerHub : IDisposable
{
    private readonly Dictionary<uint, SdlPointerState> _pens = new();
    private readonly StylusInput _stylus = DesktopStylusInput.Input;
    private readonly Action<StylusInput> _configureStylus;
    private readonly Func<PointerSample, bool> _tryBottomDown;
    private readonly Func<PointerSample, bool> _tryBottomMove;
    private readonly Func<PointerSample, bool> _tryBottomUp;
    private readonly Func<int, bool> _cancelBottomPointer;
    private readonly Func<float> _logicalDisplayScale;
    private uint? _capturedPenId;
    private uint? _bottomScreenPenId;
    private bool _disposed;

    internal SdlPointerHub(Action<StylusInput> configureStylus,
        Func<PointerSample, bool> tryBottomDown,
        Func<PointerSample, bool> tryBottomMove,
        Func<PointerSample, bool> tryBottomUp,
        Func<int, bool> cancelBottomPointer,
        Func<float> logicalDisplayScale)
    {
        _configureStylus = configureStylus ?? throw new ArgumentNullException(nameof(configureStylus));
        _tryBottomDown = tryBottomDown ?? throw new ArgumentNullException(nameof(tryBottomDown));
        _tryBottomMove = tryBottomMove ?? throw new ArgumentNullException(nameof(tryBottomMove));
        _tryBottomUp = tryBottomUp ?? throw new ArgumentNullException(nameof(tryBottomUp));
        _cancelBottomPointer = cancelBottomPointer
            ?? throw new ArgumentNullException(nameof(cancelBottomPointer));
        _logicalDisplayScale = logicalDisplayScale
            ?? throw new ArgumentNullException(nameof(logicalDisplayScale));
    }

    internal bool StylusActive => _stylus.Active;

    internal void HandleProximity(SDL_PenProximityEvent evt, bool entered)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint id = (uint)evt.which;
        if (!entered)
        {
            if (_bottomScreenPenId == id)
            {
                _cancelBottomPointer((int)id);
                _bottomScreenPenId = null;
                _pens.Remove(id);
                return;
            }
            SdlPointerState state = GetPenState(id);
            bool ended = state.InProximity
                && _stylus.PointerProximityExit(PenSample(id, state, 0));
            if (_capturedPenId == id)
            {
                // A malformed or truncated event stream may omit the matching
                // proximity sample. Only the captured pen may use fallback
                // cancellation; another pen leaving must not cancel its owner.
                if (!ended) _stylus.Cancel();
                _capturedPenId = null;
            }
            _pens.Remove(id);
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
        uint id = (uint)evt.which;
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
            if (ShouldOfferPenToBottomScreen(_stylus.Active) && _tryBottomDown(sample))
            {
                _bottomScreenPenId = id;
                return;
            }
            EnsureStylusContact(id, state.Contact, sample);
        }
        else if (_bottomScreenPenId == id)
        {
            _tryBottomUp(sample);
            _bottomScreenPenId = null;
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
        uint id = (uint)evt.which;
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
        if (_bottomScreenPenId == id)
        {
            _tryBottomMove(sample);
            return;
        }
        if (_capturedPenId == id && _stylus.Active)
        {
            _stylus.PointerMove(sample);
            return;
        }
        if (ShouldOfferPenToBottomScreen(_stylus.Active) && _tryBottomDown(sample))
        {
            _bottomScreenPenId = id;
            return;
        }
        _configureStylus(_stylus);
        if (!EnsureStylusContact(id, state.Contact, sample)) return;
        _stylus.PointerMove(sample);
    }

    internal void HandleButton(SDL_PenButtonEvent evt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint id = (uint)evt.which;
        SdlPointerState state = GetPenState(id);
        state.X = evt.x;
        state.Y = evt.y;
        state.Contact = (evt.pen_state & SDL_PenInputFlags.SDL_PEN_INPUT_DOWN) != 0;
        state.InProximity = true;
        state.Tool = PenTool(evt.pen_state);
        state.Buttons = PenButtons(evt.pen_state);
        _pens[id] = state;
        if (_bottomScreenPenId == id)
        {
            _tryBottomMove(PenSample(id, state, evt.timestamp));
            return;
        }
        _configureStylus(_stylus);
        PointerSample sample = PenSample(id, state, evt.timestamp);
        if (!state.Contact || EnsureStylusContact(id, state.Contact, sample))
            _stylus.UpdateButtonState(sample);
    }

    internal void HandleAxis(SDL_PenAxisEvent evt)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint id = (uint)evt.which;
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
        if (_bottomScreenPenId == id)
        {
            _tryBottomMove(PenSample(id, state, evt.timestamp));
            return;
        }
        _configureStylus(_stylus);
        PointerSample sample = PenSample(id, state, evt.timestamp);
        if (!state.Contact || EnsureStylusContact(id, state.Contact, sample))
            _stylus.UpdateButtonState(sample);
    }

    internal void CancelInteraction()
    {
        _stylus.Cancel();
        if (_bottomScreenPenId is uint id) _cancelBottomPointer((int)id);
        _capturedPenId = null;
        _bottomScreenPenId = null;
    }

    internal void Reset()
    {
        if (_bottomScreenPenId is uint id) _cancelBottomPointer((int)id);
        _stylus.Cancel();
        _pens.Clear();
        _capturedPenId = null;
        _bottomScreenPenId = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Reset();
    }

    private bool EnsureStylusContact(uint id, bool contact, in PointerSample sample)
    {
        if (_capturedPenId == id && _stylus.Active) return true;
        if (!ShouldBeginStylusContact(contact, _stylus.Active)) return false;
        _capturedPenId = null;
        _configureStylus(_stylus);
        if (!_stylus.PointerDown(sample)) return false;
        _capturedPenId = id;
        return true;
    }

    private SdlPointerState GetPenState(uint id)
        => _pens.TryGetValue(id, out SdlPointerState state) ? state
            : new SdlPointerState { Tool = PointerToolKind.Stylus };

    private static PointerSample PenSample(uint id, SdlPointerState state,
        ulong timestampNanoseconds)
        => new(unchecked((int)id), state.Tool, state.X, state.Y,
            state.Pressure, state.Buttons,
            (long)Math.Min(timestampNanoseconds / 1_000_000UL, (ulong)long.MaxValue),
            state.CoordinateKind, state.LogicalDisplayScale);

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
}
