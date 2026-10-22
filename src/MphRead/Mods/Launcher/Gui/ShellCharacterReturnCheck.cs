#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia.VisualTree;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static string? _returnPhase;
    private static readonly List<object> _returnFrames = new();
    private static int _returnWanted, _returnDrawn, _returnWeighted, _returnMissed;
    private static long _returnDeadline;
    private static ulong _returnMatchFrame;

    // A content-backed extension of the existing real-window shell capture.
    // UI actions are delivered through its normal hit-tested control path.
    private static Action<RenderWindow>[] SamusReturnScript => new Action<RenderWindow>[]
    {
        w => { w.ClientSize = new Vector2i(1100, 740); Wait(25); },
        _ =>
        {
            if (_front?.GetVisualDescendants().OfType<PrimeStartupScreen>().Any() == true)
                Key(Keys.Enter);
            Wait(40);
        },
        _ =>
        {
            RequireReturn(GameFiles.Ready, "Extracted game files are required.");
            RequireReturn(RenderOptions.CharacterModelReplacements, "HD character models must be enabled.");
            LauncherPrefs.LastHunter = Hunter.Samus;
            LauncherPrefs.LastColor = 0;
            LauncherPrefs.Bots = 1;
            LauncherPrefs.BotLevel = 0;
            _settings.RoomKey = "TEST ARENA";
            Click(c => FrontAction(c, "OFFLINE"));
            _returnPhase = "before";
            Wait(120);
        },
        w =>
        {
            CheckReturnPreview("before");
            Shot(w, "samus-before-match");
            _returnPhase = null;
            Click(c => c.GetValue(ControllerNav.NavIdProperty) == "offline.start");
            _returnDeadline = Environment.TickCount64 + 120000;
            Wait(20);
        },
        w =>
        {
            if (!w.HasScene)
            {
                RequireReturn(Environment.TickCount64 < _returnDeadline, "Offline button did not start a match.");
                _shotStep--; Wait(20); return;
            }
            RequireReturn(w.Scene.Players.Main.Hunter == Hunter.Samus, "Local player is not Samus.");
            RequireReturn(w.Scene.Players.Items.Any(p => p.IsBot), "No AI bot was loaded.");
            _returnMatchFrame = w.Scene.FrameCount;
            _returnDeadline = Environment.TickCount64 + 30000;
            Wait(20);
        },
        w =>
        {
            RequireReturn(w.HasScene, "Match ended before the Leave Match test.");
            if (w.Scene.FrameCount - _returnMatchFrame < 120)
            {
                RequireReturn(Environment.TickCount64 < _returnDeadline, "Match simulation did not advance.");
                _shotStep--; Wait(20); return;
            }
            Shot(w, "samus-bot-match");
            PauseMenu.HandleEscape(w);
            Wait(25);
        },
        w =>
        {
            RequireReturn(UiVisible && PauseMenu.Open, "Pause menu did not open.");
            Shot(w, "samus-leave-menu");
            int misses = ShotMisses;
            Click(c => FrontAction(c, "LEAVE MATCH"));
            RequireReturn(ShotMisses == misses, "Leave Match button was not reachable.");
            _returnPhase = "return-home";
            _returnDeadline = Environment.TickCount64 + 30000;
            Wait(1);
        },
        w =>
        {
            if (w.HasScene)
            {
                RequireReturn(Environment.TickCount64 < _returnDeadline, "Leave Match did not unload the scene.");
                _shotStep--; Wait(1); return;
            }
            RequireReturn(UiVisible, "Launcher did not return after Leave Match.");
            Shot(w, "samus-return-home");
            // Leave Match may already restore Offline. Avoid reporting a
            // missed Home action when the requested preview is already open.
            bool offlineOpen = _front?.GetVisualDescendants()
                .Any(c => c.GetValue(ControllerNav.NavIdProperty) == "offline.start") == true;
            if (!offlineOpen) Click(c => FrontAction(c, "OFFLINE"));
            ResetReturnCounts();
            _returnPhase = "after";
            Wait(1);
        },
        w => { Shot(w, "samus-return-first"); Wait(30); },
        w => { Shot(w, "samus-return-030"); Wait(90); },
        w =>
        {
            CheckReturnPreview("after");
            Shot(w, "samus-after-match");
            File.WriteAllText(Path.Combine(_shotDirectory!, "launcher-return.json"), JsonSerializer.Serialize(new
            {
                result = "PASS", backend = GraphicsBackendPolicy.Resolved.ToString(),
                scope = "Real launcher Offline start and pause-menu Leave Match hit-tested controls; same window and process.",
                wantedFrames = _returnWanted, drawnFrames = _returnDrawn,
                weightedFrames = _returnWeighted, missingFrames = _returnMissed,
                matchSimulationFrames = 120, frames = _returnFrames
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[samusreturn] PASS: {_returnWeighted}/{_returnWanted} returned Weighted4 preview frames");
            _returnPhase = null;
            Wait(5);
        }
    };

    private static void RequireReturn(bool condition, string message)
    {
        if (condition) return;
        ShotMisses++;
        File.WriteAllText(Path.Combine(_shotDirectory!, "failure.txt"), message);
        throw new InvalidOperationException("Samus launcher return: " + message);
    }

    private static void ResetReturnCounts() =>
        (_returnWanted, _returnDrawn, _returnWeighted, _returnMissed) = (0, 0, 0, 0);

    private static void CheckReturnPreview(string phase)
    {
        RequireReturn(_returnWanted >= 60, phase + ": preview was not requested for at least 60 frames.");
        RequireReturn(_returnMissed == 0 && _returnWeighted == _returnWanted,
            phase + ": block/native fallback or missing Weighted4 preview frame.");
        Console.WriteLine($"[samusreturn] {phase}: {_returnWeighted}/{_returnWanted} Weighted4 frames; {_returnMissed} missing");
    }

    private static void ObserveSamusReturn(RenderWindow window)
    {
        if (_returnPhase == null) return;
        int weighted = 0;
        if (LauncherHunter.Drawn)
        {
            Scene? scene = window.HasScene ? window.Scene :
                (Scene?)typeof(LauncherHunter).GetField("_scene", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
            if (scene != null)
            {
                var items = (List<RenderItem>)typeof(Scene).GetField("_previewItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                weighted = items.Count(item => item.WeightedSkinning);
            }
        }
        bool samus = LauncherHunter.Hunter == Hunter.Samus;
        bool requested = LauncherHunter.Wanted;
        if (_returnPhase is "before" or "after" && requested)
        {
            _returnWanted++;
            if (LauncherHunter.Drawn) _returnDrawn++;
            if (weighted >= 4 && samus && RenderOptions.CharacterModelReplacements) _returnWeighted++;
            else _returnMissed++;
        }
        _returnFrames.Add(new { phase = _returnPhase, requested, drawn = LauncherHunter.Drawn,
            weightedPackets = weighted, samus, hd = RenderOptions.CharacterModelReplacements, match = window.HasScene });
    }
}

#endif
