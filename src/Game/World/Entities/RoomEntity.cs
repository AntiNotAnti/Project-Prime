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
    public class RoomEntity : EntityBase
    {
        internal readonly List<CollisionInstance> _roomCollision = new List<CollisionInstance>();
        public IReadOnlyList<CollisionInstance> RoomCollision => _roomCollision;
        internal readonly List<Portal> _portals = new List<Portal>();
        internal readonly List<List<(Portal Portal, bool OtherSide)>> _portalSides = new List<List<(Portal, bool)>>();
        internal readonly List<PortalNodeRef> _forceFields = new List<PortalNodeRef>();
        internal IReadOnlyList<Node> Nodes => _models[0].Model.Nodes;
        private int _nextRoomPartId = 0;
        internal RoomMetadata _meta = null!;
        internal NodeData? _nodeData;
        public NodeData? NodeData => _nodeData;
        internal readonly float[] _emptyMatrixStack = Array.Empty<float>();

        protected override bool UseNodeTransform => false; // default -- will use transform if setting is enabled
        public int RoomId { get; private set; }
        public RoomMetadata Meta => _meta;

        // The world-space box each room part's geometry occupies, unioned
        // over the nodes that part draws. Built on demand and rebuilt when
        // the part count changes (a door adds a connector part mid-room).
        //
        // Its whole purpose is to say where a part is *not*. A part is
        // bounded by the planes of the portals opening out of it and by
        // nothing else, so a part with two portals is an unbounded wedge and
        // several parts' wedges overlap -- which is why GetNodeRefByPosition
        // could answer "part 5" for a point at the other end of the map. The
        // box is a superset of the part's geometry, so a point outside it is
        // outside the part for certain, and rejecting on it can only ever
        // remove a wrong answer.
        private readonly List<Vector3> _partBoundsMin = new List<Vector3>();
        private readonly List<Vector3> _partBoundsMax = new List<Vector3>();
        private int _partBoundsBuiltFor = -1;

        /// <summary>
        /// How far outside a part's box a position may be and still be
        /// treated as possibly inside it, in units.
        ///
        /// The box covers the part's geometry, and a player stands *on* the
        /// geometry rather than inside it -- on a part made of nothing but a
        /// floor, an exact test puts every player above the room. A few units
        /// is enough for that and still tens of units short of the wrong end
        /// of a map, which is what the wedges were answering with.
        /// </summary>
        internal const float _partBoundsMargin = 4;
        internal readonly Dictionary<Node, Node> _nodePairs = new Dictionary<Node, Node>();
        internal readonly HashSet<Node> _excludedNodes = new HashSet<Node>();
        internal readonly List<Node> _morphCameraExcludeNodes = new List<Node>();

        // 1. move some/most of the setup from the constructor to a public method
        // 2. have scenesetup.load call that method with the room entity it creates
        // 3. have the room transition function call it with this instances
        // 4. --> the things that it updates should all be skipped in processing during room transition
        public RoomEntity(Scene scene) : base(EntityType.Room, scene)
        {

        }

        public void Setup(string name, RoomMetadata meta, CollisionInstance collision, int layerMask, int roomId)
        {
            // todo: unlock the corresponding multiplayer arena when visiting a new planet
            _portals.Clear();
            _portalSides.Clear();
            _forceFields.Clear();
            _nodePairs.Clear();
            _morphCameraExcludeNodes.Clear();
            _partBoundsBuiltFor = -1;
            _nextRoomPartId = 0;
            ModelInstance inst = Read.GetRoomModelInstance(name);
            if (_models.Count == 0)
            {
                _models.Add(inst);
                _scene.LoadModel(inst.Model, isRoom: true);
                inst.SetAnimation(0);
            }
            else
            {
                // todo?: unload collision, etc.
                _unloadModel = _models[0].Model;
                if (_unloadModel == inst.Model)
                {
                    _unloadModel = null;
                }
                _models[0] = inst;
            }
            inst.Model.FilterNodes(layerMask);
            if (meta.Name == "UNIT2_C6") // Tetra Vista
            {
                // manually disable a decal that isn't rendered in-game because it's not on a surface
                Nodes[46].Enabled = false;
            }
            else if (meta.Name == "UNIT1_RM4" || meta.Name == "MP3 PROVING GROUND") // Combat Hall
            {
                // depending on active partial rooms, either of these may be drawn,
                // but we have a rendering issue when both are, while the game looks the same either way
                _nodePairs.Add(Nodes[16], Nodes[26]); // after drawing skyLayer0, don't draw skyLayer3
                _nodePairs.Add(Nodes[25], Nodes[17]); // ...and vice versa
                _nodePairs.Add(Nodes[17], Nodes[25]); // after drawing skyLayer01, don't draw skyLayer04
                _nodePairs.Add(Nodes[26], Nodes[16]); // ...and vice versa
            }
            else if (meta.Name == "UNIT3_C2") // Cortex CPU
            {
                // hide a wall that mysteriously isn't visible when it renders in front of the morph camera
                _morphCameraExcludeNodes.Add(Nodes[16]);
            }
            _meta = meta;
            Model model = inst.Model;
            // portals are already filtered by layer mask
            _portals.AddRange(collision.Info.Portals);
            if (_portals.Count > 0)
            {
                IEnumerable<string> parts = _portals.Select(p => p.NodeName1).Concat(_portals.Select(p => p.NodeName2)).Distinct();
                for (int i = 0; i < inst.Model.Nodes.Count; i++)
                {
                    Node node = inst.Model.Nodes[i];
                    if (parts.Contains(node.Name))
                    {
                        node.RoomPartId = _nextRoomPartId++;
                        _portalSides.Add(new List<(Portal, bool)>());
                    }
                }
                for (int i = 0; i < _portals.Count; i++)
                {
                    Portal portal = _portals[i];
                    for (int j = 0; j < model.Nodes.Count; j++)
                    {
                        Node node = model.Nodes[j];
                        if (node.Name == portal.NodeName1)
                        {
                            Debug.Assert(node.RoomPartId >= 0);
                            Debug.Assert(node.ChildIndex != -1);
                            portal.NodeRef1 = new NodeRef(meta.Name, node.RoomPartId, node.ChildIndex, modelIndex: 0);
                            _portalSides[node.RoomPartId].Add((portal, false));
                        }
                        if (node.Name == portal.NodeName2)
                        {
                            Debug.Assert(node.RoomPartId >= 0);
                            Debug.Assert(node.ChildIndex != -1);
                            portal.NodeRef2 = new NodeRef(meta.Name, node.RoomPartId, node.ChildIndex, modelIndex: 0);
                            _portalSides[node.RoomPartId].Add((portal, true));
                        }
                    }
                }
                int pmagCount = 0;
                for (int i = 0; i < _portals.Count; i++)
                {
                    Portal portal = _portals[i];
                    if (!portal.Name.StartsWith("pmag"))
                    {
                        continue;
                    }
                    pmagCount++;
                    for (int j = 0; j < model.Nodes.Count; j++)
                    {
                        if (model.Nodes[j].Name == $"geo{portal.Name[1..]}")
                        {
                            _forceFields.Add(new PortalNodeRef(portal, j));
                            break;
                        }
                    }
                }
                // biodefense chamber 04 and 07 don't have the red portal geometry nodes
                Debug.Assert(_forceFields.Count == pmagCount
                    || model.Name == "biodefense chamber 04" || model.Name == "biodefense chamber 07");
            }
            else if (meta.RoomNodeName != null
                && model.Nodes.TryFind(n => n.Name == meta.RoomNodeName && n.ChildIndex != -1, out Node? roomNode))
            {
                roomNode.RoomPartId = _nextRoomPartId;
                _nextRoomPartId++;
            }
            else
            {
                foreach (Node node in model.Nodes)
                {
                    if (node.Name.StartsWith("rm"))
                    {
                        node.RoomPartId = _nextRoomPartId;
                        _nextRoomPartId++;
                        break;
                    }
                }
            }
            Debug.Assert(model.Nodes.Any(n => n.RoomPartId >= 0));
            collision.Translation = Vector3.Zero;
            if (_roomCollision.Count == 0)
            {
                _roomCollision.Add(collision);
            }
            else
            {
                _roomCollision[0] = collision;
            }
            RoomId = roomId;
            _scene.RoomId = roomId;
        }

        public void SetNodeData(NodeData? nodeData)
        {
            _nodeData = nodeData;
            if (nodeData != null && _models.Count < 2)
            {
                // using cached instance messes with placeholders since the room entity doesn't update its instances normally
                _models.Add(Read.GetModelInstance("pick_wpn_missile", noCache: true));
            }
        }

        public Portal? GetPortalByName(string name)
        {
            for (int i = 0; i < _portals.Count; i++)
            {
                Portal portal = _portals[i];
                if (portal.Name == name)
                {
                    return portal;
                }
            }
            return null;
        }

        public int LoadEntityId { get; set; } = -1;

        public void LoadRoom(bool resume)
        {
            PlayerEntity? player = PlayerEntity.Main;
            player.StopAllSfx();
            Hunter hunter = player.Hunter;
            int recolor = player.Recolor;
            if (_scene.TransitionRoomId == -1)
            {
                _scene.TransitionRoomId = _scene.RoomId;
            }
            _scene.ResetFrameCount();
            Rng.SetRng2(0);
            StartTransition(resume);
            _scene.ClearEffects();
            if (!resume)
            {
                PlayerEntity.Reset();
                PlayerEntity.Construct(_scene);
                if (_scene.Services.RebuildingRoom)
                {
                    // A networked match needs every slot rebuilt, not just
                    // this machine's: Scene.AddPlayer is inert once a room has
                    // loaded, so a slot missing here could never be filled for
                    // the rest of the map.
                    player = _scene.Services.RebuildPlayers(_scene, hunter, recolor);
                }
                else
                {
                    player = PlayerEntity.Create(hunter, recolor);
                    Debug.Assert(player != null);
                    // todo: revisit flags
                    player.LoadFlags |= LoadFlags.SlotActive;
                    player.LoadFlags |= LoadFlags.Active;
                    player.LoadFlags |= LoadFlags.Initial;
                    PlayerEntity.PlayerCount++;
                }
            }
            ProcessTransition();
            EndTransition();
            _scene.Audio.PlayRoomMusic(_scene.RoomId, 0);
            if (!resume)
            {
                _scene.InsertEntity(player);
            }
            player.ReloadInit = resume;
            player.Initialize();
            if (!resume)
            {
                _scene.InitEntity(player);
                _scene.InitEntity(player.Halfturret);
                if (_scene.Services.RebuildingRoom)
                {
                    _scene.Services.AfterRoomRebuild(_scene);
                }
            }
        }

        private void StartTransition(bool resume = false)
        {
            Debug.Assert(_scene.TransitionRoomId != -1);
            _scene.TransitionState = TransitionState.Process;
            foreach (EntityBase entity in _scene.Entities)
            {
                if (entity.Type == EntityType.Room || entity.Type == EntityType.Model
                    || entity.Type == EntityType.Player && resume)
                {
                    continue;
                }
                _scene.RemoveEntity(entity);
                entity.Destroy();

            }
            _scene.ClearNonPersistentEffects();
            // A cam sequence that was still running gets cut off here without
            // reaching the CanEnd branch that would have paired off whatever
            // mute counter it bumped -- leaving sound classes silenced for the
            // rest of the match. Client audio resets these once at initial connect;
            // a mid-session room transition needs the same reset.
            _scene.Audio.ResetSoundMutes();
            CamSeqEntity.Current = null;
            CameraSequence.Current = null;
            _scene.ClearMessageQueue();
            // todo?: unload more stuff
            for (int i = 0; i < PlayerEntity.Players.Count; i++)
            {
                PlayerEntity player = PlayerEntity.Players[i];
                player.ResetReferences();
            }
        }

        private void ProcessTransition()
        {
            Debug.Assert(_scene.TransitionRoomId != -1);
            RoomMetadata? roomMeta = Metadata.GetRoomById(_scene.TransitionRoomId);
            Debug.Assert(roomMeta != null);
            int entityLayer = -1;
            Rng.SetRng2(Rng.Rng2StartValue);

            (_, IReadOnlyList<EntityBase> entities) = SceneSetup.SetUpRoom(_scene.Match.Rules.Mode.ToLegacyMode(),
                _scene.Services.RoomPlayerCount,
                nodeLayerMask: 0, entityLayer, roomMeta, room: this, _scene, isRoomTransition: true);
            AiPersonality.LoadAll(_scene.Match.Rules.Mode.ToLegacyMode());
            SetNodeData(SceneSetup.LoadNodeData(roomMeta.NodePath, roomMeta.Id, _scene.Match.Rules.Mode.ToLegacyMode(), entities, roomMeta.FirstHunt));
            PlayerEntity.PlayerAiData.InitializeGlobals();
            for (int i = 0; i < entities.Count; i++)
            {
                EntityBase entity = entities[i];
                entity.Initialized = false;
                _scene.InsertEntity(entity);
                _scene.LoadedEntities.Enqueue(entity);
            }
            _scene.InitLoadedEntity(count: -1);

            _scene.TransitionState = TransitionState.End;
        }

        private Model? _unloadModel = null;

        private void EndTransition()
        {
            RoomMetadata? roomMeta = Metadata.GetRoomById(_scene.TransitionRoomId);
            Debug.Assert(roomMeta != null);
            ModelInstance inst = _models[0];
            _scene.LoadModel(inst.Model, isRoom: true);
            inst.SetAnimation(0);
            _scene.SetRoomValues(roomMeta);
            if (_roomCollision.Count > 0)
            {
                _roomCollision[0].Active = true;
            }
            foreach (EntityBase entity in _scene.Entities)
            {
                entity.Initialized = true;
            }
            for (int i = 0; i < PlayerEntity.Players.Count; i++)
            {
                PlayerEntity player = PlayerEntity.Players[i];
                if (player.IsBot)
                {
                    player.AiData.InitializeAtLoad();
                }
            }
            if (_unloadModel != null)
            {
                _scene.UnloadModel(_unloadModel);
            }
            _unloadModel = null;
            GC.Collect(generation: 2, GCCollectionMode.Forced, blocking: false, compacting: true);
            _scene.TransitionState = TransitionState.None;
            _scene.TransitionRoomId = -1;
        }

        public NodeRef GetNodeRefByName(string nodeName)
        {
            Model model = _models[0].Model;
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                Node node = model.Nodes[i];
                if (node.Name == nodeName)
                {
                    Debug.Assert(node.RoomPartId >= 0);
                    Debug.Assert(node.ChildIndex != -1);
                    // for the purposes this node ref is returned for, we can assume it belongs to the actual room and not a connector
                    return new NodeRef(Meta.Name, node.RoomPartId, node.ChildIndex, modelIndex: 0);
                }
            }
            return NodeRef.None;
        }

        private void EnsurePartBounds()
        {
            if (_partBoundsBuiltFor == _nextRoomPartId)
            {
                return;
            }
            _partBoundsBuiltFor = _nextRoomPartId;
            _partBoundsMin.Clear();
            _partBoundsMax.Clear();
            for (int i = 0; i < _nextRoomPartId; i++)
            {
                _partBoundsMin.Add(new Vector3(Single.MaxValue));
                _partBoundsMax.Add(new Vector3(Single.MinValue));
            }
            if (_models.Count > 0)
            {
                AddPartBounds(_models[0], Vector3.Zero);
            }
        }

        private void AddPartBounds(ModelInstance inst, Vector3 offset)
        {
            // The same walk DrawAllNodes does: a part is a parent node, and
            // what it draws is the chain of children hanging off it.
            IReadOnlyList<Node> nodes = inst.Model.Nodes;
            for (int i = 0; i < nodes.Count; i++)
            {
                Node pnode = nodes[i];
                int part = pnode.RoomPartId;
                if (part < 0 || part >= _partBoundsMin.Count)
                {
                    continue;
                }
                int nodeIndex = pnode.ChildIndex;
                while (nodeIndex != -1)
                {
                    Node node = nodes[nodeIndex];
                    // Nodes with no mesh have no bounds worth the name, and a
                    // degenerate one would stretch the box to the origin.
                    // Nothing is filtered on Enabled: a node switched off by
                    // the layer mask still says where the part is, and the box
                    // is only ever used to rule a part out.
                    if (node.MeshCount > 0)
                    {
                        var min = new Vector3(node.Bounds[0], node.Bounds[1], node.Bounds[2]) + offset;
                        var max = new Vector3(node.Bounds[3], node.Bounds[4], node.Bounds[5]) + offset;
                        _partBoundsMin[part] = Vector3.ComponentMin(_partBoundsMin[part], min);
                        _partBoundsMax[part] = Vector3.ComponentMax(_partBoundsMax[part], max);
                    }
                    nodeIndex = node.NextIndex;
                }
            }
        }

        /// <summary>
        /// How far inside this part's geometry box the position is, in units.
        /// Zero or less means outside, and <see cref="Single.MaxValue"/> means
        /// there is no box to judge by, which is not evidence either way.
        /// </summary>
        private float PartBoundsDepth(int partIndex, Vector3 position)
        {
            EnsurePartBounds();
            if (partIndex < 0 || partIndex >= _partBoundsMin.Count)
            {
                return Single.MaxValue;
            }
            Vector3 min = _partBoundsMin[partIndex];
            Vector3 max = _partBoundsMax[partIndex];
            if (min.X > max.X)
            {
                return Single.MaxValue;
            }
            float depth = MathF.Min(position.X - min.X, max.X - position.X);
            depth = MathF.Min(depth, MathF.Min(position.Y - min.Y, max.Y - position.Y));
            depth = MathF.Min(depth, MathF.Min(position.Z - min.Z, max.Z - position.Z));
            return depth;
        }

        /// <summary>
        /// Whether the part could hold this position at all. One-sided on
        /// purpose: false means "certainly not this part", true means only
        /// that the box does not rule it out.
        /// </summary>
        public bool PartCouldContain(int partIndex, Vector3 position, float margin)
        {
            float depth = PartBoundsDepth(partIndex, position);
            return depth == Single.MaxValue || depth > -margin;
        }

        /// <summary>
        /// Which room part a position is in, for the callers that cannot walk
        /// there -- a remote player whose position arrives over the wire, a
        /// projectile placed where it struck.
        ///
        /// The portal half-spaces are a necessary condition and not a
        /// sufficient one (see <see cref="_partBoundsMin"/>), so a candidate
        /// that passes them must also be somewhere near the part's own
        /// geometry, and of those the part the position sits deepest inside
        /// wins rather than the lowest-numbered one. When nothing passes both,
        /// the answer is none: a caller that knows it has no node ref can draw
        /// the player anyway, while a confidently wrong one culls them out of
        /// the room.
        /// </summary>
        public NodeRef GetNodeRefByPosition(Vector3 position)
        {
            NodeRef best = NodeRef.None;
            float bestDepth = 0;
            for (int i = 0; i < _portalSides.Count; i++)
            {
                NodeRef result = NodeRef.None;
                bool allInside = true;
                List<(Portal Portal, bool OtherSide)> partSides = _portalSides[i];
                for (int j = 0; j < partSides.Count; j++)
                {
                    (Portal portal, bool otherSide) = partSides[j];
                    float dist = Vector3.Dot(position, portal.Plane.Xyz) - portal.Plane.W;
                    if (otherSide)
                    {
                        dist *= -1;
                    }
                    if (dist < 0)
                    {
                        allInside = false;
                        break;
                    }
                    result = otherSide ? portal.NodeRef2 : portal.NodeRef1;
                }
                if (!allInside || result == NodeRef.None)
                {
                    continue;
                }
                float depth = PartBoundsDepth(result.PartIndex, position);
                if (depth == Single.MaxValue)
                {
                    // A part with no geometry to judge by: take it only if
                    // nothing better has been found, which is what this used
                    // to do for every part.
                    if (best == NodeRef.None)
                    {
                        best = result;
                    }
                    continue;
                }
                if (depth <= -_partBoundsMargin)
                {
                    continue;
                }
                if (best == NodeRef.None || depth > bestDepth)
                {
                    best = result;
                    bestDepth = depth;
                }
            }
            return best;
        }

        public NodeRef UpdateNodeRef(NodeRef current, Vector3 prevPos, Vector3 curPos)
        {
            Debug.Assert(current.PartIndex != -1);
            for (int i = 0; i < _portals.Count; i++)
            {
                Portal portal = _portals[i];
                if (!portal.Active)
                {
                    continue;
                }
                if (portal.NodeRef1.PartIndex == current.PartIndex)
                {
                    if (CollisionDetection.CheckPortBetweenPoints(portal, prevPos, curPos, otherSide: false))
                    {
                        return portal.NodeRef2;
                    }
                }
                if (portal.NodeRef2.PartIndex == current.PartIndex)
                {
                    if (CollisionDetection.CheckPortBetweenPoints(portal, prevPos, curPos, otherSide: true))
                    {
                        return portal.NodeRef1;
                    }
                }
            }
            return current;
        }

        internal readonly struct PortalNodeRef
        {
            public readonly Portal Portal;
            public readonly int NodeIndex;

            public PortalNodeRef(Portal portal, int nodeIndex)
            {
                Portal = portal;
                NodeIndex = nodeIndex;
            }
        }
    }
}
