using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Sound;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public sealed class RoomEntityPresentation : EntityPresentation
    {
        private readonly RoomEntity _entity;
        public RoomEntityPresentation(RoomEntity entity, ScenePresentation presentation) : base(entity, presentation)
        {
            _entity = entity;
            for (int i = 0; i < _roomPartMax; i++)
            {
                _partVisInfo[i] = new RoomPartVisInfo();
                _roomFrustumItems[i] = new RoomFrustumItem();
            }
        }

        protected override void GetCollisionDrawInfo()
        {
            for (int i = 0; i < _entity._roomCollision.Count; i++)
            {
                CollisionInstance inst = _entity._roomCollision[i];
                CollisionInfo info = inst.Info;
                info.GetDrawInfo(info.Points, inst.Translation, Entity.Type, Presentation);
            }
        }

        public override void GetDrawInfo()
        {
            if (!Entity.Hidden)
            {
                if (!_scene.InRoomTransition)
                {
                    ModelInstance inst = Entity._models[0];
                    UpdateTransforms(inst, 0);
                    if (Presentation.ProcessFrame)
                    {
                        ClearRoomPartState();
                        UpdateRoomParts();
                    }

                    if (_partVisInfoHead == null || Presentation.ShowAllNodes)
                    {
                        DrawAllNodes(inst);
                    }
                    else
                    {
                        DrawRoomParts(inst);
                    }
                }
            }
            else if (Presentation.ProcessFrame) // skdebug
            {
                ClearRoomPartState();
                UpdateRoomParts();
            }

            if (Presentation.ShowCollision && (Presentation.ColEntDisplay == EntityType.All || Presentation.ColEntDisplay == Entity.Type))
            {
                GetCollisionDrawInfo();
            }

            if (_entity._nodeData != null && (Presentation.ShowNodeData || Presentation.ShowVolumes == VolumeDisplay.NodeData))
            {
                _drawnNodeData.Clear();
                Debug.Assert(Entity._models.Count == 2);
                ModelInstance inst = Entity._models[1];
                int polygonId = Presentation.GetNextPolygonId();
                for (int i = 0; i < _entity._nodeData.Data.Count; i++)
                {
                    IReadOnlyList<IReadOnlyList<NodeData3>> str1 = _entity._nodeData.Data[i];
                    for (int j = 0; j < str1.Count; j++)
                    {
                        IReadOnlyList<NodeData3> str2 = str1[j];
                        for (int k = 0; k < str2.Count; k++)
                        {
                            NodeData3 str3 = str2[k];
                            if (!_drawnNodeData.Contains(str3))
                            {
                                GetNodeDataItem(inst, str3.Transform, str3.Color, polygonId);
                                _drawnNodeData.Add(str3);
                                if (Presentation.ShowVolumes == VolumeDisplay.NodeData)
                                {
                                    var sphere = new CollisionVolume(str3.Position, str3.MaxDistance);
                                    AddVolumeItem(sphere, Vector3.UnitX);
                                }
                            }
                        }
                    }
                }
            }

            void GetNodeDataItem(ModelInstance inst, Matrix4 transform, Vector4 color, int polygonId)
            {
                Model model = inst.Model;
                Node node = model.Nodes[3];
                if (node.Enabled)
                {
                    int start = node.MeshId / 2;
                    for (int k = 0; k < node.MeshCount; k++)
                    {
                        Mesh mesh = model.Meshes[start + k];
                        if (!mesh.Visible)
                        {
                            continue;
                        }

                        Material material = model.Materials[mesh.MaterialId];
                        Presentation.AddRenderItem(material, polygonId, 1, Vector3.Zero, GetLightInfo(), Matrix4.Identity, transform, Presentation.GetMeshListId(mesh), 0, _entity._emptyMatrixStack, color, null, SelectionType.None, node.BillboardMode);
                    }
                }
            }
        }

        private bool IsNodeVisible(FrustumInfo frustumInfo, Node node, int mask, Vector3 offset)
        {
            float[] bounds = node.Bounds;
            for (int i = 0; i < frustumInfo.Count; i++)
            {
                Debug.Assert((mask & (1 << i)) != 0); // todo: if this never happens, we can just get rid of mask
                FrustumPlane frustumPlane = frustumInfo.Planes[i];
                // frustum planes have outward facing normals -- near plane = false if min bounds are on outer side,
                // right plane = false if min bounds are on outer side, left plane = false if max bounds are on outer side,
                // bottom plane = false if max bounds are on outer side, top plane = false if min bounds are on outer side
                Vector4 plane = frustumPlane.Plane;
                if (plane.X * (bounds[frustumPlane.XIndex2] + offset.X) + plane.Y * (bounds[frustumPlane.YIndex2] + offset.Y) + plane.Z * (bounds[frustumPlane.ZIndex2] + offset.Z) - plane.W < 0)
                {
                    return false;
                }

                if (plane.X * (bounds[frustumPlane.XIndex1] + offset.X) + plane.Y * (bounds[frustumPlane.YIndex1] + offset.Y) + plane.Z * (bounds[frustumPlane.ZIndex1] + offset.Z) - plane.W >= 0)
                {
                    mask &= ~(1 << i);
                }
            }

            return true;
        }

        private void DrawRoomParts(ModelInstance roomInst)
        {
            _entity._excludedNodes.Clear();
            if (PlayerEntity.Main.MorphCamera != null)
            {
                for (int i = 0; i < _entity._morphCameraExcludeNodes.Count; i++)
                {
                    _entity._excludedNodes.Add(_entity._morphCameraExcludeNodes[i]);
                }
            }

            RoomPartVisInfo? roomPart = _partVisInfoHead;
            while (roomPart != null)
            {
                RoomFrustumItem? frustumItem = _roomFrustumLinks[roomPart.NodeRef.PartIndex];
                int nodeIndex = roomPart.NodeRef.NodeIndex;
                int modelIndex = roomPart.NodeRef.ModelIndex;
                Debug.Assert(frustumItem != null);
                Debug.Assert(nodeIndex != -1);
                Debug.Assert(modelIndex != -1);
                Vector3 offset = Vector3.Zero;
                ModelInstance partInst;
                Matrix4 transform = Matrix4.Identity;
                partInst = Entity._models[0];
                if (!partInst.Active)
                {
                    roomPart = roomPart.Next;
                    continue;
                }

                while (nodeIndex != -1)
                {
                    Node? node = partInst.Model.Nodes[nodeIndex];
                    Debug.Assert(node.ChildIndex == -1);
                    if (!node.Enabled || node.MeshCount == 0 || _entity._excludedNodes.Contains(node))
                    {
                        nodeIndex = node.NextIndex;
                        continue;
                    }

                    RoomFrustumItem? frustumLink = frustumItem;
                    while (frustumLink != null)
                    {
                        if (IsNodeVisible(frustumLink.Info, node, 0x8FFF, offset))
                        {
                            if (offset != Vector3.Zero)
                            {
                                node.Animation = transform;
                            }

                            GetItems(partInst, node);
                            if (_entity._nodePairs.TryGetValue(node, out Node? exclude))
                            {
                                _entity._excludedNodes.Add(exclude);
                            }

                            break;
                        }

                        frustumLink = frustumLink.Next;
                    }

                    nodeIndex = node.NextIndex;
                }

                roomPart = roomPart.Next;
            }

            // todo: use visibility list for portals too
            if (Presentation.ShowForceFields)
            {
                for (int i = 0; i < _entity._forceFields.Count; i++)
                {
                    RoomEntity.PortalNodeRef forceField = _entity._forceFields[i];
                    Node pnode = _entity.Nodes[forceField.NodeIndex];
                    if (pnode.ChildIndex != -1)
                    {
                        Node node = _entity.Nodes[pnode.ChildIndex];
                        GetItems(roomInst, node, forceField.Portal);
                        int nextIndex = node.NextIndex;
                        while (nextIndex != -1)
                        {
                            node = _entity.Nodes[nextIndex];
                            GetItems(roomInst, node, forceField.Portal);
                            nextIndex = node.NextIndex;
                        }
                    }
                }
            }
        }

        private void DrawAllNodes(ModelInstance inst)
        {
            _entity._excludedNodes.Clear();
            IReadOnlyList<Node> nodes = inst.Model.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node pnode = nodes[i];
                if (!pnode.Enabled)
                {
                    continue;
                }

                if (Presentation.ShowAllNodes)
                {
                    GetItems(inst, pnode);
                }
                else if (pnode.RoomPartId >= 0)
                {
                    int nodeIndex = pnode.ChildIndex;
                    while (nodeIndex != -1)
                    {
                        Node node = nodes[nodeIndex];
                        if (!_entity._excludedNodes.Contains(node))
                        {
                            GetItems(inst, node);
                            if (_entity._nodePairs.TryGetValue(node, out Node? exclude))
                            {
                                _entity._excludedNodes.Add(exclude);
                            }
                        }

                        nodeIndex = node.NextIndex;
                    }
                }
            }

            if (Presentation.ShowForceFields)
            {
                for (int i = 0; i < _entity._forceFields.Count; i++)
                {
                    RoomEntity.PortalNodeRef forceField = _entity._forceFields[i];
                    Node pnode = _entity.Nodes[forceField.NodeIndex];
                    if (pnode.ChildIndex != -1)
                    {
                        Node node = _entity.Nodes[pnode.ChildIndex];
                        GetItems(inst, node, forceField.Portal);
                        int nextIndex = node.NextIndex;
                        while (nextIndex != -1)
                        {
                            node = _entity.Nodes[nextIndex];
                            GetItems(inst, node, forceField.Portal);
                            nextIndex = node.NextIndex;
                        }
                    }
                }
            }
        }

        private void GetItems(ModelInstance inst, Node node, Portal? portal = null)
        {
            if (!node.Enabled)
            {
                return;
            }

            Model model = inst.Model;
            int start = node.MeshId / 2;
            for (int k = 0; k < node.MeshCount; k++)
            {
                int polygonId = 0;
                Mesh mesh = model.Meshes[start + k];
                if (!mesh.Visible)
                {
                    continue;
                }

                Material material = model.Materials[mesh.MaterialId];
                float alpha = 1.0f;
                if (portal != null)
                {
                    polygonId = Presentation.GetNextPolygonId();
                    alpha = GetPortalAlpha(portal.Position, Presentation.CameraPosition);
                }
                else if (material.RenderMode == RenderMode.Translucent)
                {
                    polygonId = Presentation.GetNextPolygonId();
                }

                Matrix4 texcoordMatrix = GetTexcoordMatrix(inst, material, mesh.MaterialId, node);
                SelectionType selectionType = Selection.CheckSelection(_entity, inst, node, mesh);
                Presentation.AddRenderItem(material, polygonId, alpha, emission: Vector3.Zero, GetLightInfo(), texcoordMatrix, node.Animation, Presentation.GetMeshListId(mesh), model.NodeMatrixIds.Count, model.MatrixStackValues, overrideColor: null, paletteOverride: null, selectionType, node.BillboardMode);
            }
        }

        private float GetPortalAlpha(Vector3 portalPosition, Vector3 cameraPosition)
        {
            float between = (portalPosition - cameraPosition).Length;
            between /= 8;
            if (between < 1 / 4096f)
            {
                between = 0;
            }

            return MathF.Min(between, 1);
        }

        public override void GetDisplayVolumes()
        {
            if (Presentation.ShowVolumes == VolumeDisplay.NodeBounds)
            {
                if (Selection.Node != null && _entity.Nodes.Contains(Selection.Node))
                {
                    Node node = Selection.Node;
                    float width = node.MaxBounds.X - node.MinBounds.X;
                    float height = node.MaxBounds.Y - node.MinBounds.Y;
                    float depth = node.MaxBounds.Z - node.MinBounds.Z;
                    var box = new CollisionVolume(Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ, node.MinBounds.WithZ(node.MaxBounds.Z), width, height, depth);
                    AddVolumeItem(box, Vector3.UnitX);
                }
            }
            else if (Presentation.ShowVolumes == VolumeDisplay.Portal)
            {
                for (int i = 0; i < _entity._portals.Count; i++)
                {
                    Portal portal = _entity._portals[i];
                    if (!portal.Active)
                    {
                        continue;
                    }

                    int count = portal.Points.Count;
                    Vector3[] verts = ArrayPool<Vector3>.Shared.Rent(count);
                    for (int j = 0; j < count; j++)
                    {
                        verts[j] = portal.Points[j];
                    }

                    float alpha = GetPortalAlpha(portal.Position, Presentation.CameraPosition);
                    Vector4 color = portal.IsForceField ? new Vector4(16 / 31f, 16 / 31f, 1f, alpha) : new Vector4(16 / 31f, 1f, 16 / 31f, alpha);
                    Presentation.AddRenderItem(CullingMode.Neither, Presentation.GetNextPolygonId(), color, RenderItemType.Ngon, verts, count, noLines: true);
                }
            }
            else if (Presentation.ShowVolumes == VolumeDisplay.KillPlane && !_entity._meta.FirstHunt)
            {
                Vector3[] verts = ArrayPool<Vector3>.Shared.Rent(4);
                verts[0] = new Vector3(10000f, _scene.KillHeight, 10000f);
                verts[1] = new Vector3(10000f, _scene.KillHeight, -10000f);
                verts[2] = new Vector3(-10000f, _scene.KillHeight, -10000f);
                verts[3] = new Vector3(-10000f, _scene.KillHeight, 10000f);
                var color = new Vector4(1f, 0f, 1f, 0.5f);
                Presentation.AddRenderItem(CullingMode.Neither, Presentation.GetNextPolygonId(), color, RenderItemType.Quad, verts, noLines: true);
            }
            else if ((Presentation.ShowVolumes == VolumeDisplay.CameraLimit || Presentation.ShowVolumes == VolumeDisplay.PlayerLimit) && _entity._meta.HasLimits)
            {
                Vector3 minLimit = Presentation.ShowVolumes == VolumeDisplay.CameraLimit ? _entity._meta.CameraMin : _entity._meta.PlayerMin;
                Vector3 maxLimit = Presentation.ShowVolumes == VolumeDisplay.CameraLimit ? _entity._meta.CameraMax : _entity._meta.PlayerMax;
                Vector3[] bverts = ArrayPool<Vector3>.Shared.Rent(8);
                Vector3 point0 = minLimit;
                var sideX = new Vector3(maxLimit.X - minLimit.X, 0, 0);
                var sideY = new Vector3(0, maxLimit.Y - minLimit.Y, 0);
                var sideZ = new Vector3(0, 0, maxLimit.Z - minLimit.Z);
                bverts[0] = point0;
                bverts[1] = point0 + sideZ;
                bverts[2] = point0 + sideX;
                bverts[3] = point0 + sideX + sideZ;
                bverts[4] = point0 + sideY;
                bverts[5] = point0 + sideY + sideZ;
                bverts[6] = point0 + sideX + sideY;
                bverts[7] = point0 + sideX + sideY + sideZ;
                Vector4 color = Presentation.ShowVolumes == VolumeDisplay.CameraLimit ? new Vector4(1, 0, 0.69f, 0.5f) : new Vector4(1, 0, 0, 0.5f);
                Presentation.AddRenderItem(CullingMode.Neither, Presentation.GetNextPolygonId(), color, RenderItemType.Box, bverts, 8);
            }
        }

        private const int _roomPartMax = 32;
        private readonly bool[] _activeRoomParts = new bool[_roomPartMax];
        private readonly bool[] _audibleRoomParts = new bool[_roomPartMax];
        private int _visNodeRefRecursionDepth = 0;
        private int _audNodeRefRecursionDepth = 0;
        internal RoomPartVisInfo? _partVisInfoHead = null;
        private readonly RoomPartVisInfo[] _partVisInfo = new RoomPartVisInfo[_roomPartMax];
        private RoomPartVisInfo GetPartVisInfo(NodeRef nodeRef)
        {
            RoomPartVisInfo visInfo = _partVisInfo[nodeRef.PartIndex];
            if (!_activeRoomParts[nodeRef.PartIndex])
            {
                visInfo.NodeRef = nodeRef;
                visInfo.ViewMinX = 1;
                visInfo.ViewMaxX = 0;
                visInfo.ViewMinY = 1;
                visInfo.ViewMaxY = 0;
                visInfo.Next = _partVisInfoHead;
                _partVisInfoHead = visInfo;
            }

            return visInfo;
        }

        private int _roomFrustumIndex = 0;
        private readonly RoomFrustumItem[] _roomFrustumItems = new RoomFrustumItem[_roomPartMax];
        internal readonly RoomFrustumItem? [] _roomFrustumLinks = new RoomFrustumItem[_roomPartMax];
        private RoomFrustumItem GetRoomFrustumItem()
        {
            Debug.Assert(_roomFrustumIndex != _roomPartMax);
            return _roomFrustumItems[_roomFrustumIndex];
        }

        internal void ClearRoomPartState()
        {
            for (int i = 0; i < _roomPartMax; i++)
            {
                _activeRoomParts[i] = false;
                _roomFrustumLinks[i] = null;
                _audibleRoomParts[i] = false;
            }

            _visNodeRefRecursionDepth = 0;
            _audNodeRefRecursionDepth = 0;
            _roomFrustumIndex = 0;
            _partVisInfoHead = null;
        }

        internal void UpdateRoomParts()
        {
            NodeRef curNodeRef = PlayerEntity.Main.CameraInfo.NodeRef;
            if (Presentation.CameraMode != CameraMode.Player || curNodeRef.PartIndex == -1)
            {
                return;
            }

            // The camera has to actually be in the part it says it is in.
            //
            // Everything below walks the portal graph outwards from that part
            // and draws what it reaches, so a camera holding somebody else's
            // part describes a view from somewhere else in the map and the
            // room comes out black with the gun and a few pickups floating in
            // it. That is not hypothetical: a spectated player and every
            // player in a demo replay is a puppet whose node ref was looked
            // up from its position rather than walked, and a demo of a match
            // on MP1 SANCTORUS played back as an unlit void.
            //
            // Returning here leaves _partVisInfoHead null, which GetDrawInfo
            // already reads as "draw every part". The test is one-sided --
            // outside the part's own bounding box is outside the part -- so a
            // correct node ref never reaches it and the culling upstream does
            // is untouched.
            if (!_entity.PartCouldContain(curNodeRef.PartIndex, Presentation.CameraPosition, RoomEntity._partBoundsMargin))
            {
                return;
            }

            Debug.Assert(curNodeRef.NodeIndex != -1);
            RoomPartVisInfo curVisInfo = GetPartVisInfo(curNodeRef);
            curVisInfo.ViewMinX = 0;
            curVisInfo.ViewMaxX = 1;
            curVisInfo.ViewMinY = 0;
            curVisInfo.ViewMaxY = 1;
            _activeRoomParts[curNodeRef.PartIndex] = true;
            RoomFrustumItem curRoomFrustum = GetRoomFrustumItem();
            _roomFrustumIndex++;
            curRoomFrustum.Info.Count = Presentation.FrustumInfo.Count; // always 5
            curRoomFrustum.Info.Index = Presentation.FrustumInfo.Index; // always 1
            for (int i = 0; i < curRoomFrustum.Info.Planes.Length; i++)
            {
                curRoomFrustum.Info.Planes[i] = Presentation.FrustumInfo.Planes[i];
            }

            curRoomFrustum.NodeRef = curNodeRef;
            RoomFrustumItem? link = _roomFrustumLinks[curNodeRef.PartIndex];
            curRoomFrustum.Next = link;
            _roomFrustumLinks[curNodeRef.PartIndex] = curRoomFrustum;
            FindVisibleRoomParts(curRoomFrustum, curNodeRef);
            FindAudibleRoomParts(curNodeRef, curNodeRef);
        }

        private static readonly Vector3[] _startPointList = new Vector3[14];
        private static readonly Vector3[] _destPointList = new Vector3[14];
        private void FindVisibleRoomParts(RoomFrustumItem frustumItem, NodeRef mainNodeRef)
        {
            bool otherSide = false;
            for (int i = 0; i < _entity._portals.Count; i++)
            {
                Portal portal = _entity._portals[i];
                if (!portal.Active)
                {
                    continue;
                }

                if (portal.NodeRef1 == frustumItem.NodeRef)
                {
                    otherSide = false;
                }
                else if (portal.NodeRef2 == frustumItem.NodeRef)
                {
                    otherSide = true;
                }
                else
                {
                    continue;
                }

                Debug.Assert(portal.NodeRef1 != NodeRef.None);
                Debug.Assert(portal.NodeRef2 != NodeRef.None);
                Debug.Assert(portal.NodeRef1 != portal.NodeRef2);
                float minX = 1;
                float maxX = 0;
                float minY = 1;
                float maxY = 0;
                float dist = GetDistanceToPortal(Presentation.CameraPosition, portal.Plane, otherSide);
                if (dist < 0)
                {
                    continue;
                }

                // todo?: link this portal into the draw list
                if (portal.IsForceField && GetPortalAlpha(portal.Position, Presentation.CameraPosition) == 1)
                {
                    continue;
                }

                bool adjacent = false;
                if (dist < 0.5f)
                {
                    adjacent = true;
                    for (int j = 0; j < portal.Planes.Count; j++)
                    {
                        Vector4 plane = portal.Planes[j];
                        if (Vector3.Dot(Presentation.CameraPosition, plane.Xyz) - plane.W < Fixed.ToFloat(-4224))
                        {
                            adjacent = false;
                            break;
                        }
                    }
                }

                int v28;
                RoomFrustumItem nextFrustumItem = GetRoomFrustumItem();
                if (adjacent)
                {
                    // even if facing away, we're close enough to the portal that should consider its part visible
                    minX = 0;
                    maxX = 1;
                    minY = 0;
                    maxY = 1;
                    v28 = 4;
                    nextFrustumItem.Info.Index = frustumItem.Info.Index;
                    nextFrustumItem.Info.Count = frustumItem.Info.Count;
                    for (int j = 0; j < frustumItem.Info.Count; j++)
                    {
                        nextFrustumItem.Info.Planes[j] = frustumItem.Info.Planes[j];
                    }
                }
                else
                {
                    for (int j = 0; j < portal.Points.Count; j++)
                    {
                        _startPointList[j] = portal.Points[j];
                    }

                    v28 = Func21180A8(frustumItem.Info, _startPointList, portal.Points.Count, _destPointList);
                    if (v28 >= 3)
                    {
                        Debug.Assert(frustumItem.Info.Index + v28 <= 10);
                        // not entirely sure what it means for the index to be 0 or not
                        // --> basically we've got an "current count" (index) and a "new count" after adding v28?
                        // --> so not really the same usage as with the main frustum?
                        int index = frustumItem.Info.Index;
                        nextFrustumItem.Info.Index = index;
                        for (int j = 0; j < frustumItem.Info.Index; j++)
                        {
                            nextFrustumItem.Info.Planes[j] = frustumItem.Info.Planes[j];
                        }

                        // todo: names/purposes
                        nextFrustumItem.Info.Count = index;
                        for (int j = 0; j < v28; j++)
                        {
                            Vector3 point1 = _destPointList[j];
                            Vector3 point2 = _destPointList[j == v28 - 1 ? 0 : j + 1];
                            if (MathF.Abs(point1.X - point2.X) >= 1 / 4096f || MathF.Abs(point1.Y - point2.Y) >= 1 / 4096f || MathF.Abs(point1.Z - point2.Z) >= 1 / 4096f)
                            {
                                Vector3 normal;
                                Vector3 vec1 = point1 - Presentation.CameraPosition;
                                Vector3 vec2 = point2 - Presentation.CameraPosition;
                                if (otherSide)
                                {
                                    normal = Vector3.Cross(vec1, vec2).Normalized();
                                }
                                else
                                {
                                    normal = Vector3.Cross(vec2, vec1).Normalized();
                                }

                                var plane = new Vector4(normal, Vector3.Dot(normal, Presentation.CameraPosition));
                                nextFrustumItem.Info.Planes[index + j] = ScenePresentation.SetBoundsIndices(plane);
                                Vector3 destPoint = _startPointList[j];
                                if (Func2117F84(point1, ref destPoint) >= 0)
                                {
                                    minX = MathF.Min(minX, destPoint.X);
                                    maxX = MathF.Max(maxX, destPoint.X);
                                    minY = MathF.Min(minY, destPoint.Y);
                                    maxY = MathF.Max(maxY, destPoint.Y);
                                }

                                nextFrustumItem.Info.Count++;
                            }
                        }
                    }
                }

                if (v28 >= 3)
                {
                    minX = MathF.Max(minX, 0);
                    maxX = MathF.Min(maxX, 1);
                    minY = MathF.Max(minY, 0);
                    maxY = MathF.Min(maxY, 1);
                    // todo?: base these values on the aspect ratio or something?
                    if (minX < maxX - 1 / 800f && minY < maxY - 1 / 600f)
                    {
                        // todo?: link this portal into the draw list
                        NodeRef nextNodeRef = otherSide ? portal.NodeRef1 : portal.NodeRef2;
                        // todo: couldn't we bail a lot earlier for either condition?
                        if (nextNodeRef.PartIndex == mainNodeRef.PartIndex || _visNodeRefRecursionDepth < 6)
                        {
                            _visNodeRefRecursionDepth++;
                            RoomPartVisInfo nextVisInfo = GetPartVisInfo(nextNodeRef);
                            nextVisInfo.ViewMinX = MathF.Min(nextVisInfo.ViewMinX, minX);
                            nextVisInfo.ViewMaxX = MathF.Max(nextVisInfo.ViewMaxX, maxX);
                            nextVisInfo.ViewMinY = MathF.Min(nextVisInfo.ViewMinY, minY);
                            nextVisInfo.ViewMaxY = MathF.Max(nextVisInfo.ViewMaxY, maxY);
                            _activeRoomParts[nextNodeRef.PartIndex] = true;
                            _roomFrustumIndex++;
                            nextFrustumItem.NodeRef = nextNodeRef;
                            RoomFrustumItem? link = _roomFrustumLinks[nextNodeRef.PartIndex];
                            nextFrustumItem.Next = link;
                            _roomFrustumLinks[nextNodeRef.PartIndex] = nextFrustumItem;
                            FindVisibleRoomParts(nextFrustumItem, mainNodeRef);
                            _visNodeRefRecursionDepth--;
                        }
                    }
                }
            }
        }

        // todo: name/purpose
        private float Func2117F84(Vector3 point, ref Vector3 dest)
        {
            Matrix4 matrix = Presentation.ViewMatrix * Presentation.PerspectiveMatrix; // todo: no need to do this multiple times
            float v4 = point.X * matrix.Row0.W + point.Y * matrix.Row1.W + point.Z * matrix.Row2.W + matrix.Row3.W;
            if (v4 <= 0)
            {
                return v4;
            }

            dest = Matrix.Vec3MultMtx4(point, matrix);
            dest.X = (dest.X * 400 / v4 + 400) / 800;
            dest.Y = (dest.Y * 300 / v4 + 300) / 600;
            dest.Z /= v4;
            return 1 / v4;
        }

        // todo: name/purpose
        private int Func21180A8(FrustumInfo frustumInfo, Vector3[] pointList, int pointCount, Vector3[] destList)
        {
            Debug.Assert(frustumInfo.Count > 0);
            var temp1 = new Vector3[14];
            var temp2 = new Vector3[14];
            for (int i = 0; i < frustumInfo.Count; i++)
            {
                // first iteration: pointList is the list of points from the portal
                // next iterations: pointList is the list we built in temp1/temp2 in the last iteration
                int newPointCount = 0;
                Vector3[] newList;
                if (i == frustumInfo.Count - 1)
                {
                    newList = destList;
                }
                else
                {
                    newList = i % 2 == 0 ? temp1 : temp2;
                }

                Vector4 plane = frustumInfo.Planes[i].Plane;
                float dist1 = Vector3.Dot(pointList[0], plane.Xyz) - plane.W;
                bool v5 = dist1 >= 0;
                Debug.Assert(pointCount > 0);
                for (int j = 0; j < pointCount; j++)
                {
                    // each iteration, dist1 is the distance from point1 to the frustum plane,
                    // and dist2 is the distance from point2 to the portal, as we test successive edges
                    Vector3 point1 = pointList[j];
                    Vector3 point2 = pointList[j == pointCount - 1 ? 0 : j + 1];
                    if (v5)
                    {
                        newList[newPointCount++] = point1;
                    }

                    float dist2 = Vector3.Dot(point2, plane.Xyz) - plane.W;
                    bool v6 = dist2 >= 0;
                    if (v5 != v6)
                    {
                        float div = -dist1 / (dist2 - dist1);
                        newList[newPointCount++] = point1 + (point2 - point1) * div;
                    }

                    dist1 = dist2;
                    v5 = v6;
                }

                if (newPointCount == 0)
                {
                    return 0;
                }

                pointList = newList;
                pointCount = newPointCount;
            }

            return pointCount;
        }

        private float GetDistanceToPortal(Vector3 pos, Vector4 plane, bool otherSide)
        {
            float dist = Vector3.Dot(pos, plane.Xyz) - plane.W;
            if (otherSide)
            {
                dist *= -1;
            }

            return dist;
        }

        private void FindAudibleRoomParts(NodeRef nodeRef, NodeRef mainNodeRef)
        {
            _audibleRoomParts[nodeRef.PartIndex] = true;
            bool otherSide = false;
            for (int i = 0; i < _entity._portals.Count; i++)
            {
                Portal portal = _entity._portals[i];
                if (!portal.Active)
                {
                    continue;
                }

                if (portal.NodeRef1 == nodeRef)
                {
                    otherSide = false;
                }
                else if (portal.NodeRef2 == nodeRef)
                {
                    otherSide = true;
                }
                else
                {
                    continue;
                }

                if (portal.IsForceField && GetPortalAlpha(portal.Position, Presentation.CameraPosition) == 1)
                {
                    continue;
                }

                Debug.Assert(portal.NodeRef1 != NodeRef.None);
                Debug.Assert(portal.NodeRef2 != NodeRef.None);
                Debug.Assert(portal.NodeRef1 != portal.NodeRef2);
                float dist = GetDistanceToPortal(Presentation.CameraPosition, portal.Plane, otherSide);
                if (dist > Fixed.ToFloat(100000) || dist < Fixed.ToFloat(-100000))
                {
                    continue;
                }

                NodeRef nextNodeRef = otherSide ? portal.NodeRef1 : portal.NodeRef2;
                if (nextNodeRef.PartIndex == mainNodeRef.PartIndex || _audNodeRefRecursionDepth < 2)
                {
                    _audNodeRefRecursionDepth++;
                    FindAudibleRoomParts(nextNodeRef, mainNodeRef);
                    _audNodeRefRecursionDepth--;
                }
            }
        }

        public bool IsNodeRefAudible(NodeRef nodeRef)
        {
            if (nodeRef.PartIndex == -1)
            {
                return true;
            }

            return _audibleRoomParts[nodeRef.PartIndex];
        }

        public bool IsNodeRefVisible(NodeRef nodeRef)
        {
            // Nothing was culled at all this frame, so nothing may be culled
            // against it. This is the same test GetDrawInfo makes to decide
            // it must draw every part of the room -- the camera had no part
            // to walk the portal graph from -- and while it holds, the active
            // set is empty because it was never filled in, not because
            // everything is out of view.
            //
            // Read the other way round it hid every other player in the
            // match. A client joining one in progress spawns from a snapshot,
            // whose node ref is looked up from the spawn position and can
            // come back as none; nothing ever recovers it, because the
            // one-hop walk needs a part to start from. Its owner then played
            // a whole match in a room that drew perfectly and contained
            // nobody else -- opponents who could be shot and could not be
            // seen, with their shadows still moving about on the floor.
            // Straight out of a real match's log: MP6 HEADSHOT, slot 1
            // (local) nodeRef=none for two solid minutes while slot 0 held
            // a part the whole time.
            if (_partVisInfoHead == null || Presentation.ShowAllNodes)
            {
                return true;
            }

            // workaround for unintended modes
            if (nodeRef.PartIndex == -1)
            {
                return false;
            }

            return _activeRoomParts[nodeRef.PartIndex];
        }

        internal readonly HashSet<NodeData3> _drawnNodeData = [];
    }
}
