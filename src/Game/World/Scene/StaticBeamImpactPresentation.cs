using System;
using System.Diagnostics;
using MphRead.Formats;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;

namespace MphRead
{
    /// <summary>
    /// Copied facts for one confirmed beam collision with immutable room geometry.
    /// This notification carries no damage, collision, entity, or network authority.
    /// </summary>
    public readonly record struct StaticBeamImpactPresentation
    {
        private StaticBeamImpactPresentation(ulong sourceIdentity,
            uint sourceGeneration, ulong eventTick, BeamType beam,
            Vector3 position, Vector3 normal, Terrain terrain,
            NodeRef nodeRef, int roomId)
        {
            SourceIdentity = sourceIdentity;
            SourceGeneration = sourceGeneration;
            EventTick = eventTick;
            Beam = beam;
            Position = position;
            Normal = normal;
            Terrain = terrain;
            NodeRef = nodeRef;
            RoomId = roomId;
        }

        public ulong SourceIdentity { get; }
        public uint SourceGeneration { get; }
        public ulong EventTick { get; }
        public BeamType Beam { get; }
        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public Terrain Terrain { get; }
        public NodeRef NodeRef { get; }
        public int RoomId { get; }
        public bool IsValid
        {
            get
            {
                float lengthSquared = Normal.LengthSquared;
                return SourceIdentity != 0 && Enum.IsDefined(Beam)
                    && Beam != BeamType.None && RoomId >= 0
                    && IsFinite(Position) && IsFinite(Normal)
                    && float.IsFinite(lengthSquared)
                    && MathF.Abs(lengthSquared - 1) <= 1e-4f
                    && Terrain is >= Terrain.Metal and <= Terrain.Rock;
            }
        }

        public static bool TryCreate(ulong sourceIdentity,
            uint sourceGeneration, ulong eventTick, BeamType beam,
            Vector3 position, Vector3 normal, Terrain terrain,
            NodeRef nodeRef, int roomId,
            out StaticBeamImpactPresentation impact)
        {
            impact = default;
            float normalLengthSquared = normal.LengthSquared;
            if (sourceIdentity == 0 || !Enum.IsDefined(beam)
                || beam == BeamType.None || roomId < 0
                || !IsFinite(position) || !IsFinite(normal)
                || !float.IsFinite(normalLengthSquared)
                || normalLengthSquared <= 1e-8f
                // Lava and later values are fluid, special, unknown, or
                // viewer-only terrain and are not safe planar-decal hosts.
                || terrain is < Terrain.Metal or > Terrain.Rock)
            {
                return false;
            }
            impact = new StaticBeamImpactPresentation(sourceIdentity,
                sourceGeneration, eventTick, beam, position,
                normal / MathF.Sqrt(normalLengthSquared), terrain, nodeRef,
                roomId);
            return true;
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
    }

    internal static class StaticBeamImpactPresentationNotification
    {
        /// <summary>
        /// Isolates the optional presentation callback. Only failures raised by
        /// that callback are handled; the caller's collision path remains outside
        /// this boundary and retains its normal exception behavior.
        /// </summary>
        public static bool TryObserve(IScenePresentation? presentation,
            in StaticBeamImpactPresentation impact, Action<string>? report)
        {
            if (presentation == null) return false;
            try
            {
                presentation.ObserveStaticBeamImpact(impact);
                return true;
            }
            catch (Exception error)
            {
                string message = "Static beam-impact presentation failed: "
                    + $"{error.GetType().Name}: {error.Message}";
                try
                {
                    report?.Invoke(message);
                }
                catch (Exception reportingError)
                {
                    TraceSafely(message + " Diagnostic reporting also failed: "
                        + $"{reportingError.GetType().Name}: "
                        + reportingError.Message);
                }
                TraceSafely(message);
                return false;
            }
        }

        private static void TraceSafely(string message)
        {
            try
            {
                Trace.TraceError(message);
            }
            catch (Exception)
            {
                // A failing diagnostic listener is also presentation-adjacent
                // and cannot be allowed to cross the collision boundary.
            }
        }
    }
}
