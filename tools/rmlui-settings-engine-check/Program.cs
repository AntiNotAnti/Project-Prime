using MphRead.Mods.Launcher.RmlUi.Settings;
if(args.Length>0&&args[0]=="--native")
    SettingsNativePageCheck.Run(Path.GetFullPath(args.Length>1?args[1]:Path.Combine(AppContext.BaseDirectory,"rmlui")));
else EngineSettingsBackendCheck.Run();
