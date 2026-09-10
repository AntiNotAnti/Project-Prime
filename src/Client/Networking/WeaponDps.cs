using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    internal enum WeaponDpsMeasurementKind
    {
        PowerBeam,
        VoltDriver,
        Missile,
        Battlehammer,
        Imperialist,
        Judicator,
        Magmaul,
        ShockCoil,
        OmegaCannon,
        Lockjaw,
        MorphBallBomb,
        Stinglarva
    }

    internal readonly record struct WeaponDpsSelection(WeaponDpsMeasurementKind Kind, BeamType? Beam,
        BombType? Bomb, Hunter? RequiredHunter)
    {
        internal bool Continuous => Kind == WeaponDpsMeasurementKind.ShockCoil;

        internal static bool TryParse(string? value, out WeaponDpsSelection selection)
        {
            string normalized = (value ?? "").Replace("-", "", StringComparison.Ordinal)
                .Replace("_", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
            if (normalized.Equals("Lockjaw", StringComparison.OrdinalIgnoreCase))
            {
                selection = new(WeaponDpsMeasurementKind.Lockjaw, null, BombType.Lockjaw, Hunter.Sylux);
                return true;
            }
            if (normalized.Equals("MorphBallBomb", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("MorphBomb", StringComparison.OrdinalIgnoreCase))
            {
                selection = new(WeaponDpsMeasurementKind.MorphBallBomb, null, BombType.MorphBall, Hunter.Samus);
                return true;
            }
            if (normalized.Equals("Stinglarva", StringComparison.OrdinalIgnoreCase))
            {
                selection = new(WeaponDpsMeasurementKind.Stinglarva, null, BombType.Stinglarva, Hunter.Kanden);
                return true;
            }
            if (Enum.TryParse(normalized, true, out BeamType beam)
                && beam >= BeamType.PowerBeam && beam <= BeamType.OmegaCannon)
            {
                selection = new((WeaponDpsMeasurementKind)(int)beam, beam, null, null);
                return true;
            }
            selection = default;
            return false;
        }
    }

    internal readonly record struct WeaponDpsMeasurementResult(int Damage, int Hits, int Healing,
        int FiringFrames, int? KillFrame)
    {
        internal double DurationSeconds => FiringFrames / 60.0;
        internal bool Killed => KillFrame.HasValue;
    }

    internal sealed class WeaponDpsMeasurement
    {
        private readonly int _requestedFrames;
        private int _previousVictimHealth;
        private int _previousShooterHealth;
        private int _damage;
        private int _hits;
        private int _healing;
        private int? _killFrame;

        internal WeaponDpsMeasurement(int victimHealth, int shooterHealth, int requestedFrames)
        {
            _previousVictimHealth = Math.Max(0, victimHealth);
            _previousShooterHealth = Math.Max(0, shooterHealth);
            _requestedFrames = Math.Max(1, requestedFrames);
        }

        internal void Observe(int victimHealth, int shooterHealth, int completedFiringFrames)
        {
            if (_killFrame.HasValue)
            {
                return;
            }
            int currentVictim = Math.Max(0, victimHealth);
            int currentShooter = Math.Max(0, shooterHealth);
            int loss = _previousVictimHealth - currentVictim;
            if (loss > 0)
            {
                _damage += loss;
                _hits++;
            }
            if (currentVictim == 0 && _previousVictimHealth > 0)
            {
                _killFrame = Math.Max(0, completedFiringFrames);
            }
            _previousVictimHealth = currentVictim;
            int healing = currentShooter - _previousShooterHealth;
            if (healing > 0) _healing += healing;
            _previousShooterHealth = currentShooter;
        }

        internal WeaponDpsMeasurementResult Result(int completedFiringFrames)
        {
            int frames = _killFrame ?? _requestedFrames;
            if (!_killFrame.HasValue && completedFiringFrames < _requestedFrames)
                frames = Math.Max(0, completedFiringFrames);
            return new(_damage, _hits, _healing, frames, _killFrame);
        }
    }

    /// <summary>
    /// How much damage a weapon or alt-form bomb mechanic does per second,
    /// against a target that does not move or fight back.
    ///
    /// Written because a weapon was changed on the strength of reading the
    /// code. The Shock Coil is the only beam that stays alive and re-tests
    /// collision every frame, and every beam hit carries NoDmgInvuln, so
    /// nothing limits its rate but the frame rate -- which this engine runs at
    /// twice the original. That reasoning was sound and it was still only
    /// reasoning: the scripted tour keeps Sylux in alt form laying bombs and
    /// barely fires the beam at anybody, so no check in the project could
    /// confirm or deny it.
    ///
    /// Health transitions are sampled after each submitted simulation frame.
    /// A lethal transition is counted before the dead-player input gate and
    /// freezes the firing window so respawn time cannot dilute the result.
    /// </summary>
    public sealed class WeaponDps : IRenderToolClient
    {
        private readonly IRenderToolHost _host;
        private readonly ScenePresentation _presentation;
        private readonly string _room;
        private readonly Hunter _hunter;
        private readonly WeaponDpsSelection _selection;
        private readonly double _seconds;
        private readonly float _distance;
        private int _frame;
        private int _firingFrames;
        private int _startHealth;
        private int _beamFrames;
        private int _placedFrame = -1;
        private bool _placed;
        private bool _mechanicSpawned;
        private WeaponDpsMeasurement? _measurement;

        /// <summary>
        /// Topped up to the player's own maximum, not to a large number. The
        /// engine clamps health to HealthMax, so writing 999 reads back as 99
        /// on the next frame -- which the first version of this probe counted
        /// as nine hundred points of damage in one hit.
        /// </summary>
        private static int FullHealth(PlayerEntity player) => Math.Max(1, player.HealthMax);

        public Scene Scene { get; }

        private WeaponDps(string room, Hunter hunter, WeaponDpsSelection selection, double seconds, float distance,
            IRenderToolHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _room = room;
            _hunter = selection.RequiredHunter ?? hunter;
            _selection = selection;
            _seconds = seconds;
            _distance = distance;
            MapAudit.ForceEveryone = true;
            Scene = new Scene(features: ClientMatchFeatures.Capture()) { Services = new ClientSceneServices(forceSpawn: true) };
            Scene.Players.MaxPlayers = Math.Max(Scene.Players.MaxPlayers, 2);
            _presentation = host.CreatePresentation(Scene);
            // The victim is slot 0 and the shooter is slot 1, deliberately.
            // PlayerEntity.ProcessInput refills the *main* player's controls
            // from the keyboard every frame, so anything written into slot 0
            // is gone before the simulation reads it -- the first version of
            // this probe held fire for twelve seconds and spawned no beam at
            // all. The victim is meant to stand still, which is exactly what
            // an empty keyboard gives it.
            Scene.AddPlayer(Hunter.Samus, recolor: 0, team: -1);
            Scene.AddPlayer(_hunter, recolor: 0, team: -1);
            for (int i = 2; i < Scene.Players.Count; i++)
            {
                Scene.Players[i].LoadFlags &= ~LoadFlags.Active;
            }
            for (int i = 0; i < Scene.Players.Count; i++)
            {
                Scene.Players[i].IsBot = false;
            }
            Scene.Players.ActiveCount = 2;
            Scene.LocalPlayerSlot = 0;
            Scene.AddRoom(room, GameMode.Battle, playerCount: NetConfig.RoomPlayerCount);
        }

        public void OnLoad()
        {
            _presentation.Size = _host.Size;
            _presentation.OnLoad();
            _presentation.OnResize();
        }

        public void OnFrame()
        {
            _presentation.OnSimulationFrame();
            RenderToolFrameResult frame = _host.Render(_presentation);
            if (!frame.Submitted)
            {
                return;
            }
            _frame++;
            Step();
            WeaponDpsMeasurementResult result = _measurement?.Result(_firingFrames) ?? default;
            if (result.Killed || _placed && _firingFrames >= Math.Max(1, (int)Math.Ceiling(_seconds * 60)))
            {
                _host.Close();
            }
            else if (_frame > (_seconds + 20) * 60)
            {
                _host.Close(); // never got set up; the report says so
            }
        }

        public void OnCapture(RenderCaptureResult capture) { }

        public void OnClosing()
        {
            _presentation.DoCleanup();
        }

        private static bool Alive(PlayerEntity player)
        {
            return player.LoadFlags.TestFlag(LoadFlags.Active)
                && player.LoadFlags.TestFlag(LoadFlags.Spawned) && player.Health > 0;
        }

        private void Step()
        {
            if (Scene.Players.Count < 2)
            {
                return;
            }
            PlayerEntity victim = Scene.Players[0];
            PlayerEntity shooter = Scene.Players[1];
            if (_measurement != null)
            {
                _measurement.Observe(Math.Clamp(victim.Health, 0, Math.Max(0, victim.HealthMax)),
                    Math.Clamp(shooter.Health, 0, Math.Max(0, shooter.HealthMax)), _firingFrames);
                if (_measurement.Result(_firingFrames).Killed)
                {
                    NetTestScript.Rest(shooter, wantBiped: _selection.Beam.HasValue);
                    NetTestScript.Rest(victim, wantBiped: true);
                    return;
                }
            }
            if (!Alive(shooter) || !Alive(victim))
            {
                NetTestScript.Rest(shooter, wantBiped: true);
                NetTestScript.Rest(victim, wantBiped: true);
                return;
            }
            if (_selection.Beam.HasValue && (shooter.IsAltForm || shooter.IsMorphing || shooter.IsUnmorphing))
            {
                NetTestScript.Rest(shooter, wantBiped: true);
                NetTestScript.Rest(victim, wantBiped: true);
                return;
            }
            if (!_placed)
            {
                // In front of the victim, on the floor the victim is standing
                // on -- an arbitrary bearing puts the shooter in a wall.
                Vector3 facing = victim.FacingVector;
                facing = new Vector3(facing.X, 0, facing.Z);
                facing = facing.LengthSquared < 0.001f ? Vector3.UnitZ : facing.Normalized();
                Vector3 spot = victim.Position + facing * _distance;
                shooter.Teleport(spot, -facing, Scene.GetNodeRefByPosition(spot));
                _placed = true;
                _placedFrame = _frame;
                _startHealth = victim.Health;
                if (_selection.Kind == WeaponDpsMeasurementKind.ShockCoil)
                    shooter.Health = Math.Max(1, FullHealth(shooter) / 2);
                _measurement = new WeaponDpsMeasurement(victim.Health, shooter.Health,
                    Math.Max(1, (int)Math.Ceiling(_seconds * 60)));
            }
            if (_selection.Beam is BeamType beam)
            {
                shooter.ModArmWeapon(beam);
                // Chest to chest. Between the two collision volumes' centres
                // sounds more precise and is worse: they sit low, so the shot
                // goes into the floor a couple of units short.
                Vector3 toVictim = victim.Position.AddY(0.5f) - shooter.Position.AddY(0.5f);
                if (toVictim.LengthSquared > 0.001f) shooter.ModSetAim(toVictim.Normalized());
                NetTestScript.HoldFire(shooter, down: true);
            }
            else
            {
                NetTestScript.Rest(shooter, wantBiped: false);
                if (!_mechanicSpawned)
                {
                    SpawnBombMechanic(shooter, victim);
                    _mechanicSpawned = true;
                }
            }
            NetTestScript.Rest(victim, wantBiped: true);
            // "It never fired" and "it fired and missed" are different
            // answers and only the second is about the weapon.
            foreach (EntityBase entity in Scene.Entities)
            {
                if (entity.Type == EntityType.BeamProjectile
                    && entity is BeamProjectileEntity shot && shot.Owner == shooter)
                {
                    _beamFrames++;
                    break;
                }
            }
            _firingFrames++;
        }

        private void SpawnBombMechanic(PlayerEntity shooter, PlayerEntity victim)
        {
            shooter.ModForceForm(true);
            Vector3 center = victim.Position;
            Vector3[] positions = _selection.Bomb == BombType.Lockjaw
                ? new[] { center + new Vector3(-1, 0, 0), center + new Vector3(0.5f, 0, 0.866f),
                    center + new Vector3(0.5f, 0, -0.866f) }
                : new[] { center };
            foreach (Vector3 position in positions)
            {
                BombEntity? bomb = BombEntity.Spawn(shooter,
                    PlayerEntity.GetTransformMatrix(Vector3.UnitZ, Vector3.UnitY, position), Scene);
                if (bomb == null) break;
                if (_selection.Bomb == BombType.Lockjaw && !shooter.TryRegisterLockjawBomb(bomb))
                {
                    bomb.Destroy();
                    Scene.RemoveEntity(bomb);
                    break;
                }
                bomb.NodeRef = Scene.GetNodeRefByPosition(position);
                bomb.Radius = Fixed.ToFloat(shooter.Values.BombRadius);
                bomb.SelfRadius = Fixed.ToFloat(shooter.Values.BombSelfRadius);
                bomb.Damage = (ushort)shooter.Values.BombDamage;
                bomb.EnemyDamage = (ushort)shooter.Values.BombEnemyDamage;
                if (_selection.Bomb != BombType.Lockjaw) bomb.Countdown = 1;
            }
        }

        private int Report()
        {
            if (!_placed || _firingFrames == 0)
            {
                Console.WriteLine($"DPSFAIL {_room} | {_hunter} {_selection.Kind} | never got set up");
                return 1;
            }
            WeaponDpsMeasurementResult result = _measurement!.Result(_firingFrames);
            double window = Math.Max(1, result.FiringFrames) / 60.0;
            string kill = result.KillFrame.HasValue
                ? $"killed {_startHealth} hp in {result.KillFrame.Value / 60.0:0.00} s"
                : $"did not kill {_startHealth} hp in {result.DurationSeconds:0.0} s";
            Console.WriteLine($"DPS {_room} | {_hunter} {_selection.Kind} at {_distance:0.0} units | {kill} | "
                + $"damage {result.Damage} | hits {result.Hits} | healing {result.Healing} | "
                + $"{result.Damage / window:0.0} per second | "
                + $"{(result.Hits > 0 ? result.Damage / (double)result.Hits : 0):0.0} per hit | "
                + $"{result.Hits / window:0.0} hits per second | "
                + $"beam alive on {_beamFrames} of {_firingFrames} frame(s)");
            return 0;
        }

        internal static int Run(string room, Hunter hunter, WeaponDpsSelection selection, double seconds, float distance)
        {
            IRenderToolHost? host = null;
            WeaponDps? probe = null;
            try
            {
                host = RenderToolHostFactory.Create(new Vector2i(320, 180),
                    "MphRead weapon probe", updateFrequency: 60, visible: false,
                    presentable: false);
                probe = new WeaponDps(room, hunter, selection, seconds,
                    Math.Clamp(distance, 0.5f, 40f), host);
                host.Run(probe);
                return probe.Report();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DPSCRASH {room} | {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
            finally
            {
                host?.Dispose();
            }
        }
    }
}
