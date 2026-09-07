using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// One entry in the server's map cycle.
    /// </summary>
    public sealed class RotationEntry
    {
        public string RoomKey { get; init; } = "MP3 PROVING GROUND";
        public GameMode Mode { get; init; } = GameMode.Battle;
        /// <summary>Match length in seconds. Zero means "no time limit".</summary>
        public float TimeLimit { get; init; } = 7 * 60;
        public int PointGoal { get; init; } = 7;
        /// <summary>Seconds held for Defender/Prime Hunter; null uses the mode default.</summary>
        public float? ObjectiveTimeGoal { get; init; }

        public MatchRules ToMatchRules(int maxPlayers = 8, bool friendlyFire = false)
        {
            if (!Single.IsFinite(TimeLimit) || TimeLimit < 0 || TimeLimit >= TimeSpan.MaxValue.TotalSeconds
                || ObjectiveTimeGoal is float goal && (!Single.IsFinite(goal) || goal < 0 || goal >= TimeSpan.MaxValue.TotalSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(TimeLimit), "Match durations must be finite, nonnegative seconds.");
            }
            MatchRules defaults = MatchRules.CreateDefault(Mode.ToMatchMode(), RoomKey, maxPlayers);
            MatchRules rules = defaults.With(timeLimit: TimeLimit > 0 ? TimeSpan.FromSeconds(TimeLimit) : null,
                clearTimeLimit: TimeLimit == 0,
                scoreGoal: defaults.IsSurvival ? 0 : PointGoal,
                startingLives: defaults.IsSurvival ? PointGoal : 0,
                objectiveTimeGoal: ObjectiveTimeGoal.HasValue ? TimeSpan.FromSeconds(ObjectiveTimeGoal.Value) : null,
                friendlyFire: friendlyFire);
            MatchLifecycle.ValidateRules(rules);
            return rules;
        }

        public override string ToString()
        {
            return $"{RoomKey} ({Mode}, {TimeLimit / 60:0.#} min, {PointGoal} pts, objective {ObjectiveTimeGoal?.ToString(CultureInfo.InvariantCulture) ?? "default"} sec)";
        }
    }

    /// <summary>
    /// Server-side map cycle, in the shape Quake 3 admins expect: a plain
    /// text file listing maps in order, the server advancing to the next one
    /// when the time limit or point goal is reached, and wrapping at the end.
    ///
    /// Deliberately a file rather than compiled-in defaults -- a dedicated
    /// server is usually reconfigured by someone with shell access and no
    /// build toolchain.
    /// </summary>
    public sealed class MapRotation
    {
        private readonly List<RotationEntry> _entries = new();
        private int _index;

        public IReadOnlyList<RotationEntry> Entries => _entries;
        public RotationEntry Current => _entries.Count > 0 ? _entries[_index] : _fallback;
        public RotationEntry Next => _entries.Count > 0
            ? _entries[(_index + 1) % _entries.Count]
            : _fallback;
        public int Index => _index;

        private static readonly RotationEntry _fallback = new();

        /// <summary>
        /// A rotation of exactly one match, for a player hosting from the
        /// launcher: they picked a map, and a rotation file they have never
        /// heard of should not send them somewhere else after seven minutes.
        /// </summary>
        public static MapRotation SingleMatch(string roomKey, GameMode mode, float timeLimit, int pointGoal,
            float? objectiveTimeGoal = null)
        {
            var rotation = new MapRotation();
            rotation._entries.Add(new RotationEntry
            {
                RoomKey = roomKey,
                Mode = mode == GameMode.None ? GameMode.Battle : mode,
                TimeLimit = timeLimit,
                PointGoal = pointGoal,
                ObjectiveTimeGoal = objectiveTimeGoal
            });
            return rotation;
        }

        /// <summary>Select an already admitted rotation entry by its server-owned index.</summary>
        public RotationEntry Select(int index)
        {
            if (index < 0 || index >= _entries.Count) throw new ArgumentOutOfRangeException(nameof(index));
            _index = index; return Current;
        }

        /// <summary>Advance to the next map, wrapping at the end of the cycle.</summary>
        public RotationEntry Advance()
        {
            if (_entries.Count > 0)
            {
                _index = (_index + 1) % _entries.Count;
            }
            return Current;
        }

        /// <summary>
        /// Load a rotation file. Format, one match per line:
        ///
        ///   ROOM KEY | mode | minutes | points | objective seconds
        ///
        /// Only the room key is required. '#' starts a comment.
        /// </summary>
        public static MapRotation Load(string path)
        {
            var rotation = new MapRotation();
            int lineNumber = 0;
            foreach (string raw in File.ReadLines(path))
            {
                lineNumber++;
                string line = raw;
                int comment = line.IndexOf('#');
                if (comment >= 0)
                {
                    line = line[..comment];
                }
                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }
                string[] parts = line.Split('|');
                string roomKey = parts[0].Trim();
                if (roomKey.Length == 0)
                {
                    continue;
                }
                GameMode mode = GameMode.Battle;
                if (parts.Length > 5) { throw InvalidLine(); }
                if (parts.Length > 1 && parts[1].Trim().Length > 0)
                {
                    if (!Enum.TryParse(parts[1].Trim().Replace(" ", ""), ignoreCase: true, out mode)
                        || mode is < GameMode.Battle or > GameMode.PrimeHunter) { throw InvalidLine(); }
                }
                float timeLimit = 7 * 60;
                if (parts.Length > 2 && parts[2].Trim().Length > 0)
                {
                    if (!Single.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float minutes)) { throw InvalidLine(); }
                    timeLimit = minutes * 60;
                }
                int pointGoal = 7;
                if (parts.Length > 3 && parts[3].Trim().Length > 0)
                {
                    if (!Int32.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out pointGoal)) { throw InvalidLine(); }
                }
                float? objectiveTimeGoal = null;
                if (parts.Length > 4 && parts[4].Trim().Length > 0)
                {
                    if (!Single.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float seconds)) { throw InvalidLine(); }
                    objectiveTimeGoal = seconds;
                }
                var entry = new RotationEntry
                {
                    RoomKey = roomKey,
                    Mode = mode,
                    TimeLimit = timeLimit,
                    PointGoal = pointGoal,
                    ObjectiveTimeGoal = objectiveTimeGoal
                };
                try { _ = entry.ToMatchRules(); }
                catch (ArgumentException) { throw InvalidLine(); }
                rotation._entries.Add(entry);

                ProgramException InvalidLine() => new($"Invalid match rules in rotation {path}, line {lineNumber}.");
            }
            return rotation;
        }

        /// <summary>A starter rotation, written when no file exists yet.</summary>
        public static void WriteDefault(string path)
        {
            File.WriteAllLines(path, new[]
            {
                "# MphRead dedicated server map rotation.",
                "# One match per line:  ROOM KEY | mode | minutes | points | objective seconds",
                "# Mode and the numbers are optional; '#' starts a comment.",
                "# Room keys are the names MphRead uses internally -- run the",
                "# game's console menu to see the full list.",
                "",
                "MP1 SANCTORUS      | Battle | 7 | 7",
                "MP3 PROVING GROUND | Battle | 7 | 7",
                "MP4 HIGHGROUND     | Battle | 7 | 7",
                "MP2 HARVESTER      | Battle | 7 | 7",
                "MP6 HEADSHOT       | Battle | 7 | 7"
            });
        }

        public static MapRotation LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                WriteDefault(path);
                Console.WriteLine($"[server] wrote a starter rotation to {path}");
            }
            MapRotation rotation = Load(path);
            if (rotation._entries.Count == 0)
            {
                Console.WriteLine($"[server] {path} has no usable entries; using a single default map");
                rotation._entries.Add(_fallback);
            }
            return rotation;
        }
    }
}
