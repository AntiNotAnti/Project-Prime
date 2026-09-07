using System;
using System.Threading;

namespace MphRead.Mods.Network
{
    public enum SessionPumpOwner
    {
        None,
        Shell,
        Match
    }

    /// <summary>
    /// Serializes session polling and records whether the shell or match loop owns it.
    /// Ownership changes are explicit so a still-running UI timer cannot race the game loop.
    /// </summary>
    public sealed class SessionPump
    {
        private readonly object _gate = new();
        private SessionPumpOwner _owner;
        private bool _polling;

        public SessionPumpOwner Owner
        {
            get { lock (_gate) { return _owner; } }
        }

        public bool TryAcquire(SessionPumpOwner owner)
        {
            if (owner == SessionPumpOwner.None) throw new ArgumentOutOfRangeException(nameof(owner));
            lock (_gate)
            {
                if (_polling || _owner != SessionPumpOwner.None && _owner != owner) return false;
                _owner = owner;
                return true;
            }
        }

        public bool Transfer(SessionPumpOwner from, SessionPumpOwner to)
        {
            if (to == SessionPumpOwner.None) throw new ArgumentOutOfRangeException(nameof(to));
            lock (_gate)
            {
                if (_polling || _owner != from) return false;
                _owner = to;
                return true;
            }
        }

        public bool Release(SessionPumpOwner owner)
        {
            if (owner == SessionPumpOwner.None) return false;
            lock (_gate)
            {
                if (_polling || _owner != owner) return false;
                _owner = SessionPumpOwner.None;
                return true;
            }
        }

        public bool TryPoll(SessionPumpOwner owner, Action poll)
        {
            ArgumentNullException.ThrowIfNull(poll);
            lock (_gate)
            {
                if (_polling || _owner != owner) return false;
                _polling = true;
            }
            try
            {
                poll();
                return true;
            }
            finally
            {
                lock (_gate)
                {
                    _polling = false;
                    Monitor.PulseAll(_gate);
                }
            }
        }

        /// <summary>Wait for an in-flight poll, revoke ownership, then run teardown exclusively.</summary>
        public void Stop(Action teardown)
        {
            ArgumentNullException.ThrowIfNull(teardown);
            lock (_gate)
            {
                while (_polling) Monitor.Wait(_gate);
                _owner = SessionPumpOwner.None;
                _polling = true;
            }
            try
            {
                teardown();
            }
            finally
            {
                lock (_gate)
                {
                    _polling = false;
                    Monitor.PulseAll(_gate);
                }
            }
        }
    }
}
