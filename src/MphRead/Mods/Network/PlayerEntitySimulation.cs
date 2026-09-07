using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        private bool _networkInputActive;
        private Vector3 _networkAim = -Vector3.UnitZ;
        private ulong _serverConnectionId;
        private uint _serverLife;
        private bool _serverWasAlive;

        internal void ServerActivate(ulong connectionId, Hunter hunter, int team)
        {
            if (!_scene.IsHeadless) { throw new InvalidOperationException("Server player requires a headless scene."); }
            _serverConnectionId = connectionId;
            _serverLife = 0;
            _serverWasAlive = false;
            Hunter = hunter;
            TeamIndex = team;
            Team = GameState.Teams ? team == 0 ? Team.Orange : Team.Green : Team.None;
            Recolor = GameState.Teams ? team == 0 ? 4 : 5 : 0;
            IsBot = false;
            LoadFlags = LoadFlags.SlotActive | LoadFlags.Active | LoadFlags.Initial
                | LoadFlags.Connected | LoadFlags.WasConnected;
            Controls.ClearAll();
            _availableWeapons.ClearAll();
            _availableCharges.ClearAll();
            ReloadInit = false;
            Flags1 = 0;
            Flags2 = 0;
            Initialize();
            _networkInputActive = true;
            Controls.MouseAim = false;
            Controls.KeyboardAim = false;
            PlayerSpawnEntity? spawn = GetRespawnPoint();
            if (spawn == null)
            {
                throw new ProgramException("Server room has no usable spawn point.");
            }
            Spawn(spawn.Position, spawn.FacingVector, spawn.UpVector, spawn.NodeRef, respawn: true);
        }

        internal void ServerDeactivate()
        {
            Controls.ClearAll();
            _networkInputActive = false;
            Health = 0;
            Halfturret.Health = 0;
            Speed = Vector3.Zero;
            LoadFlags = LoadFlags.SlotActive;
            Flags2 |= PlayerFlags2.HideModel;
        }

        internal void ApplyNetworkInput(in InputCommand command)
        {
            if (_scene.IsHeadless)
            {
                ServerSetSpectating((command.Buttons & InputButtons.Spectate) != 0);
                if (Flags2.TestFlag(PlayerFlags2.Spectating)) { return; }
            }
            _networkInputActive = true;
            _networkAim = command.Aim;
            Input.HasInput = command.Buttons != InputButtons.None || command.Pressed != InputButtons.None;
            PlayerControls c = Controls;
            SetNetworkBind(c.MoveLeft, InputButtons.Left, command);
            SetNetworkBind(c.MoveRight, InputButtons.Right, command);
            SetNetworkBind(c.MoveUp, InputButtons.Forward, command);
            SetNetworkBind(c.MoveDown, InputButtons.Back, command);
            SetNetworkBind(c.Shoot, InputButtons.Shoot, command);
            SetNetworkBind(c.Zoom, InputButtons.Zoom, command);
            SetNetworkBind(c.Jump, InputButtons.Jump, command);
            SetNetworkBind(c.Morph, InputButtons.Morph, command);
            SetNetworkBind(c.Boost, InputButtons.Boost, command);
            SetNetworkBind(c.AltAttack, InputButtons.AltAttack, command);
            SetNetworkBind(c.NextWeapon, InputButtons.NextWeapon, command);
            SetNetworkBind(c.PrevWeapon, InputButtons.PreviousWeapon, command);
            SetNetworkBind(c.RolltLeft, InputButtons.RollLeft, command);
            SetNetworkBind(c.RollRight, InputButtons.RollRight, command);
            SetNetworkBind(c.RollUp, InputButtons.RollForward, command);
            SetNetworkBind(c.RollDown, InputButtons.RollBack, command);
            if (command.DesiredWeapon <= 8)
            {
                // Normal availability, cooldown and transition checks apply.
                // Unlike the old owner state path, this never grants a weapon
                // or copies a client ammunition count.
                TryEquipWeapon((BeamType)command.DesiredWeapon);
            }
        }

        private static void SetNetworkBind(Keybind bind, InputButtons button, in InputCommand command)
        {
            bool pressed = (command.Pressed & button) != 0;
            bool down = (command.Buttons & button) != 0 || pressed;
            bind.IsReleased = bind.IsDown && !down;
            bind.IsDown = down;
            bind.IsPressed = pressed;
        }

        internal SnapshotPlayer CaptureServerState()
        {
            bool alive = Health > 0 && LoadFlags.TestFlag(LoadFlags.Spawned);
            if (alive && !_serverWasAlive) { _serverLife++; }
            _serverWasAlive = alive;
            SnapshotPlayerFlags flags = SnapshotPlayerFlags.Active;
            if (alive) flags |= SnapshotPlayerFlags.Spawned;
            if (IsAltForm) flags |= SnapshotPlayerFlags.AltForm;
            if (IsMorphing) flags |= SnapshotPlayerFlags.Morphing;
            if (IsUnmorphing) flags |= SnapshotPlayerFlags.Unmorphing;
            if (ModFrozen) flags |= SnapshotPlayerFlags.Frozen;
            if (ModBurning) flags |= SnapshotPlayerFlags.Burning;
            if (ModDisrupted) flags |= SnapshotPlayerFlags.Disrupted;
            if (EquipInfo.Zoomed) flags |= SnapshotPlayerFlags.Zoomed;
            if (Flags2.TestFlag(PlayerFlags2.Spectating)) flags |= SnapshotPlayerFlags.Spectating;
            if (Flags1.TestFlag(PlayerFlags1.Grounded)) flags |= SnapshotPlayerFlags.Grounded;
            ushort available = 0;
            for (int weapon = 0; weapon <= 8; weapon++)
            {
                if (_availableWeapons[(BeamType)weapon]) { available |= (ushort)(1 << weapon); }
            }
            return new SnapshotPlayer
            {
                Slot = (byte)SlotIndex, Hunter = Hunter, TeamIndex = (byte)TeamIndex,
                Weapon = (byte)CurrentWeapon, Flags = flags, Life = _serverLife,
                ConnectionId = _serverConnectionId, Position = Position, Speed = Speed,
                Aim = _gunVec1, Facing = _facingVector,
                Health = (ushort)Math.Clamp(Health, 0, UInt16.MaxValue),
                AmmoUa = (ushort)Math.Clamp(_ammo[UA], 0, UInt16.MaxValue),
                AmmoMissiles = (ushort)Math.Clamp(_ammo[Missiles], 0, UInt16.MaxValue),
                AvailableWeapons = available, FrozenTicks = _frozenTimer,
                Points = GameState.Points[SlotIndex], Kills = GameState.Kills[SlotIndex],
                Deaths = GameState.Deaths[SlotIndex]
            };
        }
    }
}
