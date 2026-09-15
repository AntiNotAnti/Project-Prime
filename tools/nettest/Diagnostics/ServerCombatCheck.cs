using System;
using MphRead.Entities;
using MphRead.NetTest;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    /// <summary>Extracted-content diagnostic. Direct hit probes validate the resolver, not live hit registration.</summary>
    public static class ServerCombatCheck
    {
        public static int Run(string data, string version, string room, bool balancedMode = false)
        {
            ServerContent.Open(data, version);
            Scene scene = Scene.CreateHeadless();
            try
            {
                scene.Match.ApplyRules(BalanceProfileOptions.CreateRules(room, GameMode.Battle, balancedMode));
                scene.LoadServerRoom(room, GameMode.Battle, players: 2);
                PlayerEntity shooter = scene.Players[0], target = scene.Players[1];
                var combat = new ServerCombat();
                scene.Services = new ServerSceneServices(combat);
                uint tick = 0;
                combat.BeginTick(tick);
                {
                    shooter.ServerActivate(100, Hunter.Samus, 0);
                    target.ServerActivate(200, Hunter.Kanden, 1);
                }
                combat.Consume(combat.Count);
                int covered = 0, damageEvents = 0;
                Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
                for (int weapon = 0; weapon <= 8; weapon++)
                {
                    combat.BeginTick(++tick);
                    {
                        target.Spawn(target.Position, target.FacingVector, Vector3.UnitY, target.NodeRef, respawn: false);
                        shooter.ModArmWeapon((BeamType)weapon);
                    }
                    bool fired = false;
                    for (int frame = 0; frame < 180; frame++)
                    {
                        combat.BeginTick(++tick);
                        var command = new InputCommand(tick, tick, tick, InputButtons.Shoot,
                            frame % 30 == 0 ? InputButtons.Shoot : 0, shooter.FacingVector, (byte)weapon);
                        combat.SetCommand(0, command);
                        shooter.ApplyNetworkInput(command);
                        scene.StepHeadlessFrame(advanceMatch: false);
                        while (combat.Count > 0)
                        {
                            int count = combat.CopyPending(events);
                            for (int i = 0; i < count; i++)
                                fired |= events[i].Kind == CombatEventKind.Shot && events[i].Weapon == weapon;
                            combat.Consume(count);
                        }
                    }
                    if (!fired) throw new ProgramException("Combat input produced no legal shot: " + (BeamType)weapon);
                    covered++;
                    combat.BeginTick(++tick);
                    {
                        // Use the normal weapon table and projectile owner identity. This
                        // controlled hit deliberately bypasses geometry to isolate resolution.
                        shooter.ModArmWeapon((BeamType)weapon);
                        shooter.EquipInfo.ChargeLevel = 0;
                        combat.SetCommand(0, new(tick, tick, tick, 0, 0, shooter.FacingVector, (byte)weapon));
                        BeamProjectileEntity.Spawn(shooter, shooter.EquipInfo, shooter.Position, shooter.FacingVector,
                            BeamSpawnFlags.NoMuzzle, shooter.NodeRef, scene);
                        BeamProjectileEntity? source = null;
                        foreach (BeamProjectileEntity beam in shooter.EquipInfo.Beams)
                            if (beam.Owner == shooter && beam.Beam == (BeamType)weapon) { source = beam; break; }
                        if (source == null) throw new ProgramException("No projectile for resolver probe.");
                        target.Health = 199;
                        target.TakeDamage(10, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, Vector3.Zero, source);
                        bool resolved = false;
                        while (combat.Count > 0)
                        {
                            int count = combat.CopyPending(events);
                            for (int i = 0; i < count; i++)
                                resolved |= events[i].Kind == CombatEventKind.Damage && events[i].Target.ConnectionId == 200
                                    && events[i].Actor.ConnectionId == 100 && events[i].Weapon == weapon;
                            combat.Consume(count);
                        }
                        if (!resolved) throw new ProgramException("Combat damage event missing: " + (BeamType)weapon);
                        damageEvents++;
                    }
                    Console.WriteLine($"[combatcheck] weapon={(BeamType)weapon} normal-input-shot=PASS controlled-resolver=PASS");
                }
                combat.BeginTick(++tick);
                {
                    foreach (BeamType weapon in new[] { BeamType.Judicator, BeamType.Magmaul, BeamType.VoltDriver })
                    {
                        target.Spawn(target.Position, target.FacingVector, Vector3.UnitY, target.NodeRef, respawn: false);
                        shooter.ModArmWeapon(weapon);
                        shooter.EquipInfo.ChargeLevel = 0;
                        BeamProjectileEntity.Spawn(shooter, shooter.EquipInfo, shooter.Position, shooter.FacingVector,
                            BeamSpawnFlags.NoMuzzle, shooter.NodeRef, scene);
                        BeamProjectileEntity? source = null;
                        foreach (BeamProjectileEntity beam in shooter.EquipInfo.Beams)
                            if (beam.Owner == shooter && beam.Beam == weapon) { source = beam; break; }
                        if (source == null) throw new ProgramException("Missing controlled affliction source.");
                        // Supply the retail charged affinity flags to the normal resolver.
                        // Geometry and charging are deliberately outside this diagnostic.
                        source.Afflictions = Weapons.Current[(int)weapon + 9].Afflictions[1];
                        target.TakeDamage(1, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, Vector3.Zero, source);
                        bool applied = weapon switch
                        {
                            BeamType.Judicator => target.ModFrozen,
                            BeamType.Magmaul => target.ModBurning,
                            _ => target.ModDisrupted
                        };
                        bool recorded = false;
                        while (combat.Count > 0)
                        {
                            int count = combat.CopyPending(events);
                            for (int i = 0; i < count; i++) recorded |= events[i].Kind == CombatEventKind.Affliction;
                            combat.Consume(count);
                        }
                        if (!applied || !recorded) throw new ProgramException("Missing controlled affliction: " + weapon);
                        Console.WriteLine($"[combatcheck] controlled-affliction={source.Afflictions} PASS");
                    }
                    uint previousLife = target.ServerCombatIdentity.Life;
                    target.TakeDamage(0, DamageFlags.Death | DamageFlags.IgnoreInvuln, null, shooter);
                    int deaths = 0;
                    while (combat.Count > 0)
                    {
                        int count = combat.CopyPending(events);
                        for (int i = 0; i < count; i++) if (events[i].Kind == CombatEventKind.Death) deaths++;
                        combat.Consume(count);
                    }
                    if (target.Health != 0 || deaths != 1) throw new ProgramException("Death event must be emitted exactly once.");
                    target.Spawn(target.Position, target.FacingVector, Vector3.UnitY, target.NodeRef, respawn: false);
                    if (target.ServerCombatIdentity.Life != previousLife + 1) throw new ProgramException("Respawn did not advance life.");
                    shooter.ModArmWeapon(BeamType.PowerBeam);
                    shooter.EquipInfo.ChargeLevel = 0;
                    BeamProjectileEntity.Spawn(shooter, shooter.EquipInfo, shooter.Position, shooter.FacingVector,
                        BeamSpawnFlags.NoMuzzle, shooter.NodeRef, scene);
                    BeamProjectileEntity? oldShot = null;
                    foreach (BeamProjectileEntity beam in shooter.EquipInfo.Beams)
                        if (beam.Owner == shooter && beam.CombatShot.Actor.ConnectionId == 100) { oldShot = beam; break; }
                    if (oldShot == null) throw new ProgramException("Missing pre-reconnect projectile.");
                    shooter.ServerDeactivate();
                    shooter.ServerActivate(300, Hunter.Samus, 0);
                    int health = target.Health;
                    target.TakeDamage(20, DamageFlags.IgnoreInvuln | DamageFlags.NoDmgInvuln, null, oldShot);
                    if (target.Health != health) throw new ProgramException("Stale projectile transferred to replacement connection.");
                    Console.WriteLine("[combatcheck] death-once=PASS respawn-life=PASS replacement-connection=PASS");
                }
                Console.WriteLine($"[combatcheck] PASS profile={(balancedMode ? "Balanced" : "Classic")} weapons={covered} controlled-damage={damageEvents} dropped={combat.Dropped}");
                return 0;
            }
            finally { scene.CloseHeadless(); }
        }
    }
}
