using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Bounded input playout. At most one command is applied per server tick,
    /// regardless of client packet rate. A short queue absorbs jitter; a gap
    /// briefly holds controls without repeating edges, then skips to new data.
    /// </summary>
    public sealed class ServerInputStream
    {
        private const int Capacity = 16;
        private const uint HoldTicks = 6;
        private readonly InputCommand[] _commands = new InputCommand[Capacity];
        private readonly byte[] _rewindPresentationDelays = new byte[Capacity];
        private readonly bool[] _present = new bool[Capacity];
        private bool _started;
        private uint _next;
        private uint _lastArrival;
        private int _startup;
        private int _gap;
        private byte _playoutTicks = NetworkTimingProfile.Compatibility.InputPlayoutTicks;
        private byte _lastRewindPresentationDelay = NetworkTimingProfile.Compatibility.PresentationDelayTicks;
        private int _bufferedCommands;
        private InputCommand _last = new(0, 0, 0, InputButtons.None, InputButtons.None,
            -Vector3.UnitZ, InputCommand.NoWeapon);

        public bool HasProcessed { get; private set; }
        public uint LastProcessed { get; private set; }
        public long Duplicates { get; private set; }
        public long LateCommands { get; private set; }
        public long SkippedCommands { get; private set; }
        public long StarvedTicks { get; private set; }
        public int BufferedCommands => _bufferedCommands;
        public int MaximumBufferedCommands { get; private set; }
        /// <summary>Startup/gap tolerance; this is not a continuously forced queue depth.</summary>
        public byte InputPlayoutTicks => _playoutTicks;

        public void ConfigurePlayout(byte ticks)
        {
            if (ticks is < NetworkTimingProfile.MinimumInputPlayoutTicks
                or > NetworkTimingProfile.MaximumInputPlayoutTicks)
                throw new ArgumentOutOfRangeException(nameof(ticks));
            _playoutTicks = ticks;
        }

        public void Receive(ReadOnlySpan<InputCommand> commands, uint serverTick)
            => Receive(commands, serverTick, NetworkTimingProfile.Compatibility.PresentationDelayTicks);

        public void Receive(ReadOnlySpan<InputCommand> commands, uint serverTick,
            byte rewindPresentationDelayTicks)
        {
            if (rewindPresentationDelayTicks is < NetworkTimingProfile.MinimumPresentationDelayTicks
                or > NetworkTimingProfile.MaximumPresentationDelayTicks)
                throw new ArgumentOutOfRangeException(nameof(rewindPresentationDelayTicks));
            if (commands.IsEmpty)
            {
                return;
            }
            uint newest = commands[^1].Sequence;
            if (!_started)
            {
                _started = true;
                _next = commands[0].Sequence;
                _startup = _playoutTicks;
            }
            if (newest != _next && !Sequence32.IsNewer(newest, _next))
            {
                LateCommands += commands.Length;
                return;
            }
            uint ahead = unchecked(newest - _next);
            if (ahead >= InputBundle.Capacity)
            {
                // A long loss burst or client stall must not leave a peer
                // permanently waiting for commands no bundle still contains.
                uint next = unchecked(newest - (InputBundle.Capacity - 1));
                SkippedCommands += unchecked(next - _next);
                _next = next;
                for (int i = 0; i < Capacity; i++)
                {
                    if (_present[i] && !Sequence32.IsNewer(_commands[i].Sequence, next)
                        && _commands[i].Sequence != next)
                    {
                        _present[i] = false;
                        _bufferedCommands--;
                    }
                }
            }
            bool receivedNew = false;
            foreach (InputCommand command in commands)
            {
                if (command.Sequence != _next && !Sequence32.IsNewer(command.Sequence, _next))
                {
                    LateCommands++;
                    continue;
                }
                int index = (int)(command.Sequence % Capacity);
                if (_present[index] && _commands[index].Sequence == command.Sequence)
                {
                    Duplicates++;
                    continue;
                }
                bool occupied = _present[index];
                _commands[index] = command;
                _rewindPresentationDelays[index] = rewindPresentationDelayTicks;
                _present[index] = true;
                if (!occupied)
                {
                    _bufferedCommands++;
                    MaximumBufferedCommands = Math.Max(MaximumBufferedCommands, _bufferedCommands);
                }
                receivedNew = true;
            }
            if (receivedNew)
            {
                _lastArrival = serverTick;
            }
        }

        public InputCommand Take(uint serverTick)
            => Take(serverTick, out _);

        public InputCommand Take(uint serverTick, out byte rewindPresentationDelayTicks)
        {
            if (!_started || _startup-- > 0)
            {
                rewindPresentationDelayTicks = _lastRewindPresentationDelay;
                return _last.Neutral();
            }
            _startup = 0;
            int index = (int)(_next % Capacity);
            if (!_present[index] || _commands[index].Sequence != _next)
            {
                _gap = Math.Min(_gap + 1, 3);
                if (_gap > _playoutTicks)
                {
                    for (uint offset = 1; offset < Capacity; offset++)
                    {
                        uint candidate = unchecked(_next + offset);
                        int candidateIndex = (int)(candidate % Capacity);
                        if (_present[candidateIndex] && _commands[candidateIndex].Sequence == candidate)
                        {
                            SkippedCommands += offset;
                            _next = candidate;
                            index = candidateIndex;
                            break;
                        }
                    }
                }
                if (!_present[index] || _commands[index].Sequence != _next)
                {
                    if (unchecked(serverTick - _lastArrival) > HoldTicks)
                    {
                        StarvedTicks++;
                        rewindPresentationDelayTicks = _lastRewindPresentationDelay;
                        return _last.Neutral();
                    }
                    rewindPresentationDelayTicks = _lastRewindPresentationDelay;
                    return _last.WithoutEdges();
                }
            }
            _present[index] = false;
            _bufferedCommands--;
            _last = _commands[index];
            _lastRewindPresentationDelay = _rewindPresentationDelays[index];
            rewindPresentationDelayTicks = _lastRewindPresentationDelay;
            LastProcessed = _next++;
            HasProcessed = true;
            _gap = 0;
            return _last;
        }
    }
}
