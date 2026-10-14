using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Opt-in CAMP-07 native single-player room load with a user's extracted
/// cartridge. The content-free suite cannot execute this. It proves real
/// room/actor simulation and original save bytes, NOT actual controller,
/// scan acquisition, ship boarding, video/audio, GL or boss completion.
/// </summary>
internal static class CampaignNativeSceneTests
{
    internal static int Run(string output)
    {
        Headless.Enter();
        if (File.Exists(output))
        {
            Console.Error.WriteLine("CAMP-07 native receipt already exists; preserve prior evidence.");
            return 1;
        }
        string priorDirectory = Directory.GetCurrentDirectory();
        string root = Path.Combine(Path.GetTempPath(),
            "prime-camp07-native-" + Guid.NewGuid().ToString("N"));
        SceneGameState originalState = GameState.Current;
        ScenePlayerRegistry originalPlayers = PlayerEntity.LegacyRegistry;
        var originalRng = Rng.Current;
        byte originalSlot = Menu.SaveSlot;
        SaveWhen originalWhen = Menu.NeededSave;
        bool originalSkip = Cheats.SkipPlanetIntros;
        string status = "FAIL";
        string? failure = null;
        ulong firstFrame = 0, finalFrame = 0;
        int loadedRoom = -1, entities = 0, players = 0;
        string savedHash = "";
        int saveBytes = 0;
        int simulatedFrames = 0;
        Scene? scene = null;
        PostMatchCommitContext? receipt = null;
        try
        {
            Directory.CreateDirectory(root);
            Directory.SetCurrentDirectory(root);
            Menu.SaveSlot = 0;
            Menu.NeededSave = SaveWhen.Never;
            Cheats.SkipPlanetIntros = true;
            scene = new Scene(new Vector2i(256, 192),
                SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(),
                _ => { }, () => { });
            scene.Players.MaxPlayers = 4;
            scene.AddPlayer(Hunter.Samus);
            scene.AddRoom("UNIT2_LAND", GameMode.SinglePlayer, playerCount: 1);
            scene.OnLoad();
            loadedRoom = scene.RoomId;
            players = scene.Players.PlayerCount;
            entities = 0;
            foreach (EntityBase entity in scene.Entities) entities++;
            if (loadedRoom != 45 || players != 1 || entities < 2
                || !scene.GameState.SinglePlayer)
                throw new InvalidOperationException("Native Celestial Archives Adventure room/player/entities did not load.");
            firstFrame = scene.FrameCount;
            for (int i = 0; i < 90; i++)
            {
                scene.OnSimulationFrame();
                simulatedFrames++;
            }
            finalFrame = scene.FrameCount;
            if (finalFrame <= firstFrame)
                throw new InvalidOperationException("Native Adventure simulation did not advance any frames.");

            // The real StorySave was constructed by the native Adventure
            // scene from supplied game data. Simulate a verified hatch exit
            // after loading; DO NOT claim the ship/hatch was played.
            StorySave save = scene.GameState.StorySave;
            save.CheckpointRoomId = 45;
            save.SetVisitedRoom(45);
            byte[] expected = PostMatchSaveTests.LegacySave(save);
            scene.GameState.LastCampaignDeparture =
                MphRead.Mods.Campaign.CampaignDeparture.ReturnedToShip;
            scene.GameState.LastCampaignDepartureRoomId = 45;
            Menu.SaveSlot = 1;
            Menu.NeededSave = SaveWhen.Always;
            receipt = MatchStart.PrepareBeforeTeardown(scene.GameState)
                ?? throw new InvalidOperationException("Native scene unexpectedly in room transition.");
            if (!receipt.HasPayload)
                throw new InvalidOperationException("Native save capture contained no payload.");

            scene.DoCleanup();
            scene.UnloadGl();
            MatchStart.AfterMatch(receipt);
            if (!ForegroundSceneBinding.TryRelease(receipt))
                throw new InvalidOperationException("Scene cleanup left an unowned foreground binding.");
            string savePath = Path.Combine("Savedata", "save001.json");
            byte[] actual = File.ReadAllBytes(savePath);
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException("Native StorySave bytes differ from frozen legacy serializer.");
            StorySave? continued = GameState.PeekSave(1);
            if (continued?.CheckpointRoomId != 45 || !continued.CheckVisitedRoom(45))
                throw new InvalidOperationException("Native StorySave did not roundtrip through file select.");
            saveBytes = actual.Length;
            savedHash = Convert.ToHexString(SHA256.HashData(actual)).ToLowerInvariant();
            status = "PASS";
            Console.WriteLine($"CAMP-07 NATIVE PASS room={loadedRoom} players={players} entities={entities}"
                + $" stepped={simulatedFrames} savedBytes={saveBytes} hash={savedHash}");
            return 0;
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name + ": " + ex.Message;
            Console.Error.WriteLine("CAMP-07 NATIVE FAIL " + failure);
            return 1;
        }
        finally
        {
            try
            {
                if (scene != null && receipt == null)
                {
                    scene.DoCleanup();
                    scene.UnloadGl();
                }
                if (File.Exists("Savedata")) File.Delete("Savedata");
                MatchStart.RetryPendingCommit();
            }
            finally
            {
                Read.ClearCache();
                GameState.Current = originalState;
                PlayerEntity.LegacyRegistry = originalPlayers;
                Rng.Current = originalRng;
                Menu.SaveSlot = originalSlot;
                Menu.NeededSave = originalWhen;
                Cheats.SkipPlanetIntros = originalSkip;
                MphRead.Mods.Launcher.MatchStart.ClearRecentShipDock();
                MphRead.Mods.Launcher.MatchStart.ClearRecentCampaignEnding();
                Directory.SetCurrentDirectory(priorDirectory);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, JsonSerializer.Serialize(new
                {
                    schema = 1, status, failure,
                    scope = "asset-backed headless native Adventure room simulation and save parity",
                    nativeCartridgeFilesUsed = true,
                    nativeGlAudioMoviesControllerInput = "NOT_RUN",
                    actualShipHatchOrBossPlayed = "NOT_RUN",
                    loadedRoom, players, entities, firstFrame, finalFrame,
                    simulatedFrames, saveBytes, savedHash
                }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }
    }
}
