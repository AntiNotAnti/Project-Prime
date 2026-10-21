using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead.Mods.Network;

// Independent of lobby Ready/revision and of the post-load CWM1 source barrier.
// Protocol 81 currently admits exactly one package into the single-weapon runtime.
internal readonly record struct WeaponPackagePreparationKey(ushort Match, ulong Epoch,
    ulong Configuration, byte Slot, ushort Generation, ulong Connection, WeaponDigest Manifest)
{
    internal const int Size = 61;
    internal bool IsValid => Match != 0 && Epoch != 0 && Configuration != 0 && Slot < 8
        && Generation != 0 && Connection != 0 && Manifest.IsValid;
    internal void Write(Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, Match);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[2..], Epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[10..], Configuration);
        bytes[18] = Slot;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[19..], Generation);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[21..], Connection);
        Manifest.Write(bytes[29..61]);
    }
    internal static bool TryRead(ReadOnlySpan<byte> bytes, out WeaponPackagePreparationKey key)
    {
        key = default;
        if (bytes.Length < Size) return false;
        key = new(BinaryPrimitives.ReadUInt16LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes[2..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[10..]), bytes[18], BinaryPrimitives.ReadUInt16LittleEndian(bytes[19..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[21..]), WeaponDigest.Read(bytes[29..61]));
        return key.IsValid;
    }
}

public sealed partial class DedicatedServer
{
    private WeaponMatchPackageBinding? _weaponPackages;
    private ulong _weaponPackageConfiguration;
    private readonly WeaponPackagePreparationKey?[] _weaponPackageReady = new WeaponPackagePreparationKey?[8];
    private double _nextWeaponPackageOffer;

    // Operator-selected, already installed bytes only. No paths or archives come
    // from a peer; configuration is immutable once the server socket is running.
    public void ConfigureWeaponPackage(string storeDirectory, string archiveHash)
    {
        if (_running || _weaponPackages != null) throw new InvalidOperationException("Configure one package before starting the server.");
        var store = new WeaponPackageStore(storeDirectory);
        using var selected = store.AcquireInstalled(archiveHash);
        var manifest = new CustomWeaponPackageManifest(new[] { selected.Package });
        _weaponPackages = WeaponMatchPackageBinding.BindInstalled(store, manifest);
        do { _weaponPackageConfiguration = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8)); }
        while (_weaponPackageConfiguration == 0);
        RiftlineM7Available = true;
        _sessionPolicy = ServerSessionPolicy.Lobby;
    }

    private WeaponPackagePreparationKey PackageKey(Peer peer) => new(_matchId, _authorityEpoch,
        _weaponPackageConfiguration, (byte)peer.SlotIndex, _slotGenerations[peer.SlotIndex],
        _transport?.ConnectionStats(peer.EndPoint)?.ConnectionId ?? 0, _weaponPackages?.Manifest.Digest ?? default);

    private bool PackagePeerReady(Peer peer) => !CurrentDefinition.PackagedWeaponAvailable
        || _weaponPackages != null && PackageKey(peer) is { IsValid: true } key && _weaponPackageReady[peer.SlotIndex] == key;

    internal bool IsWeaponPackagePrepared(int slot) => CurrentDefinition.PackagedWeaponAvailable
        && _peers.Find(peer => peer.SlotIndex == slot) is { } peer && PackagePeerReady(peer);

    private void PumpWeaponPackagePreparation(double now)
    {
        if (!CurrentDefinition.PackagedWeaponAvailable || _weaponPackages == null || _transport == null) return;
        if (now < _nextWeaponPackageOffer) return;
        _nextWeaponPackageOffer = now + .5;
        foreach (var peer in _peers)
        {
            var key = PackageKey(peer);
            if (!key.IsValid || PackagePeerReady(peer)) continue;
            var bytes = new byte[WeaponPackagePreparationKey.Size + _weaponPackages.Manifest.Size];
            key.Write(bytes); _weaponPackages.Manifest.Write(bytes.AsSpan(WeaponPackagePreparationKey.Size));
            _transport.TrySendSemantic(peer.EndPoint, PacketType.WeaponPackageOffer, bytes);
        }
    }

    private void HandleWeaponPackageReady(ReceivedPacket packet, double now)
    {
        if (!CurrentDefinition.PackagedWeaponAvailable || _weaponPackages == null || _transport == null
            || packet.Payload.Length != WeaponPackagePreparationKey.Size || Find(packet.Sender) is not { } peer
            || !WeaponPackagePreparationKey.TryRead(packet.Payload, out var key) || key != PackageKey(peer)
            || packet.ConnectionId != key.Connection) return;
        _weaponPackageReady[peer.SlotIndex] = key; peer.LastSeen = now;
        _transport.TrySendSemantic(peer.EndPoint, PacketType.WeaponPackageConfirmed, packet.Payload);
    }

    private bool RequirePackagesForStart(MatchDefinition match, out string reason)
    {
        reason = "";
        if (!match.PackagedWeaponAvailable) return true;
        if (_weaponPackages == null || _weaponPackages.Count != 1)
        { reason = "The server has no exact single-weapon package binding."; return false; }
        var waiting = _peers.FirstOrDefault(peer => !PackagePeerReady(peer));
        if (waiting != null) { reason = $"Waiting for {waiting.Name} to verify the exact installed weapon package."; return false; }
        // CW-12b establishes a real pre-load barrier. The next slice connects
        // the retained definition/resources to authority and ordinary clients.
        reason = "Package preparation is complete; online package execution is not enabled in this build.";
        return false;
    }
}

public static partial class NetSession
{
    private sealed class WeaponPackagePreparation(WeaponPackagePreparationKey key)
    {
        internal readonly WeaponPackagePreparationKey Key = key;
        internal readonly CancellationTokenSource Cancel = new();
        internal Task<WeaponMatchPackageBinding>? Work;
        internal WeaponMatchPackageBinding? Binding;
        internal bool Confirmed;
        internal string? Error;
        internal double NextAck;
    }
    private static WeaponPackagePreparation? _weaponPreparation;
    // A retired synchronous parser cannot be interrupted midway. Keep its task
    // as a fence across Stop/reconnect so another parser cannot overlap it.
    private static Task<WeaponMatchPackageBinding>? _weaponPackageWorker;
    public static string WeaponPackagePreparationMessage => _weaponPreparation?.Error
        ?? (ServerSession?.Match.PackagedWeaponAvailable == true && !RequiredWeaponPackagesReady
            ? "Waiting for the exact installed weapon package to be verified." : "");
    internal static string WeaponPackageStoreDirectory => Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_STORE")
        is { Length: > 0 } path ? path : Path.Combine(Platform.AppPaths.UserDataDirectory, "weapons");
    public static bool RequiredWeaponPackagesReady => ServerSession is not { Match.PackagedWeaponAvailable: true }
        || !_playback && _weaponPreparation is { Confirmed: true, Binding: not null, Error: null } state && CurrentPackageKey(state.Key);

    private static bool CurrentPackageKey(WeaponPackagePreparationKey key) => !_playback && Role == NetRole.Client
        && ServerSession is { Match.PackagedWeaponAvailable: true } session && session.MatchId == key.Match
        && session.AuthorityEpoch == key.Epoch && LocalSlot == key.Slot
        && NetPlayerLifecycle.Generation(LocalSlot) == key.Generation && _hostEndPoint != null
        && _transport?.ConnectionStats(_hostEndPoint)?.ConnectionId == key.Connection;

    private static void ResetWeaponPackagePreparation()
    {
        var old = _weaponPreparation; _weaponPreparation = null;
        if (old == null) return;
        old.Cancel.Cancel(); old.Binding?.Dispose();
        if (old.Work is { } work) _ = work.ContinueWith(task =>
        {
            if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
            else _ = task.Exception;
            Interlocked.CompareExchange(ref _weaponPackageWorker, null, work);
            old.Cancel.Dispose();
        }, TaskScheduler.Default);
        else old.Cancel.Dispose();
    }

    private static void HandleWeaponPackagePreparation(ReceivedPacket packet)
    {
        if (!WeaponPackagePreparationKey.TryRead(packet.Payload, out var key) || !CurrentPackageKey(key)
            || packet.ConnectionId != key.Connection) return;
        if (packet.Type == PacketType.WeaponPackageConfirmed)
        {
            if (packet.Payload.Length == WeaponPackagePreparationKey.Size && _weaponPreparation is { Binding: not null } current
                && current.Key == key && current.Error == null)
            {
                current.Confirmed = true;
                if (_pendingLoadedScene is { } loaded && loaded.MatchId == key.Match && loaded.AuthorityEpoch == key.Epoch)
                    MarkMatchLoaded();
            }
            return;
        }
        if (packet.Type != PacketType.WeaponPackageOffer
            || !CustomWeaponPackageManifest.TryRead(packet.Payload[WeaponPackagePreparationKey.Size..], out var manifest)
            || manifest!.Digest != key.Manifest || manifest.Rows.Count != 1) return;
        if (_weaponPreparation?.Key == key) return;
        // Operator package selection is immutable for this match/incarnation.
        // Ignore contradictory offers instead of spawning unbounded workers.
        if (_weaponPreparation is { } pinned && CurrentPackageKey(pinned.Key)) return;
        ResetWeaponPackagePreparation();
        if (_weaponPackageWorker is { IsCompleted: false }) return;
        var state = new WeaponPackagePreparation(key); _weaponPreparation = state;
        state.Work = Task.Run(() => WeaponMatchPackageBinding.BindInstalled(
            new WeaponPackageStore(WeaponPackageStoreDirectory), manifest, state.Cancel.Token), state.Cancel.Token);
        _weaponPackageWorker = state.Work;
    }

    private static void PumpWeaponPackagePreparation(double now)
    {
        if (_weaponPreparation is not { } state) return;
        if (!CurrentPackageKey(state.Key)) { ResetWeaponPackagePreparation(); return; }
        if (state.Binding == null && state.Error == null && state.Work is { IsCompleted: true } work)
        {
            try { state.Binding = work.GetAwaiter().GetResult(); }
            catch (Exception) { state.Error = "The exact required weapon package is unavailable or invalid. Install it explicitly before joining."; }
        }
        if (state.Binding == null || state.Error != null || state.Confirmed || now < state.NextAck || _hostEndPoint == null) return;
        state.NextAck = now + .5;
        var bytes = new byte[WeaponPackagePreparationKey.Size]; state.Key.Write(bytes);
        _transport?.TrySendSemantic(_hostEndPoint, PacketType.WeaponPackageReady, bytes);
    }

    internal static void RequireWeaponPackagesForLoad()
    {
        if (!RequiredWeaponPackagesReady) throw new InvalidDataException("Exact weapon packages have not completed preparation.");
    }
}
