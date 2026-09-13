using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using MphRead.Entities;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;
namespace MphRead.Formats.Collision { public static class CollisionPresentation {

        private static readonly IReadOnlyList<Vector4> _colors = new List<Vector4>()
        {
            /*  0 */ new Vector4(0.69f, 0.69f, 0.69f, 1f), // metal (gray)
            /*  1 */ new Vector4(1f, 0.612f, 0.153f, 1f), // orange holo (orange)
            /*  2 */ new Vector4(0f, 1f, 0f, 1f), // green holo (green)
            /*  3 */ new Vector4(0f, 0f, 0.858f, 1f), // blue holo (blue)
            /*  4 */ new Vector4(0.141f, 1f, 1f, 1f), // ice (light blue)
            /*  5 */ new Vector4(1f, 1f, 1f, 1f), // snow (white)
            /*  6 */ new Vector4(0.964f, 1f, 0.058f, 1f), // sand (yellow)
            /*  7 */ new Vector4(0.505f, 0.364f, 0.211f, 1f), // rock (brown)
            /*  8 */ new Vector4(0.984f, 0.701f, 0.576f, 1f), // lava (salmon)
            /*  9 */ new Vector4(0.988f, 0.463f, 0.824f, 1f), // acid (pink)
            /* 10 */ new Vector4(0.615f, 0f, 0.909f, 1f), // Gorea (purple)
            /* 11 */ new Vector4(0.85f, 0.85f, 0.85f, 1f) // unused (dark gray)
        };
public static void GetDrawInfo(this CollisionInfo info, IReadOnlyList<Vector3> points, Vector3 translation, EntityType entityType, ScenePresentation scene) { if (info is MphCollisionInfo mph) mph.GetDrawInfo(points, translation, entityType, scene); else if (info is FhCollisionInfo fh) fh.GetDrawInfo(points, translation, entityType, scene); }
        public static void GetDrawInfo(this MphCollisionInfo info, IReadOnlyList<Vector3> points, Vector3 translation,
            EntityType entityType, ScenePresentation scene)
        {
            //EntityBase? target = scene.Entities.FirstOrDefault(e => e.Type == EntityType.Model);
            //if (target != null)
            //{
            //    GetPartition(target.Position, points, entityType, scene);
            //    return;
            //}
            // todo: visualize extra things like slipperiness, reflection, damage
            int polygonId = scene.GetNextPolygonId();
            for (int i = 0; i < info.Data.Count; i++)
            {
                CollisionData data = info.Data[i];
                if (scene.ColTerDisplay != Terrain.All && scene.ColTerDisplay != data.Terrain)
                {
                    continue;
                }
                if ((scene.ColTypeDisplay == CollisionType.Player && data.IgnorePlayers)
                    || (scene.ColTypeDisplay == CollisionType.Beam && data.IgnoreBeams)
                    || (scene.ColTypeDisplay == CollisionType.Both && (data.IgnorePlayers || data.IgnoreBeams)))
                {
                    continue;
                }
                Vector4 color;
                if (scene.ColDisplayColor == CollisionColor.Entity)
                {
                    if (entityType == EntityType.Platform)
                    {
                        // teal
                        color = new Vector4(0.109f, 0.768f, 0.850f, 1f);
                    }
                    else if (entityType == EntityType.Object)
                    {
                        // magenta
                        color = new Vector4(0.952f, 0.105f, 0.635f, 1f);
                    }
                    else
                    {
                        // orange (room)
                        color = new Vector4(0.952f, 0.694f, 0.105f, 1f);
                    }
                }
                else if (scene.ColDisplayColor == CollisionColor.Terrain)
                {
                    color = _colors[(int)data.Terrain];
                }
                else if (scene.ColDisplayColor == CollisionColor.Type)
                {
                    if (data.IgnoreBeams)
                    {
                        // yellow
                        color = new Vector4(0.956f, 0.933f, 0.203f, 1f);
                    }
                    else if (data.IgnorePlayers)
                    {
                        // green
                        color = new Vector4(0.250f, 0.807f, 0.250f, 1f);
                    }
                    else
                    {
                        // purple (both)
                        color = new Vector4(0.807f, 0.250f, 0.776f, 1f);
                    }
                }
                else
                {
                    color = new Vector4(1, 0, 0, 1);
                }
                color.W = scene.ColDisplayAlpha;
                Debug.Assert(data.PointIndexCount >= 3 && data.PointIndexCount <= 10);
                Vector3[] verts = ArrayPool<Vector3>.Shared.Rent(data.PointIndexCount);
                for (int j = 0; j < data.PointIndexCount; j++)
                {
                    ushort pointIndex = info.PointIndices[data.PointStartIndex + j];
                    verts[j] = points[pointIndex] + translation;
                }
                scene.AddRenderItem(CullingMode.Back, polygonId, color, RenderPrimitive.Ngon, verts, data.PointIndexCount);
            }
        }


        public static void GetPartition(this MphCollisionInfo info, Vector3 point, IReadOnlyList<Vector3> points, EntityType entityType, ScenePresentation scene)
        {
            int entryIndex = info.EntryIndexFromPoint(point);
            int polygonId = scene.GetNextPolygonId();
            if (entryIndex < 0 || entryIndex > info.Entries.Count)
            {
                return;
            }
            CollisionEntry entry = info.Entries[entryIndex];
            for (int i = 0; i < entry.DataCount; i++)
            {
                CollisionData data = info.Data[info.DataIndices[entry.DataStartIndex + i]];
                if (scene.ColTerDisplay != Terrain.All && scene.ColTerDisplay != data.Terrain)
                {
                    continue;
                }
                if ((scene.ColTypeDisplay == CollisionType.Player && data.IgnorePlayers)
                    || (scene.ColTypeDisplay == CollisionType.Beam && data.IgnoreBeams)
                    || (scene.ColTypeDisplay == CollisionType.Both && (data.IgnorePlayers || data.IgnoreBeams)))
                {
                    continue;
                }
                Vector4 color;
                if (scene.ColDisplayColor == CollisionColor.Entity)
                {
                    if (entityType == EntityType.Platform)
                    {
                        // teal
                        color = new Vector4(0.109f, 0.768f, 0.850f, 1f);
                    }
                    else if (entityType == EntityType.Object)
                    {
                        // magenta
                        color = new Vector4(0.952f, 0.105f, 0.635f, 1f);
                    }
                    else
                    {
                        // orange (room)
                        color = new Vector4(0.952f, 0.694f, 0.105f, 1f);
                    }
                }
                else if (scene.ColDisplayColor == CollisionColor.Terrain)
                {
                    color = _colors[(int)data.Terrain];
                }
                else if (scene.ColDisplayColor == CollisionColor.Type)
                {
                    if (data.IgnoreBeams)
                    {
                        // yellow
                        color = new Vector4(0.956f, 0.933f, 0.203f, 1f);
                    }
                    else if (data.IgnorePlayers)
                    {
                        // green
                        color = new Vector4(0.250f, 0.807f, 0.250f, 1f);
                    }
                    else
                    {
                        // purple (both)
                        color = new Vector4(0.807f, 0.250f, 0.776f, 1f);
                    }
                }
                else
                {
                    color = new Vector4(1, 0, 0, 1);
                }
                color.W = scene.ColDisplayAlpha;
                Debug.Assert(data.PointIndexCount >= 3 && data.PointIndexCount <= 10);
                Vector3[] verts = ArrayPool<Vector3>.Shared.Rent(data.PointIndexCount);
                for (int j = 0; j < data.PointIndexCount; j++)
                {
                    ushort pointIndex = info.PointIndices[data.PointStartIndex + j];
                    verts[j] = points[pointIndex];
                }
                scene.AddRenderItem(CullingMode.Back, polygonId, color, RenderPrimitive.Ngon, verts, data.PointIndexCount);
            }
            Vector3[] bverts = ArrayPool<Vector3>.Shared.Rent(8);
            Vector3 point0 = info.MinPosition;
            Vector3i partInc = info.PartIndexFromEntry(entryIndex);
            point0 = point0.AddX(partInc.X * 4).AddY(partInc.Y * 4).AddZ(partInc.Z * 4);
            var sideX = new Vector3(4, 0, 0);
            var sideY = new Vector3(0, 4, 0);
            var sideZ = new Vector3(0, 0, 4);
            bverts[0] = point0;
            bverts[1] = point0 + sideZ;
            bverts[2] = point0 + sideX;
            bverts[3] = point0 + sideX + sideZ;
            bverts[4] = point0 + sideY;
            bverts[5] = point0 + sideY + sideZ;
            bverts[6] = point0 + sideX + sideY;
            bverts[7] = point0 + sideX + sideY + sideZ;
            polygonId = scene.GetNextPolygonId();
            var bcolor = new Vector4(1, 0.3f, 1, 0.5f);
            scene.AddRenderItem(CullingMode.Front, polygonId, bcolor, RenderPrimitive.Box, bverts, 8);
        }


        public static void GetDrawInfo(this FhCollisionInfo info, IReadOnlyList<Vector3> points, Vector3 translation,
            EntityType entityType, ScenePresentation scene)
        {
            //GetPartition(points, scene);
            //return;
            var color = new Vector4(Vector3.UnitX, 0.5f);
            color.W = scene.ColDisplayAlpha;
            int polygonId = scene.GetNextPolygonId();
            for (int i = info.Portals.Count; i < info.Data.Count; i++)
            {
                FhCollisionData data = info.Data[i];
                Debug.Assert(data.VectorCount >= 3 && data.VectorCount <= 8);
                Vector3[] verts = ArrayPool<Vector3>.Shared.Rent(data.VectorCount);
                for (int j = 0; j < data.VectorCount; j++)
                {
                    FhCollisionVector vector = info.Vectors[data.VectorStartIndex + j];
                    verts[j] = points[vector.Point2Index] + translation;
                }
                scene.AddRenderItem(CullingMode.Back, polygonId, color, RenderPrimitive.Ngon, verts, data.VectorCount);
            }
        }


        public static void GetPartition(this FhCollisionInfo info, List<Vector3> points, ScenePresentation scene)
        {
            int entryIndex = (int)scene.ShowVolumes;
            if (entryIndex <= 0)
            {
                return;
            }
            var color = new Vector4(Vector3.UnitX, 0.5f);
            color.W = scene.ColDisplayAlpha;
            int polygonId = scene.GetNextPolygonId();
            FhCollisionEntry entry = info.Entries[entryIndex];
            for (int i = 0; i < entry.DataCount; i++)
            {
                int dataIndex = info.DataIndices[entry.DataStartIndex + i];
                FhCollisionData data = info.Data[dataIndex];
                Debug.Assert(data.VectorCount >= 3 && data.VectorCount <= 8);
                Vector3[] verts = ArrayPool<Vector3>.Shared.Rent(data.VectorCount);
                for (int j = 0; j < data.VectorCount; j++)
                {
                    FhCollisionVector vector = info.Vectors[data.VectorStartIndex + j];
                    verts[j] = points[vector.Point2Index];
                }
                scene.AddRenderItem(CullingMode.Back, polygonId, color, RenderPrimitive.Ngon, verts, data.VectorCount);
            }
            Vector3[] bverts = ArrayPool<Vector3>.Shared.Rent(8);
            Vector3 minPoint = entry.MinBounds.ToFloatVector();
            Vector3 maxPoint = entry.MaxBounds.ToFloatVector();
            var sideX = new Vector3(maxPoint.X - minPoint.X, 0, 0);
            var sideY = new Vector3(0, maxPoint.Y - minPoint.Y, 0);
            var sideZ = new Vector3(0, 0, maxPoint.Z - minPoint.Z);
            bverts[0] = minPoint;
            bverts[1] = minPoint + sideZ;
            bverts[2] = minPoint + sideX;
            bverts[3] = minPoint + sideX + sideZ;
            bverts[4] = minPoint + sideY;
            bverts[5] = minPoint + sideY + sideZ;
            bverts[6] = minPoint + sideX + sideY;
            bverts[7] = minPoint + sideX + sideY + sideZ;
            polygonId = scene.GetNextPolygonId();
            var bcolor = new Vector4(1, 0.3f, 1, 0.5f);
            scene.AddRenderItem(CullingMode.Front, polygonId, bcolor, RenderPrimitive.Box, bverts, 8);
        }

}}
