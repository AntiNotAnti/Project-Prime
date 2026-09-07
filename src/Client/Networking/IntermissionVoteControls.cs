using System;
using MphRead.Entities;
using MphRead.Hud;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Network
{
    /// <summary>Presentation-only ballot selection. The server validates every vote.</summary>
    public static class IntermissionVoteControls
    {
        private readonly record struct Tap(NetClient Session, uint Match, uint Phase, byte Option);
        private static readonly object Gate = new();
        private static Tap? _tap;
        private static int _held, _selected;
        private static uint _match, _phase;
        private static NetClient? _session;
        private static NetClient? Client => AuthoritativePlay.Current?.Client;
        private static IntermissionBallot? Current => Client is { IsObserver: false } client
            && client.Ballot is { } ballot && ballot.MatchId == client.Accepted.MatchId ? ballot : null;
        public static bool Available => Current != null;
        internal static bool IsClosed(IntermissionBallot ballot, uint tick) => ballot.HasDeadline
            && (tick == ballot.DeadlineTick || Sequence32.IsNewer(tick, ballot.DeadlineTick));
        private static bool Closed(IntermissionBallot ballot) => IsClosed(ballot, Client?.Snapshot.ServerTick ?? 0);

        public static void Move(int direction)
        {
            if (Current is not { } ballot || Closed(ballot)) return;
            Observe(ballot);
            _selected = (_selected + Math.Sign(direction) + ballot.Options.Length) % ballot.Options.Length;
        }
        public static bool Submit()
        {
            if (Current is not { } ballot || Closed(ballot) || ClientInputState.PauseOpen || Chat.ChatBox.Composing) return false;
            Observe(ballot);
            return Client!.Vote(ballot.Options[_selected].Id);
        }
        private static void Observe(IntermissionBallot ballot)
        {
            if (!ReferenceEquals(_session, Client) || _match != ballot.MatchId || _phase != ballot.PhaseRevision)
            { _session = Client; _match = ballot.MatchId; _phase = ballot.PhaseRevision; _selected = 0; }
            _selected = Math.Clamp(_selected, 0, ballot.Options.Length - 1);
        }
        public static bool QueuePointerDown(float x, float y)
        {
            if (Current is not { } ballot || Closed(ballot) || ClientInputState.PauseOpen || Chat.ChatBox.Composing
                || !float.IsFinite(x) || !float.IsFinite(y) || x < 8 || x > 248 || y < 50) return false;
            int option = (int)((y - 50) / 12);
            if ((uint)option >= ballot.Options.Length) return false;
            lock (Gate) _tap = new(Client!, ballot.MatchId, ballot.PhaseRevision, ballot.Options[option].Id);
            return true;
        }

        /// <summary>Consume a touchscreen ballot tap on the game thread.</summary>
        public static bool ConsumePointerVote()
        {
            if (Current is not { } ballot || Closed(ballot) || ClientInputState.PauseOpen || Chat.ChatBox.Composing)
            {
                lock (Gate) _tap = null;
                return false;
            }
            Tap? tap;
            lock (Gate) { tap = _tap; _tap = null; }
            if (tap is { } value && ReferenceEquals(value.Session, Client) && value.Match == ballot.MatchId && value.Phase == ballot.PhaseRevision)
                return Client!.Vote(value.Option);
            return false;
        }

        public static void Poll(KeyboardState keyboard, MouseState mouse, Vector2i size)
        {
            int held = mouse.IsButtonDown(MouseButton.Left) ? 256 : 0;
            for (int i = 0; i < 8; i++) if (keyboard.IsKeyDown((Keys)((int)Keys.D1 + i))) held |= 1 << i;
            int pressed = held & ~_held; _held = held;
            if (Current is not { } ballot) { lock (Gate) _tap = null; return; }
            Observe(ballot);
            if (Closed(ballot) || ClientInputState.PauseOpen || Chat.ChatBox.Composing) { lock (Gate) _tap = null; return; }
            if ((pressed & 256) != 0 && size.X > 0 && size.Y > 0)
                QueuePointerDown(mouse.Position.X * 256 / size.X, mouse.Position.Y * 192 / size.Y);
            ConsumePointerVote();
            for (int i = 0; i < ballot.Options.Length; i++)
                if ((pressed & (1 << i)) != 0) { _selected = i; Submit(); break; }
        }
        internal static bool Draw(PlayerPresentation player)
        {
            if (Current is not { } ballot) return false;
            Observe(ballot);
            bool closed = Closed(ballot);
            player.DrawText2D(128, 34, Align.Center, 0,
                ballot.Phase == MatchPhase.WaitingForPlayers ? "LOBBY - CHOOSE NEXT MATCH" : "NEXT MATCH VOTE", scale: .7f);
            for (int i = 0; i < ballot.Options.Length; i++)
            {
                var option = ballot.Options[i];
                string marker = option.Id == ballot.SelectedId ? "*" : !closed && i == _selected ? ">" : " ";
                player.DrawText2D(8, 50 + i * 12, Align.Left, 0,
                    $"{marker} {i + 1}. {option.Label} ({option.Votes})", maxLength: 50, scale: .6f);
            }
            player.DrawText2D(128, 159, Align.Center, 0,
                closed ? "Vote closed" : ballot.SelectedId == 0 ? "No vote confirmed" : "* Vote confirmed and locked", scale: .6f);
            int remaining = Math.Max(0, unchecked((int)(ballot.DeadlineTick - (Client?.Snapshot.ServerTick ?? 0))));
            string time = closed ? "Final vote counts" : ballot.HasDeadline ? $"{(remaining + 59L) / 60}s remaining" : "Waiting for a match choice";
            player.DrawText2D(128, 169, Align.Center, 0, time, scale: .55f);
            player.DrawText2D(128, 181, Align.Center, 0, closed ? "Waiting for the next match" : "1-8 / tap: vote | cycle + fire: select", scale: .5f);
            return true;
        }
    }
}
