using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class ReplayLibraryMetadataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"project-prime-library-metadata-{Guid.NewGuid():N}");

    public ReplayLibraryMetadataTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SidecarUsesAuthoritativeFactsAndRegeneratesWhenStaleOrMalformed()
    {
        string replay = Path.Combine(_root, "metadata.fpreplay");
        WriteReplay(replay, "MP1 SANCTORUS", GameMode.Battle, 600);
        var service = new ReplayLibraryMetadataService();

        ReplayLibraryMetadata first = service.GetOrCreate(replay);
        string sidecar = ReplayLibraryMetadataService.SidecarPath(replay);

        Assert.Equal("MP1 SANCTORUS", first.MapKey);
        Assert.Equal(GameMode.Battle, first.Mode);
        Assert.Equal(2, first.PlayerCount);
        Assert.Equal(TimeSpan.FromSeconds(10), first.Duration);
        Assert.Equal(ReplayCompatibilityStatus.Ready, first.CompatibilityStatus);
        Assert.True(File.Exists(sidecar));
        Assert.True(service.TryRead(replay, out ReplayLibraryMetadata? cached));
        Assert.Equal(first, cached);

        File.WriteAllText(sidecar, "{ malformed");
        ReplayLibraryMetadata regenerated = service.GetOrCreate(replay);
        Assert.Equal(first.ReplayId, regenerated.ReplayId);
        Assert.True(service.TryRead(replay, out _));

        using (var stream = new FileStream(replay, FileMode.Append, FileAccess.Write,
            FileShare.Read)) stream.WriteByte(0);
        File.SetLastWriteTimeUtc(replay, DateTime.UtcNow.AddSeconds(2));
        Assert.False(service.TryRead(replay, out _));
        ReplayLibraryMetadata changed = service.GetOrCreate(replay);
        Assert.NotEqual(first.ReplayId, changed.ReplayId);
        Assert.True(service.TryRead(replay, out _));
    }

    [Fact]
    public void InvalidReplayStillHasStableFingerprintAndReusableSidecar()
    {
        string replay = Path.Combine(_root, "damaged.fpreplay");
        File.WriteAllBytes(replay, [1, 2, 3]);
        var service = new ReplayLibraryMetadataService();

        ReplayLibraryMetadata first = service.GetOrCreate(replay);
        ReplayLibraryMetadata second = service.GetOrCreate(replay);

        Assert.Equal(64, first.ReplayId.Length);
        Assert.Equal(first, second);
        Assert.Equal(ReplayRecoveryStatus.Damaged, first.RecoveryStatus);
        Assert.Equal(ReplayCompatibilityStatus.Damaged, first.CompatibilityStatus);
        Assert.True(service.TryRead(replay, out _));
    }

    [Fact]
    public async Task CoordinatorCachesBySourceVersionAndInvalidatesChangedFile()
    {
        string replay = Path.Combine(_root, "coordinator.fpreplay");
        WriteReplay(replay, "MP2 ALINOS GATE", GameMode.Nodes, 120);
        using var coordinator = new ReplayAnalysisCoordinator(
            new ReplayHighlightMetadataService(_root));

        ReplayAnalysisResult first = await coordinator.SelectAsync(replay);
        ReplayAnalysisResult cached = await coordinator.SelectAsync(replay);
        Assert.Same(first, cached);
        Assert.True(coordinator.TryGetCached(replay, out ReplayAnalysisResult? found));
        Assert.Same(first, found);

        File.Delete(replay);
        WriteReplay(replay, "MP3 PROVING GROUND", GameMode.Capture, 240);
        File.SetLastWriteTimeUtc(replay, DateTime.UtcNow.AddSeconds(2));
        ReplayAnalysisResult changed = await coordinator.SelectAsync(replay);

        Assert.NotEqual(first.Library.ReplayId, changed.Library.ReplayId);
        Assert.Equal("MP3 PROVING GROUND", changed.Library.MapKey);
        Assert.Equal(TimeSpan.FromSeconds(4), changed.Library.Duration);
    }

    [Fact]
    public void TheatreFiltersSearchFilterAndSortCachedMetadataDeterministically()
    {
        DateTime now = new(2026, 9, 12, 12, 0, 0);
        PrimeReplayEntry first = Entry("one", "MP1 SANCTORUS", now, 10,
            GameMode.Battle, highlights: 0, ReplayRecoveryStatus.Complete,
            TimeSpan.FromMinutes(1));
        PrimeReplayEntry second = Entry("two", "MP3 PROVING GROUND", now.AddDays(-1),
            20, GameMode.Capture, highlights: 3, ReplayRecoveryStatus.Recovered,
            TimeSpan.FromMinutes(4));

        PrimeReplayEntry result = Assert.Single(TheatreController.FilterAndSort(
            [first, second], new TheatreFilters(Search: "proving",
                Mode: GameMode.Capture, HasHighlights: true,
                Recovery: ReplayRecoveryStatus.Recovered,
                Sort: TheatreSortOrder.Duration)));
        Assert.Equal(second.Id, result.Id);
        Assert.Equal([second.Id, first.Id], TheatreController.FilterAndSort(
            [first, second], new TheatreFilters(Sort: TheatreSortOrder.FileSize))
            .Select(value => value.Id));
    }

    private PrimeReplayEntry Entry(string id, string map, DateTime recorded,
        long bytes, GameMode mode, int highlights, ReplayRecoveryStatus recovery,
        TimeSpan duration)
    {
        string path = Path.Combine(_root, id + ReplayFile.Extension);
        var metadata = new ReplayLibraryMetadata(new string(id[0], 64), path,
            Path.GetFileName(path), map, map, mode, recorded, duration, bytes,
            ReplayFile.IndexedFormatVersion, NetHeader.Version, 4, highlights,
            recovery, recovery == ReplayRecoveryStatus.Complete
                ? ReplayCompatibilityStatus.Ready
                : ReplayCompatibilityStatus.PartialCompatibility);
        return new PrimeReplayEntry(id, path, Path.GetFileName(path), map,
            recorded, bytes, metadata);
    }

    private static void WriteReplay(string path, string room, GameMode mode,
        uint endFrame)
    {
        using var writer = new ReplayWriter(path, NetHeader.Version);
        byte[] match = new byte[MatchTransitionPacket.Size];
        new MatchTransitionPacket(1, 1000, mode, room).Write(match);
        writer.WriteRecord(0, Record(ReplayRecordKind.Match, match));

        NetRosterEntry[] entries =
        [
            new(0, 101, Hunter.Samus, 0, "SAMUS"),
            new(1, 202, Hunter.Sylux, 1, "SYLUX")
        ];
        byte[] body = new byte[4 + SessionRosterPacket.MaxSize];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(body, 1);
        int count = SessionRosterPacket.Write(body.AsSpan(4), 1, entries);
        writer.WriteRecord(1, Record(ReplayRecordKind.Roster,
            body.AsSpan(0, count + 4)));
        writer.WriteRecord(endFrame, Record(ReplayRecordKind.Match, match));
    }

    private static byte[] Record(ReplayRecordKind kind, ReadOnlySpan<byte> body)
    {
        byte[] result = new byte[body.Length + 1];
        result[0] = (byte)kind;
        body.CopyTo(result.AsSpan(1));
        return result;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
