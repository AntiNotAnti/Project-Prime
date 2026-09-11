using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network
{
    /// <summary>Default Theatre facade. Independent consumers use <see cref="ReplayPlaybackSession"/>.</summary>
    public static class ReplayPlayback
    {
        private static readonly ReplayPlaybackSession _default = new(new TheatreReplaySessionHost());
        internal const uint TransientWarmupTicks = 1801;
        internal const uint JoinSearchFrames = 60 * 20;
        internal const uint JoinGraceFrames = 120;
        internal static object? SessionIdentity => _default.SessionIdentity;
        internal static ModernReplayState Modern => _default.Modern;
        public static MatchRules? InitialRules => _default.InitialRules;
        public static uint? SnapshotServerTick => _default.SnapshotServerTick;
        public static uint? WorldServerTick => _default.WorldServerTick;
        public static bool IsModern => _default.IsModern;
        public static bool ApplyingSnapshot => _default.ApplyingSnapshot;
        public static bool IsActive => _default.IsActive;
        public static ReplayTransport Transport => _default.Transport;
        public static bool IsSeeking => _default.IsSeeking;
        public static uint CurrentFrame => _default.CurrentFrame;
        public static uint DurationFrames => _default.DurationFrames;
        public static bool CanSeek => _default.CanSeek;
        public static IReadOnlyList<ReplayIndexEntry> Index => _default.Index;
        public static uint LastRestoreFrame => _default.LastRestoreFrame;
        public static int LastSeekSteps => _default.LastSeekSteps;
        public static double LastSeekMilliseconds => _default.LastSeekMilliseconds;
        public static bool AtEnd => _default.AtEnd;
        public static bool ShouldExitAtEnd => _default.ShouldExitAtEnd;
        public static ReplayHighlight? CurrentHighlight => _default.CurrentHighlight;
        public static string? LastError => _default.LastError;
        public static bool Prepare(string path) => _default.Prepare(path);
        internal static bool ConsumePrepared(string path) => _default.ConsumePrepared(path);
        public static bool Join(string path, int timeoutMs = 8000) => _default.Join(path, timeoutMs);
        public static bool Seek(uint frame) => _default.Seek(frame);
        public static bool SeekEvent(bool next) => _default.SeekEvent(next);
        public static void ConfigureHighlights(IReadOnlyList<ReplayHighlight> highlights)
            => _default.ConfigureHighlights(highlights);
        internal static bool AdvanceHighlightRange() => _default.AdvanceHighlightRange();
        internal static int TakeSimulationSteps() => _default.TakeSimulationSteps();
        internal static bool ProcessSeek(Action simulationStep, Action? beginSeek = null)
            => _default.ProcessSeek(simulationStep, beginSeek);
        internal static void ApplyRoster(Scene scene) => _default.ApplyRoster(scene);
        public static void BeforeSimulation(Scene scene) => _default.BeforeSimulation(scene);
        public static void AfterSimulation(Scene scene) => _default.AfterSimulation(scene);
        public static void PumpFrame() => _default.PumpFrame();
        public static void Stop() => _default.Stop();
        internal static void CloseFile() => _default.CloseFile();
    }
}
