using Microsoft.EntityFrameworkCore;

namespace MphRead.Backend.Matches;

// The original bytes are authoritative. All other tables in this file are rebuildable projections.
public sealed class AcceptedMatch
{
    public long ProcessingOrder { get; set; }
    public Guid MatchId { get; set; }
    public Guid ServerId { get; set; }
    public Guid ServerIncarnation { get; set; }
    public string PayloadHash { get; set; } = "";
    public byte[] OriginalReport { get; set; } = [];
    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
    public string RoomKey { get; set; } = "";
    public int Mode { get; set; }
    public int TrustClass { get; set; }
    public bool CareerEligible { get; set; }
    public string RatingStatus { get; set; } = "policyPending";
}

public sealed class CareerParticipation
{
    public Guid MatchId { get; set; }
    public Guid PlayerId { get; set; }
    public long ProcessingOrder { get; set; }
    public bool Eligible { get; set; }
    public bool Won { get; set; }
    public bool Tied { get; set; }
    public int Outcome { get; set; }
    public long PlayedTicks { get; set; }
    public long Kills { get; set; }
    public long Deaths { get; set; }
    public long Assists { get; set; }
    public long Damage { get; set; }
}

/// <summary>Dimension is career, hunter, map, mode or weapon. Hunter rows contain only
/// attributable span time; aggregate combat facts cannot be assigned to individual hunters.</summary>
public sealed class CareerAggregate
{
    public Guid PlayerId { get; set; }
    public int TrustClass { get; set; }
    public string Dimension { get; set; } = "";
    public string Key { get; set; } = "";
    public long Matches { get; set; }
    public long Wins { get; set; }
    public long Ties { get; set; }
    public long PlayedTicks { get; set; }
    public long Kills { get; set; }
    public long Deaths { get; set; }
    public long Assists { get; set; }
    public long Damage { get; set; }
    public long OctolithScores { get; set; }
    public long NodesCaptured { get; set; }
    public long KillsAsPrime { get; set; }
    public long HeadshotKills { get; set; }
    public long? BipedKills { get; set; } = 0;
    public long? AltFormKills { get; set; } = 0;
    public long LongestKillStreak { get; set; }
    public long CurrentWinStreak { get; set; }
    public long LongestWinStreak { get; set; }
    public long OutcomeSamples { get; set; }
}

public static class MatchDataModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<AcceptedMatch>(e =>
        {
            e.ToTable("accepted_matches"); e.HasKey(x => x.MatchId);
            e.Property(x => x.ProcessingOrder).ValueGeneratedOnAdd();
            e.HasIndex(x => x.ProcessingOrder).IsUnique();
            e.Property(x => x.PayloadHash).HasMaxLength(64);
            e.Property(x => x.RoomKey).HasMaxLength(128);
            e.Property(x => x.RatingStatus).HasMaxLength(32);
        });
        model.Entity<CareerParticipation>(e =>
        {
            e.ToTable("career_participations"); e.HasKey(x => new { x.MatchId, x.PlayerId });
            e.HasIndex(x => new { x.PlayerId, x.ProcessingOrder });
            e.HasOne<AcceptedMatch>().WithMany().HasForeignKey(x => x.MatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Data.HunterLicense>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<CareerAggregate>(e =>
        {
            e.ToTable("career_aggregates"); e.HasKey(x => new { x.PlayerId, x.TrustClass, x.Dimension, x.Key });
            e.HasIndex(x => new { x.TrustClass, x.Dimension, x.Kills, x.PlayerId });
            e.HasIndex(x => new { x.TrustClass, x.Dimension, x.Wins, x.PlayerId });
            e.Property(x => x.Dimension).HasMaxLength(16); e.Property(x => x.Key).HasMaxLength(128);
            e.HasOne<Data.HunterLicense>().WithMany().HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
