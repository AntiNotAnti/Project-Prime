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
        private bool _hasAuthoritativeWeaponPickupFence;
        private uint _authoritativeWeaponPickupTick;

        internal void ServerActivate(ulong connectionId, Hunter hunter, int team, int? botSkill = null)
        {
            if (!_scene.IsHeadless) { throw new InvalidOperationException("Server player requires a headless scene."); }
            // A countdown reset respawns the same connection. Keep Life monotonic
            // so its next snapshot resets client prediction instead of reusing life 1.
            if (_serverConnectionId != connectionId) { _serverLife = 0; }
            _serverConnectionId = connectionId;
            _serverWasAlive = false;
            if (Hunter != hunter) { AdvancePresentationPoseEpoch(); }
            Hunter = hunter;
            TeamIndex = team;
            Team = _scene.Match.Rules.Teams ? team == 0 ? Team.Orange : Team.Green : Team.None;
            Recolor = _scene.Match.Rules.Teams ? team == 0 ? 4 : 5 : 0;
            IsBot = botSkill.HasValue;
            if (botSkill.HasValue)
            {
                Difficulty = (BotDifficulty)Math.Clamp(botSkill.Value,
                    (int)BotDifficulty.Beginner, (int)BotDifficulty.Expert);
                BotLevel = Difficulty.LegacyLevel();
                MphRead.Formats.AiPersonality.Load(this,
                    _scene.Match.Rules.Mode.ToLegacyMode());
                AiData.InitializeAtLoad();
            }
            LoadFlags = LoadFlags.SlotActive | LoadFlags.Active | LoadFlags.Initial
                | LoadFlags.Connected | LoadFlags.WasConnected;
            Controls.ClearAll();
            Input.ClearBoostIntents();
            _availableWeapons.ClearAll();
            _availableCharges.ClearAll();
            ReloadInit = false;
            Flags1 = 0;
            Flags2 = 0;
            Initialize();
            _networkInputActive = !IsBot;
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
            ClearClientCombatIdentity();
            ClearPowerupPresentationState();
            _aimAssist.Reset();
            ResetLockjawBombState();
            AdvancePresentationPoseEpoch();
            IsBot = false;
            Controls.ClearAll();
            Input.ClearBoostIntents();
            _pendingAutoEquipWeapon = BeamType.None;
            _hasAuthoritativeWeaponPickupFence = false;
            ClearAnalogMovement();
            ResetRemoteLocomotion();
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
                if (Flags2.TestFlag(PlayerFlags2.Spectating))
                {
                    ClearAnalogMovement();
                    return;
                }
            }
            _networkInputActive = true;
            _networkAim = command.Aim;
            BoostIntent boostIntent = command.BoostRequest;
            Input.QueueBoostIntent(boostIntent);
            Input.HasInput = command.Buttons != InputButtons.None
                || command.Pressed != InputButtons.None
                || boostIntent.Activation != BoostActivation.None
                || command.AnalogMovementPresent && command.AnalogMovement != Vector2.Zero;
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
            // The client resolves cycle input against its authoritative
            // availability snapshot and sends the exact result in
            // DesiredWeapon. Applying that result and the original cycle
            // edge would switch twice (for example Power -> Missile ->
            // Power), which looks like the weapon is repeatedly swapping.
            // Keep the raw edge in the InputCommand journal, but let the
            // explicit target be the simulation's single weapon action.
            InputButtons nextWeaponButton = command.DesiredWeapon <= 8
                ? InputButtons.None : InputButtons.NextWeapon;
            InputButtons previousWeaponButton = command.DesiredWeapon <= 8
                ? InputButtons.None : InputButtons.PreviousWeapon;
            SetNetworkBind(c.NextWeapon, nextWeaponButton, command);
            SetNetworkBind(c.PrevWeapon, previousWeaponButton, command);
            SetNetworkBind(c.RolltLeft, InputButtons.RollLeft, command);
            SetNetworkBind(c.RollRight, InputButtons.RollRight, command);
            SetNetworkBind(c.RollUp, InputButtons.RollForward, command);
            SetNetworkBind(c.RollDown, InputButtons.RollBack, command);
            if (command.AnalogMovementPresent)
            {
                InputButtons movementButtons = (command.Buttons | command.Pressed)
                    & (InputButtons.Left | InputButtons.Right
                        | InputButtons.Forward | InputButtons.Back);
                InputButtons rollButtons = (command.Buttons | command.Pressed)
                    & (InputButtons.RollLeft | InputButtons.RollRight
                        | InputButtons.RollForward | InputButtons.RollBack);
                SetAnalogMovement(command.AnalogMovement,
                    new Vector2(DigitalAxis(movementButtons, InputButtons.Right,
                        InputButtons.Left),
                        DigitalAxis(movementButtons, InputButtons.Forward,
                            InputButtons.Back)),
                    new Vector2(DigitalAxis(rollButtons, InputButtons.RollRight,
                        InputButtons.RollLeft),
                        DigitalAxis(rollButtons, InputButtons.RollForward,
                            InputButtons.RollBack)),
                    movementButtons, rollButtons,
                    command.Pressed & movementButtons,
                    command.Pressed & rollButtons);
            }
            else
            {
                ClearAnalogMovement();
            }
            if (command.DesiredWeapon <= 8
                && IsNetworkWeaponIntentCurrent(command.ViewServerTick)
                && (BeamType)command.DesiredWeapon != CurrentWeapon)
            {
                // Normal availability, cooldown and transition checks apply.
                // Unlike the old owner state path, this never grants a weapon
                // or copies a client ammunition count.
                TryEquipWeapon((BeamType)command.DesiredWeapon);
            }
        }

        private bool IsNetworkWeaponIntentCurrent(uint viewServerTick)
            => !_hasAuthoritativeWeaponPickupFence
                || viewServerTick == _authoritativeWeaponPickupTick
                || Sequence32.IsNewer(viewServerTick, _authoritativeWeaponPickupTick);

        private void MarkAuthoritativeWeaponPickup()
        {
            if (_scene.Services.Combat is not ICombatAuthority authority)
            {
                return;
            }
            _hasAuthoritativeWeaponPickupFence = true;
            _authoritativeWeaponPickupTick = authority.Tick;
        }

        private static void SetNetworkBind(PlayerActionState bind, InputButtons button, in InputCommand command)
        {
            bool pressed = (command.Pressed & button) != 0;
            bool down = (command.Buttons & button) != 0 || pressed;
            bind.IsReleased = bind.IsDown && !down;
            bind.IsDown = down;
            bind.IsPressed = pressed;
        }

        private static float DigitalAxis(InputButtons buttons, InputButtons positive,
            InputButtons negative)
            => (buttons & positive) != 0 ? 1
                : (buttons & negative) != 0 ? -1 : 0;

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
            if (Flags2.TestFlag(PlayerFlags2.RadarReveal)) flags |= SnapshotPlayerFlags.RadarReveal;
            if (Flags2.TestFlag(PlayerFlags2.RadarRevealPrevious)) flags |= SnapshotPlayerFlags.RadarRevealPrevious;
            if (EquipInfo.Zoomed) flags |= SnapshotPlayerFlags.Zoomed;
            if (Flags2.TestFlag(PlayerFlags2.Spectating)) flags |= SnapshotPlayerFlags.Spectating;
            if (Flags1.TestFlag(PlayerFlags1.Grounded)) flags |= SnapshotPlayerFlags.Grounded;
            if (Flags2.TestFlag(PlayerFlags2.Cloaking) && _cloakTimer > 0) flags |= SnapshotPlayerFlags.Cloaking;
            if (ShouldCaptureReplicatedAltAttack(Hunter, alive, IsAltForm,
                Flags2.TestFlag(PlayerFlags2.AltAttack)))
            {
                flags |= SnapshotPlayerFlags.SpireAltAttack;
            }
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
                BurnTicks = _burnTimer, DisruptTicks = _disruptedTimer, Assists = _scene.Match.Players[SlotIndex].Assists,
                ChargeLevel = EquipInfo.ChargeLevel,
                DoubleDamageTicks = _doubleDmgTimer, CloakTicks = _cloakTimer,
                DeathaltTicks = _deathaltTimer,
                Points = _scene.Match.Players[SlotIndex].Points, Kills = _scene.Match.Players[SlotIndex].Kills,
                Deaths = _scene.Match.Players[SlotIndex].Deaths
            };
        }

        internal static bool ShouldCaptureReplicatedAltAttack(Hunter hunter,
            bool alive, bool altForm, bool altAttack)
            => alive && altForm && altAttack
                && SupportsReplicatedAltAttack(hunter);
    }
}
