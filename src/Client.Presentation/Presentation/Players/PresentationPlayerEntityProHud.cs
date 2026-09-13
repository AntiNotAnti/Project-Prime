using System;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Text;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        /// <summary>
        /// Below these fractions of a full tank the readout goes amber, then
        /// red -- 60 and 33 of 99, which is where GetCrosshairColor turns.
        /// </summary>
        private const float ProHudWarn = 60 / 99f;
        private const float ProHudDanger = 33 / 99f;
        /// <summary>
        /// The three states every readout here shares: plenty, getting low,
        /// nearly out. One set of colours for energy and ammo both, so a
        /// glance at either corner means the same thing, and so the two
        /// cannot drift apart into two vocabularies.
        ///
        /// Three, not four: carrying more than a hunter's own tank used to get
        /// a fourth colour of its own, which made the one state you are never
        /// in trouble in the one state the bar changed colour for. Plenty is
        /// plenty, and the number beside the bar says how much.
        /// </summary>
        private static readonly Vector4 ProGood = new Vector4(0.24f, 0.85f, 0.32f, 1);
        private static readonly Vector4 ProWarn = new Vector4(1f, 0.68f, 0.1f, 1);
        private static readonly Vector4 ProDanger = new Vector4(0.95f, 0.18f, 0.18f, 1);
        private static readonly Vector4 ProHudPanel = new Vector4(0.025f, 0.035f, 0.055f, 0.72f);
        private static readonly Vector4 ProHudShade = new Vector4(0, 0, 0, 0.65f);
        private static readonly Vector4 ProHudTrack = new Vector4(1, 1, 1, 0.16f);
        private static readonly ColorRgba ProHudInk = new ColorRgba(235, 238, 245, 255);
        private static readonly ColorRgba ProHudDim = new ColorRgba(178, 186, 200, 255);
        private static readonly ColorRgba ProHudShadow = new ColorRgba(0, 0, 0, 255);
        public void DrawProHud()
        {
            float aspect = HudAspectFix;
            Vector4 health = ProHealthColor();
            DrawProPanel(2 * aspect, 168, 60 * aspect, 190, health);
            ProNumber(6 * aspect, 172, Align.Left, _player._health.ToString(), ProInk(health), 1.5f);
            ProBar(4 * aspect, 186, 54, 3, ProHealthFraction(), health);
            DrawProAmmo();
            // Below the chat log: the pro score sits in the same corner the
            // log is drawn into, and at 12 units down it was underneath the
            // second line of it. See ModChatClearance.
            float scoreY = ModChatClearance(12);
            DrawProPanel(2 * aspect, scoreY - 2, 70 * aspect, scoreY + 22,
                new Vector4(0.42f, 0.72f, 1f, 1));
            ProScore(5 * aspect, scoreY + 1, Align.Left, 1.1f);
        }

        /// <summary>
        /// Panel geometry for the ammo corner, in HUD units off the screen's
        /// height. Wider than the energy panel opposite it because it carries
        /// an icon as well as a number, and the number can be three digits:
        /// the Battlehammer costs 4 a shot, so a full pool is 149 of them --
        /// which is 36 units of digits beside a 12-unit icon.
        /// </summary>
        private const float ProAmmoPanelWidth = 58;
        private const float ProAmmoNumberScale = 1.5f;
        public void DrawProAmmo()
        {
            string? ammo = ProAmmoText();
            if (ammo == null)
            {
                return;
            }

            float aspect = HudAspectFix;
            Vector4 color = ProAmmoColor();
            float right = 256 - 2 * aspect;
            float left = right - ProAmmoPanelWidth * aspect;
            DrawProPanel(left, 168, right, 190, color);
            DrawProAmmoIcon(left + 2 * aspect, 172);
            ProNumber(right - 4 * aspect, 172, Align.Right, ammo, ProInk(color), ProAmmoNumberScale);
            ProBar(left + 2 * aspect, 186, ProAmmoPanelWidth - 4, 3, ProAmmoFraction(), color);
        }

        public void DrawProAmmoIcon(float x, float y)
        {
            int index = (int)_player.CurrentWeapon;
            if (index < 0 || index >= _weaponListIcons.Length)
            {
                return;
            }

            HudObjectInstance icon = _weaponListIcons[index];
            if (icon == null)
            {
                return;
            }

            // A glyph is 8 units tall before scaling, so this is exactly the
            // height of the number it stands next to.
            float side = 8 * ProAmmoNumberScale;
            float aspect = HudAspectFix;
            IconBounds bounds = _weaponListIconBounds[index];
            float scale = side / Math.Max(bounds.Width, bounds.Height);
            icon.SetData(index, _weaponListColors[index], _player._scene);
            icon.Alpha = Features.HudOpacity;
            // The ink's centre put in the centre of a box `side` across and
            // `side` down -- across being measured off the height too, hence
            // the aspect on every horizontal term.
            icon.PositionX = (x + side * aspect / 2 - bounds.CentreX * scale * aspect) / 256f;
            icon.PositionY = (y + side / 2 - bounds.CentreY * scale) / 192f;
            Presentation.DrawHudObject(icon, mode: 1, scale: scale);
        }

        public float ProHealthFraction()
        {
            return Math.Clamp(_player._health / (float)ProHealthSpan(), 0, 1);
        }

        public int ProHealthSpan()
        {
            return Math.Max(_player.Values.EnergyTank - 1, 1);
        }

        public Vector4 ProHealthColor()
        {
            float fraction = ProHealthFraction();
            if (fraction > ProHudWarn)
            {
                return ProGood;
            }

            if (fraction > ProHudDanger)
            {
                return ProWarn;
            }

            return ProDanger;
        }

        public static ColorRgba ProInk(Vector4 color)
        {
            return new ColorRgba((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), 255);
        }

        public string? ProAmmoText()
        {
            if (_player.IsAltForm || _player.IsMorphing || _player.IsUnmorphing)
            {
                return null;
            }

            WeaponInfo info = _player.EquipInfo.Weapon;
            if (info.AmmoCost == 0)
            {
                return null;
            }

            int amount = _player._ammo[info.AmmoType];
            return amount < 0 ? "--" : (amount / info.AmmoCost).ToString();
        }

        /// <summary>
        /// What counts as a full load, in pool units: the big ammo pickup.
        ///
        /// Not <c>_ammoMax</c>, which is the *cap* -- 599 in multiplayer, six
        /// times what anything hands out at once. Measured against that, a
        /// hunter carrying a perfectly ordinary loadout reads as nearly empty,
        /// and the bar never moves off its left end. What the game actually
        /// gives you is 100 units from a big pickup and 50 from a small one
        /// (250 and 100 in the story), a weapon pickup tops you up to 60, and
        /// multiplayer spawns you with exactly 100 units of missiles -- ten of
        /// them, at ten a shot. So one big pickup is what "full" means here,
        /// and the spawn loadout reads full, which is what it is.
        /// </summary>
        private const int ProAmmoFull = 100;
        public float ProAmmoFraction()
        {
            int amount = _player._ammo[_player.EquipInfo.Weapon.AmmoType];
            if (amount < 0)
            {
                return 1;
            }

            return Math.Clamp(amount / (float)ProAmmoFull, 0, 1);
        }

        public Vector4 ProAmmoColor()
        {
            int amount = _player._ammo[_player.EquipInfo.Weapon.AmmoType];
            if (amount < 0 || amount >= ProAmmoFull / 2)
            {
                return ProGood;
            }

            if (amount >= ProAmmoFull / 5)
            {
                return ProWarn;
            }

            return ProDanger;
        }

        public void ProBar(float x, float y, float width, float height, float fill, Vector4 color)
        {
            float aspect = HudAspectFix;
            float pad = 1f;
            Presentation.DrawHudFlatBox(x - pad * aspect, y - pad, x + (width + pad) * aspect, y + height + pad, ProHudShade);
            Presentation.DrawHudFlatBox(x, y, x + width * aspect, y + height, ProHudTrack);
            if (fill > 0)
            {
                Presentation.DrawHudFlatBox(x, y, x + width * fill * aspect, y + height, color);
            }
        }

        private void DrawProPanel(float left, float top, float right, float bottom,
            Vector4 accent)
        {
            float shadowOffset = HudAspectFix;
            Presentation.DrawHudFlatBox(left + shadowOffset, top + 1,
                right + shadowOffset, bottom + 1, ProHudShade);
            Presentation.DrawHudFlatBox(left, top, right, bottom, ProHudPanel);
            Presentation.DrawHudFlatBox(left, top, right, top + 1,
                new Vector4(accent.X, accent.Y, accent.Z, 0.9f));
        }

        public void ProNumber(float x, float y, Align align, ReadOnlySpan<char> text, ColorRgba color, float scale)
        {
            float aspect = HudAspectFix;
            DrawText2D(x + 0.8f * aspect, y + 0.8f, align, palette: 0, text, ProHudShadow, scale: scale);
            DrawText2D(x, y, align, palette: 0, text, color, scale: scale);
        }

        public void ProScore(float x, float y, Align align, float scale)
        {
            string label = Strings.GetHudMessage(ProScoreMessageId());
            ProNumber(x, y, align, label, ProHudDim, 0.55f);
            ProNumber(x, y + 8, align, FormatModeScore(_player._scene.LocalPlayerSlot), ProHudInk, scale);
        }

        public int ProScoreMessageId()
        {
            switch (_player._scene.Match.Rules.Mode)
            {
                case MatchMode.Survival:
                case MatchMode.TeamSurvival:
                    return 213; // lives left
                case MatchMode.PrimeHunter:
                    return 214; // prime time
                case MatchMode.Bounty:
                case MatchMode.TeamBounty:
                    return 215; // octoliths
                case MatchMode.Capture:
                    return 216; // octoliths
                case MatchMode.Defender:
                case MatchMode.TeamDefender:
                    return 217; // ring time
                case MatchMode.Nodes:
                case MatchMode.TeamNodes:
                    return 218; // points
                default:
                    return 212; // points
            }
        }
    }
}
