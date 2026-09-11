using MphRead.Mods;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Compatibility presenter for the pre-overlay desktop window. It is kept as
/// the default registration for classic UI and direct desktop scene launches;
/// the Prime desktop shell replaces it with its persistent overlay instance.
/// </summary>
internal sealed class LegacyPauseMenuPresenter : IPauseMenuPresenter
{
    internal static LegacyPauseMenuPresenter Instance { get; } = new();

    private LegacyPauseMenuPresenter() { }

    public bool IsOpen => PauseMenuWindow.IsOpen;

    public bool Open(Scene scene) => PauseMenuWindow.Open(scene);

    public void Close() => PauseMenuWindow.CloseIfOpen();

    public void Pump() => GuiLauncher.Pump();

    public void OnWindowMoved() => PauseMenuWindow.FollowGameWindow();
}
