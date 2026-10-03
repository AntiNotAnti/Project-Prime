using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;

namespace MphRead.Mods.Network;

public readonly record struct HostChallengePacket(byte Protocol, ulong Nonce)
{
    public const int Size = 9;
    public void Write(Span<byte> dest)
    {
        dest[0] = Protocol;
        BinaryPrimitives.WriteUInt64LittleEndian(dest[1..], Nonce);
    }
    public static bool TryRead(ReadOnlySpan<byte> src, out HostChallengePacket packet)
    {
        packet = default;
        if (src.Length != Size) return false;
        packet = new(src[0], BinaryPrimitives.ReadUInt64LittleEndian(src[1..]));
        return true;
    }
}

public readonly record struct HostChallengeReplyPacket(ulong Nonce, ulong Cookie)
{
    public const int Size = 16;
    public void Write(Span<byte> dest)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(dest, Nonce);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[8..], Cookie);
    }
    public static bool TryRead(ReadOnlySpan<byte> src, out HostChallengeReplyPacket packet)
    {
        packet = default;
        if (src.Length != Size) return false;
        packet = new(BinaryPrimitives.ReadUInt64LittleEndian(src),
            BinaryPrimitives.ReadUInt64LittleEndian(src[8..]));
        return true;
    }
}

/// <summary>
/// Stateless proof-of-return-path plus bounded process-spawn admission.
/// A spoofed HostRequest cannot consume a game port because only the endpoint
/// that received a fresh challenge cookie can present it back. Spawn budgets
/// are process-wide so multiple HostPool owners cannot multiply capacity.
/// </summary>
internal static class HostRequestGuard
{
    private sealed class Bucket
    {
        public double Tokens;
        public double Last;
    }

    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(32);
    private static readonly object Gate = new();
    private static readonly Dictionary<IPAddress, Bucket> PerAddress = new();
    private static readonly Bucket Global = new() { Tokens = 10 };
    private const double CookieBucketSeconds = 30;
    private const double AddressRatePerSecond = 6.0 / 60.0;
    private const double GlobalRatePerSecond = 30.0 / 60.0;
    private const double AddressBurst = 3;
    private const double GlobalBurst = 10;

    internal static HostChallengeReplyPacket Challenge(
        IPEndPoint sender, HostChallengePacket challenge, double now)
        => new(challenge.Nonce, Cookie(sender, challenge.Nonce,
            (long)Math.Floor(now / CookieBucketSeconds)));

    internal static bool Validate(IPEndPoint sender, HostRequestPacket request,
        double now, out string reason)
    {
        reason = "";
        long bucket = (long)Math.Floor(now / CookieBucketSeconds);
        ulong current = Cookie(sender, request.HostNonce, bucket);
        ulong prior = Cookie(sender, request.HostNonce, bucket - 1);
        if (request.HostNonce == 0
            || request.HostCookie != current && request.HostCookie != prior)
        {
            reason = "hosting challenge expired; retry lobby creation";
            return false;
        }
        lock (Gate)
        {
            if (!Take(Global, now, GlobalRatePerSecond, GlobalBurst))
            {
                reason = "host is starting other lobbies; try again shortly";
                return false;
            }
            if (!PerAddress.TryGetValue(sender.Address, out Bucket? address))
            {
                if (PerAddress.Count >= 1024)
                {
                    foreach (var item in new List<IPAddress>(PerAddress.Keys))
                    {
                        if (now - PerAddress[item].Last > 600) PerAddress.Remove(item);
                        if (PerAddress.Count < 768) break;
                    }
                    if (PerAddress.Count >= 1024)
                    {
                        reason = "host admission table is busy; try again shortly";
                        Global.Tokens = Math.Min(GlobalBurst, Global.Tokens + 1);
                        return false;
                    }
                }
                address = new Bucket { Tokens = AddressBurst };
                PerAddress[sender.Address] = address;
            }
            if (!Take(address, now, AddressRatePerSecond, AddressBurst))
            {
                reason = "too many lobby starts from this address; try again shortly";
                Global.Tokens = Math.Min(GlobalBurst, Global.Tokens + 1);
                return false;
            }
        }
        return true;
    }

    private static bool Take(Bucket bucket, double now, double rate, double burst)
    {
        if (bucket.Last == 0)
        {
            bucket.Last = now;
            bucket.Tokens = Math.Max(bucket.Tokens, burst);
        }
        bucket.Tokens = Math.Min(burst,
            bucket.Tokens + Math.Max(0, now - bucket.Last) * rate);
        bucket.Last = now;
        if (bucket.Tokens < 1) return false;
        bucket.Tokens--;
        return true;
    }

    private static ulong Cookie(IPEndPoint sender, ulong nonce, long bucket)
    {
        byte[] address = sender.Address.GetAddressBytes();
        Span<byte> input = stackalloc byte[1 + 16 + 2 + 8 + 8];
        input.Clear();
        input[0] = (byte)address.Length;
        address.CopyTo(input[1..]);
        BinaryPrimitives.WriteUInt16LittleEndian(input[17..], (ushort)sender.Port);
        BinaryPrimitives.WriteUInt64LittleEndian(input[19..], nonce);
        BinaryPrimitives.WriteInt64LittleEndian(input[27..], bucket);
        byte[] digest = HMACSHA256.HashData(Secret, input);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }
}
