using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

internal static class HostRequestIdentityAcceptance
{
    internal static void Run()
    {
        const string room = "SYNTHETIC EXACT ARENA";
        var pinned = new NetworkMapIdentity(Guid.Parse("7301e038-e90b-42d5-81ae-5cde5a18f143"),
            new MapHash256(1, 2, 3, 4), new MapHash256(5, 6, 7, 8), NetworkMapFlags.Custom);
        var replacement = pinned with { PackageHash = new MapHash256(9, 10, 11, 12) };
        const string builtIn = "MP1 SANCTORUS";
        var lookedUp = new List<string>();
        NetworkMapIdentity Resolve(string key)
        {
            lookedUp.Add(key);
            return key.Equals(builtIn, StringComparison.OrdinalIgnoreCase) ? default : replacement;
        }
        var rotation = new (string RoomKey, GameMode Mode)[]
        {
            (room, GameMode.Battle), (room.ToLowerInvariant(), GameMode.None),
            (builtIn, GameMode.Battle), ("OTHER CUSTOM ARENA", GameMode.Battle)
        };
        var exact = Prepare(room, rotation, pinned, Resolve);
        Check(exact.Identity == pinned && exact.Rotation![0].PackageHash == pinned.PackageHash
            && exact.Rotation[1].PackageHash == pinned.PackageHash
            && !lookedUp.Any(key => key.Equals(room, StringComparison.OrdinalIgnoreCase)),
            "explicit first map and repeated rotation entries never resolve their identity by room name");
        Check(exact.Rotation![1].Mode == GameMode.Battle
            && exact.Rotation[2].PackageHash.IsZero
            && exact.Rotation[3].PackageHash == replacement.PackageHash,
            "exact rotation preserves mode normalization and resolves other entries normally");

        var packet = new HostRequestPacket
        {
            Protocol = NetConfig.ProtocolVersion, RoomKey = room, MapIdentity = exact.Identity,
            Rotation = exact.Rotation, Mode = (byte)GameMode.Battle, MaxPlayers = 4,
            ServerName = "Exact arena", Policy = ServerSessionPolicy.Lobby,
            HostNonce = 1, HostCookie = 2
        };
        byte[] bytes = new byte[packet.Length];
        packet.Write(bytes);
        var readback = HostRequestPacket.Read(bytes);
        Check(readback.MapIdentity == pinned && readback.Rotation![0].PackageHash == pinned.PackageHash
            && readback.Rotation[1].PackageHash == pinned.PackageHash && readback.RequiresMapPreparation,
            "host request wire retains exact immutable first identity and rotation package hashes");

        lookedUp.Clear();
        var legacy = Prepare(room, rotation, null, Resolve);
        Check(legacy.Identity == replacement && legacy.Rotation![0].PackageHash == replacement.PackageHash
            && lookedUp.Count(key => key.Equals(room, StringComparison.OrdinalIgnoreCase)) == 3,
            "legacy host path continues resolving first map and rotation by room name");
        var builtInOnly = Prepare(builtIn, null, null, Resolve);
        Check(builtInOnly.Identity == default && builtInOnly.Rotation == null,
            "legacy built-in request retains zero identity and null rotation");

        foreach (var invalid in new[] { default(NetworkMapIdentity), pinned with { MapId = Guid.Empty },
            pinned with { PackageHash = default }, pinned with { Flags = (NetworkMapFlags)128 } })
            Refuses(room, invalid);
        Refuses(builtIn, pinned);
        Refuses("", pinned);
        var exactOnly = Prepare(room, null, pinned,
            _ => throw new InvalidOperationException("Unexpected lookup"));
        Check(exactOnly.Identity == pinned && exactOnly.Rotation == null,
            "exact first map without rotation never performs a name lookup");
        var capped = Prepare(room, Enumerable.Repeat((room, GameMode.Battle),
            HostRequestPacket.MaxRotation + 2).ToArray(), pinned, Resolve);
        Check(capped.Rotation!.Count == HostRequestPacket.MaxRotation,
            "pure preparation preserves established rotation wire cap");
    }

    private static (NetworkMapIdentity Identity, IReadOnlyList<HostRotationEntry>? Rotation) Prepare(
        string room, IReadOnlyList<(string RoomKey, GameMode Mode)>? rotation,
        NetworkMapIdentity? required, Func<string, NetworkMapIdentity> resolve)
    {
        var method = typeof(NetMasterClient).GetMethod("PrepareRequestedMaps",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Production map preparation helper is unavailable.");
        try
        {
            return ((NetworkMapIdentity, IReadOnlyList<HostRotationEntry>?))method.Invoke(null,
                new object?[] { room, rotation, required, resolve })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException)
        {
            throw ex.InnerException!;
        }
    }

    private static void Refuses(string room, NetworkMapIdentity required)
    {
        bool refused = false;
        try { Prepare(room, null, required, _ => throw new InvalidOperationException("Invalid target reached lookup")); }
        catch (ArgumentException) { refused = true; }
        Check(refused, "invalid explicit custom target is rejected before identity lookup");
    }

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine("RML93 HOST WIRE PASS " + description);
    }
}
