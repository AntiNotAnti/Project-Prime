using MphRead.Mods.Network;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    /// <summary>One bounded, authoritative Lingering Heat patch owned by one Spire.</summary>
    public sealed class SpireScorchPatchEntity : EntityBase
    {
        private readonly PlayerEntity _owner;
        private readonly CombatActor[] _hitVictims = new CombatActor[PlayerEntity.SlotCapacity];
        private ushort _ticks = EnhancedHunterTuning.SpireScorchLifetimeTicks;
        internal PlayerEntity Owner { get; }
        internal CombatShot CombatShot { get; }

        internal SpireScorchPatchEntity(PlayerEntity owner, in CombatShot shot,
            Vector3 position, NodeRef nodeRef, Scene scene)
            : base(EntityType.EnhancedEffect, nodeRef, scene)
        {
            _owner = Owner = owner;
            CombatShot = shot;
            Position = position;
            ShouldDraw = false;
        }

        public ushort RemainingTicks => _ticks;
        internal void Expire()
        {
            if (_ticks > 0) _scene.Services.ForgetWorldEntity(this);
            _ticks = 0;
        }

        public override bool Process()
        {
            if (_ticks == 0 || !_scene.Match.Rules.EnhancedHunters
                || _owner.ServerCombatIdentity != CombatShot.Actor)
            {
                Expire();
                _owner.ForgetEnhancedSpireScorch(this);
                return false;
            }
            _ticks--;
            float radiusSquared = EnhancedHunterTuning.SpireScorchRadius
                * EnhancedHunterTuning.SpireScorchRadius;
            foreach (PlayerEntity victim in _scene.GetPlayerEntities())
            {
                CombatActor identity = victim.ServerCombatIdentity;
                if (_hitVictims[victim.SlotIndex] == identity || victim == _owner || victim.Health == 0
                    || Vector3.DistanceSquared(victim.Position, Position) > radiusSquared) continue;
                int resolved = victim.TakeDamageResolved(
                    EnhancedHunterTuning.SpireScorchDamage,
                    DamageFlags.None, direction: null, source: this);
                if (resolved > 0) _hitVictims[victim.SlotIndex] = identity;
            }
            if (_ticks == 0)
            {
                _scene.Services.ForgetWorldEntity(this);
                _owner.ForgetEnhancedSpireScorch(this);
            }
            return _ticks > 0;
        }
    }
}
