using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.CustomWeapons;

// Capability only: this has no source boundary or action admission authority.
// Immutable reference publication also permits diagnostics to read a whole fact.
internal sealed record CustomPeerReadyEvidence(WorldBootstrapIdentity Bootstrap,WeaponDigest Manifest,
    byte Slot,ulong ConnectionId,uint OpenedWorld);

/// <summary>One reviewed built-in definition in this first live handshake. Unknown
/// packages are never downloaded or interpreted. Exact cached wire comparison
/// verifies every row/field without duplicate page allocation.</summary>
internal sealed class CustomReadinessWire
{
    internal WorldBootstrapIdentity Bootstrap { get; }
    internal byte Slot { get; }
    internal CustomWeaponManifest Manifest { get; }
    internal WeaponDefinition Definition { get; }
    internal byte[] ManifestBytes { get; }
    internal byte[] ReadyBytes { get; }
    internal CustomReadinessWire(WorldBootstrapIdentity bootstrap,byte slot)
    {
        if (!CustomWeaponProtocol.ValidBootstrap(bootstrap) || slot >= 8) throw new ArgumentException("Exact recipient bootstrap required.");
        Bootstrap = bootstrap; Slot = slot;
        Definition=RiftlineM7.Create(); Manifest = new(new[] { Definition });
        var page = new CustomManifestPage(bootstrap,Manifest.Digest,1,0,new[] { Manifest.Rows[0] });
        ManifestBytes = new byte[page.Size]; page.Write(ManifestBytes);
        ReadyBytes = new byte[CustomWeaponReady.Size];
        new CustomWeaponReady(bootstrap,Manifest.Digest,slot,WeaponMechanics.SchemaVersion,
            WeaponMechanics.BehaviorVersion,CustomWeaponReady.PrecisionCapability).Write(ReadyBytes);
    }
}
internal enum CustomReadinessResult : byte { Ignored,Accepted,Duplicate,Invalidated }

internal sealed class CustomReadinessPeer
{
    internal const double RetrySeconds = .25, DeadlineSeconds = 30;
    internal const int OffersPerSecond = 16;
    internal readonly CustomReadinessWire Wire;
    internal readonly ulong ConnectionId;
    private readonly double _created;
    private double _nextSend,_lastTime,_offerWindow;
    private int _offers;
    private CustomPeerReadyEvidence? _evidence;
    internal CustomPeerReadyEvidence? Evidence => System.Threading.Volatile.Read(ref _evidence);
    internal bool Invalidated { get; private set; }
    internal CustomReadinessPeer(WorldBootstrapIdentity bootstrap,byte slot,ulong connectionId,double now)
    {
        if (connectionId == 0 || !double.IsFinite(now)) throw new ArgumentException("Live connection and clock required.");
        Wire = new(bootstrap,slot); ConnectionId = connectionId; _created = _lastTime = _offerWindow = now; _nextSend = now;
    }
    internal void Invalidate()
    { Invalidated = true; System.Threading.Volatile.Write(ref _evidence,null); }
    internal bool Due(double now)
    {
        if (!double.IsFinite(now) || now < _lastTime) return false;
        _lastTime = now;
        if (Evidence == null && now-_created > DeadlineSeconds) Invalidate();
        if (Invalidated || Evidence != null || now < _nextSend) return false;
        _nextSend = now+RetrySeconds; return true;
    }
    internal CustomReadinessResult Receive(ReadOnlySpan<byte> payload,ulong connectionId,uint world,double now)
    {
        if (Invalidated || connectionId != ConnectionId || !double.IsFinite(now) || now < _lastTime)
            return CustomReadinessResult.Ignored;
        _lastTime = now;
        if (now-_offerWindow >= 1) { _offerWindow = now; _offers = 0; }
        if (_offers >= OffersPerSecond) return CustomReadinessResult.Ignored;
        _offers++;
        if (!CustomWeaponProtocol.ReadHeader(payload,out var bootstrap,out _) || bootstrap != Wire.Bootstrap)
            return CustomReadinessResult.Ignored;
        if (Evidence == null && now-_created > DeadlineSeconds || !payload.SequenceEqual(Wire.ReadyBytes))
        { Invalidate(); return CustomReadinessResult.Invalidated; }
        if (Evidence != null) return CustomReadinessResult.Duplicate;
        System.Threading.Volatile.Write(ref _evidence,new(Wire.Bootstrap,Wire.Manifest.Digest,Wire.Slot,ConnectionId,world));
        return CustomReadinessResult.Accepted;
    }
}

internal sealed class CustomReadinessClient
{
    internal readonly CustomReadinessWire Wire;
    internal bool ManifestAccepted { get; private set; }
    internal bool Confirmed { get; private set; }
    internal bool Invalidated { get; private set; }
    private double _nextSend,_lastTime;
    private readonly double _created;
    internal CustomReadinessClient(WorldBootstrapIdentity bootstrap,byte slot,double now = 0)
    {
        if (!double.IsFinite(now)) throw new ArgumentException("Finite clock required.");
        Wire = new(bootstrap,slot); _created = _lastTime = now;
    }
    private bool Time(double now)
    {
        if (!double.IsFinite(now) || now < _lastTime) return false;
        _lastTime = now;
        if (!Confirmed && now-_created > CustomReadinessPeer.DeadlineSeconds) Invalidate();
        return !Invalidated;
    }
    internal CustomReadinessResult Receive(PacketType type,ReadOnlySpan<byte> payload,double now = 0)
    {
        if (!Time(now) || !CustomWeaponProtocol.ReadHeader(payload,out var bootstrap,out _) || bootstrap != Wire.Bootstrap)
            return CustomReadinessResult.Ignored;
        if (type == PacketType.CustomWeaponManifest)
        {
            if (!payload.SequenceEqual(Wire.ManifestBytes)) return Invalidate();
            bool duplicate = ManifestAccepted; ManifestAccepted = true;
            return duplicate ? CustomReadinessResult.Duplicate : CustomReadinessResult.Accepted;
        }
        if (type != PacketType.CustomWeaponReadyConfirmed || !ManifestAccepted) return CustomReadinessResult.Ignored;
        if (!payload.SequenceEqual(Wire.ReadyBytes)) return Invalidate();
        bool repeated = Confirmed; Confirmed = true;
        return repeated ? CustomReadinessResult.Duplicate : CustomReadinessResult.Accepted;
    }
    internal CustomReadinessResult Invalidate()
    { Invalidated = true; Confirmed = false; return CustomReadinessResult.Invalidated; }
    internal bool Due(double now)
    {
        if (!Time(now) || !ManifestAccepted || Confirmed || now < _nextSend) return false;
        _nextSend = now+CustomReadinessPeer.RetrySeconds; return true;
    }
}
