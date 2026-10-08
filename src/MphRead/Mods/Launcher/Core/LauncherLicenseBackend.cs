using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Launcher.Core;

internal sealed class LauncherLicenseBackend : ILicenseBackend
{
    public LicenseData LocalSnapshot()
    {
#if !MPHREAD_SERVER
        return Copy(HunterLicenseClient.LocalSnapshot());
#else
        return new() { Profile = new(DisplayName: LauncherPrefs.PlayerName, FavoriteHunter: (int)Hunters.Resolve(LauncherPrefs.LastHunter)) };
#endif
    }
    public async Task<LicenseData> LoadAsync(CancellationToken cancellationToken)
    {
#if !MPHREAD_SERVER
        return Copy(await HunterLicenseClient.LoadAsync(cancellationToken).ConfigureAwait(false));
#else
        await Task.CompletedTask;
        return LocalSnapshot() with { Status = "Account synchronization is unavailable in this build." };
#endif
    }
    public async Task<LicenseActionResult> AccountAsync(LicenseAccountAction action, string email, string password, CancellationToken cancellationToken)
    {
#if !MPHREAD_SERVER
        HunterLicenseActionResult result = action switch
        {
            LicenseAccountAction.SendVerification => await HunterLicenseClient.BeginEmailLinkAsync(email, cancellationToken).ConfigureAwait(false),
            LicenseAccountAction.FinishPassword => await HunterLicenseClient.SetPasswordAsync(password, cancellationToken).ConfigureAwait(false),
            LicenseAccountAction.Recover => await HunterLicenseClient.RecoverWithPasswordAsync(email, password, cancellationToken).ConfigureAwait(false),
            LicenseAccountAction.LinkGoogle => await HunterLicenseClient.StartOAuthLinkAsync("google", cancellationToken).ConfigureAwait(false),
            LicenseAccountAction.LinkGitHub => await HunterLicenseClient.StartOAuthLinkAsync("github", cancellationToken).ConfigureAwait(false),
            LicenseAccountAction.LinkDiscord => await HunterLicenseClient.StartOAuthLinkAsync("discord", cancellationToken).ConfigureAwait(false),
            _ => HunterLicenseActionResult.Fail("Unknown account action.")
        };
        return new(result.Success, result.Message);
#else
        await Task.CompletedTask;
        return new(false, "Account synchronization is unavailable in this build.");
#endif
    }
#if !MPHREAD_SERVER
    private static LicenseData Copy(HunterLicenseSnapshot data)
    {
        HunterLicenseStats s = data.Stats;
        HunterLicenseProfile p = data.Profile;
        return new()
        {
            Connected = data.Connected, Status = data.Status,
            Account = new(data.Account.IsAnonymous, data.Account.Email, data.Account.Providers.ToImmutableArray()),
            Profile = new(p.PlayerId, p.DisplayName, p.FavoriteHunter, p.CreatedAt, p.RatingPoints, p.RatingTier),
            Stats = new() { GamesPlayed = s.GamesPlayed, Wins = s.Wins, Ties = s.Ties, Losses = s.Losses,
                Kills = s.Kills, Deaths = s.Deaths, Assists = s.Assists, Damage = s.Damage, PlayedTicks = s.PlayedTicks,
                Headshots = s.Headshots, LongestKillStreak = s.LongestKillStreak, CurrentWinStreak = s.CurrentWinStreak,
                LongestWinStreak = s.LongestWinStreak, OctolithScores = s.OctolithScores, NodesCaptured = s.NodesCaptured, KillsAsPrime = s.KillsAsPrime },
            Matches = data.Matches.Take(25).Select(m => new LicenseMatch(m.MatchId, m.PlayedAt, m.RoomKey, m.Mode, m.TrustClass,
                m.CareerEligible, m.RatingStatus, m.Eligible, m.Won, m.Tied, m.PlayedTicks, m.Kills, m.Deaths, m.Assists, m.Damage)).ToImmutableArray(),
            Cosmetics = data.Cosmetics.Where(c => c.Hunter is >= 0 and < 7).Select(c => new LicenseCosmetic((Hunter)c.Hunter,
                c.SkinKey, c.ArmorEffectKey, c.DeathEffectKey)).ToImmutableArray()
        };
    }
#endif
}
