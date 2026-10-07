using System;

namespace MphRead.Mods.Network;

public static partial class NetSession
{
    /// <summary>Adopt the exact transport and Welcome from a reserved-seat admission.
    /// Reopening a socket here would lose the authenticated queue connection.</summary>
    internal static void StartQueuedClient(LobbyQueueClient queue)
    {
        if (!queue.Admitted || queue.ClientId != ClientId)
            throw new InvalidOperationException(
                "The accepted queue connection belongs to another client.");
        var admission = queue.TakeAdmission();
        AdoptReservedAdmission(
            admission.Transport,
            admission.Server,
            admission.ClientId,
            admission.Welcome,
            "reserved-seat queue admission");
    }

    internal static void StartPartyReservedClient(PartyReservedAdmission reservation)
    {
        if (reservation == null)
            throw new ArgumentNullException(nameof(reservation));

        var admission = reservation.Take();
        if (admission.ClientId != ClientId)
        {
            admission.Transport.Dispose();
            throw new InvalidOperationException(
                "The accepted party reservation belongs to another client.");
        }

        AdoptReservedAdmission(
            admission.Transport,
            admission.Server,
            admission.ClientId,
            admission.Welcome,
            "party-reserved admission");
    }

    private static void AdoptReservedAdmission(
        NetTransport transport,
        System.Net.IPEndPoint server,
        uint clientId,
        ReceivedPacket welcome,
        string label)
    {
        if (clientId != ClientId)
        {
            transport.Dispose();
            throw new InvalidOperationException(
                "The reserved admission belongs to another client.");
        }

        Stop();
        _ownerToken = Guid.Empty;
        _transport = transport;
        _transport.EnableRealtimeStateCoalescing();
        _transport.AnswerPingsImmediately();
        _hostEndPoint = server;
        _lastServerPacket = Clock;
        Role = NetRole.Client;
        LocalSlot = -1;
        NetFrame = 0;
        LastError = null;
        NetLog.Open(PlayerName);
        NetLog.Event("adopting " + label);
        Handle(welcome, Clock);
        SendIdentify();
    }
}
