using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Core
{
    public sealed record NewsDispatch(string Category, string Title, string Summary, string Detail);
    public interface INewsProvider { IReadOnlyList<NewsDispatch> Read(); }

    public sealed class BundledNewsProvider : INewsProvider
    {
        public IReadOnlyList<NewsDispatch> Read() => new NewsDispatch[]
        {
            new("NEWS", "PROJECT PRIME COMMUNITY UPDATE",
                "A new home for your hunts. Catch up on what's changing in Project Prime.",
                "Project Prime brings Metroid Prime Hunters to modern PCs and Android. This news page collects project updates, patch notes and announcements. Join the Discord to follow the community, and check GitHub Releases for published builds."),
            new("PATCH NOTES", "LOBBY & SETTINGS REFINEMENTS",
                "Clearer controls, more room for settings, and team selection in the roster.",
                "Lobby rule controls have larger OFF/ON buttons. Settings uses a single category strip. Hunter previews stay behind dialogs, and team-mode rosters offer team arrows that respect team locks and available slots. These changes are included in this build."),
            new("ANNOUNCEMENTS", "JOIN THE PROJECT PRIME DISCORD",
                "Stay connected with the Project Prime community.",
                "Join the Discord using the button at the top of Home. Follow project announcements, keep up with patch notes, and connect with other hunters."),
            new("NEWS", "PERSISTENT MULTIPLAYER LOBBIES",
                "Stay connected between matches.",
                "Project Prime lobbies support up to eight players, owner controls, team layouts, optional ready checks and rematches. The active lobby indicator in the header brings you back to your session.")
        };
    }

}
