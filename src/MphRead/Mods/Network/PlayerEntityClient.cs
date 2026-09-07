using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        internal InputCommand CaptureNetworkInput(uint sequence, uint viewServerTick)
        {
            if (Mods.SpectatorMode.IsSpectating)
            {
                return new InputCommand(sequence, sequence, viewServerTick, InputButtons.Spectate,
                    InputButtons.None, _gunVec1, InputCommand.NoWeapon);
            }
            InputButtons held = 0, pressed = 0;
            Capture(Controls.MoveLeft, InputButtons.Left, ref held, ref pressed);
            Capture(Controls.MoveRight, InputButtons.Right, ref held, ref pressed);
            Capture(Controls.MoveUp, InputButtons.Forward, ref held, ref pressed);
            Capture(Controls.MoveDown, InputButtons.Back, ref held, ref pressed);
            Capture(Controls.Shoot, InputButtons.Shoot, ref held, ref pressed);
            Capture(Controls.Zoom, InputButtons.Zoom, ref held, ref pressed);
            Capture(Controls.Jump, InputButtons.Jump, ref held, ref pressed);
            Capture(Controls.Morph, InputButtons.Morph, ref held, ref pressed);
            Capture(Controls.Boost, InputButtons.Boost, ref held, ref pressed);
            Capture(Controls.AltAttack, InputButtons.AltAttack, ref held, ref pressed);
            Capture(Controls.NextWeapon, InputButtons.NextWeapon, ref held, ref pressed);
            Capture(Controls.PrevWeapon, InputButtons.PreviousWeapon, ref held, ref pressed);
            Capture(Controls.RolltLeft, InputButtons.RollLeft, ref held, ref pressed);
            Capture(Controls.RollRight, InputButtons.RollRight, ref held, ref pressed);
            Capture(Controls.RollUp, InputButtons.RollForward, ref held, ref pressed);
            Capture(Controls.RollDown, InputButtons.RollBack, ref held, ref pressed);
            return new InputCommand(sequence, sequence, viewServerTick, held, pressed, _gunVec1,
                (byte)CurrentWeapon);
        }

        private static void Capture(Keybind bind, InputButtons button, ref InputButtons held,
            ref InputButtons pressed)
        {
            if (bind.IsDown) held |= button;
            if (bind.IsPressed) pressed |= button;
        }

        internal void ClientActivate(in SnapshotPlayer state)
        {
            ServerDeactivate();
            Hunter = state.Hunter;
            TeamIndex = state.TeamIndex;
            Team = GameState.Teams ? TeamIndex == 0 ? Team.Orange : Team.Green : Team.None;
            Recolor = GameState.Teams ? TeamIndex == 0 ? 4 : 5 : 0;
            IsBot = false;
            LoadFlags = LoadFlags.SlotActive | LoadFlags.Active | LoadFlags.Initial
                | LoadFlags.Connected | LoadFlags.WasConnected;
            ReloadInit = false;
            Flags1 = 0;
            Flags2 = 0;
            Initialize();
        }

        internal void ApplyServerState(in SnapshotPlayer state, bool newLife, bool predicted = false)
        {
            bool spawned = (state.Flags & SnapshotPlayerFlags.Spawned) != 0;
            if (spawned && (newLife || !ModIsInPlay))
            {
                ModNetSpawn(state.Position, state.Facing);
            }
            else if (!spawned && Health > 0 && state.Health == 0
                && (state.Flags & SnapshotPlayerFlags.Spectating) == 0)
            {
                ModNetDie();
            }
            Health = state.Health;
            for (int weapon = 0; weapon <= 8; weapon++)
            {
                _availableWeapons[(BeamType)weapon] = (state.AvailableWeapons & (1 << weapon)) != 0;
            }
            ModSetWeapon((BeamType)state.Weapon);
            _ammo[UA] = state.AmmoUa;
            _ammo[Missiles] = state.AmmoMissiles;
            EquipInfo.Zoomed = (state.Flags & SnapshotPlayerFlags.Zoomed) != 0;
            bool alt = (state.Flags & SnapshotPlayerFlags.AltForm) != 0;
            if ((!predicted || newLife) && IsAltForm != alt) { ModForceForm(alt); }
            ModSetSpectating((state.Flags & SnapshotPlayerFlags.Spectating) != 0);
            ModSetFrozen((state.Flags & SnapshotPlayerFlags.Frozen) != 0);
            _frozenTimer = state.FrozenTicks;
        }

        internal void ApplySnapshotTransform(in SnapshotPlayer state, bool local = false)
        {
            Vector3 previous = Position;
            Position = state.Position;
            PrevPosition = state.Position;
            Speed = state.Speed;
            ModRefreshNodeRef(previous);
            if (!local)
            {
                _networkInputActive = true;
                _networkAim = state.Aim;
                ModSetAim(state.Aim);
                ModSetFacing(state.Facing);
                SetTransform(_facingVector, _upVector, Position);
            }
        }

        internal void CorrectPredictedPosition(Vector3 position)
        {
            Vector3 previous = Position;
            Position = position;
            PrevPosition += position - previous;
            ModRefreshNodeRef(previous);
        }
    }
}
