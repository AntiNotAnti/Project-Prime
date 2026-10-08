using System;
using System.Collections.Generic;
using System.IO;
using Android.App;
using Android.Runtime;
using MphRead.Mods;
using MphRead.Mods.Launcher;

namespace MphRead.Droid
{
#if DEBUG
    [Application(UsesCleartextTraffic = true)]
#else
    [Application]
#endif
    public class MainApplication : Application
    {
        public MainApplication(nint javaReference, JniHandleOwnership transfer)
            : base(javaReference, transfer)
        {
        }

        public override void OnCreate()
        {
            base.OnCreate();
            AndroidConsole.Install();
            string root = ChooseRoot();
            if (root.Length > 0)
            {
                LauncherPrefs.Directory = root;
                GameFiles.Root = root;
                try
                {
                    Directory.SetCurrentDirectory(root);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[android] could not use {root} as the working directory: {ex.Message}");
                }
            }
            AndroidMaps.Install(Assets, root);
            ThumbnailHost.Current = new AndroidThumbnailHost();
            MphRead.Mods.Update.UpdateInstall.Current = new AndroidUpdateInstaller();
            MphRead.Mods.LogShare.Current = new AndroidLogShare(this);
            MphRead.Mods.Platform.WebLink.Current = new AndroidWebLink(this);
            MphRead.Mods.Render.HunterShot.Current = new AndroidHunterShot();
            ScreenCapture.PngWriter = AndroidPng.Write;
            CrashReport.Install();
            AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => CrashReport.Report(e.Exception, "android");
            LauncherPrefs.Load();
            Mods.Input.ControllerBaselineState.Load();
            Mods.InputSettings.Load();
            Mods.DebugLog.Attach();
            MenuSettings settings = GameState.LoadSettings();
            AndroidPerformance.ApplyStartupDefaults(settings);
            GameSettings.Apply(settings);
            if (GameFiles.Ready) GameFiles.ApplyPaths();
            // Preview services also create this Application in their worker
            // processes. MainActivity.OnResume starts the social lifetimes
            // only for the interactive client.
        }

        private string ChooseRoot()
        {
            var candidates = new List<string>();
            string? external = GetExternalFilesDir(null)?.AbsolutePath;
            if (!String.IsNullOrEmpty(external))
            {
                candidates.Add(external);
            }
            string? internalFiles = FilesDir?.AbsolutePath;
            if (!String.IsNullOrEmpty(internalFiles))
            {
                candidates.Add(internalFiles);
            }
            foreach (string candidate in candidates)
            {
                if (Writable(candidate) && File.Exists(Path.Combine(candidate, "paths.txt")))
                {
                    return candidate;
                }
            }
            foreach (string candidate in candidates)
            {
                if (Writable(candidate))
                {
                    if (candidate != candidates[0])
                    {
                        Console.WriteLine($"[android] {candidates[0]} cannot be written to; using {candidate}");
                    }
                    return candidate;
                }
            }
            return "";
        }

        private static bool Writable(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-probe");
                File.WriteAllBytes(probe, Array.Empty<byte>());
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
