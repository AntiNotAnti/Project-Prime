using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using MphRead.Formats;
using MphRead.Hud;
using MphRead.Text;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private HudObjects _hudObjects = null!;
        private HudObject _targetCircleObj = null!;
        private HudObject _sniperCircleObj = null!;
        private HudObjectInstance _targetCircleInst = null!;
        private HudObjectInstance _cloakInst = null!;
        private HudObjectInstance _doubleDamageInst = null!;
        private readonly HudObjectInstance[] _weaponSelectInsts = new HudObjectInstance[6];
        private readonly HudObjectInstance[] _selectBoxInsts = new HudObjectInstance[6];
        private HudObjectInstance _textInst = null!;
        private HudMeter _healthbarMainMeter = null!;
        private HudMeter _healthbarSubMeter = null!;
        private HudMeter _ammoBarMeter = null!;
        private HudObjectInstance _weaponIconInst = null!;
        private readonly HudObjectInstance[] _weaponListIcons = new HudObjectInstance[9];
        /// <summary>Where the drawing actually is inside each of those frames. See ModIconBounds.</summary>
        private readonly IconBounds[] _weaponListIconBounds = new IconBounds[9];
        private HudObjectInstance _boostInst = null!;
        private HudObjectInstance _bombInst = null!;
        private HudMeter _enemyHealthMeter = null!;
        private HudObjectInstance _octolithInst = null!;
        private HudObjectInstance _primeHunterInst = null!;
        private HudObjectInstance _nodesInst = null!;
        private HudMeter _nodeProgressMeter = null!;
        private ModelInstance _damageIndicator = null!;
        private readonly ushort[] _damageIndicatorTimers = new ushort[8];
        private readonly Node[] _damageIndicatorNodes = new Node[8];
        private ModelInstance _playerLocator = null!;
        private ModelInstance _arrowLocator = null!;
        private ModelInstance _nodeLocator = null!;
        private ModelInstance _octolithLocator = null!;
        private HudObjectInstance _starsInst = null!;
        private readonly HudObjectInstance[] _hunterInsts = new HudObjectInstance[8];
        private ModelInstance _filterModel = null!;
        /// <summary>
        /// Whether the scoreboard is up: this player holding the button for
        /// it, or somebody spectating this player holding it. A spectator's
        /// own button state is kept by <see cref = "Mods.SpectatorMode"/>,
        /// because the input pass that fills in <c>_showScoreboard</c>
        /// is the one spectating skips.
        /// </summary>
        private bool ShowScoreboard => _player._showScoreboard || Mods.SpectatorMode.ShowScoreboard && _player.IsMainPlayer || ModForceScoreboard && _player.IsMainPlayer;
        /// <summary>
        /// Hold the scoreboard open, for the harness.
        ///
        /// It cannot be done by writing <c>Controls.Pause</c> the way the tour
        /// writes every other button: the main player's controls are refilled
        /// from the keyboard at the top of each simulation step, so anything
        /// the harness puts there is gone before the input pass reads it.
        /// Which is exactly why the scoreboard had never been drawn by a
        /// check -- the one screen in the game that a player opens by holding
        /// a button was the one screen nothing here could open.
        /// </summary>
        public static bool ModForceScoreboard { get; set; }

        private int _iceLayerBindingId = -1;
        private int _helmetBindingId = -1;
        private int _helmetDropBindingId = -1;
        private int _visorBindingId = -1;
        private IReadOnlyList<ColorRgba> _textPaletteData = null!;
        public void SetUpHud()
        {
            (_iceLayerBindingId, _) = HudInfo.CharMapToTexture(HudElements.IceLayer, startX: 16, startY: 0, tilesX: 32, tilesY: 32, _player._scene);
            (_helmetBindingId, _) = HudInfo.CharMapToTexture(_hudObjects.Helmet, _player._scene);
            (_helmetDropBindingId, _) = HudInfo.CharMapToTexture(_hudObjects.HelmetDrop, _player._scene);
            (_visorBindingId, IReadOnlyList<ushort>? visorPal) = HudInfo.CharMapToTexture(_hudObjects.Visor, startX: 0, startY: 0, tilesX: 0, tilesY: 32, _player._scene);
            if (_player.Hunter == Hunter.Samus)
            {
                visorPal = null;
            }

            // todo: only load what needs to be loaded for the mode
            _filterModel = Read.GetModelInstance("filter");
            Presentation.LoadModel(_filterModel.Model);
            _damageIndicator = Read.GetModelInstance("damage", dir: MetaDir.Hud);
            Presentation.LoadModel(_damageIndicator.Model);
            _damageIndicator.Active = false;
            for (int i = 0; i < 8; i++)
            {
                _damageIndicatorTimers[i] = 0;
            }

            _damageIndicatorNodes[0] = _damageIndicator.Model.GetNodeByName("north")!;
            _damageIndicatorNodes[1] = _damageIndicator.Model.GetNodeByName("ne")!;
            _damageIndicatorNodes[2] = _damageIndicator.Model.GetNodeByName("east")!;
            _damageIndicatorNodes[3] = _damageIndicator.Model.GetNodeByName("se")!;
            _damageIndicatorNodes[4] = _damageIndicator.Model.GetNodeByName("south")!;
            _damageIndicatorNodes[5] = _damageIndicator.Model.GetNodeByName("sw")!;
            _damageIndicatorNodes[6] = _damageIndicator.Model.GetNodeByName("west")!;
            _damageIndicatorNodes[7] = _damageIndicator.Model.GetNodeByName("nw")!;
            _playerLocator = Read.GetModelInstance("hud_icon_player", dir: MetaDir.Hud);
            Presentation.LoadModel(_playerLocator.Model);
            _arrowLocator = Read.GetModelInstance("hud_icon_arrow", dir: MetaDir.Hud);
            Presentation.LoadModel(_arrowLocator.Model);
            _nodeLocator = Read.GetModelInstance("hud_icon_nodes", dir: MetaDir.Hud);
            Presentation.LoadModel(_nodeLocator.Model);
            _octolithLocator = Read.GetModelInstance("hud_icon_octolith", dir: MetaDir.Hud);
            Presentation.LoadModel(_octolithLocator.Model);
            _targetCircleObj = HudInfo.GetHudObject(_hudObjects.Reticle);
            _sniperCircleObj = HudInfo.GetHudObject(_hudObjects.SniperReticle);
            Debug.Assert(_sniperCircleObj.Width >= _targetCircleObj.Width);
            Debug.Assert(_sniperCircleObj.Height >= _targetCircleObj.Height);
            _targetCircleInst = new HudObjectInstance(_targetCircleObj.Width, _targetCircleObj.Height, _sniperCircleObj.Width, _sniperCircleObj.Height);
            _targetCircleInst.SetCharacterData(_targetCircleObj.CharacterData, _player._scene);
            _targetCircleInst.SetPaletteData(_targetCircleObj.PaletteData, _player._scene);
            _targetCircleInst.Center = true;
            HudObject cloak = HudInfo.GetHudObject(_hudObjects.Cloaking);
            _cloakInst = new HudObjectInstance(cloak.Width, cloak.Height);
            _cloakInst.SetCharacterData(cloak.CharacterData, _player._scene);
            _cloakInst.SetPaletteData(cloak.PaletteData, _player._scene);
            _cloakInst.Enabled = true;
            HudObject doubleDamage = HudInfo.GetHudObject(_hudObjects.DoubleDamage);
            _doubleDamageInst = new HudObjectInstance(doubleDamage.Width, doubleDamage.Height);
            _doubleDamageInst.SetCharacterData(doubleDamage.CharacterData, _player._scene);
            _doubleDamageInst.SetPaletteData(doubleDamage.PaletteData, _player._scene);
            _doubleDamageInst.Enabled = true;
            HudObject weaponSelectObj = HudInfo.GetHudObject(_hudObjects.WeaponSelect);
            HudObject selectBoxObj = HudInfo.GetHudObject(_hudObjects.SelectBox);
            // todo: left-handed mode
            var positions = new Vector2[6]
            {
                new Vector2(201 / 256f, 156 / 192f),
                new Vector2(161 / 256f, 152 / 192f),
                new Vector2(122 / 256f, 142 / 192f),
                new Vector2(90 / 256f, 109 / 192f),
                new Vector2(81 / 256f, 70 / 192f),
                new Vector2(77 / 256f, 32 / 192f)
            };
            HudObject iconInst = HudInfo.GetHudObject(_hudObjects.SelectIcon);
            for (int i = 0; i < 6; i++)
            {
                var weaponInst = new HudObjectInstance(weaponSelectObj.Width, weaponSelectObj.Height);
                int frame = i == 0 ? 1 : i + 2;
                weaponInst.SetCharacterData(weaponSelectObj.CharacterData, frame, _player._scene);
                weaponInst.SetPaletteData(weaponSelectObj.PaletteData, _player._scene);
                weaponInst.Alpha = 0.722f;
                var boxInst = new HudObjectInstance(selectBoxObj.Width, selectBoxObj.Height);
                boxInst.SetCharacterData(selectBoxObj.CharacterData, _player._scene);
                boxInst.SetPaletteData(iconInst.PaletteData, _player._scene);
                boxInst.Enabled = true;
                Vector2 position = positions[i];
                weaponInst.PositionX = position.X;
                weaponInst.PositionY = position.Y;
                boxInst.PositionX = position.X;
                boxInst.PositionY = position.Y;
                _weaponSelectInsts[i] = weaponInst;
                _selectBoxInsts[i] = boxInst;
            }

            HudObject healthbarMain = HudInfo.GetHudObject(_hudObjects.HealthBarA);
            HudObject healthbarSub = HudInfo.GetHudObject(_hudObjects.HealthBarB);
            _healthbarMainMeter = HudElements.MainHealthbars[(int)_player.Hunter];
            _healthbarSubMeter = HudElements.SubHealthbars[(int)_player.Hunter];
            _healthbarMainMeter.BarInst = new HudObjectInstance(healthbarMain.Width, healthbarMain.Height);
            _healthbarMainMeter.BarInst.SetCharacterData(healthbarMain.CharacterData, _player._scene);
            _healthbarMainMeter.BarInst.SetPaletteData(healthbarMain.PaletteData, _player._scene);
            _healthbarMainMeter.BarInst.Enabled = true;
            _healthbarSubMeter.BarInst = new HudObjectInstance(healthbarSub.Width, healthbarSub.Height);
            _healthbarSubMeter.BarInst.SetCharacterData(healthbarSub.CharacterData, _player._scene);
            _healthbarSubMeter.BarInst.SetPaletteData(healthbarSub.PaletteData, _player._scene);
            _healthbarSubMeter.BarInst.Enabled = true;
            HudObject damageBar = HudInfo.GetHudObject(_hudObjects.DamageBar);
            _enemyHealthMeter = new HudMeter()
            {
                Horizontal = true,
            };
            _enemyHealthMeter.BarInst = new HudObjectInstance(damageBar.Width, damageBar.Height);
            _enemyHealthMeter.BarInst.SetCharacterData(damageBar.CharacterData, _player._scene);
            _enemyHealthMeter.BarInst.SetPaletteData(damageBar.PaletteData, _player._scene);
            _enemyHealthMeter.BarInst.Enabled = true;
            _healthbarYOffset = _hudObjects.HealthOffsetY;
            HudObject ammoBar = HudInfo.GetHudObject(_hudObjects.AmmoBar);
            _ammoBarMeter = HudElements.AmmoBars[(int)_player.Hunter];
            _ammoBarMeter.BarInst = new HudObjectInstance(ammoBar.Width, ammoBar.Height);
            _ammoBarMeter.BarInst.SetCharacterData(ammoBar.CharacterData, _player._scene);
            _ammoBarMeter.BarInst.SetPaletteData(ammoBar.PaletteData, _player._scene);
            _textPaletteData = ammoBar.PaletteData;
            HudObject weaponIcon = HudInfo.GetHudObject(_hudObjects.WeaponIcon);
            _weaponIconInst = new HudObjectInstance(weaponIcon.Width, weaponIcon.Height);
            _weaponIconInst.SetCharacterData(weaponIcon.CharacterData, _player._scene);
            _weaponIconInst.SetPaletteData(weaponIcon.PaletteData, _player._scene);
            _weaponIconInst.SetAnimationFrames(weaponIcon.AnimParams);
            // Modern HUD's weapon list: one static icon per weapon, each
            // pinned to its own frame instead of switching on equip.
            //
            // Off the **weapon-select sheet**, not the equipped-weapon icon
            // beside the ammo bar. Both have a frame per beam, and they are
            // not the same picture: the equipped icon is a monochrome glyph in
            // the visor's own green, meant to be read at a glance next to a
            // green bar, and a column of nine of them is nine green smudges
            // that have to be told apart by shape. The select sheet is the
            // touchscreen weapon menu's -- the game's own full-colour art for
            // each beam, with its own palette -- which is what makes a list
            // scannable, and is the whole reason Quake's weapon bar works.
            //
            // Frame index is the BeamType either way (see the select ring
            // above, which reads _availableWeapons[inst.CurrentFrame]).
            HudObject listSheet = HudInfo.GetHudObject(_hudObjects.WeaponSelect);
            for (int i = 0; i < _weaponListIcons.Length; i++)
            {
                var listIcon = new HudObjectInstance(listSheet.Width, listSheet.Height);
                listIcon.SetCharacterData(listSheet.CharacterData, i, _player._scene);
                listIcon.SetPaletteData(listSheet.PaletteData, _player._scene);
                listIcon.Enabled = true;
                _weaponListIcons[i] = listIcon;
                // Once, here, because it is a fact about the art and never
                // changes: which part of the frame the weapon is drawn in.
                _weaponListIconBounds[i] = ModIconBounds(listSheet.CharacterData, i, listSheet.Width, listSheet.Height);
            }

            HudObject boost = HudInfo.GetHudObject(HudElements.Boost);
            _boostInst = new HudObjectInstance(boost.Width, boost.Height);
            _boostInst.SetCharacterData(boost.CharacterData, _player._scene);
            _boostInst.SetPaletteData(boost.PaletteData, _player._scene);
            _boostInst.SetAnimationFrames(boost.AnimParams);
            _boostInst.Enabled = true;
            HudObject bombs = HudInfo.GetHudObject(HudElements.Bombs);
            _bombInst = new HudObjectInstance(bombs.Width, bombs.Height);
            _bombInst.SetCharacterData(bombs.CharacterData, _player._scene);
            _bombInst.SetPaletteData(bombs.PaletteData, _player._scene);
            _bombInst.Enabled = true;
            _boostBombsYOffset = 208;
            HudObject stars = HudInfo.GetHudObject(HudElements.Stars);
            _starsInst = new HudObjectInstance(stars.Width, stars.Height);
            _starsInst.SetCharacterData(stars.CharacterData, _player._scene);
            _starsInst.SetPaletteData(stars.PaletteData, _player._scene);
            _starsInst.Enabled = true;
            for (int i = 0; i < 8; i++)
            {
                HudObject hunter = HudInfo.GetHudObject(HudElements.Hunters[i]);
                var hunterInst = new HudObjectInstance(hunter.Width, hunter.Height);
                hunterInst.SetCharacterData(hunter.CharacterData, _player._scene);
                if (i == 7)
                {
                    // skdebug: black out the Guardian portrait (using Samus's) except the frame
                    var palette = hunter.PaletteData.ToList();
                    for (int j = 0; j < palette.Count; j++)
                    {
                        if (j != 1)
                        {
                            palette[j] = new ColorRgba(0, 0, 0, 255);
                        }
                    }

                    hunterInst.SetPaletteData(palette, _player._scene);
                }
                else
                {
                    hunterInst.SetPaletteData(hunter.PaletteData, _player._scene);
                }

                hunterInst.Enabled = true;
                _hunterInsts[i] = hunterInst;
            }

            HudObject octolith = HudInfo.GetHudObject(HudElements.Octolith);
            _octolithInst = new HudObjectInstance(octolith.Width, octolith.Height);
            _octolithInst.SetCharacterData(octolith.CharacterData, _player._scene);
            _octolithInst.SetPaletteData(octolith.PaletteData, _player._scene);
            _octolithInst.Enabled = true;
            HudObject primeHunter = HudInfo.GetHudObject(_hudObjects.PrimeHunter);
            _primeHunterInst = new HudObjectInstance(primeHunter.Width, primeHunter.Height);
            _primeHunterInst.SetCharacterData(primeHunter.CharacterData, _player._scene);
            _primeHunterInst.SetPaletteData(primeHunter.PaletteData, _player._scene);
            _primeHunterInst.Enabled = true;
            HudObject nodes = HudInfo.GetHudObject(_player._scene.Match.Rules.Teams ? HudElements.NodesOG : HudElements.NodesRB);
            _nodesInst = new HudObjectInstance(nodes.Width, nodes.Height);
            _nodesInst.SetCharacterData(nodes.CharacterData, _player._scene);
            _nodesInst.SetPaletteData(nodes.PaletteData, _player._scene);
            _nodesInst.Enabled = true;
            HudObject systemLoad = HudInfo.GetHudObject(HudElements.SystemLoad);
            _nodeProgressMeter = HudElements.NodeProgressBar;
            _nodeProgressMeter.BarInst = new HudObjectInstance(systemLoad.Width, systemLoad.Height);
            _nodeProgressMeter.BarInst.SetCharacterData(systemLoad.CharacterData, _player._scene);
            _nodeProgressMeter.BarInst.SetPaletteData(systemLoad.PaletteData, _player._scene);
            _nodeProgressMeter.BarInst.Enabled = true;
            _textInst = new HudObjectInstance(width: 8, height: 8, maxWidth: 16, maxHeight: 16);
            _textInst.SetCharacterData(Font.Normal.CharacterData, _player._scene);
            _textInst.SetPaletteData(ammoBar.PaletteData, _player._scene);
            _textInst.Enabled = true;
            for (int i = 0; i < _hudMessageQueue.Count; i++)
            {
                _hudMessageQueue[i].Lifetime = 0;
            }

            LoadModeRules();
            HudReady = true;
        }

        /// <summary>
        /// Whether <see cref = "SetUpHud"/> has run for this player. Only ever
        /// true at spawn for whoever was <see cref = "PlayerEntity.Main"/> at the time --
        /// every other player's HUD fields are left null, which is fine
        /// until something (spectating) points <see cref = "PlayerEntity.MainPlayerIndex"/>
        /// at one of them and tries to draw a HUD nothing built.
        /// </summary>
        public bool HudReady { get; private set; }

        private RulesInfo _rulesInfo = null!;
        private readonly string? [] _rulesLines = new string[8];
        private readonly (int Length, int Newlines)[] _rulesLengths = new (int, int)[8];
        public void LoadModeRules()
        {
            for (int i = 0; i < 8; i++)
            {
                _rulesLines[i] = null;
                _rulesLengths[i] = (0, 0);
            }

            MatchMode mode = _player._scene.Match.Rules.Mode;
            if (mode == MatchMode.Battle || mode == MatchMode.TeamBattle)
            {
                _rulesInfo = HudElements.RulesInfo[0];
            }
            else if (mode == MatchMode.Survival || mode == MatchMode.TeamSurvival)
            {
                _rulesInfo = HudElements.RulesInfo[1];
            }
            else if (mode == MatchMode.PrimeHunter)
            {
                _rulesInfo = HudElements.RulesInfo[2];
            }
            else if (mode == MatchMode.Bounty || mode == MatchMode.TeamBounty)
            {
                _rulesInfo = HudElements.RulesInfo[3];
            }
            else if (mode == MatchMode.Capture)
            {
                _rulesInfo = HudElements.RulesInfo[4];
            }
            else if (mode == MatchMode.Defender || mode == MatchMode.TeamDefender)
            {
                _rulesInfo = HudElements.RulesInfo[5];
            }
            else if (mode == MatchMode.Nodes || mode == MatchMode.TeamNodes)
            {
                _rulesInfo = HudElements.RulesInfo[6];
            }

            char[] buf = new char[256];
            for (int i = 0; i < _rulesInfo.Count; i++)
            {
                string line = Strings.GetMessage('S', _rulesInfo.MessageIds[i], StringTables.HudMessagesMP);
                if (i == 0)
                {
                    _rulesLines[i] = line;
                    _rulesLengths[0] = (30, 0);
                }
                else
                {
                    // todo: text wrapping should be based on current window width
                    // --> and so it also needs to be deferred until the text is being rendered
                    int offset = _rulesInfo.Offsets[i];
                    int lineCount = WrapText(line, 244 - (offset + 12), buf);
                    int length = 0;
                    for (int j = 0; j < buf.Length; j++)
                    {
                        if (buf[j] == '\0')
                        {
                            break;
                        }

                        length++;
                    }

                    line = new string (buf, 0, length);
                    _rulesLines[i] = line;
                    _rulesLengths[i] = (_rulesLengths[i - 1].Length + line.Length, lineCount - 1);
                }
            }
        }

        private float _hudShiftX = 0;
        private float _hudShiftY = 0;
        private float _objShiftX = 0;
        private float _objShiftY = 0;
        private bool _hudWeaponMenuOpen = false;
        public void InitHudState()
        {
            _targetCircleInst.Enabled = false;
            _ammoBarMeter.BarInst.Enabled = false;
            _weaponIconInst.Enabled = false;
            _damageIndicator.Active = false;
            Presentation.Layer1Info.BindingId = -1;
            Presentation.Layer2Info.BindingId = -1;
            Presentation.Layer3Info.BindingId = -1;
            Presentation.Layer4Info.BindingId = -1;
            Presentation.Layer5Info.BindingId = -1;
        }

        public void UpdateHud()
        {
            ProcessDoubleDamageHud();
            ProcessCloakHud();
            UpdateHealthbars();
            UpdateAmmoBar();
            _weaponIconInst.ProcessAnimation(_player._scene);
            _boostInst.ProcessAnimation(_player._scene);
            UpdateBoostBombs();
            UpdateDamageIndicators();
            UpdateDisruptedState();
            UpdateWhiteoutState();
            _player.WeaponSelection = _player.CurrentWeapon;
            if (_player.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen))
            {
                if (!_hudWeaponMenuOpen)
                {
                    _player._soundSource.PlayFreeSfx(SfxId.HUD_WEAPON_SWITCH1);
                }

                _hudWeaponMenuOpen = true;
                UpdateWeaponSelect();
            }
            else
            {
                _hudWeaponMenuOpen = false;
            }

            InitHudState();
            Presentation.Layer1Info.ShiftX = 0;
            Presentation.Layer1Info.ShiftY = 0;
            Presentation.Layer2Info.ShiftX = 0;
            Presentation.Layer2Info.ShiftY = 0;
            Presentation.Layer3Info.ShiftX = 0;
            Presentation.Layer3Info.ShiftY = 0;
            Presentation.Layer4Info.ShiftX = 0;
            Presentation.Layer4Info.ShiftY = 0;
            Presentation.Layer5Info.ShiftX = 0;
            Presentation.Layer5Info.ShiftY = 0;
            if (CameraSequence.Current?.Flags.TestFlag(CamSeqFlags.BlockInput) == true)
            {
                return;
            }

            if (_player._health > 0)
            {
                if (!_player.IsAltForm && !_player.IsMorphing && !_player.IsUnmorphing)
                {
                    if (!_player.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen) && !ShowScoreboard && _player._scene.Match.LegacyState == MatchState.InProgress)
                    {
                        if (_player._drawIceLayer)
                        {
                            Presentation.Layer4Info.BindingId = _iceLayerBindingId;
                            Presentation.Layer4Info.Alpha = 9 / 16f;
                            Presentation.Layer4Info.ScaleX = -1;
                            Presentation.Layer4Info.ScaleY = -1;
                        }

                        Presentation.Layer3Info.BindingId = _helmetDropBindingId;
                        Presentation.Layer3Info.Alpha = Features.HelmetOpacity;
                        Presentation.Layer3Info.ScaleX = 2;
                        Presentation.Layer3Info.ScaleY = 256 / 192f;
                        Presentation.Layer1Info.BindingId = _visorBindingId;
                        Presentation.Layer1Info.MaskId = -1;
                        Presentation.Layer1Info.Alpha = Features.VisorOpacity;
                        Presentation.Layer1Info.ScaleX = 1;
                        Presentation.Layer1Info.ScaleY = 256 / 192f;
                        Presentation.Layer2Info.BindingId = _helmetBindingId;
                        Presentation.Layer2Info.Alpha = Features.HelmetOpacity;
                        Presentation.Layer2Info.ScaleX = 2;
                        Presentation.Layer2Info.ScaleY = 256 / 192f;
                        Presentation.Layer1Info.ShiftX = _hudShiftX / 256f;
                        Presentation.Layer1Info.ShiftY = _hudShiftY / 192f;
                        Presentation.Layer2Info.ShiftX = _hudShiftX / 256f;
                        Presentation.Layer2Info.ShiftY = _hudShiftY / 192f;
                        Presentation.Layer3Info.ShiftX = -_hudShiftX / 4 / 256f;
                        Presentation.Layer3Info.ShiftY = -_hudShiftY / 4 / 192f;
                    }

                    if (Features.NoIdleSway || _player._timeSinceInput < (ulong)_player.Values.GunIdleTime * SimTicks.TicksPer30HzFrame)
                    {
                        UpdateReticle();
                    }

                    _ammoBarMeter.BarInst.Enabled = true;
                    _weaponIconInst.Enabled = true;
                }

                _damageIndicator.Active = true;
            }
        }

        private int _healthbarPalette = 0;
        private bool _healthbarChangedColor = false;
        private float _healthbarYOffset = 0;
        public void UpdateHealthbars()
        {
            if (_player._health < 25)
            {
                if (!_healthbarChangedColor)
                {
                    _healthbarPalette = 2;
                    _healthbarChangedColor = true;
                }
            }
            else if (_player._timeSinceHeal < SimTicks.From30HzFrames(10))
            {
                if (!_healthbarChangedColor)
                {
                    _healthbarPalette = 1;
                    _healthbarChangedColor = true;
                }
            // todo?: update radar lights
            }
            else if (_player._timeSinceDamage < SimTicks.From30HzFrames(6))
            {
                if (!_healthbarChangedColor)
                {
                    _healthbarPalette = 2;
                    _healthbarChangedColor = true;
                }
            }
            else if (_healthbarChangedColor)
            {
                _healthbarPalette = 0;
                _healthbarChangedColor = false;
            }

            float targetOffsetY = _hudObjects.HealthOffsetY;
            if (_player.IsAltForm || _player.IsMorphing)
            {
                targetOffsetY += _hudObjects.HealthOffsetYAlt;
            }

            if (_healthbarYOffset > targetOffsetY)
            {
                _healthbarYOffset -= 1 / 2f; // todo: FPS stuff
            }
            else if (_healthbarYOffset < targetOffsetY)
            {
                _healthbarYOffset += 1 / 2f; // todo: FPS stuff
            }
        }

        private int _ammoBarPalette = 0;
        private bool _ammoBarChangedColor = false;
        public void UpdateAmmoBar()
        {
            // todo?:
            // - use the other palettes for low ammo warning and danger?
            // - the bar flashes when picking up UA w/ missiles equipped and vice versa
            // - the bar doesn't flash when ammo is restored by the affinity weapon pickup
            if (_player._timeSincePickup < SimTicks.From30HzFrames(10))
            {
                if (!_ammoBarChangedColor)
                {
                    _ammoBarPalette = 1;
                    _ammoBarChangedColor = true;
                }
            // todo?: update radar lights
            }
            else if (_ammoBarChangedColor)
            {
                _ammoBarPalette = 0;
                _ammoBarChangedColor = false;
            }
        }

        private float _boostBombsYOffset = 0;
        public void UpdateBoostBombs()
        {
            float targetOffsetY = 208;
            if (_player.IsAltForm || _player.IsMorphing)
            {
                targetOffsetY = 160;
            }

            if (_boostBombsYOffset > targetOffsetY)
            {
                _boostBombsYOffset -= 2 / 2f; // todo: FPS stuff
            }
            else if (_boostBombsYOffset < targetOffsetY)
            {
                _boostBombsYOffset += 2 / 2f; // todo: FPS stuff
            }
        }

        private int _hudPreviousWeaponSelection = -1;
        public void UpdateWeaponSelect()
        {
            BeamType previousWeapon = _player.WeaponSelection;
            int selection = -1;
            float x = _mouseState?.X ?? 0;
            float y = _mouseState?.Y ?? 0;
            float ratioX = Presentation.Size.X / 256f;
            float ratioY = Presentation.Size.Y / 192f;
            float distX = 224 * ratioX - x; // todo: invert for left-handed mode
            float distY = y - 38 * ratioY;
            if (distX > 0 && distY > 0 && distX * distX + distY * distY > 20 * ratioY * 20 * ratioY)
            {
                float div = distX / distY;
                if (div >= Fixed.ToFloat(1060) * ratioX / (Fixed.ToFloat(3956) * ratioY))
                {
                    if (div >= Fixed.ToFloat(2048) * ratioX / (Fixed.ToFloat(3547) * ratioY))
                    {
                        if (div >= Fixed.ToFloat(2896) * ratioX / (Fixed.ToFloat(2896) * ratioY))
                        {
                            if (div >= Fixed.ToFloat(3547) * ratioX / (Fixed.ToFloat(2048) * ratioY))
                            {
                                if (div >= Fixed.ToFloat(3956) * ratioX / (Fixed.ToFloat(1060) * ratioY))
                                {
                                    if (_player._availableWeapons[BeamType.ShockCoil])
                                    {
                                        selection = 5;
                                        _player.WeaponSelection = BeamType.ShockCoil;
                                    }
                                }
                                else if (_player._availableWeapons[BeamType.Magmaul])
                                {
                                    selection = 4;
                                    _player.WeaponSelection = BeamType.Magmaul;
                                }
                            }
                            else if (_player._availableWeapons[BeamType.Judicator])
                            {
                                selection = 3;
                                _player.WeaponSelection = BeamType.Judicator;
                            }
                        }
                        else if (_player._availableWeapons[BeamType.Imperialist])
                        {
                            selection = 2;
                            _player.WeaponSelection = BeamType.Imperialist;
                        }
                    }
                    else if (_player._availableWeapons[BeamType.Battlehammer])
                    {
                        selection = 1;
                        _player.WeaponSelection = BeamType.Battlehammer;
                    }
                }
                else if (_player._availableWeapons[BeamType.VoltDriver])
                {
                    selection = 0;
                    _player.WeaponSelection = BeamType.VoltDriver;
                }
            }

            for (int i = 0; i < 6; i++)
            {
                HudObjectInstance weaponInst = _weaponSelectInsts[i];
                bool available = _player._availableWeapons[weaponInst.CurrentFrame];
                weaponInst.Enabled = available;
                HudObjectInstance boxInst = _selectBoxInsts[i];
                boxInst.SetIndex(available ? (i == selection ? 2 : 1) : 0, _player._scene);
            }

            if (selection != _hudPreviousWeaponSelection)
            {
                _player._soundSource.PlayFreeSfx(SfxId.HUD_WEAPON_SWITCH2);
                _hudPreviousWeaponSelection = selection;
            }
        }

        public void UpdateDamageIndicators()
        {
            for (int i = 0; i < 8; i++)
            {
                ushort time = _damageIndicatorTimers[i];
                if (time > 0)
                {
                    time--;
                    _damageIndicatorTimers[i] = time;
                }

                _damageIndicatorNodes[i].Enabled = (time & (4 * 2)) != 0; // todo: FPS stuff
            }
        }

        private bool _smallReticle = false;
        private ushort _smallReticleTimer = 0;
        private bool _sniperReticle = false;
        private bool _hudZoom = false;
        public void HudOnFiredShot()
        {
            if (Features.FixedCrosshair)
            {
                return;
            }

            if (!_smallReticle && !_sniperReticle)
            {
                _smallReticle = true;
                _targetCircleInst.SetAnimation(start: 0, target: 3, frames: 4);
            }

            _smallReticleTimer = (ushort)SimTicks.From30HzFrames(60);
        }

        public void ResetReticle()
        {
            _targetCircleInst.SetCharacterData(_targetCircleObj.CharacterData, _targetCircleObj.Width, _targetCircleObj.Height, _player._scene);
            _smallReticle = false;
            _smallReticleTimer = 0;
        }

        public void UpdateReticle()
        {
            if (_smallReticleTimer > 0 && !_sniperReticle)
            {
                _smallReticleTimer--;
                if (_smallReticleTimer == 0 && _smallReticle)
                {
                    // the game's animation for this gets stuck at full contraction for 4 frames,
                    // then has one frame of starting to expand, and then jumps to fully expanded
                    _targetCircleInst.SetAnimation(start: 3, target: 0, frames: 4);
                    _smallReticle = false;
                }
            }

            if (Features.FixedCrosshair)
            {
                // Screen-dead-centre, not reprojected from _aimPosition: aim
                // and camera facing are smoothed at different rates (see
                // UpdateAimVecs), so the reprojected point visibly drifts
                // off-centre on its own even with the fire animation off.
                // Quake's crosshair doesn't do that.
                _targetCircleInst.PositionX = 0.5f;
                _targetCircleInst.PositionY = 0.5f;
            }
            else
            {
                Matrix.ProjectPosition(_player._aimPosition, Presentation.ViewMatrix, Presentation.PerspectiveMatrix, out Vector2 pos);
                _targetCircleInst.PositionX = MathF.Round(pos.X, 5);
                _targetCircleInst.PositionY = MathF.Round(pos.Y, 5);
            }

            _targetCircleInst.Enabled = true;
            _targetCircleInst.ProcessAnimation(_player._scene);
        }

        public Vector3 GetCrosshairColor()
        {
            if (_player.Health > 60)
            {
                return new Vector3(0, 1, 0);
            }

            if (_player.Health > 33)
            {
                return new Vector3(1, 0.65f, 0);
            }

            return new Vector3(1, 0, 0);
        }

        public void HudOnMorphStart()
        {
            _targetCircleInst.SetIndex(0, _player._scene);
        }

        public void HudOnWeaponSwitch(BeamType beam)
        {
            if (beam != BeamType.Imperialist || _sniperReticle)
            {
                _sniperReticle = false;
                ResetReticle();
            }
            else
            {
                _sniperReticle = true;
                _targetCircleInst.SetCharacterData(_sniperCircleObj.CharacterData, _sniperCircleObj.Width, _sniperCircleObj.Height, _player._scene);
            }

            _weaponIconInst.SetAnimation(start: 9, target: 27, frames: 19, afterAnim: (int)beam);
        }

        public void HudOnZoom(bool zoom)
        {
            if (_hudZoom != zoom)
            {
                _hudZoom = zoom;
                if (_hudZoom)
                {
                    _targetCircleInst.SetAnimation(start: 0, target: 2, frames: 2);
                }
                else
                {
                    _targetCircleInst.SetAnimation(start: 2, target: 0, frames: 2);
                }
            }
        }

        public byte HudDisruptedState { get; private set; } = 0;
        public float HudDisruptionFactor { get; private set; } = 0;

        private ushort _hudDisruptedTimer = 0;
        public void HudOnDisrupted()
        {
            if (CameraSequence.Current == null)
            {
                HudDisruptedState = 1;
                _hudDisruptedTimer = _player._disruptedTimer;
            }
        }

        public void HudEndDisrupted()
        {
            if (HudDisruptedState != 0)
            {
                HudDisruptedState = 0;
                _hudDisruptedTimer = 0;
                HudDisruptionFactor = 0;
            }
        }

        public void UpdateDisruptedState()
        {
            if (HudDisruptedState == 1)
            {
                HudDisruptionFactor += 0.25f / 2; // todo: FPS stuff
                if (HudDisruptionFactor >= 1)
                {
                    HudDisruptionFactor = 1;
                    HudDisruptedState = 2;
                }
            }
            else if (HudDisruptedState == 2)
            {
                if (--_hudDisruptedTimer == 0)
                {
                    HudDisruptedState = 3;
                }
            }
            else if (HudDisruptedState == 3)
            {
                HudDisruptionFactor -= 0.125f / 2; // todo: FPS stuff
                if (HudDisruptionFactor <= 0)
                {
                    HudDisruptionFactor = 0;
                    HudDisruptedState = 0;
                    _hudDisruptedTimer = (ushort)SimTicks.From30HzFrames(32);
                }
            }
            else if (HudDisruptedState != 0)
            {
                if (--_hudDisruptedTimer == 0)
                {
                    HudDisruptedState = 1;
                }
            }
        }

        public int HudWhiteoutState { get; private set; } = -1;
        public float HudWhiteoutFactor { get; private set; } = 0;

        private float _whiteoutAmount = 0;
        private float _whiteoutTime = 0;
        public void BeginWhiteout()
        {
            HudWhiteoutState = 0;
            HudWhiteoutFactor = 0;
            _whiteoutTime = _player._scene.GlobalElapsedTime;
            UpdateWhiteoutTable(value: 0);
        }

        public void EndWhiteout()
        {
            HudWhiteoutState = -1;
            HudWhiteoutFactor = 0;
        }

        public void UpdateWhiteoutState()
        {
            float GetPosition()
            {
                float time = (_player._scene.GlobalElapsedTime - _whiteoutTime) * 60;
                float timeSquared = time * time;
                float timeCubed = timeSquared * time;
                float jerk = 0.004f;
                float initAcceleration = 0;
                float initVelocity = 1;
                float initPosition = 0;
                // the game has limits on the acceleration and velocity, but the limits on
                // first the factor and then the position are reached before those
                Debug.Assert(initAcceleration + jerk * time < 120); // acceleration
                Debug.Assert(initVelocity + initAcceleration * time + 0.5f * jerk * timeSquared < 480); // velocity
                return initPosition + initVelocity * time + 0.5f * initAcceleration * timeSquared + 1 / 6f * jerk * timeCubed;
            }

            if (HudWhiteoutState == 0)
            {
                HudWhiteoutFactor += 2.8125f * _player._scene.FrameTime;
                if (HudWhiteoutFactor >= 1)
                {
                    HudWhiteoutFactor = 1;
                    HudWhiteoutState = 1;
                }

                _whiteoutAmount = GetPosition();
                Debug.Assert(_whiteoutAmount < 96);
            }
            else if (HudWhiteoutState == 1)
            {
                _whiteoutAmount = GetPosition();
                if (_whiteoutAmount >= 96)
                {
                    _whiteoutAmount = 96;
                    HudWhiteoutState = 2;
                    _whiteoutTime = _player._scene.GlobalElapsedTime;
                }

                UpdateWhiteoutTable(_whiteoutAmount);
            }
            else if (HudWhiteoutState == 2)
            {
                HudWhiteoutFactor = -1; // use the table value directly instead of as a factor
                float time = _player._scene.GlobalElapsedTime - _whiteoutTime;
                float value = 1 - Math.Min(time / (16 / (float)SimTicks.LegacyHz), 1);
                Array.Fill(HudWhiteoutTable, value);
            }
        }

        public static readonly float[] HudWhiteoutTable = new float[192];
        public void UpdateWhiteoutTable(float value)
        {
            int trunc = (int)value;
            float amount = 0.9975f;
            for (int i = 95; i >= 0; i--)
            {
                int index = i - trunc;
                if (index >= 0)
                {
                    Debug.Assert(index <= 95);
                    float factor = (MathF.Pow(amount, 8) * 32 - 16) / 16f;
                    Debug.Assert(factor >= -1 && factor <= 1);
                    HudWhiteoutTable[index] = factor;
                    HudWhiteoutTable[191 - index] = factor;
                }

                amount -= 0.0105f;
            }

            for (int j = 95; j >= 95 - trunc && j >= 0; j--)
            {
                HudWhiteoutTable[j] = 16;
                HudWhiteoutTable[191 - j] = 16;
            }
        }

        public void DrawHudObjects()
        {
            if (Mods.ThumbnailMode.Active)
            {
                return;
            }

            // Before the pause check, not after it: a frame rate you cannot
            // read while the settings window is open -- which is where the
            // switch for it is -- would be a switch with no feedback.
            if (Mods.RenderOptions.ShowFps)
            {
                DrawFps();
            }

            // Before the pause and spectator checks, like the counter above
            // it and for the same reason: somebody watching a match is in the
            // one position where reading what the players are saying is most
            // of the point, and a message that arrives while the scoreboard
            // is up has still arrived.
            ModDrawChat();
            ModDrawCombatFeedback();
            if (Mods.SpectatorMode.WaitingForNextMatch)
                DrawText2D(128, 32, Align.Center, 0, "SPECTATING - NEXT MATCH", new ColorRgba(0x3FEF), scale: .75f);
            DrawWeaponRadial();
            DrawNetworkHealth();
            if (DrawPostMatchResults()) return;
            if (_player._scene.Match.Phase == MatchPhase.WaitingForPlayers)
            {
                DrawText2D(128, 40, Align.Center, 0, "WAITING FOR PLAYERS", new ColorRgba(0x3FEF), fontSpacing: 8);
            }
            else if (_player._scene.Match.Phase == MatchPhase.Countdown)
            {
                uint tick = Mods.Network.AuthoritativePlay.Current?.WorldServerTick ?? Mods.Network.DemoPlayback.WorldServerTick ?? _player._scene.Match.PhaseStartTick;
                int remaining = Math.Max(0, unchecked((int)(_player._scene.Match.PhaseEndTick - tick)));
                int seconds = remaining / 60 + (remaining % 60 == 0 ? 0 : 1);
                DrawText2D(128, 40, Align.Center, 0, $"STARTING IN {seconds}", new ColorRgba(0x3FEF), fontSpacing: 8);
            }

            if (Mods.SpectatorMode.FreeCamera)
            {
                // Looking at the map, not out of anybody's eyes: there is no
                // player whose readouts these would be. Following one is the
                // other case and keeps their HUD -- watching a hunter play
                // without seeing what they are playing with tells you very
                // little, and it is what watching a recording back has always
                // shown.
                //
                // The scoreboard is the exception, because it is the match's
                // and not a player's: it is what somebody watching from the
                // map is most likely to want, and holding the button for it
                // still answers here.
                if (ShowScoreboard)
                {
                    DrawMatchTime();
                    DrawScoreboard();
                }

                return;
            }

            if (_player._scene.Match.LegacyState == MatchState.GameOver)
            {
                string text = Strings.GetHudMessage(219); // GAME OVER
                DrawText2D(128, 40, Align.Center, 0, text, new ColorRgba(0x3FEF), fontSpacing: 8);
            }
            else if (_player._scene.Match.LegacyState == MatchState.Ending)
            {
                DrawScoreboard();
            }
            else if (CameraSequence.Current?.Flags.TestFlag(CamSeqFlags.BlockInput) == true)
            {
                return;
            }
            else if (CameraSequence.Current?.IsIntro == true)
            {
                DrawModeRules();
                DrawQueuedHudMessages();
                return;
            }
            else if (_player.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen))
            {
                for (int i = 0; i < 6; i++)
                {
                    Presentation.DrawHudObject(_selectBoxInsts[i], mode: 1);
                    Presentation.DrawHudObject(_weaponSelectInsts[i], mode: 1);
                }
            }
            else if (ShowScoreboard)
            {
                DrawMatchTime();
                DrawScoreboard();
            }
            else
            {
                if (_player.Health > 0)
                {
                    if (_player.IsAltForm || _player.IsMorphing || _player.IsUnmorphing)
                    {
                        DrawBoostBombs();
                    }
                    else
                    {
                        // The ammo meter and the weapon icon beside it are
                        // both drawn into the helmet's moulding, so Pro
                        // mode -- which has no helmet -- draws neither.
                        // What it puts in their place is in DrawProHud.
                        if (!Features.ProHud)
                        {
                            DrawAmmoBar();
                            _weaponIconInst.PositionX = (_hudObjects.WeaponIconPosX + _objShiftX) / 256f;
                            _weaponIconInst.PositionY = (_hudObjects.WeaponIconPosY + _objShiftY) / 192f;
                            _weaponIconInst.Alpha = Features.HudOpacity;
                            Presentation.DrawHudObject(_weaponIconInst);
                        }

                        if (Features.CustomCrosshair)
                        {
                            Presentation.DrawCustomCrosshair(GetCrosshairColor());
                        }
                        else
                        {
                            _targetCircleInst.Alpha = Features.ReticleOpacity;
                            Presentation.DrawHudObject(_targetCircleInst);
                        }

                        if (Features.ModernHud)
                        {
                            DrawWeaponList();
                        }
                    }

                    DrawModeHud();
                    DrawDoubleDamageHud();
                    DrawCloakHud();
                    if (Features.ProHud)
                    {
                        DrawProHud();
                    }
                    else
                    {
                        DrawHealthbars();
                    }
                }

                DrawQueuedHudMessages();
            }
        }

        public void DrawHudModels()
        {
            if (Mods.ThumbnailMode.Active)
            {
                return;
            }

            if (Mods.SpectatorMode.FreeCamera)
            {
                // The other half of the HUD -- the locator icons and the
                // screen filter. See DrawHudObjects, including why the
                // scoreboard is still drawn, which is what this dims for.
                if (ShowScoreboard)
                {
                    Presentation.DrawHudFilterModel(_filterModel);
                }

                return;
            }

            if (CameraSequence.Current?.IsIntro == true)
            {
                Presentation.DrawHudFilterModel(_filterModel, alpha: 15 / 31f);
            }
            else if (_player._scene.Match.LegacyState == MatchState.GameOver)
            {
                Presentation.DrawHudFilterModel(_filterModel, alpha: 12 / 31f);
            }
            else if (_player.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen) || ShowScoreboard || _player._scene.Match.LegacyState == MatchState.Ending)
            {
                Presentation.DrawHudFilterModel(_filterModel);
            }
            else
            {
                if (_player._health > 0)
                {
                    DrawLocatorIcons();
                }

                if (_damageIndicator.Active)
                {
                    Presentation.DrawHudDamageModel(_damageIndicator);
                }
            }
        }

        private struct LocatorInfo
        {
            public Vector3 Position;
            public ModelInstance Model;
            public ColorRgb Color;
            public float Alpha;
            public LocatorInfo(Vector3 position, ModelInstance model, ColorRgb color, float alpha)
            {
                Position = position;
                Model = model;
                Color = color;
                Alpha = alpha;
            }
        }

        private readonly List<LocatorInfo> _locatorInfo = new List<LocatorInfo>(15);
        public void AddLocatorInfo(Vector3 position, ModelInstance inst, ColorRgb color, float alpha = 1, int team = -1)
        {
            _locatorInfo.Add(new LocatorInfo(position, inst, color, alpha));
            // This sink receives only contacts admitted by the existing mode
            // policy below. Enhanced drawing never searches additional entities.
            var type = inst == _playerLocator ? Hud.Radar.RadarContactType.Enemy : Hud.Radar.RadarContactType.Objective;
            var objective = inst == _playerLocator ? Hud.Radar.RadarObjective.None
                : inst == _octolithLocator ? Hud.Radar.RadarObjective.Flag
                : _player._scene.Match.Rules.Mode is MatchMode.Nodes or MatchMode.TeamNodes ? Hud.Radar.RadarObjective.Node
                : _player._scene.Match.Rules.Mode is MatchMode.Defender or MatchMode.TeamDefender ? Hud.Radar.RadarObjective.Defender
                : Hud.Radar.RadarObjective.Base;
            if (inst == _playerLocator && _player._scene.Match.Rules.Mode == MatchMode.PrimeHunter)
                type = Hud.Radar.RadarContactType.PrimeHunter;
            _radarFrame.AddApproved(new(type, position, team, objective, alpha));
        }

        public void DrawLocatorIcons()
        {
            if (Hud.Radar.RadarSettings.Style == Hud.Radar.RadarStyle.Enhanced)
            {
                DrawEnhancedRadar();
                return;
            }
            for (int i = 0; i < _locatorInfo.Count; i++)
            {
                LocatorInfo info = _locatorInfo[i];
                DrawLocatorIcon(info.Position, info.Model, info.Color, info.Alpha);
            }
        }

        public void DrawLocatorIcon(Vector3 position, ModelInstance inst, ColorRgb color, float alpha)
        {
            float W(float value)
            {
                return value / 256f * Presentation.Size.X;
            }

            float H(float value)
            {
                return value / 192f * Presentation.Size.Y;
            }

            float x;
            float y;
            bool behind = false;
            Vector2 proj = Vector2.Zero;
            Vector3 mult = Matrix.Vec3MultMtx4(position, Presentation.ViewMatrix);
            if (mult.Z < -1)
            {
                Matrix.ProjectPosition(position, Presentation.ViewMatrix, Presentation.PerspectiveMatrix, out proj);
                x = proj.X * Presentation.Size.X - W(128);
                y = proj.Y * Presentation.Size.Y - H(106);
            }
            else
            {
                x = W(mult.X);
                y = -H(mult.Y);
                behind = true;
            }

            float absX = MathF.Abs(x);
            float absY = MathF.Abs(y);
            if (behind || absX > W(100) || absY > H(60))
            {
                if (absY >= 1 / 4096f)
                {
                    float v15 = absX + MathF.Truncate((H(60) - absY) * absX / absY);
                    if (v15 > W(100))
                    {
                        float v17 = absY + MathF.Truncate((W(100) - absX) * absY / absX);
                        if (x <= 0)
                        {
                            proj.X = W(28);
                        }
                        else
                        {
                            proj.X = W(228);
                        }

                        if (y <= 0)
                        {
                            proj.Y = H(106) - v17;
                        }
                        else
                        {
                            proj.Y = v17 + H(106);
                        }
                    }
                    else
                    {
                        if (x <= 0)
                        {
                            proj.X = W(128) - v15;
                        }
                        else
                        {
                            proj.X = v15 + W(128);
                        }

                        if (y <= 0)
                        {
                            proj.Y = H(46);
                        }
                        else
                        {
                            proj.Y = H(166);
                        }
                    }
                }
                else if (x <= 0)
                {
                    proj.X = W(28);
                }
                else
                {
                    proj.X = W(228);
                }

                proj.X /= Presentation.Size.X;
                proj.Y /= Presentation.Size.Y;
                float angle = MathHelper.RadiansToDegrees(MathF.Atan2(-y, x));
                Presentation.DrawIconModel(proj, angle, _arrowLocator, color, alpha);
            }
            else
            {
                Presentation.DrawIconModel(proj, angle: 0, inst, color, alpha);
            }
        }

        public string FormatTime(TimeSpan time)
        {
            return $"{time.Hours * 60 + time.Minutes}:{time.Seconds:00}";
        }

        public void DrawMatchTime()
        {
            if (_player._scene.Match.MatchTime < 0)
            {
                return;
            }

            var time = TimeSpan.FromSeconds(_player._scene.Match.MatchTime);
            int palette = time.TotalSeconds < 10 ? 2 : 0;
            float posY = 10;
            string text = Strings.GetHudMessage(5); // TIME
            DrawText2D(128, posY, Align.Center, palette, text);
            text = FormatTime(time);
            DrawText2D(128, posY + 10, Align.Center, palette, text);
        }

        private const float _scoreStartSpace = 13;
        private const float _scoreTeamHeaderSpace = 4;
        private const float _scoreTeamLineSpace = 18;
        private const float _scorePlayerSpace = 28;
        /// <summary>Tightest a row can get before the hunter icons collide.</summary>
        private const float _scoreMinPlayerSpace = 19;
        public float GetScoreboardRowSpace()
        {
            int rows = _player._scene.Match.ActivePlayers;
            if (rows <= 4)
            {
                return _scorePlayerSpace;
            }

            float available = 168 - _scoreStartSpace;
            if (_player._scene.Match.LegacyState == MatchState.Ending)
            {
                available -= _scoreStartSpace;
            }

            if (_player._scene.Match.Rules.Teams)
            {
                available -= 2 * _scoreTeamLineSpace;
            }

            return Math.Clamp(available / rows, _scoreMinPlayerSpace, _scorePlayerSpace);
        }

        public float GetScoreboardHeight()
        {
            float rowSpace = GetScoreboardRowSpace();
            float height = _scoreStartSpace;
            if (_player._scene.Match.LegacyState == MatchState.Ending)
            {
                height *= 2;
            }

            int curTeam = 4;
            for (int i = 0; i < _player._scene.Match.ActivePlayers; i++)
            {
                PlayerEntity player = PlayerEntity.Players[_player._scene.Match.ResultSlots[i]];
                if (!player.LoadFlags.TestFlag(LoadFlags.Active))
                {
                    continue;
                }

                if (_player._scene.Match.Rules.Teams && player.TeamIndex != curTeam)
                {
                    if (curTeam != 4)
                    {
                        height -= _scoreTeamHeaderSpace;
                    }

                    height += _scoreTeamLineSpace;
                    curTeam = player.TeamIndex;
                }

                height += rowSpace;
            }

            return height;
        }

        public static float Lerp(float first, float second, float by)
        {
            return first * (1 - by) + second * by;
        }

        public void DrawScoreboard()
        {
            MatchMode mode = _player._scene.Match.Rules.Mode;
            float rowSpace = GetScoreboardRowSpace();
            float posY = 104 - GetScoreboardHeight() / 2;
            if (_player._scene.Match.LegacyState == MatchState.Ending)
            {
                string text = Strings.GetHudMessage(219); // GAME OVER
                DrawText2D(128, posY, Align.Center, 0, text, new ColorRgba(0x53F4), fontSpacing: 8);
                posY += _scoreStartSpace;
            }

            string header1 = "";
            string header2 = "";
            if (mode == MatchMode.Battle || mode == MatchMode.TeamBattle || mode == MatchMode.Nodes || mode == MatchMode.TeamNodes)
            {
                header1 = Strings.GetHudMessage(225); // points
            }
            else if (mode == MatchMode.Capture || mode == MatchMode.Bounty || mode == MatchMode.TeamBounty)
            {
                header1 = Strings.GetHudMessage(227); // octoliths
            }
            else // survival/teams, defender/teams, prime hunter
            {
                header1 = Strings.GetHudMessage(224); // time
            }

            if (mode == MatchMode.Battle || mode == MatchMode.TeamBattle || mode == MatchMode.Survival || mode == MatchMode.TeamSurvival)
            {
                header2 = Strings.GetHudMessage(223); // deaths
            }
            else // capture, bounty/teams, defender/teams, nodes/teams, prime hunter
            {
                header2 = Strings.GetHudMessage(220); // kills
            }

            DrawText2D(ModScoreColumn1, posY, Align.Center, 0, header1, new ColorRgba(0x3FEF), fontSpacing: 8);
            DrawText2D(ModScoreColumn2, posY, Align.Center, 0, header2, new ColorRgba(0x3FEF), fontSpacing: 8);
            ModDrawPingHeader(posY);
            posY += _scoreStartSpace;
            string maxText = Strings.GetHudMessage(256); // MAX
            string ChooseValue1(float time, int points)
            {
                if (mode == MatchMode.Survival || mode == MatchMode.TeamSurvival || mode == MatchMode.Defender || mode == MatchMode.TeamDefender || mode == MatchMode.PrimeHunter)
                {
                    // time should only be -1 to indicate max in survival/teams
                    return time < 0 ? maxText : FormatTime(TimeSpan.FromSeconds(time));
                }

                // battle/teams, capture, bounty/teams, nodes/teams
                return points.ToString();
            }

            string ChooseValue2(int deaths, int kills)
            {
                if (mode == MatchMode.Survival || mode == MatchMode.TeamSurvival || mode == MatchMode.Battle || mode == MatchMode.TeamBattle)
                {
                    return deaths.ToString();
                }

                // capture, bounty/teams, defender/teams, nodes/teams, prime hunter
                return kills.ToString();
            }

            string teamText = Strings.GetHudMessage(222); // team
            int curTeam = 4;
            for (int i = 0; i < _player._scene.Match.ActivePlayers; i++)
            {
                int slot = _player._scene.Match.ResultSlots[i];
                PlayerEntity player = PlayerEntity.Players[slot];
                if (!player.LoadFlags.TestFlag(LoadFlags.Active))
                {
                    continue;
                }

                if (_player._scene.Match.Rules.Teams && player.TeamIndex != curTeam)
                {
                    if (curTeam != 4)
                    {
                        posY -= _scoreTeamHeaderSpace;
                    }

                    curTeam = player.TeamIndex;
                    string teamValue1 = ChooseValue1(_player._scene.Match.TeamTime[curTeam], _player._scene.Match.TeamPoints[curTeam]);
                    string teamValue2 = ChooseValue2(_player._scene.Match.TeamDeaths[curTeam], _player._scene.Match.TeamKills[curTeam]);
                    var teamColor = new ColorRgba(player.Team == Team.Orange ? 0x23Fu : 0x2BEAu);
                    string teamName = $"{teamText} {player.TeamIndex + 1}";
                    DrawText2D(42, posY, Align.Center, 0, teamName, teamColor, fontSpacing: 8);
                    DrawText2D(ModScoreColumn1, posY, Align.Center, 0, teamValue1, teamColor, fontSpacing: 8);
                    DrawText2D(ModScoreColumn2, posY, Align.Center, 0, teamValue2, teamColor, fontSpacing: 8);
                    posY += _scoreTeamLineSpace;
                }

                string value1 = ChooseValue1(_player._scene.Match.Players[slot].Time, _player._scene.Match.Players[slot].Points);
                string value2 = ChooseValue2(_player._scene.Match.Players[slot].Deaths, _player._scene.Match.Players[slot].Kills);
                var color = new ColorRgba(0x7DEF);
                if (player.IsMainPlayer)
                {
                    float rg;
                    float pct = _player._scene.ElapsedTime / (32 / (float)SimTicks.LegacyHz) % 1;
                    if (pct <= 0.5f)
                    {
                        rg = Lerp(0, 1, pct * 2);
                    }
                    else
                    {
                        rg = Lerp(1, 0, (pct - 0.5f) * 2);
                    }

                    color = new ColorRgba((byte)(rg * 255), (byte)(rg * 255), 255, 255);
                }

                DrawScoreboardPlayer(60, posY, color, _hunterInsts[(int)player.Hunter], slot);
                DrawText2D(ModScoreColumn1, posY, Align.Center, 0, value1, color, fontSpacing: 8);
                DrawText2D(ModScoreColumn2, posY, Align.Center, 0, value2, color, fontSpacing: 8);
                ModDrawPingRow(posY, color, slot);
                posY += rowSpace;
            }
        }

        public void DrawScoreboardPlayer(float posX, float posY, ColorRgba color, HudObjectInstance hunter, int slot)
        {
            hunter.PositionX = (posX - 40) / 256f;
            hunter.PositionY = (posY - 13) / 192f;
            Presentation.DrawHudObject(hunter, mode: 2);
            int stars = _player._scene.Match.Players[slot].Stars;
            _starsInst.PositionX = posX / 256f;
            _starsInst.PositionY = posY / 192f;
            _starsInst.SetIndex(stars * 2, _player._scene);
            Presentation.DrawHudObject(_starsInst, mode: 2);
            _starsInst.PositionX = (posX + 32) / 256f;
            _starsInst.SetIndex(stars * 2 + 1, _player._scene);
            Presentation.DrawHudObject(_starsInst, mode: 2);
            string nickname = GameState.Nicknames[slot];
            DrawText2D(posX + 32, posY - 9, Align.Center, 0, nickname, color, fontSpacing: 8);
        }

        public void DrawHealthbars()
        {
            _healthbarMainMeter.TankAmount = _player.Values.EnergyTank;
            _healthbarMainMeter.TankCount = _player._healthMax / _player.Values.EnergyTank;
            DrawMeter(_hudObjects.HealthMainPosX + _objShiftX, _hudObjects.HealthMainPosY + _healthbarYOffset + _objShiftY, _player.Values.EnergyTank - 1, _player._health, _healthbarPalette, _healthbarMainMeter, drawText: true, drawTanks: false, Features.HudOpacity);
            int amount = 0;
            if (_player._health >= _player.Values.EnergyTank)
            {
                amount = _player._health - _player.Values.EnergyTank;
            }

            _healthbarSubMeter.TankAmount = _player.Values.EnergyTank;
            _healthbarSubMeter.TankCount = _player._healthMax / _player.Values.EnergyTank;
            DrawMeter(_hudObjects.HealthSubPosX + _objShiftX, _hudObjects.HealthSubPosY + _healthbarYOffset + _objShiftY, _player.Values.EnergyTank - 1, amount, _healthbarPalette, _healthbarSubMeter, drawText: false, drawTanks: false, Features.HudOpacity);
        }

        public void DrawAmmoBar()
        {
            WeaponInfo info = _player.EquipInfo.Weapon;
            if (info.AmmoCost == 0 || !_ammoBarMeter.BarInst.Enabled)
            {
                return;
            }

            _ammoBarMeter.TankAmount = _player._ammoMax[info.AmmoType] + 1;
            _ammoBarMeter.TankCount = 0;
            int amount = _player._ammo[info.AmmoType];
            DrawMeter(_hudObjects.AmmoBarPosX + _objShiftX, _hudObjects.AmmoBarPosY + _objShiftY, amount, amount, _ammoBarPalette, _ammoBarMeter, drawText: false, drawTanks: false, Features.HudOpacity);
            amount /= info.AmmoCost;
            DrawText2D(_hudObjects.AmmoBarPosX + _ammoBarMeter.BarOffsetX + _objShiftX, _hudObjects.AmmoBarPosY + _ammoBarMeter.BarOffsetY + _objShiftY, _ammoBarMeter.Align, _ammoBarPalette, $"{amount:00}", alpha: Features.HudOpacity);
        }

        /// <summary>
        /// "Modern HUD": every weapon the player has picked up, with its
        /// ammo, listed down the right edge -- the equipped one boxed. Uses
        /// the same weapon-icon sheet and frame convention as the equipped-
        /// weapon icon (<see cref = "HudOnWeaponSwitch"/>), and the same ammo
        /// read <see cref = "DrawAmmoBar"/> uses for the current weapon, just
        /// for every owned weapon instead of only the equipped one.
        /// </summary>
        /// <summary>
        /// Down the left, under the score, the way Quake's is: one row per
        /// weapon you are carrying, its own colour art on the left and its
        /// ammo beside it, the equipped one lit and the rest dimmed.
        ///
        /// It used to sit hard against the right edge, over the visor frame
        /// and under the ammo readout, drawn in the HUD's monochrome green --
        /// which is a list you have to look *for*, and then read shape by
        /// shape. The two things that make Quake's work are that it is in one
        /// clear column with nothing else in it, and that each weapon is a
        /// different colour, so you find one by its colour instead of reading
        /// the whole list. Both are copied here; the art is the game's own
        /// (the touchscreen weapon menu's sheet, see SetUpHud).
        ///
        /// Every coordinate is in the DS's 256x192 HUD space, which
        /// DrawHudObject mode 2 and DrawText2D both scale to the window.
        /// </summary>
        /// <summary>
        /// How many horizontal HUD units make up one vertical one -- 1 on a
        /// 4:3 window, less than 1 on anything wider.
        ///
        /// Everything in the HUD is laid out in the DS's 256x192 space, which
        /// is 4:3, and reaches the window as x / 256 * width and
        /// y / 192 * height. Those are the same number of pixels per unit only
        /// on a 4:3 screen; on a wider one a unit is further across than it is
        /// down, so anything measured in units comes out stretched sideways
        /// and anything placed by them drifts. Multiplying a horizontal
        /// measurement by this gives a distance that is the same number of
        /// pixels on any window. <see cref = "DrawText2D"/> already does exactly
        /// this to its glyph advances.
        /// </summary>
        private float HudAspectFix
        {
            get
            {
                if (Presentation.Size.X <= 0 || Presentation.Size.Y <= 0)
                {
                    return 1;
                }

                return Presentation.Size.Y / 192f * (256f / Presentation.Size.X);
            }
        }

        /// <summary>
        /// One colour per weapon for the list, indexed by <see cref = "BeamType"/>.
        ///
        /// Not the game's own weapon table, which is what this used first.
        /// <c>WeaponInfo.Colors[0]</c> is the colour of the *shot* -- what the
        /// projectile is tinted with in mid-air -- and several of those are
        /// nothing like the colour the weapon is known by: it came out with a
        /// white Volt Driver and a purple Judicator.
        ///
        /// These are the values the melonPrimeDS weapon overlay uses, read out
        /// of its SVGs (res/assets/weapons), which is the reference the colours
        /// were asked to match. Only the numbers are here -- nine RGB triples,
        /// which are facts about which colour goes with which gun. None of
        /// that project's art is copied or shipped, which is also what
        /// tools/check-no-game-assets.sh would refuse.
        /// </summary>
        private static readonly ColorRgba[] _weaponListColors =
        {
            new ColorRgba(0xF8, 0x28, 0x28, 255), // Power Beam
            new ColorRgba(0xF8, 0xF8, 0x08, 255), // Volt Driver
            new ColorRgba(0xF8, 0x28, 0x28, 255), // Missile
            new ColorRgba(0x20, 0xC0, 0x20, 255), // Battlehammer
            new ColorRgba(0xD0, 0x18, 0x18, 255), // Imperialist
            new ColorRgba(0x98, 0x38, 0xC0, 255), // Judicator
            new ColorRgba(0xF8, 0xB0, 0x18, 255), // Magmaul
            new ColorRgba(0x50, 0x98, 0xD0, 255), // Shock Coil
            new ColorRgba(0xD0, 0xD0, 0xD0, 255) // Omega Cannon
        };
        public void DrawWeaponList()
        {
            // Below the score block in the top-left corner, and short enough
            // that all nine weapons still fit above the bottom edge.
            // Quake's weapon bar, at Quake's size and in Quake's place.
            //
            // Measured off the reference rather than guessed: in that shot the
            // column starts hard against the left edge, each row is about 3%
            // of the screen's height and the whole panel about 7.5% of its
            // width -- which in the DS's 256x192 HUD space, where every
            // coordinate here lives, is a 6-unit row in a 20-unit panel. That
            // is a *lot* smaller than a HUD readout, and it is the point: the
            // bar is meant to be read out of the corner of the eye and take up
            // nothing.
            //
            // Scalable for the same reason as before -- eyesight and screen
            // size are not design decisions -- and every measurement below is
            // multiplied, so the panel keeps its proportions. Only the corner
            // it starts from stays put.
            float scale = Math.Clamp(Features.WeaponListScale, 0.6f, 2f);
            // Every width below is measured off the screen's *height*: left
            // uncorrected the panel was a different shape on every window --
            // at 21:9 its rows came out two thirds wider than they were
            // drawn, the icons in them stretched to match, and the ammo
            // count, right-aligned against a panel edge that had moved but
            // drawn by DrawText2D at its true shape, drifted further from its
            // icon the wider the window got. See HudAspectFix.
            float aspectFix = HudAspectFix;
            // Against the left edge, not inset. The reference has no margin
            // worth the name and the panel reads as part of the frame because
            // of it.
            float panelX = 2 * aspectFix;
            float rowHeight = 8f * scale;
            float panelWidth = 26f * scale * aspectFix;
            // The icon sits in a square block of the row's own height at the
            // left of it, and the count is right-aligned in what is left.
            // iconBox is that block's side in HUD *height* units; iconBoxX is
            // the same distance expressed across, which is what the panel's
            // own coordinates are in.
            float iconBox = rowHeight - 1f * scale;
            float iconBoxX = iconBox * aspectFix;
            float ammoRightX = panelX + panelWidth - 1.5f * scale * aspectFix;
            float y = 46;
            for (int i = 0; i < _weaponListIcons.Length; i++)
            {
                var beam = (BeamType)i;
                if (!_player.AvailableWeapons[beam])
                {
                    continue;
                }

                bool equipped = beam == _player.CurrentWeapon;
                WeaponInfo info = Weapons.Current[i];
                // The colour this weapon is known by. See _weaponListColors:
                // deliberately not the game's own beam colour, which is the
                // colour of the *shot* and is nothing like it for several.
                ColorRgba tint = _weaponListColors[i];
                float rowBottom = y + rowHeight - 1f * scale;
                Presentation.DrawHudFlatBox(panelX, y, panelX + panelWidth, rowBottom, equipped ? new Vector4(0.45f, 0.4f, 0.2f, 0.72f * Features.HudOpacity) : new Vector4(0, 0, 0, 0.42f * Features.HudOpacity));
                HudObjectInstance icon = _weaponListIcons[i];
                // The glyph itself in the weapon's colour, on nothing.
                //
                // SetData redraws the instance's texture with every pixel that
                // is not palette index 0 in a flat colour, and index 0 -- the
                // whole background of these frames -- left transparent. So the
                // icon is a coloured symbol over the row, not a coloured tile:
                // filling the square behind it instead, which is what this did
                // first, reads as a block of colour with something faint on it
                // rather than as an icon.
                //
                // Cheap to call every frame: it rebuilds the texture only when
                // the frame or the colour has actually changed, and neither
                // does after the first one.
                icon.SetData(i, tint, _player._scene);
                // Sized and placed on the *drawing*, not on the frame around
                // it. The select sheet's frames come off the touchscreen
                // weapon wheel, where each weapon sits wherever it sits on
                // that ring and several are much smaller than their frame, so
                // fitting frames to the boxes put every icon in a different
                // spot at a different size. _weaponListIconBounds is where the
                // ink actually is (see ModIconBounds); the larger of its two
                // sides fills the box, and its centre goes in the box's
                // centre, so nine icons drawn to nine different conventions
                // come out as one column.
                //
                // Mode 1, not mode 2: mode 2 maps the frame's width to the
                // window's width and its height to the window's height
                // independently, so every icon came out stretched sideways by
                // exactly as much as the window is wider than 4:3. Mode 1
                // sizes both from the height, so the frame keeps the shape it
                // was drawn in on any screen.
                IconBounds bounds = _weaponListIconBounds[i];
                // A margin inside the box, so the icons do not touch the row
                // above and below; and the box's own centre, not the row
                // pitch's, since the row is drawn one unit shorter than it.
                float iconFit = iconBox - 1f * scale;
                float iconScale = iconFit / Math.Max(bounds.Width, bounds.Height);
                icon.PositionX = (panelX + iconBoxX / 2 - bounds.CentreX * iconScale * aspectFix) / 256f;
                icon.PositionY = (y + iconBox / 2 - bounds.CentreY * iconScale) / 192f;
                // Never faded. A dimmed icon at this size is an empty box.
                icon.Alpha = Features.HudOpacity;
                Presentation.DrawHudObject(icon, mode: 1, scale: iconScale);
                // Shots, not the internal ammo pool.
                //
                // _ammo counts the currency a shot is *bought* with, and one
                // shot costs AmmoCost of it -- ten for a missile -- so the raw
                // value read 100 for the ten missiles the game starts you
                // with, and was wrong by that weapon's own factor for every
                // other one too. Same division the ammo readout beside the gun
                // does (see DrawAmmoBar), so the two now agree.
                //
                // The Power Beam costs no ammo, and -1 is the unlimited-ammo
                // marker single-player bots carry; a blank where every other
                // row has a number reads as "unknown" rather than as
                // "unlimited", so both say so.
                int ammoAmount = _player._ammo[info.AmmoType];
                string ammo = info.AmmoCost > 0 && ammoAmount >= 0 ? (ammoAmount / info.AmmoCost).ToString() : "--";
                // White, like the reference's: the colour is carried by the
                // icon block beside it, and a coloured number as well made
                // every row a different brightness to read.
                // Small. This font is drawn for the 16-unit readouts round
                // the screen edge; its digits are wide and its zero is a
                // filled slashed box, so at a row's size the count was reading
                // as the biggest thing in the bar rather than as a footnote to
                // the icon. Spacing is left to the font: fontSpacing is in HUD
                // units and does not follow `scale`, so setting it spread the
                // digits back out and undid the shrink.
                DrawText2D(ammoRightX, y + 1.6f * scale, Align.Right, palette: 0, ammo, color: new ColorRgba(230, 234, 242, 255), alpha: Features.HudOpacity, scale: 0.42f * scale);
                y += rowHeight;
            }
        }

        public void DrawBoostBombs()
        {
            float posY = _boostBombsYOffset;
            if (_player._abilities.TestFlag(AbilityFlags.Bombs) && _player.Hunter != Hunter.Kanden)
            {
                float posX = 244;
                for (int i = 3; i > 0; i--)
                {
                    _bombInst.SetIndex(_player._bombAmmo < i ? 1 : 0, _player._scene);
                    _bombInst.PositionX = (posX - _bombInst.Width / 2) / 256f;
                    _bombInst.PositionY = posY / 192f;
                    Presentation.DrawHudObject(_bombInst, mode: 2);
                    posX -= 14;
                }

                string message = Strings.GetHudMessage(1); // bombs
                DrawText2D(230, posY + 18, Align.Center, palette: 0, message);
            }

            if (_player._abilities.TestFlag(AbilityFlags.Boost))
            {
                if (_player._altAttackCooldown == 0)
                {
                    _boostInst.SetIndex(0, _player._scene);
                }
                else if (_boostInst.Timer <= 1 / (float)SimTicks.LegacyHz)
                {
                    _boostInst.SetIndex(1, _player._scene);
                }

                _boostInst.PositionX = (29 - _boostInst.Width / 2) / 256f;
                _boostInst.PositionY = (posY - 16) / 192f;
                Presentation.DrawHudObject(_boostInst, mode: 2);
                string message = Strings.GetHudMessage(2); // boost
                DrawText2D(29, posY + 18, Align.Center, palette: 0, message);
            }
        }

        public void DrawMeter(float x, float y, int baseAmount, int curAmount, int palette, HudMeter meter, bool drawText, bool drawTanks, float alpha = 1)
        {
            int filledTanks = 0;
            int remaining = curAmount;
            if (drawTanks && meter.TankCount > 0)
            {
                for (int i = 0; i < meter.TankCount; i++)
                {
                    if (remaining < meter.TankAmount)
                    {
                        break;
                    }

                    filledTanks++;
                    remaining -= meter.TankAmount;
                }
            }

            int barAmount;
            barAmount = Math.Min(baseAmount, curAmount);
            int tiles = (meter.Length + 7) / 8;
            int filledTiles = 100000 * barAmount / (99000 * meter.TankAmount / meter.Length);
            if (filledTiles == 0 && barAmount > 0)
            {
                filledTiles = 1;
            }

            if (drawText)
            {
                int amount = curAmount;
                DrawText2D(x + meter.BarOffsetX, y + meter.BarOffsetY, meter.Align, _healthbarPalette, $"{amount:00}", alpha: alpha);
                if (meter.MessageId > 0)
                {
                    string message = Strings.GetHudMessage(meter.MessageId);
                    DrawText2D(x + meter.TextOffsetX, y + meter.TextOffsetY, Align.Left, _healthbarPalette, message, alpha: alpha);
                }

                if (drawTanks && meter.TankCount > 0)
                {
                    Debug.Assert(meter.TankInst != null);
                    float tankX = x + meter.TankOffsetX;
                    float tankY = y + meter.TankOffsetY;
                    for (int i = 0; i < meter.TankCount; i++)
                    {
                        meter.TankInst.PositionX = tankX / 256f;
                        meter.TankInst.PositionY = tankY / 192f;
                        meter.TankInst.SetData(charFrame: i < filledTanks ? 0 : 1, palette, _player._scene);
                        meter.TankInst.Alpha = alpha;
                        Presentation.DrawHudObject(meter.TankInst);
                        if (meter.Horizontal)
                        {
                            tankX += meter.TankSpacing;
                        }
                        else
                        {
                            tankY -= meter.TankSpacing;
                        }
                    }
                }
            }

            void DrawTile(int charFrame)
            {
                meter.BarInst.PositionX = x / 256f;
                meter.BarInst.PositionY = y / 192f;
                meter.BarInst.SetData(charFrame, palette, _player._scene);
                meter.BarInst.Alpha = alpha;
                Presentation.DrawHudObject(meter.BarInst, mode: 2);
                if (meter.Horizontal)
                {
                    x += 8;
                }
                else
                {
                    y -= 8;
                }
            }

            for (int i = 0; i < filledTiles / 8; i++)
            {
                DrawTile(charFrame: 0);
                tiles--;
            }

            if (tiles > 0)
            {
                DrawTile(charFrame: 8 - (filledTiles & 7));
                tiles--;
                if (tiles > 0)
                {
                    for (int i = 0; i < tiles; i++)
                    {
                        DrawTile(charFrame: 8);
                    }
                }
            }
        }

        public void ProcessModeHud()
        {
            _locatorInfo.Clear();
            _radarFrame.Begin(_player.Position, _player.FacingVector, _player._scene.FrameCount);
            ProcessOpponent();
            if (_player._scene.Match.Rules.Mode == MatchMode.Survival || _player._scene.Match.Rules.Mode == MatchMode.TeamSurvival)
            {
                ProcessHudSurvival();
            }
            else if (_player._scene.Match.Rules.Mode == MatchMode.Bounty || _player._scene.Match.Rules.Mode == MatchMode.TeamBounty)
            {
                ProcessHudBounty();
            }
            else if (_player._scene.Match.Rules.Mode == MatchMode.Capture)
            {
                ProcessHudCapture();
            }
            else if (_player._scene.Match.Rules.Mode == MatchMode.Defender || _player._scene.Match.Rules.Mode == MatchMode.TeamDefender)
            {
                ProcessHudDefender();
            }
            else if (_player._scene.Match.Rules.Mode == MatchMode.Nodes || _player._scene.Match.Rules.Mode == MatchMode.TeamNodes)
            {
                ProcessHudNodes();
            }
            else if (_player._scene.Match.Rules.Mode == MatchMode.PrimeHunter)
            {
                ProcessHudPrimeHunter();
            }
        }

        public void ProcessHudSurvival()
        {
            int reveal = 0;
            foreach (PlayerEntity player in _player._scene.GetPlayerEntities())
            {
                if (player.Health == 0 || player.TeamIndex == _player.TeamIndex)
                {
                    continue;
                }

                float alpha = 1;
                if (_player._scene.Match.RadarPlayers)
                {
                    float past = _player._scene.ElapsedTime % (120 / (float)SimTicks.LegacyHz);
                    if (past > 32 / (float)SimTicks.LegacyHz)
                    {
                        alpha = 0;
                    }
                    else
                    {
                        float pct = past / (32 / (float)SimTicks.LegacyHz) % 1;
                        if (pct <= 0.5f)
                        {
                            alpha = Lerp(0, 1, pct * 2);
                        }
                        else
                        {
                            alpha = Lerp(1, 0, (pct - 0.5f) * 2);
                        }
                    }
                }
                else
                {
                    if (!player.Flags2.TestFlag(PlayerFlags2.RadarReveal))
                    {
                        continue;
                    }

                    if (player.Flags2.TestFlag(PlayerFlags2.RadarRevealPrevious))
                    {
                        reveal = 2;
                    }
                    else if (reveal == 0)
                    {
                        reveal = 1;
                    }
                }

                Vector3 pos = player.Position;
                if (!player.IsAltForm)
                {
                    pos.Y += 0.75f;
                }

                AddLocatorInfo(pos, _playerLocator, new ColorRgb(31, 31, 31), alpha, player.TeamIndex);
            }

            if (reveal == 1)
            {
                _player._soundSource.QueueStream(VoiceId.VOICE_CAMPING, delay: 1);
                QueueHudMessage(128, 150, 60 / (float)SimTicks.LegacyHz, 0, 234); // COWARD DETECTED!
            }
        }

        public void ProcessHudBounty()
        {
            var goodColor = new ColorRgb(15, 15, 31);
            if (_player.OctolithFlag != null)
            {
                foreach (FlagBaseEntity flagBase in _player._scene.GetFlagBaseEntities())
                {
                    AddLocatorInfo(flagBase.Position, _nodeLocator, goodColor, team: (int)flagBase.Data.TeamId);
                }
            }
            else
            {
                foreach (OctolithFlagEntity flag in _player._scene.GetOctolithFlagEntities())
                {
                    var color = new ColorRgb(31, 31, 31);
                    if (flag.Carrier != null && (_player._scene.FrameCount & (4 * 2)) != 0) // todo: FPS stuff
                    {
                        color = flag.Carrier.TeamIndex == _player.TeamIndex ? goodColor : new ColorRgb(31, 0, 0);
                    }

                    AddLocatorInfo(flag.Position, _octolithLocator, color, team: flag.Carrier?.TeamIndex ?? -1);
                }
            }
        }

        public void ProcessHudCapture()
        {
            var goodColor = new ColorRgb(15, 15, 31); // todo: share common colors
            foreach (OctolithFlagEntity flag in _player._scene.GetOctolithFlagEntities())
            {
                if (flag.Carrier != _player)
                {
                    ColorRgb color = Metadata.TeamColors[flag.Data.TeamId];
                    if (flag.Carrier != null && (_player._scene.FrameCount & (4 * 2)) != 0) // todo: FPS stuff
                    {
                        color = flag.Carrier.TeamIndex == _player.TeamIndex ? goodColor : new ColorRgb(31, 0, 0);
                    }

                    AddLocatorInfo(flag.Position, _octolithLocator, color, team: flag.Data.TeamId);
                    if (_player.OctolithFlag != null && flag.Data.TeamId == _player.TeamIndex)
                    {
                        AddLocatorInfo(flag.BasePosition, _nodeLocator, goodColor, team: flag.Data.TeamId);
                    }
                }
            }
        }

        public void ProcessHudDefender()
        {
            foreach (NodeDefenseEntity defense in _player._scene.GetNodeDefenseEntities())
            {
                ColorRgb color;
                if (defense.CurrentTeam == NodeDefenseEntity.NeutralTeam)
                {
                    color = new ColorRgb(31, 31, 31);
                }
                else if (_player._scene.Match.Rules.Teams)
                {
                    Debug.Assert(defense.CurrentTeam == 0 || defense.CurrentTeam == 1);
                    color = Metadata.TeamColors[defense.CurrentTeam];
                }
                else if (defense.CurrentTeam == _player.TeamIndex)
                {
                    color = new ColorRgb(15, 15, 31);
                }
                else
                {
                    color = new ColorRgb(31, 0, 0);
                }

                AddLocatorInfo(defense.Position, _nodeLocator, color, team: defense.CurrentTeam);
            }
        }

        private int _nodeBonusOpponent = -1;
        private bool _mainNodeBonus = false;
        private readonly int[] _teamNodeCounts = new int[PlayerEntity.SlotCapacity];
        public int _nodesHudState = 0;
        public int _nodesProgressAmount = 0;
        public void ProcessHudNodes()
        {
            _nodeBonusOpponent = -1;
            _mainNodeBonus = false;
            for (int i = 0; i < _teamNodeCounts.Length; i++)
            {
                _teamNodeCounts[i] = 0;
            }

            bool showBar = false;
            foreach (NodeDefenseEntity defense in _player._scene.GetNodeDefenseEntities())
            {
                ColorRgb color;
                if (defense.CurrentTeam == NodeDefenseEntity.NeutralTeam)
                {
                    if (defense.Blinking)
                    {
                        if (_player._scene.Match.Rules.Teams)
                        {
                            Debug.Assert(defense.OccupyingTeam == 0 || defense.OccupyingTeam == 1);
                            color = Metadata.TeamColors[defense.OccupyingTeam];
                        }
                        else if (defense.OccupyingTeam == _player.TeamIndex)
                        {
                            color = new ColorRgb(15, 15, 31);
                        }
                        else
                        {
                            color = new ColorRgb(31, 0, 0);
                        }
                    }
                    else
                    {
                        color = new ColorRgb(31, 31, 31);
                    }
                }
                else if (_player._scene.Match.Rules.Teams)
                {
                    color = Metadata.TeamColors[defense.Blinking ? defense.OccupyingTeam : defense.CurrentTeam];
                }
                else
                {
                    if (defense.CurrentTeam == _player.TeamIndex)
                    {
                        if (!defense.Blinking || defense.OccupyingTeam == _player.TeamIndex)
                        {
                            color = new ColorRgb(15, 15, 31);
                        }
                        else
                        {
                            color = new ColorRgb(31, 0, 0);
                        }
                    }
                    else if (defense.Blinking && defense.OccupyingTeam == _player.TeamIndex)
                    {
                        color = new ColorRgb(15, 15, 31);
                    }
                    else
                    {
                        color = new ColorRgb(31, 0, 0);
                    }
                }

                AddLocatorInfo(defense.Position, _nodeLocator, color, team: defense.CurrentTeam);
                if (defense.CurrentTeam != NodeDefenseEntity.NeutralTeam && defense.OccupyingTeam == NodeDefenseEntity.NeutralTeam)
                {
                    int count = _teamNodeCounts[defense.CurrentTeam] + 1;
                    _teamNodeCounts[defense.CurrentTeam] = count;
                    if (count > 1)
                    {
                        if (defense.CurrentTeam == _player.TeamIndex)
                        {
                            _mainNodeBonus = true;
                        }
                        else if (_nodeBonusOpponent == -1 || count > _teamNodeCounts[_nodeBonusOpponent])
                        {
                            _nodeBonusOpponent = defense.CurrentTeam;
                        }
                    }
                }

                if (defense.OccupiedBy[_player.SlotIndex])
                {
                    showBar = true;
                    if (_nodesHudState == 0)
                    {
                        QueueHudMessage(128, 133, 45 / (float)SimTicks.LegacyHz, 17, 205); // acquiring node
                        _nodesProgressAmount = 0;
                        _nodesHudState = 1;
                    }
                    else if (_nodesHudState == 1)
                    {
                        _nodesProgressAmount = (int)MathF.Round(Lerp(0, 40, defense.Progress / (300 / (float)SimTicks.LegacyHz)));
                    }
                }
            }

            if (!showBar && _nodesHudState != 0)
            {
                ClearHudMessage(mask: 16);
                _nodesHudState = 0;
            }
        }

        private bool _hudIsPrimeHunter = false;
        private float _primeHunterTextTimer = 0;
        public void ProcessHudPrimeHunter()
        {
            if (_player._scene.Match.PrimeHunter == _player.SlotIndex)
            {
                if (!_hudIsPrimeHunter)
                {
                    _primeHunterInst.SetAnimation(start: 0, target: 1, frames: 20, loop: true);
                    _primeHunterTextTimer = 90 / (float)SimTicks.LegacyHz;
                    _hudIsPrimeHunter = true;
                }

                if (_primeHunterTextTimer > 0)
                {
                    _primeHunterTextTimer -= _player._scene.FrameTime;
                }
            }
            else
            {
                if (_hudIsPrimeHunter)
                {
                    _hudIsPrimeHunter = false;
                }

                if (_player._scene.Match.PrimeHunter != -1)
                {
                    PlayerEntity primeHunter = PlayerEntity.Players[_player._scene.Match.PrimeHunter];
                    Vector3 pos = primeHunter.Position;
                    if (!primeHunter.IsAltForm)
                    {
                        pos.Y += 0.75f;
                    }

                    AddLocatorInfo(pos, _playerLocator, new ColorRgb(31, 0, 0), team: primeHunter.TeamIndex);
                }
            }

            _primeHunterInst.ProcessAnimation(_player._scene);
        }

        public void DrawModeHud()
        {
            MatchMode mode = _player._scene.Match.Rules.Mode;
            if (mode == MatchMode.Battle || mode == MatchMode.TeamBattle)
            {
                DrawHudBattle();
            }
            else if (mode == MatchMode.Survival || mode == MatchMode.TeamSurvival)
            {
                DrawHudSurvival();
            }
            else if (mode == MatchMode.Bounty || mode == MatchMode.TeamBounty)
            {
                DrawHudBounty();
            }
            else if (mode == MatchMode.Capture)
            {
                DrawHudCapture();
            }
            else if (mode == MatchMode.Defender || mode == MatchMode.TeamDefender)
            {
                DrawHudDefender();
            }
            else if (mode == MatchMode.Nodes || mode == MatchMode.TeamNodes)
            {
                DrawHudNodes();
            }
            else if (mode == MatchMode.PrimeHunter)
            {
                DrawHudPrimeHunter();
            }

            DrawOpponent();
        }

        public string FormatModeScore(int slot)
        {
            MatchMode mode = _player._scene.Match.Rules.Mode;
            if (mode == MatchMode.Battle || mode == MatchMode.TeamBattle || mode == MatchMode.Capture || mode == MatchMode.Nodes || mode == MatchMode.TeamNodes || mode == MatchMode.Bounty || mode == MatchMode.TeamBounty)
            {
                if (_player._scene.Match.Rules.Teams)
                {
                    return $"{_player._scene.Match.TeamPoints[PlayerEntity.Players[slot].TeamIndex]} / {_player._scene.Match.Rules.LegacyPointGoal}";
                }

                return $"{_player._scene.Match.Players[slot].Points} / {_player._scene.Match.Rules.LegacyPointGoal}";
            }

            if (mode == MatchMode.Survival || mode == MatchMode.TeamSurvival)
            {
                int lives = Math.Max(_player._scene.Match.Rules.LegacyPointGoal - _player._scene.Match.TeamDeaths[PlayerEntity.Players[slot].TeamIndex], 0);
                return lives.ToString();
            }

            if (mode == MatchMode.Defender || mode == MatchMode.TeamDefender || mode == MatchMode.PrimeHunter)
            {
                return $"{FormatTime(TimeSpan.FromSeconds(_player._scene.Match.Players[slot].Time))}/" + $"{FormatTime(TimeSpan.FromSeconds(_player._scene.Match.Rules.LegacyTimeGoal))}";
            }

            return " ";
        }

        public void DrawModeScore(int messageId, string text)
        {
            if (Features.ProHud)
            {
                // Drawn by DrawProHud instead, in its own place and its own
                // size. Suppressed here rather than at each of the seven
                // modes that calls this.
                return;
            }

            float posX = _hudObjects.ScorePosX + _objShiftX;
            // Below the chat log rather than under it: see ModChatClearance.
            float posY = ModChatClearance(_hudObjects.ScorePosY) + _objShiftY;
            _textSpacingY = 8;
            string message = Strings.GetHudMessage(messageId);
            // the game wraps text here, but the text used will never wrap (and doesn't have newlines)
            DrawText2D(posX, posY, _hudObjects.ScoreAlign, 0, message);
            posY += 9;
            DrawText2D(posX, posY, _hudObjects.ScoreAlign, 0, text);
            _textSpacingY = 0;
        }

        public void DrawHudBattle()
        {
            DrawModeScore(212, FormatModeScore(PlayerEntity.MainPlayerIndex)); // points
        }

        public void DrawHudSurvival()
        {
            DrawModeScore(213, FormatModeScore(PlayerEntity.MainPlayerIndex)); // lives left
        }

        public void DrawOctolithInst(int frame)
        {
            bool drawIcon = false;
            if (_player.OctolithFlag != null)
            {
                drawIcon = (_player._scene.FrameCount & (16 * 2)) != 0; // todo: FPS stuff
                _octolithInst.Alpha = 1;
            }
            else if (_player._scene.Match.Rules.Teams)
            {
                foreach (OctolithFlagEntity flag in _player._scene.GetOctolithFlagEntities())
                {
                    if (flag.Carrier != null && flag.Carrier.TeamIndex == _player.TeamIndex)
                    {
                        drawIcon = true;
                        _octolithInst.Alpha = 0.5f;
                        break;
                    }
                }
            }

            if (drawIcon)
            {
                _octolithInst.PositionX = (_hudObjects.OctolithPosX + _objShiftX) / 256f;
                _octolithInst.PositionY = (_hudObjects.OctolithPosY + _objShiftY) / 192f;
                _octolithInst.SetIndex(frame, _player._scene);
                Presentation.DrawHudObject(_octolithInst);
            }
        }

        public void DrawHudBounty()
        {
            DrawModeScore(215, FormatModeScore(PlayerEntity.MainPlayerIndex)); // octoliths
            DrawOctolithInst(frame: 0);
        }

        public void DrawHudCapture()
        {
            DrawModeScore(216, FormatModeScore(PlayerEntity.MainPlayerIndex)); // octoliths
            DrawOctolithInst(frame: _player.TeamIndex == 0 ? 4 : 3);
        }

        public void DrawHudDefender()
        {
            DrawModeScore(217, FormatModeScore(PlayerEntity.MainPlayerIndex)); // ring time
        }

        public void DrawHudNodes()
        {
            DrawModeScore(218, FormatModeScore(PlayerEntity.MainPlayerIndex)); // points
            DrawNodesBonuses();
            DrawNodesIcons();
            if (_nodesHudState == 1 && !IsHudMessageQueued(mask: 16))
            {
                _nodeProgressMeter.TankAmount = 40;
                _nodeProgressMeter.TankCount = 0;
                DrawMeter(108, 143, _nodesProgressAmount, _nodesProgressAmount, palette: 0, _nodeProgressMeter, drawText: false, drawTanks: false);
                string message = Strings.GetHudMessage(204); // progress
                DrawText2D(128, 133, Align.Center, palette: 0, message);
            }
        }

        public void DrawNodesBonuses()
        {
            string message = Strings.GetHudMessage(210); // bonus
            if (_mainNodeBonus)
            {
                _nodesInst.PositionX = (_hudObjects.NodeBonusPosX + _objShiftX) / 256f;
                _nodesInst.PositionY = (_hudObjects.NodeBonusPosY + _objShiftY) / 192f;
                _nodesInst.SetIndex(_player._scene.Match.Rules.Teams && _player.TeamIndex == 0 ? 2 : 4, _player._scene);
                Presentation.DrawHudObject(_nodesInst);
                string text = $"x {_teamNodeCounts[_player.TeamIndex]}";
                DrawText2D(_hudObjects.NodeBonusPosX + 12 + _objShiftX, _hudObjects.NodeBonusPosY + 2 + _objShiftY, Align.Left, 0, text);
                DrawText2D(_hudObjects.NodeBonusPosX + _objShiftX, _hudObjects.NodeBonusPosY + 10 + _objShiftY, Align.Left, 0, message);
            }

            float past = _player._scene.ElapsedTime % (16 / (float)SimTicks.LegacyHz);
            if (_nodeBonusOpponent != -1 && past < 12 / (float)SimTicks.LegacyHz)
            {
                _nodesInst.PositionX = (_hudObjects.EnemyBonusPosX + _objShiftX) / 256f;
                _nodesInst.PositionY = (_hudObjects.EnemyBonusPosY + _objShiftY) / 192f;
                _nodesInst.SetIndex(_player._scene.Match.Rules.Teams && _nodeBonusOpponent == 1 ? 4 : 2, _player._scene);
                Presentation.DrawHudObject(_nodesInst);
                string text = $"x {_teamNodeCounts[_nodeBonusOpponent]}";
                DrawText2D(_hudObjects.EnemyBonusPosX + 12 + _objShiftX, _hudObjects.EnemyBonusPosY + 2 + _objShiftY, Align.Left, 2, text);
                DrawText2D(_hudObjects.EnemyBonusPosX + _objShiftX, _hudObjects.EnemyBonusPosY + 10 + _objShiftY, Align.Left, 2, message);
            }
        }

        public void DrawNodesIcons()
        {
            float startX = 12;
            int nodeCount = 0;
            foreach (NodeDefenseEntity defense in _player._scene.GetNodeDefenseEntities())
            {
                if (defense.Type == EntityType.NodeDefense)
                {
                    nodeCount++;
                }
            }

            if (nodeCount < 4)
            {
                startX = (16 * nodeCount / 2) - 12;
            }

            float posX = 0;
            foreach (NodeDefenseEntity defense in _player._scene.GetNodeDefenseEntities())
            {
                int frame;
                if (defense.CurrentTeam == NodeDefenseEntity.NeutralTeam)
                {
                    if (defense.Blinking)
                    {
                        if (_player._scene.Match.Rules.Teams)
                        {
                            frame = defense.OccupyingTeam == 0 ? 2 : 4;
                        }
                        else
                        {
                            frame = defense.OccupyingTeam == _player.TeamIndex ? 4 : 2;
                        }
                    }
                    else
                    {
                        frame = 0;
                    }
                }
                else if (_player._scene.Match.Rules.Teams)
                {
                    if (defense.Blinking)
                    {
                        frame = defense.OccupyingTeam == 0 ? 2 : 4;
                    }
                    else
                    {
                        frame = defense.CurrentTeam == 0 ? 2 : 4;
                    }
                }
                else
                {
                    if (defense.CurrentTeam == _player.TeamIndex)
                    {
                        if (!defense.Blinking || defense.OccupyingTeam == _player.TeamIndex)
                        {
                            frame = 4;
                        }
                        else
                        {
                            frame = 2;
                        }
                    }
                    else if (defense.Blinking && defense.OccupyingTeam == _player.TeamIndex)
                    {
                        frame = 4;
                    }
                    else
                    {
                        frame = 2;
                    }
                }

                _nodesInst.PositionX = (_hudObjects.NodeIconPosX + startX - posX + _objShiftX) / 256f;
                _nodesInst.PositionY = (_hudObjects.NodeIconPosY - 8 + _objShiftY) / 192f;
                _nodesInst.SetIndex(frame, _player._scene);
                Presentation.DrawHudObject(_nodesInst);
                posX += 16;
            }

            string text = Strings.GetHudMessage(8); // NODES
            DrawText2D(_hudObjects.NodeTextPosX + _objShiftX, _hudObjects.NodeTextPosY + _objShiftY, Align.Center, 0, text);
        }

        public void DrawHudPrimeHunter()
        {
            if (_hudIsPrimeHunter)
            {
                float posX = _hudObjects.PrimePosX + _objShiftX;
                float posY = _hudObjects.PrimePosY + _objShiftY;
                _primeHunterInst.PositionX = (posX - 16) / 256f;
                _primeHunterInst.PositionY = (posY - 16) / 192f;
                Presentation.DrawHudObject(_primeHunterInst);
                if (_primeHunterTextTimer > 0)
                {
                    float elapsed = (90 / (float)SimTicks.LegacyHz) - _primeHunterTextTimer;
                    int length = (int)MathF.Ceiling(elapsed / (1 / (float)SimTicks.LegacyHz));
                    string message = Strings.GetHudMessage(11); // prime hunter
                    _textSpacingY = 8;
                    DrawText2D(posX + _hudObjects.PrimeTextPosX, posY + _hudObjects.PrimeTextPosY, _hudObjects.PrimeAlign, 0, message, maxLength: length);
                    _textSpacingY = 0;
                }
            }

            DrawModeScore(214, FormatModeScore(PlayerEntity.MainPlayerIndex)); // prime time
        }

        private int _doubleDamageSpeed = 0;
        private float _doubleDamageTextTimer = 0;
        private float _doubleDamageIconTimer = 0;
        public void UpdateDoubleDamageSpeed(int speed)
        {
            _doubleDamageSpeed = speed;
            _doubleDamageIconTimer = 0;
            if (speed == 1)
            {
                _doubleDamageTextTimer = 60 / (float)SimTicks.LegacyHz;
            }
        }

        public void ProcessDoubleDamageHud()
        {
            if (_player._doubleDmgTimer > 0)
            {
                if (_doubleDamageTextTimer > 0)
                {
                    _doubleDamageTextTimer -= _player._scene.FrameTime;
                }

                _doubleDamageIconTimer += _player._scene.FrameTime;
            }
        }

        public void DrawDoubleDamageHud()
        {
            if (_player._doubleDmgTimer > 0)
            {
                float posX = _hudObjects.DblDmgPosX + _objShiftX;
                float posY = _hudObjects.DblDmgPosY + _objShiftY;
                _doubleDamageInst.PositionX = (posX - 16) / 256f;
                _doubleDamageInst.PositionY = (posY - 16) / 192f;
                int frame = 0;
                if (_doubleDamageSpeed == 1)
                {
                    float past = _doubleDamageIconTimer % (35 / (float)SimTicks.LegacyHz);
                    if (past >= 30 / (float)SimTicks.LegacyHz)
                    {
                        frame = 1;
                    }
                }
                else if (_doubleDamageSpeed == 2)
                {
                    float past = _doubleDamageIconTimer % (25 / (float)SimTicks.LegacyHz);
                    if (past >= 20 / (float)SimTicks.LegacyHz)
                    {
                        frame = 1;
                    }
                }
                else if (_doubleDamageSpeed == 3)
                {
                    float past = _doubleDamageIconTimer % (10 / (float)SimTicks.LegacyHz);
                    if (past >= 5 / (float)SimTicks.LegacyHz)
                    {
                        frame = 1;
                    }
                }

                _doubleDamageInst.SetIndex(frame, _player._scene);
                _doubleDamageInst.Alpha = 0.5f;
                Presentation.DrawHudObject(_doubleDamageInst);
                if (_doubleDamageTextTimer > 0)
                {
                    float elapsed = (60 / (float)SimTicks.LegacyHz) - _doubleDamageTextTimer;
                    int length = (int)MathF.Ceiling(elapsed / (1 / (float)SimTicks.LegacyHz));
                    string message = Strings.GetHudMessage(3); // double damage
                    _textSpacingY = 10;
                    DrawText2D(posX + _hudObjects.DblDmgTextPosX, posY + _hudObjects.DblDmgTextPosY, _hudObjects.DblDmgAlign, 0, message, maxLength: length);
                    _textSpacingY = 0;
                }
            }
        }

        private bool _hudCloaking = false;
        private float _cloakTextTimer = 0;
        public void ProcessCloakHud()
        {
            if (_player._cloakTimer > 0 && _player.Flags2.TestFlag(PlayerFlags2.Cloaking))
            {
                if (!_hudCloaking)
                {
                    _hudCloaking = true;
                    _cloakTextTimer = 45 / (float)SimTicks.LegacyHz;
                }

                if (_cloakTextTimer > 0)
                {
                    _cloakTextTimer -= _player._scene.FrameTime;
                }
            }
            else
            {
                _hudCloaking = false;
            }
        }

        public void DrawCloakHud()
        {
            if (_player._cloakTimer > 0 && _player.Flags2.TestFlag(PlayerFlags2.Cloaking))
            {
                float posX = _hudObjects.CloakPosX + _objShiftX;
                float posY = _hudObjects.CloakPosY + _objShiftY;
                _cloakInst.PositionX = (posX - 16) / 256f;
                _cloakInst.PositionY = (posY - 16) / 192f;
                _cloakInst.Alpha = 0.5f;
                Presentation.DrawHudObject(_cloakInst);
                if (_cloakTextTimer > 0)
                {
                    float elapsed = (45 / (float)SimTicks.LegacyHz) - _cloakTextTimer;
                    int length = (int)MathF.Ceiling(elapsed / (1 / (float)SimTicks.LegacyHz));
                    string message = Strings.GetHudMessage(4); // cloak
                    DrawText2D(posX + _hudObjects.CloakTextPosX, posY + _hudObjects.CloakTextPosY, _hudObjects.CloakAlign, 0, message, maxLength: length);
                }
            }
        }

        private float _opponentHealthbarTimer = 0;
        private int _opponentIndex = -1;
        public void UpdateOpponent(int slot)
        {
            if (slot != _player.SlotIndex)
            {
                _opponentHealthbarTimer = 60 / (float)SimTicks.LegacyHz;
                _opponentIndex = slot;
            }
        }

        public void ProcessOpponent()
        {
            if (_opponentIndex != -1 && _opponentHealthbarTimer > 0)
            {
                _opponentHealthbarTimer -= _player._scene.FrameTime;
                if (_opponentHealthbarTimer <= 0)
                {
                    _opponentHealthbarTimer = 0;
                    _opponentIndex = -1;
                }
            }
        }

        public void DrawOpponent()
        {
            if (_opponentIndex == -1 || _opponentHealthbarTimer == 0 || !Features.TopScreenTargetInfo)
            {
                return;
            }

            PlayerEntity opponent = PlayerEntity.Players[_opponentIndex];
            float posX = 93;
            float posY = 182;
            if (Features.TargetInfoSway)
            {
                posX += _objShiftX;
                posY += _objShiftY;
            }

            string nickname = GameState.Nicknames[_opponentIndex];
            DrawText2D(posX, posY, Align.Center, 0, nickname);
            HudObjectInstance portrait = _hunterInsts[(int)opponent.Hunter];
            portrait.PositionX = (posX - 16) / 256f;
            portrait.PositionY = (posY - 33) / 192f;
            Presentation.DrawHudObject(portrait);
            posX += 18;
            posY -= 26;
            int remainingAmount = opponent.Health >= _player.Values.EnergyTank ? opponent.Health - _player.Values.EnergyTank : 0;
            _enemyHealthMeter.TankAmount = _player.Values.EnergyTank;
            _enemyHealthMeter.TankCount = opponent.HealthMax / _player.Values.EnergyTank;
            _enemyHealthMeter.Length = 72;
            DrawMeter(posX, posY, _player.Values.EnergyTank - 1, opponent.Health, 0, _enemyHealthMeter, drawText: false, drawTanks: false);
            DrawMeter(posX, posY + 5, _player.Values.EnergyTank - 1, remainingAmount, 0, _enemyHealthMeter, drawText: false, drawTanks: false);
            string score = FormatModeScore(opponent.SlotIndex);
            DrawText2D(posX + 5, posY + 14, Align.Left, 0, score);
        }

        private int _prevScrollingChars = 0;
        public void DrawModeRules()
        {
            string? header = _rulesLines[0];
            Debug.Assert(header != null);
            ColorRgba? color = Paths.IsMphJapan || Paths.IsMphKorea ? null : new ColorRgba(0x7FDE);
            DrawText2D(128, 10, Align.Center, 0, header, color);
            int totalCharacters = (int)(_player._scene.ElapsedTime / (1 / (float)SimTicks.LegacyHz));
            float posY = 28;
            _textSpacingY = 8;
            for (int i = 1; i < _rulesInfo.Count; i++)
            {
                (int prevLength, _) = _rulesLengths[i - 1];
                int characters = totalCharacters - prevLength;
                if (characters <= 0)
                {
                    break;
                }

                string? line = _rulesLines[i];
                Debug.Assert(line != null);
                float posX = _rulesInfo.Offsets[i] + 12;
                color = Paths.IsMphJapan || Paths.IsMphKorea ? null : new ColorRgba(0x7F5A);
                DrawText2D(posX, posY, Align.Left, 0, line, color, maxLength: characters);
                posY += 13 + _rulesLengths[i].Newlines * 8;
            }

            // todo?: ideally this should be in a process method, not draw
            if (totalCharacters > _prevScrollingChars && totalCharacters > _rulesLengths[0].Length && totalCharacters <= _rulesLengths[_rulesInfo.Count - 1].Length)
            {
                _player._soundSource.StopFreeSfx(SfxId.LETTER_BLIP);
                _player._soundSource.PlayFreeSfx(SfxId.LETTER_BLIP);
                _prevScrollingChars = totalCharacters;
            }

            _textSpacingY = 0;
        }

        private bool _usingKanjiFont = false;
        public static int GlyphIndex(Font font, int ch)
        {
            int index = ch - font.MinCharacter;
            if (index >= 0 && index < font.Widths.Count && index < font.Offsets.Count)
            {
                return index;
            }

            int fallback = '?' - font.MinCharacter;
            return fallback >= 0 && fallback < font.Widths.Count && fallback < font.Offsets.Count ? fallback : 0;
        }

        public Font SetUpFont(char firstChar, bool set)
        {
            Font font = Font.Normal;
            if (Scene.Language == Language.Japanese && (Paths.IsMphJapan || Paths.IsMphKorea) && (firstChar & 0xA0) == 0xA0)
            {
                font = Font.Kanji;
                if (set && !_usingKanjiFont)
                {
                    _textInst.SetCharacterData(Font.Kanji.CharacterData, width: 16, height: 16, _player._scene);
                    _usingKanjiFont = true;
                }
            }
            else if (set && _usingKanjiFont)
            {
                _textInst.SetCharacterData(Font.Normal.CharacterData, width: 8, height: 8, _player._scene);
                _usingKanjiFont = false;
            }

            return font;
        }

        /// <summary>
        /// The frame rate, in the corner of the DS's own screen.
        ///
        /// Formatted into a stack buffer rather than a string, because this
        /// runs every frame and a counter that allocates every frame is
        /// measuring itself. Not shifted by <c>_objShiftX</c>, which is the
        /// idle sway: the rest of the HUD drifts on purpose and a number you
        /// are trying to read should not.
        /// </summary>
        /// <summary>
        /// The right-hand corner, on every platform.
        ///
        /// It used to be the left one, which is where a frame counter belongs
        /// when nothing else is there. The chat log is there now -- three
        /// lines of it, growing downward from the same y -- and two things
        /// sharing that corner meant the counter sat under the first message
        /// anybody sent. Nothing on the right competes for it: the ammo
        /// readout is at the foot of the screen and the scoreboard is drawn
        /// down the middle. The Android MENU button, which is why this had a
        /// per-platform inset at all, is on the left and stays the chat log's
        /// problem instead.
        /// </summary>
        private const float NumberMargin = 3;
        private const float NumberY = 3;
        private const float NumberScale = 0.5f;
        private const float UnitScale = 0.34f;
        public void DrawFps()
        {
            Span<char> buffer = stackalloc char[12];
            if (!Presentation.FramesPerSecond.TryFormat(buffer, out int written, "0"))
            {
                return;
            }

            var color = new ColorRgba(0x3FEF);
            // The unit is drawn smaller than the number rather than in
            // lowercase, because the DS shipped one alphabet and it is
            // capitals: a lowercase f in the string comes out as a capital F
            // whatever we do. Smaller is what makes it read as a unit instead
            // of as three more digits.
            // Corrected across, like every other horizontal HUD measurement:
            // NumberMargin is a margin in units of the screen's height, so the
            // counter sits the same distance from the edge on any window.
            //
            // Right-aligned, so the unit is placed first and the digits are
            // hung off its left edge: laid out from the left instead, a run
            // that gains a digit at 100 fps would push "fps" off the screen.
            // A glyph is placed from its top, so the smaller run has to come
            // down by the difference in height to sit on the same baseline.
            Vector2 unit = DrawText2D(256 - NumberMargin * HudAspectFix, NumberY + (NumberScale - UnitScale) * 8, Align.Right, palette: 0, "fps", color, fontSpacing: 8, scale: UnitScale);
            DrawText2D(unit.X - HudAspectFix, NumberY, Align.Right, palette: 0, buffer[..written], color, fontSpacing: 8, scale: NumberScale);
        }

        private float _textSpacingY = 0;
        public Vector2 DrawText2D(float x, float y, Align type, int palette, ReadOnlySpan<char> text, ColorRgba? color = null, float alpha = 1, float fontSpacing = -1, int maxLength = -1, float scale = 1)
        {
            int padAfter = maxLength;
            if (type == Align.PadCenter)
            {
                maxLength = -1;
            }

            int length = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\0')
                {
                    break;
                }

                length++;
            }

            if (maxLength != -1)
            {
                length = Math.Min(length, maxLength);
            }

            if (length == 0)
            {
                return new Vector2(x, y);
            }

            Font font = SetUpFont(text[0], set: true);
            _textInst.Alpha = alpha;
            // A DS unit is square, and it has to stay square on any screen.
            //
            // Everything below lays text out in the DS's own 256x192 space,
            // which is 4:3, and a position in it reaches the screen as
            // x / 256 * viewWidth and y / 192 * viewHeight. On a 4:3 window
            // those two are the same number of pixels per unit and the text is
            // the shape it was drawn as. On anything wider they are not: at
            // 2400x1080 a unit is 9.375 pixels across and 5.625 down, so every
            // run came out two thirds wider than it should be and overran the
            // boxes around it -- which are drawn aspect-correct, off the
            // height, and so kept their shape while the text did not.
            //
            // This is the ratio between the two, applied to horizontal
            // *offsets from the anchor* and never to the anchor itself: the
            // HUD is meant to spread to the edges of a wide screen, and only
            // the shape of each run is wrong. It comes out at exactly 1 on 4:3,
            // so the DS proportions are preserved rather than approximated.
            // Nothing here is per-platform: the desktop is 16:9 more often
            // than not and had the same defect, a third as strong.
            float aspectFix = 1;
            if (Presentation.Size.X > 0 && Presentation.Size.Y > 0)
            {
                aspectFix = Presentation.Size.Y / 192f * (256f / Presentation.Size.X);
            }

            float spacingY = _textSpacingY == 0 ? (fontSpacing == -1 ? 12 : fontSpacing) : _textSpacingY;
            // `scale` applies to every alignment.
            //
            // It used to be honoured by Align.Left alone: the Right and Centre
            // branches drew each glyph with DrawHudObject's default scale and
            // advanced by the font's unscaled widths, so asking for smaller
            // right-aligned text silently produced text of exactly the same
            // size. Measured: the ammo counts in the weapon list came out
            // pixel-for-pixel identical at 0.5, 0.38, 0.28 and 0.15.
            if (scale != 1)
            {
                spacingY *= scale;
            }

            if (type == Align.Left)
            {
                float startX = x;
                for (int i = 0; i < length; i++)
                {
                    int ch = text[i];
                    int orig = ch;
                    // The continuation byte, only if there is one: a string
                    // whose last character has the high bit set ran off the
                    // end of the span here.
                    if ((ch & 0x80) != 0 && i + 1 < text.Length)
                    {
                        ch = text[++i] & 0x3F | ((ch & 0x1F) << 6);
                    }

                    if (orig == '\n')
                    {
                        x = startX;
                        y += spacingY;
                    }
                    else
                    {
                        int index = GlyphIndex(font, ch);
                        float offset = font.Offsets[index] * scale + y;
                        if (orig != ' ')
                        {
                            _textInst.PositionX = x / 256f;
                            _textInst.PositionY = offset / 192f;
                            if (color.HasValue)
                            {
                                _textInst.SetData(index, color.Value, _player._scene);
                            }
                            else
                            {
                                _textInst.SetData(index, palette, _player._scene);
                            }

                            Presentation.DrawHudObject(_textInst, mode: 1, scale: scale);
                        }

                        x += font.Widths[index] * scale * aspectFix;
                    }
                }
            }
            else if (type == Align.Right)
            {
                float startX = x;
                int start = 0;
                int end = 0;
                do
                {
                    end = text[start..].IndexOf('\n');
                    if (end != -1)
                    {
                        end += start;
                    }

                    if (end == -1 || length < end)
                    {
                        end = length;
                    }

                    x = startX;
                    for (int i = end - 1; i >= start; i--)
                    {
                        int ch = text[i];
                        int orig = ch;
                        if ((ch & 0x80) != 0 && i + 1 < text.Length)
                        {
                            ch = text[++i] & 0x3F | ((ch & 0x1F) << 6);
                        }

                        int index = GlyphIndex(font, ch);
                        x -= font.Widths[index] * scale * aspectFix;
                        float offset = font.Offsets[index] * scale + y;
                        if (orig != ' ')
                        {
                            _textInst.PositionX = x / 256f;
                            _textInst.PositionY = offset / 192f;
                            if (color.HasValue)
                            {
                                _textInst.SetData(index, color.Value, _player._scene);
                            }
                            else
                            {
                                _textInst.SetData(index, palette, _player._scene);
                            }

                            Presentation.DrawHudObject(_textInst, mode: 1, scale: scale);
                        }
                    }

                    if (end != length)
                    {
                        do
                        {
                            end++;
                            start = end;
                            y += spacingY;
                        }
                        while (text[start] == '\n');
                    }
                }
                while (end < length);
            }
            else if (type == Align.Center || type == Align.PadCenter)
            {
                float startX = x;
                int start = 0;
                int end = 0;
                do
                {
                    end = text[start..].IndexOf('\n');
                    if (end != -1)
                    {
                        end += start;
                    }

                    if (end == -1 || length < end)
                    {
                        end = length;
                    }

                    x = startX;
                    float width = 0;
                    for (int i = start; i < end; i++)
                    {
                        int ch = text[i];
                        if ((ch & 0x80) != 0 && i + 1 < text.Length)
                        {
                            ch = text[++i] & 0x3F | ((ch & 0x1F) << 6);
                        }

                        int index = GlyphIndex(font, ch);
                        width += font.Widths[index] * scale;
                    }

                    // character widths include their rightmost empty pixel, leading to a slight overestimation of the total width before the line break,
                    // making the text shift to the left instead of being centered. fix by flooring (which the game does implicitly with integer division).
                    x = startX - MathF.Floor(width / 2) * aspectFix;
                    for (int i = start; i < end; i++)
                    {
                        int ch = text[i];
                        int orig = ch;
                        if ((ch & 0x80) != 0 && i + 1 < text.Length)
                        {
                            ch = text[++i] & 0x3F | ((ch & 0x1F) << 6);
                        }

                        int index = GlyphIndex(font, ch);
                        float offset = font.Offsets[index] * scale + y;
                        if (orig != ' ')
                        {
                            _textInst.PositionX = x / 256f;
                            _textInst.PositionY = offset / 192f;
                            if (type != Align.PadCenter || i < padAfter)
                            {
                                if (color.HasValue)
                                {
                                    _textInst.SetData(index, color.Value, _player._scene);
                                }
                                else
                                {
                                    _textInst.SetData(index, palette, _player._scene);
                                }

                                Presentation.DrawHudObject(_textInst, mode: 1, scale: scale);
                            }
                        }

                        x += font.Widths[index] * scale * aspectFix;
                    }

                    if (end != length)
                    {
                        do
                        {
                            end++;
                            start = end;
                            y += spacingY;
                        }
                        while (text[start] == '\n');
                    }
                }
                while (end < length);
            }

            return new Vector2(x, y);
        }

        public void QueueHudMessage(float x, float y, float duration, byte category, int messageId)
        {
            if (_player._scene.IsHeadless)
            {
                return;
            }

            string text = Strings.GetHudMessage(messageId);
            QueueHudMessage(x, y, Align.Center, 256, 8, new ColorRgba(0x3FEF), 1, duration, category, text);
        }

        public void QueueHudMessage(float x, float y, int maxWidth, float duration, byte category, int messageId)
        {
            if (_player._scene.IsHeadless)
            {
                return;
            }

            string text = Strings.GetHudMessage(messageId);
            QueueHudMessage(x, y, Align.Center, maxWidth, 8, new ColorRgba(0x3FEF), 1, duration, category, text);
        }

        public void QueueHudMessage(float x, float y, float duration, byte category, string text)
        {
            QueueHudMessage(x, y, Align.Center, 256, 8, new ColorRgba(0x3FEF), 1, duration, category, text);
        }

        public void QueueHudMessage(float x, float y, int maxWidth, float duration, byte category, string text)
        {
            QueueHudMessage(x, y, Align.Center, maxWidth, 8, new ColorRgba(0x3FEF), 1, duration, category, text);
        }

        public void QueueHudMessage(float x, float y, Align align, int maxWidth, float fontSize, ColorRgba color, float alpha, float duration, byte category, string text)
        {
            if (_player._scene.IsHeadless)
            {
                return;
            }

            Debug.Assert(text.Length < 256);
            char[] buffer = new char[512];
            int lineCount = WrapText(text, maxWidth, buffer);
            float minDuration = Single.MaxValue;
            HudMessage? message = null;
            for (int i = 0; i < _hudMessageQueue.Count; i++)
            {
                HudMessage existing = _hudMessageQueue[i];
                if (existing.Lifetime > 0)
                {
                    if ((category & existing.Category & 14) != 0)
                    {
                        existing.Position = existing.Position.AddY(-lineCount * existing.FontSize);
                    }
                    else if (existing.Position.Y == y)
                    {
                        existing.Lifetime = 0;
                    }
                }

                if (existing.Lifetime < minDuration)
                {
                    minDuration = existing.Lifetime;
                    message = existing;
                }
            }

            Debug.Assert(message != null);
            Array.Fill(message.Text, '\0');
            Array.Copy(buffer, message.Text, message.Text.Length);
            if ((category & 14) != 0)
            {
                y -= (lineCount - 1) * fontSize;
            }

            message.Position = new Vector2(x, y);
            message.MaxWidth = maxWidth;
            message.FontSize = fontSize;
            message.Color = color;
            message.Alpha = alpha;
            message.Align = align;
            message.Category = category;
            message.Lifetime = duration;
        }

        public int WrapText(string text, int maxWidth, Span<char> dest, int maxTiles = 0)
        {
            return WrapText(text.AsSpan(), maxWidth, dest, maxTiles);
        }

        public int WrapText(ReadOnlySpan<char> text, int maxWidth, Span<char> dest, int maxTiles = 0)
        {
            int lines = 1;
            if (maxWidth <= 0)
            {
                return lines;
            }

            int lineWidth = 0;
            // if the line is broken at a previous space, how much width the now-next line already
            // has written to it. zeroed out if we break without a previous space to break at, since in
            // that case we break after the most recent character and the new line will start empty.
            int widthAfterBreak = 0;
            int breakPos = 0;
            int c = 0;
            if (text.Length == 0)
            {
                return 1;
            }

            Font font = SetUpFont(text[0], set: false);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                dest[c] = ch;
                if (ch == '\n')
                {
                    lineWidth = 0;
                    breakPos = 0;
                    widthAfterBreak = 0;
                    lines++;
                }
                else
                {
                    if (ch == ' ')
                    {
                        breakPos = c;
                        widthAfterBreak = 0;
                    }

                    if (ch >= ' ')
                    {
                        int index = ch;
                        if ((ch & 0x80) != 0)
                        {
                            char next = text[++i];
                            dest[++c] = next;
                            index = next & 0x3F | ((ch & 0x1F) << 6);
                        }

                        index -= font.MinCharacter;
                        int width = font.Widths[index];
                        lineWidth += width;
                        if (ch != ' ')
                        {
                            widthAfterBreak += width;
                        }
                    }

                    // todo?:
                    // the game has a stupid, possibly bugged behavior with scan and other bottom screen messages. the gist is that they're
                    // limited to 90 characters, presumably because of OAM limitations. because spaces are skipped instead of being actually drawn,
                    // they don't count toward the OAM limit, but they do get counted by the game anyway, sort of. basically the game counts
                    // 90 non-space characters, and if it finds that many on one page, it then counts 90 of *any* character, and breaks the
                    // dialog page there. this results in unnecessary breaks with text unexpectedly being pushed to the next page (such as the first
                    // lore scan having "divine" pushed to the second page) even when it could have fit. the initial count also counts newlines
                    // as non-space characters, which is dumb, since they also aren't drawn. to implement this we would have done some/all of:
                    // - keep count of non-space characters/tiles, reset every 3 lines
                    // - (counting newline characters, since the game does, even though that's stupid)
                    // - if we hit 90, count 90 chars (incl. spaces) from the start of the page and break there, starting a new page
                    // - (this uses the break rule of breaking at the previous space, or after the current character if no spaces)
                    // - this potentially pushes some already written text onto a new page, so we may need to undo/reinitialize wrapping on it?
                    // - (main thing is determining if this is necessary; if it is, we may need to track "real" newline characters vs. added breaks?)
                    // - (since we need to undo the breaks we inserted and start fresh, but don't want to erase newlines from the original text)
                    // - (easiest way to do this might be to just set our position back to the start of the new page in both source and dest?)
                    // - also need to start counting 90 non-space characters again from the the start of that page
                    if (i + 1 < text.Length && lineWidth > maxWidth)
                    {
                        if (breakPos == 0 && maxWidth >= 8)
                        {
                            breakPos = c++ + 1;
                            widthAfterBreak = 0;
                        }

                        if (breakPos > 0)
                        {
                            dest[breakPos] = '\n';
                            lineWidth = widthAfterBreak;
                            widthAfterBreak = 0;
                            breakPos = 0;
                            lines++;
                        }
                    }
                }

                c++;
            }

            dest[c] = '\0';
            return lines;
        }

        public void ClearHudMessage(int mask)
        {
            for (int i = 0; i < _hudMessageQueue.Count; i++)
            {
                HudMessage message = _hudMessageQueue[i];
                if ((mask & message.Category) != 0)
                {
                    message.Lifetime = 0;
                }
            }
        }

        public bool IsHudMessageQueued(int mask)
        {
            for (int i = 0; i < _hudMessageQueue.Count; i++)
            {
                HudMessage message = _hudMessageQueue[i];
                if ((mask & message.Category) != 0 && message.Lifetime > 0)
                {
                    return true;
                }
            }

            return false;
        }

        public void ProcessHudMessageQueue()
        {
            for (int i = 0; i < _hudMessageQueue.Count; i++)
            {
                HudMessage message = _hudMessageQueue[i];
                if (message.Lifetime > 0)
                {
                    message.Lifetime -= _player._scene.FrameTime;
                    if (message.Lifetime < 0)
                    {
                        message.Lifetime = 0;
                    }
                }
            }
        }

        public void DrawQueuedHudMessages()
        {
            for (int i = 0; i < _hudMessageQueue.Count; i++)
            {
                HudMessage message = _hudMessageQueue[i];
                if (message.Lifetime > 0 && ((message.Category & 1) == 0 || (_player._scene.FrameCount & (7 * 2)) <= 3 * 2)) // todo: FPS stuff
                {
                    // todo: support font size
                    DrawText2D(message.Position.X, message.Position.Y, message.Align, palette: 0, message.Text, message.Color, message.Alpha, fontSpacing: message.FontSize);
                }
            }
        }

        private class HudMessage
        {
            public Vector2 Position { get; set; }
            public float FontSize { get; set; }
            public ColorRgba Color { get; set; }
            public float Lifetime { get; set; }
            public float Alpha { get; set; }
            public byte Category { get; set; }
            public int MaxWidth { get; set; }
            public Align Align { get; set; }
            public char[] Text { get; } = new char[256];
        }

        private static readonly IReadOnlyList<HudMessage> _hudMessageQueue = new HudMessage[20]
        {
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage(),
            new HudMessage()
        };
    }
}
