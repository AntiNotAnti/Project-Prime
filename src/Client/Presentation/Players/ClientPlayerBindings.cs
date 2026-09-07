using System;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Entities
{
    public enum ButtonType
    {
        Key,
        Mouse,
        ScrollUp,
        ScrollDown
    }

    public class Keybind
    {
        internal PlayerActionState State { get; set; } = new PlayerActionState();
        public ButtonType Type { get; set; }
        public Keys Key { get; set; }
        public MouseButton MouseButton { get; set; }
        public bool IsPressed { get => State.IsPressed; set => State.IsPressed = value; }
        public bool IsDown { get => State.IsDown; set => State.IsDown = value; }
        public bool IsReleased { get => State.IsReleased; set => State.IsReleased = value; }
        public bool NeedsRepress { get => State.NeedsRepress; set => State.NeedsRepress = value; }

        public Keybind(Keys key)
        {
            Type = ButtonType.Key;
            Key = key;
        }

        public Keybind(MouseButton mouseButton)
        {
            Type = ButtonType.Mouse;
            MouseButton = mouseButton;
        }

        public Keybind(ButtonType scrollType)
        {
            if (scrollType != ButtonType.ScrollUp && scrollType != ButtonType.ScrollDown)
            {
                throw new ProgramException("Unexpected control type.");
            }

            Type = scrollType;
        }

        public static bool operator ==(Keybind lhs, Keybind rhs)
        {
            return lhs.Type == rhs.Type && lhs.Key == rhs.Key && lhs.MouseButton == rhs.MouseButton;
        }

        public static bool operator !=(Keybind lhs, Keybind rhs)
        {
            return lhs.Type != rhs.Type || lhs.Key != rhs.Key || lhs.MouseButton != rhs.MouseButton;
        }

        public override bool Equals(object? obj)
        {
            return obj is Keybind other && Type == other.Type && Key == other.Key && MouseButton == other.MouseButton;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Type, Key, MouseButton);
        }
    }

    public class ClientPlayerBindings
    {
        private PlayerControls _controls = new PlayerControls();
        internal void Attach(PlayerControls controls)
        {
            controls.MouseAim = MouseAim;
            controls.KeyboardAim = KeyboardAim;
            controls.ScrollAllWeapons = ScrollAllWeapons;
            _controls = controls;
            MoveLeft.State = controls.MoveLeft;
            MoveRight.State = controls.MoveRight;
            MoveUp.State = controls.MoveUp;
            MoveDown.State = controls.MoveDown;
            RolltLeft.State = controls.RolltLeft;
            RollRight.State = controls.RollRight;
            RollUp.State = controls.RollUp;
            RollDown.State = controls.RollDown;
            AimLeft.State = controls.AimLeft;
            AimRight.State = controls.AimRight;
            AimUp.State = controls.AimUp;
            AimDown.State = controls.AimDown;
            Shoot.State = controls.Shoot;
            Zoom.State = controls.Zoom;
            Jump.State = controls.Jump;
            Morph.State = controls.Morph;
            Boost.State = controls.Boost;
            AltAttack.State = controls.AltAttack;
            NextWeapon.State = controls.NextWeapon;
            PrevWeapon.State = controls.PrevWeapon;
            WeaponMenu.State = controls.WeaponMenu;
            PowerBeam.State = controls.PowerBeam;
            Missile.State = controls.Missile;
            VoltDriver.State = controls.VoltDriver;
            Battlehammer.State = controls.Battlehammer;
            Imperialist.State = controls.Imperialist;
            Judicator.State = controls.Judicator;
            Magmaul.State = controls.Magmaul;
            ShockCoil.State = controls.ShockCoil;
            OmegaCannon.State = controls.OmegaCannon;
            AffinitySlot.State = controls.AffinitySlot;
            Pause.State = controls.Pause;
            HudOverlay.State = controls.HudOverlay;
        }

        public bool MouseAim { get => _controls.MouseAim; set => _controls.MouseAim = value; }
        public bool KeyboardAim { get => _controls.KeyboardAim; set => _controls.KeyboardAim = value; }
        public Keybind MoveLeft { get; }
        public Keybind MoveRight { get; }
        public Keybind MoveUp { get; }
        public Keybind MoveDown { get; }
        public Keybind RolltLeft { get; }
        public Keybind RollRight { get; }
        public Keybind RollUp { get; }
        public Keybind RollDown { get; }
        public Keybind AimLeft { get; }
        public Keybind AimRight { get; }
        public Keybind AimUp { get; }
        public Keybind AimDown { get; }
        public Keybind Shoot { get; }
        public Keybind Zoom { get; }
        public Keybind Jump { get; }
        public Keybind Morph { get; }
        public Keybind Boost { get; }
        public Keybind AltAttack { get; }
        public Keybind NextWeapon { get; }
        public Keybind PrevWeapon { get; }
        public Keybind WeaponMenu { get; }
        public Keybind RecapHistory { get; } = new(Keys.F6);
        public Keybind QuickSwap { get; } = new(Keys.Q);
        public Keybind PowerBeam { get; }
        public Keybind Missile { get; }
        public Keybind VoltDriver { get; }
        public Keybind Battlehammer { get; }
        public Keybind Imperialist { get; }
        public Keybind Judicator { get; }
        public Keybind Magmaul { get; }
        public Keybind ShockCoil { get; }
        public Keybind OmegaCannon { get; }
        public Keybind AffinitySlot { get; }
        public Keybind Pause { get; }
        public Keybind HudOverlay { get; }
        public bool InvertAimY { get; }
        public bool InvertAimX { get; }
        /// <summary>
        /// Whether the wheel walks the whole list, Power Beam and Missile
        /// included, or only the seven affinity slots.
        ///
        /// Settable, and <see cref = "Mods.InputSettings.ScrollAllWeapons"/> is
        /// where the player's answer lives. It was a readonly false, and the
        /// weapon-cycling block above only runs at all when it is true *or*
        /// the equipped weapon is neither the Power Beam nor the Missile -- so
        /// with the wheel's own defaults bound, scrolling did nothing
        /// whatsoever while holding the beam every player spawns with.
        /// </summary>
        public bool ScrollAllWeapons { get => _controls.ScrollAllWeapons; set => _controls.ScrollAllWeapons = value; }
        public Keybind[] All { get; }

        public ClientPlayerBindings(Keybind moveLeft, Keybind moveRight, Keybind moveUp, Keybind moveDown, Keybind rollLeft, Keybind rollRight, Keybind rollUp, Keybind rollDown, Keybind aimLeft, Keybind aimRight, Keybind aimUp, Keybind aimDown, Keybind shoot, Keybind zoom, Keybind jump, Keybind morph, Keybind boost, Keybind altAttack, Keybind nextWeapon, Keybind prevWeapon, Keybind weaponMenu, Keybind powerBeam, Keybind missile, Keybind voltDriver, Keybind battlehammer, Keybind imperialist, Keybind judicator, Keybind magmaul, Keybind shockCoil, Keybind omegaCannon, Keybind affinitySlot, Keybind pause, Keybind hudOverlay)
        {
            MouseAim = true;
            KeyboardAim = true;
            ScrollAllWeapons = true;
            MoveLeft = moveLeft;
            MoveRight = moveRight;
            MoveUp = moveUp;
            MoveDown = moveDown;
            RolltLeft = rollLeft;
            RollRight = rollRight;
            RollUp = rollUp;
            RollDown = rollDown;
            AimLeft = aimLeft;
            AimRight = aimRight;
            AimUp = aimUp;
            AimDown = aimDown;
            Shoot = shoot;
            Zoom = zoom;
            Jump = jump;
            Morph = morph;
            Boost = boost;
            AltAttack = altAttack;
            NextWeapon = nextWeapon;
            PrevWeapon = prevWeapon;
            WeaponMenu = weaponMenu;
            PowerBeam = powerBeam;
            Missile = missile;
            VoltDriver = voltDriver;
            Battlehammer = battlehammer;
            Imperialist = imperialist;
            Judicator = judicator;
            Magmaul = magmaul;
            ShockCoil = shockCoil;
            OmegaCannon = omegaCannon;
            AffinitySlot = affinitySlot;
            Pause = pause;
            HudOverlay = hudOverlay;
            All = new[]
            {
                moveLeft,
                moveRight,
                moveUp,
                moveDown,
                rollLeft,
                rollRight,
                rollUp,
                rollDown,
                aimLeft,
                aimRight,
                aimUp,
                aimDown,
                shoot,
                zoom,
                jump,
                morph,
                boost,
                altAttack,
                nextWeapon,
                prevWeapon,
                weaponMenu,
                QuickSwap,
                RecapHistory,
                powerBeam,
                missile,
                voltDriver,
                battlehammer,
                imperialist,
                judicator,
                magmaul,
                shockCoil,
                omegaCannon,
                affinitySlot,
                pause,
                hudOverlay
            };
        }

        public void ClearAll()
        {
            for (int i = 0; i < All.Length; i++)
            {
                All[i].IsDown = false;
                All[i].IsPressed = false;
                All[i].IsReleased = false;
            }
        }

        public void ClearPressed()
        {
            for (int i = 0; i < All.Length; i++)
            {
                All[i].IsPressed = false;
            }
        }

        public static ClientPlayerBindings GetDefault()
        {
            ClientPlayerBindings controls = CreateDefault();
            // Whatever the player has bound, applied to every set of controls
            // the game creates. Mods.InputSettings holds the canonical one.
            Mods.InputSettings.Apply(controls);
            return controls;
        }

        private static ClientPlayerBindings CreateDefault()
        {
            return new ClientPlayerBindings(moveLeft: new Keybind(Keys.A), moveRight: new Keybind(Keys.D), moveUp: new Keybind(Keys.W), moveDown: new Keybind(Keys.S), rollLeft: new Keybind(Keys.A), rollRight: new Keybind(Keys.D), rollUp: new Keybind(Keys.W), rollDown: new Keybind(Keys.S), aimLeft: new Keybind(Keys.Left), aimRight: new Keybind(Keys.Right), aimUp: new Keybind(Keys.Up), aimDown: new Keybind(Keys.Down), shoot: new Keybind(MouseButton.Left), zoom: new Keybind(MouseButton.Right), jump: new Keybind(Keys.Space), morph: new Keybind(Keys.C), boost: new Keybind(Keys.Space), altAttack: new Keybind(MouseButton.Left), nextWeapon: new Keybind(ButtonType.ScrollDown), prevWeapon: new Keybind(ButtonType.ScrollUp), weaponMenu: new Keybind(MouseButton.Middle), powerBeam: new Keybind(Keys.D1), missile: new Keybind(Keys.D2), voltDriver: new Keybind(Keys.D3), battlehammer: new Keybind(Keys.D4), imperialist: new Keybind(Keys.D5), judicator: new Keybind(Keys.D6), magmaul: new Keybind(Keys.D7), shockCoil: new Keybind(Keys.D8), omegaCannon: new Keybind(Keys.D9), affinitySlot: new Keybind(Keys.Unknown), pause: new Keybind(Keys.Tab), hudOverlay: new Keybind(Keys.LeftShift));
        }
    }
}
