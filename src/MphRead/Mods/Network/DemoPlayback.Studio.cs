using System;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Network;
public static partial class DemoPlayback
{
    /// <summary>Borrow the exact settled foreground world for explicit read-only Studio evidence. Ownership stays here.</summary>
    internal static PassiveReplayScene ReviewedStudioWorld(bool allowPlaying=false)
    {
        if(_player is not{Ready:true} player||!ReferenceEquals(PresentationScene,player.Current.Scene)||!IsActive
            ||player.Transport.IsSeeking||(!allowPlaying&&!(player.Transport.IsPaused||player.Transport.AtEnd))||ReplayVideoExporter.Active)
            throw new InvalidOperationException("Pause and wait for the foreground replay before comparing worlds.");
        return player.Current;
    }
}
