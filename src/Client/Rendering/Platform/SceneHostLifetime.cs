using System;
using System.Runtime.CompilerServices;

namespace MphRead
{
    /// <summary>Saved preferences change a reused window; manual toggles survive unchanged preferences.</summary>
    internal sealed class SceneWindowModePreference
    {
        private Mods.WindowStartMode? _lastApplied;
        public void ApplyIfChanged(Mods.WindowStartMode preference, Action<Mods.WindowStartMode> apply)
        {
            if (_lastApplied == preference) return;
            apply(preference);
            _lastApplied = preference;
        }
    }

    /// <summary>Same-thread ownership of sequential scenes inside one native host.</summary>
    internal sealed class SceneHostLifetime : IDisposable
    {
        private readonly ConditionalWeakTable<object, object> _usedScenes = new();
        private bool _active;
        private bool _disposed;
        public bool CloseRequested { get; private set; }
        public bool SceneStopRequested { get; private set; }

        public void Run(object scene, Action run, Action beforeCleanup, Action cleanup)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (CloseRequested || _active || _usedScenes.TryGetValue(scene, out _))
                throw new InvalidOperationException("The host cannot run a closed, active, or previously consumed scene.");
            _usedScenes.Add(scene, new object());
            _active = true;
            SceneStopRequested = false;
            try { run(); }
            finally
            {
                try { beforeCleanup(); }
                finally
                {
                    try { cleanup(); }
                    finally { _active = false; }
                }
            }
        }

        public void StopScene() => SceneStopRequested = true;
        public void Close() { CloseRequested = true; StopScene(); }
        public void Dispose()
        {
            if (_active) throw new InvalidOperationException("Finish the scene before disposing its host.");
            _disposed = true;
            Close();
        }
    }
}
