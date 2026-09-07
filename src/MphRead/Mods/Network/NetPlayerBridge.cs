using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Presentation adapter for passive protocol-4 demo records.</summary>
    public static class NetPlayerBridge
    {
        private const int FormGraceFrames = 90;
        private static readonly int[] _formMismatch = new int[PlayerEntity.SlotCapacity];

        private static readonly int[] _formAttempts = new int[PlayerEntity.SlotCapacity];

        public static long RejectedUpdates { get; private set; }

        public static long NodeLookupsUnresolved;

        private const float PositionLimit = 100000f;

        // Biped and alt-form position origins differ by their collision-volume centers.
        private static Vector3 InForm(PlayerEntity player, Vector3 position, bool measuredInAlt)
        {
            if (measuredInAlt == player.IsAltForm)
            {
                return position;
            }
            int hunter = (int)player.Hunter;
            if (hunter < 0 || hunter >= 8)
            {
                return position;
            }
            Vector3 delta = PlayerEntity.PlayerVolumes[hunter, 0].SpherePosition
                - PlayerEntity.PlayerVolumes[hunter, 2].SpherePosition;
            return measuredInAlt ? position - delta : position + delta;
        }

        private static bool Sane(Vector3 value)
        {
            return Single.IsFinite(value.X) && Single.IsFinite(value.Y) && Single.IsFinite(value.Z)
                && MathF.Abs(value.X) < PositionLimit && MathF.Abs(value.Y) < PositionLimit
                && MathF.Abs(value.Z) < PositionLimit;
        }

        private const IntentButtons PressedButtons = ~(IntentButtons.ZoomedState
            | IntentButtons.AltFormState | IntentButtons.InPlayState
            | IntentButtons.SpectatingState);

        private static readonly uint[] _lastPressFrame = new uint[PlayerEntity.SlotCapacity];
        private static readonly bool[] _pressSeen = new bool[PlayerEntity.SlotCapacity];

        /// <summary>Apply recorded controls to drive playback animations.</summary>
        public static void ApplyIntent(PlayerEntity player, in IntentPacket intent)
        {
            if (!Sane(intent.Aim))
            {
                RejectedUpdates++;
                NetLog.Event($"slot {player.SlotIndex} intent rejected: aim={intent.Aim}");
                return;
            }
            PlayerControls c = player.Controls;
            IntentButtons missed = MissedPresses(player.SlotIndex, intent);
            Set(c.MoveLeft, intent.Buttons.HasFlag(IntentButtons.MoveLeft), missed.HasFlag(IntentButtons.MoveLeft));
            Set(c.MoveRight, intent.Buttons.HasFlag(IntentButtons.MoveRight), missed.HasFlag(IntentButtons.MoveRight));
            Set(c.MoveUp, intent.Buttons.HasFlag(IntentButtons.MoveUp), missed.HasFlag(IntentButtons.MoveUp));
            Set(c.MoveDown, intent.Buttons.HasFlag(IntentButtons.MoveDown), missed.HasFlag(IntentButtons.MoveDown));
            Set(c.Shoot, intent.Buttons.HasFlag(IntentButtons.Shoot), missed.HasFlag(IntentButtons.Shoot));
            Set(c.Zoom, intent.Buttons.HasFlag(IntentButtons.Zoom), missed.HasFlag(IntentButtons.Zoom));
            Set(c.Jump, intent.Buttons.HasFlag(IntentButtons.Jump), missed.HasFlag(IntentButtons.Jump));
            Set(c.Morph, intent.Buttons.HasFlag(IntentButtons.Morph), missed.HasFlag(IntentButtons.Morph));
            if (c.Morph.IsPressed)
            {
                NetLog.Event($"slot {player.SlotIndex} morph press received, now {player.ModFormState()}");
            }
            Set(c.Boost, intent.Buttons.HasFlag(IntentButtons.Boost), missed.HasFlag(IntentButtons.Boost));
            Set(c.AltAttack, intent.Buttons.HasFlag(IntentButtons.AltAttack), missed.HasFlag(IntentButtons.AltAttack));
            Set(c.NextWeapon, intent.Buttons.HasFlag(IntentButtons.NextWeapon), missed.HasFlag(IntentButtons.NextWeapon));
            Set(c.PrevWeapon, intent.Buttons.HasFlag(IntentButtons.PrevWeapon), missed.HasFlag(IntentButtons.PrevWeapon));
            Set(c.RolltLeft, intent.Buttons.HasFlag(IntentButtons.RollLeft), missed.HasFlag(IntentButtons.RollLeft));
            Set(c.RollRight, intent.Buttons.HasFlag(IntentButtons.RollRight), missed.HasFlag(IntentButtons.RollRight));
            Set(c.RollUp, intent.Buttons.HasFlag(IntentButtons.RollUp), missed.HasFlag(IntentButtons.RollUp));
            Set(c.RollDown, intent.Buttons.HasFlag(IntentButtons.RollDown), missed.HasFlag(IntentButtons.RollDown));
            if (intent.WeaponSelect != 0xFF)
            {
                player.ModSetWeapon((BeamType)intent.WeaponSelect);
            }
            player.ModSetAmmo(intent.AmmoUa, intent.AmmoMissiles);
            // Recorded activity keeps idle and firing animations consistent.
            if ((intent.Buttons & PressedButtons) != IntentButtons.None)
            {
                player.ModNoteInput();
            }
            // After the weapon, because zoom belongs to one and the engine
            // refuses it on a weapon that cannot. Taken as state rather than
            // rebuilt from the press: see IntentButtons.ZoomedState.
            player.ModSetZoom(intent.Buttons.HasFlag(IntentButtons.ZoomedState));
            // Spectator state is presentation data from the recording.
            player.ModSetSpectating(intent.Buttons.HasFlag(IntentButtons.SpectatingState));
        }

        private static IntentButtons MissedPresses(int slot, in IntentPacket intent)
        {
            if (slot < 0 || slot >= _lastPressFrame.Length || intent.Presses == null)
            {
                return IntentButtons.None;
            }
            if (!_pressSeen[slot])
            {
                // First packet from this peer: note where their frame counter
                // stands and replay nothing. The history reaches back several
                // frames, and applying all of it would open with a burst of
                // presses from before this client was listening.
                _pressSeen[slot] = true;
                _lastPressFrame[slot] = intent.Frame;
                return IntentButtons.None;
            }
            IntentButtons missed = IntentButtons.None;
            for (int i = intent.Presses.Length - 1; i >= 0; i--)
            {
                if (intent.Frame < (uint)i)
                {
                    continue;
                }
                uint frame = intent.Frame - (uint)i;
                if (frame <= _lastPressFrame[slot])
                {
                    continue;
                }
                missed |= (IntentButtons)intent.Presses[i];
            }
            // Every frame up to this packet is now accounted for, whether or
            // not it carried a press. Leaving gaps here let the same frame be
            // consumed again by a later packet.
            _lastPressFrame[slot] = Math.Max(_lastPressFrame[slot], intent.Frame);
            return missed;
        }

        private static void Set(Keybind bind, bool down, bool pressed = false)
        {
            bool wasDown = bind.IsDown;
            bind.IsDown = down || pressed;
            bind.IsPressed = pressed;
            bind.IsReleased = !down && wasDown && !pressed;
        }

        /// <summary>Apply recorded state; playback has no locally owned player.</summary>
        public static void ApplyState(Scene scene, PlayerEntity player, in PlayerState state)
        {
            if (!Sane(state.Position) || !Sane(state.Speed) || !Sane(state.Facing))
            {
                RejectedUpdates++;
                NetLog.Event($"slot {player.SlotIndex} snapshot rejected: "
                    + $"pos={state.Position} speed={state.Speed} facing={state.Facing}");
                return;
            }
            bool spawned = (state.Flags & PlayerState.FlagSpawned) != 0;
            bool wasInPlay = player.LoadFlags.TestFlag(LoadFlags.Spawned) && player.Health > 0;
            int slot = player.SlotIndex;
            if (slot >= 0 && slot < scene.Match.Players.Count && !NetRoomChange.Settling)
            {
                scene.Match.Players[slot].Points = state.Points;
                scene.Match.Players[slot].Kills = state.Kills;
                scene.Match.Players[slot].Deaths = state.Deaths;
            }
            // Before health is reconciled, because the engine's damage
            // feedback is produced by the hit rather than by the number: a
            // client that only assigned the new health showed a bar dropping
            // in silence, with no indicator, no animation and no kill banner.
            NetDamage.Replay(scene, player, state);
            if (!spawned)
            {
                // Waiting to be placed, or just killed. Health is the whole
                // point of this branch: it is how a client learns that it
                // died, and skipping it left a player who had been killed on
                // every other screen still walking around on its own.
                if (wasInPlay && state.Health == 0 && player.Health > 0)
                {
                    // Killed by something that leaves no damage record: a
                    // fall, a kill plane, the match ending them. Assigning
                    // zero health would look right and count nothing, so the
                    // scoreboards drifted apart by exactly those deaths.
                    player.ModNetDie();
                }
                player.Health = state.Health;
                return;
            }
            if (!wasInPlay)
            {
                // Spawn initializes model visibility as well as position.
                player.ModNetSpawn(state.Position, state.Facing);
            }

            // Recorded and displayed forms may be at different animation frames.
            Move(player, InForm(player, state.Position,
                (state.Flags & PlayerState.FlagAltForm) != 0));
            player.Speed = state.Speed;
            player.Health = state.Health;
            player.ModSetFacing(state.Facing);
            player.ModSetWeapon((BeamType)state.CurrentWeapon);
            player.EquipInfo.Zoomed = (state.Flags & PlayerState.FlagZoomed) != 0;
            ApplyForm(player, (state.Flags & PlayerState.FlagAltForm) != 0);
            // Hidden and non-solid on this machine too, not just the one
            // whose input is frozen -- Quake 3's spectator, not a player who
            // merely stopped moving.
            player.ModSetSpectating((state.Flags & PlayerState.FlagSpectating) != 0);
            player.ModSetFrozen((state.Flags & PlayerState.FlagFrozen) != 0);
        }

        private static void ApplyForm(PlayerEntity player, bool altForm)
        {
            int slot = player.SlotIndex;
            if (slot < 0 || slot >= _formMismatch.Length)
            {
                return;
            }
            if (player.IsAltForm == altForm)
            {
                _formMismatch[slot] = 0;
                _formAttempts[slot] = 0;
                return;
            }
            _formMismatch[slot]++;
            if (_formMismatch[slot] <= FormGraceFrames)
            {
                return;
            }
            _formMismatch[slot] = 0;
            // First the real transition, because that is what creates the
            // parts of a form that are separate entities -- Weavel's
            // halfturret exists only because EnterAltForm adds it, so a
            // client that skipped straight to the flag showed a Weavel in alt
            // form with no turret. Only if that does not take does the flag
            // get forced.
            if (_formAttempts[slot] == 0)
            {
                _formAttempts[slot] = 1;
                player.ModStartFormSwitch();
                return;
            }
            _formAttempts[slot] = 0;
            player.ModForceForm(altForm);
        }

        public static void NoteRoomChanged()
        {
            Array.Clear(_formMismatch);
            Array.Clear(_formAttempts);
            Array.Clear(_lastPressFrame);
            Array.Clear(_pressSeen);
        }

        public static void Reset()
        {
            NoteRoomChanged();
            RejectedUpdates = 0;
            NodeLookupsUnresolved = 0;
        }

        public static void ForgetSlot(int slot)
        {
            if (slot < 0 || slot >= PlayerEntity.SlotCapacity)
            {
                return;
            }
            _formMismatch[slot] = 0;
            _formAttempts[slot] = 0;
            _lastPressFrame[slot] = 0;
            _pressSeen[slot] = false;
        }

        private static void Move(PlayerEntity player, Vector3 position)
        {
            Vector3 previous = player.Position;
            player.Position = position;
            // This runs after PlayerProcess has captured PrevPosition. Keep
            // the next collision sweep anchored to the corrected position;
            // otherwise the engine treats the network correction as player
            // movement and can push the puppet away from the hitbox.
            player.PrevPosition = position;
            player.ModRefreshNodeRef(previous);
        }
    }
}
