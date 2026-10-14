using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MphRead;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Enter before toolkit/feedback initialization: no game GL, runtime or native audio.
Headless.Enter();
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")))
    throw new InvalidOperationException("Set PROJECT_PRIME_USER_DATA to an isolated fixture directory.");
string output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/results-ui-check");
Directory.CreateDirectory(output);
if (!GuiLauncher.EnsureSetup(requireDisplay: false)) return 2;
Deck.Still = true;
ModalPreviewChecks.Run();

var scene = new Scene(default, null!, null!, _ => { }, () => { }, initializeRuntime: false);
GameState.Current = scene.GameState;
PlayerEntity.LegacyRegistry = scene.Players;
GameState.Mode = GameMode.Battle;
GameState.MatchState = MatchState.GameOver;
GameState.MatchTime = 15;
var originalHunter = LauncherPrefs.LastHunter;
var originalSuit = LauncherPrefs.LastColor;
int checks = 0;
void Check(bool ok, string name)
{
    checks++;
    if (!ok) throw new InvalidOperationException(name);
    Console.WriteLine("RESULTS PASS " + name);
}
Check(!EndScreen.CharacterChangeEnabled && EndScreen.Available, "legacy picker disabled during active results");
Check(!EndScreen.HandleKeyDown(Keys.Left) && !EndScreen.HandleKeyDown(Keys.Right), "results arrows cannot select Hunter");
// Also characterizes the former no-op API when run against the pristine baseline.
typeof(EndScreen).GetMethod("Pick")?.Invoke(null, new object[] { Hunter.Kanden, 3 });
Check(LauncherPrefs.LastHunter == originalHunter && LauncherPrefs.LastColor == originalSuit,
    "legacy picker does not write persisted choice");

var rooms = (List<string>)typeof(MapPick).GetField("_rooms", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
rooms.Clear();
rooms.AddRange(new[] { "MP1 SANCTORUS", "MP2 HARVESTER" });
var tileGradientHashes = rooms.ToDictionary(room => room, room => room.GetHashCode());
MapPick.Apply(new MapChoicesPacket { Open = 1, Count = 0, RoomKeys = Array.Empty<string>(), Votes = Array.Empty<byte>(), Eligible = 2 });
Check(MapPick.Available && MapPick.Order.Count == 2, "results ballot active");
Check(EndScreen.HandleKeyDown(Keys.Down) && MapPick.Cursor == 1, "ballot down advances native cursor");
string chosenBefore = MapPick.Order[MapPick.Cursor];
Check(EndScreen.HandleKeyDown(Keys.Enter) && MapPick.Picked == chosenBefore, "ballot Enter chooses actual map");
MapPick.NoteLayout(new[] { new EndScreen.Hit(0, 0, .5f, 1), new EndScreen.Hit(.5f, 0, 1, 1) }, 2);
EndScreen.NotePointer(.25f, .5f);
string clickedBefore = MapPick.Order[0], previousPick = MapPick.Picked;
Check(EndScreen.HandleClick() && MapPick.Picked == (previousPick == clickedBefore ? "" : clickedBefore),
    "results pointer selects ballot");

var hashes = new Dictionary<string, string>();
void Shot(string name)
{
    var view = new EndPanelView();
    var window = new Avalonia.Controls.Window { Width = 1280, Height = 720, Content = view };
    try
    {
        window.Show();
        for (int i = 0; i < 4; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Check(!view.GetVisualDescendants().OfType<ChoiceRow>().Any(x => x.IsEffectivelyVisible),
            "no visible Hunter/Suit picker " + name);
        using var bitmap = new RenderTargetBitmap(new PixelSize(1280, 720), new Vector(96, 96));
        bitmap.Render(window);
        using var bytes = new MemoryStream();
        bitmap.Save(bytes, PngBitmapEncoderOptions.Default);
        hashes[name] = Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
        File.WriteAllBytes(Path.Combine(output, name + ".png"), bytes.ToArray());
    }
    finally { window.Close(); }
}
Shot("offline-ballot");
MapPick.Reset();
Shot("empty-ballot");
try
{
    // Loopback-only session metadata; no real account, social service or server.
    NetSession.StartClient("127.0.0.1", 9);
    NetSession.ApplySessionState(new SessionStatePacket
    {
        Policy = ServerSessionPolicy.Lobby, Phase = SessionPhase.PostMatch, MatchId = 1, AuthorityEpoch = 1,
        MaxPlayers = 8, WorldProfile = MatchWorldProfile.Resolve(8),
        Match = new MatchDefinition { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle }
    });
    GameState.MatchState = MatchState.Ending;
    GameState.MatchTime = 15;
    Shot("persistent-return");
}
finally { NetSession.Stop(); }
Check(LauncherPrefs.LastHunter == originalHunter && LauncherPrefs.LastColor == originalSuit,
    "ballot and presentation preserve saved Hunter/Suit");
var evidence = new
{
    ProductMvid = typeof(Scene).Assembly.ManifestModule.ModuleVersionId,
    ProductSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Scene).Assembly.Location))),
    Hashes = hashes,
    // DeckTile.Drift uses these process-randomized hashes; compare its tile interiors separately.
    TileGradientHashes = tileGradientHashes,
    Checks = checks
};
File.WriteAllText(Path.Combine(output, "evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("RESULTS PRODUCT " + evidence.ProductMvid);
Console.WriteLine("RESULTS HASHES " + JsonSerializer.Serialize(hashes));
Console.WriteLine("RESULTS PASS " + checks + " assertions");
return 0;
