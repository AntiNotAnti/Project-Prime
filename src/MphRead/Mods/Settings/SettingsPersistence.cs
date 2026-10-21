using System;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace MphRead.Mods.Settings;

/// <summary>Serialize preference writes with import/reset; old process values cannot overwrite a new disk generation.</summary>
internal static class SettingsPersistence
{
    internal static readonly object Gate = new();
    private static volatile bool _restartRequired;
    internal static bool RestartRequired { get => _restartRequired; private set => _restartRequired = value; }
    [ThreadStatic] private static CommitLease? _commit;
    internal static bool Strict => _commit != null;
    internal static CommitLease BeginCommit() => new();
    internal static void Track(string path)
    {
        if (Directory.Exists(path)) throw new IOException("Preference path is a directory: " + path);
        if (_commit is { } commit && !commit.Before.ContainsKey(path))
            commit.Before[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
    internal static void WriteText(string path, string text)
    {
        Track(path);
        WriteBytes(path, Encoding.UTF8.GetBytes(text));
    }
    internal static void WriteLines(string path, IEnumerable<string> lines)
        => WriteText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    internal static void WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal sealed class CommitLease : IDisposable
    {
        internal readonly Dictionary<string, byte[]?> Before = new();
        private bool _completed;
        internal CommitLease()
        {
            Monitor.Enter(Gate);
            if (_commit != null) { Monitor.Exit(Gate); throw new InvalidOperationException("Settings commit already active."); }
            _commit = this;
        }
        internal void Complete() => _completed = true;
        public void Dispose()
        {
            _commit = null;
            var failures = new List<Exception>();
            try
            {
                if (!_completed)
                    foreach (var entry in Before)
                        try
                        {
                            if (entry.Value == null) File.Delete(entry.Key);
                            else if (!File.Exists(entry.Key) || !File.ReadAllBytes(entry.Key).AsSpan().SequenceEqual(entry.Value))
                                WriteBytes(entry.Key, entry.Value);
                        }
                        catch (Exception error) { failures.Add(error); }
            }
            finally { Monitor.Exit(Gate); }
            if (failures.Count > 0) throw new AggregateException("Settings save failed and rollback was incomplete.", failures);
        }
    }
    internal static IDisposable? BeginWrite()
    {
        Monitor.Enter(Gate);
        if (RestartRequired) { Monitor.Exit(Gate); return null; }
        return new WriteLease();
    }
    internal static void Replace(Action install)
    {
        lock (Gate)
        {
            bool previous = RestartRequired;
            RestartRequired = true;
            try { install(); }
            catch (AggregateException) { throw; } // Incomplete rollback: keep all old writers fenced off.
            catch { RestartRequired = previous; throw; }
        }
    }
    private sealed class WriteLease : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (!_disposed) { _disposed = true; Monitor.Exit(Gate); } }
    }
}
