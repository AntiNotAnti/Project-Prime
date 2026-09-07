using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public class FhEnemySpawnEntity : EntityBase
    {
        private readonly FhEnemySpawnEntityData _data;
        protected override Vector4? OverrideColor { get; } = new ColorRgb(0x00, 0x00, 0x8B).AsVector4();

        // todo: FH enemy spawning
        public FhEnemySpawnEntity(FhEnemySpawnEntityData data, Scene scene) : base(EntityType.FhEnemySpawn, scene)
        {
            _data = data;
            Id = data.Header.EntityId;
            SetTransform(data.Header.FacingVector, data.Header.UpVector, data.Header.Position);
            AddPlaceholderModel();
        }
    }
}
