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
        private uint _inputEpoch;

        public bool HasProcessed { get; private set; }
        public uint LastProcessed { get; private set; }
        public long Duplicates { get; private set; }
        public long LateCommands { get; private set; }
        public long SkippedCommands { get; private set; }
        public long StarvedTicks { get; private set; }
        public long StaleEpochCommands { get; private set; }
        public int BufferedCommands => _bufferedCommands;
        public int MaximumBufferedCommands { get; private set; }
        public uint InputEpoch => _inputEpoch;
        /// <summary>Startup/gap tolerance; this is not a continuously forced queue depth.</summary>
        public byte InputPlayoutTicks => _playoutTicks;

        public void SetInputEpoch(uint inputEpoch, uint serverTick = 0)
            => SetInputEpoch(inputEpoch, serverTick, -Vector3.UnitZ);

        public void SetInputEpoch(uint inputEpoch, uint serverTick, Vector3 neutralAim)
        {
            if (inputEpoch == 0) throw new ArgumentOutOfRangeException(nameof(inputEpoch));
            float aimLengthSquared = neutralAim.LengthSquared;
            if (!Single.IsFinite(aimLengthSquared) || aimLengthSquared <= 0.0001f)
                throw new ArgumentOutOfRangeException(nameof(neutralAim));
            if (_inputEpoch == inputEpoch) return;
            neutralAim /= MathF.Sqrt(aimLengthSquared);
            _inputEpoch = inputEpoch;
            for (int i = 0; i < Capacity; i++)
            {
                if (_present[i] && _commands[i].InputEpoch != inputEpoch)
                {
                    _present[i] = false;
                    _bufferedCommands--;
                    StaleEpochCommands++;
                }
            }
            _gap = 0;
            _startup = 0;
            // The first fallback after a spawn is still a valid current-life
            // command, but it must not carry the previous life’s view/tick
            // metadata into combat attribution or lag-compensation requests.
            // Keep the next expected client sequence for ordering diagnostics,
            // preserve the authoritative spawn aim until current-life input
            // arrives, and stamp both ticks at the authoritative boundary.
            _last = new InputCommand(_next, serverTick, serverTick,
                InputButtons.None, InputButtons.None, neutralAim,
                InputCommand.NoWeapon, inputEpoch);
        }

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

            // Direct callers used by deterministic fixtures may not configure
            // the first epoch separately. Once the server has an authoritative
            // spawn epoch, every later command is checked before sequence
            // playout so an old-life retransmission cannot become input.
            if (_inputEpoch == 0)
            {
                for (int i = 0; i < commands.Length; i++)
                {
                    if (commands[i].InputEpoch != 0)
                    {
                        SetInputEpoch(commands[i].InputEpoch);
                        break;
                    }
                }
            }
            if (_inputEpoch == 0)
            {
                StaleEpochCommands += commands.Length;
                return;
            }
            int firstIndex = -1;
            int newestIndex = -1;
            for (int i = 0; i < commands.Length; i++)
            {
                if (commands[i].InputEpoch == _inputEpoch)
                {
                    if (firstIndex < 0) firstIndex = i;
                    newestIndex = i;
                }
                else
                {
                    StaleEpochCommands++;
                }
            }
            if (newestIndex < 0)
            {
                return;
            }
            uint newest = commands[newestIndex].Sequence;
            if (!_started)
            {
                _started = true;
                _next = commands[firstIndex].Sequence;
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
                if (command.InputEpoch != _inputEpoch)
                {
                    continue;
                }
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
            if (_present[index] && _commands[index].InputEpoch != _inputEpoch)
            {
                _present[index] = false;
                _bufferedCommands--;
                StaleEpochCommands++;
            }
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
                        return StarvedFallback();
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

        private InputCommand StarvedFallback()
        {
            InputCommand neutral = _last.Neutral();
            // A missing packet is not evidence that the trigger was released.
            // Preserve only the held fire level; movement and every edge stay
            // neutral, and a later redundant command supplies the real release.
            return neutral with
            {
                Buttons = neutral.Buttons
                    | (_last.Buttons & InputButtons.Shoot)
            };
        }
    }
}
