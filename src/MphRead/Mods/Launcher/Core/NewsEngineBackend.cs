namespace MphRead.Mods.Launcher.Core;

public sealed class NewsEngineLinkLauncher : INewsLinkLauncher
{
    public bool Open(string uri) => uri == NewsController.DiscordUrl && Mods.Update.Updater.OpenLink(uri);
}
