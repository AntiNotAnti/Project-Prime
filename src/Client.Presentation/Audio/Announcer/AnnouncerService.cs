using System;
using MphRead;
using MphRead.Mods.Network;
using MphRead.Runtime.Content;

namespace MphRead.Mods.Audio;

public readonly record struct AnnouncerCue(AnnouncerEvent Event, string AssetKey, uint Tick,
    uint AwardId, MatchAwardKind? AwardKind, int Priority);

/// <summary>
/// Client-side semantic consumer. The server decides the fact; this service
/// only performs bounded deduplication, priority, cooldown, and asset choice.
/// Its dequeue surface is the presentation/audio integration boundary; this
/// pass deliberately does not invent a file loader or claim that custom audio
/// has been played.
/// </summary>
public sealed class AnnouncerService
{
    public const int QueueCapacity = 8;
    public const int DedupCapacity = 128;
    public const int CacheCapacity = 32;
    public const uint CooldownTicks = 15;

    private readonly AnnouncerPack _pack;
    private readonly AnnouncerCue[] _queue = new AnnouncerCue[QueueCapacity];
    private readonly uint[] _seenAwards = new uint[DedupCapacity];
    private readonly string[] _assetCache = new string[CacheCapacity];
    private int _queueCount, _seenCount, _seenHead, _cacheCount;
    private uint _lastTick;
    private int _lastPriority;
    private bool _hasLast;

    public int QueuedCount => _queueCount;
    public int CachedAssetCount => _cacheCount;
    public long DuplicateAwards { get; private set; }
    public long DroppedCues { get; private set; }
    public long PlayedCues { get; private set; }

    public AnnouncerService(AnnouncerPack? pack = null) => _pack = pack ?? AnnouncerPack.BuiltIn();

    public static AnnouncerService FromOptionalPack(InstalledOptionalPresentationPack? installed)
        => new(AnnouncerPack.FromOptionalPack(installed));

    public bool Consume(in MatchAward award)
    {
        if (!AnnouncerEventMapping.TryFromAward(award, out AnnouncerEvent value)) return false;
        if (!RememberAward(award.AwardId))
        {
            DuplicateAwards++;
            return false;
        }
        return Enqueue(new(value, Resolve(value), award.Tick, award.AwardId, award.Kind, award.Priority));
    }

    public bool Consume(in MatchEvent value)
    {
        if (!AnnouncerEventMapping.TryFromMatchEvent(value, out AnnouncerEvent eventKind)) return false;
        return Enqueue(new(eventKind, Resolve(eventKind), value.Tick, 0, null, Priority(eventKind)));
    }

    public bool Consume(in MatchEvent value, byte localTeam)
    {
        if (!AnnouncerEventMapping.TryFromMatchEvent(value, localTeam, out AnnouncerEvent eventKind))
            return false;
        return Enqueue(new(eventKind, Resolve(eventKind), value.Tick, 0, null, Priority(eventKind)));
    }

    public bool TryDequeue(out AnnouncerCue cue)
    {
        if (_queueCount == 0) { cue = default; return false; }
        int best = 0;
        for (int i = 1; i < _queueCount; i++)
            if (_queue[i].Priority > _queue[best].Priority) best = i;
        cue = _queue[best];
        for (int i = best + 1; i < _queueCount; i++) _queue[i - 1] = _queue[i];
        _queue[--_queueCount] = default;
        PlayedCues++;
        return true;
    }

    public void Reset()
    {
        Array.Clear(_queue); Array.Clear(_seenAwards); Array.Clear(_assetCache);
        _queueCount = _seenCount = _seenHead = _cacheCount = 0;
        _lastTick = 0; _lastPriority = 0; _hasLast = false;
        DuplicateAwards = DroppedCues = PlayedCues = 0;
    }

    private bool Enqueue(in AnnouncerCue cue)
    {
        uint age = _hasLast ? unchecked(cue.Tick - _lastTick) : uint.MaxValue;
        bool isNewer = !_hasLast || cue.Tick == _lastTick || Sequence32.IsNewer(cue.Tick, _lastTick);
        if (_hasLast && isNewer && age < CooldownTicks && cue.Priority <= _lastPriority)
        {
            DroppedCues++;
            return false;
        }
        _lastTick = cue.Tick; _lastPriority = cue.Priority; _hasLast = true;
        if (_queueCount < QueueCapacity) { _queue[_queueCount++] = cue; return true; }
        int lowest = 0;
        for (int i = 1; i < _queueCount; i++)
            if (_queue[i].Priority < _queue[lowest].Priority) lowest = i;
        if (cue.Priority <= _queue[lowest].Priority)
        {
            DroppedCues++;
            return false;
        }
        _queue[lowest] = cue;
        DroppedCues++;
        return true;
    }

    private string Resolve(AnnouncerEvent value)
    {
        string asset = _pack.Resolve(value);
        for (int i = 0; i < _cacheCount; i++) if (_assetCache[i] == asset) return asset;
        if (_cacheCount < CacheCapacity) _assetCache[_cacheCount++] = asset;
        return asset;
    }

    private bool RememberAward(uint id)
    {
        for (int i = 0; i < _seenCount; i++)
            if (_seenAwards[(_seenHead - 1 - i + DedupCapacity) % DedupCapacity] == id) return false;
        _seenAwards[_seenHead] = id; _seenHead = (_seenHead + 1) % DedupCapacity;
        if (_seenCount < DedupCapacity) _seenCount++;
        return true;
    }

    private static int Priority(AnnouncerEvent value) => value switch
    {
        AnnouncerEvent.TripleKill => 100,
        AnnouncerEvent.DoubleKill => 90,
        AnnouncerEvent.FirstHunt => 80,
        AnnouncerEvent.PrimeSlayer => 70,
        AnnouncerEvent.Interceptor => 60,
        AnnouncerEvent.Defender => 50,
        AnnouncerEvent.Capture => 40,
        AnnouncerEvent.Assist => 20,
        AnnouncerEvent.Overtime or AnnouncerEvent.MatchPoint => 95,
        AnnouncerEvent.Victory or AnnouncerEvent.Defeat => 100,
        _ => 10
    };
}
