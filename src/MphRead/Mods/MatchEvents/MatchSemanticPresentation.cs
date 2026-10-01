using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Network;
namespace MphRead.Mods.MatchEvents;
/// <summary>Scene-owned presentation only; never changes health, scoring or award decisions.</summary>
internal sealed class MatchSemanticPresentation
{
    private readonly MatchSemanticReceiver _local = new();
    private ushort _match;
    private ulong _epoch;
    private uint _event, _award, _lastAwardSource;
    private bool _replicaInitialized;
    private readonly Dictionary<uint, List<string>> _medals = new();
    internal static bool Enabled(Scene scene) => scene.GameState.Multiplayer && !scene.Services.IsReplica && !NetSession.SemanticLegacyPlayback;
    internal static PlayerEntity? Resolve(Scene scene, MatchSemanticActor actor)
    {
        if (!actor.IsPlayer) return null;
        foreach (var player in scene.GetPlayerEntities())
            if (player.SlotIndex == actor.Slot && (scene.Services is ReplaySceneServices replay
                ? replay.State.Occupant(actor.Slot).Generation == actor.SlotGeneration
                : scene.MatchEvents.SameOccupant(player, actor))) return player;
        return null;
    }
    internal void Update(Scene scene)
    {
        if (!Enabled(scene) || !scene.GameState.Multiplayer) return;
        var receiver = NetSession.Active && !NetSession.IsAuthority ? NetSession.SemanticReceived : _local;
        if (receiver == _local)
        {
            var bus = scene.MatchEvents.Bus;
            if (bus.MatchId == 0) return;
            if (_match != bus.MatchId || _epoch != bus.AuthorityEpoch)
            { _match = bus.MatchId; _epoch = bus.AuthorityEpoch; _event = _award = _lastAwardSource = 0; _local.Begin(_match, _epoch); }
            foreach (var fact in bus.Events)
            {
                if (fact.EventId <= _event) continue;
                _event = fact.EventId; _local.Accept(MatchSemanticEventPacket.From(fact));
                foreach (var award in bus.Awards.Awards)
                    if (award.EventId == fact.EventId)
                        _local.Accept(new MatchAwardPacket(_match, _epoch, ++_award, award.EventId, award.Tick,
                            award.Actor.Slot, award.Actor.SlotGeneration, award.Actor.Life, award.Kind));
            }
        }
        _medals.Clear();
        receiver.DrainPresentation(fact =>
        {
            if (fact.Type is MatchSemanticEventType.PlayerKilled or MatchSemanticEventType.PlayerSuicide)
                scene.KillFeed.RecordCanonical(scene, fact.ToFact());
            if (fact.Type == MatchSemanticEventType.Headshot
                && Resolve(scene, fact.ToFact().Actor) is { } attacker && attacker == scene.Players.Main)
                Sound.CombatFeedbackAudio.OnConfirmedHeadshot(scene, (BeamType)fact.Weapon, canonical: true);
        }, award =>
        {
            var player = Resolve(scene, new(award.ActorSlot, award.ActorGeneration, award.ActorLife));
            if (player == null) return;
            if (award.Kind == MatchAwardKind.KillingSpree)
                foreach (var source in receiver.Events)
                    if (source.EventId == award.SourceEventId)
                    { Resolve(scene, source.ToFact().Target)?.PresentCanonicalSpree(player); break; }
            if (player != scene.Players.Main) return;
            string label = Sound.CombatFeedbackAudio.OnCanonicalAward(scene, award.Kind, _lastAwardSource != award.SourceEventId);
            _lastAwardSource = award.SourceEventId;
            if (!_medals.TryGetValue(award.SourceEventId, out var labels)) _medals[award.SourceEventId] = labels = new();
            labels.Add(label.ToUpperInvariant());
        });
        if (Launcher.LauncherPrefs.CombatNotificationsVisible && !Headless.Active && !ThumbnailMode.Active)
            foreach (var labels in _medals.Values) scene.Players.Main?.QueueCombatNotifications(labels);
        if (receiver.PresentationOverrun && NetSession.Active && !NetSession.IsAuthority)
        {
            NetLog.Event("Canonical presentation history overrun; disconnecting rather than skipping facts.");
            NetSession.Stop();
        }
    }
    internal void UpdateReplay(Scene scene, MatchSemanticReceiver receiver, bool seeking)
    {
        if (!_replicaInitialized || seeking)
        {
            receiver.SuppressHistory(); scene.KillFeed.Clear(); _replicaInitialized = true;
            return;
        }
        // Replay presentation is visual-only. Audio and live HUD callbacks remain isolated.
        receiver.DrainPresentation(fact =>
        {
            if (fact.Type is MatchSemanticEventType.PlayerKilled or MatchSemanticEventType.PlayerSuicide)
                scene.KillFeed.RecordCanonical(scene, fact.ToFact());
        }, _ => { });
    }

}
