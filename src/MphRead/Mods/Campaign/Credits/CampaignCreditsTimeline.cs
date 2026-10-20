using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Campaign;

/// <summary>Explicit 30 Hz ending timeline. No clock, IO, callbacks, save, audio or rendering ownership.</summary>
public sealed class CampaignCreditsTimeline : IDisposable
{
    public const int MaxOperationsPerStep = 4096, MaxNestingDepth = 64;
    public sealed record ItemSnapshot(int Index, int State, int Code, long Frame, long DelayFx, bool Started,
        CampaignCreditsSource.State? Visual);
    public sealed record TimelineSnapshot(int Page, int? PendingPage, long PageFrame, long SettledFrame,
        bool Closed, string? Failure, IReadOnlyList<ItemSnapshot> Items);
    private sealed class ItemRuntime
    {
        internal int State = 1, Code = 1;
        internal long Frame, DelayFx;
        internal bool Started;
    }
    private sealed class StepFailure : Exception
    {
        internal StepFailure(string message) : base(message) { }
    }
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly CampaignCreditsFrameTable _frames;
    private readonly Dictionary<int, CampaignCreditsSource.Page> _pages = new();
    private readonly bool _goodEnding;
    private CampaignCreditsSource.Page _page = null!;
    private ItemRuntime[] _items = Array.Empty<ItemRuntime>();
    private int? _pending;
    private long _pageFrame, _settledFrame = -1;
    private int _exitFrames, _operations, _depth;
    private bool _closed, _pendingReturn;
    private string? _failure;

    public CampaignCreditsTimeline(CampaignCreditsSource.Document document, CampaignCreditsFrameTable frames, bool goodEnding)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(frames);
        if (document.MarmSha256 != frames.MarmSha256 || document.TextSha256 != frames.TextSha256
            || document.Widgets.Count != frames.Frames.Count) throw new InvalidDataException("Credits frame table belongs to another source document.");
        _frames = frames; _goodEnding = goodEnding;
        foreach (var page in document.Pages) _pages.Add(page.Index, page);
        foreach (var page in document.Pages)
        {
            foreach (var item in page.Items)
            {
                Duration(item.DelayFx);
                foreach (var state in item.States)
                {
                    if (state.Text != null) Duration(state.Text.DurationFx);
                    if (state.WidgetId is int id && !frames.Frames.ContainsKey(id))
                        throw new InvalidDataException("Credits widget has no admitted duration.");
                }
                foreach (var action in item.Actions) ValidateAction(action);
            }
            foreach (var action in page.Actions) ValidateAction(action);
            foreach (var timer in page.Timers) { Duration(timer.DelayFx); ValidateAction(timer.Action); }
        }
        try { Enter(6); }
        catch (StepFailure e) { Fail(e.Message); }
    }
    private static void Duration(int fx)
    {
        if (fx < 0 || fx > CampaignCreditsAnimationDuration.MaxFrames * 4096)
            throw new InvalidDataException("Credits timeline duration exceeds its supported extent.");
    }
    private void ValidateAction(CampaignCreditsSource.Action action)
    {
        if (action.TargetPage != 255 && !_pages.ContainsKey(action.TargetPage))
            throw new InvalidDataException("Credits action leaves the admitted ending pages.");
    }
    public TimelineSnapshot Snapshot()
    {
        VerifyOwner();
        var items = new ItemSnapshot[_items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            var rt = _items[i];
            items[i] = new(i, rt.State, rt.Code, rt.Frame, rt.DelayFx, rt.Started, rt.Started ? Visual(i, rt.Code) : null);
        }
        return new(_page.Index, _pending, _pageFrame, _settledFrame, _closed, _failure, Array.AsReadOnly(items));
    }
    public bool Tick()
    {
        VerifyOwner(); if (_closed) return false;
        _operations = _depth = 0;
        try { TickCore(); }
        catch (StepFailure e) { Fail(e.Message); }
        return !_closed;
    }
    private void TickCore()
    {
        _pageFrame = Advance(_pageFrame);
        for (int i = 0; i < _items.Length && !_closed; i++)
        {
            Spend(); var rt = _items[i];
            if (!rt.Started) { rt.DelayFx -= 4096; continue; }
            rt.Frame = Advance(rt.Frame);
            if (rt.Code >= 8 && rt.Frame >= CodeLength(i, rt.Code)) Arrive(i);
        }
        if (_closed) return;
        if (_pending == null)
        {
            StartItems(); if (_closed) return;
            if (_settledFrame >= 0) _settledFrame = Advance(_settledFrame);
            else if (Settled()) _settledFrame = 0;
            foreach (var timer in _page.Timers)
            {
                Spend();
                if (_settledFrame == Ceiling(timer.DelayFx)) Run(timer.Action);
                if (_closed) break;
            }
        }
        else
        {
            _exitFrames++;
            bool done = true;
            foreach (var rt in _items) if (rt.Code >= 8) done = false;
            if (done) Enter(_pending.Value);
            else if (_exitFrames > 120) throw new StepFailure("Credits page exit exceeded its original 120-tick grace period.");
        }
    }
    private bool Settled()
    {
        foreach (var rt in _items) if (!rt.Started || rt.Code >= 8) return false;
        return true;
    }
    private void Enter(int page)
    {
        Nest();
        try
        {
            var destination = _pages[page];
            var items = new ItemRuntime[destination.Items.Count];
            for (int i = 0; i < items.Length; i++) { Spend(); items[i] = new() { DelayFx = destination.Items[i].DelayFx }; }
            _page = destination; _items = items; _pending = null;
            _pageFrame = 0; _settledFrame = -1; _exitFrames = 0;
            StartItems();
        }
        finally { _depth--; }
    }
    private void StartItems()
    {
        for (int i = 0; i < _items.Length && !_closed; i++)
        {
            Spend(); var rt = _items[i];
            if (!rt.Started && rt.DelayFx <= 0) { rt.Started = true; SetState(i, _page.Items[i].InitialState); }
        }
    }
    private void GoTo(int page)
    {
        Nest();
        try
        {
            _pending = page; _exitFrames = 0;
            for (int i = 0; i < _items.Length && !_closed; i++) SetState(i, 1);
        }
        finally { _depth--; }
    }
    private void SetState(int item, int target)
    {
        Nest();
        try
        {
            var rt = _items[item];
            if (!rt.Started) { rt.Started = true; rt.DelayFx = 0; }
            int from = rt.State;
            if (from == target && rt.Code < 8) return;
            int code = (target << 3) | from;
            if (Visual(item, code) == null && Visual(item, (target << 3) | 7) != null) code = (target << 3) | 7;
            rt.State = target; rt.Frame = 0;
            if (Visual(item, code) != null && CodeLength(item, code) > 0) rt.Code = code;
            else Arrive(item);
        }
        finally { _depth--; }
    }
    private void Arrive(int item)
    {
        Nest();
        try
        {
            var rt = _items[item]; int played = rt.Code, arrived = rt.State;
            rt.Code = arrived; rt.Frame = 0;
            foreach (var action in _page.Items[item].Actions)
            {
                Spend();
                if (action.Kind == 0 && (action.ArrivalCode == arrived || action.ArrivalCode == played)) Run(action);
                if (_closed) break;
            }
        }
        finally { _depth--; }
    }
    private void Run(CampaignCreditsSource.Action action)
    {
        Nest();
        try
        {
            if (action.TargetCode != 0) SetState(action.TargetItem, action.TargetCode >= 8 ? action.TargetCode >> 3 : action.TargetCode);
            if (_closed) return;
            foreach (var call in action.Calls)
            {
                Spend();
                if (call.B == 12) GoTo(_goodEnding ? 49 : 50);
                else if (call.B == 206)
                {
                    if (_page.Index is not (49 or 50)) throw new StepFailure("Credits return was requested outside results.");
                    _closed = _pendingReturn = true; _pending = null; return;
                }
                // Call 11 cannot skip credits after an ending. No executable callback is dispatched.
                if (_closed) return;
            }
            if (action.TargetPage != 255 && !_closed) GoTo(action.TargetPage);
        }
        finally { _depth--; }
    }
    private CampaignCreditsSource.State? Visual(int item, int code)
    {
        foreach (var state in _page.Items[item].States) if (state.Code == code) return state;
        return null;
    }
    private int CodeLength(int item, int code)
    {
        var visual = Visual(item, code);
        if (visual?.Text is { } text) return Ceiling(text.DurationFx);
        return visual?.WidgetId is int id ? _frames.Frames[id] : 0;
    }
    private static int Ceiling(int fx) => (int)(((long)fx + 4095) / 4096);
    private static long Advance(long value) => value == long.MaxValue
        ? throw new StepFailure("Credits timeline counter reached its supported lifetime.") : value + 1;

    /// <summary>An explicit results item intent; does not map global keys or allow credits skipping.</summary>
    public bool InvokeItemAction(int page, int item, int action)
    {
        VerifyOwner();
        if (_closed || _pending != null || _page.Index != page || page is not (49 or 50)
            || (uint)item >= (uint)_items.Length || !_items[item].Started || _items[item].State == 1
            || (uint)action >= (uint)_page.Items[item].Actions.Count || _page.Items[item].Actions[action].Kind != 1) return false;
        _operations = _depth = 0;
        try { Run(_page.Items[item].Actions[action]); }
        catch (StepFailure e) { Fail(e.Message); }
        return _failure == null;
    }
    public bool TryTakeReturn()
    {
        VerifyOwner(); if (!_pendingReturn) return false;
        _pendingReturn = false; return true;
    }
    public void Retire() { VerifyOwner(); _closed = true; _pendingReturn = false; _pending = null; }
    public void Dispose() => Retire();
    private void Fail(string message) { _failure = message; _closed = true; _pendingReturn = false; _pending = null; }
    private void Spend() { if (++_operations > MaxOperationsPerStep) throw new StepFailure("Credits timeline operation budget exceeded."); }
    private void Nest()
    {
        Spend(); if (++_depth > MaxNestingDepth) throw new StepFailure("Credits timeline nesting budget exceeded.");
    }
    private void VerifyOwner()
    {
        if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Credits timeline belongs to its creating thread.");
    }
}
