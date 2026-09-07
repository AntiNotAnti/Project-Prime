using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;
namespace MphRead.Tests;
public sealed class GameTicketTests
{
    internal sealed class Issuer : IDisposable
    {
        internal readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        internal readonly Guid Server = Guid.NewGuid(), Session = Guid.NewGuid(), Player = Guid.NewGuid();
        internal const string Origin = "https://tickets.example.test";
        internal string Keys
        {
            get
            {
                ECParameters p = Key.ExportParameters(false);
                return JsonSerializer.Serialize(new { keys = new[] { new { kty = "EC", crv = "P-256", alg = "ES256", use = "sig", kid = "test",
                    x = Base64UrlEncoder.Encode(p.Q.X!), y = Base64UrlEncoder.Encode(p.Q.Y!) } } });
            }
        }
        internal string Ticket(ulong nonce = 123, Dictionary<string, object>? overrides = null, ECDsa? signingKey = null, Guid? player = null)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var claims = new Dictionary<string, object>
            {
                ["iss"] = Origin, ["sub"] = (player ?? Player).ToString("D"), ["aud"] = Server.ToString("D"), ["sid"] = Session.ToString("D"),
                ["jti"] = Guid.NewGuid().ToString("D"), ["name"] = "TICKET", ["nonce"] = nonce.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["iat"] = now, ["nbf"] = now, ["exp"] = now + 120
            };
            if (overrides != null) foreach (var pair in overrides) claims[pair.Key] = pair.Value;
            return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
            {
                Claims = claims, SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(signingKey ?? Key) { KeyId = "test" }, SecurityAlgorithms.EcdsaSha256)
            });
        }
        internal TicketVerifier Verifier() { var v = new TicketVerifier(Origin, Server, Session); v.ReplaceKeys(Keys); return v; }
        public void Dispose() => Key.Dispose();
    }
    [Fact]
    public async Task SignedTicketIsBoundToAccountServerIncarnationNonceNameAndReplayOwner()
    {
        using var issuer = new Issuer(); var v = issuer.Verifier();
        var join = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "TICKET", Ticket: issuer.Ticket());
        var ep = new IPEndPoint(IPAddress.Loopback, 4000);
        TicketIdentity? identity = await v.ValidateAsync(join, ep, DateTimeOffset.UtcNow);
        Assert.Equal(new PlayerId(issuer.Player), identity!.Value.PlayerId);
        Assert.Equal(identity, await v.ValidateAsync(join, ep, DateTimeOffset.UtcNow));
        Assert.Null(await v.ValidateAsync(join, new IPEndPoint(IPAddress.Loopback, 4001), DateTimeOffset.UtcNow));
        Assert.Null(await v.ValidateAsync(join with { Nonce = 124 }, ep, DateTimeOffset.UtcNow));
        Assert.Null(await v.ValidateAsync(join with { Name = "OTHER" }, ep, DateTimeOffset.UtcNow));
        Assert.Null(await v.ValidateAsync(join, ep, DateTimeOffset.UtcNow.AddSeconds(121)));
    }
    [Theory]
    [InlineData("iss", "https://evil.example")]
    [InlineData("aud", "11111111-1111-1111-1111-111111111111")]
    [InlineData("sid", "11111111-1111-1111-1111-111111111111")]
    [InlineData("sub", "00000000-0000-0000-0000-000000000000")]
    [InlineData("nonce", "0123")]
    [InlineData("name", "OTHER")]
    [InlineData("jti", "invalid")]
    public async Task WrongSignedClaimFailsClosed(string claim, string value)
    {
        using var issuer = new Issuer();
        var join = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "TICKET", Ticket: issuer.Ticket(overrides: new() { [claim] = value }));
        Assert.Null(await issuer.Verifier().ValidateAsync(join, new(IPAddress.Loopback, 1), DateTimeOffset.UtcNow));
    }
    [Fact]
    public async Task UnknownKeyBadSignatureExcessLifetimeAndDuplicateClaimsFailClosed()
    {
        using var issuer = new Issuer(); using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var v = issuer.Verifier(); var endpoint = new IPEndPoint(IPAddress.Loopback, 1);
        var join = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "TICKET", Ticket: issuer.Ticket(signingKey: other));
        Assert.Null(await v.ValidateAsync(join, endpoint, DateTimeOffset.UtcNow));
        join = join with { Ticket = issuer.Ticket(overrides: new() { ["exp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600 }) };
        Assert.Null(await v.ValidateAsync(join, endpoint, DateTimeOffset.UtcNow));
        string[] parts = issuer.Ticket().Split('.');
        string payload = Base64UrlEncoder.Decode(parts[1]);
        parts[1] = Base64UrlEncoder.Encode(payload[..^1] + ",\"nonce\":\"123\"}");
        byte[] signed = System.Text.Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        parts[2] = Base64UrlEncoder.Encode(issuer.Key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.Null(await v.ValidateAsync(join with { Ticket = string.Join('.', parts) }, endpoint, DateTimeOffset.UtcNow));
        parts = issuer.Ticket().Split('.');
        parts[0] = Base64UrlEncoder.Encode("{\"alg\":\"ES256\",\"kid\":\"unknown\"}");
        Assert.Null(await v.ValidateAsync(join with { Ticket = string.Join('.', parts) }, endpoint, DateTimeOffset.UtcNow));
    }
    [Theory]
    [InlineData("iat", 30)]
    [InlineData("nbf", 30)]
    [InlineData("exp", -1)]
    public async Task FutureOrExpiredSignedTimesFailClosed(string claim, int delta)
    {
        using var issuer = new Issuer();
        var join = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "TICKET",
            Ticket: issuer.Ticket(overrides: new() { [claim] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + delta }));
        Assert.Null(await issuer.Verifier().ValidateAsync(join, new(IPAddress.Loopback, 1), DateTimeOffset.UtcNow));
    }
    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    public async Task AlgorithmSubstitutionNeverFallsBackToAnotherVerifier(string algorithm)
    {
        using var issuer = new Issuer();
        string[] parts = issuer.Ticket().Split('.');
        parts[0] = Base64UrlEncoder.Encode("{\"alg\":\"" + algorithm + "\",\"kid\":\"test\"}");
        var join = new JoinPacket(NetHeader.Version, 123, Hunter.Samus, "TICKET", Ticket: string.Join('.', parts));
        Assert.Null(await issuer.Verifier().ValidateAsync(join, new(IPAddress.Loopback, 1), DateTimeOffset.UtcNow));
    }
    [Fact]
    public void PrivateOrWrongCurveKeysAreNeverAcceptedFromJwks()
    {
        using var issuer = new Issuer(); var verifier = issuer.Verifier();
        Assert.Throws<ArgumentException>(() => verifier.ReplaceKeys(issuer.Keys.Replace("P-256", "P-384")));
        Assert.Throws<ArgumentException>(() => verifier.ReplaceKeys(issuer.Keys.Replace("\"kty\":", "\"d\":\"secret\",\"kty\":")));
        Assert.Throws<ArgumentException>(() => verifier.ReplaceKeys("{\"keys\":[]}"));
    }
    [Fact]
    public void JoinCredentialFitsExistingDatagramAndRejectsMalformedExtension()
    {
        string ticket = new string('A', JoinPacket.MaxTicketBytes - 4) + ".B.C";
        var join = new JoinPacket(NetHeader.Version, 1, Hunter.Samus, "TICKET", Ticket: ticket);
        Assert.Equal(1024, NetHeader.Size + join.EncodedSize);
        Assert.DoesNotContain(ticket, join.ToString());
        Assert.DoesNotContain("operator-secret", new ServerTicketOptions(new Uri("https://tickets.example/"), "https://tickets.example/", Guid.NewGuid(), "operator-secret").ToString());
        byte[] bytes = new byte[join.EncodedSize]; join.Write(bytes);
        Assert.True(JoinPacket.TryRead(bytes, out JoinPacket copy)); Assert.Equal(join, copy);
        Assert.False(JoinPacket.TryRead(bytes.AsSpan(0, bytes.Length - 1), out _));
        bytes[^1] = 0xFF; Assert.False(JoinPacket.TryRead(bytes, out _));
        Assert.Throws<ArgumentException>(() => (join with { Ticket = ticket + "A" }).Write(new byte[1100]));
        byte[] guest = new byte[JoinPacket.Size]; (join with { Ticket = "" }).Write(guest);
        Assert.True(JoinPacket.TryRead(guest, out copy)); Assert.Equal("", copy.Ticket);
        Assert.False(JoinPacket.TryRead(new byte[JoinPacket.Size + 2], out _));
    }
}
