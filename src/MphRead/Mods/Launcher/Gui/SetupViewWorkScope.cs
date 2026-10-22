using System;
using System.Collections.Generic;
using System.Threading;
namespace MphRead.Mods.Launcher.Gui;

// Per-view callback retirement only. Extraction remains the GameFiles owner.
internal sealed class SetupViewWorkScope
{
    internal readonly record struct Ticket(long Epoch, CancellationToken Token);
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private CancellationTokenSource _cancel = new();
    private long _epoch;
    private bool _retired, _closed;
    private void Verify() { if (Environment.CurrentManagedThreadId != _owner) throw new InvalidOperationException("Setup view requires its UI owner."); }
    internal void Resume() { Verify(); if (_closed || !_retired) return; _cancel.Dispose(); _cancel = new(); _retired = false; _epoch++; }
    internal Ticket Begin() { Verify(); if (_closed || _retired) return new(_epoch, new CancellationToken(true)); _cancel.Cancel(); _cancel.Dispose(); _cancel = new(); return new(++_epoch, _cancel.Token); }
    internal bool Current(Ticket ticket) { Verify(); return !_closed && !_retired && ticket.Epoch == _epoch && !ticket.Token.IsCancellationRequested; }
    internal void Retire() { Verify(); if (_retired) return; _retired = true; _epoch++; _cancel.Cancel(); }
    internal bool TryClose() { Verify(); if (_closed) return false; _closed = true; Retire(); return true; }
}
internal sealed class SetupViewProgress
{
    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private readonly SetupProgress _progress = new();
    private bool _posted;
    internal bool Report(string line)
    {
        lock (_gate)
        {
            string text = (line ?? "").Replace('\r', ' '); if (text.Length > 1024) text = text[..1024];
            if (_lines.Count == 8) _lines.Dequeue(); _lines.Enqueue(text); _progress.Observe(text);
            if (_posted) return false; _posted = true; return true;
        }
    }
    internal (string[] Lines, double Fraction, string Stage) Drain()
    {
        lock (_gate) { var result = (_lines.ToArray(), _progress.Fraction, _progress.Stage); _lines.Clear(); _posted = false; return result; }
    }
}
