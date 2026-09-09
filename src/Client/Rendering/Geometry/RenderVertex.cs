using OpenTK.Mathematics;

namespace MphRead
{
    [System.Flags]
    public enum RenderVertexFlags : uint
    {
        None = 0,
        /// <summary>The source command explicitly supplied a vertex colour.</summary>
        ExplicitColor = 1u << 0
    }

    /// <summary>
    /// Backend-neutral vertex format shared by the Android translator and the
    /// future SDL upload path. MatrixIndex remains a vertex attribute because
    /// DS display lists can select a node matrix per vertex.
    /// </summary>
    public readonly struct RenderVertex : System.IEquatable<RenderVertex>
    {
        public Vector3 Position { get; }
        public Vector4 Color { get; }
        public Vector3 Normal { get; }
        public Vector2 TexCoord { get; }
        public uint MatrixIndex { get; }
        public RenderVertexFlags Flags { get; }

        public bool HasExplicitColor => (Flags & RenderVertexFlags.ExplicitColor) != 0;

        public RenderVertex(Vector3 position, Vector4 color, Vector3 normal, Vector2 texCoord,
            uint matrixIndex = 0, RenderVertexFlags flags = RenderVertexFlags.None)
        {
            Position = position;
            Color = color;
            Normal = normal;
            TexCoord = texCoord;
            MatrixIndex = matrixIndex;
            Flags = flags;
        }

        public bool Equals(RenderVertex other)
            => Position == other.Position && Color == other.Color && Normal == other.Normal
                && TexCoord == other.TexCoord && MatrixIndex == other.MatrixIndex && Flags == other.Flags;
        public override bool Equals(object? obj) => obj is RenderVertex other && Equals(other);
        public override int GetHashCode() => System.HashCode.Combine(Position, Color, Normal, TexCoord, MatrixIndex, Flags);
        public static bool operator ==(RenderVertex left, RenderVertex right) => left.Equals(right);
        public static bool operator !=(RenderVertex left, RenderVertex right) => !left.Equals(right);
    }
}
