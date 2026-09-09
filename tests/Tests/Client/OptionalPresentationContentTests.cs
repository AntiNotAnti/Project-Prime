using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Audio;
using MphRead.Mods.Content;
using MphRead.Mods.Launcher;
using MphRead.Runtime.Content;
using Xunit;

namespace MphRead.Tests;

public sealed class OptionalPresentationContentTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "prime-optional-content-test-" + Guid.NewGuid().ToString("N"));

    public OptionalPresentationContentTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void PreferenceCodecRoundTripsExactIdentityAndRejectsMalformedValue()
    {
        var expected = new ContentPackIdentity("Arena voice 2", "v1 beta", new string('a', 64));

        string encoded = OptionalContentPreferenceCodec.Encode(expected);

        Assert.DoesNotContain('=', encoded);
        Assert.True(OptionalContentPreferenceCodec.TryDecode(encoded, out ContentPackIdentity? decoded));
        Assert.Equal(expected, decoded);
        Assert.False(OptionalContentPreferenceCodec.TryDecode("../pack", out _));
        Assert.False(OptionalContentPreferenceCodec.TryDecode(
            new string('a', OptionalContentPreferenceCodec.MaximumEncodedLength + 1), out _));

        var maximumUtf8 = new ContentPackIdentity(
            new string('\u754c', ContentManifestLimits.MaximumStableIdLength),
            new string('\u7248', ContentManifestLimits.MaximumVersionLength), new string('b', 64));
        Assert.True(OptionalContentPreferenceCodec.TryDecode(
            OptionalContentPreferenceCodec.Encode(maximumUtf8), out ContentPackIdentity? maximumDecoded));
        Assert.Equal(maximumUtf8, maximumDecoded);
    }

    [Fact]
    public void ExactSavedSelectionsCreateIndependentAnnouncerAndMusicResolvers()
    {
        InstalledOptionalPresentationPack voice = Pack("voice-a", OptionalPresentationKind.Announcer,
            ("first.wav", new byte[] { 1, 2, 3 }));
        InstalledOptionalPresentationPack music = Pack("music-a", OptionalPresentationKind.Music,
            ("arena.ogg", new byte[] { 4, 5, 6 }));
        var catalog = new InstalledContentCatalog(optionalPacks: [voice, music]);

        ClientPresentationContentState selected = ClientPresentationContentState.Create(catalog,
            voice.Identity, music.Identity);

        Assert.False(selected.Announcer.UsedFallback);
        Assert.False(selected.Music.UsedFallback);
        Assert.Equal(voice.Identity, selected.AnnouncerAssets!.Identity);
        Assert.Equal(music.Identity, selected.MusicAssets!.Identity);

        ClientPresentationContentState missingVoice = ClientPresentationContentState.Create(catalog,
            new ContentPackIdentity("missing", "1", new string('0', 64)), music.Identity);
        Assert.True(missingVoice.Announcer.UsedFallback);
        Assert.Null(missingVoice.AnnouncerAssets);
        Assert.False(missingVoice.Music.UsedFallback);
    }

    [Fact]
    public async Task AssetResolverAllowsOnlyDeclaredUnchangedFiles()
    {
        InstalledOptionalPresentationPack voice = Pack("voice", OptionalPresentationKind.Announcer,
            ("events/first.wav", new byte[] { 1, 2, 3 }));
        var resolver = new OptionalPresentationAssetResolver(voice);

        FileStream? verified = await resolver.OpenVerifiedAsync("events/first.wav", CancellationToken.None);
        Assert.NotNull(verified);
        Assert.Equal(Path.Combine(voice.RootDirectory!, "events", "first.wav"), verified!.Name);
        verified.Dispose();
        Assert.Null(await resolver.OpenVerifiedAsync("../events/first.wav", CancellationToken.None));
        Assert.Null(await resolver.OpenVerifiedAsync("undeclared.wav", CancellationToken.None));

        string path = Path.Combine(voice.RootDirectory!, "events", "first.wav");
        File.WriteAllBytes(path, [9, 8, 7]);
        Assert.Null(await resolver.OpenVerifiedAsync("events/first.wav", CancellationToken.None));
    }

    [Fact]
    public async Task MusicPackUsesDeterministicManifestPlaylistAndFailsClosedWhenMissing()
    {
        InstalledOptionalPresentationPack music = Pack("music", OptionalPresentationKind.Music,
            ("one.ogg", new byte[] { 1 }), ("two.ogg", new byte[] { 2 }));
        var resolver = new OptionalPresentationAssetResolver(music);
        var pack = new OptionalMusicPack(music, resolver);

        FileStream? odd = await pack.OpenTrackAsync(roomId: 1, variant: 0, CancellationToken.None);
        Assert.NotNull(odd);
        Assert.EndsWith("two.ogg", odd!.Name, StringComparison.Ordinal);
        odd.Dispose();
        FileStream? even = await pack.OpenTrackAsync(roomId: 2, variant: 0, CancellationToken.None);
        Assert.NotNull(even);
        string evenPath = even!.Name;
        Assert.EndsWith("one.ogg", evenPath, StringComparison.Ordinal);
        even.Dispose();

        File.WriteAllBytes(evenPath, [9]);
        Assert.Null(await pack.OpenTrackAsync(roomId: 2, variant: 0, CancellationToken.None));
    }

    private InstalledOptionalPresentationPack Pack(string id, OptionalPresentationKind kind,
        params (string Path, byte[] Bytes)[] assets)
    {
        string directory = Path.Combine(_root, id);
        Directory.CreateDirectory(directory);
        var files = new ContentFileEntry[assets.Length];
        for (int i = 0; i < assets.Length; i++)
        {
            string fullPath = Path.Combine(directory,
                assets[i].Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllBytes(fullPath, assets[i].Bytes);
            files[i] = new ContentFileEntry(assets[i].Path, assets[i].Bytes.Length,
                Convert.ToHexStringLower(SHA256.HashData(assets[i].Bytes)));
        }
        string hash = Convert.ToHexStringLower(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(id)));
        OptionalPresentationEvent[] events = kind == OptionalPresentationKind.Announcer
            ? [new OptionalPresentationEvent("firstHunt", assets[0].Path)]
            : [];
        var manifest = new OptionalPresentationManifest(OptionalPresentationManifest.CurrentFormat,
            id, "1", hash, kind, files, events);
        return new InstalledOptionalPresentationPack(manifest, directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
