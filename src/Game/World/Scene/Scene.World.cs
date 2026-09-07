using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using OpenTK.Mathematics;
namespace MphRead
{
 public partial class Scene
 {
        private float _killHeight = 0f;

        private float _frameTime = 0;
        private float _elapsedTime = 0;
        private float _globalElapsedTime = 0;
        private ulong _frameCount = 0;
        private ulong _liveFrames = 0;
        private bool _roomLoaded = false;
        private RoomEntity? _room = null;
        public int RoomId { get; set; } = -1;

        private static Language _language = Language.English;
        public static Language Language
        {
            get
            {
                if (Paths.IsMphKorea)
                {
                    return Language.Japanese;
                }
                return _language;
            }
            set
            {
                _language = value;
            }
        }
        public float FrameTime => _frameTime;
        public ulong FrameCount => _frameCount;
        public ulong LiveFrames => _liveFrames;
        public float ElapsedTime => _elapsedTime;
        public float GlobalElapsedTime => _globalElapsedTime;
        public float KillHeight => _killHeight;
        public RoomEntity? Room => _room;
        internal bool RoomLoaded => _roomLoaded;
        public Vector3 Light1Vector => _room?.Meta.Light1Vector ?? Vector3.Zero;
        public Vector3 Light2Vector => _room?.Meta.Light2Vector ?? Vector3.Zero;
        public Vector3 Light1Color => _room == null ? Vector3.Zero : new Vector3(
            _room.Meta.Light1Color.Red / 31f, _room.Meta.Light1Color.Green / 31f, _room.Meta.Light1Color.Blue / 31f);
        public Vector3 Light2Color => _room == null ? Vector3.Zero : new Vector3(
            _room.Meta.Light2Color.Red / 31f, _room.Meta.Light2Color.Green / 31f, _room.Meta.Light2Color.Blue / 31f);
        internal bool IsModelInUse(Model model) => _entities.Any(e => e.GetModels().Any(m => m.Model == model));

        // called before load
        public void AddRoom(string name, GameMode mode = GameMode.None, int playerCount = 0,
            int nodeLayerMask = 0, int entityLayerId = -1)
        {
            if (_roomLoaded)
            {
                throw new ProgramException("Cannot load more than one room in a scene.");
            }
            _roomLoaded = true;
            (RoomEntity room, RoomMetadata meta, CollisionInstance collision, IReadOnlyList<EntityBase> entities)
                = SceneSetup.LoadGame(name, this, mode, playerCount, nodeLayerMask, entityLayerId);
            _entities.AddFirst(room);
            Presentation?.InitEntity(room);
            _room = room;
            foreach (EntityBase entity in entities)
            {
                InsertEntityByType(entity);
                Debug.Assert(entity.Id != -1);
                _entityMap.Add(entity.Id, entity);
                Presentation?.InitEntity(entity);
                entity.Initialized = false;
            }
            SceneSetup.LoadItemResources(this);
            SceneSetup.LoadObjectResources(this);
            SceneSetup.LoadPlatformResources(this);
            Match.Flow.Setup();
            PlayerEntity.PlayerAiData.InitializeGlobals();
            _killHeight = meta.KillHeight;
            Presentation?.RoomLoaded(meta);
            for (int i = 0; i < PlayerEntity.Players.Count; i++)
            {
                PlayerEntity player = PlayerEntity.Players[i];
                if (player.LoadFlags.TestFlag(LoadFlags.SlotActive))
                {
                    InsertEntityByType(player);
                }
            }
            for (int i = 0; i < PlayerEntity.Players.Count; i++)
            {
                PlayerEntity player = PlayerEntity.Players[i];
                if (player.IsBot)
                {
                    player.AiData.InitializeAtLoad();
                }
            }

        }

        public void AddPlayer(Hunter hunter, int recolor = 0, int team = -1, Vector3? position = null)
        {
            if (!_roomLoaded)
            {
                var player = PlayerEntity.Create(hunter, recolor);
                if (player != null)
                {
                    player.ForcedSpawnPos = position;
                    // todo: revisit flags
                    player.LoadFlags |= LoadFlags.SlotActive;
                    player.LoadFlags |= LoadFlags.Active;
                    player.LoadFlags |= LoadFlags.Initial;
                    if (team != -1)
                    {
                        Debug.Assert(team == 0 || team == 1);
                        player.TeamIndex = team;
                    }
                    player.IsBot = PlayerEntity.PlayerCount >= 1;
                    PlayerEntity.PlayerCount++;
                }
            }
        }

        public NodeRef UpdateNodeRef(NodeRef current, Vector3 prevPos, Vector3 curPos)
        {
            return Room?.UpdateNodeRef(current, prevPos, curPos) ?? NodeRef.None;
        }

        public NodeRef GetNodeRefByName(string nodeName)
        {
            return Room?.GetNodeRefByName(nodeName) ?? NodeRef.None;
        }

        /// <summary>
        /// Whether a room part could hold this position at all -- false only
        /// when the part's own geometry is nowhere near it. See
        /// <c>RoomEntity.PartCouldContain</c>.
        /// </summary>
        public bool PartCouldContain(int partIndex, Vector3 position)
        {
            return Room?.PartCouldContain(partIndex, position, margin: 4) ?? true;
        }

        public NodeRef GetNodeRefByPosition(Vector3 position)
        {
            return Room?.GetNodeRefByPosition(position) ?? NodeRef.None;
        }

        public bool IsNodeRefAudible(NodeRef nodeRef)
        {
            return Presentation?.IsEntityAudible(nodeRef) ?? true;
        }

        internal void RestorePresentationClock(ulong frame, ulong liveFrames, float elapsed, float globalElapsed)
        {
            if (!float.IsFinite(elapsed) || !float.IsFinite(globalElapsed) || elapsed < 0 || globalElapsed < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsed));
            _frameCount = frame; _liveFrames = liveFrames; _elapsedTime = elapsed; _globalElapsedTime = globalElapsed;
            _frameTime = 1f / SimTicks.Hz;
        }

        public void ResetFrameCount()
        {
            _frameCount = 0;
        }
        private static readonly int _bombMax = 32;
        private readonly Queue<BombEntity> _inactiveBombs = new Queue<BombEntity>(_bombMax);
        private readonly List<BombEntity> _activeBombs = new List<BombEntity>(_bombMax);

        private void AllocateBombs()
        {
            for (int i = 0; i < _bombMax; i++)
            {
                _inactiveBombs.Enqueue(new BombEntity(this));
            }
        }

        public BombEntity? InitBomb()
        {
            if (_inactiveBombs.Count == 0)
            {
                return null;
            }
            return _inactiveBombs.Dequeue();
        }

        public void UnlinkBomb(BombEntity entry)
        {
            _activeBombs.Remove(entry);
            _inactiveBombs.Enqueue(entry);
        }

        public ConcurrentQueue<EntityBase> LoadedEntities { get; } = new ConcurrentQueue<EntityBase>();
        public bool InitEntities { get; set; }

        public void InitLoadedEntity(int count)
        {
            int i = 0;
            while ((count == -1 || i++ < count) && LoadedEntities.TryDequeue(out EntityBase? entity))
            {
                InitializeEntity(entity);
                SceneSetup.LoadEntityResources(entity, this);
            }
        }

        private void UpdateScene()
        {
            Presentation?.BeforeWorldUpdate();
            foreach (EntityBase entity in Entities)
            {
                if (entity.Initialized && !entity.Process())
                {
                    SendMessage(Message.Destroyed, entity, null, 0, 0, delay: 1);
                    // todo: need to handle destroying vs. unloading etc.
                    entity.Destroy();
                    RemoveEntity(entity);
                }
            }
            PlayerEntity.PlayerAiData.UpdateVisibilityAndGlobals(this);
            for (int i = 0; i < PlayerEntity.Players.Count; i++)
            {
                PlayerEntity.Players[i].ClosestNode = null;
            }
            for (int i = 0; i < PlayerEntity.Players.Count; i++)
            {
                PlayerEntity player = PlayerEntity.Players[i];
                if (player.IsBot && player.Health != 0)
                {
                    player.AiData.Process();
                }
            }
            Presentation?.AfterWorldUpdate();
            Match.Logic.UpdateState();

        }

        internal void InitializeWorld()
        {
            AllocateBombs();
            CollisionDetection.Init();
            foreach (EntityBase entity in Entities)
            {
                if (!entity.Initialized)
                {
                    Presentation?.PrepareEntity(entity);
                    entity.Initialize();
                    entity.Initialized = true;
                }
            }
            foreach (PlayerEntity player in PlayerEntity.Players)
            {
                if (player.LoadFlags.TestFlag(LoadFlags.SlotActive))
                {
                    Presentation?.PrepareEntity(player);
                    player.Initialize();
                }
            }
        }

        internal void CloseWorld()
        {
            foreach (EntityBase entity in Entities) entity.Destroy();
            PlatformEntity.DestroyBeams();
            ResetForceFieldLockProjectiles();
            _entities.Clear();
            _entityMap.Clear();
            _entityNodesByType.Clear();
        }

        internal void BeginFrame(bool advance)
        {
            _frameTime = 1 / 60f;
            if (advance)
            {
                _globalElapsedTime += _frameTime;
                if (Match.LegacyState == MatchState.InProgress) _elapsedTime += _frameTime;
            }
        }

        internal void ProcessWorldStep(bool waitingForServer)
        {
            Match.Flow.ProcessFrame();
            if (!waitingForServer && Match.LegacyState == MatchState.InProgress) UpdateScene();
            Services.AfterSimulation(this);
        }

        internal void EndFrame(bool waitingForServer)
        {
            if (!waitingForServer && Match.LegacyState == MatchState.InProgress)
            {
                ProcessMessageQueue();
                _liveFrames++;
            }
            _frameCount++;
            Match.Flow.UpdateTime();
        }

        public void SetRoomValues(RoomMetadata metadata)
        {
            _killHeight = metadata.KillHeight;
            Presentation?.SetRoomValues(metadata);
        }

 }
}
