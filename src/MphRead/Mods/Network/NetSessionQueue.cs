using System;

namespace MphRead.Mods.Network;

public static partial class NetSession
{
    /// <summary>Adopt the exact transport and Welcome from a reserved-seat admission.
    /// Reopening a socket here would lose the authenticated queue connection.</summary>
    internal static void StartQueuedClient(LobbyQueueClient queue)
    {
        if (!queue.Admitted || queue.ClientId != ClientId)
            throw new InvalidOperationException("The accepted queue connection belongs to another client.");
        Stop();
        var admission = queue.TakeAdmission();
        _ownerToken = Guid.Empty;
        _transport = admission.Transport;
        _transport.EnableRealtimeStateCoalescing();
        _transport.AnswerPingsImmediately();
        _hostEndPoint = admission.Server;
        _lastServerPacket = Clock;
        Role = NetRole.Client;
        LocalSlot = -1;
        NetFrame = 0;
        LastError = null;
        NetLog.Open(PlayerName);
        NetLog.Event("adopting reserved-seat queue admission");
        Handle(admission.Welcome, Clock);
        SendIdentify();
    }
}
