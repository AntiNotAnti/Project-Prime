using System;
using System.IO;
using OpenTK.Mathematics;
namespace MphRead.Mods.Cosmetics.Death
{
    public sealed class DeathPresentationState
    {
        public bool Active { get; private set; }
        public string Key { get; private set; } = "death.classic";
        public float StartTime { get; private set; }
        public uint Seed { get; private set; }
        public Vector3 Position { get; private set; }
        public Vector3 Facing { get; private set; }
        public bool WasAltForm { get; private set; }
        private ushort _life, _generation;
        private bool _alive;
        public void Observe(int health, ushort life, ushort generation, float time,
            CosmeticAppearance appearance, Vector3 position, Vector3 facing, bool alt, uint seed)
        {
            if (life != _life || generation != _generation || health > 0)
                Active = false;
            if (health <= 0 && _alive && life == _life && generation == _generation)
            {
                Active = true; Key = appearance.Death.Key; StartTime = time;
                Position = position; Facing = facing; WasAltForm = alt; Seed = seed;
            }
            _life = life; _generation = generation; _alive = health > 0;
        }
        public void Preview(CosmeticAppearance appearance, float time)
        { Active = true; Key = appearance.Death.Key; StartTime = time; Seed = 17; }
        internal void Write(BinaryWriter writer)
        {
            writer.Write(Active); writer.Write(CosmeticCatalog.ResolveDeath(Key).WireId);
            writer.Write(StartTime); writer.Write(Seed);
            writer.Write(Position.X); writer.Write(Position.Y); writer.Write(Position.Z);
            writer.Write(Facing.X); writer.Write(Facing.Y); writer.Write(Facing.Z);
            writer.Write(WasAltForm); writer.Write(_life); writer.Write(_generation); writer.Write(_alive);
        }
        internal void Read(BinaryReader reader)
        {
            Active = reader.ReadBoolean();
            Key = CosmeticCatalog.FromWire(Hunter.Samus, 0, 0, reader.ReadUInt16()).DeathEffectKey;
            StartTime = reader.ReadSingle(); Seed = reader.ReadUInt32();
            Position = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            Facing = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            WasAltForm = reader.ReadBoolean(); _life = reader.ReadUInt16(); _generation = reader.ReadUInt16(); _alive = reader.ReadBoolean();
            if (!float.IsFinite(StartTime) || !float.IsFinite(Position.LengthSquared) || !float.IsFinite(Facing.LengthSquared))
                throw new InvalidDataException("Invalid cosmetic death presentation.");
        }
        public void Reset() { Active = false; _alive = false; }
        public float Progress(float time, DeathPresentationDefinition definition) => Math.Clamp((time - StartTime) / definition.Duration, 0, 1);
    }
    public static class DeathPresentationRuntime
    {
        public static bool Visible(DeathPresentationState state, DeathPresentationDefinition definition, float time) =>
            state.Active && definition.WireId != 0 && RenderOptions.ShowCustomCosmetics
            && RenderOptions.CosmeticQuality != CosmeticEffectQuality.Off && time - state.StartTime < definition.Duration;
        public static CosmeticSurface Surface(CosmeticAppearance appearance, DeathPresentationDefinition definition, float time, float progress, bool team = false)
        {
            float dissolve = Math.Clamp((progress - definition.FadeStart)
                / Math.Max(0.01f, definition.HideBodyAt - definition.FadeStart), 0, 1);
            return new(appearance.Skin.SurfaceTreatment, (int)definition.SurfaceEffect,
                time, definition.LightEffect, Vector3.One, 0.95f, 5, 1.6f, dissolve, team);
        }
        public static Matrix4 Pose(DeathPresentationDefinition definition, float progress)
        {
            progress = Math.Clamp(progress, 0, 1);
            // Anticipation then acceleration for falls; eased departure for ascension.
            float fall = progress * progress * (2 - progress);
            float ease = progress * progress * (3 - 2 * progress);
            var transform = definition.PoseStyle switch
            {
                DeathPoseStyle.Backfall => Matrix4.CreateRotationX(-fall * 1.3f),
                DeathPoseStyle.KneelCollapse => Matrix4.CreateScale(1 - ease * 0.65f),
                DeathPoseStyle.Float => Matrix4.CreateTranslation(0, ease * 0.8f, 0),
                _ => Matrix4.Identity
            };
            return transform;
        }
    }
}
