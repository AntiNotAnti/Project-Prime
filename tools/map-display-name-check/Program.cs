using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;

KeyValuePair<string, string>[] stock =
[
    new("MP1 SANCTORUS", "Data Shrine"),
    new("MP2 HARVESTER", "Harvester"),
    new("MP3 PROVING GROUND", "Combat Hall"),
    new("MP4 HIGHGROUND - EXPANDED", "Elder Passage"),
    new("MP4 HIGHGROUND", "High Ground"),
    new("MP5 FUEL SLUICE", "Compression Chamber"),
    new("MP6 HEADSHOT", "Head Shot"),
    new("MP7 PROCESSOR CORE", "Processor Core"),
    new("MP8 FIRE CONTROL", "Weapons Complex"),
    new("MP9 CRYOCHASM", "Ice Hive"),
    new("MP10 OVERLOAD", "Incubation Vault"),
    new("MP11 BREAKTHROUGH", "Sanctorus"),
    new("MP12 SIC TRANSIT", "Sic Transit"),
    new("MP13 ACCELERATOR", "Fuel Stack"),
    new("MP14 OUTER REACH", "Outer Reach"),
    new("CTF1 FAULT LINE - EXPANDED", "Fault Line"),
    new("CTF1_FAULT LINE", "Subterranean"),
    new("AD1 TRANSFER LOCK BT", "Transfer Lock"),
    new("AD1 TRANSFER LOCK DM", "Transfer Lock"),
    new("AD2 MAGMA VENTS", "Council Chamber"),
    new("AD2 ALINOS PERCH", "Alinos Perch"),
    new("UNIT1 ALINOS LANDFALL", "Alinos Gateway"),
    new("UNIT2 LANDING BAY", "Celestial Gateway"),
    new("UNIT 3 VESPER STARPORT", "VDO Gateway"),
    new("UNIT 4 ARCTERRA BASE", "Arcterra Gateway"),
    new("Gorea Prison", "Oubliette"),
    new("E3 FIRST HUNT", "Stasis Bunker")
];
int checks = 0;
Type recordingType = typeof(MapVote).Assembly.GetType("MphRead.Mods.Network.DemoRecording")!;
string replayPath = Path.Combine(Path.GetTempPath(), "prime-map-title-" + Guid.NewGuid().ToString("N") + ".ppdemo");
object Recording(string key) => Activator.CreateInstance(recordingType,
    replayPath, key, DateTime.UnixEpoch, 0L, null, ReplayOpenResult.Success, 0U)!;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    checks++;
}
foreach (var (key, name) in stock)
{
    Check(LauncherRoomDisplayName.Get(key) == name, "retail title: " + key);
    Check(LauncherRoomDisplayName.Get(key.ToLowerInvariant()) == name, "case-insensitive replay/server title: " + key);
    Check(MapPick.NameOf(key) == name, "results ballot title: " + key);
    var draft = new RmlUiOfflineDraft(new[] { key }, Hunter.Samus, 0, 0, 1);
    Check(draft.Map == name && draft.Current.RoomKey == key, "offline title preserves launch identity: " + key);
    using var controller = new RmlUiMenuController(new Services(key));
    Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline), "offline route");
    Check(controller.Tick().Bindings["offline_arena_value"].Text == name, "native offline label: " + key);
    Check(controller.Dispatch(RmlUiIntentKind.OfflineOpenArena), "open picker");
    Check(controller.Tick().Bindings["offline_arena_name0"].Text == name, "native picker title: " + key);
    controller.Dispatch(RmlUiIntentKind.OfflineCancelArena);
    Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Theatre), "Theatre route");
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (!controller.Tick().Bindings["theatre_entry0_present"].Boolean && DateTime.UtcNow < deadline)
        Thread.Sleep(5);
    var bindings = controller.Tick().Bindings;
    Check(bindings["theatre_metadata"].Text.Contains(name, StringComparison.Ordinal)
        && bindings["theatre_hero"].Text.Contains(name, StringComparison.Ordinal)
        && bindings["theatre_entry0_detail"].Text.Contains(name, StringComparison.Ordinal), "native Theatre labels: " + key);
    if (name != key)
        Check(!bindings["theatre_metadata"].Text.Contains(key, StringComparison.Ordinal), "Theatre does not leak runtime key: " + key);
    object recording = Recording(key);
    Check((string)recordingType.GetProperty("DisplayName")!.GetValue(recording)! == name
        && (string)recordingType.GetProperty("Room")!.GetValue(recording)! == key,
        "unnamed recording title preserves source identity: " + key);
    MapVote.Apply(new VoteStatePacket { State = VoteStatePacket.StateRunning, RoomKey = key, Proposer = "Fixture" });
    Check(MapVote.PromptLine() == "Fixture PROPOSES " + name.ToUpperInvariant(), "vote prompt title: " + key);
    byte[] packet = new byte[VoteStatePacket.Size];
    new VoteStatePacket { State = VoteStatePacket.StateRunning, RoomKey = MapVote.RoomKey, Proposer = "Fixture" }.Write(packet);
    Check(MapVote.RoomKey == key && VoteStatePacket.Read(packet).RoomKey == key, "network vote still carries runtime key: " + key);
    var archive = new RmlUiTheatreIndex();
    archive.Refresh(new[] { new RmlUiReplayEntry("fixture.ppdemo", "Fixture", "Battle", key, 0, 1, 0, false, false, false, true) });
    Check(archive.SearchFor(name) && archive.Count == 1 && archive.Selected?.Room == key,
        "replay title search preserves archive identity: " + key);
}
MapVote.Reset();
try
{
    File.WriteAllText(replayPath + ".name", "My custom replay title");
    Check((string)recordingType.GetProperty("DisplayName")!.GetValue(Recording(stock[0].Key))! == "My custom replay title",
        "explicit replay rename remains unchanged");
}
finally { File.Delete(replayPath + ".name"); }
foreach (var room in Metadata.RoomMetadata.Values)
    if (!String.IsNullOrWhiteSpace(room.InGameName))
        Check(LauncherRoomDisplayName.Get(room.Name) == room.InGameName, "metadata title: " + room.Name);
Check(MapPick.NameOf(MapPick.ReturnToLobbyKey) == "Return to lobby", "non-map ballot action remains intact");
Check(LauncherRoomDisplayName.Get(null) == "" && LauncherRoomDisplayName.Get("  ") == "", "empty room placeholders");
Check(LauncherRoomDisplayName.Get("Uninstalled Custom Arena") == "Uninstalled Custom Arena", "unknown/custom supplied title retained");
Check(LauncherRoomDisplayName.Get("Sanctorus") == "Sanctorus", "display name never remapped as a code suffix");
Check(LauncherRoomDisplayName.Get("Breakthrough") == "Breakthrough", "unknown identifiers never guessed");
Console.WriteLine($"MAP DISPLAY NAME PASS {checks} checks: all 27 stock multiplayer maps, remaining named metadata, native picker, votes, results and replay search; runtime identities preserved.");

sealed class Services(string room) : IRmlUiMenuServices
{
    public HubSnapshot Hub() => new("Fixture", Hunter.Samus, Hunter.Samus, 0, true, "");
    public string BuildVersion() => "map-display-fixture";
    public RmlUiLobbyView Lobby() => new(false, false, false, false, false, false, false,
        "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
    public IReadOnlyList<string> BuiltInArenaKeys() => new[] { room };
    public Task<RmlUiReplayEntry[]> ReadTheatreAsync(CancellationToken token)
        => Task.FromResult(new[] { new RmlUiReplayEntry("fixture.ppdemo", "Fixture", "Battle", room, 0, 1, 0,
            false, false, false, true) });
    public Task<RmlUiDirectory> Browse(CancellationToken token) => Task.FromResult(new RmlUiDirectory(false, "", []));
    public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) => Task.FromResult<RmlUiServerCandidate?>(null);
    public Task<RmlUiJoinResult> Join(string endpoint, string name, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, default, ""));
    public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token) => Task.FromResult(new RmlUiJoinResult(false, default, ""));
    public bool AcceptJoin(LaunchPlan plan) => false;
    public bool OpenCreateLobby() => false;
    public bool NavigateTo(RmlUiRouteArgument destination) => false;
    public bool OpenStudio() => false;
    public bool SetReady() => false;
    public bool StartMatch() => false;
    public bool LeaveLobby() => false;
    public bool NextHunter() => false;
    public bool NextSuit() => false;
    public bool OpenLobbyDetails() => false;
    public RmlUiRuleDraft? ReadRules() => null;
    public bool SubmitRules(RmlUiRuleDraft draft, out string error) { error = ""; return false; }
    public void Quit() { }
}
