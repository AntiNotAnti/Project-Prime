namespace MphRead.Droid;

public partial class MainActivity
{
#if !MPHREAD_RMLUI_ANDROID
    private bool NativeRmlOwnsGraphics => false;
    private void BeginNativeLauncher() { }
    private void RetireNativeLauncherForMatch() { }
    private void RestoreNativeLauncher(bool keepSession) { }
    private bool NativeLauncherBack() => false;
    private void PauseNativeLauncher(bool paused) { }
    private void DestroyNativeLauncher() { }
#endif
}
