using System;
using System.IO;
using System.Threading.Tasks;

namespace MphRead.Mods.Replay;

/// <summary>Consumes progress without retaining lines; the caller owns the reader and process.</summary>
internal static class ReplayEncoderProgress
{
    internal static async Task Read(StreamReader reader, Action<int> publish)
    {
        char[] buffer = new char[2048];
        var line = new FrameLine();
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            for (int i = 0; i < count; i++)
            {
                char value = buffer[i];
                if (value is '\r' or '\n')
                {
                    line.Publish(publish);
                    line = default;
                }
                else line.Add(value);
            }
        line.Publish(publish); // ReadLine also accepts an unterminated final line.
    }

    private struct FrameLine
    {
        private const string Prefix = "frame=";
        private enum Phase { Leading, Signed, Digits, Trailing, Nulls }
        private int _prefix;
        private Phase _phase;
        private uint _magnitude;
        private bool _negative, _digits, _invalid;

        internal void Add(char value)
        {
            if (_invalid) return;
            if (_prefix < Prefix.Length)
            {
                _invalid = value != Prefix[_prefix++];
                return;
            }
            // These are NumberStyles.Integer / InvariantCulture's whitespace
            // and signs. Do not substitute Unicode whitespace/digit classes.
            bool white = value is ' ' or >= '\t' and <= '\r';
            if (_phase == Phase.Leading)
            {
                if (white) return;
                if (value is '+' or '-')
                {
                    _negative = value == '-';
                    _phase = Phase.Signed;
                    return;
                }
            }
            if (_phase is Phase.Leading or Phase.Signed or Phase.Digits)
            {
                if (value is >= '0' and <= '9')
                {
                    uint digit = (uint)(value - '0');
                    uint limit = _negative ? 2147483648u : int.MaxValue;
                    if (_magnitude > (limit - digit) / 10) _invalid = true;
                    else _magnitude = _magnitude * 10 + digit;
                    _digits = true;
                    _phase = Phase.Digits;
                    return;
                }
                if (!_digits) { _invalid = true; return; }
                _phase = Phase.Trailing;
            }
            if (_phase == Phase.Trailing && white) return;
            // Integer parsing permits trailing NULs after digits/whitespace,
            // but permits neither whitespace nor digits after the first NUL.
            if (value == '\0') _phase = Phase.Nulls;
            else _invalid = true;
        }

        internal readonly void Publish(Action<int> publish)
        {
            if (!_invalid && _prefix == Prefix.Length && _digits)
                publish(_negative ? unchecked(-(int)_magnitude) : (int)_magnitude);
        }
    }
}
