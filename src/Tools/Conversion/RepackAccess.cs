using System.Collections.Generic;
using MphRead.Editor;
using MphRead.Formats.Collision;

namespace MphRead.Utility
{
    public static partial class RepackCollision
    {
        public static byte[] PackMphCollision(IReadOnlyList<CollisionDataEditor> data, IReadOnlyList<Portal> portals)
        {
            return RepackMphCollision(data, portals);
        }
    }
}
