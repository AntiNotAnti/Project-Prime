using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MphRead.Mods;
using MphRead.Mods.Update;
using MphRead.Runtime.Content;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// A persisted region ID paired with the friendly label shown in Settings.
    /// The ID remains the authoritative value used by Node selection; labels
    /// are presentation only and unknown IDs retain a visible fallback.
    /// </summary>
    public sealed record PreferredRegionOption(string Id, string Label);

    /// <summary>
    /// Launcher-only preferences, kept in their own file beside the
    /// executable.
    ///
    /// Deliberately not folded into upstream's MenuSettings: that type is
    /// serialized by GameState and gains fields as upstream develops, so
    /// adding mod-specific keys to it would guarantee a merge conflict. A
    /// separate file costs nothing and keeps the fetch clean.
    /// </summary>
    public static class LauncherPrefs
    {
        public const string AutomaticPreferredRegionId = "Automatic";

        /// <summary>Backend origin used by fresh development clients.</summary>
        public const string DefaultBackendAddress = "https://rebooty.xyz/";
        private const string LegacyBackendAddress = "http://51.161.113.128:18085/";
        public static string BackendAddress { get; set; } = DefaultBackendAddress;
        /// <summary>
        /// Where launcher.txt lives. Beside the executable, which is where the
        /// rest of a portable install keeps its files -- except where the
        /// program does not own that folder. An Android package's own
        /// directory is read-only, so the head there points this at the app's
        /// data directory before anything reads.
        /// </summary>
        public static string Directory { get; set; } = AppContext.BaseDirectory;

        private static string Path => System.IO.Path.Combine(Directory, "launcher.txt");

        public static int LastRole { get; set; }
        public static string PlayerName { get; set; } = "Player";
        /// <summary>Hunter last chosen, possibly <see cref="Hunter.Random"/>.</summary>
        public static Hunter LastHunter { get; set; } = Hunter.Samus;
        /// <summary>Bots in an offline match.</summary>
        public static int Bots { get; set; } = 3;
        /// <summary>0 easy, 1 normal, 2 hard -- PlayerEntity.BotLevel.</summary>
        public static int BotLevel { get; set; } = 1;
        /// <summary>Last LaunchKind, so the front screen can offer it again.</summary>
        public static int LastKind { get; set; }

        /// <summary>
        /// How startup update work is handled. Missing preference files default
        /// to Automatic; the legacy auto_update key is migrated once below.
        /// </summary>
        public static UpdatePolicy UpdatePolicy { get; set; } = UpdatePolicy.Automatic;

        /// <summary>
        /// The process-known region IDs available to the launcher when it
        /// chooses a Node. Directory refreshes add the exact IDs advertised by
        /// the Node; the persisted nonautomatic ID remains available even when
        /// it is not currently observed. The persisted value remains the exact
        /// ID; Settings derives a presentation label without replacing it.
        /// </summary>
        public static IReadOnlyList<string> PreferredRegionChoices
        {
            get
            {
                lock (_preferredRegionGate)
                {
                    return BuildPreferredRegionIdsUnsafe().AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Region choices for a player-facing selector. IDs are kept separate
        /// from labels so a friendly name can never become the value sent to
        /// Node selection. Unknown IDs are still selectable and are displayed
        /// as <c>Unknown (id)</c> rather than being silently discarded.
        /// </summary>
        public static IReadOnlyList<PreferredRegionOption> PreferredRegionOptions
        {
            get
            {
                lock (_preferredRegionGate)
                {
                    return BuildPreferredRegionIdsUnsafe()
                        .Select(id => new PreferredRegionOption(id, PreferredRegionLabel(id)))
                        .ToArray();
                }
            }
        }

        private static readonly IReadOnlyDictionary<string, string> _preferredRegionLabels
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["us"] = "United States",
                ["us-east"] = "US East",
                ["us-east-1"] = "US East",
                ["us-central"] = "US Central",
                ["us-central-1"] = "US Central",
                ["us-west"] = "US West",
                ["us-west-1"] = "US West",
                ["europe"] = "Europe",
                ["eu-west"] = "Europe",
                ["eu-west-1"] = "Europe",
                ["asia"] = "Asia Pacific",
                ["asia-pacific"] = "Asia Pacific",
                ["apac"] = "Asia Pacific",
                ["ap-southeast-1"] = "Asia Pacific",
                ["japan"] = "Japan",
                ["jp"] = "Japan"
            };

        public static string PreferredRegionLabel(string? id)
        {
            if (String.IsNullOrWhiteSpace(id))
            {
                return AutomaticPreferredRegionId;
            }
            string value = id.Trim();
            if (value.Equals(AutomaticPreferredRegionId, StringComparison.OrdinalIgnoreCase)
                || value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                return AutomaticPreferredRegionId;
            }
            return _preferredRegionLabels.TryGetValue(value, out string? label)
                ? label : $"Unknown ({value})";
        }

        private const int MaxPreferredRegionLength = 32;
        internal const int MaxObservedPreferredRegions = 128;
        private static readonly object _preferredRegionGate = new();
        private static readonly SortedSet<string> _observedPreferredRegions
            = new(StringComparer.Ordinal);
        private static string _preferredRegion = "Automatic";

        private static List<string> BuildPreferredRegionIdsUnsafe()
        {
            var choices = new List<string>(_observedPreferredRegions.Count + 1)
            {
                AutomaticPreferredRegionId
            };
            var regionChoices = new SortedSet<string>(_observedPreferredRegions,
                StringComparer.Ordinal);
            if (_preferredRegion != AutomaticPreferredRegionId
                && IsValidPreferredRegion(_preferredRegion))
            {
                regionChoices.Add(_preferredRegion);
            }
            choices.AddRange(regionChoices);
            return choices;
        }

        public static string PreferredRegion
        {
            get
            {
                lock (_preferredRegionGate)
                {
                    return _preferredRegion;
                }
            }
            set
            {
                string normalized = NormalizePreferredRegion(value);
                lock (_preferredRegionGate)
                {
                    _preferredRegion = normalized;
                }
            }
        }

        /// <summary>
        /// Add exact region IDs advertised by the current directory refresh.
        /// Invalid, empty, control-containing, and overlong values are ignored
        /// so a malformed Node response cannot grow the settings list without
        /// bound. "Automatic" is the local no-match sentinel, not a Node ID.
        /// </summary>
        public static void ObservePreferredRegions(IEnumerable<string> regions)
        {
            ArgumentNullException.ThrowIfNull(regions);
            lock (_preferredRegionGate)
            {
                // Each directory refresh is authoritative. Keep only the
                // valid IDs from this result, retaining the lexicographically
                // first bounded subset so enumeration order cannot affect the
                // menu presented to the player.
                var observed = new SortedSet<string>(StringComparer.Ordinal);
                foreach (string region in regions)
                {
                    if (IsValidPreferredRegion(region))
                    {
                        observed.Add(region);
                        if (observed.Count > MaxObservedPreferredRegions)
                        {
                            observed.Remove(observed.Max!);
                        }
                    }
                }
                _observedPreferredRegions.Clear();
                _observedPreferredRegions.UnionWith(observed);
            }
        }

        /// <summary>
        /// Compatibility shim for older launcher surfaces. New code should use
        /// <see cref="UpdatePolicy"/> so NotifyOnly is not mistaken for Off.
        /// </summary>
        [Obsolete("Use UpdatePolicy")]
        public static bool AutoUpdate
        {
            get => UpdatePolicy != UpdatePolicy.Off;
            set => UpdatePolicy = value ? UpdatePolicy.NotifyOnly : UpdatePolicy.Off;
        }

        /// <summary>
        /// How the game window opens. Kept here rather than in MenuSettings
        /// for the same reason as everything else in this file, and read by
        /// Mods.WindowMode, which is where the window itself lives.
        /// </summary>
        public static WindowStartMode WindowMode { get; set; } = WindowStartMode.Windowed;

        /// <summary>
        /// Whether the program writes a file of everything it can say about
        /// itself. See <see cref="Mods.DebugLog"/>.
        ///
        /// Enabled by default so a first-run failure leaves diagnostics behind.
        /// The launcher setting remains persistent, so players can turn it off
        /// when they do not want the extra files and per-line write lock.
        /// </summary>
        public static bool DebugLogs { get; set; } = true;
        public static bool ReducedMotion { get; set; }

        private static bool _showOnlinePresence = true;
        private static bool _savedShowOnlinePresence = true;

        /// <summary>
        /// Whether this device's display name and activity may appear in the
        /// public Online Players list. This is a local preference only for
        /// now; the event is the narrow handoff seam for a future connected
        /// session update and deliberately carries no account or session data.
        /// </summary>
        public static bool ShowOnlinePresence
        {
            get => _showOnlinePresence;
            set
            {
                if (_showOnlinePresence == value)
                {
                    return;
                }

                _showOnlinePresence = value;
            }
        }

        /// <summary>
        /// Raised after a user settings save successfully persists a changed
        /// presence preference. Assigning the preference, loading it, and
        /// ordinary programmatic saves remain silent.
        /// </summary>
        public static event EventHandler? ShowOnlinePresenceChanged;

        /// <summary>
        /// Exact local optional-pack choices. These identities affect only
        /// presentation and are deliberately absent from admission messages.
        /// A null identity means the built-in presentation.
        /// </summary>
        public static ContentPackIdentity? AnnouncerPack { get; set; }
        public static ContentPackIdentity? MusicPack { get; set; }

        /// <summary>
        /// Fixed local discovery root. Players install data-only packs as
        /// direct child directories; the client never downloads into it.
        /// </summary>
        public static string OptionalContentDirectory
            => System.IO.Path.Combine(Directory, "content");


        public static void Load()
        {
            // Load is called again when the launcher resumes after a match.
            // A removed preference must select built-ins rather than retain a
            // stale process-global choice from the previous read.
            AnnouncerPack = null;
            MusicPack = null;
            UpdatePolicy = UpdatePolicy.Automatic;
            PreferredRegion = AutomaticPreferredRegionId;
            DebugLogs = true;
            ReducedMotion = false;
            ShowOnlinePresence = true;
            if (!File.Exists(Path))
            {
                _savedShowOnlinePresence = ShowOnlinePresence;
                return;
            }
            bool policyRead = false;
            bool legacyRead = false;
            bool legacyValue = false;
            bool backendMigrated = false;
            try
            {
                foreach (string raw in File.ReadAllLines(Path))
                {
                    string line = raw.Trim();
                    int split = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == '#' || split <= 0)
                    {
                        continue;
                    }
                    string key = line[..split].Trim();
                    string value = line[(split + 1)..].Trim();
                    switch (key)
                    {
                        case "player_name":
                            if (value.Length > 0)
                            {
                                PlayerName = value;
                            }
                            break;
                        case "last_role":
                            if (Int32.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int role))
                            {
                                LastRole = role;
                            }
                            break;
                        case "hunter":
                            if (Enum.TryParse(value, ignoreCase: true, out Hunter hunter))
                            {
                                LastHunter = hunter;
                            }
                            break;
                        case "bots":
                            if (Int32.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int bots))
                            {
                                Bots = bots;
                            }
                            break;
                        case "bot_level":
                            if (Int32.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int level))
                            {
                                BotLevel = level;
                            }
                            break;
                        case "window_mode":
                            WindowMode = Mods.WindowMode.Parse(value, WindowMode);
                            break;
                        case "backend_address":
                            if (value.Length > 0)
                            {
                                if (value.Equals(LegacyBackendAddress,
                                    StringComparison.OrdinalIgnoreCase))
                                {
                                    BackendAddress = DefaultBackendAddress;
                                    backendMigrated = true;
                                }
                                else
                                {
                                    BackendAddress = value;
                                }
                            }
                            break;
                        case "auto_update":
                            if (!policyRead && Boolean.TryParse(value, out bool autoUpdate))
                            {
                                legacyRead = true;
                                legacyValue = autoUpdate;
                            }
                            break;
                        case "update_policy":
                            if (UpdatePolicyCodec.TryParse(value, out UpdatePolicy parsedPolicy))
                            {
                                UpdatePolicy = parsedPolicy;
                                policyRead = true;
                            }
                            break;
                        case "preferred_region":
                            PreferredRegion = value;
                            break;
                        case "debug_logs":
                            if (Boolean.TryParse(value, out bool debugLogs))
                            {
                                DebugLogs = debugLogs;
                            }
                            break;
                        case "reduced_motion":
                            if (Boolean.TryParse(value, out bool reducedMotion))
                            {
                                ReducedMotion = reducedMotion;
                            }
                            break;
                        case "show_online_presence":
                            if (Boolean.TryParse(value, out bool showOnlinePresence))
                            {
                                ShowOnlinePresence = showOnlinePresence;
                            }
                            break;
                        case "last_kind":
                            if (Int32.TryParse(value, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int kind))
                            {
                                LastKind = kind;
                            }
                            break;
                        case "announcer_pack":
                            if (OptionalContentPreferenceCodec.TryDecode(value, out ContentPackIdentity? announcer))
                            {
                                AnnouncerPack = announcer;
                            }
                            break;
                        case "music_pack":
                            if (OptionalContentPreferenceCodec.TryDecode(value, out ContentPackIdentity? music))
                            {
                                MusicPack = music;
                            }
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // Preferences are a convenience; a unreadable file must not
                // stop the launcher from opening.
            }
            // Loading is state initialization, not a user edit. Establish the
            // comparison point before any migration save so it can never
            // publish the future live-update seam.
            _savedShowOnlinePresence = ShowOnlinePresence;
            if (!policyRead && legacyRead)
            {
                UpdatePolicy = legacyValue ? UpdatePolicy.NotifyOnly : UpdatePolicy.Off;
                // One-time migration: Save no longer writes the legacy key.
                Save();
            }
            else if (backendMigrated)
            {
                // Retire the former public HTTP origin. Authentication rejects
                // non-loopback HTTP, so preserving it would strand upgraded clients.
                Save();
            }
        }

        /// <summary>Persist the current launcher preferences without publishing a presence update.</summary>
        public static void Save() => Save(notifyPresenceChange: false);

        /// <summary>
        /// Persist the current launcher preferences. The notification overload
        /// is reserved for the Settings user-commit path; all existing callers
        /// keep the quiet programmatic-save behavior.
        /// </summary>
        /// <returns><see langword="true"/> when the file write succeeds.</returns>
        public static bool Save(bool notifyPresenceChange)
        {
            try
            {
                File.WriteAllLines(Path, GetSaveLines());
            }
            catch (Exception)
            {
                // Same rationale as Load: never block launching over this.
                return false;
            }

            bool presenceChanged = _showOnlinePresence != _savedShowOnlinePresence;
            _savedShowOnlinePresence = _showOnlinePresence;
            if (notifyPresenceChange && presenceChanged)
            {
                // Publish only after the file write succeeds. A future client
                // Node adapter can subscribe here without observing a value
                // that failed to persist.
                ShowOnlinePresenceChanged?.Invoke(null, EventArgs.Empty);
            }
            return true;
        }

        /// <summary>
        /// Serialize launcher preferences without touching the filesystem.
        /// Keeping this seam beside <see cref="Save"/> makes migration and
        /// finite-choice persistence testable without changing the user's
        /// actual launcher file.
        /// </summary>
        public static IReadOnlyList<string> GetSaveLines()
        {
            return new[]
            {
                $"# {Branding.Name} launcher preferences.",
                $"backend_address={BackendAddress}",
                $"last_role={LastRole.ToString(CultureInfo.InvariantCulture)}",
                $"player_name={PlayerName}",
                $"hunter={LastHunter}",
                $"bots={Bots.ToString(CultureInfo.InvariantCulture)}",
                $"bot_level={BotLevel.ToString(CultureInfo.InvariantCulture)}",
                $"last_kind={LastKind.ToString(CultureInfo.InvariantCulture)}",
                $"update_policy={UpdatePolicy}",
                $"preferred_region={PreferredRegion}",
                $"debug_logs={DebugLogs.ToString().ToLowerInvariant()}",
                $"reduced_motion={ReducedMotion.ToString().ToLowerInvariant()}",
                $"show_online_presence={ShowOnlinePresence.ToString().ToLowerInvariant()}",
                $"announcer_pack={OptionalContentPreferenceCodec.Encode(AnnouncerPack)}",
                $"music_pack={OptionalContentPreferenceCodec.Encode(MusicPack)}",
                $"window_mode={(WindowMode == WindowStartMode.BorderlessFullscreen ? "borderless" : "windowed")}"
            };
        }

        private static string NormalizePreferredRegion(string? value)
        {
            if (String.IsNullOrWhiteSpace(value))
            {
                return AutomaticPreferredRegionId;
            }
            string trimmed = value.Trim();
            if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
            {
                return AutomaticPreferredRegionId;
            }
            if (IsValidPreferredRegion(value))
            {
                return value;
            }
            return AutomaticPreferredRegionId;
        }

        private static bool IsValidPreferredRegion(string? value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > MaxPreferredRegionLength)
            {
                return false;
            }
            string trimmed = value.Trim();
            if (trimmed.Length != value.Length
                || trimmed.Equals("Automatic", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            foreach (char character in value)
            {
                if (Char.IsControl(character))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
