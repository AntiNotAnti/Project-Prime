using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;

// Synthetic UI state publication only: no role, gameplay, transport or device acceptance.
Headless.Enter();
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")))
    throw new InvalidOperationException("Set isolated PROJECT_PRIME_USER_DATA.");
string userData = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")!);
Directory.CreateDirectory(userData);
string fixtureDirectory = Path.Combine(userData, "fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtureDirectory); Directory.SetCurrentDirectory(fixtureDirectory);
if (!GuiLauncher.EnsureSetup(requireDisplay: false)) return 2;
const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException(message); Console.WriteLine("LEGACY MOVEMENT UI PASS " + message); }
object Field(object value, string name) => value.GetType().GetField(name, Hidden)!.GetValue(value)!;
void Set(object value, string name, object? state) => value.GetType().GetField(name, Hidden)!.SetValue(value, state);
object? Call(object value, string name, params object[] args) => value.GetType().GetMethod(name, Hidden)!.Invoke(value, args);
void Click(HubNavButton button) => ((EventHandler)typeof(HubNavButton).GetField("Click", Hidden)!.GetValue(button)!).Invoke(button, EventArgs.Empty);
var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
GameState.Current = scene.GameState; PlayerEntity.LegacyRegistry = scene.Players;
GameState.Mode = GameMode.Battle;
var localSlot = typeof(NetSession).GetProperty(nameof(NetSession.LocalSlot))!;
var sessionProperty = typeof(NetSession).GetProperty(nameof(NetSession.ServerSession))!;
int oldSlot = NetSession.LocalSlot; var oldSession = NetSession.ServerSession;
try
{
    localSlot.SetValue(null, 0);
    var session = new SessionStatePacket { AuthorityEpoch = 7, MatchId = 1, Revision = 1, OwnerSlot = 0,
        Phase = SessionPhase.Lobby, Policy = ServerSessionPolicy.Lobby, MaxPlayers = 8,
        WorldProfile = MphRead.Mods.Multiplayer.MatchWorldProfile.Resolve(8),
        Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle } };
    sessionProperty.SetValue(null, session);
    var lobby = new LobbyScreen(new[] { "MP1 SANCTORUS" });
    void Publish() { sessionProperty.SetValue(null, session); Call(lobby, "Refresh"); }
    Publish();
    var movement = (ChoiceRow)Field(lobby, "_movement");
    Check(movement.Label == "Movement" && movement.Value == "Classic", "Native default displayed as Classic");
    Check(ToolTip.GetTip(movement)?.ToString()?.Contains("air-strafing") == true, "movement help describes air-strafing");
    movement.Index = 1;
    Check(((MatchDefinition)Call(lobby, "DraftMatch")!).MovementModel == MovementModel.Momentum && (bool)Field(lobby, "_draftDirty"), "real choice Changed authors Momentum draft");
    // Exact current acknowledgment clears the movement edit.
    Set(lobby, "_submittedMatch", session.Match with { MovementModel = MovementModel.Momentum });
    Set(lobby, "_submittedRules", session.RuleFlags);
    Set(lobby, "_submittedDraftVersion", Field(lobby, "_draftVersion"));
    session.Match = session.Match with { MovementModel = MovementModel.Momentum }; session.Revision++; Publish();
    Check(!(bool)Field(lobby, "_movementDirty") && !(bool)Field(lobby, "_draftDirty"), "exact current draft acknowledgment clears dirty state");
    // Submit Classic, then make a newer Momentum edit while crossing the network.
    movement.Index = 0;
    Set(lobby, "_submittedMatch", session.Match with { MovementModel = MovementModel.Native });
    Set(lobby, "_submittedRules", session.RuleFlags); Set(lobby, "_submittedDraftVersion", Field(lobby, "_draftVersion"));
    movement.Index = 1;
    session.Match = session.Match with { MovementModel = MovementModel.Native }; session.Revision++; Publish();
    Check(movement.Index == 1 && (bool)Field(lobby, "_movementDirty"), "older acknowledgment preserves newer movement choice");
    // Unrelated authoritative changes must also retain that newer movement edit.
    session.Match = session.Match with { PointGoal = 23 }; session.Revision++; Publish();
    Check(movement.Index == 1, "unrelated authoritative revision retains movement draft");
    session.Match = session.Match with { MovementModel = MovementModel.Momentum }; session.Revision++; Publish();
    Check(movement.Index == 1 && !(bool)Field(lobby, "_movementDirty"), "external model change resets movement dirty state");
    movement.Index = 0; session.AuthorityEpoch++; session.MatchId++; Publish();
    Check(movement.Index == 1 && !(bool)Field(lobby, "_movementDirty"), "new session generation resynchronizes choice");
    session.OwnerSlot = 1; Publish();
    Check(!((Control)Field(lobby, "_ownerControls")).IsEnabled, "nonowner cannot edit owner controls");
    session.OwnerSlot = 0; session.Phase = SessionPhase.Starting; Publish();
    Check(!((Control)Field(lobby, "_ownerControls")).IsEnabled, "starting match freezes owner controls");
    sessionProperty.SetValue(null, null);

    var overlays = new PrimeOverlayHost();
    string save = Path.Combine(Environment.CurrentDirectory, "Savedata");
    ChoiceRow Open(OfflineWorkspace workspace)
    {
        Call(workspace, "Rules");
        return overlays.GetLogicalDescendants().OfType<ChoiceRow>().Single(row => row.Label == "Movement");
    }
    void Apply() => Click(overlays.GetLogicalDescendants().OfType<PrimeButton>().Single(button => button.Label == "APPLY RULES"));
    var settings = new MenuSettings { MovementModel = "future-model", TimeLimit = "4:00", PointGoal = "19", Fiesta = "on" };
    var offline = new OfflineWorkspace(settings, new[] { "MP1 SANCTORUS" }, overlays);
    var offlineMovement = Open(offline);
    Check(offlineMovement.Value == "Choose movement", "unknown saved movement has explicit selection sentinel");
    Apply();
    Check(settings.MovementModel == "future-model" && !File.Exists(Path.Combine(save, "settings.json")), "unknown movement Apply cannot silently rewrite to Native");
    Check(overlays.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("Saved movement is unknown") == true), "unknown movement error stays visible");
    offlineMovement.Index = 1; Apply();
    Check(settings.MovementModel == "Momentum" && File.Exists(Path.Combine(save, "settings.json")), "explicit Momentum selection persists through actual Apply callback");
    using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(save, "settings.json"))))
        Check(json.RootElement.GetProperty("MenuSettings").GetProperty("MovementModel").GetString() == "Momentum", "saved settings retains selected model");
    Check(settings.TimeLimit == "4:00" && settings.PointGoal == "19" && settings.Fiesta == "on", "movement apply preserves unrelated rules");
    offlineMovement = Open(offline); offlineMovement.Index = 0;
    Click(overlays.GetLogicalDescendants().OfType<PrimeButton>().Single(button => button.Label == "CANCEL"));
    Check(settings.MovementModel == "Momentum", "cancel retains committed movement");
    // Actual filesystem failure exercises existing callback rollback.
    File.Move(Path.Combine(save, "settings.json"), Path.Combine(save, "settings-success.json"));
    Directory.CreateDirectory(Path.Combine(save, "settings.json"));
    offlineMovement = Open(offline); offlineMovement.Index = 0; Apply();
    Check(settings.MovementModel == "Momentum", "write failure rolls movement back atomically");
    Check(overlays.GetLogicalDescendants().OfType<TextBlock>().Any(text => text.Text?.StartsWith("Could not save rules:") == true), "save failure remains visible for retry");
    File.WriteAllText(Path.Combine(userData, "legacy-ui-evidence.json"), JsonSerializer.Serialize(new {
        checks, productMvid = typeof(Scene).Assembly.ManifestModule.ModuleVersionId, fixtureDirectory,
        scope = "Synthetic staged UI session; actual Avalonia ChoiceRow Changed, lobby draft/ack refresh and offline Apply/Cancel callbacks; isolated real settings persistence and filesystem-failure rollback; no gameplay/UDP/physical acceptance"
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"LEGACY MOVEMENT UI PASS {checks} assertions; synthetic staged session + actual Avalonia controls/callbacks + isolated persistence; no gameplay/UDP/physical acceptance");
    return 0;
}
finally
{
    sessionProperty.SetValue(null, oldSession); localSlot.SetValue(null, oldSlot);
    try { scene.DoCleanup(); } finally { scene.UnloadGl(); }
}
