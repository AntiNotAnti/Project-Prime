#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Threading;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Droid;

/// <summary>Copied native input ownership, safe to read from Android's UI thread.</summary>
internal sealed record RmlUiAndroidInputSnapshot(long SurfaceGeneration, long InputGeneration,
    RmlUiDocumentToken Document, bool Active, bool HasTextInput, RmlUiTextInputState TextInput);

/// <summary>
/// Bounded UI-thread callbacks for the existing EGL owner. No Android View,
/// native bridge, Scene, network pump, clock, or EGL object is owned here.
/// Coordinates are already framebuffer pixels; density is not applied again.
/// </summary>
internal sealed class RmlUiAndroidInputQueue
{
    private readonly record struct Pending(long SurfaceGeneration, long InputGeneration, RmlUiPlatformInputEvent Input);
    private readonly object _gate = new();
    private readonly Pending[] _pending;
    private int _head, _count, _ownerThread;
    private long _surfaceGeneration, _inputGeneration;
    private bool _surfaceActive, _releasePending, _preserveFocus;
    private int _heldButtons;
    private ulong _heldKeys;
    private bool _composition;
    private RmlUiAndroidInputSnapshot _snapshot = new(0, 0, default, false, false, default);
    private long _dropped, _overflows, _dispatched, _releases;

    internal const int MaximumTextLength = 4096;
    internal RmlUiAndroidInputQueue(int capacity = 256)
    {
        if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        _pending = new Pending[capacity];
    }
    internal RmlUiAndroidInputSnapshot Current => Volatile.Read(ref _snapshot);
    internal (int Pending, long Dropped, long Overflows, long Dispatched, long Releases) Metrics
    { get { lock (_gate) return (_count, _dropped, _overflows, _dispatched, _releases); } }

    /// <summary>
    /// Enqueue copied primitives, never a MotionEvent/KeyEvent/InputConnection.
    /// IME callbacks must retain the snapshot from their InputConnection's focus
    /// lifetime; reading a fresh snapshot for a delayed commit is not valid.
    /// </summary>
    internal bool TryEnqueue(RmlUiAndroidInputSnapshot witness, in RmlUiPlatformInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(witness);
        lock (_gate)
        {
            if (!CurrentWitness(witness) || input.Document != witness.Document
                || !Enum.IsDefined(input.Kind) || !Enum.IsDefined(input.Device)
                || input.Text == null || input.Text.Length > MaximumTextLength)
                return Reject(witness, input);
            bool text = input.Kind is RmlUiPlatformInputKind.TextCommitted
                or RmlUiPlatformInputKind.CompositionBegin or RmlUiPlatformInputKind.CompositionUpdate
                or RmlUiPlatformInputKind.CompositionCommit or RmlUiPlatformInputKind.CompositionCancel
                || input.Device == RmlUiInputDevice.InputMethod;
            if (text && (!witness.HasTextInput || !Current.HasTextInput || input.FocusEpoch == 0
                || input.FocusEpoch != witness.TextInput.FocusEpoch
                || input.FocusEpoch != Current.TextInput.FocusEpoch
                || input.Document != witness.TextInput.Document))
                return Reject(witness, input);
            if (_count == _pending.Length)
            {
                // Discard the whole burst, including this event. A guaranteed
                // owner-thread release replaces any lost pointer/key-up.
                _dropped += _count + 1; _overflows++;
                InvalidatePending(release: true, preserveFocus: false);
                PublishSnapshot(Current.Document, Current.HasTextInput, Current.TextInput);
                return false;
            }
            _pending[(_head + _count) % _pending.Length] = new(_surfaceGeneration, _inputGeneration, input);
            _count++;
            return true;
        }
    }

    private bool CurrentWitness(RmlUiAndroidInputSnapshot witness) => _surfaceActive && witness.Active
        && witness.SurfaceGeneration == _surfaceGeneration && witness.InputGeneration == _inputGeneration
        && witness.Document == Current.Document && witness.Document != default;

    private bool Reject(RmlUiAndroidInputSnapshot witness, in RmlUiPlatformInputEvent input)
    {
        _dropped++;
        if (CurrentWitness(witness) && HasHeld && input.Kind is RmlUiPlatformInputKind.PointerUp
            or RmlUiPlatformInputKind.KeyUp or RmlUiPlatformInputKind.CompositionCommit
            or RmlUiPlatformInputKind.CompositionCancel)
        {
            InvalidatePending(release: true, preserveFocus: true);
            PublishSnapshot(Current.Document, Current.HasTextInput, Current.TextInput);
        }
        return false;
    }

    /// <summary>
    /// May be called by the Activity/UI thread immediately on pause, surface
    /// loss or route retirement. Native release occurs only in Drain/Resume.
    /// </summary>
    internal void Suspend()
    {
        lock (_gate)
        {
            SuspendCore();
        }
    }
    internal (bool Resume, RmlUiAndroidInputSnapshot Scope) SuspendForLayout()
    {
        VerifyOwner();
        lock (_gate)
        {
            bool resume = _surfaceActive;
            SuspendCore();
            return (resume, Current);
        }
    }
    private void SuspendCore()
    {
        if (_surfaceActive) _surfaceGeneration++;
        _surfaceActive = false;
        InvalidatePending(release: true, preserveFocus: false);
        PublishSnapshot(default, false, default);
    }

    /// <summary>Existing EGL owner calls this only when its surface is usable.</summary>
    internal void Resume(RmlUiHost host, RmlUiAndroidInputSnapshot? onlyIfUnchanged = null)
    {
        VerifyOwner();
        ArgumentNullException.ThrowIfNull(host);
        FlushRelease(host);
        lock (_gate)
        {
            if (_surfaceActive || onlyIfUnchanged != null && !ReferenceEquals(Current, onlyIfUnchanged)) return;
            _surfaceActive = true; _surfaceGeneration++;
            InvalidatePending(release: false, preserveFocus: false);
        }
        Refresh(host);
    }

    /// <summary>Publish current document/focus after owner-thread route changes.</summary>
    internal void Refresh(RmlUiHost host)
    {
        VerifyOwner();
        ArgumentNullException.ThrowIfNull(host);
        if (!host.Active)
        {
            Suspend();
            lock (_gate) _releasePending = _preserveFocus = false;
            ClearHeld();
            return;
        }
        RmlUiDocumentToken document = host.CurrentInputDocument;
        bool text = host.TryGetTextInputState(out var textState) && textState.Document == document;
        lock (_gate)
        {
            if (!_surfaceActive) return;
            if (Current.Document != default && Current.Document != document)
                InvalidatePending(release: HasHeld, preserveFocus: true);
            PublishSnapshot(document, text, text ? textState : default);
        }
        FlushRelease(host);
        // FocusLost retires preedit. If route navigation had already focused
        // its new field, FlushRelease restores that field with a fresh epoch.
        text = host.TryGetTextInputState(out textState) && textState.Document == document;
        lock (_gate)
        {
            _composition = text && textState.Composing;
            if (_surfaceActive && host.CurrentInputDocument == document)
                PublishSnapshot(document, text, text ? textState : default);
        }
    }

    /// <summary>Existing EGL owner drains at most one bounded burst per frame.</summary>
    internal void Drain(RmlUiHost host)
    {
        VerifyOwner();
        Refresh(host);
        FlushRelease(host);
        int budget;
        lock (_gate) budget = _count;
        while (budget-- > 0)
        {
            Pending pending;
            lock (_gate)
            {
                if (_count == 0) break;
                pending = _pending[_head]; _pending[_head] = default;
                _head = (_head + 1) % _pending.Length; _count--;
                if (!_surfaceActive || pending.SurfaceGeneration != _surfaceGeneration
                    || pending.InputGeneration != _inputGeneration || pending.Input.Document != Current.Document)
                { _dropped++; continue; }
            }
            if (pending.Input.Document != host.CurrentInputDocument)
            { lock (_gate) _dropped++; Refresh(host); continue; }
            RmlUiInputResult result;
            lock (_gate)
            {
                // Serialize cancellation with the short native dispatch. Once
                // UI-thread Suspend returns, no already-dequeued event can be
                // delivered afterward against the retired surface witness.
                if (!_surfaceActive || pending.SurfaceGeneration != _surfaceGeneration
                    || pending.InputGeneration != _inputGeneration)
                { _dropped++; continue; }
                result = host.Input.Dispatch(pending.Input);
                if (result == RmlUiInputResult.Accepted) TrackHeld(pending.Input);
            }
            if (result == RmlUiInputResult.Accepted)
            {
                lock (_gate) _dispatched++;
            }
            else
            {
                lock (_gate) _dropped++;
                if (HasHeld && pending.Input.Kind is RmlUiPlatformInputKind.PointerUp
                    or RmlUiPlatformInputKind.KeyUp or RmlUiPlatformInputKind.CompositionCommit
                    or RmlUiPlatformInputKind.CompositionCancel)
                {
                    lock (_gate) InvalidatePending(release: true, preserveFocus: true);
                    FlushRelease(host);
                }
            }
        }
    }

    private bool HasHeld => _heldButtons != 0 || _heldKeys != 0 || _composition;
    private void ClearHeld() { lock (_gate) { _heldButtons = 0; _heldKeys = 0; _composition = false; } }
    private void TrackHeld(in RmlUiPlatformInputEvent input)
    {
        switch (input.Kind)
        {
            case RmlUiPlatformInputKind.PointerDown: _heldButtons |= 1 << input.Code; break;
            case RmlUiPlatformInputKind.PointerUp: _heldButtons &= ~(1 << input.Code); break;
            case RmlUiPlatformInputKind.KeyDown: _heldKeys |= 1UL << input.Code; break;
            case RmlUiPlatformInputKind.KeyUp: _heldKeys &= ~(1UL << input.Code); break;
            case RmlUiPlatformInputKind.CompositionBegin: _composition = true; break;
            case RmlUiPlatformInputKind.CompositionCommit:
            case RmlUiPlatformInputKind.CompositionCancel: _composition = false; break;
            case RmlUiPlatformInputKind.FocusLost: ClearHeld(); break;
        }
    }

    private void FlushRelease(RmlUiHost host)
    {
        bool preserve;
        lock (_gate)
        {
            if (!_releasePending) return;
            preserve = _preserveFocus;
            _releasePending = _preserveFocus = false;
        }
        if (host.Active)
        {
            RmlUiDocumentToken document = host.CurrentInputDocument;
            string focus = preserve ? host.FocusedElement() : "";
            host.ReleaseInput();
            if (preserve && focus.Length > 0 && host.CurrentInputDocument == document)
                host.FocusDocument(document, focus);
            lock (_gate) _releases++;
        }
        ClearHeld();
    }

    private void InvalidatePending(bool release, bool preserveFocus)
    {
        Array.Clear(_pending); _head = _count = 0; _inputGeneration++;
        // A suspend/overflow cancellation must not be weakened by a later
        // route refresh which would otherwise restore an abandoned IME field.
        _preserveFocus = _releasePending ? _preserveFocus && preserveFocus : preserveFocus;
        _releasePending |= release;
    }
    private void PublishSnapshot(RmlUiDocumentToken document, bool text, RmlUiTextInputState textState)
    {
        var previous = Current;
        if (previous.SurfaceGeneration == _surfaceGeneration && previous.InputGeneration == _inputGeneration
            && previous.Document == document && previous.Active == _surfaceActive
            && previous.HasTextInput == text && previous.TextInput == textState) return;
        Volatile.Write(ref _snapshot, new(_surfaceGeneration, _inputGeneration, document,
            _surfaceActive, text, textState));
    }
    private void VerifyOwner()
    {
        int current = Environment.CurrentManagedThreadId;
        if (_ownerThread == 0) _ownerThread = current;
        else if (_ownerThread != current)
            throw new InvalidOperationException("RmlUi Android input must be drained/published on the existing EGL owner thread.");
    }
}
#endif
