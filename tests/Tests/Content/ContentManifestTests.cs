using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Runtime.Content;
using Xunit;

namespace MphRead.Tests;

public sealed class ContentManifestTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fruity-content-manifest-test-" + Guid.NewGuid().ToString("N"));

    public ContentManifestTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void RequiredMatchIsExactAndMismatchFailsClosed()
    {
        GameplayContentManifest manifest = Gameplay("gameplay-a", "1.0", [1, 2, 3]);
        InstalledContentCatalog catalog = new([
            new InstalledGameplayPack(manifest, _root)
        ]);

        Assert.True(catalog.TryMatchGameplay(manifest.PackIdentity, out InstalledGameplayPack? matched));
        Assert.Equal(manifest.PackIdentity, matched!.Identity);
        Assert.Equal(manifest.Files, matched.Manifest.Files);
        Assert.Same(matched, catalog.RequireGameplay(manifest.PackIdentity));
        Assert.Throws<RequiredContentMismatchException>(() => catalog.RequireGameplay(
            new ContentPackIdentity(manifest.StableId, manifest.Version, new string('a', 64))));
    }

    [Fact]
    public void DifferentOptionalPacksDoNotAffectRequiredGameplaySelection()
    {
        GameplayContentManifest gameplay = Gameplay("gameplay-a", "1.0", [1, 2, 3]);
        OptionalPresentationManifest first = Optional("voice-a", OptionalPresentationKind.Announcer, "a.wav", [4]);
        OptionalPresentationManifest second = Optional("voice-b", OptionalPresentationKind.Announcer, "b.wav", [5]);
        var catalog = new InstalledContentCatalog(
            [new InstalledGameplayPack(gameplay, _root)],
            [new InstalledOptionalPresentationPack(first, _root), new InstalledOptionalPresentationPack(second, _root)]);

        Assert.NotNull(catalog.RequireGameplay(gameplay.PackIdentity));
        OptionalPresentationSelection selected = catalog.SelectOptional(OptionalPresentationKind.Announcer, first.PackIdentity);
        Assert.False(selected.UsedFallback);
        Assert.Equal(first.PackIdentity, selected.Pack!.Identity);
        Assert.False(catalog.SelectOptional(OptionalPresentationKind.Announcer, second.PackIdentity).UsedFallback);
    }

    [Fact]
    public void MissingOrInvalidOptionalPackUsesBuiltInFallback()
    {
        OptionalPresentationManifest invalid = Optional("voice-invalid", OptionalPresentationKind.Announcer, "voice.wav", [4]);
        string directory = Path.Combine(_root, "invalid");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ContentManifestFileNames.OptionalPresentation), "{}");

        InstalledContentCatalog catalog = InstalledContentCatalog.Discover(_root);
        OptionalPresentationSelection missing = catalog.SelectOptional(OptionalPresentationKind.Announcer, invalid.PackIdentity);

        Assert.Contains(catalog.Issues, issue => !issue.IsGameplay);
        Assert.True(missing.UsedFallback);
        Assert.Null(missing.Pack);
        Assert.Equal(OptionalPresentationFallbackReason.Missing, missing.FallbackReason);
    }

    [Fact]
    public void StrictJsonRejectsUnknownAndDuplicateProperties()
    {
        string valid = Encoding.UTF8.GetString(ContentManifestJson.Serialize(Gameplay("g", "1", [1])));
        string unknown = valid.TrimEnd('}') + ",\"unexpected\":true}";
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestJson.ParseGameplay(Encoding.UTF8.GetBytes(unknown)));

        string duplicate = valid.Replace("\"format\": 1", "\"format\": 1,\"format\": 1", StringComparison.Ordinal);
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestJson.ParseGameplay(Encoding.UTF8.GetBytes(duplicate)));
    }

    [Fact]
    public void StrictJsonRejectsTrailingDataCommentsAndExcessDepth()
    {
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestJson.ParseGameplay(
            Encoding.UTF8.GetBytes("{\"format\":1}//comment")));
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestJson.ParseGameplay(
            Encoding.UTF8.GetBytes("{\"format\":1} {\"format\":1}")));

        string nested = "{" + String.Concat(Enumerable.Repeat("\"x\":[", ContentManifestLimits.MaximumJsonDepth + 1))
            + "0" + String.Concat(Enumerable.Repeat("]", ContentManifestLimits.MaximumJsonDepth + 1)) + "}";
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestJson.ParseGameplay(Encoding.UTF8.GetBytes(nested)));
    }

    [Fact]
    public void ManifestSizeAndPathLengthBoundsAreRejectedBeforeDiscovery()
    {
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestJson.ParseGameplay(
            new byte[ContentManifestLimits.MaximumManifestBytes + 1]));

        string longPath = new string('a', ContentManifestLimits.MaximumPathLength + 1) + ".bin";
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateGameplay(
            Gameplay("g", "1", [1]) with
            {
                Files = [new ContentFileEntry(longPath, 1, Sha([1]))]
            }));
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("/absolute.bin")]
    [InlineData("C:/outside.bin")]
    [InlineData("folder//file.bin")]
    [InlineData("folder/../file.bin")]
    [InlineData("folder\\file.bin")]
    public void UnsafePathsAreRejected(string path)
    {
        GameplayContentManifest manifest = Gameplay("g", "1", [1]) with
        {
            Files = [new ContentFileEntry(path, 1, Sha([1]))]
        };
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateGameplay(manifest));
    }

    [Fact]
    public void DuplicateFilesAndOversizedDeclarationsAreRejected()
    {
        ContentFileEntry file = new("asset.bin", 1, Sha([1]));
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateGameplay(
            Gameplay("g", "1", [1]) with { Files = [file, file] }));
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateGameplay(
            Gameplay("g", "1", [1]) with
            {
                Files = [file with { Bytes = ContentManifestLimits.MaximumIndividualFileBytes + 1 }]
            }));
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateGameplay(
            Gameplay("g", "1", [1]) with
            {
                Files = Enumerable.Range(0, ContentManifestLimits.MaximumFiles + 1)
                    .Select(index => new ContentFileEntry($"{index}.bin", 0, Sha(Array.Empty<byte>()))).ToArray()
            }));
    }

    [Fact]
    public void UnknownAndDuplicateAnnouncerEventsAreRejected()
    {
        OptionalPresentationManifest manifest = Optional("voice", OptionalPresentationKind.Announcer, "voice.wav", [1]);
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateOptional(
            manifest with { Events = [new OptionalPresentationEvent("unknown", "voice.wav")] }));
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateOptional(
            manifest with
            {
                Events = [new OptionalPresentationEvent("go", "voice.wav"), new OptionalPresentationEvent("GO", "voice.wav")]
            }));
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateOptional(
            manifest with { Events = [new OptionalPresentationEvent("go", "missing.wav")] }));
    }

    [Fact]
    public void NonAnnouncerKindsCannotDeclareEventsOrUseExecutableExtensions()
    {
        OptionalPresentationManifest music = Optional("music", OptionalPresentationKind.Music, "music.ogg", [1]);
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateOptional(
            music with { Events = [new OptionalPresentationEvent("go", "music.ogg")] }));

        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateOptional(
            Optional("theme", OptionalPresentationKind.HudTheme, "theme.dll", [1])));
        Assert.Throws<ContentManifestValidationException>(() => ContentManifestValidator.ValidateOptional(
            Optional("effects", OptionalPresentationKind.CosmeticEffects, "effect.lua", [1])));
    }

    [Fact]
    public void InstalledDiscoveryChecksDeclaredHashAndUnlistedFiles()
    {
        byte[] bytes = [7, 8, 9];
        GameplayContentManifest manifest = Gameplay("g", "1", bytes);
        string directory = Path.Combine(_root, "gameplay");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "map.bin"), bytes);
        File.WriteAllBytes(Path.Combine(directory, ContentManifestFileNames.Gameplay), ContentManifestJson.Serialize(manifest));
        File.WriteAllBytes(Path.Combine(directory, "unlisted.bin"), [3]);

        InstalledContentCatalog catalog = InstalledContentCatalog.Discover(_root);
        Assert.Empty(catalog.GameplayPacks);
        Assert.Contains(catalog.Issues, issue => issue.IsGameplay);

        File.Delete(Path.Combine(directory, "unlisted.bin"));
        File.WriteAllBytes(Path.Combine(directory, "map.bin"), [0]);
        catalog = InstalledContentCatalog.Discover(_root);
        Assert.Empty(catalog.GameplayPacks);
        Assert.Contains(catalog.Issues, issue => issue.IsGameplay);
    }

    [Fact]
    public void InstalledDiscoveryRejectsSymlinkedFiles()
    {
        byte[] bytes = [1, 2];
        GameplayContentManifest manifest = Gameplay("g", "1", bytes);
        string directory = Path.Combine(_root, "symlink");
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "outside.bin");
        string link = Path.Combine(directory, "map.bin");
        File.WriteAllBytes(target, bytes);
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        File.WriteAllBytes(Path.Combine(directory, ContentManifestFileNames.Gameplay), ContentManifestJson.Serialize(manifest));

        InstalledContentCatalog catalog = InstalledContentCatalog.Discover(_root);
        Assert.Empty(catalog.GameplayPacks);
        Assert.Contains(catalog.Issues, issue => issue.IsGameplay);
    }

    [Fact]
    public void OptionalHashesAndFileSizesAreValidatedDuringDiscovery()
    {
        byte[] bytes = [1, 2, 3];
        OptionalPresentationManifest manifest = Optional("voice", OptionalPresentationKind.Announcer, "voice.wav", bytes);
        string directory = Path.Combine(_root, "optional");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "voice.wav"), [9]);
        File.WriteAllBytes(Path.Combine(directory, ContentManifestFileNames.OptionalPresentation), ContentManifestJson.Serialize(manifest));

        InstalledContentCatalog catalog = InstalledContentCatalog.Discover(_root);
        Assert.Empty(catalog.OptionalPresentationPacks);
        Assert.Contains(catalog.Issues, issue => !issue.IsGameplay);
    }

    [Fact]
    public void DuplicateInstalledPackIdentitiesAreRejected()
    {
        GameplayContentManifest manifest = Gameplay("g", "1", [1]);
        Assert.Throws<ContentManifestValidationException>(() => new InstalledContentCatalog(
            [new InstalledGameplayPack(manifest, _root), new InstalledGameplayPack(manifest, _root)]));
    }

    private static GameplayContentManifest Gameplay(string id, string version, byte[] bytes)
        => new(GameplayContentManifest.CurrentFormat, id, version, Sha(bytes),
            new GameplayContentIdentity("map-v1", "collision-v1", "entities-v1", "gameplay-v1"),
            [new ContentFileEntry("map.bin", bytes.Length, Sha(bytes))]);

    private static OptionalPresentationManifest Optional(string id, OptionalPresentationKind kind,
        string path, byte[] bytes)
        => new(OptionalPresentationManifest.CurrentFormat, id, "1", Sha(bytes), kind,
            [new ContentFileEntry(path, bytes.Length, Sha(bytes))],
            kind == OptionalPresentationKind.Announcer
                ? [new OptionalPresentationEvent("go", path)]
                : []);

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
