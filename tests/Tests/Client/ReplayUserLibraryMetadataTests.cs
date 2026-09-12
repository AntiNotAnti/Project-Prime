using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class ReplayUserLibraryMetadataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"prime-replay-user-{Guid.NewGuid():N}");
    private readonly string _replay;
    private readonly ReplayUserLibraryMetadataStore _store;
    private const string Fingerprint =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public ReplayUserLibraryMetadataTests()
    {
        Directory.CreateDirectory(_root);
        _replay = Path.Combine(_root, "source.fpreplay");
        File.WriteAllBytes(_replay, [1, 2, 3, 4]);
        _store = new ReplayUserLibraryMetadataStore(Path.Combine(_root, "user"));
    }

    [Fact]
    public void ClipAndFavoritesPersistWithoutChangingReplayBytes()
    {
        byte[] original = File.ReadAllBytes(_replay);
        CombatActor focus = new(2, 71, 4);
        ReplayUserLibrarySelection saved = _store.SaveClip(_replay, Fingerprint,
            120, 360, focus, "Opening fight");
        ReplayUserClip clip = Assert.Single(saved.Clips);
        _store.SetReplayFavorite(_replay, Fingerprint, favorite: true);
        _store.SetClipFavorite(_replay, Fingerprint, clip.Id, favorite: true);
        var highlight = new ReplayHighlight(100, 180, 240, 50, focus,
            HighlightKind.Kill, 20, ReplayMarker.Kill, "KILL");
        _store.SetHighlightFavorite(_replay, Fingerprint, highlight, favorite: true);

        ReplayUserLibrarySelection loaded = _store.Load(_replay, Fingerprint);

        Assert.True(loaded.ReplayFavorite);
        Assert.True(Assert.Single(loaded.Clips).Favorite);
        Assert.Equal(focus, loaded.Clips[0].Focus);
        Assert.Equal(ReplayHighlightIdentity.From(highlight),
            Assert.Single(loaded.FavoriteHighlights));
        Assert.Equal(original, File.ReadAllBytes(_replay));
        Assert.True(File.Exists(_store.Path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_store.Path)!, "*.tmp"));
    }

    [Fact]
    public void ReplacedReplayAtSamePathDoesNotInheritAnnotations()
    {
        _store.SaveClip(_replay, Fingerprint, 10, 20, null, "Old bytes");
        File.WriteAllBytes(_replay, [9, 8, 7]);
        const string replacement =
            "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

        ReplayUserLibrarySelection loaded = _store.Load(_replay, replacement);

        Assert.True(loaded.SourceIsStale);
        Assert.Empty(loaded.Clips);
        Assert.False(loaded.ReplayFavorite);
    }

    [Fact]
    public void ClipValidationRequiresOrderedRangeExactFocusAndVisibleLabel()
    {
        Assert.Throws<ArgumentException>(() => _store.SaveClip(_replay, Fingerprint,
            20, 20, null, "bad"));
        Assert.Throws<ArgumentException>(() => _store.SaveClip(_replay, Fingerprint,
            10, 20, new CombatActor(2, 0, 0), "bad"));
        Assert.Throws<ArgumentException>(() => _store.SaveClip(_replay, Fingerprint,
            10, 20, null, "\n"));
    }

    [Fact]
    public void EventLabelsUseDeterministicPriorityForCombinedMarkers()
    {
        Assert.Equal("MATCH END", ReplayEventTimeline.Label(
            ReplayMarker.Kill | ReplayMarker.MatchEnd));
        Assert.Equal("HEADSHOT", ReplayEventTimeline.Label(
            ReplayMarker.Kill | ReplayMarker.Headshot));
        Assert.Equal("NODE CAPTURE", ReplayEventTimeline.Label(
            ReplayMarker.NodeCapture | ReplayMarker.Award));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
