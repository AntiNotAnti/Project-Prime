using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.Mods.Replay
{
    internal enum ReplayHighlightKind
    {
        Kill,
        MultiKill,
        Objective,
        CloseFinish,
        Comeback,
        Duel,
        MatchFinish,
        Context
    }

    internal readonly record struct ReplayHighlight(
        uint StartFrame,
        uint EndFrame,
        uint FocusFrame,
        ReplayHighlightKind Kind,
        byte ActorSlot,
        byte TargetSlot,
        int Score,
        string Label);

    // Category multipliers preserve the existing ranking while exposing deterministic tuning.
    // Scores are always normalized to 0..100; zero suppresses a category.
    internal sealed record ReplayHighlightWeights(
        int CombatPercent = 100, int ObjectivePercent = 100, int FinishPercent = 100);

    internal readonly record struct ReplayPlayerAnalytics(
        byte Slot,
        int Kills,
        int Deaths,
        int Damage,
        int ScoreEvents,
        int ObjectiveEvents,
        int Spawns,
        int Joins,
        int Leaves);

    internal readonly record struct ReplayTimeBucket(
        uint StartFrame,
        uint EndFrame,
        int Damage,
        int Kills,
        int Objectives)
    {
        // Annotation activity per fixed ten-second window, not suspense or a
        // probability: 100 damage = 10 points, kill = 20, objective = 25.
        // Fixed weights make recordings comparable; sparse final windows are
        // deliberately not extrapolated to a full window.
        public int ActivityScore => (int)Math.Clamp(
            Math.Max(0L, Damage) / 10 + Math.Max(0L, Kills) * 20
                + Math.Max(0L, Objectives) * 25, 0, 100);
    }

    internal readonly record struct ReplayWeaponUsage(int Weapon, int Shots);

    internal sealed class ReplayAnalyticsSnapshot
    {
        public IReadOnlyList<ReplayPlayerAnalytics> Players { get; init; } = Array.Empty<ReplayPlayerAnalytics>();
        public int TotalKills { get; init; }
        public int TotalDamage { get; init; }
        public int ObjectiveEvents { get; init; }
        public uint DurationFrames { get; init; }
        public const uint ActivityBucketFrames = 10 * 60;
        public const string ActivityDescription = "Recorded activity per 10s: damage/10 + kills×20 + objectives×25, capped at 100. Final windows are not extrapolated.";
        public IReadOnlyList<ReplayTimeBucket> DamageTimeline { get; init; }
            = Array.Empty<ReplayTimeBucket>();
        public IReadOnlyList<ReplayWeaponUsage> WeaponUsage { get; init; }
            = Array.Empty<ReplayWeaponUsage>();
    }

    /// <summary>
    /// Derived replay information. It only reads annotations and metadata, so it can never
    /// influence packet playback or simulation determinism.
    /// </summary>
    internal static class ReplayStudio
    {
        internal const int MaximumAnalysisEvents = 100_000;
        private const uint HighlightPreRoll = 4 * 60;
        private const uint HighlightPostRoll = 3 * 60;
        private const uint MultiKillWindow = 8 * 60;
        private static string? _cachePath;
        private static int _cacheEventCount = -1;
        private static uint _cacheDuration;
        private static IReadOnlyList<ReplayHighlight>? _cachedHighlights;
        private static ReplayAnalyticsSnapshot? _cachedAnalytics;

        internal static bool TryBeamType(int value, out BeamType beam)
        {
            beam = default;
            if (value is < sbyte.MinValue or > sbyte.MaxValue)
            {
                return false;
            }
            beam = (BeamType)(sbyte)value;
            return Enum.IsDefined(beam);
        }

        internal static void ResetCache()
        {
            _cachePath = null;
            _cacheEventCount = -1;
            _cacheDuration = 0;
            _cachedHighlights = null;
            _cachedAnalytics = null;
        }

        private static bool DefaultCacheValid(int eventCount, uint duration)
            => ReplayPathComparer.Comparer.Equals(_cachePath, DemoPlayback.LogicalPath)
                && _cacheEventCount == eventCount
                && _cacheDuration == duration;

        private static void NoteDefaultCache(int eventCount, uint duration)
        {
            string? path = DemoPlayback.LogicalPath;
            if (!ReplayPathComparer.Comparer.Equals(_cachePath, path)
                || _cacheEventCount != eventCount || _cacheDuration != duration)
            {
                _cachePath = path;
                _cacheEventCount = eventCount;
                _cacheDuration = duration;
                _cachedHighlights = null;
                _cachedAnalytics = null;
            }
        }

        public static IReadOnlyList<ReplayHighlight> Highlights(
            IReadOnlyList<ReplayEvent>? events = null, uint? duration = null,
            ReplayHighlightWeights? weights = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool useCache = events == null && !duration.HasValue && weights == null && !cancellationToken.CanBeCanceled;
            events ??= DemoPlayback.Events;
            uint length = duration ?? DemoPlayback.LastFrame;
            if (events.Count > MaximumAnalysisEvents) throw new InvalidOperationException("Replay exceeds highlight analysis event budget.");
            if (useCache)
            {
                NoteDefaultCache(events.Count, length);
                if (DefaultCacheValid(events.Count, length) && _cachedHighlights != null)
                    return _cachedHighlights;
            }

            weights ??= new ReplayHighlightWeights();
            var result = new List<ReplayHighlight>();
            var recentKills = new Dictionary<byte, Queue<ReplayEvent>>();

            foreach (ReplayEvent e in events.Where(e => e.Frame <= length).Distinct()
                .OrderBy(e => e.Frame).ThenBy(e => e.Type).ThenBy(e => e.ActorSlot)
                .ThenBy(e => e.TargetSlot).ThenBy(e => e.Value))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Count >= 2048) throw new InvalidOperationException("Replay exceeds highlight candidate budget.");
                // An annotation has no generation/life ID. Never carry a chain across an
                // observed lifecycle boundary; missing identities cannot establish a streak.
                if (e.Type is ReplayEventType.PlayerDeath or ReplayEventType.PlayerSpawn
                    or ReplayEventType.PlayerJoined or ReplayEventType.PlayerLeft)
                    recentKills.Remove(e.ActorSlot);
                if (e.Type == ReplayEventType.Kill && e.ActorSlot < 8)
                {
                    if (!recentKills.TryGetValue(e.ActorSlot, out Queue<ReplayEvent>? queue))
                    {
                        queue = new Queue<ReplayEvent>();
                        recentKills[e.ActorSlot] = queue;
                    }
                    while (queue.Count > 0 && e.Frame - queue.Peek().Frame > MultiKillWindow)
                        queue.Dequeue();
                    queue.Enqueue(e);

                    int chain = queue.Count;
                    int score = chain >= 3 ? 90 + chain * 8 : chain == 2 ? 72 : 45;
                    ReplayHighlightKind kind = chain >= 2 ? ReplayHighlightKind.MultiKill : ReplayHighlightKind.Kill;
                    string label = chain switch
                    {
                        >= 4 => $"{chain}x multi-kill",
                        3 => "Triple kill",
                        2 => "Double kill",
                        _ => "Kill"
                    };
                    AddMerged(result, Make(e.Frame, length, kind, e.ActorSlot, e.TargetSlot, score, label), cancellationToken);
                }
                else if (e.Type == ReplayEventType.MatchPoint)
                {
                    AddMerged(result, Make(e.Frame, length, ReplayHighlightKind.Kill,
                        e.ActorSlot, e.TargetSlot, 75, "Recorded match point"), cancellationToken);
                }
                else if (e.Type is ReplayEventType.Objective or ReplayEventType.FlagCapture or ReplayEventType.NodeCapture or ReplayEventType.PrimeChanged)
                {
                    AddMerged(result, Make(e.Frame, length, ReplayHighlightKind.Objective,
                        e.ActorSlot, e.TargetSlot, 82, "Objective play"), cancellationToken);
                }
                else if (e.Type == ReplayEventType.MatchEnded && e.Frame > 0)
                {
                    uint start = e.Frame > 12 * 60 ? e.Frame - 12 * 60 : 0;
                    result.Add(new ReplayHighlight(start, Math.Min(length, (uint)Math.Min(uint.MaxValue, (ulong)e.Frame + 2 * 60)), e.Frame,
                        ReplayHighlightKind.MatchFinish, e.ActorSlot, e.TargetSlot, 88, "Match finish"));
                }
            }

            // Dense damage exchanges without a kill are worth surfacing as duels.
            foreach (IGrouping<uint, ReplayEvent> bucket in events
                .Where(e => e.Type == ReplayEventType.Damage && e.Frame <= length).Distinct()
                .GroupBy(e => e.Frame / (5 * 60)).OrderBy(bucket => bucket.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int damage = bucket.Sum(e => Math.Max(0, e.Value));
                if (damage < 120) continue;
                ReplayEvent focus = bucket.OrderByDescending(e => e.Value).ThenBy(e => e.Frame)
                    .ThenBy(e => e.ActorSlot).ThenBy(e => e.TargetSlot).First();
                AddMerged(result, Make(focus.Frame, length, ReplayHighlightKind.Duel,
                    focus.ActorSlot, focus.TargetSlot, Math.Min(80, 45 + damage / 6), "Heavy duel"), cancellationToken);
            }

            ReplayHighlight[] highlights = result
                .Select(h => h with { Score = (int)Math.Clamp((long)h.Score * Math.Clamp(
                    h.Kind == ReplayHighlightKind.Objective ? weights.ObjectivePercent
                    : h.Kind == ReplayHighlightKind.MatchFinish ? weights.FinishPercent
                    : weights.CombatPercent, 0, 1000) / 100, 0, 100) })
                .Where(h => h.Score > 0 && h.StartFrame < h.EndFrame)
                .OrderByDescending(h => h.Score)
                .ThenBy(h => h.FocusFrame).ThenBy(h => h.ActorSlot)
                .ThenBy(h => h.TargetSlot).ThenBy(h => h.Kind)
                .ToArray();
            if (useCache) _cachedHighlights = highlights;
            return highlights;
        }

        /// <summary>Pure, timestamp-free export. Call on a worker with an immutable annotation snapshot.</summary>
        public static string HighlightsJson(IReadOnlyList<ReplayEvent> events, uint duration,
            ReplayHighlightWeights? weights = null, CancellationToken cancellationToken = default)
            => JsonSerializer.Serialize(new
            {
                Version = 1,
                Provenance = "Replay annotations; lifecycle identities and spatial context unavailable",
                DurationFrames = duration,
                FramesPerSecond = 60,
                Weights = weights ?? new ReplayHighlightWeights(),
                Highlights = Highlights(events, duration, weights, cancellationToken)
            }, new JsonSerializerOptions { WriteIndented = true });

        public static ReplayAnalyticsSnapshot Analytics(
            IReadOnlyList<ReplayEvent>? events = null, uint? duration = null)
        {
            bool useCache = events == null && !duration.HasValue;
            events ??= DemoPlayback.Events;
            uint length = duration ?? DemoPlayback.LastFrame;
            if (useCache)
            {
                NoteDefaultCache(events.Count, length);
                if (DefaultCacheValid(events.Count, length) && _cachedAnalytics != null)
                    return _cachedAnalytics;
            }

            var rows = new Dictionary<byte, MutableAnalytics>();
            MutableAnalytics Row(byte slot)
            {
                if (!rows.TryGetValue(slot, out MutableAnalytics? row))
                {
                    row = new MutableAnalytics();
                    rows[slot] = row;
                }
                return row;
            }

            const uint bucketFrames = ReplayAnalyticsSnapshot.ActivityBucketFrames;
            var timeline = new Dictionary<uint, MutableTimeline>();
            MutableTimeline Bucket(uint frame)
            {
                uint startFrame = frame / bucketFrames * bucketFrames;
                if (!timeline.TryGetValue(startFrame, out MutableTimeline? bucket))
                {
                    bucket = new MutableTimeline();
                    timeline[startFrame] = bucket;
                }
                return bucket;
            }

            var weapons = new Dictionary<int, int>();
            bool explicitDeaths = events.Any(e => e.Type == ReplayEventType.PlayerDeath);
            int totalKills = 0;
            int totalDamage = 0;
            int objectives = 0;
            foreach (ReplayEvent e in events)
            {
                if (e.ActorSlot != byte.MaxValue)
                {
                    MutableAnalytics actor = Row(e.ActorSlot);
                    switch (e.Type)
                    {
                        case ReplayEventType.Kill:
                            actor.Kills++;
                            totalKills++;
                            Bucket(e.Frame).Kills++;
                            break;
                        case ReplayEventType.Damage:
                            int damage = Math.Max(0, e.Value);
                            actor.Damage += damage;
                            totalDamage += damage;
                            Bucket(e.Frame).Damage += damage;
                            break;
                        case ReplayEventType.ScoreChanged:
                            actor.ScoreEvents++;
                            break;
                        case ReplayEventType.FlagCapture:
                        case ReplayEventType.NodeCapture:
                        case ReplayEventType.PrimeChanged:
                        case ReplayEventType.Objective:
                            actor.Objectives++;
                            objectives++;
                            Bucket(e.Frame).Objectives++;
                            break;
                        case ReplayEventType.PlayerSpawn:
                            actor.Spawns++;
                            break;
                        case ReplayEventType.PlayerJoined:
                            actor.Joins++;
                            break;
                        case ReplayEventType.PlayerLeft:
                            actor.Leaves++;
                            break;
                        case ReplayEventType.WeaponFired:
                            if (e.Value >= 0)
                                weapons[e.Value] = weapons.GetValueOrDefault(e.Value) + 1;
                            break;
                    }
                }

                if (e.Type == ReplayEventType.PlayerDeath
                    && e.ActorSlot != byte.MaxValue)
                {
                    Row(e.ActorSlot).Deaths++;
                }
                else if (!explicitDeaths && e.Type == ReplayEventType.Kill
                    && e.TargetSlot != byte.MaxValue)
                {
                    // Older recordings may contain Kill without a paired
                    // PlayerDeath. New recordings write both; count one source
                    // or the other, never the same death twice.
                    Row(e.TargetSlot).Deaths++;
                }
            }

            var players = rows.OrderBy(p => p.Key)
                .Select(p => new ReplayPlayerAnalytics(p.Key, p.Value.Kills, p.Value.Deaths,
                    p.Value.Damage, p.Value.ScoreEvents, p.Value.Objectives,
                    p.Value.Spawns, p.Value.Joins, p.Value.Leaves))
                .ToArray();
            ReplayTimeBucket[] buckets = timeline
                .OrderBy(pair => pair.Key)
                .Select(pair => new ReplayTimeBucket(pair.Key,
                    Math.Min(length, pair.Key + bucketFrames),
                    pair.Value.Damage, pair.Value.Kills, pair.Value.Objectives))
                .ToArray();
            ReplayWeaponUsage[] weaponUsage = weapons
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Select(pair => new ReplayWeaponUsage(pair.Key, pair.Value))
                .ToArray();

            var snapshot = new ReplayAnalyticsSnapshot
            {
                Players = players,
                TotalKills = totalKills,
                TotalDamage = totalDamage,
                ObjectiveEvents = objectives,
                DurationFrames = length,
                DamageTimeline = buckets,
                WeaponUsage = weaponUsage
            };
            if (useCache) _cachedAnalytics = snapshot;
            return snapshot;
        }

        private static ReplayHighlight Make(uint frame, uint duration, ReplayHighlightKind kind,
            byte actor, byte target, int score, string label)
        {
            uint start = frame > HighlightPreRoll ? frame - HighlightPreRoll : 0;
            uint end = Math.Min(duration, (uint)Math.Min(uint.MaxValue, (ulong)frame + HighlightPostRoll));
            return new ReplayHighlight(start, end, frame, kind, actor, target, score, label);
        }

        private static void AddMerged(List<ReplayHighlight> result, ReplayHighlight candidate, CancellationToken cancellationToken)
        {
            for (int i = 0; i < result.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReplayHighlight current = result[i];
                if (current.ActorSlot == candidate.ActorSlot
                    && candidate.StartFrame <= current.EndFrame + 60
                    && current.StartFrame <= candidate.EndFrame + 60)
                {
                    result[i] = new ReplayHighlight(
                        Math.Min(current.StartFrame, candidate.StartFrame),
                        Math.Max(current.EndFrame, candidate.EndFrame),
                        candidate.Score >= current.Score ? candidate.FocusFrame : current.FocusFrame,
                        candidate.Score >= current.Score ? candidate.Kind : current.Kind,
                        candidate.ActorSlot,
                        candidate.Score >= current.Score ? candidate.TargetSlot : current.TargetSlot,
                        Math.Max(current.Score, candidate.Score),
                        candidate.Score >= current.Score ? candidate.Label : current.Label);
                    return;
                }
            }
            if (result.Count >= 2048) throw new InvalidOperationException("Replay exceeds highlight candidate budget.");
            result.Add(candidate);
        }

        private sealed class MutableAnalytics
        {
            public int Kills;
            public int Deaths;
            public int Damage;
            public int ScoreEvents;
            public int Objectives;
            public int Spawns;
            public int Joins;
            public int Leaves;
        }

        private sealed class MutableTimeline
        {
            public int Damage;
            public int Kills;
            public int Objectives;
        }
    }

    internal sealed record ReplayVirtualClipDocument(
        int Version,
        string SourceReplay,
        uint StartFrame,
        uint EndFrame,
        DateTime CreatedUtc,
        string Name);

    internal static class ReplayVirtualClips
    {
        public const string Extension = ".ppclip";

        public static string Save(string sourceReplay, uint startFrame, uint endFrame, string? name = null)
        {
            if (startFrame >= endFrame) throw new ArgumentOutOfRangeException(nameof(endFrame));
            string source = Path.GetFullPath(sourceReplay);

            // Watching a virtual clip uses a materialized .ppdemo cache. A clip
            // cut from that should not depend on the cache surviving: flatten it
            // back onto the original replay and rebase the selected frame range.
            string logical = LogicalPath(source);
            if (logical.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
                && TryLoad(logical, out ReplayVirtualClipDocument? parent)
                && parent != null)
            {
                startFrame = checked(parent.StartFrame + startFrame);
                endFrame = checked(parent.StartFrame + endFrame);
                source = Path.GetFullPath(parent.SourceReplay);
            }

            if (!File.Exists(source)) throw new FileNotFoundException("The source replay does not exist.", source);
            using (var reader = DemoReader.Open(source, out _))
                if (reader?.Metadata?.CustomWeapons != null)
                    throw new InvalidOperationException("Custom replay virtual clips are not supported.");
            Directory.CreateDirectory(DemoLibrary.Directory);
            string title = string.IsNullOrWhiteSpace(name)
                ? $"Clip {ReplayHud.Time(startFrame)}-{ReplayHud.Time(endFrame)}"
                : name.Trim();
            var document = new ReplayVirtualClipDocument(1, source, startFrame, endFrame, DateTime.UtcNow, title);
            string path = Path.Combine(DemoLibrary.Directory,
                $"virtual_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid():N}{Extension}");
            File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions));
            return path;
        }

        public static bool TryLoad(string path, out ReplayVirtualClipDocument? document)
        {
            document = null;
            try
            {
                if (!File.Exists(path)) return false;
                document = JsonSerializer.Deserialize<ReplayVirtualClipDocument>(
                    File.ReadAllText(path), JsonOptions);
                return document is { Version: 1 } value
                    && value.StartFrame < value.EndFrame
                    && !string.IsNullOrWhiteSpace(value.SourceReplay);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return false;
            }
        }

        public static ReplayOpenResult Materialize(string virtualClipPath, string outputPath)
        {
            if (!TryLoad(virtualClipPath, out ReplayVirtualClipDocument? clip) || clip == null)
                return ReplayOpenResult.Corrupt;
            if (!File.Exists(clip.SourceReplay)) return ReplayOpenResult.FileMissing;
            return ReplayArchive.Extract(clip.SourceReplay, clip.StartFrame, clip.EndFrame, outputPath);
        }

        public static IReadOnlyList<string> List()
        {
            if (!Directory.Exists(DemoLibrary.Directory)) return Array.Empty<string>();
            return Directory.EnumerateFiles(DemoLibrary.Directory, "*" + Extension, SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToArray();
        }

        public static bool IsFavorite(string path) => File.Exists(path + ".favorite");

        public static void ToggleFavorite(string path)
        {
            string marker = path + ".favorite";
            if (File.Exists(marker)) File.Delete(marker);
            else File.WriteAllText(marker, "");
        }

        public static bool Rename(string path, string name)
        {
            if (!TryLoad(path, out ReplayVirtualClipDocument? clip) || clip == null)
                return false;
            string title = String.IsNullOrWhiteSpace(name) ? clip.Name : name.Trim();
            File.WriteAllText(path, JsonSerializer.Serialize(
                clip with { Name = title }, JsonOptions));
            return true;
        }

        public static void Delete(string path)
        {
            ReplayArtifacts.DeleteAll(path);
        }

        public static string? ResolveForPlayback(string path, out ReplayOpenResult result)
        {
            result = ReplayOpenResult.Corrupt;
            if (!TryLoad(path, out ReplayVirtualClipDocument? clip) || clip == null)
                return null;
            if (!File.Exists(clip.SourceReplay))
            {
                result = ReplayOpenResult.FileMissing;
                return null;
            }

            string output = CachePath(path);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var source = new FileInfo(clip.SourceReplay);
                var descriptor = new FileInfo(path);
                if (File.Exists(output)
                    && File.GetLastWriteTimeUtc(output) >= descriptor.LastWriteTimeUtc
                    && File.GetLastWriteTimeUtc(output) >= source.LastWriteTimeUtc)
                {
                    result = ReplayOpenResult.Success;
                    return output;
                }

                ReplayArtifacts.DeleteCache(output);
                result = ReplayArchive.Extract(clip.SourceReplay,
                    clip.StartFrame, clip.EndFrame, output);
                return result == ReplayOpenResult.Success ? output : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException)
            {
                return null;
            }
        }

        public static string LogicalPath(string playbackPath)
        {
            string full = Path.GetFullPath(playbackPath);
            string? directory = Path.GetDirectoryName(full);
            if (directory != null && ReplayPathComparer.Comparer.Equals(
                Path.GetFileName(directory), ".virtual-cache")
                && Path.GetDirectoryName(directory) is { } ownerDirectory)
            {
                string descriptor = Path.Combine(ownerDirectory,
                    Path.GetFileNameWithoutExtension(full) + Extension);
                if (File.Exists(descriptor)) return Path.GetFullPath(descriptor);
            }
            return full;
        }

        internal static string CachePath(string path)
        {
            string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".virtual-cache");
            string file = Path.GetFileNameWithoutExtension(path) + DemoFile.Extension;
            return Path.Combine(directory, file);
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };
    }

    internal sealed record ReplayStoragePolicy(
        long MaxBytes,
        bool DeleteFullMatches = true,
        bool DeleteMaterializedClips = false,
        bool DeleteVirtualClips = false);

    internal readonly record struct ReplayStorageResult(long BeforeBytes, long AfterBytes, int DeletedFiles);

    internal static class ReplayStorageManager
    {
        public static ReplayStorageResult Apply(ReplayStoragePolicy policy)
        {
            if (policy.MaxBytes <= 0) return new ReplayStorageResult(0, 0, 0);
            Directory.CreateDirectory(DemoLibrary.Directory);
            FileInfo[] files = new DirectoryInfo(DemoLibrary.Directory)
                .EnumerateFiles("*" + DemoFile.Extension, SearchOption.TopDirectoryOnly)
                .Where(f => !f.Name.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToArray();

            string cacheDirectory = Path.Combine(DemoLibrary.Directory, ".virtual-cache");
            FileInfo[] cacheFiles = Directory.Exists(cacheDirectory)
                ? new DirectoryInfo(cacheDirectory)
                    .EnumerateFiles("*" + DemoFile.Extension, SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToArray()
                : Array.Empty<FileInfo>();

            var protectedSources = ProtectedSources();
            string[] virtualClips = ReplayVirtualClips.List().ToArray();
            long virtualBytes = virtualClips.Sum(path => { try { return new FileInfo(path).Length; } catch { return 0; } });

            long before = files.Sum(f => f.Length) + cacheFiles.Sum(f => f.Length) + virtualBytes;
            long total = before;
            int deleted = 0;

            // Materialized virtual clips are a cache, never the source of truth.
            // Evict them before deleting a real recording.
            foreach (FileInfo cache in cacheFiles)
            {
                if (total <= policy.MaxBytes) break;
                if (DemoPlayback.IsActive && DemoPlayback.PlaybackPath != null
                    && ReplayPathComparer.Same(cache.FullName, DemoPlayback.PlaybackPath))
                {
                    // The active player must be able to reopen its materialized file.
                    continue;
                }
                try
                {
                    long bytes = cache.Length;
                    ReplayArtifacts.DeleteCache(cache.FullName);
                    total = Math.Max(0, total - bytes);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Continue with other cache entries or recordings.
                }
            }

            if (policy.DeleteVirtualClips && total > policy.MaxBytes)
            {
                foreach (string path in virtualClips.OrderBy(File.GetLastWriteTimeUtc))
                {
                    if (total <= policy.MaxBytes) break;
                    if (File.Exists(path + ".favorite")
                        || DemoPlayback.LogicalPath is { } active && ReplayPathComparer.Same(path, active))
                        continue;
                    try
                    {
                        long bytes = new FileInfo(path).Length;
                        ReplayVirtualClips.Delete(path);
                        total = Math.Max(0, total - bytes);
                        deleted++;
                        protectedSources = ProtectedSources();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }

            foreach (FileInfo file in files)
            {
                if (total <= policy.MaxBytes) break;
                string path = file.FullName;
                if (File.Exists(path + ".favorite")) continue;
                if (DemoPlayback.IsActive && DemoPlayback.PlaybackPath != null
                    && ReplayPathComparer.Same(path, DemoPlayback.PlaybackPath))
                    continue;
                if (protectedSources.Contains(ReplayPathComparer.Normalize(path))) continue;
                using DemoReader? reader = DemoReader.Open(path, out _, metadataOnly: true);
                ReplayType type = reader?.Metadata?.Type ?? ReplayType.FullMatch;
                if (type == ReplayType.FullMatch && !policy.DeleteFullMatches) continue;
                if (type == ReplayType.Clip && !policy.DeleteMaterializedClips) continue;
                try
                {
                    long bytes = file.Length;
                    ReplayArtifacts.DeleteAll(path, deleteVirtualCache: false);
                    total = Math.Max(0, total - bytes);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Keep pruning other eligible files.
                }
            }

            return new ReplayStorageResult(before, total, deleted);
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static HashSet<string> ProtectedSources()
        {
            var result = new HashSet<string>(ReplayPathComparer.Comparer);
            foreach (string virtualClip in ReplayVirtualClips.List())
                if (ReplayVirtualClips.TryLoad(virtualClip, out ReplayVirtualClipDocument? clip)
                    && clip != null)
                    result.Add(ReplayPathComparer.Normalize(clip.SourceReplay));
            return result;
        }
    }

    internal enum ReplayVideoResolution
    {
        P720,
        P1080,
        P1440,
        P2160
    }

    internal sealed record ReplayVideoExportManifest(
        int Version,
        string Replay,
        uint StartFrame,
        uint EndFrame,
        int Width,
        int Height,
        int Fps,
        bool CleanHud,
        bool Director,
        bool CameraTrack,
        string FramePattern,
        string SuggestedOutput,
        string FfmpegArguments,
        IReadOnlyList<ReplayVideoSegment>? Segments = null,
        string PresetName = "",
        bool? GameHud = null,
        bool? ReplayOverlay = null,
        string EncoderExecutable="ffmpeg",string EncoderCodec="libx264"
#if !ANDROID && !MPHREAD_SERVER
        ,ReplayExportAudioInput? Audio=null
#endif
        )
    {
        // Version 2 manifests only knew CleanHud. Preserve that meaning:
        // game HUD followed !CleanHud and replay presentation UI was never exported.
        internal bool IncludeGameHud => GameHud ?? !CleanHud;
        internal bool IncludeReplayOverlay => ReplayOverlay ?? false;
    }

    internal static partial class ReplayVideoExport
    {
        public static ReplayVideoExportManifest CreateManifest(string replay, uint startFrame, uint endFrame,
            ReplayVideoResolution resolution = ReplayVideoResolution.P1080, int fps = 60,
            bool cleanHud = true, bool director = false, bool cameraTrack = true,
            bool? gameHud = null, bool replayOverlay = false)
        {
            if (startFrame >= endFrame) throw new ArgumentOutOfRangeException(nameof(endFrame));
            if (!ReplayExportRates.IsSupported(fps))
                throw new ArgumentOutOfRangeException(nameof(fps), fps,
                    $"Supported replay export rates are {String.Join(", ", ReplayExportRates.Supported)} FPS.");
            (int width, int height) = resolution switch
            {
                ReplayVideoResolution.P720 => (1280, 720),
                ReplayVideoResolution.P1440 => (2560, 1440),
                ReplayVideoResolution.P2160 => (3840, 2160),
                _ => (1920, 1080)
            };
            string root = Path.Combine(DemoLibrary.Directory, "exports",
                $"render_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            string frames = Path.Combine(root, "frame_%08d.png");
            string output = Path.Combine(root, "replay.mp4");
            int sourceFps = fps;
            string filters = $"scale={width}:{height}:flags=lanczos,fps={fps}";
            string ffmpeg = $"-y -framerate {sourceFps} -i \"{frames}\" "
                + $"-vf \"{filters}\" -c:v libx264 -preset slow -crf 18 "
                + $"-pix_fmt yuv420p -movflags +faststart \"{output}\"";
            bool includeGameHud = gameHud ?? !cleanHud;
            var manifest = new ReplayVideoExportManifest(3, Path.GetFullPath(replay), startFrame, endFrame,
                width, height, fps, !includeGameHud, director, cameraTrack, frames, output, ffmpeg,
                GameHud: includeGameHud, ReplayOverlay: replayOverlay);
            File.WriteAllText(Path.Combine(root, "render.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(root, "encode.txt"), "ffmpeg " + ffmpeg + Environment.NewLine);
            return manifest;
        }
    }
}
