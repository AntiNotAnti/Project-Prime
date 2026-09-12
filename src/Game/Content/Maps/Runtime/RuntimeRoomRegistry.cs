using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace MphRead.Mods.MapGen;

public enum MapRuntimeAvailability
{
    Available,
    Removed,
    MissingContent,
    Invalid
}

/// <summary>
/// The legacy integer assigned to one room inside this process. The integer is
/// deliberately absent from package, network, replay, and catalog identity.
/// </summary>
public sealed record RuntimeRoomRegistration(
    int RuntimeId,
    string RoomKey,
    MapContentIdentity? ContentIdentity,
    RoomMetadata Metadata,
    bool IsCustom,
    MapRuntimeAvailability Availability = MapRuntimeAvailability.Available);

public sealed record RuntimeRoomRegistrySnapshot(
    ImmutableArray<RuntimeRoomRegistration> Rooms,
    FrozenDictionary<string, RuntimeRoomRegistration> ByName,
    FrozenDictionary<int, RuntimeRoomRegistration> ById,
    FrozenDictionary<MapContentIdentity, RuntimeRoomRegistration> ByContent)
{
    public static RuntimeRoomRegistrySnapshot Empty { get; } = Create([], [], []);

    internal static RuntimeRoomRegistrySnapshot Create(
        IEnumerable<RuntimeRoomRegistration> rooms,
        IEnumerable<RuntimeRoomRegistration> namedRooms,
        IEnumerable<RuntimeRoomRegistration> contentVariants)
    {
        ImmutableArray<RuntimeRoomRegistration> available = rooms
            .Where(room => room.Availability == MapRuntimeAvailability.Available)
            .OrderBy(room => room.RuntimeId)
            .ToImmutableArray();
        return new RuntimeRoomRegistrySnapshot(available,
            namedRooms
                .Where(room => room.Availability
                    == MapRuntimeAvailability.Available)
                .ToFrozenDictionary(room => room.RoomKey,
                StringComparer.OrdinalIgnoreCase),
            available.ToFrozenDictionary(room => room.RuntimeId),
            contentVariants
                .Where(room => room.Availability
                    == MapRuntimeAvailability.Available
                    && room.ContentIdentity != null)
                .ToFrozenDictionary(room => room.ContentIdentity!));
    }
}

/// <summary>
/// Owns the only process-local mapping from stable map identities to the
/// numeric room IDs required by the original MPH runtime. Room names remain a
/// compatibility lookup and are not unique map identities. Existing
/// allocations never move and removed IDs are never reassigned during the
/// process lifetime.
/// </summary>
public sealed class RuntimeRoomRegistry
{
    public const int FirstCustomRoomId = 138;

    private readonly object _sync = new();
    private readonly Dictionary<string, RuntimeRoomRegistration> _roomsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, RuntimeRoomRegistration> _roomsById = [];
    private readonly Dictionary<string, RuntimeRoomRegistration> _customByStableId =
        new(StringComparer.Ordinal);
    private readonly Dictionary<MapContentIdentity, RuntimeRoomRegistration>
        _customByContent = [];
    private RuntimeRoomRegistrySnapshot _snapshot = RuntimeRoomRegistrySnapshot.Empty;
    private int _nextCustomRoomId = FirstCustomRoomId;

    public static RuntimeRoomRegistry Shared { get; } = new();

    public RuntimeRoomRegistrySnapshot Snapshot => Volatile.Read(ref _snapshot);

    public RuntimeRoomRegistration? FindByName(string roomKey)
        => String.IsNullOrWhiteSpace(roomKey)
            ? null : Snapshot.ByName.GetValueOrDefault(roomKey);

    public RuntimeRoomRegistration? FindByName(string roomKey,
        MapContentIdentity? contentIdentity)
    {
        if (String.IsNullOrWhiteSpace(roomKey)) return null;
        if (contentIdentity != null)
        {
            return Snapshot.ByContent.TryGetValue(contentIdentity,
                    out RuntimeRoomRegistration? exact)
                && exact.RoomKey.Equals(roomKey,
                    StringComparison.OrdinalIgnoreCase)
                    ? exact : null;
        }
        return FindByName(roomKey);
    }

    public RuntimeRoomRegistration? FindById(int runtimeId)
        => Snapshot.ById.GetValueOrDefault(runtimeId);

    public RuntimeRoomRegistration? FindById(int runtimeId,
        MapContentIdentity? contentIdentity)
    {
        if (contentIdentity != null)
        {
            return Snapshot.ByContent.TryGetValue(contentIdentity,
                    out RuntimeRoomRegistration? exact)
                && exact.RuntimeId == runtimeId ? exact : null;
        }
        return FindById(runtimeId);
    }

    public RuntimeRoomRegistration Require(string roomKey)
        => FindByName(roomKey) ?? throw new MapRuntimeException("MAP-RUN-001",
            $"Runtime room registration is missing for '{roomKey}'.");

    public RuntimeRoomRegistration Require(string roomKey,
        MapContentIdentity? contentIdentity)
        => FindByName(roomKey, contentIdentity)
            ?? throw new MapRuntimeException("MAP-RUN-001",
                $"Runtime room registration is missing for '{roomKey}'.");

    internal void EnsureBuiltIns(IReadOnlyDictionary<int, string> ids,
        IReadOnlyList<RoomMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(metadata);
        lock (_sync)
        {
            Dictionary<string, RoomMetadata> byName = metadata.ToDictionary(
                room => room.Name, StringComparer.OrdinalIgnoreCase);
            foreach ((int runtimeId, string roomKey) in ids.OrderBy(pair => pair.Key))
            {
                if (!byName.TryGetValue(roomKey, out RoomMetadata? roomMetadata))
                    throw new MapRuntimeException("MAP-RUN-008",
                        $"Built-in room metadata is missing for '{roomKey}'.");
                RegisterBuiltInCore(runtimeId, roomKey, roomMetadata);
            }
            PublishCore();
        }
    }

    public void Synchronize(MapCatalogSnapshot catalog,
        Func<InstalledMap, int, RoomMetadata> metadataFactory)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(metadataFactory);
        lock (_sync)
        {
            foreach ((string key, RuntimeRoomRegistration registration) in
                _roomsByName.ToArray())
                if (registration.IsCustom)
                    _roomsByName.Remove(key);
            foreach ((string stableId, RuntimeRoomRegistration registration) in
                _customByStableId.ToArray())
            {
                RuntimeRoomRegistration removed = registration with
                {
                    Availability = MapRuntimeAvailability.Removed
                };
                _customByStableId[stableId] = removed;
                _roomsById[removed.RuntimeId] = removed;
            }

            foreach (InstalledMap map in catalog.Maps
                .OrderBy(map => map.Project.Map.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(map => map.ContentIdentity.Identity.StableId,
                    StringComparer.Ordinal)
                .ThenBy(map => map.ContentIdentity.Identity.Version))
            {
                RegisterCustomCore(map, metadataFactory);
            }
            foreach ((string roomKey, InstalledMap map) in catalog.ByRoomName)
            {
                RuntimeRoomRegistration selected =
                    _customByContent[map.ContentIdentity];
                if (!selected.RoomKey.Equals(roomKey,
                    StringComparison.OrdinalIgnoreCase))
                    throw new MapRuntimeException("MAP-RUN-008",
                        $"Catalog room key '{roomKey}' does not match {map.ContentIdentity}.");
                SetDefaultCore(selected);
            }
            PublishCore();
        }
    }

    /// <summary>
    /// Activates an exact installed version for a room key without changing
    /// its already allocated runtime ID.
    /// </summary>
    public RuntimeRoomRegistration RegisterCustom(InstalledMap map,
        Func<InstalledMap, int, RoomMetadata> metadataFactory)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(metadataFactory);
        lock (_sync)
        {
            RuntimeRoomRegistration registration = RegisterCustomCore(map,
                metadataFactory);
            PublishCore();
            return registration;
        }
    }

    private void RegisterBuiltInCore(int runtimeId, string roomKey,
        RoomMetadata metadata)
    {
        if (runtimeId >= FirstCustomRoomId)
            throw new MapRuntimeException("MAP-RUN-002",
                $"Built-in room '{roomKey}' uses reserved custom room ID {runtimeId}.");
        if (_roomsById.TryGetValue(runtimeId, out RuntimeRoomRegistration? byId))
        {
            if (!byId.IsCustom
                && byId.RoomKey.Equals(roomKey, StringComparison.OrdinalIgnoreCase))
                return;
            throw Conflict(roomKey, runtimeId);
        }
        if (_roomsByName.TryGetValue(roomKey, out RuntimeRoomRegistration? byName))
        {
            if (!byName.IsCustom && byName.RuntimeId == runtimeId) return;
            throw Conflict(roomKey, runtimeId);
        }
        AddCore(new RuntimeRoomRegistration(runtimeId, roomKey, null, metadata,
            IsCustom: false));
    }

    private RuntimeRoomRegistration RegisterCustomCore(InstalledMap map,
        Func<InstalledMap, int, RoomMetadata> metadataFactory)
    {
        string roomKey = map.Project.Map.Name;
        if (String.IsNullOrWhiteSpace(roomKey))
            throw new MapRuntimeException("MAP-RUN-008",
                $"Map {map.ContentIdentity} has no runtime room key.");

        string stableId = map.ContentIdentity.Identity.StableId;
        if (_customByStableId.TryGetValue(stableId,
            out RuntimeRoomRegistration? existing))
        {
            if (_customByContent.TryGetValue(map.ContentIdentity,
                    out RuntimeRoomRegistration? exact))
            {
                MapRuntimeAvailability availability = Availability(map);
                var refreshed = exact with
                {
                    Metadata = metadataFactory(map, existing.RuntimeId),
                    // Once an exact content identity has been available to a
                    // match, catalog removal or a transient source failure
                    // cannot revoke its process-local metadata underneath it.
                    Availability = exact.Availability
                        == MapRuntimeAvailability.Available
                            ? MapRuntimeAvailability.Available : availability
                };
                _customByContent[map.ContentIdentity] = refreshed;
                _customByStableId[stableId] = refreshed;
                _roomsById[existing.RuntimeId] = refreshed;
                return refreshed;
            }
            var variant = new RuntimeRoomRegistration(existing.RuntimeId,
                roomKey, map.ContentIdentity,
                metadataFactory(map, existing.RuntimeId), IsCustom: true,
                Availability(map));
            _customByContent.Add(map.ContentIdentity, variant);
            _customByStableId[stableId] = variant;
            _roomsById[existing.RuntimeId] = variant;
            return variant;
        }

        int runtimeId = checked(_nextCustomRoomId++);
        if (_roomsById.ContainsKey(runtimeId)) throw Conflict(roomKey, runtimeId);
        var registration = new RuntimeRoomRegistration(runtimeId, roomKey,
            map.ContentIdentity, metadataFactory(map, runtimeId), IsCustom: true,
            Availability(map));
        _roomsById.Add(runtimeId, registration);
        _customByStableId.Add(stableId, registration);
        _customByContent.Add(map.ContentIdentity, registration);
        return registration;
    }

    private static MapRuntimeAvailability Availability(InstalledMap map)
        => map.BuildState switch
        {
            MapBuildState.Invalid or MapBuildState.Unsupported
                => MapRuntimeAvailability.Invalid,
            MapBuildState.MissingDependency => MapRuntimeAvailability.MissingContent,
            _ => MapRuntimeAvailability.Available
        };

    private void AddCore(RuntimeRoomRegistration registration)
    {
        _roomsByName.Add(registration.RoomKey, registration);
        _roomsById.Add(registration.RuntimeId, registration);
    }

    private void SetDefaultCore(RuntimeRoomRegistration registration)
    {
        if (_roomsByName.TryGetValue(registration.RoomKey,
                out RuntimeRoomRegistration? existing) && !existing.IsCustom)
            return;
        _roomsByName[registration.RoomKey] = registration;
        _roomsById[registration.RuntimeId] = registration;
        if (registration.ContentIdentity != null)
            _customByStableId[registration.ContentIdentity.Identity.StableId]
                = registration;
    }

    private void PublishCore()
        => Volatile.Write(ref _snapshot,
            RuntimeRoomRegistrySnapshot.Create(_roomsById.Values,
                _roomsByName.Values,
                _customByContent.Values));

    private static MapRuntimeException Conflict(string roomKey, int runtimeId)
        => new("MAP-RUN-002",
            $"Runtime room registration conflicts at '{roomKey}' / {runtimeId}.");

    internal void ResetForTests()
    {
        lock (_sync)
        {
            _roomsByName.Clear();
            _roomsById.Clear();
            _customByStableId.Clear();
            _customByContent.Clear();
            _nextCustomRoomId = FirstCustomRoomId;
            PublishCore();
        }
    }
}

public sealed class MapRuntimeException : ProgramException
{
    public string Code { get; }
    public string DiagnosticMessage { get; }

    public MapRuntimeException(string code, string message)
        : base($"{code}: {message}")
    {
        Code = code;
        DiagnosticMessage = message;
    }
}
