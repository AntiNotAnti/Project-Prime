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
        private static readonly ColorRgba[] ProTeamInk =
        {
            new(255, 170, 48, 255), new(80, 235, 120, 255),
            new(80, 175, 255, 255), new(225, 105, 255, 255)
        };
        private static readonly Vector2[] ProDamageOffsets =
        {
            new(0, -1), new(.72f, -.72f), new(1, 0), new(.72f, .72f),
            new(0, 1), new(-.72f, .72f), new(-1, 0), new(-.72f, -.72f)
        };
        private static readonly string[] ProDamageLabels =
            { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        private readonly float[] _proDamageStrength = new float[8];
        private uint _proPickupSequence;
        private uint _proPickupTick;
        private uint _proPickupItem;
        private int _proPickupCount;
        private string _proPickupMessage = "";
        private ulong _proHudTextFrame = ulong.MaxValue;
        private string _proHealthText = "";
        private string? _proAmmoText;
        private string _proMatchClockText = "";
        private string _proMatchScoreText = "";
        private string _proObjectiveText = "";
        private string _proModeScoreLabel = "";
        private string _proModeScoreText = "";
        private string? _proDoubleDamageText;
        private string? _proCloakText;
        private string? _proBombText;

        public void DrawProHud()
        {
            RefreshProHudText();
            float aspect = HudAspectFix;
            float scale = Features.ProHudScale;
            float edge = ProHudLeftEdge(aspect);
            float bottom = 190;
            float height = 22 * scale;
            float top = bottom - height;
            Vector4 health = ProHealthColor();
            DrawProPanel(edge, top, edge + 58 * scale * aspect, bottom, health);
            ProNumber(edge + 4 * scale * aspect, top + 4 * scale, Align.Left,
                _proHealthText, ProInk(health), 1.5f * scale);
            ProBar(edge + 2 * scale * aspect, bottom - 4 * scale,
                54 * scale, 3 * scale, ProHealthFraction(), health);
            DrawProAmmo(scale, top, bottom);
            DrawProStatusRow(edge, top, scale);
            DrawProMatchStrip(scale);
            DrawProDamageIndicators(scale);
            // Below the chat log: the pro score sits in the same corner the
            // log is drawn into, and at 12 units down it was underneath the
            // second line of it. See ModChatClearance.
            float scoreY = ModChatClearance(12);
            DrawProPanel(edge, scoreY - 2, edge + 68 * scale * aspect,
                scoreY + 22 * scale,
                new Vector4(0.42f, 0.72f, 1f, 1));
            ProScore(edge + 3 * scale * aspect, scoreY + scale,
                Align.Left, 1.1f * scale);
        }

        private float ProHudLeftEdge(float aspect)
        {
            float edge = 2 * aspect;
            if (Features.ProHudSafeArea)
            {
                // At 16:9 HudAspectFix is .75. On wider displays this adds the
                // centered 16:9 pillar margin in the HUD's 256-unit space.
                edge += Math.Max(0, 128 * (1 - 4f / 3f * aspect));
            }
            return edge;
        }

        private float ProHudRightEdge(float aspect) => 256 - ProHudLeftEdge(aspect);

        /// <summary>
        /// Panel geometry for the ammo corner, in HUD units off the screen's
        /// height. Wider than the energy panel opposite it because it carries
        /// an icon as well as a number, and the number can be three digits:
        /// the Battlehammer costs 4 a shot, so a full pool is 149 of them --
        /// which is 36 units of digits beside a 12-unit icon.
        /// </summary>
        private const float ProAmmoPanelWidth = 58;
        private const float ProAmmoNumberScale = 1.5f;
        public void DrawProAmmo(float scale, float top, float bottom)
        {
            RefreshProHudText();
            string? ammo = _proAmmoText;
            if (ammo == null)
            {
                return;
            }

            float aspect = HudAspectFix;
            Vector4 color = ProAmmoColor();
            float right = ProHudRightEdge(aspect);
            float left = right - ProAmmoPanelWidth * scale * aspect;
            DrawProPanel(left, top, right, bottom, color);
            DrawProAmmoIcon(left + 2 * scale * aspect, top + 4 * scale,
                scale);
            ProNumber(right - 4 * scale * aspect, top + 4 * scale,
                Align.Right, ammo, ProInk(color), ProAmmoNumberScale * scale);
            ProBar(left + 2 * scale * aspect, bottom - 4 * scale,
                (ProAmmoPanelWidth - 4) * scale, 3 * scale,
                ProAmmoFraction(), color);
            float charge = ProChargeFraction();
            if (charge > 0)
            {
                ProBar(left + 2 * scale * aspect, top + 1.5f * scale,
                    (ProAmmoPanelWidth - 4) * scale, 1.25f * scale,
                    charge, charge >= 1 ? ProGood : ProWarn);
            }
        }

        public void DrawProAmmoIcon(float x, float y, float hudScale)
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
            float side = 8 * ProAmmoNumberScale * hudScale;
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

        private float ProChargeFraction()
        {
            WeaponInfo weapon = _player.EquipInfo.Weapon;
            if (!weapon.Flags.TestFlag(WeaponFlags.CanCharge)
                || _player.EquipInfo.ChargeLevel == 0)
            {
                return 0;
            }
            int full = Math.Max(SimTicks.From30HzFrames(weapon.FullCharge), 1);
            return Math.Clamp(_player.EquipInfo.ChargeLevel / (float)full, 0, 1);
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
                return Features.ProHudHighContrast
                    ? new Vector4(.2f, 1f, .55f, 1) : ProGood;
            }

            if (fraction > ProHudDanger)
            {
                return Features.ProHudHighContrast
                    ? new Vector4(1f, .82f, .05f, 1) : ProWarn;
            }

            return Features.ProHudHighContrast
                ? new Vector4(1f, .08f, .55f, 1) : ProDanger;
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
                return Features.ProHudHighContrast
                    ? new Vector4(.2f, 1f, .55f, 1) : ProGood;
            }

            if (amount >= ProAmmoFull / 5)
            {
                return Features.ProHudHighContrast
                    ? new Vector4(1f, .82f, .05f, 1) : ProWarn;
            }

            return Features.ProHudHighContrast
                ? new Vector4(1f, .08f, .55f, 1) : ProDanger;
        }

        private void DrawProMatchStrip(float scale)
        {
            float aspect = HudAspectFix;
            float width = 116 * scale * aspect;
            float left = 128 - width / 2;
            float top = 3;
            float bottom = top + 27 * scale;
            ColorRgba teamInk = ProTeamInk[Math.Clamp(_player.TeamIndex, 0,
                ProTeamInk.Length - 1)];
            Vector4 accent = _player._scene.Match.Period != MatchPeriod.Regulation
                ? ProWarn : _player._scene.Match.Rules.Teams
                    ? new Vector4(teamInk.Red / 255f, teamInk.Green / 255f,
                        teamInk.Blue / 255f, 1)
                    : new Vector4(.32f, .72f, 1f, 1);
            DrawProPanel(left, top, left + width, bottom, accent);

            ProNumber(128, top + 2 * scale, Align.Center,
                _proMatchClockText, ProHudInk, .62f * scale);
            ProNumber(128, top + 10 * scale, Align.Center,
                _proMatchScoreText, ProHudInk, .58f * scale);
            if (_proObjectiveText.Length > 0)
            {
                ColorRgba objectiveColor = _proObjectiveText.Contains("CONTESTED",
                    StringComparison.Ordinal) ? ProInk(ProWarn) : ProHudDim;
                ProNumber(128, top + 18 * scale, Align.Center,
                    _proObjectiveText, objectiveColor, .48f * scale);
            }
        }

        private void RefreshProHudText()
        {
            ulong frame = _player._scene.FrameCount;
            if (_proHudTextFrame == frame)
            {
                return;
            }
            _proHudTextFrame = frame;

            _proHealthText = _player._health.ToString();
            _proAmmoText = ProAmmoText();
            string clock = _player._scene.Match.MatchTime < 0 ? "--:--"
                : FormatTime(TimeSpan.FromSeconds(
                    Math.Max(0, _player._scene.Match.MatchTime)));
            string period = _player._scene.Match.Period switch
            {
                MatchPeriod.Overtime => "OT",
                MatchPeriod.SuddenDeath => "SD",
                _ => "TIME"
            };
            _proMatchClockText = $"{period} {clock}";
            _proMatchScoreText = ProMatchScoreText();
            _proObjectiveText = ProObjectiveStatus();
            _proModeScoreLabel = Strings.GetHudMessage(ProScoreMessageId());
            _proModeScoreText = FormatModeScore(_player.SlotIndex);
            _proDoubleDamageText = _player._doubleDmgTimer > 0
                ? $"2X {ProSeconds(_player._doubleDmgTimer)}" : null;
            _proCloakText = _player._cloakTimer > 0
                && _player.Flags2.TestFlag(PlayerFlags2.Cloaking)
                    ? $"CLOAK {ProSeconds(_player._cloakTimer)}" : null;
            _proBombText = _player.IsAltForm
                && _player._abilities.TestFlag(AbilityFlags.Bombs)
                    ? $"BOMBS {_player._bombAmmo}" : null;
        }

        internal string ProMatchScoreText()
        {
            MatchRuntime match = _player._scene.Match;
            if (match.Rules.Teams)
            {
                int teams = Math.Clamp(match.Rules.TeamCount, 2,
                    MatchRules.MaximumTeamCount);
                string text = "";
                for (int team = 0; team < teams; team++)
                {
                    if (team > 0) text += "  |  ";
                    text += $"T{team + 1} {ProTeamScore(team)}";
                }
                return text;
            }

            int localSlot = _player.SlotIndex;
            string local = ProPlayerScore(localSlot);
            int leader = localSlot;
            for (int slot = 0; slot < _player._scene.Players.Count; slot++)
            {
                PlayerEntity candidate = _player._scene.Players[slot];
                if (!candidate.LoadFlags.TestFlag(LoadFlags.Active)) continue;
                if (ProPlayerScoreValue(slot) > ProPlayerScoreValue(leader))
                    leader = slot;
            }
            string goal = ProGoalText();
            return leader == localSlot ? $"YOU {local}{goal}"
                : $"YOU {local}{goal}  |  LEAD {ProPlayerScore(leader)}";
        }

        private string ProTeamScore(int team)
        {
            MatchRuntime match = _player._scene.Match;
            MatchMode mode = match.Rules.Mode;
            if (mode is MatchMode.TeamSurvival)
            {
                return Math.Max(match.Rules.StartingLives
                    - match.TeamDeaths[team], 0).ToString();
            }
            if (mode is MatchMode.TeamDefender)
                return FormatTime(TimeSpan.FromSeconds(match.TeamTime[team]));
            return match.TeamPoints[team].ToString();
        }

        private string ProPlayerScore(int slot)
        {
            MatchRuntime match = _player._scene.Match;
            MatchMode mode = match.Rules.Mode;
            if (mode is MatchMode.Survival)
                return Math.Max(match.Rules.StartingLives
                    - match.TeamDeaths[_player._scene.Players[slot].TeamIndex], 0)
                    .ToString();
            if (mode is MatchMode.Defender or MatchMode.PrimeHunter)
                return FormatTime(TimeSpan.FromSeconds(match.Players[slot].Time));
            return match.Players[slot].Points.ToString();
        }

        private float ProPlayerScoreValue(int slot)
        {
            MatchRuntime match = _player._scene.Match;
            MatchMode mode = match.Rules.Mode;
            if (mode is MatchMode.Survival)
                return Math.Max(match.Rules.StartingLives
                    - match.TeamDeaths[_player._scene.Players[slot].TeamIndex], 0);
            if (mode is MatchMode.Defender or MatchMode.PrimeHunter)
                return match.Players[slot].Time;
            return match.Players[slot].Points;
        }

        private string ProGoalText()
        {
            MatchRules rules = _player._scene.Match.Rules;
            if (rules.IsObjectiveTimeMode)
                return rules.LegacyTimeGoal > 0
                    ? $"/{FormatTime(TimeSpan.FromSeconds(rules.LegacyTimeGoal))}" : "";
            return rules.LegacyPointGoal > 0 ? $"/{rules.LegacyPointGoal}" : "";
        }

        internal string ProObjectiveStatus()
        {
            MatchMode mode = _player._scene.Match.Rules.Mode;
            if (mode is MatchMode.Nodes or MatchMode.TeamNodes)
            {
                int friendly = 0, enemy = 0, contested = 0;
                foreach (NodeDefenseEntity node in _player._scene.GetNodeDefenseEntities())
                {
                    if (node.Contested) contested++;
                    else if (node.CurrentTeam == _player.TeamIndex) friendly++;
                    else if (node.CurrentTeam != NodeDefenseEntity.NeutralTeam) enemy++;
                }
                string text = $"NODES {friendly}-{enemy}";
                return contested > 0 ? text + "  CONTESTED" : text;
            }
            if (mode is MatchMode.Defender or MatchMode.TeamDefender)
            {
                foreach (NodeDefenseEntity ring in _player._scene.GetNodeDefenseEntities())
                {
                    if (ring.Contested) return "RING CONTESTED";
                    if (ring.CurrentTeam == NodeDefenseEntity.NeutralTeam) return "RING OPEN";
                    return ring.CurrentTeam == _player.TeamIndex ? "RING OURS" : "RING ENEMY";
                }
                return "RING OPEN";
            }
            if (mode is MatchMode.Capture or MatchMode.Bounty or MatchMode.TeamBounty)
            {
                bool localCarrier = false, allyCarrier = false;
                bool enemyCarrier = false, available = false;
                foreach (OctolithFlagEntity flag in _player._scene.GetOctolithFlagEntities())
                {
                    localCarrier |= ReferenceEquals(flag.Carrier, _player);
                    allyCarrier |= flag.Carrier != null
                        && flag.Carrier.TeamIndex == _player.TeamIndex
                        && !ReferenceEquals(flag.Carrier, _player);
                    enemyCarrier |= flag.Carrier != null
                        && flag.Carrier.TeamIndex != _player.TeamIndex;
                    available |= flag.AtBase;
                }
                if (localCarrier) return "CARRYING OCTOLITH";
                if (allyCarrier) return "ALLY CARRIER";
                if (enemyCarrier) return "ENEMY CARRIER";
                return available ? "OCTOLITH AVAILABLE" : "OCTOLITH DROPPED";
            }
            if (mode == MatchMode.PrimeHunter)
            {
                if (_player.IsPrimeHunter) return "YOU ARE PRIME";
                int prime = _player._scene.Match.PrimeHunter;
                return prime >= 0 ? $"PRIME P{prime + 1}" : "PRIME UNCLAIMED";
            }
            return _player._scene.Match.Period switch
            {
                MatchPeriod.Overtime => "OVERTIME",
                MatchPeriod.SuddenDeath => "SUDDEN DEATH",
                _ => ""
            };
        }

        private void DrawProStatusRow(float left, float healthTop, float scale)
        {
            float x = left;
            float y = healthTop - 9 * scale;
            if (_proDoubleDamageText != null)
                DrawProBadge(ref x, y, _proDoubleDamageText,
                    new Vector4(1f, .72f, .08f, 1), scale);
            if (_proCloakText != null)
                DrawProBadge(ref x, y, _proCloakText,
                    new Vector4(.42f, .82f, 1f, 1), scale);
            if (_player.IsPrimeHunter)
                DrawProBadge(ref x, y, "PRIME", new Vector4(1f, .45f, .08f, 1), scale);
            if (_player.OctolithFlag != null)
                DrawProBadge(ref x, y, "FLAG", new Vector4(.92f, .85f, .15f, 1), scale);
            if (_proBombText != null)
                DrawProBadge(ref x, y, _proBombText,
                    new Vector4(.72f, .72f, .82f, 1), scale);
        }

        private static int ProSeconds(ushort ticks)
            => Math.Max(1, (ticks + SimTicks.Hz - 1) / SimTicks.Hz);

        private void DrawProBadge(ref float x, float y, string text,
            Vector4 accent, float scale)
        {
            float aspect = HudAspectFix;
            float width = Math.Max(15, 4 + text.Length * 3.25f) * scale;
            DrawProPanel(x, y, x + width * aspect, y + 7 * scale, accent);
            ProNumber(x + 2 * scale * aspect, y + 1.2f * scale,
                Align.Left, text, ProHudInk, .42f * scale);
            x += (width + 2) * aspect;
        }

        internal void RecordProDamage(int index, int amount)
        {
            if ((uint)index >= _proDamageStrength.Length) return;
            _proDamageStrength[index] = Math.Clamp(amount / 80f, .35f, 1f);
        }

        private void DrawProDamageIndicators(float scale)
        {
            const float maxTicks = 126;
            float aspect = HudAspectFix;
            for (int index = 0; index < _damageIndicatorTimers.Length; index++)
            {
                ushort timer = _damageIndicatorTimers[index];
                if (timer == 0) continue;
                float fade = Math.Clamp(timer / maxTicks, 0, 1);
                float strength = Math.Max(_proDamageStrength[index], .35f);
                Vector2 offset = ProDamageOffsets[index];
                float x = 128 + offset.X * 26 * scale * aspect;
                float y = 96 + offset.Y * 21 * scale;
                Vector4 color = strength >= .7f ? ProDanger : ProWarn;
                Presentation.DrawHudFlatBox(x - 4 * scale * aspect,
                    y - 3 * scale, x + 4 * scale * aspect, y + 3 * scale,
                    new Vector4(color.X, color.Y, color.Z, .32f * fade));
                DrawText2D(x, y - 2.2f * scale, Align.Center, 0,
                    ProDamageLabels[index], ProInk(color), alpha: fade,
                    scale: .38f * scale);
            }
        }

        internal string ProPickupMessage(MphRead.Combat.WorldFeedback world)
        {
            if (_proPickupMessage.Length == 0
                || _proPickupSequence != world.Sequence)
            {
                bool combine = world.LastKind == WorldSignalKind.PickupConsumed
                    && world.Sequence == unchecked(_proPickupSequence + 1)
                    && _proPickupCount > 0 && _proPickupItem == world.LastEvent.A
                    && MphRead.Combat.CombatFeedback.Age(world.Tick, _proPickupTick) <= 60;
                _proPickupCount = combine ? _proPickupCount + 1 : 1;
                _proPickupSequence = world.Sequence;
                _proPickupTick = world.Tick;
                _proPickupItem = world.LastEvent.A;
                _proPickupMessage = _proPickupCount > 1
                    ? $"{world.Message} x{_proPickupCount}" : world.Message;
            }
            return _proPickupMessage;
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
            RefreshProHudText();
            ProNumber(x, y, align, _proModeScoreLabel, ProHudDim, .5f * scale);
            ProNumber(x, y + 7.25f * scale, align,
                _proModeScoreText, ProHudInk, scale);
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
