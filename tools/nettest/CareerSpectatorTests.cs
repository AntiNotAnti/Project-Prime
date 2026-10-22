using System;
using System.Collections;
using System.Reflection;
using MphRead.Mods;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Asset-free coverage for the authoritative career lifecycle, not the
// presentation-only spectator camera. No Supabase credentials are needed.
internal static class CareerSpectatorTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int _checks;

    public static int Run()
    {
        try
        {
            var rotation = MapRotation.FromList(
                new[] { ("MP1 SANCTORUS", GameMode.Battle) }, 300, 5);
            var server = new DedicatedServer(0, 8, rotation);
            Type serverType = typeof(DedicatedServer);
            Type matchType = serverType.GetNestedType("CareerMatchState", BindingFlags.NonPublic)!;
            Type peerType = serverType.GetNestedType("Peer", BindingFlags.NonPublic)!;
            object careerMatch = Activator.CreateInstance(matchType, nonPublic: true)!;
            serverType.GetField("_careerMatch", PrivateInstance)!.SetValue(server, careerMatch);

            var participants = (IDictionary)matchType.GetField("Participants", AnyInstance)!
                .GetValue(careerMatch)!;
            MethodInfo activate = serverType.GetMethod("CareerActivate", PrivateInstance)!;
            MethodInfo setRole = serverType.GetMethod("CareerSetSpectating", PrivateInstance)!;

            object Peer(uint id, int slot, bool spectating)
            {
                object peer = Activator.CreateInstance(peerType, nonPublic: true)!;
                peerType.GetField("ClientId", AnyInstance)!.SetValue(peer, id);
                peerType.GetField("SlotIndex", AnyInstance)!.SetValue(peer, slot);
                peerType.GetField("Spectating", AnyInstance)!.SetValue(peer, spectating);
                peerType.GetField("Name", AnyInstance)!.SetValue(peer, $"Hunter {slot}");
                return peer;
            }

            object State(uint id) => participants[id]!;
            bool Flag(object state, string field) =>
                (bool)state.GetType().GetField(field, AnyInstance)!.GetValue(state)!;
            void Activate(object peer, bool started) => activate.Invoke(server, new[] { peer, (object)started });
            void Role(object peer, bool spectating) => setRole.Invoke(server, new[] { peer, (object)spectating });
            void Check(bool ok, string message)
            {
                _checks++;
                if (!ok) throw new InvalidOperationException(message);
            }

            // Observer connected for the entire match: not an eligible starter
            // and no open time segment from which a loss could be inferred.
            object viewer = Peer(101, 0, spectating: true);
            Activate(viewer, started: true);
            Check(!Flag(State(101), "StartedMatch") && !Flag(State(101), "Active"),
                "opening spectator must never be a career match participant");

            // Joining gameplay after the opening must not retroactively turn
            // spectating time into a start-of-match or ranked participation.
            Role(viewer, spectating: false);
            Check(!Flag(State(101), "StartedMatch") && Flag(State(101), "Active"),
                "spectator rejoin opens a segment but cannot invent match-start eligibility");
            Role(viewer, spectating: true);
            Check(!Flag(State(101), "StartedMatch") && !Flag(State(101), "Active"),
                "returning to spectator closes the gameplay segment");
            Role(viewer, spectating: true);
            Check(!Flag(State(101), "Active"), "duplicate role announcement stays idempotent");

            // Real competitors remain eligible when they temporarily spectate:
            // switching roles must not allow a losing starter to erase the
            // participation they already earned at the beginning.
            object competitor = Peer(202, 1, spectating: false);
            Activate(competitor, started: true);
            Check(Flag(State(202), "StartedMatch") && Flag(State(202), "Active"),
                "starting player counts as an actual participant");
            Role(competitor, spectating: true);
            Check(Flag(State(202), "StartedMatch") && !Flag(State(202), "Active"),
                "spectating pauses play without erasing real starting participation");
            Role(competitor, spectating: false);
            Check(Flag(State(202), "StartedMatch") && Flag(State(202), "Active"),
                "returning player opens a fresh segment");

            // Late spectator remains ineligible, as does a spectator who never
            // switches roles and only receives duplicate match acknowledgments.
            object late = Peer(303, 2, spectating: true);
            Activate(late, started: false);
            Activate(late, started: false);
            Check(!Flag(State(303), "StartedMatch") && !Flag(State(303), "Active"),
                "late spectator cannot create a match or a loss");

            Console.WriteLine($"PASS: {_checks} career spectator/participant role assertions");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }
}
