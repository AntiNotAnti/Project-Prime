using System;
using System.Globalization;
using System.Numerics;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>Display-only start facts; the session owns the countdown and release.</summary>
public readonly record struct RmlUiLobbyStartPresentation(string Number, string Detail)
{
    public static RmlUiLobbyStartPresentation Read(SessionStatePacket? session, double remainingSeconds)
    {
        if (session is not { Phase: SessionPhase.Starting } start) return new("", "");
        int expected = BitOperations.PopCount((uint)start.ExpectedParticipants);
        if (start.StartStage == StartStage.Countdown && remainingSeconds > 0)
            return new(Math.Max(1, (int)Math.Ceiling(remainingSeconds)).ToString(CultureInfo.InvariantCulture),
                "READY // MATCH STARTING");
        if (start.StartStage == StartStage.Countdown)
            return new("READY", "WAITING FOR WORLD RELEASE");
        if (start.StartStage == StartStage.Synchronizing)
            return new("...", $"SYNCHRONIZING WORLD // {BitOperations.PopCount((uint)(start.WorldReadyParticipants & start.ExpectedParticipants))}/{expected} READY");
        return new("...", $"LOADING WORLD // {BitOperations.PopCount((uint)(start.LoadedParticipants & start.ExpectedParticipants))}/{expected} LOADED");
    }
}
