using MphRead.Mods.Launcher.RmlUi.Host;

// Real managed binding cache -> actual native Home model. The caller already
// installed its native-library resolver; this fixture owns no resolver/module.
// Only invented RML plus three public runtime fonts enter its private assets.
internal static class HomeModelSnapshotCheck
{
    internal static void Run(string runtimeRoot)
    {
        int checks=0;
        void Check(bool value,string label)
        {checks++;if(!value)throw new InvalidOperationException("Managed/native Home snapshot: "+label);}
        string ownRoot=Path.Combine(Path.GetTempPath(),"prime-home-snapshot-own-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(ownRoot,"fonts"));
        try
        {
            foreach(string font in new[]{"Rajdhani-SemiBold.ttf","Rajdhani-Bold.ttf","JetBrainsMono-Regular.ttf"})
                File.Copy(Path.Combine(Path.GetFullPath(runtimeRoot),"fonts",font),Path.Combine(ownRoot,"fonts",font));
            File.WriteAllText(Path.Combine(ownRoot,"prime_home.rml"),"""
                <rml><head><title>Invented managed Home model snapshot</title><style>
                body {font-family:Rajdhani;font-weight:600;font-size:24px;margin:0px;}
                button {display:block;width:480px;height:48px;margin:8px;tab-index:auto;}
                </style></head><body data-model="prime_menu">
                <button id="movement" data-event-click="lobby_rules_toggle17">OWN MOVEMENT {{rules_toggle17}}</button>
                <button id="rules_map_next">OWN MAP NEXT</button>
                </body></rml>
                """);
            using var host=new RmlUiHost();
            Check(host.Initialize(640,360,1,ownRoot,RmlUiRenderBackend.DrawList),"real CPU backend initialized without Scene/window/GL");
            var home=host.HomeDocument;
            host.SetText(home,"rules_toggle17","OWN_MODE");
            // This deliberately caches rules_open=true while outside lobby.
            // Entering lobby then closes it inside native SetBool; the managed
            // cache must invalidate that dependent value before the next write.
            host.SetBool(home,"lobby_mode",false);
            host.SetBool(home,"lobby_rules_open",true);
            host.SetBool(home,"rules_owner",true);
            host.Update();
            host.SetBool(home,"lobby_mode",true);
            host.SetBool(home,"lobby_rules_open",true);
            host.Update();
            Check(host.FocusDocument(home,"movement")&&host.FocusedElement()=="movement",
                "real invented Home Movement control accepts keyboard focus");
            Check(!host.TryTakeIntent(out _),"binding snapshots and focus alone produce no actions");
            host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
            Check(host.TryTakeIntent(out var intent),
                "reapplied rules_open=true must reach native after lobby_mode cross-mutation; managed cache cannot suppress it");
            Check(intent.Kind==RmlUiIntentKind.LobbyRulesToggle&&intent.Argument==17&&intent.Document==home&&intent.Sequence>0,
                "actual Home model callback emits literal Movement17 with current Home ownership");
            Check(!host.TryTakeIntent(out _),"one Enter dispatch emits exactly one current callback");

            Check(host.FocusDocument(home,"movement"),"retain real Movement focus before unchanged snapshot");
            host.SetBool(home,"multiplayer_mode",false);
            host.SetBool(home,"lobby_rules_open",true);
            host.SetBool(home,"rules_owner",true);
            host.Update();
            Check(host.FocusedElement()=="movement","unchanged rules snapshot cannot replay opening focus transition");
            Check(!host.TryTakeIntent(out _),"unchanged snapshot cannot manufacture an input callback");
            host.SetBool(home,"lobby_rules_open",false);host.Update();
            Check(host.FocusDocument(home,"movement"),"real Movement focus retained before genuine rules reopen");
            host.SetBool(home,"lobby_rules_open",true);host.Update();
            Check(host.FocusedElement()=="rules_map_next","genuine rules false-to-true transition retains authored map focus target");
            host.DeviceLost();
            Check(!host.Active&&!host.IsAlive(home),"private actual Home runtime retires through existing native lifetime owner");
        }
        finally{Directory.Delete(ownRoot,recursive:true);}
        Console.WriteLine($"MANAGED HOME SNAPSHOT PASS {checks} actual Host/native CPU checks; private assets removed; no original media or GL.");
    }
}
