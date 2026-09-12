using System;
using System.Runtime.CompilerServices;

namespace MphRead
{
    /// <summary>
    /// Presentation-owned identities for pooled sources which have no authored
    /// id. Allocation follows the presentation's deterministic traversal order
    /// within one room or replay-timeline scope. It does not claim cross-run
    /// object identity.
    /// </summary>
    public sealed class VisualLightIdentityAllocator
    {
        private sealed class Entry
        {
            public ulong Scope;
            public uint Identity;
        }

        private readonly ConditionalWeakTable<object, Entry> _entries = new();
        private ulong _scope = 1;
        private uint _nextIdentity = 1;

        public ulong Scope => _scope;

        public ulong GetSourceKey(VisualLightSourceKind kind, object source,
            uint generation = 0)
        {
            ArgumentNullException.ThrowIfNull(source);
            Entry entry = _entries.GetValue(source, static _ => new Entry());
            if (entry.Scope != _scope)
            {
                if (_nextIdentity == 0)
                {
                    throw new InvalidOperationException(
                        "Visual light identity scope exhausted its source ids.");
                }
                entry.Scope = _scope;
                entry.Identity = _nextIdentity++;
            }
            return VisualLightSourceKey.ForPresentation(kind, _scope,
                entry.Identity, generation);
        }

        /// <summary>Starts a new room or replay-timeline identity scope.</summary>
        public void ResetScope()
        {
            _scope = checked(_scope + 1);
            _nextIdentity = 1;
        }
    }
}
