using Microsoft.AspNetCore.Identity;
using MphRead.Backend.Matches;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MphRead.Backend.Data;

public sealed class HunterAccount : IdentityUser<Guid> { }

public sealed class PlayerProfile
{
    public Guid PlayerId { get; set; }
    public string DisplayName { get; set; } = "";
    public Hunter FavoriteHunter { get; set; }
}

public sealed class HunterLicense
{
    public Guid PlayerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int RatingPoints { get; set; }
}

public sealed class CareerProjectionState
{
    public int Id { get; set; } = 1;
    public bool RebuildRequired { get; set; } = true;
}

public sealed class BackendDbContext(DbContextOptions<BackendDbContext> options)
    : IdentityDbContext<HunterAccount, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AcceptedMatch> Matches => Set<AcceptedMatch>();
    public DbSet<CareerParticipation> Participations => Set<CareerParticipation>();
    public DbSet<CareerAggregate> Aggregates => Set<CareerAggregate>();
    public DbSet<PlayerProfile> Profiles => Set<PlayerProfile>();
    public DbSet<HunterLicense> Licenses => Set<HunterLicense>();
    public DbSet<RatingLedgerEntry> RatingTransactions => Set<RatingLedgerEntry>();
    public DbSet<RatingPairLedgerEntry> RatingPairContributions => Set<RatingPairLedgerEntry>();
    public DbSet<CareerProjectionState> ProjectionStates => Set<CareerProjectionState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        MatchDataModel.Configure(builder);
        builder.Entity<HunterAccount>().ToTable("players");
        // Identity's normalized username is already unique; require email uniqueness too.
        builder.Entity<HunterAccount>().HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.Entity<PlayerProfile>(entity =>
        {
            entity.ToTable("player_profiles");
            entity.HasKey(x => x.PlayerId);
            entity.Property(x => x.DisplayName).HasMaxLength(16).IsRequired();
            entity.Property(x => x.FavoriteHunter).HasConversion<byte>();
            entity.HasOne<HunterAccount>().WithOne().HasForeignKey<PlayerProfile>(x => x.PlayerId);
        });
        builder.Entity<HunterLicense>(entity =>
        {
            entity.ToTable("hunter_licenses");
            entity.HasKey(x => x.PlayerId);
            entity.HasOne<HunterAccount>().WithOne().HasForeignKey<HunterLicense>(x => x.PlayerId);
        });
        builder.Entity<CareerProjectionState>(entity =>
        {
            entity.ToTable("career_projection_state");
            entity.HasKey(x => x.Id);
            entity.HasData(new CareerProjectionState());
        });
    }
}
