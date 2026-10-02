namespace MphRead.Mods.Input
{
    public static class ControllerBaselineState
    {
        private const string FileName = "controller-baseline.state";

        public static bool Enabled { get; private set; }

        private static string Path => System.IO.Path.Combine(
            Launcher.LauncherPrefs.Directory, FileName);

        public static void Load()
        {
            Enabled = false;
            try
            {
                if (System.IO.File.Exists(Path))
                {
                    Enabled = System.IO.File.ReadAllText(Path).Trim() == "1";
                }
            }
            catch
            {
                Enabled = false;
            }
            Apply();
        }

        public static bool Toggle()
        {
            Enabled = !Enabled;
            Apply();
            try
            {
                System.IO.Directory.CreateDirectory(Launcher.LauncherPrefs.Directory);
                System.IO.File.WriteAllText(Path, Enabled ? "1" : "0");
            }
            catch
            {
                // The runtime toggle still works for this session if persistence is unavailable.
            }
            return Enabled;
        }

        private static void Apply()
        {
            AimAssist.AimAssistDebug.UnassistedArm = Enabled;
        }
    }
}
