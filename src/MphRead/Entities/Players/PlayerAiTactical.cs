using System;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    public partial class PlayerAiData
    {
        private readonly struct BotTacticalTuning
        {
            public int DecisionFrames { get; }
            public int TargetMemoryFrames { get; }
            public int WeaponDecisionFrames { get; }
            public float TargetStickiness { get; }
            public float RetreatHealthFraction { get; }
            public float HearingRange { get; }
            public int StrafeFrames { get; }

            public BotTacticalTuning(int decisionFrames, int targetMemoryFrames, int weaponDecisionFrames,
                float targetStickiness, float retreatHealthFraction, float hearingRange, int strafeFrames)
            {
                DecisionFrames = decisionFrames;
                TargetMemoryFrames = targetMemoryFrames;
                WeaponDecisionFrames = weaponDecisionFrames;
                TargetStickiness = targetStickiness;
                RetreatHealthFraction = retreatHealthFraction;
                HearingRange = hearingRange;
                StrafeFrames = strafeFrames;
            }
        }

        // Difficulty should primarily change how quickly and how well the bot makes decisions.
        // Mechanical aim is tuned separately in PlayerAi.cs. This prevents Insane from becoming
        // "Hard with an aimbot" while Easy still gets the same basic situational awareness.
        private static readonly BotTacticalTuning[] _tacticalTuning =
        [
            new(42, 36, 120, 12, 0.12f, 8, 110),   // Easy
            new(24, 90, 72, 18, 0.22f, 12, 85),    // Normal
            new(12, 150, 42, 24, 0.32f, 16, 58),   // Hard
            new(6, 240, 24, 30, 0.42f, 20, 38)     // Insane
        ];

        private static readonly BeamType[] _tacticalWeapons =
        [
            BeamType.PowerBeam, BeamType.Missile, BeamType.VoltDriver, BeamType.Battlehammer,
            BeamType.Imperialist, BeamType.Judicator, BeamType.Magmaul, BeamType.ShockCoil,
            BeamType.OmegaCannon
        ];

        private PlayerEntity? _tacticalTarget;
        private int _tacticalDecisionTimer;
        private int _tacticalWeaponTimer;
        private BeamType _tacticalWeapon = BeamType.None;
        private int _tacticalThreatSlot = -1;
        private Vector3 _tacticalThreatPosition;
        private int _tacticalThreatFrames;
        private float _tacticalThreatWeight;
        private bool _tacticalStrafeRight;
        private int _tacticalStrafeTimer;
        private bool _tacticalMovementActive;
        private Vector3 _tacticalProgressPosition;
        private int _tacticalProgressTimer;
        private int _tacticalStuckCount;
        private int _tacticalEscapeFrames;

        private BotTacticalTuning TacticalTuning =>
            _tacticalTuning[Math.Clamp(_player.BotLevel, 0, _tacticalTuning.Length - 1)];

        private void ResetTacticalState()
        {
            _tacticalTarget = null;
            _tacticalDecisionTimer = 0;
            _tacticalWeaponTimer = 0;
            _tacticalWeapon = BeamType.None;
            _tacticalThreatSlot = -1;
            _tacticalThreatPosition = Vector3.Zero;
            _tacticalThreatFrames = 0;
            _tacticalThreatWeight = 0;
            _tacticalStrafeRight = false;
            _tacticalStrafeTimer = 0;
            _tacticalMovementActive = false;
            _tacticalProgressPosition = _player.Position;
            _tacticalProgressTimer = 0;
            _tacticalStuckCount = 0;
            _tacticalEscapeFrames = 0;
        }

        private void RecordTacticalThreat(PlayerEntity? attacker, int damage)
        {
            if (attacker == null || attacker == _player || attacker.Health == 0
                || attacker.TeamIndex == _player.TeamIndex)
            {
                return;
            }

            BotTacticalTuning tuning = TacticalTuning;
            _tacticalThreatSlot = attacker.SlotIndex;
            _tacticalThreatPosition = attacker.Position;
            _tacticalThreatFrames = Math.Max(_tacticalThreatFrames, tuning.TargetMemoryFrames);
            _tacticalThreatWeight = Math.Clamp(14 + damage * 0.6f, 14, 42);
            _tacticalDecisionTimer = 0;
        }

        private void ApplyTacticalBrain()
        {
            BotTacticalTuning tuning = TacticalTuning;
            _tacticalMovementActive = false;

            if (_tacticalThreatFrames > 0)
            {
                _tacticalThreatFrames--;
                _tacticalThreatWeight = Math.Max(0, _tacticalThreatWeight - 0.08f);
            }
            else
            {
                _tacticalThreatSlot = -1;
                _tacticalThreatWeight = 0;
            }

            UpdateTacticalHearing(tuning);

            if (_tacticalDecisionTimer > 0)
            {
                _tacticalDecisionTimer--;
            }
            if (_tacticalTarget == null || !TacticalTargetValid(_tacticalTarget)
                || _tacticalDecisionTimer <= 0)
            {
                _tacticalTarget = ChooseTacticalTarget(tuning);
                _tacticalDecisionTimer = tuning.DecisionFrames;
            }

            if (_tacticalTarget == null)
            {
                // A recent gunshot or attacker should at least turn the bot toward the remembered
                // location. We intentionally do not set TargetPlayer here, so hearing never grants
                // perfect through-wall tracking or firing.
                if (_tacticalThreatFrames > 0 && _tacticalThreatPosition != Vector3.Zero)
                {
                    _field1038 = _tacticalThreatPosition - _player.CameraInfo.Position;
                    Func21447E8();
                }
                _tacticalWeapon = BeamType.None;
                return;
            }

            bool visible = TacticalCanSee(_tacticalTarget);
            bool legacyKnown = AggroFunc214857C(6, 1, 2, null, _tacticalTarget);
            if (!visible && !legacyKnown)
            {
                // Damage/hearing memory stores a position, not magical access to the
                // attacker's live transform. Do not feed a hidden moving player back
                // into the legacy target/path tree until normal perception knows them.
                if (_tacticalThreatFrames > 0 && _tacticalThreatPosition != Vector3.Zero)
                {
                    _field1038 = _tacticalThreatPosition - _player.CameraInfo.Position;
                    Func21447E8();
                }
                return;
            }

            Flags2 &= ~AiFlags2.Bit9;
            Func21356C0(_tacticalTarget);

            if (visible)
            {
                _tacticalThreatSlot = _tacticalTarget.SlotIndex;
                _tacticalThreatPosition = _tacticalTarget.Position;
                _tacticalThreatFrames = Math.Max(_tacticalThreatFrames, tuning.TargetMemoryFrames);
            }

            if (_tacticalWeaponTimer > 0)
            {
                _tacticalWeaponTimer--;
            }
            if (_tacticalWeapon == BeamType.None || !CheckBeam(_tacticalWeapon)
                || _tacticalWeaponTimer <= 0)
            {
                _tacticalWeapon = ChooseTacticalWeapon(_tacticalTarget, visible);
                _tacticalWeaponTimer = tuning.WeaponDecisionFrames;
            }

            if (_tacticalWeapon != BeamType.None)
            {
                int weaponIndex = GetWeaponIndex(_tacticalWeapon);
                _weapon1 = weaponIndex;
                _weapon2 = weaponIndex;

                if (CheckCharge(_tacticalWeapon))
                {
                    int chargeChance = Difficulty.ChargeChancePercent;
                    if (_tacticalWeapon == Weapons.AffinityWeapons[(int)_player.Hunter])
                    {
                        chargeChance = Math.Min(100, chargeChance + 8);
                    }
                    if (_scene.Random.GetRandomInt2(100) < chargeChance)
                    {
                        Flags4 |= AiFlags4.Bit1;
                    }
                    else
                    {
                        Flags4 &= ~AiFlags4.Bit1;
                    }
                }
                else
                {
                    Flags4 &= ~AiFlags4.Bit1;
                }
            }

            if (visible)
            {
                ApplyTacticalMovement(_tacticalTarget, tuning);
                ApplyTacticalWeaponDecision();
            }
        }

        private void ApplyTacticalWeaponDecision()
        {
            if (_tacticalWeapon == BeamType.None || _player.IsAltForm
                || _player.IsMorphing || _player.IsUnmorphing)
            {
                return;
            }

            // Execute() already ran this frame, so make the tactical result authoritative
            // for the final combat input instead of merely leaving _weapon1 for the legacy
            // tree to potentially replace on the next frame.
            _weapon1 = GetWeaponIndex(_tacticalWeapon);
            if (_player.CurrentWeapon != _tacticalWeapon)
            {
                switch (_tacticalWeapon)
                {
                    case BeamType.PowerBeam: _touchButtons.PowerBeam.IsDown = true; break;
                    case BeamType.Missile: _touchButtons.Missile.IsDown = true; break;
                    case BeamType.VoltDriver: _touchButtons.VoltDriver.IsDown = true; break;
                    case BeamType.Battlehammer: _touchButtons.Battlehammer.IsDown = true; break;
                    case BeamType.Imperialist: _touchButtons.Imperialist.IsDown = true; break;
                    case BeamType.Judicator: _touchButtons.Judicator.IsDown = true; break;
                    case BeamType.Magmaul: _touchButtons.Magmaul.IsDown = true; break;
                    case BeamType.ShockCoil: _touchButtons.ShockCoil.IsDown = true; break;
                    case BeamType.OmegaCannon: _touchButtons.OmegaCannon.IsDown = true; break;
                }
                return;
            }

            Func2144B88();
            if (Flags2.TestFlag(AiFlags2.Bit8))
            {
                Func2143A40();
            }
            else if (Flags4.TestFlag(AiFlags4.Bit1))
            {
                // Preserve useful charge while the reticle is still settling.
                Func214380C();
            }
        }

        private void UpdateTacticalHearing(BotTacticalTuning tuning)
        {
            float hearingSqr = tuning.HearingRange * tuning.HearingRange;
            float nearest = Single.MaxValue;
            PlayerEntity? heard = null;
            foreach (PlayerEntity other in _scene.GetPlayerEntities())
            {
                if (other == _player || other.Health == 0 || !other.ModInPlay
                    || other.TeamIndex == _player.TeamIndex || TacticalCanSee(other)
                    || !other.Flags2.TestFlag(PlayerFlags2.Shooting))
                {
                    continue;
                }

                float dist = Vector3.DistanceSquared(other.Position, _player.Position);
                if (dist <= hearingSqr && dist < nearest)
                {
                    nearest = dist;
                    heard = other;
                }
            }

            if (heard != null && (_tacticalThreatFrames == 0 || _tacticalThreatWeight < 12))
            {
                _tacticalThreatSlot = heard.SlotIndex;
                _tacticalThreatPosition = heard.Position;
                _tacticalThreatFrames = Math.Max(_tacticalThreatFrames, tuning.TargetMemoryFrames / 2);
                // Sound creates an investigation cue, not enough confidence to select a hidden target.
                _tacticalThreatWeight = Math.Max(_tacticalThreatWeight, 8);
            }
        }

        private bool TacticalTargetValid(PlayerEntity player)
        {
            return player != _player && player.Health > 0 && player.ModInPlay
                && player.TeamIndex != _player.TeamIndex;
        }

        private bool TacticalCanSee(PlayerEntity player)
        {
            if (!IsPlayerVisible(_player, player))
            {
                return false;
            }
            if (player.CurAlpha >= 1 || player.Flags2.TestFlag(PlayerFlags2.RadarReveal)
                || _scene.GameState.RadarPlayers || player.OctolithFlag != null || player.IsPrimeHunter)
            {
                return true;
            }
            // A cloaked attacker can still be tracked briefly if the legacy aggro system has
            // a legitimate observation/damage record for them.
            return AggroFunc214857C(6, 1, 2, null, player)
                || (_tacticalThreatSlot == player.SlotIndex && _tacticalThreatWeight >= 14);
        }

        private PlayerEntity? ChooseTacticalTarget(BotTacticalTuning tuning)
        {
            PlayerEntity? best = null;
            float bestScore = Single.MinValue;
            foreach (PlayerEntity candidate in _scene.GetPlayerEntities())
            {
                if (!TacticalTargetValid(candidate))
                {
                    continue;
                }

                bool visible = TacticalCanSee(candidate);
                bool known = AggroFunc214857C(6, 1, 2, null, candidate);
                bool recentThreat = _tacticalThreatSlot == candidate.SlotIndex && _tacticalThreatWeight >= 14;
                if (!visible && !known && !recentThreat)
                {
                    continue;
                }

                float distance = (candidate.Position - _player.Position).Length;
                bool occupyingObjective = TacticalOccupiesObjective(candidate);
                int carriedTokens = (uint)candidate.SlotIndex < (uint)_scene.GameState.TokenCarried.Length
                    ? _scene.GameState.TokenCarried[candidate.SlotIndex] : 0;
                float objectivePriority = TacticalObjectivePriorityForTest(_scene.GameState.Mode,
                    carriedTokens, occupyingObjective, candidate.OctolithFlag != null, candidate.IsPrimeHunter);
                bool objective = objectivePriority > 0;
                int aggro = AggroFunc2148394(7, 2, 1, candidate, null);
                float healthFraction = candidate.HealthMax <= 0 ? 1
                    : Math.Clamp(candidate.Health / (float)candidate.HealthMax, 0, 1);
                float score = TacticalTargetUtilityForTest(distance, visible,
                    candidate == _tacticalTarget, objective: false, healthFraction, aggro, recentThreat,
                    Math.Clamp(_player.BotLevel, 0, 3));
                score += objectivePriority;

                if (candidate == _tacticalTarget)
                {
                    score += tuning.TargetStickiness;
                }
                if (candidate.SlotIndex == _tacticalThreatSlot)
                {
                    score += _tacticalThreatWeight;
                }

                if (!objective)
                {
                    int alliesOnTarget=0;
                    foreach(PlayerEntity ally in _scene.GetPlayerEntities())
                    {
                        if(ally!=_player&&ally.IsBot&&ally.TeamIndex==_player.TeamIndex
                            && ally.AiData._targetPlayer==candidate)alliesOnTarget++;
                    }
                    score-=Math.Min(18,alliesOnTarget*(3+Math.Clamp(_player.BotLevel,0,3)));
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        private bool TacticalOccupiesObjective(PlayerEntity candidate)
        {
            GameMode mode = _scene.GameState.Mode;
            if (mode is not (GameMode.Nodes or GameMode.NodesTeams or GameMode.Defender
                or GameMode.DefenderTeams or GameMode.Hardpoint or GameMode.HardpointTeams))
            {
                return false;
            }

            foreach (NodeDefenseEntity node in _scene.GetNodeDefenseEntities())
            {
                if (_scene.GameState.IsHardpoint && node.Id != _scene.GameState.ActiveHardpointId)
                {
                    continue;
                }
                if ((uint)candidate.SlotIndex < (uint)node.OccupiedBy.Count
                    && node.OccupiedBy[candidate.SlotIndex])
                {
                    return true;
                }
                if (node.CapturedPlayer == candidate)
                {
                    return true;
                }
            }
            return false;
        }

        public static float TacticalObjectivePriorityForTest(GameMode mode, int carriedTokens,
            bool occupyingObjective, bool carriesFlag, bool primeHunter)
        {
            float score = 0;
            if (carriesFlag && mode is GameMode.Capture or GameMode.Bounty or GameMode.BountyTeams)
            {
                score = Math.Max(score, 36);
            }
            if (primeHunter && mode == GameMode.PrimeHunter)
            {
                score = Math.Max(score, 38);
            }
            if (carriedTokens > 0 && mode is GameMode.KillConfirmed or GameMode.KillConfirmedTeams or GameMode.Headhunter)
            {
                score = Math.Max(score, 18 + Math.Min(24, carriedTokens * 3));
            }
            if (occupyingObjective && mode is GameMode.Nodes or GameMode.NodesTeams
                or GameMode.Defender or GameMode.DefenderTeams or GameMode.Hardpoint or GameMode.HardpointTeams)
            {
                score = Math.Max(score, 32);
            }
            return score;
        }

        public static float TacticalTargetUtilityForTest(float distance, bool visible, bool current,
            bool objective, float healthFraction, int aggro, bool recentThreat, int difficulty)
        {
            distance = Math.Max(0, distance);
            healthFraction = Math.Clamp(healthFraction, 0, 1);
            difficulty = Math.Clamp(difficulty, 0, 3);

            float score = visible ? 52 : 8;
            score += Math.Max(-12, 25 - distance * 0.7f);
            score += (1 - healthFraction) * (10 + difficulty * 3);
            score += Math.Clamp(aggro, 0, 60) * 0.35f;
            if (objective) score += 32;
            if (recentThreat) score += 22;
            if (current) score += 10 + difficulty * 2;
            if (!visible && distance > 35) score -= 12;
            return score;
        }

        private BeamType ChooseTacticalWeapon(PlayerEntity target, bool visible)
        {
            if (_scene.GameState.InstaGib || _scene.GameState.Fiesta
                || _scene.GameState.Mode == GameMode.GunGame
                || _scene.GameState.OneInTheChamber)
            {
                return BeamType.None;
            }

            float distance = (target.Position - _player.Position).Length;
            BeamType affinity = Weapons.AffinityWeapons[(int)_player.Hunter];
            float selfHealth = _player.HealthMax <= 0 ? 1
                : Math.Clamp(_player.Health / (float)_player.HealthMax, 0, 1);

            BeamType best = BeamType.None;
            float bestScore = Single.MinValue;
            float currentScore = Single.MinValue;
            foreach (BeamType beam in _tacticalWeapons)
            {
                if (!CheckBeam(beam))
                {
                    continue;
                }

                float score = TacticalWeaponUtilityForTest(beam, distance, beam == affinity,
                    beam == _player.CurrentWeapon, target.IsAltForm, target.ModFrozen, selfHealth, visible);
                score += TacticalHunterWeaponBiasForTest(_player.Hunter, beam, distance, target.ModFrozen);
                if (beam == _player.CurrentWeapon)
                {
                    currentScore = score;
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = beam;
                }
            }

            // Do not thrash between two nearly equivalent guns. Weapon swaps cost time and,
            // more importantly, look robotic when the scores oscillate around a boundary.
            if ((int)_player.CurrentWeapon >= (int)BeamType.PowerBeam
                && (int)_player.CurrentWeapon <= (int)BeamType.OmegaCannon
                && CheckBeam(_player.CurrentWeapon) && currentScore >= bestScore - 7)
            {
                return _player.CurrentWeapon;
            }
            return best;
        }

        public static float TacticalWeaponUtilityForTest(BeamType beam, float distance, bool affinity,
            bool current, bool targetAltForm, bool targetFrozen, float selfHealthFraction, bool visible)
        {
            distance = Math.Max(0, distance);
            selfHealthFraction = Math.Clamp(selfHealthFraction, 0, 1);
            float ideal = TacticalPreferredRange(beam);
            float falloff = beam switch
            {
                BeamType.ShockCoil => 3.2f,
                BeamType.Battlehammer or BeamType.Magmaul => 2.2f,
                BeamType.Judicator => 1.8f,
                BeamType.Imperialist => 1.25f,
                _ => 1.4f
            };
            float score = 64 - MathF.Abs(distance - ideal) * falloff;

            score += beam switch
            {
                BeamType.PowerBeam => 0,
                BeamType.Missile => 8,
                BeamType.VoltDriver => 10,
                BeamType.Battlehammer => 12,
                BeamType.Imperialist => 18,
                BeamType.Judicator => 13,
                BeamType.Magmaul => 14,
                BeamType.ShockCoil => 20,
                BeamType.OmegaCannon => 46,
                _ => -100
            };

            if (beam == BeamType.Imperialist && distance < 9) score -= 58;
            if (beam == BeamType.ShockCoil && distance > 16) score -= 70;
            if ((beam == BeamType.Battlehammer || beam == BeamType.Magmaul || beam == BeamType.Judicator)
                && distance < 4) score -= 42;
            if (beam == BeamType.OmegaCannon && distance < 8) score -= 22;
            if (!visible && beam == BeamType.Imperialist) score -= 24;

            if (targetAltForm && (beam == BeamType.Battlehammer || beam == BeamType.Magmaul
                || beam == BeamType.Judicator)) score += 8;
            if (targetFrozen && (beam == BeamType.Imperialist || beam == BeamType.Magmaul)) score += 8;
            if (affinity) score += 10;
            if (current) score += 8;

            // Wounded bots should prefer weapons that let them create space rather than
            // charging into a Shock Coil duel simply because its close-range DPS is high.
            if (selfHealthFraction < 0.35f)
            {
                score += ideal >= 16 ? 9 : -8;
            }
            return score;
        }

        private static float TacticalPreferredRange(BeamType beam)
        {
            return beam switch
            {
                BeamType.PowerBeam => 15,
                BeamType.Missile => 18,
                BeamType.VoltDriver => 20,
                BeamType.Battlehammer => 8,
                BeamType.Imperialist => 28,
                BeamType.Judicator => 10,
                BeamType.Magmaul => 9,
                BeamType.ShockCoil => 7,
                BeamType.OmegaCannon => 18,
                _ => 14
            };
        }

        public static float TacticalHunterWeaponBiasForTest(Hunter hunter, BeamType beam,
            float distance, bool targetFrozen)
        {
            return hunter switch
            {
                Hunter.Samus when beam == BeamType.Missile => 8,
                Hunter.Kanden when beam == BeamType.VoltDriver => distance >= 8 ? 12 : 6,
                Hunter.Trace when beam == BeamType.Imperialist => distance >= 14 ? 20 : 4,
                Hunter.Sylux when beam == BeamType.ShockCoil => distance <= 14 ? 18 : 4,
                Hunter.Noxus when targetFrozen && beam is BeamType.Imperialist or BeamType.Magmaul => 16,
                Hunter.Noxus when beam == BeamType.Judicator => 14,
                Hunter.Spire when beam == BeamType.Magmaul => 14,
                Hunter.Weavel when beam == BeamType.Battlehammer => 14,
                _ => 0
            };
        }

        private float TacticalPreferredRangeForHunter(BeamType beam)
        {
            float range=TacticalPreferredRange(beam);
            if(_player.Hunter==Hunter.Trace&&beam==BeamType.Imperialist)range+=5;
            else if(_player.Hunter==Hunter.Sylux&&beam==BeamType.ShockCoil)range-=1;
            else if(_player.Hunter==Hunter.Noxus&&beam==BeamType.Judicator)range+=2;
            return Math.Max(3,range);
        }

        private void ApplyTacticalMovement(PlayerEntity target, BotTacticalTuning tuning)
        {
            if (_player.IsAltForm || _player.IsMorphing || _player.IsUnmorphing)
            {
                return;
            }

            Vector3 toTarget = (target.Position - _player.Position).WithY(0);
            float distance = toTarget.Length;
            if (distance <= 0.01f)
            {
                return;
            }

            BeamType weapon = _tacticalWeapon == BeamType.None ? _player.CurrentWeapon : _tacticalWeapon;
            float ideal = TacticalPreferredRangeForHunter(weapon);
            float healthFraction = _player.HealthMax <= 0 ? 1
                : Math.Clamp(_player.Health / (float)_player.HealthMax, 0, 1);
            bool retreat = healthFraction <= tuning.RetreatHealthFraction || distance < ideal * 0.60f;
            bool advance = !retreat && distance > ideal * 1.35f;

            if (_tacticalStrafeTimer <= 0)
            {
                _tacticalStrafeRight = _scene.Random.GetRandomInt2(2) == 0;
                int variance = Math.Max(1, tuning.StrafeFrames / 3);
                _tacticalStrafeTimer = tuning.StrafeFrames + (int)_scene.Random.GetRandomInt2(variance);
            }
            else
            {
                _tacticalStrafeTimer--;
            }

            if (_tacticalEscapeFrames > 0)
            {
                _tacticalEscapeFrames--;
                retreat = false;
                advance = false;
            }

            Vector3 forward = _player.FacingVector.WithY(0);
            forward = forward.LengthSquared > 0 ? forward.Normalized() : Vector3.UnitZ;
            Vector3 right = new Vector3(forward.Z, 0, -forward.X);

            bool moved = false;
            if (_tacticalEscapeFrames > 0)
            {
                moved = TryTacticalMove(_tacticalStrafeRight ? -right : right,
                    _tacticalStrafeRight ? _buttons.Y : _buttons.A);
            }
            else if (retreat)
            {
                moved = TryTacticalMove(-forward, _buttons.B);
            }
            else if (advance)
            {
                moved = TryTacticalMove(forward, _buttons.X);
            }

            if (!moved)
            {
                Vector3 strafe = _tacticalStrafeRight ? right : -right;
                moved = TryTacticalMove(strafe, _tacticalStrafeRight ? _buttons.A : _buttons.Y);
                if (!moved)
                {
                    _tacticalStrafeRight = !_tacticalStrafeRight;
                    strafe = _tacticalStrafeRight ? right : -right;
                    moved = TryTacticalMove(strafe, _tacticalStrafeRight ? _buttons.A : _buttons.Y);
                }
            }

            if (moved)
            {
                _tacticalMovementActive = true;
            }

            UpdateTacticalProgress(moved);

            bool obstacleJump = !moved && _player.BotLevel >= 1;
            bool combatJump = moved && _player.BotLevel >= 1 && _combatJumpCooldown == 0
                && _tacticalStrafeTimer == tuning.StrafeFrames / 2
                && _scene.Random.GetRandomInt2(100) < 4 + _player.BotLevel * 5;
            if ((obstacleJump || combatJump) && _combatJumpCooldown == 0
                && _player.Flags1.TestFlag(PlayerFlags1.Grounded)
                && !_player.Flags1.TestFlag(PlayerFlags1.UsedJump)
                && _buttons.L.FramesUp > 10 * 2)
            {
                _buttons.L.IsDown = true;
                _combatJumpCooldown = Math.Max(30, Difficulty.JumpCooldownMinFrames) * 2;
                _tacticalMovementActive = true;
            }
        }

        private bool TryTacticalMove(Vector3 direction, AiButton button)
        {
            if (!TacticalCanMove(direction))
            {
                return false;
            }
            button.IsDown = true;
            return true;
        }

        private bool TacticalCanMove(Vector3 direction)
        {
            direction = direction.WithY(0);
            if (direction.LengthSquared <= 0.001f)
            {
                return false;
            }
            direction.Normalize();

            Vector3 start = _player.Position.AddY(0.65f);
            Vector3 end = start + direction * 1.5f;
            CollisionResult wall = default;
            if (CollisionDetection.CheckBetweenPoints(start, end, TestFlags.None, _scene, ref wall))
            {
                return false;
            }

            Vector3 floorStart = end.AddY(0.75f);
            Vector3 floorEnd = end.AddY(-2.75f);
            CollisionResult floor = default;
            if (!CollisionDetection.CheckBetweenPoints(floorStart, floorEnd, TestFlags.None, _scene, ref floor))
            {
                return false;
            }
            return floor.Plane.Y >= 0.45f && (int)floor.Terrain < (int)Terrain.Lava;
        }

        private void UpdateTacticalProgress(bool moving)
        {
            if (!moving)
            {
                _tacticalProgressTimer = 0;
                _tacticalStuckCount = 0;
                _tacticalProgressPosition = _player.Position;
                return;
            }

            if (_tacticalProgressTimer <= 0)
            {
                float movedSqr = Vector3.DistanceSquared(_player.Position, _tacticalProgressPosition);
                if (movedSqr < 0.20f * 0.20f)
                {
                    _tacticalStuckCount++;
                }
                else
                {
                    _tacticalStuckCount = 0;
                }
                _tacticalProgressPosition = _player.Position;
                _tacticalProgressTimer = 30;

                if (_tacticalStuckCount >= 2)
                {
                    _tacticalStuckCount = 0;
                    _tacticalStrafeRight = !_tacticalStrafeRight;
                    _tacticalEscapeFrames = 45;
                    _combatJumpCooldown = 0;
                }
            }
            else
            {
                _tacticalProgressTimer--;
            }
        }

        public static bool TacticalViewportContainsForTest(Vector2 projected)
        {
            return projected.X >= 0 && projected.X < 1 && projected.Y >= 0 && projected.Y < 1;
        }
    }
}
