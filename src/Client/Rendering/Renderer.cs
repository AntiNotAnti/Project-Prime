using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Effects;
using MphRead.Entities;
using MphRead.Export;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Formats.Culling;
using MphRead.Hud;
using MphRead.Mods.Content;
#if ANDROID
using OpenTK.Graphics.OpenGL;
#endif
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead
{
    public enum VolumeDisplay
    {
        None,
        LightColor1,
        LightColor2,
        TriggerParent,
        TriggerChild,
        AreaInside,
        AreaExit,
        MorphCamera,
        JumpPad,
        Teleporter,
        EnemyHurt,
        Object,
        FlagBase,
        DefenseNode,
        KillPlane,
        PlayerLimit,
        CameraLimit,
        NodeBounds,
        NodeData,
        Portal
    }

    public enum CollisionType
    {
        Any,
        Player,
        Beam,
        Both
    }

    public enum CollisionColor
    {
        None,
        Entity,
        Terrain,
        Type
    }

    public enum CameraMode
    {
        Pivot,
        Roam,
        Player
    }

    public partial class ScenePresentation : IScenePresentation
    {
        public Vector2i Size { get; set; }
        private Matrix4 _viewMatrix = Matrix4.Identity;
        private Matrix4 _viewInvRotMatrix = Matrix4.Identity;
        private Matrix4 _viewInvRotYMatrix = Matrix4.Identity;
        private Matrix4 _perspectiveMatrix = Matrix4.Identity;
        public Matrix4 PerspectiveMatrix => _perspectiveMatrix;

        private CameraMode _cameraMode = CameraMode.Pivot;
        public CameraMode CameraMode => _cameraMode;
        public bool ShowCursor => World.LocalPlayer!?.Flags1.TestFlag(PlayerFlags1.WeaponMenuOpen) == true;
        private float _pivotAngleY = 0.0f;
        private float _pivotAngleX = 0.0f;
        private float _pivotDistance = 5.0f;
        private Vector3 _cameraPosition = Vector3.Zero;
        private Vector3 _cameraFacing = -Vector3.UnitZ;
        private Vector3 _cameraUp = Vector3.UnitY;
        private Vector3 _cameraRight = Vector3.UnitX;
        private float _cameraFov = MathHelper.DegreesToRadians(78);
        private bool _leftMouse = false;
        private int _activeCutscene = -1;
        private Vector3 _priorCameraPos = Vector3.Zero;
        private Vector3 _priorCameraFacing = -Vector3.UnitZ;
        private float _priorCameraFov = MathHelper.DegreesToRadians(78);
        public FrustumInfo FrustumInfo { get; } = new FrustumInfo();

        private bool _showTextures = true;
        private bool _showColors = true;
        private bool _wireframe = false;
        // 0 - lines + fill, 1 - lines only, 2 - fill only
        private int _volumeEdges = 0;
        private bool _faceCulling = true;
        // The three the settings own. Read out of RenderOptions every time
        // rather than copied into a field when the scene was built: the
        // settings window opens from the pause menu *during* a match, so a
        // copy is a fog switch that does nothing until the next room -- which
        // is exactly how it behaved. The debug keys write to the same place,
        // so F, L and G still flip them and now agree with what the settings
        // page says.
        private bool FilteringOn
        {
            get => Mods.RenderOptions.TextureFiltering;
            set => Mods.RenderOptions.TextureFiltering = value;
        }

        private bool LightingOn
        {
            get => Mods.RenderOptions.Lighting;
            set => Mods.RenderOptions.Lighting = value;
        }

        private bool FogOn
        {
            get => Mods.RenderOptions.Fog;
            set => Mods.RenderOptions.Fog = value;
        }
        private bool _scanVisor = false;
        private int _showInvisible = 0;
        private bool _showNodeData = false;
        private VolumeDisplay _showVolumes = VolumeDisplay.None;
        private int _showBotAiSlot = -1;
        private bool _showCollision = false;
        private bool _showAllNodes = false;
        private bool _transformRoomNodes = false;
        private bool _outputCameraPos = false;

        // map each model's texture ID/palette ID combinations to the bound OpenGL texture ID and "onlyOpaque" boolean
        private int _textureCount = 0;
        private readonly Dictionary<int, TextureMap> _texPalMap = new Dictionary<int, TextureMap>();

#if ANDROID
        private int _shaderProgramId = 0;
        private int _rttShaderProgramId = 0;
        private int _shiftShaderProgramId = 0;
        private int _celShaderProgramId = 0;
        private readonly ShaderLocations _legacyShaderLocations = new ShaderLocations();
        private ShaderLocations _shaderLocations = null!;
        private readonly GlesEnhancedRuntime _glesEnhanced = new();
        private GlesEnhancedPlan _glesEnhancedPlan;
        private bool _glesEnhancedActive;
        private bool _glesEnhancedTextureRetirementPending;
#endif

        private Vector3 _light1Vector = Vector3.Zero;
        private Vector3 _light1Color = Vector3.Zero;
        private Vector3 _light2Vector = Vector3.Zero;
        private Vector3 _light2Color = Vector3.Zero;
        private bool _hasFog = false;
        private Vector4 _fogColor = Vector4.Zero;
        private int _fogOffset = 0;
        private int _fogSlope = 0;
        private Color4 _clearColor = new Color4(0f, 0f, 0f, 1f);
        private readonly float _nearClip = 0.0625f;
        private float _farClip = 0;
        private bool _useClip = false;
        private bool _frameAdvanceOn = false;
        private bool _frameAdvanceLastFrame = false;
        public bool FrameAdvance => _frameAdvanceOn;
        public bool FrameAdvanceLastFrame => _frameAdvanceLastFrame;
        /// <summary>
        /// True when the input edge requested the one simulation step that is
        /// about to be rendered. Hosts consume this before drawing; the flag
        /// is cleared only at the successful present boundary.
        /// </summary>
        public bool FrameAdvanceRequested => _advanceOneFrame;
        private bool _advanceOneFrame = false;
        private bool _recording = false;
        private RenderCaptureRequest? _pendingSdlScreenshot;
        private RenderCaptureRequest? _pendingSdlRecordingRequest;
        // Deterministic SDL render tools queue a one-picture capture before
        // OnDrawFrame. These requests are kept separate from the interactive
        // screenshot/recording requests so a tool can use SceneTarget or
        // ThumbnailTarget without changing the desktop UI capture state.
        private readonly List<RenderCaptureRequest> _pendingSdlToolCaptures = new();
#if ANDROID
        // Android's direct GLES HUD methods use this flag to redirect their
        // output into an SDL frame when the shared capture path is exercised.
        private bool _capturingPresentationFrame;
#endif
        private RenderPresentationStage _capturingPresentationStage;
        private int _framesRecorded = 0;
        public bool ProcessFrame => (PlaybackActive || World.FrameCount == 0 || !_frameAdvanceOn || _advanceOneFrame) && !_exiting;
        private bool _exiting = false;
        public bool Exiting => _exiting;

        public Matrix4 ViewMatrix => _viewMatrix;
        /// <summary>Complete immutable-for-the-frame submission snapshot.</summary>
        public RenderFrame CurrentRenderFrame => _renderFrame;
        public Matrix4 ViewInvRotMatrix => _viewInvRotMatrix;
        public Matrix4 ViewInvRotYMatrix => _viewInvRotYMatrix;
        public Vector3 CameraPosition => _cameraPosition;
        public bool ShowNodeData => _showNodeData;
        public bool ShowInvisibleEntities => _showInvisible != 0;
        public bool ShowAllEntities => _showInvisible == 2;
        public bool TransformRoomNodes => _transformRoomNodes;
        public bool ShowAllNodes { get => _showAllNodes; set => _showAllNodes = value; }
        public VolumeDisplay ShowVolumes => _showVolumes;
        public bool ShowForceFields => _showVolumes != VolumeDisplay.Portal;
        public bool ScanVisor => _scanVisor;
        public Vector3 Light1Vector => _light1Vector;
        public Vector3 Light1Color => _light1Color;
        public Vector3 Light2Vector => _light2Vector;
        public Vector3 Light2Color => _light2Color;
        public int ActiveCutscene => _activeCutscene;
        // todo: disallow if camera roll is not zero?
        public bool AllowCameraMovement => _activeCutscene == -1 || (_frameAdvanceOn && !_advanceOneFrame);

        public const int DisplaySphereStacks = 16;
        public const int DisplaySphereSectors = 24;

        private readonly KeyboardState _keyboardState;
        private readonly MouseState _mouseState;
        private readonly Action<string> _setTitle;
        private readonly Action _close;
        private readonly Mods.Network.ReplayPlaybackSession? _replaySession;
        private readonly bool _isolatedPresentation;
        private bool _audioActive;
        private bool _gameplayInputSuppressed;
        internal Action<ScenePresentation>? AdditionalOverlay { get; set; }

        private bool PlaybackActive => _replaySession?.IsActive
            ?? Mods.Network.ReplayPlayback.IsActive;
        private bool PlaybackSeeking => _replaySession?.IsSeeking
            ?? Mods.Network.ReplayPlayback.IsSeeking;
        private bool PlaybackAtEnd => _replaySession?.AtEnd
            ?? Mods.Network.ReplayPlayback.AtEnd;

        public bool IsEntityVisible(NodeRef nodeRef) => nodeRef == NodeRef.None || CameraMode != CameraMode.Player
            || ShowInvisibleEntities || PlaybackActive || IsNodeRefVisible(nodeRef);
        public bool IsEntityAudible(NodeRef nodeRef) => nodeRef == NodeRef.None || CameraMode != CameraMode.Player
            || (World.Room != null && ((RoomEntityPresentation)EntityPresentation.Get(World.Room, this)).IsNodeRefAudible(nodeRef));
        public Scene World { get; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Scene, ScenePresentation> _presentations = new();
        private sealed class CpuMeshCache
        {
            public List<CpuMeshCacheEntry> Entries { get; } = new List<CpuMeshCacheEntry>();
        }

        // MeshCompiler.CompileDisplayList is pure over this complete tuple:
        // the model-owned instruction stream, texture dimensions, texgen, and
        // room matrix semantics. GeometryIdentity is the stable owner key;
        // the parsed instruction lists are immutable after Read creates them.
        private sealed class CpuMeshCacheEntry
        {
            public IReadOnlyList<RenderInstruction> Instructions { get; }
            public int TextureWidth { get; }
            public int TextureHeight { get; }
            public bool Texgen { get; }
            public bool IsRoom { get; }
            public CpuMesh Mesh { get; }

            public CpuMeshCacheEntry(IReadOnlyList<RenderInstruction> instructions, int textureWidth,
                int textureHeight, bool texgen, bool isRoom, CpuMesh mesh)
            {
                Instructions = instructions;
                TextureWidth = textureWidth;
                TextureHeight = textureHeight;
                Texgen = texgen;
                IsRoom = isRoom;
                Mesh = mesh;
            }
        }

        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, CpuMeshCache> _cpuMeshCache = new();
        private readonly Dictionary<object, CpuMesh> _portableMeshes
            = new Dictionary<object, CpuMesh>(ReferenceEqualityComparer.Instance);
        public static ScenePresentation Get(Scene scene) => _presentations.TryGetValue(scene, out var presentation)
            ? presentation : throw new InvalidOperationException("The scene has no client presentation.");

        public ScenePresentation(Scene world, Vector2i size, KeyboardState keyboardState, MouseState mouseState,
            Action<string> setTitle, Action close, ISceneServices? sceneServices = null,
            Mods.Network.ReplayPlaybackSession? replaySession = null)
        {
            if (world.IsHeadless) throw new ArgumentException("A server scene cannot own client presentation.", nameof(world));
            World = world;
            _replaySession = replaySession;
            _isolatedPresentation = replaySession?.IsPassive == true;
            _audioActive = !_isolatedPresentation;
            _presentations.Add(world, this);
            world.Presentation = this;
            if (!_isolatedPresentation)
            {
                world.JumpPadActivated += Mods.WorldEvents.NoteJumpPad;
                world.PlayerTeleported += Mods.WorldEvents.NoteTeleport;
            }
            world.PlayerTeleported += (_, _) => ResetPoseHistory();
            world.Services = sceneServices ?? new Mods.Network.ClientSceneServices();
            Size = size;
#if ANDROID
            _shaderLocations = _legacyShaderLocations;
#endif
            _keyboardState = keyboardState;
            _mouseState = mouseState;
            _setTitle = setTitle;
            _close = close;
            ClientPresentationContentState content = ClientPresentationContent.Refresh();
            Announcer = Mods.Audio.AnnouncerService.FromOptionalPack(content.Announcer.Pack);
            AnnouncerAssets = content.AnnouncerAssets;
            if (!_isolatedPresentation) Music.Init(content);
        }

        public void AddRoom(string name, GameMode mode = GameMode.None, int playerCount = 0,
            int nodeLayerMask = 0, int entityLayerId = -1)
        {
            (RoomMetadata? metadata, _) = Metadata.GetRoomByName(name);
            if (metadata == null || !metadata.Multiplayer)
                throw new ProgramException("No supported multiplayer room with this name is known.");
            if (mode == GameMode.None)
                mode = metadata.Name == "AD1 TRANSFER LOCK BT" ? GameMode.Bounty : GameMode.Battle;
            MatchRules? admitted = _replaySession?.InitialRules
                ?? Mods.Network.AuthoritativePlay.Current?.Client.Accepted.Rules
                ?? Mods.Network.ReplayPlayback.InitialRules;
            World.Match.ApplyRules(admitted ?? MatchRules.CreateDefault(mode.ToMatchMode(), metadata.Name));
            if (admitted == null) Mods.GameSettings.ApplyMatchRules(World);
            _visualLightIdentities.ResetScope();
            ResetTransientVisualLights();
            World.AddRoom(name, mode, playerCount, nodeLayerMask, entityLayerId);
            if (admitted != null)
            {
                World.Match.MatchTime = admitted.TimeLimit.HasValue ? (float)admitted.TimeLimit.Value.TotalSeconds : -1;
                World.Match.RadarPlayers = admitted.PlayerRadar;
                World.Match.Phase = MatchPhase.WaitingForPlayers;
            }
        }

        public float ProjectionAspect => Size.X / (float)Size.Y;
        public float ProjectionFarClip => _useClip ? _farClip : 10000f;
        public Vector3 ViewPosition => CameraPosition;
        public bool ControlsPlayer => CameraMode == CameraMode.Player;
        public bool MinimalResources => Mods.ThumbnailMode.Active;

        public void RoomLoaded(RoomMetadata metadata)
        {
            ResetTransientVisualLights();
#if ANDROID
            // RoomLoaded may run while the prior sealed frame still owns its
            // bindings. Retire at the next producer boundary, before uploads.
            _glesEnhancedTextureRetirementPending = true;
#endif
                if ((Paths.IsMphJapan || Paths.IsMphKorea))
                {
                    (int count, byte[] charData) = Read.ReadKanjiFont(singlePlayer: false);
                    byte[] widths = new byte[count];
                    if (Paths.IsMphJapan)
                    {
                        Array.Fill(widths, (byte)10);
                    }
                    else
                    {
                        Array.Fill(widths, (byte)11);
                        widths[1] = 2; // KR period
                        widths[32] = 6; // ASCII space
                    }
                    byte[] offsets = new byte[count];
                    Text.Font.Kanji.SetData(widths, offsets, charData, minChar: 0);
                }

            if (metadata.InGameName != null) _setTitle(metadata.InGameName);
            SetRoomValues(metadata);
            ConfigureEnvironmentalParticles(metadata);
            ConfigureImpactDecals(metadata);
            _cameraMode = World.LocalPlayer!.LoadFlags.TestFlag(LoadFlags.Active) ? CameraMode.Player : CameraMode.Roam;
            _inputMode = _cameraMode == CameraMode.Player ? InputMode.All : InputMode.CameraOnly;
            // A secondary replay scene shares the live process audio device.
            // It must not replace the live scene's global SFX subscription or
            // music owner while it is warming up off screen.
            if (!_isolatedPresentation)
            {
                Sound.Sfx.Load(World);
                Music.TryPlayRoomMusic(World.RoomId, 0);
            }
        }

        public void BeforeWorldUpdate()
        {
            if (World.LocalPlayer!.LoadFlags.TestFlag(LoadFlags.Active))
            {
                World.LocalPlayer!.GetPresentation().UpdateTimedSounds();
                World.LocalPlayer!.GetPresentation().ProcessHudMessageQueue();
            }
        }
        public void AfterWorldUpdate()
        {
            if (World.LocalPlayer!.LoadFlags.TestFlag(LoadFlags.Active)) World.LocalPlayer!.GetPresentation().ProcessModeHud();
        }

        public void SetRoomValues(RoomMetadata meta)
        {
            _light1Vector = meta.Light1Vector;
            _light1Color = new Vector3(
                meta.Light1Color.Red / 31.0f,
                meta.Light1Color.Green / 31.0f,
                meta.Light1Color.Blue / 31.0f
            );
            _light2Vector = meta.Light2Vector;
            _light2Color = new Vector3(
                meta.Light2Color.Red / 31.0f,
                meta.Light2Color.Green / 31.0f,
                meta.Light2Color.Blue / 31.0f
            );
            _hasFog = meta.FogEnabled;
            _fogColor = new Vector4(
                meta.FogColor.Red / 31f,
                meta.FogColor.Green / 31f,
                meta.FogColor.Blue / 31f,
                1.0f
            );
            _fogOffset = meta.FogOffset;
            _fogSlope = meta.FogSlope;
            if (meta.ClearFog && meta.FirstHunt)
            {
                _clearColor = new Color4(_fogColor.X, _fogColor.Y, _fogColor.Z, _fogColor.W);
            }
            _farClip = meta.FarClip;

#if ANDROID
            if (_shaderProgramId != 0)
            {
                SetShaderFog();
            }
#endif
        }

#if ANDROID
        private void SetShaderFog()
        {
            float fogMin = _fogOffset / (float)0x7FFF;
            float fogMax = (_fogOffset + 32 * (0x400 >> _fogSlope)) / (float)0x7FFF;
            GL.Uniform4(_shaderLocations.FogColor, _fogColor);
            GL.Uniform1(_shaderLocations.FogMinDistance, fogMin);
            GL.Uniform1(_shaderLocations.FogMaxDistance, fogMax);
        }
#endif

        // called before load
        public EntityBase AddModel(string name, int recolor = 0, bool firstHunt = false, MetaDir dir = MetaDir.Models, Vector3? pos = null)
        {
            ModelInstance model = Read.GetModelInstance(name, firstHunt, dir);
            var entity = new ModelEntity(model, World, recolor);
            World.InsertEntity(entity);
            InitEntity(entity);
            if (pos.HasValue)
            {
                entity.Position = pos.Value;
            }
            return entity;
        }

        public bool IsNodeRefVisible(NodeRef nodeRef)
        {
            return World.Room != null && ((RoomEntityPresentation)EntityPresentation.Get(World.Room, this)).IsNodeRefVisible(nodeRef);
        }

        public void OnLoad()
        {
#if ANDROID
            // What the driver calls itself, once, at the only moment there is
            // certainly a context current. Everything about a picture being
            // wrong on somebody else's machine starts with these three lines,
            // and asking for them by hand means asking somebody to run a
            // second program.
            if (Mods.DebugLog.Active)
            {
                Mods.DebugLog.Line("gl", $"vendor={GL.GetString(StringName.Vendor)}");
                Mods.DebugLog.Line("gl", $"renderer={GL.GetString(StringName.Renderer)}");
                Mods.DebugLog.Line("gl", $"version={GL.GetString(StringName.Version)}");
                Mods.DebugLog.Line("gl", "shading language="
                    + GL.GetString(StringName.ShadingLanguageVersion));
            }
            GL.ClearColor(_clearColor);
            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Texture2D);
            GL.DepthFunc(DepthFunction.Lequal);
#endif
            // One line a scene, because the machine that has to be asked about
            // this is always somebody else's: what the render options actually
            // came out as is the first thing worth knowing when a picture is
            // wrong on a device nobody here can plug in.
            Console.WriteLine($"[render] cel shading "
                + $"{(Mods.RenderOptions.CelShading ? "on" : "off")}, "
                + $"{Mods.RenderOptions.CelBands} bands, "
                + $"outline {Mods.RenderOptions.CelEdge.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}, "
                + $"fog {Mods.RenderOptions.OnOff(Mods.RenderOptions.Fog)}");
#if ANDROID
            InitShaders();
#endif
            AllocateEffects();
            World.InitializeWorld();
            // RenderFrame owns and reuses submission storage. No draw
            // allocates a matrix stack on the render thread.
            foreach (PlayerEntity player in World.Players)
            {
                if (player.LoadFlags.TestFlag(LoadFlags.SlotActive))
                {
                    InitEntity(player);
                    InitEntity(player.Halfturret);
                }
            }
            CaptureSimulationPoses();
            if (!_isolatedPresentation) OutputStart();
            GC.Collect(generation: 2, GCCollectionMode.Forced, blocking: true, compacting: true);
            // Android's runtime throws PlatformNotSupported for this, which took
            // every match on that head down before a room had finished loading.
            // It is a hint to the collector, so going without it costs nothing.
            if (!OperatingSystem.IsAndroid())
            {
                GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            }
        }

#if ANDROID
        private int _frameBuffer = 0;
        private int _screenTexture = 0;
        private int _renderBuffer = 0;
        // What the ink pass reads. A pass cannot sample the target it is
        // drawing into, so the finished scene is copied here first.
        private int _celTexture = 0;
        // The scene's depth, as a texture rather than as _renderBuffer, which
        // nothing can read. Only allocated while cel shading is drawing its
        // outline, and zero when the driver would not take one.
        private int _depthTexture = 0;
        private bool _depthTextureRefused = false;
        private int _celFrameBuffer = 0;
        private int _celFrameBufferColor = 0;
#endif

        /// <summary>
        /// The size the 3D scene is actually drawn at, which the resolution
        /// scale may make smaller than the window. The quad that puts it on
        /// screen stretches it back, and the HUD is drawn after that at full
        /// size, so nothing readable is ever scaled.
        /// </summary>
        public Vector2i RenderSize => new Vector2i(
            Mods.RenderOptions.Scaled(Size.X), Mods.RenderOptions.Scaled(Size.Y));

        private Vector2i _targetSize;

        public void OnResize()
        {
            _targetSize = RenderSize;
#if ANDROID
            if (_screenTexture != 0)
            {
                Vector2i target = RenderSize;
                _targetSize = target;
                GL.BindTexture(TextureTarget.Texture2D, _screenTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, target.X, target.Y, 0,
                    PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
                // Nearest is the DS look and is right at full size; a stretched
                // target needs linear or every edge stair-steps.
                bool upscaling = Mods.RenderOptions.ResolutionScale < 100;
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)(upscaling ? TextureMinFilter.Linear : TextureMinFilter.Nearest));
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                    (int)(upscaling ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
                GL.BindTexture(TextureTarget.Texture2D, 0);
                if (_celTexture != 0)
                {
                    GL.BindTexture(TextureTarget.Texture2D, _celTexture);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, target.X, target.Y, 0,
                        PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                }
                Debug.Assert(_renderBuffer != 0);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _renderBuffer);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, target.X, target.Y);
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
                if (_depthTexture != 0)
                {
                    GL.BindTexture(TextureTarget.Texture2D, _depthTexture);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Depth24Stencil8,
                        target.X, target.Y, 0, PixelFormat.DepthStencil, PixelType.UnsignedInt248, IntPtr.Zero);
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                }
            }
#endif
        }

#if ANDROID
        private void InitShaders()
        {
            string fragmentLog;
            string vertexLog;
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, Mods.Render.EsShaders.VertexShader);
            GL.CompileShader(vertexShader);
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, Mods.Render.EsShaders.FragmentShader);
            GL.CompileShader(fragmentShader);
            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int vertexStatus);
            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out int fragmentStatus);
            if (Debugger.IsAttached)
            {
                vertexLog = GL.GetShaderInfoLog(vertexShader);
                fragmentLog = GL.GetShaderInfoLog(fragmentShader);
                if (vertexLog != "" || fragmentLog != "")
                {
                    Debugger.Break();
                }
            }
            if (vertexStatus == 0 || fragmentStatus == 0)
            {
                // The driver's own message. Without it a shader that will not
                // compile is one sentence with nothing in it to act on.
                throw new ProgramException("Failed to compile main shaders."
                    + $" vertex: {GL.GetShaderInfoLog(vertexShader)}"
                    + $" fragment: {GL.GetShaderInfoLog(fragmentShader)}");
            }

            _shaderProgramId = GL.CreateProgram();
            GL.AttachShader(_shaderProgramId, vertexShader);
            GL.AttachShader(_shaderProgramId, fragmentShader);
            GL.LinkProgram(_shaderProgramId);
            GL.DetachShader(_shaderProgramId, vertexShader);
            GL.DetachShader(_shaderProgramId, fragmentShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteShader(vertexShader);

            vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, Mods.Render.EsShaders.RttVertexShader);
            GL.CompileShader(vertexShader);
            fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, Mods.Render.EsShaders.RttFragmentShader);
            GL.CompileShader(fragmentShader);
            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out vertexStatus);
            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out fragmentStatus);
            if (Debugger.IsAttached)
            {
                vertexLog = GL.GetShaderInfoLog(vertexShader);
                fragmentLog = GL.GetShaderInfoLog(fragmentShader);
                if (vertexLog != "" || fragmentLog != "")
                {
                    Debugger.Break();
                }
            }
            if (vertexStatus == 0 || fragmentStatus == 0)
            {
                throw new ProgramException("Failed to compile RTT shaders.");
            }
            _rttShaderProgramId = GL.CreateProgram();
            GL.AttachShader(_rttShaderProgramId, vertexShader);
            GL.AttachShader(_rttShaderProgramId, fragmentShader);
            GL.LinkProgram(_rttShaderProgramId);
            GL.DetachShader(_rttShaderProgramId, vertexShader);
            GL.DetachShader(_rttShaderProgramId, fragmentShader);
            GL.DeleteShader(fragmentShader);

            // use same vertex shader
            fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, Mods.Render.EsShaders.ShiftFragmentShader);
            GL.CompileShader(fragmentShader);
            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out fragmentStatus);
            if (Debugger.IsAttached)
            {
                fragmentLog = GL.GetShaderInfoLog(fragmentShader);
                if (fragmentLog != "")
                {
                    Debugger.Break();
                }
            }
            if (fragmentStatus == 0)
            {
                throw new ProgramException("Failed to compile shift shader.");
            }
            _shiftShaderProgramId = GL.CreateProgram();
            GL.AttachShader(_shiftShaderProgramId, vertexShader);
            GL.AttachShader(_shiftShaderProgramId, fragmentShader);
            GL.LinkProgram(_shiftShaderProgramId);
            GL.DetachShader(_shiftShaderProgramId, vertexShader);
            GL.DetachShader(_shiftShaderProgramId, fragmentShader);
            GL.DeleteShader(fragmentShader);

            // use same vertex shader
            fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, Mods.Render.EsShaders.CelFragmentShader);
            GL.CompileShader(fragmentShader);
            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out fragmentStatus);
            if (fragmentStatus == 0)
            {
                throw new ProgramException("Failed to compile the cel shading shader."
                    + $" {GL.GetShaderInfoLog(fragmentShader)}");
            }
            _celShaderProgramId = GL.CreateProgram();
            GL.AttachShader(_celShaderProgramId, vertexShader);
            GL.AttachShader(_celShaderProgramId, fragmentShader);
            GL.LinkProgram(_celShaderProgramId);
            GL.DetachShader(_celShaderProgramId, vertexShader);
            GL.DetachShader(_celShaderProgramId, fragmentShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteShader(vertexShader);

            _frameBuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _frameBuffer);
            _screenTexture = GL.GenTexture();
            _textureCount++;
            Vector2i renderTarget = RenderSize;
            _targetSize = renderTarget;
            GL.BindTexture(TextureTarget.Texture2D, _screenTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, renderTarget.X, renderTarget.Y, 0,
                PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
            // Nearest at full size, which is what the DS looked like; linear
            // once the scene is being stretched, where nearest is a mess of
            // stair-stepped edges rather than a soft picture.
            bool upscaling = Mods.RenderOptions.ResolutionScale < 100;
            int minParameter = (int)(upscaling ? TextureMinFilter.Linear : TextureMinFilter.Nearest);
            int magParameter = (int)(upscaling ? TextureMagFilter.Linear : TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, minParameter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, magParameter);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, _screenTexture, 0);

            // The ink pass's copy of the scene. Same size and same filtering;
            // it is only ever sampled texel for texel.
            _celTexture = GL.GenTexture();
            _textureCount++;
            GL.BindTexture(TextureTarget.Texture2D, _celTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, renderTarget.X, renderTarget.Y, 0,
                PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            _renderBuffer = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _renderBuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, renderTarget.X, renderTarget.Y);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer, _renderBuffer);

            FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            FramebufferStatus = status;
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                // Was a Debugger.Break, which is silence in a release build --
                // and an incomplete framebuffer is exactly the fault that
                // renders nothing while every other signal says the context
                // is fine. Say it out loud.
                Console.WriteLine($"[render] the offscreen target is not usable: {status}. "
                    + $"Nothing drawn into it will appear. Size {Size.X}x{Size.Y}.");
                Debugger.Break();
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            _shaderLocations.UseLight = GL.GetUniformLocation(_shaderProgramId, "use_light");
            _shaderLocations.ShowColors = GL.GetUniformLocation(_shaderProgramId, "show_colors");
            _shaderLocations.UseTexture = GL.GetUniformLocation(_shaderProgramId, "use_texture");
            _shaderLocations.Light1Color = GL.GetUniformLocation(_shaderProgramId, "light1col");
            _shaderLocations.Light1Vector = GL.GetUniformLocation(_shaderProgramId, "light1vec");
            _shaderLocations.Light2Color = GL.GetUniformLocation(_shaderProgramId, "light2col");
            _shaderLocations.Light2Vector = GL.GetUniformLocation(_shaderProgramId, "light2vec");
            _shaderLocations.Diffuse = GL.GetUniformLocation(_shaderProgramId, "diffuse");
            _shaderLocations.Ambient = GL.GetUniformLocation(_shaderProgramId, "ambient");
            _shaderLocations.Specular = GL.GetUniformLocation(_shaderProgramId, "specular");
            _shaderLocations.Emission = GL.GetUniformLocation(_shaderProgramId, "emission");
            _shaderLocations.UseFog = GL.GetUniformLocation(_shaderProgramId, "fog_enable");
            _shaderLocations.CelBands = GL.GetUniformLocation(_shaderProgramId, "cel_bands");
            _shaderLocations.UseFlat = GL.GetUniformLocation(_shaderProgramId, "use_flat");
            _shaderLocations.FlatColor = GL.GetUniformLocation(_shaderProgramId, "flat_color");
            _shaderLocations.FogColor = GL.GetUniformLocation(_shaderProgramId, "fog_color");
            _shaderLocations.FogMinDistance = GL.GetUniformLocation(_shaderProgramId, "fog_min");
            _shaderLocations.FogMaxDistance = GL.GetUniformLocation(_shaderProgramId, "fog_max");
            _shaderLocations.UseOverride = GL.GetUniformLocation(_shaderProgramId, "use_override");
            _shaderLocations.OverrideColor = GL.GetUniformLocation(_shaderProgramId, "override_color");
            _shaderLocations.UsePaletteOverride = GL.GetUniformLocation(_shaderProgramId, "use_pal_override");
            _shaderLocations.PaletteOverrideColor = GL.GetUniformLocation(_shaderProgramId, "pal_override_color");
            _shaderLocations.MaterialAlpha = GL.GetUniformLocation(_shaderProgramId, "mat_alpha");
            _shaderLocations.MaterialMode = GL.GetUniformLocation(_shaderProgramId, "mat_mode");
            _shaderLocations.ViewMatrix = GL.GetUniformLocation(_shaderProgramId, "view_mtx");
            _shaderLocations.ViewInvMatrix = GL.GetUniformLocation(_shaderProgramId, "view_inv_mtx");
            _shaderLocations.ProjectionMatrix = GL.GetUniformLocation(_shaderProgramId, "proj_mtx");
            _shaderLocations.TextureMatrix = GL.GetUniformLocation(_shaderProgramId, "tex_mtx");
            _shaderLocations.TexgenMode = GL.GetUniformLocation(_shaderProgramId, "texgen_mode");
            _shaderLocations.MatrixStack = GL.GetUniformLocation(_shaderProgramId, "mtx_stack");
            _shaderLocations.ToonTable = GL.GetUniformLocation(_shaderProgramId, "toon_table");

            _shaderLocations.CelOutline = GL.GetUniformLocation(_celShaderProgramId, "outline");
            _shaderLocations.CelTexelWidth = GL.GetUniformLocation(_celShaderProgramId, "texel_w");
            _shaderLocations.CelTexelHeight = GL.GetUniformLocation(_celShaderProgramId, "texel_h");
            _shaderLocations.CelNearPlane = GL.GetUniformLocation(_celShaderProgramId, "near_plane");
            _shaderLocations.CelFarPlane = GL.GetUniformLocation(_celShaderProgramId, "far_plane");
            _shaderLocations.CelDepthQuantum = GL.GetUniformLocation(_celShaderProgramId, "depth_quantum");
            _shaderLocations.CelProbe = GL.GetUniformLocation(_celShaderProgramId, "probe");

            _shaderLocations.FadeColor = GL.GetUniformLocation(_rttShaderProgramId, "fade_color");
            _shaderLocations.LayerAlpha = GL.GetUniformLocation(_rttShaderProgramId, "alpha");
            _shaderLocations.UseMask = GL.GetUniformLocation(_rttShaderProgramId, "use_mask");
            _shaderLocations.ViewWidth = GL.GetUniformLocation(_rttShaderProgramId, "view_width");
            _shaderLocations.ViewHeight = GL.GetUniformLocation(_rttShaderProgramId, "view_height");
            _shaderLocations.UseHudVertexColor = GL.GetUniformLocation(_rttShaderProgramId, "use_hud_vertex_color");
            _shaderLocations.UseHudTexture = GL.GetUniformLocation(_rttShaderProgramId, "use_hud_texture");
            int texLocation = GL.GetUniformLocation(_rttShaderProgramId, "tex");
            int maskLocation = GL.GetUniformLocation(_rttShaderProgramId, "mask");
            GL.UseProgram(_rttShaderProgramId);
            GL.Uniform1(texLocation, 0);
            GL.Uniform1(maskLocation, 1);

            GL.UseProgram(_celShaderProgramId);
            GL.Uniform1(GL.GetUniformLocation(_celShaderProgramId, "tex"), 0);
            GL.Uniform1(GL.GetUniformLocation(_celShaderProgramId, "depth_tex"), 1);

            _shaderLocations.ShiftTable = GL.GetUniformLocation(_shiftShaderProgramId, "shift_table");
            _shaderLocations.ShiftIndex = GL.GetUniformLocation(_shiftShaderProgramId, "shift_idx");
            _shaderLocations.ShiftFactor = GL.GetUniformLocation(_shiftShaderProgramId, "shift_fac");
            _shaderLocations.LerpFactor = GL.GetUniformLocation(_shiftShaderProgramId, "lerp_fac");
            _shaderLocations.WhiteoutTable = GL.GetUniformLocation(_shiftShaderProgramId, "white_table");
            _shaderLocations.WhiteoutFactor = GL.GetUniformLocation(_shiftShaderProgramId, "white_fac");

            GL.UseProgram(_shiftShaderProgramId);

            float[] shifts = new float[64];
            for (int i = 0; i < 64; i++)
            {
                int val;
                if ((i & 32) != 0)
                {
                    val = 31 - (i & 31);
                }
                else
                {
                    val = i & 31;
                }
                shifts[i] = -((val - 16) << 12) / 4096f / 256f;
            }
            GL.Uniform1(_shaderLocations.ShiftTable, 64, shifts);

            GL.UseProgram(_shaderProgramId);

            var floats = new List<float>(Metadata.ToonTable.Count * 3);
            foreach (Vector3 vector in Metadata.ToonTable)
            {
                floats.Add(vector.X);
                floats.Add(vector.Y);
                floats.Add(vector.Z);
            }
            GL.Uniform3(_shaderLocations.ToonTable, Metadata.ToonTable.Count, floats.ToArray());
            SetShaderFog();
        }
#endif

        public void PrepareEntity(EntityBase entity) => EntityPresentation.Get(entity, this);

        public void InitEntity(EntityBase entity)
        {
            _poses.Remove(entity); // pooled entities begin a new render history at initialization
            PrepareEntity(entity);
            foreach (ModelInstance inst in entity.GetModels())
            {
                // Both shipping executors consume the same portable meshes;
                // desktop display-list construction no longer exists.
                InitTextures(inst.Model);
                PrepareCpuMeshes(inst.Model, isRoom: entity.Type == EntityType.Room);
            }
        }

        private void PrepareCpuMeshes(Model model, bool isRoom)
        {
            NormalizeModelMaterials(model);
            foreach (Mesh mesh in model.Meshes)
            {
                Material material = model.Materials[mesh.MaterialId];
                int width = 0;
                int height = 0;
                if (material.TextureId >= 0 && model.Recolors.Count > 0)
                {
                    Texture texture = model.Recolors[0].Textures[material.TextureId];
                    width = texture.Width;
                    height = texture.Height;
                }
                CpuMesh compiled = GetCompiledMesh(model, mesh, width, height,
                    material.TexgenMode == TexgenMode.Normal, isRoom);
                _portableMeshes[mesh.GeometryIdentity] = compiled;
            }
        }

        private CpuMesh GetCompiledMesh(Model model, Mesh mesh, int textureWidth, int textureHeight,
            bool texgen, bool isRoom)
        {
            IReadOnlyList<RenderInstruction> instructions = model.RenderInstructionLists[mesh.DlistId];
            CpuMeshCache cache = _cpuMeshCache.GetOrCreateValue(mesh.GeometryIdentity);
            foreach (CpuMeshCacheEntry cached in cache.Entries)
            {
                if (ReferenceEquals(cached.Instructions, instructions)
                    && cached.TextureWidth == textureWidth
                    && cached.TextureHeight == textureHeight
                    && cached.Texgen == texgen
                    && cached.IsRoom == isRoom)
                {
                    return cached.Mesh;
                }
            }

            CpuMesh compiled = MeshCompiler.CompileDisplayList(instructions, textureWidth, textureHeight, texgen, isRoom);
            cache.Entries.Add(new CpuMeshCacheEntry(instructions, textureWidth, textureHeight, texgen, isRoom, compiled));
            return compiled;
        }

        public void LoadModel(string name, bool firstHunt = false)
        {
            LoadModel(Read.GetModelInstance(name, firstHunt).Model);
        }

        public void LoadModel(Model model, bool isRoom = false)
        {
            InitTextures(model);
            PrepareCpuMeshes(model, isRoom);
        }

        internal static void NormalizeModelMaterials(Model model)
        {
            foreach (Material material in model.Materials)
            {
                if (material.TextureId != -1
                    && (material.RenderMode == RenderMode.Unknown3 || material.RenderMode == RenderMode.Unknown4))
                {
                    material.RenderMode = RenderMode.Normal;
                }
            }
        }

        private void InitTextures(Model model)
        {
            NormalizeModelMaterials(model);
            if (_texPalMap.ContainsKey(model.Id))
            {
                return;
            }
            var combos = new HashSet<(int, int, int)>();
            foreach (Material material in model.Materials)
            {
                if (material.TextureId == -1)
                {
                    continue;
                }
                for (int i = 0; i < model.Recolors.Count; i++)
                {
                    combos.Add((material.TextureId, material.PaletteId, i));
                }
            }
            foreach (TextureAnimationGroup group in model.AnimationGroups.Texture)
            {
                foreach (TextureAnimation animation in group.Animations.Values)
                {
                    for (int i = animation.StartIndex; i < animation.StartIndex + animation.Count; i++)
                    {
                        for (int j = 0; j < model.Recolors.Count; j++)
                        {
                            combos.Add((group.TextureIds[i], group.PaletteIds[i], j));
                        }
                    }
                }
            }
            if (combos.Count == 0 && model.Recolors.Count > 0
                && model.Recolors[0].Textures.Count > 0 && model.Recolors[0].Palettes.Count > 0)
            {
                combos.Add((0, 0, 0));
            }
            if (combos.Count > 0)
            {
                var map = new TextureMap();
                foreach ((int textureId, int paletteId, int recolorId) in combos)
                {
#if ANDROID
                    bool onlyOpaque = BindTexture(model, textureId, paletteId, recolorId);
                    map.Add(textureId, paletteId, recolorId, _textureCount, onlyOpaque);
#else
                        IReadOnlyList<ColorRgba> decoded = model.GetPixels(textureId, paletteId, recolorId);
                        Texture texture = model.Recolors[recolorId].Textures[textureId];
                        TextureIdentity identity = new TextureIdentity(model.Recolors[recolorId], textureId,
                            paletteId, recolorId);
                        RenderTexturePixels record;
                        try
                        {
                            record = PrepareTexture(identity, decoded, texture.Width, texture.Height);
                        }
                        catch (ArgumentException ex)
                        {
                            throw new InvalidOperationException($"Model {model.Name} texture {textureId}, palette {paletteId}, "
                                + $"recolor {recolorId} decoded {decoded.Count} pixels for "
                                + $"{texture.Width}x{texture.Height} dimensions.", ex);
                        }
                        map.Add(textureId, paletteId, recolorId, 0, record.OnlyOpaque);
#endif
                }
                _texPalMap.Add(model.Id, map);
            }
        }

        public int BindGetTexture(Model model, int textureId, int paletteId, int recolorId)
        {
            if (_texPalMap.TryGetValue(model.Id, out TextureMap? value))
            {
                return value.Get(textureId, paletteId, recolorId).BindingId;
            }
            BindTexture(model, textureId, paletteId, recolorId);
            return _textureCount;
        }

        private bool BindTexture(Model model, int textureId, int paletteId, int recolorId)
        {
            _textureCount++;
            bool onlyOpaque = true;
            var pixels = new List<uint>();
            var average = new FlatColor();
            IReadOnlyList<ColorRgba> decoded = model.GetPixels(textureId, paletteId, recolorId);
            foreach (ColorRgba pixel in decoded)
            {
                pixels.Add(pixel.ToUint());
                onlyOpaque &= pixel.Alpha == 255;
                average.Add(pixel);
            }
            Texture texture = model.Recolors[recolorId].Textures[textureId];
            TextureIdentity identity = new TextureIdentity(model.Recolors[recolorId], textureId,
                paletteId, recolorId);
            RenderTexturePixels record = PrepareTexture(identity, decoded, texture.Width, texture.Height);
#if ANDROID
            RegisterLegacyTextureIdentity(_textureCount, identity);
            GL.BindTexture(TextureTarget.Texture2D, _textureCount);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, texture.Width, texture.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels.ToArray());
            GL.BindTexture(TextureTarget.Texture2D, 0);
            _flatColors[_textureCount] = average.Result;
            return onlyOpaque;
#else
            return record.OnlyOpaque;
#endif
        }

        /// <summary>
        /// The one colour each bound texture averages to, by binding ID.
        ///
        /// Cel shading paints surfaces in a flat colour instead of in their
        /// texture, and this is the colour it uses. Worked out once, as the
        /// texels go to the card, because it is a property of the texture and
        /// not of the frame -- and there is no other moment when this code has
        /// the pixels in hand.
        /// </summary>
#if ANDROID
        private readonly Dictionary<int, Vector3> _flatColors = new Dictionary<int, Vector3>();
#endif

        /// <summary>
        /// A texture's average colour, weighted by alpha.
        ///
        /// Weighted because a cut-out texture -- a grate, a decal, a sprite --
        /// is mostly transparent, and the transparent texels usually carry
        /// black or whatever happened to be in that corner of the sheet.
        /// Averaging those in drags every such surface towards black, which is
        /// the one colour an outline pass needs to keep for itself.
        /// </summary>
        private struct FlatColor
        {
            private float _red;
            private float _green;
            private float _blue;
            private float _weight;
            private float _count;
            private float _plainRed;
            private float _plainGreen;
            private float _plainBlue;

            public void Add(ColorRgba pixel)
            {
                float alpha = pixel.Alpha / 255f;
                _red += pixel.Red * alpha;
                _green += pixel.Green * alpha;
                _blue += pixel.Blue * alpha;
                _weight += alpha;
                _plainRed += pixel.Red;
                _plainGreen += pixel.Green;
                _plainBlue += pixel.Blue;
                _count++;
            }

            public Vector3 Result
            {
                get
                {
                    if (_weight > 0.01f)
                    {
                        return new Vector3(_red, _green, _blue) / _weight / 255f;
                    }
                    // fully transparent: nothing is drawn with it anyway
                    if (_count > 0)
                    {
                        return new Vector3(_plainRed, _plainGreen, _plainBlue) / _count / 255f;
                    }
                    return Vector3.One;
                }
            }
        }

        public int BindGetTexture(IReadOnlyList<ColorRgba> data, int width, int height)
        {
            _textureCount++;
            TextureIdentity identity = CreateDynamicTextureIdentity(data, width, height, _textureCount);
            RegisterLegacyTextureIdentity(_textureCount, identity);
#if ANDROID
            GL.BindTexture(TextureTarget.Texture2D, _textureCount);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, data.ToArray());
            GL.BindTexture(TextureTarget.Texture2D, 0);
            _flatColors[_textureCount] = AverageOf(data);
#endif
            return _textureCount;
        }

        public void BindTexture(IReadOnlyList<ColorRgba> data, int width, int height, int bindingId)
        {
            TextureIdentity identity;
            if (!TryGetTextureIdentityForBinding(bindingId, out identity))
            {
                identity = CreateDynamicTextureIdentity(data, width, height, bindingId);
                RegisterLegacyTextureIdentity(bindingId, identity);
            }
            PrepareTexture(identity, data, width, height);
#if ANDROID
            GL.BindTexture(TextureTarget.Texture2D, bindingId);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, data.ToArray());
            GL.BindTexture(TextureTarget.Texture2D, 0);
            // this binding may already have had a different picture in it
            _flatColors[bindingId] = AverageOf(data);
#endif
        }

        private static Vector3 AverageOf(IReadOnlyList<ColorRgba> data)
        {
            var average = new FlatColor();
            for (int i = 0; i < data.Count; i++)
            {
                average.Add(data[i]);
            }
            return average.Result;
        }

        public void UpdateMaterials(Model model, int recolorId)
        {
            for (int i = 0; i < model.Materials.Count; i++)
            {
                Material material = model.Materials[i];
                int textureId = material.CurrentTextureId;
                if (textureId == -1)
                {
                    continue;
                }
                int paletteId = material.CurrentPaletteId;
                (int bindingId, bool onlyOpaque) = _texPalMap[model.Id].Get(textureId, paletteId, recolorId);
                SetTextureBindingId(material, bindingId);
                material.CurrentTextureId = textureId;
                material.CurrentPaletteId = paletteId;
                UpdateMaterial(material, onlyOpaque);
            }
        }

        private void UpdateMaterial(Material material, bool onlyOpaque)
        {
            if (material.CurrentAlpha < 1.0f)
            {
                material.RenderMode = RenderMode.Translucent;
            }
            else if (material.RenderMode != RenderMode.Normal && onlyOpaque)
            {
                material.RenderMode = RenderMode.Normal;
            }
            else if (material.RenderMode == RenderMode.Normal && !onlyOpaque)
            {
                material.RenderMode = RenderMode.Translucent;
            }
        }

        public static bool BreakNextFrame { get; set; } // skdebug

        /// <summary>
        /// One simulation step and the frame drawn from it, the way this has
        /// always worked. Every harness client -- <c>NetCheckClient</c>,
        /// <c>MapAudit</c>, <c>WeaponDps</c>, <c>ThumbnailCapture</c> -- calls
        /// this once per frame of its own loop and is therefore completely
        /// unaffected by the decoupling below: same steps, same order, same
        /// packets on the same frame numbers.
        /// </summary>
        public void OnUpdateFrame()
        {
            OnSimulationFrame();
            OnDrawFrame();
        }

        /// <summary>
        /// Advance the game by exactly one 60 Hz step.
        ///
        /// Everything that decides what the game *is* lives here and nowhere
        /// else: input, the network session, the world, the clock, the frame
        /// counter. The platform host runs this on a fixed-step
        /// accumulator so it happens 60 times a second whatever the picture is
        /// doing -- which is what lets the drawing run at 144 without the
        /// 800-odd frame-counted timers in the entity code, the per-frame
        /// intent stream or the replay format noticing anything.
        /// </summary>
        public SpectatorCameraController SpectatorCamera { get; } = new();

        public void OnSimulationFrame()
        {
            if (_replaySession != null) _replaySession.AdvanceHighlightRange();
            else Mods.Network.ReplayPlayback.AdvanceHighlightRange();
            if (!_isolatedPresentation)
            {
                SpectatorCamera.Poll(this, _keyboardState);
                Mods.Network.ReplayControls.Poll(_keyboardState, _mouseState, Size);
                Mods.Network.IntermissionVoteControls.Poll(_keyboardState, _mouseState, Size);
            }
            bool processedSeek = _replaySession != null
                ? _replaySession.ProcessSeek(OnSimulationStep, BeginReplaySeek)
                : Mods.Network.ReplayPlayback.ProcessSeek(OnSimulationStep, BeginReplaySeek);
            if (processedSeek)
            {
                if (!PlaybackSeeking)
                {
                    ResetPoseHistory(); ResetRenderLook();
                    foreach (PlayerEntity player in World.GetPlayerEntities()) player.GetPresentation().SynchronizeReplayFeedbackAudio();
                    var local = CombatFeedback.Local;
                    FeedbackAudio.RestoreReplayBaseline(local, local.IsValid ? (ushort)World.Players[local.Slot].Health : (ushort)0);
                    WorldFeedback.ClearPendingNotices();
                    if (!_isolatedPresentation) Music.TryPlayRoomMusic(World.RoomId, 0);
                }
                return;
            }
            int steps = _replaySession?.TakeSimulationSteps()
                ?? Mods.Network.ReplayPlayback.TakeSimulationSteps();
            if (steps == 0 && !_isolatedPresentation && Mods.SpectatorMode.IsSpectating) OnKeyHeld();
            for (int step = 0; step < steps; step++)
            {
                if (PlaybackAtEnd) break;
                OnSimulationStep();
            }
        }

        private void BeginReplaySeek()
        {
            // Seeking fast-forwards simulation state without presenting the
            // intermediate frames.  Fence render histories immediately so the
            // first post-seek picture cannot blend across the skipped range.
            ResetPoseHistory();
            _visualLightIdentities.ResetScope();
            ResetTransientVisualLights();
            ResetEnvironmentalParticlePresentation();
            ResetImpactDecalPresentation();
            if (!_isolatedPresentation)
            {
                Sound.Sfx.Instance.StopAllSound(force: true);
                Music.Stop();
            }
            WorldFeedback.ClearPendingNotices();
        }

        private void OnSimulationStep()
        {
            // The effect clock, before anything can spawn an effect. See
            // _effectFrame: it has to be the same value for the spawn and for
            // the ProcessEffects call that belongs to this step, and the
            // game's own World.FrameCount stopped being that when the step and the
            // picture became two calls.
            _effectFrame++;
            if (!CanCaptureSimulationLook) ResetRenderLook();
            // todo: FPS stuff
            if (BreakNextFrame)
            {
                _frameAdvanceOn = true;
                BreakNextFrame = false;
            }
            World.BeginFrame(ProcessFrame);
            if (ProcessFrame)
            {
                if (_inputMode == InputMode.CameraOnly || Mods.Chat.ChatBox.Composing)
                {
                    // Every frame the prompt is up, not once when it opens: a
                    // key held at the moment somebody pressed T stays held in
                    // the binding until something clears it, and a player who
                    // opened chat mid-stride would otherwise walk into the
                    // nearest wall for as long as they were typing.
                    World.LocalPlayer!.Controls.ClearAll();
                }
                else if (Mods.Chat.ChatBox.ConsumeJustClosed())
                {
                    World.LocalPlayer!.GetPresentation().ModForgetInputDeltas();
                }
                if (_replaySession != null) _replaySession.PumpFrame();
                else
                {
                    Mods.Network.ReplayPlayback.PumpFrame();
                    Mods.Network.NetSession.Update(World.GlobalElapsedTime);
                }
                if (!_isolatedPresentation && Mods.Network.ReplayPlayback.IsActive
                    && !Mods.SpectatorMode.IsSpectating)
                {
                    // No local player to spawn as during playback -- watch
                    // as soon as anyone recorded becomes available, rather
                    // than sitting on an empty room waiting for Escape. And
                    // out of somebody's eyes straight away, unlike spectating
                    // a live match: there is no "your own view" to have just
                    // left, so an empty overview would be the whole of what
                    // opening a replay did.
                    Mods.SpectatorMode.Start(World, watchSomeone: true);
                }
                // Spectating is asked for from the pause menu, which runs on
                // this thread but has no scene to hand; it leaves the camera
                // it wants here and this picks it up between frames.
                bool? freeCamera = Mods.SpectatorMode.TakeCameraRequest();
                if (freeCamera.HasValue)
                {
                    SetFreeCamera(freeCamera.Value);
                }
                // Read the pad before the keyboard is turned into binds, and
                // add it after: BeginFrame works out this frame's rising
                // edges and stick aim, and Apply ors the result onto the same
                // binds ProcessInput has just filled in. Suppressed by exactly
                // the things that suppress a keyboard, and by spectating,
                // where World.LocalPlayer! is somebody else's hunter.
                if (!PlaybackSeeking)
                {
                    bool noPlayerInput = _isolatedPresentation || _gameplayInputSuppressed
                        || PlaybackSeeking || _inputMode == InputMode.CameraOnly
                        || Mods.ClientInputState.PauseOpen || Mods.Chat.ChatBox.Composing;
                    bool allowLocalLook = CanCaptureSimulationLook && !noPlayerInput;
                    Mods.Input.GamepadInput.BeginFrame(allowLocalLook,
                        World.LocalPlayer?.EquipInfo.Zoomed == true);
                    World.Services.BeginLocalLookFrame(allowLocalLook);
                    PlayerPresentation.ProcessInput(World, _keyboardState, _mouseState, noPlayerInput);
                    if (!noPlayerInput && !Mods.SpectatorMode.IsSpectating)
                    {
                        Mods.Input.GamepadInput.Apply(World.LocalPlayer!);
                    }
                    World.Services.AfterInput(World);
                    World.LocalPlayer!.GetPresentation().ApplyWeaponSelection(noPlayerInput || Mods.SpectatorMode.IsSpectating);
                    RenderLook?.MarkSimulationStep();
                }
                else
                {
                    World.Services.BeginLocalLookFrame(allowAimAssist: false);
                    foreach (PlayerEntity player in World.GetPlayerEntities()) player.Controls.ClearAll();
                    World.Services.AfterInput(World);
                    RenderLook?.MarkSimulationStep();
                }
            }
            if (!PlaybackSeeking && !_isolatedPresentation) OnKeyHeld();
            bool waitingForServer = Mods.Network.AuthoritativePlay.Active
                && World.Match.Phase is MatchPhase.WaitingForPlayers or MatchPhase.Countdown;
            if (ProcessFrame && World.Room != null)
            {
                World.ProcessWorldStep(waitingForServer);
                if (_audioActive)
                {
                    Sound.Sfx.Update(World.FrameTime);
                    Music.UpdateMusic();
                }
            }
            if (ProcessFrame && World.LocalPlayer!.LoadFlags.TestFlag(LoadFlags.Active))
            {
                World.LocalPlayer!.GetPresentation().UpdateHud();
            }
            if (ProcessFrame)
            {
                World.EndFrame(waitingForServer);
            }
            CaptureSimulationPoses();
            _frameAdvanceLastFrame = _frameAdvanceOn;
            // Effects are advanced inside GetDrawItems, where their ordering
            // against the entity draw pass is what it has always been. This is
            // how many times it owes when the next frame gets there.
            if (PlaybackSeeking && World.Match.LegacyState == MatchState.InProgress)
            {
                ProcessEffects(_effectFrame);
                _pendingEffectSteps = 0;
            }
            else _pendingEffectSteps = Math.Min(_pendingEffectSteps + 1,
                Mods.Render.FrameTiming.MaxCatchUpSteps);
            _pendingFadeSteps = Math.Min(_pendingFadeSteps + 1,
                Mods.Render.FrameTiming.MaxCatchUpSteps);
            if (PlaybackSeeking) UpdateFade(updateDevice: false);
        }

        /// <summary>
        /// Build and submit one picture of wherever the simulation has got to.
        ///
        /// Nothing here may change the game. It reads the world and turns it
        /// into render items; a bug that let a draw write back to an entity
        /// would make the game run differently on a fast monitor, which is the
        /// one failure this split must not have.
        /// </summary>
        public void OnDrawFrame()
        {
            if (PlaybackSeeking) return;
            ulong capturedPresentationTick = World.FrameCount;
            float capturedRenderFraction = Mods.Render.FrameTiming.RenderAlpha;
            if (!float.IsFinite(capturedRenderFraction)) capturedRenderFraction = 0;
            capturedRenderFraction = Math.Clamp(capturedRenderFraction, 0, 1);
            EnvironmentalParticlePresentationClock.FrameTime presentationFrameTime
                = EnvironmentalParticlePresentationClock.Capture(
                    capturedPresentationTick, capturedRenderFraction);
            uint capturedCombatPresentationTick
                = _replaySession?.WorldServerTick
                    ?? Mods.Network.AuthoritativePlay.Current?.WorldServerTick
                    ?? Mods.Network.ReplayPlayback.WorldServerTick
                    ?? unchecked((uint)capturedPresentationTick);
            TimeSpan transientLightPresentationTime
                = presentationFrameTime.SchedulingTime;
            CapturedPresentationTime = presentationFrameTime.SamplingTime;
            // Native polling/sampling is render-rate. Fixed-step input owns
            // hysteresis, boost timing and button edges; these calls only
            // refresh the latest raw state/velocity for prediction.
#if !ANDROID
            Mods.Input.GamepadDesktop.Poll();
#endif
            if (CanCaptureSimulationLook)
            {
                Mods.Input.GamepadInput.SampleNativeFrame();
            }
            else
            {
                Mods.Input.GamepadInput.ResetLook();
            }
            bool sdlBackend = RenderBackendSelection.Current == RenderBackendKind.Sdl;
            Mods.RenderQualitySnapshot frameQuality = Mods.RenderOptions.CaptureSnapshot();
            Mods.Network.AuthoritativePlay.Current?.AdvancePresentation();
            // One scene owner drains one semantic announcer cue per rendered
            // frame. This also covers spectator and replay presentation,
            // where there is no local-player draw call to own the queue.
            if (_audioActive) AnnouncerAudio.PresentNext();
            // The scene's own target, which the resolution scale may have made
            // smaller than the window. Reallocated here rather than only on a
            // window resize, so moving the slider during a match is seen.
            Vector2i target = RenderSize;
            if (target != _targetSize)
            {
                OnResize();
                target = _targetSize;
            }
            // Before the frame is drawn into it, since this swaps what the
            // depth is drawn into.
#if ANDROID
            _glesEnhancedPlan = _glesEnhanced.Prepare(frameQuality.GraphicsPreset,
                target, Size);
            if (_glesEnhancedTextureRetirementPending)
            {
                _glesEnhanced.RetireRoomTextures();
                _glesEnhancedTextureRetirementPending = false;
            }
            _glesEnhancedActive = _glesEnhancedPlan.UsesEnhancedLightingAndMaterials;
            _shaderLocations = _glesEnhancedActive
                ? _glesEnhanced.SceneLocations : _legacyShaderLocations;
            if (_glesEnhancedActive)
            {
                _glesEnhanced.BeginScene(target);
            }
            else
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _frameBuffer);
                UpdateDepthAttachment(target);
                GL.Viewport(0, 0, target.X, target.Y);
                GL.UseProgram(_shaderProgramId);
            }
#endif
            LoadAndUnload();
            _radarMapPresentation.InvalidateIfChanged(this, World.Room);
            if (Hud.Radar.RadarSettings.Style == Hud.Radar.RadarStyle.Enhanced)
                _radarMapPresentation.Ensure(this, World.Room);
            _decalItems.Clear();
            _nonDecalItems.Clear();
            _translucentItems.Clear();
            for (int i = 0; i < _renderFrame.Count; i++)
            {
                DrawSubmission item = _renderFrame.Submissions[i];
                if (item.Primitive != RenderPrimitive.Mesh && item.Points.Length != 0)
                {
                    ArrayPool<Vector3>.Shared.Return(item.Points);
                }
            }
            _legacySubmissionResources.Clear();
            _renderFrame.Reset();
            _nextPolygonId = 1;
            // Singles are filled in by the entity draws below and drawn at
            // the end of the same pass, so they are cleared here and not in
            // the simulation step: the picture can run faster than the
            // simulation, and a frame with no step behind it would otherwise
            // draw the previous frame's particles a second time, at the
            // positions they had then, until the 200-entry table filled up and
            // started dropping the new ones.
            _singleParticleCount = 0;
            if (ProcessFrame || CameraMode != CameraMode.Player)
            {
                TransformCamera();
                UpdateCameraPosition();
            }
            UpdateProjection();
            PrepareEnvironmentalParticles(frameQuality, capturedPresentationTick,
                capturedRenderFraction);
            PrepareImpactDecals(frameQuality, capturedPresentationTick);
            (EnhancedEnvironment frameEnvironment,
                EnhancedColorGradeSelection frameColorGrade)
                = ResolveEnhancedEnvironmentSnapshot(frameQuality,
                    sdlBackend
#if ANDROID
                    || _glesEnhancedActive
#endif
                    );
            RenderReflectionProbe? frameReflectionProbe
                = ResolveEnhancedReflectionProbeSnapshot(frameQuality, sdlBackend,
                    frameEnvironment);
            RenderColorGradeState frameColorGradeState
                = RenderColorGradeState.FromSelection(frameColorGrade);
            RenderSkyState? frameSky = ResolveEnhancedSkySnapshot(frameQuality,
                sdlBackend, CapturedPresentationTime);
            _renderFrame.CaptureState(
                _viewMatrix,
                _viewInvRotMatrix,
                _viewInvRotYMatrix,
                _perspectiveMatrix,
                _cameraPosition,
                Size,
                target,
                new Vector4(_clearColor.R, _clearColor.G, _clearColor.B, _clearColor.A),
                _light1Vector,
                _light1Color,
                _light2Vector,
                _light2Color,
                _hasFog,
                _fogColor,
                _fogOffset,
                _fogSlope,
                new RenderFrameOptions(
                    _showTextures,
                    _showColors,
                    _wireframe,
                    _faceCulling,
                    FilteringOn,
                    LightingOn,
                    FogOn,
                    Mods.RenderOptions.CelShading,
                    Mods.RenderOptions.CelBands,
                    Mods.RenderOptions.CelEdge,
                    _volumeEdges,
                    ShowInvisibleEntities,
                    false,
                    frameQuality),
                frameEnvironment.Exposure);
            _renderFrame.CaptureColorGrade(frameColorGradeState);
            if (frameSky is not null)
            {
                _renderFrame.CaptureSky(frameSky);
            }
            bool enhancedFrame = (sdlBackend
#if ANDROID
                || _glesEnhancedActive
#endif
                )
                && frameQuality.GraphicsPreset == Mods.GraphicsPreset.Enhanced;
            _renderFrame.CaptureEnhancedFog(RenderEnhancedFogState.FromEnvironment(
                frameEnvironment, enhancedFrame && FogOn));
            // Directional shadows remain SDL-only; Android consumes the
            // environment exposure/LUT but does not allocate a shadow map.
            ShadowQualitySettings shadowQuality = sdlBackend
                && frameQuality.GraphicsPreset == Mods.GraphicsPreset.Enhanced
                && LightingOn
                ? ShadowQualityPolicy.Default
                : ShadowQualityPolicy.Resolve(ShadowQuality.Off);
            if (PrimaryShadowLightPolicy.TrySelect(_light1Vector, _light1Color,
                    _light2Vector, _light2Color,
                    frameEnvironment.PrimaryShadowLightIndex,
                    out PrimaryShadowLight primaryShadow)
                && StableShadowProjectionPolicy.TryCreate(_cameraPosition,
                    primaryShadow.Direction, shadowQuality,
                    StableShadowProjectionPolicy.DefaultHalfExtent,
                    StableShadowProjectionPolicy.DefaultDepthRange,
                    out StableShadowProjection shadowProjection))
            {
                _renderFrame.CaptureDirectionalShadow(
                    new RenderDirectionalShadowState(primaryShadow.SourceIndex,
                        primaryShadow.Direction, shadowProjection.ViewProjection,
                        shadowQuality.MapSize, shadowQuality.PcfRadius));
            }
            if (frameColorGradeState.Enabled)
            {
                // The immutable bytes are captured before Seal; the backend
                // never consults the optional pack or mutable room state.
                _renderFrame.CaptureTexture(frameColorGrade.Lut.Pixels);
            }
            if (frameReflectionProbe != null)
            {
                // Six decoded immutable faces are selected with the same
                // pre-draw room snapshot as environment/exposure/LUT state.
                _renderFrame.CaptureReflectionProbe(frameReflectionProbe);
            }
            Mods.Network.AuthoritativePlay? presentation = Mods.Network.AuthoritativePlay.Current;
            try
            {
                presentation?.BeginRemotePresentation(World);
                GetDrawItems();
                SubmitImpactDecals();
            }
            finally { presentation?.EndRemotePresentation(); }
            SubmitTransientVisualLights(capturedPresentationTick,
                transientLightPresentationTime);
            for (int i = 0; i < _renderFrame.Submissions.Count; i++)
            {
                DrawSubmission submission = _renderFrame.Submissions[i];
                if (submission.GeometryIdentity != null
                    && _portableMeshes.TryGetValue(submission.GeometryIdentity, out CpuMesh? mesh))
                {
                    _renderFrame.CaptureMesh(submission.GeometryIdentity, mesh);
                }
                else if (submission.Primitive != RenderPrimitive.Mesh)
                {
                    // Dynamic geometry is part of the sealed frame too. The
                    // backend never interprets legacy Points arrays.
                    _renderFrame.CaptureMesh(submission,
                        DynamicPrimitiveCompiler.Compile(submission));
                }
                if (submission.TextureIdentity is TextureIdentity identity
                    && TryGetSubmissionTexture(identity, out RenderTexturePixels? texture))
                {
                    if (texture == null) continue;
                    // Palette overrides are draw constants, but the sealed
                    // material also owns the final identity. Register an
                    // exact-key immutable record so the backend never has to
                    // guess which palette variant a draw meant.
                    if (texture.Identity != identity)
                    {
                        texture = new RenderTexturePixels(identity, texture.Width, texture.Height,
                            texture.Rgba8, texture.Revision, texture.OnlyOpaque,
                            texture.AlphaWeightedFlatColor);
                    }
                    _renderFrame.CaptureTexture(texture);
                }
                if (submission.Material.Enhanced?.Normal is TextureIdentity normalTexture)
                {
                    CapturePresentationTexture(normalTexture);
                }
                if (submission.Material.Enhanced?.Albedo is TextureIdentity albedoTexture
                    && albedoTexture != submission.TextureIdentity)
                {
                    CapturePresentationTexture(albedoTexture);
                }
                if (submission.Material.Enhanced?.Emissive is TextureIdentity emissiveTexture)
                {
                    CapturePresentationTexture(emissiveTexture);
                }
            }
            // Match legacy OnDrawFrame -> OnRenderFrame ordering: world draw
            // items describe the old room first, then the fade transition may
            // load the next room.  The presentation commands are recorded
            // after that update, as the legacy HUD was drawn after
            // UpdateUniforms, but the already captured world queue is kept.
            // A fade exit suppresses this picture entirely. The environment,
            // exposure, quality and LUT deliberately remain the coherent
            // pre-draw old-room snapshot even if UpdateFade loads a new room.
            if (sdlBackend)
            {
                if (ProcessFrame)
                {
                    UpdateFade(updateDevice: false);
                }
                if (_exiting)
                {
                    _renderFrame.Reset();
                    _renderFrame.Seal();
                    return;
                }
                // Do not recapture world state here. UpdateFade may have loaded
                // another room, while this frame's submissions still belong
                // to the room captured before GetDrawItems.
                CapturePresentationFrame(target, capturedCombatPresentationTick,
                    capturedRenderFraction);
                CapturePresentationResources();
                AttachSdlCaptureRequests();
            }
#if ANDROID
            _glesWorldContext.BeginFrame(_shaderLocations,
                _glesEnhancedActive ? _glesEnhanced : null);
            for (int i = 0; i < _renderFrame.Submissions.Count; i++)
            {
                DrawSubmission submission = _renderFrame.Submissions[i];
                _glesWorldContext.Add(submission, GetLegacyTexture(submission), _renderFrame);
            }
            _glesWorldContext.Seal();
#endif
            _renderFrame.Seal();
        }

        /// <summary>
        /// Attach requests while the frame is still mutable. A recording
        /// request stays pending until a successful presentation so a
        /// minimized or failed-submit frame retries the same output name and
        /// request identity instead of advancing the recording counter.
        /// </summary>
        private void AttachSdlCaptureRequests()
        {
            if (_pendingSdlScreenshot != null)
            {
                _renderFrame.AddCaptureRequest(ForCurrentPresentationSize(_pendingSdlScreenshot));
            }
            if (_recording)
            {
                _pendingSdlRecordingRequest ??= new RenderCaptureRequest(
                    Guid.NewGuid(),
                    Mods.Render.FrameTiming.TotalFrames,
                    CaptureTargetKind.FinalPresentedFrame,
                    Size.X,
                    Size.Y,
                    CapturePixelFormat.Rgb8,
                    CaptureRowOrientation.BottomUp,
                    CaptureDeliveryKind.Recording,
                    $"frame{_framesRecorded:0000}");
                _renderFrame.AddCaptureRequest(ForCurrentPresentationSize(_pendingSdlRecordingRequest));
            }
            for (int i = 0; i < _pendingSdlToolCaptures.Count; i++)
            {
                _renderFrame.AddCaptureRequest(_pendingSdlToolCaptures[i]);
            }
        }

        private RenderCaptureRequest ForCurrentPresentationSize(RenderCaptureRequest request)
        {
            if (request.Width == Size.X && request.Height == Size.Y) return request;
            // A failed submit can leave a request alive across a window
            // resize. Preserve its identity/name for retry while describing
            // the dimensions of the next real final-composite target.
            return new RenderCaptureRequest(request.RequestId, request.OriginatingFrame,
                request.Target, Size.X, Size.Y, request.PixelFormat,
                request.RowOrientation, request.Delivery, request.OutputName);
        }

        /// <summary>
        /// Queue one backend-neutral capture for the next SDL picture. The
        /// request is consumed only by <see cref="AfterRenderFrame"/>, which
        /// is called after a real submission; a failed/minimized picture
        /// therefore retries the same request without changing its identity.
        /// </summary>
        internal void QueueSdlCapture(RenderCaptureRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (RenderBackendSelection.Current != RenderBackendKind.Sdl)
            {
                throw new InvalidOperationException("SDL capture requests require the SDL renderer.");
            }
            // A failed submit leaves the prior request owned by the
            // presentation for retry. Tool callers may issue the same logical
            // request again on the next picture; do not attach a duplicate
            // transfer with a new identity while the original is pending.
            for (int i = 0; i < _pendingSdlToolCaptures.Count; i++)
            {
                RenderCaptureRequest pending = _pendingSdlToolCaptures[i];
                if (pending.Target == request.Target
                    && string.Equals(pending.OutputName, request.OutputName,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }
            if (_pendingSdlToolCaptures.Count >= RenderFrame.DefaultMaximumCaptureRequests)
            {
                throw new InvalidOperationException("The SDL tool capture queue is full.");
            }
            _pendingSdlToolCaptures.Add(request);
        }

        private Matrix4 HudProjectionMatrix()
            => Matrix4.CreateOrthographic(Size.X, Size.Y, 0.5f, 1.5f);

        /// <summary>
        /// Capture the presentation half of the legacy render routine without
        /// entering OpenGL. This is intentionally called only by the SDL
        /// frontend after world items and the gated fade update have finished.
        /// The legacy path continues to execute the original methods in
        /// <see cref="OnRenderFrame"/>.
        /// </summary>
        private void CapturePresentationFrame(Vector2i target,
            uint presentationTick, float renderFraction)
        {
            bool playerHud = World.LocalPlayer!.LoadFlags.TestFlag(LoadFlags.Active)
                && CameraMode == CameraMode.Player;
            bool scoreboard = ScoreboardOverFreeCamera;
            PlayerPresentation hud = World.LocalPlayer!.GetPresentation();

            bool enhancedVisor = EnhancedVisorPolicy.IsEligible(
                _renderFrame.Options.Quality.GraphicsPreset, playerHud,
                Mods.SpectatorMode.IsSpectating, World.LocalPlayer.Health > 0,
                World.LocalPlayer.IsAltForm, World.LocalPlayer.IsMorphing,
                World.LocalPlayer.CameraType == CameraType.First,
                World.CameraSequences.Current != null);
            _renderFrame.CaptureVisor(enhancedVisor
                ? hud.CaptureVisorState(presentationTick, renderFraction,
                    CapturedPresentationTime)
                : RenderVisorState.Disabled);

            bool celEnabled = Mods.RenderOptions.CelShading && Mods.RenderOptions.CelEdge > 0;
            Vector2 texelSize = target.X > 0 && target.Y > 0
                ? new Vector2(1f / target.X, 1f / target.Y)
                : Vector2.Zero;
            _renderFrame.CaptureCelState(new RenderCelState(celEnabled,
                Mods.RenderOptions.CelEdge, Mods.RenderOptions.CelBands, texelSize,
                _nearClip, ProjectionFarClip, _depthQuantum));

            bool disrupted = hud.HudDisruptedState != 0 || hud.HudWhiteoutState != -1;
            if (!disrupted)
            {
                _renderFrame.CaptureDisruption(RenderDisruptionState.Disabled);
            }
            else
            {
                float div = World.ElapsedTime / (1 / 30f);
                _renderFrame.CaptureDisruption(new RenderDisruptionState(true,
                    hud.HudDisruptionFactor, (int)div, div % 1, hud.HudWhiteoutFactor,
                    _presentationShiftTable,
                    hud.HudWhiteoutFactor != 0 ? PlayerPresentation.HudWhiteoutTable : null));
            }

            _renderFrame.CaptureComposite(new RenderCompositeState(Size, target,
                Mods.RenderOptions.ResolutionScale < 100
                    ? RenderCompositeFilter.Linear : RenderCompositeFilter.Nearest,
                ClearDestination: true));

#if ANDROID
            _capturingPresentationFrame = true;
#endif
            try
            {
                // This marker is diagnostic only; the model submissions live
                // in their own list so the backend cannot accidentally run
                // them after the cel/composite boundary.
                _capturingPresentationStage = RenderPresentationStage.HudScene;
                _renderFrame.AddOverlayCommand(
                    RenderOverlayCommand.StageMarker(RenderPresentationStage.HudScene));
                if (playerHud || scoreboard)
                {
                    hud.DrawHudModels();
                }

                _capturingPresentationStage = RenderPresentationStage.Cel;
                _renderFrame.AddOverlayCommand(
                    RenderOverlayCommand.StageMarker(RenderPresentationStage.Cel));
                _capturingPresentationStage = RenderPresentationStage.SceneComposite;
                _renderFrame.AddOverlayCommand(
                    RenderOverlayCommand.StageMarker(RenderPresentationStage.SceneComposite));
                _capturingPresentationStage = RenderPresentationStage.HudOverlay;
                _renderFrame.AddOverlayCommand(
                    RenderOverlayCommand.StageMarker(RenderPresentationStage.HudOverlay));

                if (playerHud)
                {
                    // The layer order is part of the original renderer's
                    // output, not a sorting policy for the backend.
                    CaptureHudLayer(Layer4Info); // ice layer
                    CaptureHudLayer(Layer3Info); // helmet back
                    CaptureHudLayer(Layer1Info); // visor
                    CaptureHudLayer(Layer2Info); // helmet front
                    CaptureHudLayer(Layer5Info); // dialog overlay
                    hud.DrawHudObjects();
                }
                else if (scoreboard)
                {
                    hud.DrawHudObjects();
                }

                _capturingPresentationStage = RenderPresentationStage.SpectatorOverlay;
                _renderFrame.AddOverlayCommand(
                    RenderOverlayCommand.StageMarker(RenderPresentationStage.SpectatorOverlay));
                SpectatorCamera.Draw(this);
                AdditionalOverlay?.Invoke(this);
                _capturingPresentationStage = RenderPresentationStage.ReplayOverlay;
                _renderFrame.AddOverlayCommand(
                    RenderOverlayCommand.StageMarker(RenderPresentationStage.ReplayOverlay));
                if (!_isolatedPresentation) Mods.Network.ReplayControls.Draw(this);

                RenderFadeState fade = CaptureFadeState(playerHud);
                _renderFrame.CaptureFade(fade);
                _capturingPresentationStage = RenderPresentationStage.Fade;
                _renderFrame.AddOverlayCommand(
                    RenderOverlayCommand.StageMarker(RenderPresentationStage.Fade));
                if (fade.Active && fade.Coverage > 0)
                {
                    _renderFrame.AddOverlayCommand(new RenderOverlayCommand(
                        RenderOverlayKind.Fade, FullscreenOverlayVertices(),
                        color: new Vector4(fade.Color, fade.Color, fade.Color, 1),
                        alpha: fade.Coverage, stage: RenderPresentationStage.Fade));
                }
            }
            finally
            {
#if ANDROID
                _capturingPresentationFrame = false;
#endif
            }
        }

        private RenderFadeState CaptureFadeState(bool playerHud)
        {
            if (!playerHud || _fadeType == FadeType.None)
            {
                return RenderFadeState.None;
            }
            float coverage = _fadeIn ? 1 - _fadePercent : _fadePercent;
            return new RenderFadeState(true, _fadeType, _fadeColor, _fadeIn,
                _fadePercent, coverage);
        }

        private void CapturePresentationResources()
        {
            for (int i = 0; i < _renderFrame.HudSceneItems.Count; i++)
            {
                RenderHudSceneSubmission submission = _renderFrame.HudSceneItems[i];
                if (submission.GeometryIdentity != null
                    && _portableMeshes.TryGetValue(submission.GeometryIdentity, out CpuMesh? mesh))
                {
                    _renderFrame.CaptureMesh(submission.GeometryIdentity, mesh);
                }
                if (submission.TextureIdentity is TextureIdentity identity)
                {
                    CapturePresentationTexture(identity);
                }
            }
            for (int i = 0; i < _renderFrame.OverlayCommands.Count; i++)
            {
                RenderOverlayCommand command = _renderFrame.OverlayCommands[i];
                if (command.Texture is TextureIdentity texture)
                {
                    CapturePresentationTexture(texture);
                }
                if (command.MaskTexture is TextureIdentity mask)
                {
                    CapturePresentationTexture(mask);
                }
            }
        }

        private void CapturePresentationTexture(TextureIdentity identity)
        {
            if (!TryGetSubmissionTexture(identity, out RenderTexturePixels? texture) || texture == null)
            {
                return;
            }
            // Palette overrides are draw constants, but the frame lookup is
            // exact-keyed. Clone the immutable pixels under the final identity
            // when the shared resource was registered under its base key.
            if (texture.Identity != identity)
            {
                texture = new RenderTexturePixels(identity, texture.Width, texture.Height,
                    texture.Rgba8, texture.Revision, texture.OnlyOpaque,
                    texture.AlphaWeightedFlatColor);
            }
            _renderFrame.CaptureTexture(texture);
        }

        private void CaptureHudTexture(int bindingId, out TextureIdentity? identity)
        {
            identity = null;
            if (bindingId < 0 || !TryGetTextureIdentityForBinding(bindingId, out TextureIdentity value))
            {
                return;
            }
            if (value.Variant is DynamicTextureSource
                && _textureResources.TryGetValue(value, out RenderTexturePixels? texture)
                && texture != null)
            {
                TextureIdentity revisionIdentity = new TextureIdentity(value.Source,
                    value.TextureId, value.PaletteId, value.RecolorId,
                    new DynamicTextureRevision(value, texture.Revision), value.PaletteOverride);
                _renderFrame.CaptureTexture(new RenderTexturePixels(revisionIdentity,
                    texture.Width, texture.Height, texture.Rgba8, texture.Revision,
                    texture.OnlyOpaque, texture.AlphaWeightedFlatColor));
                identity = revisionIdentity;
            }
            else
            {
                identity = value;
            }
        }

        private static RenderMaterial CreateHudMaterial(Material material, TextureIdentity? texture,
            float alpha, Vector4? colorOverride = null)
        {
            return new RenderMaterial
            {
                Diffuse = material.CurrentDiffuse,
                Ambient = material.CurrentAmbient,
                Specular = material.CurrentSpecular,
                Emission = Vector3.Zero,
                Alpha = alpha,
                Lighting = false,
                Textured = texture.HasValue,
                PolygonMode = material.PolygonMode,
                RenderMode = alpha < 1 ? RenderMode.Translucent : material.RenderMode,
                CullingMode = material.Culling,
                BillboardMode = BillboardMode.None,
                TexgenMode = material.TexgenMode,
                WrapX = material.XRepeat,
                WrapY = material.YRepeat,
                Texture = texture,
                TextureMatrix = Matrix4.Identity,
                ColorOverride = colorOverride,
                PaletteOverride = null,
                Wireframe = material.Wireframe != 0,
                NoLines = false
            };
        }

        private void EnsurePortableModelPrepared(Model model)
        {
            if (!_texPalMap.ContainsKey(model.Id))
            {
                InitTextures(model);
            }
            if (model.Meshes.Any(mesh => !_portableMeshes.ContainsKey(mesh.GeometryIdentity)))
            {
                PrepareCpuMeshes(model, isRoom: false);
            }
        }

        private void AddHudSceneMaterial(Material material, TextureIdentity? texture,
            float alpha, int polygonId, Matrix4 transform, int matrixStackCount,
            IReadOnlyList<float>? matrixStack, object? geometryIdentity, CpuMesh? inlineMesh = null,
            Vector4? colorOverride = null, int itemCount = 0, Vector4? currentColor = null)
        {
            RenderMaterial renderMaterial = CreateHudMaterial(material, texture, alpha, colorOverride);
            _renderFrame.AddHudSceneSubmission(new RenderHudSceneSubmission(renderMaterial,
                RenderPrimitive.Mesh, polygonId, alpha, transform, matrixStackCount, matrixStack,
                geometryIdentity, texture, LightInfo.Zero, Matrix4.Identity,
                HudProjectionMatrix(), inlineMesh, itemCount: itemCount, currentColor: currentColor));
        }

        private void CaptureHudFilterModel(ModelInstance instance, float alpha)
        {
            Model model = instance.Model;
            if (model.Materials.Count == 0)
            {
                return;
            }
            EnsurePortableModelPrepared(model);
            UpdateMaterials(model, 0);
            Material material = model.Materials[0];
            TextureIdentity? texture = GetTextureIdentity(model, material, 0);
            var vertices = new RenderVertex[]
            {
                new(new Vector3(Size.X, Size.Y, -1), new Vector4(1), Vector3.UnitZ,
                    new Vector2(1, 0)),
                new(new Vector3(-Size.X, Size.Y, -1), new Vector4(1), Vector3.UnitZ,
                    new Vector2(0, 0)),
                new(new Vector3(Size.X, -Size.Y, -1), new Vector4(1), Vector3.UnitZ,
                    new Vector2(1, 1)),
                new(new Vector3(-Size.X, -Size.Y, -1), new Vector4(1), Vector3.UnitZ,
                    new Vector2(0, 1))
            };
            var mesh = new CpuMesh(vertices, new[] { 0, 1, 2, 2, 1, 3 });
            AddHudSceneMaterial(material, texture, material.Alpha / 31f * alpha,
                polygonId: 0, Matrix4.Identity, matrixStackCount: 0, null, null, mesh);
        }

        private void CaptureHudIconModel(Vector2 position, float angle, ModelInstance instance,
            ColorRgb color, float alpha)
        {
            Model model = instance.Model;
            if (model.Materials.Count == 0 || model.Meshes.Count == 0)
            {
                return;
            }
            EnsurePortableModelPrepared(model);
            UpdateMaterials(model, 0);
            Material material = model.Materials[0];
            TextureIdentity? texture = GetTextureIdentity(model, material, 0);
            float scale = Size.Y / 192f;
            Vector3 position3d = new Vector3(position.X * Size.X - Size.X / 2,
                (1 - position.Y) * Size.Y - Size.Y / 2, -1f);
            Matrix4 transform = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(angle))
                * Matrix4.CreateScale(scale, scale, 1) * Matrix4.CreateTranslation(position3d);
            var currentColor = new Vector4(color.Red / 31f, color.Green / 31f,
                color.Blue / 31f, 1);
            Mesh mesh = model.Meshes[0];
            AddHudSceneMaterial(material, texture, alpha, polygonId: 0, transform,
                matrixStackCount: 0, null, mesh.GeometryIdentity, currentColor: currentColor);
        }

        private void CaptureHudDamageModel(ModelInstance instance)
        {
            Model model = instance.Model;
            if (model.Materials.Count == 0)
            {
                return;
            }
            EnsurePortableModelPrepared(model);
            UpdateMaterials(model, 0);
            Material material = model.Materials[0];
            TextureIdentity? texture = GetTextureIdentity(model, material, 0);
            float viewWidth = Size.X;
            float viewHeight = Size.Y;
            float xOffset = -viewWidth / 2;
            float yOffset = -viewHeight / 2;
            for (int i = 1; i < 9 && i < model.Nodes.Count; i++)
            {
                Node node = model.Nodes[i];
                if (!node.Enabled) continue;
                float width = node.MaxBounds.X - node.MinBounds.X;
                float height = node.MaxBounds.Y - node.MinBounds.Y;
                if (width == 0 || height == 0) continue;
                float newWidth = width / 256 * viewWidth * model.Scale.X;
                float newHeight = height / 192 * viewHeight * model.Scale.Y;
                Matrix4 transform = Matrix4.CreateScale(newWidth / width, newHeight / height, 1);
                transform.Row3.Xyz = new Vector3(xOffset, yOffset, -1);
                node.Animation = transform;
            }
            model.UpdateMatrixStack();
            for (int i = 1; i < 9 && i < model.Nodes.Count; i++)
            {
                Node node = model.Nodes[i];
                if (!node.Enabled) continue;
                int meshIndex = node.MeshId / 2;
                if ((uint)meshIndex >= (uint)model.Meshes.Count) continue;
                Mesh mesh = model.Meshes[meshIndex];
                AddHudSceneMaterial(material, texture, 1, polygonId: 0,
                    Matrix4.Identity, model.NodeMatrixIds.Count, model.MatrixStackValues,
                    mesh.GeometryIdentity);
            }
        }

        private static IReadOnlyList<RenderOverlayVertex> FullscreenOverlayVertices()
            => new[]
            {
                new RenderOverlayVertex(new Vector3(1, 1, 0), new Vector2(1, 1), Vector4.One),
                new RenderOverlayVertex(new Vector3(-1, 1, 0), new Vector2(0, 1), Vector4.One),
                new RenderOverlayVertex(new Vector3(1, -1, 0), new Vector2(1, 0), Vector4.One),
                new RenderOverlayVertex(new Vector3(-1, -1, 0), new Vector2(0, 0), Vector4.One)
            };

        private void CaptureHudLayer(LayerInfo info)
        {
            if (info.BindingId == -1)
            {
                return;
            }
            CaptureHudTexture(info.BindingId, out TextureIdentity? texture);
            float viewWidth = Size.X;
            float viewHeight = Size.Y;
            float width;
            float height;
            if (info.ScaleX == -1 || info.ScaleY == -1)
            {
                float size = MathF.Max(viewWidth, viewHeight) / 2;
                width = size / (viewWidth / 2);
                height = size / (viewHeight / 2);
            }
            else
            {
                width = viewWidth * info.ScaleX / 2 / (viewWidth / 2);
                height = viewHeight * info.ScaleY / 2 / (viewHeight / 2);
            }
            _renderFrame.AddOverlayCommand(new RenderOverlayCommand(RenderOverlayKind.HudLayer,
                new[]
                {
                    new RenderOverlayVertex(new Vector3(width + info.ShiftX, height + info.ShiftY, 0), new Vector2(1, 0), Vector4.One),
                    new RenderOverlayVertex(new Vector3(-width + info.ShiftX, height + info.ShiftY, 0), new Vector2(0, 0), Vector4.One),
                    new RenderOverlayVertex(new Vector3(width + info.ShiftX, -height + info.ShiftY, 0), new Vector2(1, 1), Vector4.One),
                    new RenderOverlayVertex(new Vector3(-width + info.ShiftX, -height + info.ShiftY, 0), new Vector2(0, 1), Vector4.One)
                }, texture: texture, alpha: info.Alpha, useTexture: texture.HasValue,
                sourceBindingId: info.BindingId, scaleX: info.ScaleX, scaleY: info.ScaleY,
                shiftX: info.ShiftX, shiftY: info.ShiftY, stage: _capturingPresentationStage));
        }

        private void CaptureHudObject(HudObjectInstance instance, int mode, float scale)
        {
            if (!instance.Enabled)
            {
                return;
            }
            CaptureHudTexture(instance.BindingId, out TextureIdentity? texture);
            float x = instance.PositionX;
            float y = instance.PositionY;
            float width = instance.Width;
            float height = instance.Height;
            if (mode == 2)
            {
                width = width / 256 * Size.X;
                height = height / 192 * Size.Y;
            }
            else if (mode == 1)
            {
                float aspect = height / width;
                height = height / 192 * Size.Y;
                width = height / aspect;
            }
            else
            {
                float aspect = width / height;
                width = width / 256 * Size.X;
                height = width / aspect;
            }
            width *= scale;
            height *= scale;
            float viewLeft = -Size.X / 2;
            float viewTop = Size.Y / 2;
            float left = viewLeft + x * Size.X - (instance.Center ? width / 2 : 0);
            float right = left + width;
            float top = viewTop - y * Size.Y + (instance.Center ? height / 2 : 0);
            float bottom = top - height;
            left /= Size.X / 2;
            right /= Size.X / 2;
            top /= Size.Y / 2;
            bottom /= Size.Y / 2;
            if (instance.FlipHorizontal) (right, left) = (left, right);
            if (instance.FlipVertical) (bottom, top) = (top, bottom);
            TextureIdentity? mask = null;
            if (instance.UseMask && Layer1Info.MaskId != -1)
            {
                CaptureHudTexture(Layer1Info.MaskId, out mask);
            }
            _renderFrame.AddOverlayCommand(new RenderOverlayCommand(RenderOverlayKind.HudObject,
                new[]
                {
                    new RenderOverlayVertex(new Vector3(right, top, 0), new Vector2(1, 0), Vector4.One),
                    new RenderOverlayVertex(new Vector3(left, top, 0), new Vector2(0, 0), Vector4.One),
                    new RenderOverlayVertex(new Vector3(right, bottom, 0), new Vector2(1, 1), Vector4.One),
                    new RenderOverlayVertex(new Vector3(left, bottom, 0), new Vector2(0, 1), Vector4.One)
                }, texture: texture, maskTexture: mask, alpha: instance.Alpha,
                useTexture: texture.HasValue, useMask: instance.UseMask && mask.HasValue,
                sourceBindingId: instance.BindingId, mode: mode, scale: scale,
                stage: _capturingPresentationStage));
        }

        private void CaptureHudFlatBox(float left, float top, float right, float bottom, Vector4 color)
            => _renderFrame.AddOverlayCommand(CreateHudFlatBoxCommand(
                Size, left, top, right, bottom, color, _capturingPresentationStage));

        internal static RenderOverlayCommand CreateHudFlatBoxCommand(Vector2i size,
            float left, float top, float right, float bottom, Vector4 color,
            RenderPresentationStage stage)
        {
            float halfW = size.X / 2f;
            float halfH = size.Y / 2f;
            float x0 = (left / 256f * size.X - halfW) / halfW;
            float x1 = (right / 256f * size.X - halfW) / halfW;
            float y0 = (halfH - top / 192f * size.Y) / halfH;
            float y1 = (halfH - bottom / 192f * size.Y) / halfH;
            return new RenderOverlayCommand(RenderOverlayKind.FlatBox,
                new[]
                {
                    new RenderOverlayVertex(new Vector3(x1, y0, 0), Vector2.Zero, color),
                    new RenderOverlayVertex(new Vector3(x0, y0, 0), Vector2.Zero, color),
                    new RenderOverlayVertex(new Vector3(x1, y1, 0), Vector2.Zero, color),
                    new RenderOverlayVertex(new Vector3(x0, y1, 0), Vector2.Zero, color)
                }, color: color, stage: stage);
        }

        private void CaptureHudTexture(TextureIdentity texture, float left, float top, float right,
            float bottom, Vector4 color, Vector4 uvRect, float rotation, float alpha)
            => _renderFrame.AddOverlayCommand(CreateHudTextureCommand(Size, texture, left, top, right,
                bottom, color, uvRect, rotation, alpha, _capturingPresentationStage));

        internal static RenderOverlayCommand CreateHudTextureCommand(Vector2i size, TextureIdentity texture,
            float left, float top, float right, float bottom, Vector4 color, Vector4 uvRect,
            float rotation, float alpha, RenderPresentationStage stage)
        {
            if (size.X <= 0 || size.Y <= 0) throw new ArgumentOutOfRangeException(nameof(size));
            if (!float.IsFinite(rotation)) rotation = 0;
            float halfW = size.X / 2f, halfH = size.Y / 2f;
            float x0 = (left / 256f * size.X - halfW) / halfW;
            float x1 = (right / 256f * size.X - halfW) / halfW;
            float y0 = (halfH - top / 192f * size.Y) / halfH;
            float y1 = (halfH - bottom / 192f * size.Y) / halfH;
            Vector2 center = new((uvRect.X + uvRect.Z) / 2, (uvRect.Y + uvRect.W) / 2);
            float cosine = MathF.Cos(rotation), sine = MathF.Sin(rotation);
            Vector2 Rotate(Vector2 uv)
            {
                Vector2 delta = uv - center;
                return center + new Vector2(delta.X * cosine - delta.Y * sine,
                    delta.X * sine + delta.Y * cosine);
            }
            return new RenderOverlayCommand(RenderOverlayKind.HudTexture, new[]
            {
                new RenderOverlayVertex(new Vector3(x1, y0, 0), Rotate(new Vector2(uvRect.Z, uvRect.Y)), color),
                new RenderOverlayVertex(new Vector3(x0, y0, 0), Rotate(new Vector2(uvRect.X, uvRect.Y)), color),
                new RenderOverlayVertex(new Vector3(x1, y1, 0), Rotate(new Vector2(uvRect.Z, uvRect.W)), color),
                new RenderOverlayVertex(new Vector3(x0, y1, 0), Rotate(new Vector2(uvRect.X, uvRect.W)), color)
            }, texture: texture, alpha: alpha, useTexture: true, color: color, stage: stage);
        }

        private void CaptureHudGeometry(IReadOnlyList<HudGeometryVertex> geometry)
            => _renderFrame.AddOverlayCommand(CreateHudGeometryCommand(Size, geometry, _capturingPresentationStage));

        internal static RenderOverlayCommand CreateHudGeometryCommand(Vector2i size,
            IReadOnlyList<HudGeometryVertex> geometry, RenderPresentationStage stage)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            if (geometry.Count < 3 || geometry.Count > 2048)
                throw new ArgumentOutOfRangeException(nameof(geometry));
            float halfW = size.X / 2f, halfH = size.Y / 2f;
            var vertices = new RenderOverlayVertex[geometry.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                HudGeometryVertex vertex = geometry[i];
                float x = (vertex.Position.X / 256f * size.X - halfW) / halfW;
                float y = (halfH - vertex.Position.Y / 192f * size.Y) / halfH;
                vertices[i] = new RenderOverlayVertex(new Vector3(x, y, 0), Vector2.Zero, vertex.Color);
            }
            return new RenderOverlayCommand(RenderOverlayKind.HudGeometry, vertices, stage: stage);
        }

        private void CaptureHudRadialSector(int sector, Vector4 color)
        {
            var vertices = new List<RenderOverlayVertex>(14);
            for (int step = 0; step <= 6; step++)
            {
                Vector2 outer = Mods.Input.WeaponRadialSelection.SectorPoint(sector, step / 6f, 87, 58);
                Vector2 inner = Mods.Input.WeaponRadialSelection.SectorPoint(sector, step / 6f, 19, 13);
                vertices.Add(new RenderOverlayVertex(new Vector3(outer.X / 128, (4 - outer.Y) / 96, 0), Vector2.Zero, color));
                vertices.Add(new RenderOverlayVertex(new Vector3(inner.X / 128, (4 - inner.Y) / 96, 0), Vector2.Zero, color));
            }
            _renderFrame.AddOverlayCommand(new RenderOverlayCommand(RenderOverlayKind.RadialSector,
                vertices, color: color, stage: _capturingPresentationStage));
        }

        private void CaptureCustomCrosshair(Vector3 color, Vector2 position)
        {
            float halfW = Size.X / 2f;
            float halfH = Size.Y / 2f;
            Vector4 fill = new Vector4(color, 1);
            IReadOnlyList<Mods.Render.CrosshairBar> bars =
                Mods.Render.Crosshair.BarsOf(Mods.Render.Crosshair.Style, Mods.Render.Crosshair.Scale);
            for (int i = 0; i < bars.Count; i++)
            {
                (float left, float right, float bottom, float top) = Mods.Render.Crosshair.EdgesOf(bars[i]);
                _renderFrame.AddOverlayCommand(new RenderOverlayCommand(RenderOverlayKind.Crosshair,
                    new[]
                    {
                        new RenderOverlayVertex(Mods.Render.Crosshair.OffsetNdc(position, right, top, halfW, halfH), Vector2.Zero, fill),
                        new RenderOverlayVertex(Mods.Render.Crosshair.OffsetNdc(position, left, top, halfW, halfH), Vector2.Zero, fill),
                        new RenderOverlayVertex(Mods.Render.Crosshair.OffsetNdc(position, right, bottom, halfW, halfH), Vector2.Zero, fill),
                        new RenderOverlayVertex(Mods.Render.Crosshair.OffsetNdc(position, left, bottom, halfW, halfH), Vector2.Zero, fill)
                    }, color: fill, stage: _capturingPresentationStage));
            }
            (float radius, float thickness) = Mods.Render.Crosshair.RingOf(
                Mods.Render.Crosshair.Style, Mods.Render.Crosshair.Scale);
            if (thickness <= 0) return;
            const int segments = 40;
            float inner = radius - thickness / 2;
            float outer = radius + thickness / 2;
            var vertices = new List<RenderOverlayVertex>((segments + 1) * 2);
            for (int i = 0; i <= segments; i++)
            {
                float angle = MathHelper.TwoPi * i / segments;
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);
                vertices.Add(new RenderOverlayVertex(Mods.Render.Crosshair.OffsetNdc(position, outer * cos, outer * sin, halfW, halfH), Vector2.Zero, fill));
                vertices.Add(new RenderOverlayVertex(Mods.Render.Crosshair.OffsetNdc(position, inner * cos, inner * sin, halfW, halfH), Vector2.Zero, fill));
            }
            _renderFrame.AddOverlayCommand(new RenderOverlayCommand(RenderOverlayKind.Crosshair,
                vertices, color: fill, stage: _capturingPresentationStage));
        }

        private bool TryGetSubmissionTexture(TextureIdentity identity, out RenderTexturePixels? texture)
        {
            if (_textureResources.TryGetValue(identity, out texture)) return true;
            TextureIdentity baseIdentity = identity.WithPaletteOverride(null);
            return _textureResources.TryGetValue(baseIdentity, out texture);
        }

        public Matrix4 GetPerspectiveMatrix(float fov)
        {
            float aspect = Size.X / (float)Size.Y;
            return Matrix4.CreatePerspectiveFieldOfView(fov, aspect, _nearClip, _useClip ? _farClip : 10000f);
        }

        private void UpdateProjection()
        {
            // todo: update this only when the viewport or camera values change
            _perspectiveMatrix = GetPerspectiveMatrix(_cameraFov);
#if ANDROID
            GL.UniformMatrix4(_shaderLocations.ProjectionMatrix, transpose: false, ref _perspectiveMatrix);
#endif
            // update frustum info
            Vector3 camPos = World.LocalPlayer!.CameraInfo.Position;
            var camRight = new Vector3(_viewMatrix.Row0.X, _viewMatrix.Row0.Y, -_viewMatrix.Row0.Z);
            var camUp = new Vector3(_viewMatrix.Row1.X, _viewMatrix.Row1.Y, -_viewMatrix.Row1.Z);
            var camFacing = new Vector3(_viewMatrix.Row2.X, _viewMatrix.Row2.Y, -_viewMatrix.Row2.Z);

            Vector4 ComputePlane(Vector3 input)
            {
                var normal = new Vector3(
                    Vector3.Dot(input, camRight),
                    Vector3.Dot(input, camUp),
                    Vector3.Dot(input, camFacing)
                );
                float w = Vector3.Dot(normal, camPos);
                return new Vector4(normal, w);
            }

            float aspect = Size.X / (float)Size.Y;
            float cosFov = MathF.Cos(_cameraFov / 2);
            float cosFovDiv = cosFov / aspect;
            float sinFov = MathF.Sin(_cameraFov / 2);

            FrustumInfo.Index = 1;
            FrustumInfo.Count = 5;
            // near plane
            FrustumInfo.Planes[0] = SetBoundsIndices(ComputePlane(Vector3.UnitZ).AddW(_nearClip));
            // right plane
            Vector3 temp = new Vector3(cosFovDiv, 0, sinFov).Normalized();
            FrustumInfo.Planes[1] = SetBoundsIndices(ComputePlane(temp));
            // left plane
            temp = new Vector3(-cosFovDiv, 0, sinFov).Normalized();
            FrustumInfo.Planes[2] = SetBoundsIndices(ComputePlane(temp));
            // bottom plane
            temp = new Vector3(0, -cosFov, sinFov).Normalized();
            FrustumInfo.Planes[3] = SetBoundsIndices(ComputePlane(temp));
            // top plane
            temp = new Vector3(0, cosFov, sinFov).Normalized();
            FrustumInfo.Planes[4] = SetBoundsIndices(ComputePlane(temp));
        }

        public static FrustumPlane SetBoundsIndices(Vector4 plane)
        {
            int xIndex1 = 0; // min.x
            int xIndex2 = 3; // max.x
            if (plane.X < 0)
            {
                xIndex1 = 3;
                xIndex2 = 0;
            }
            int yIndex1 = 1; // min.y
            int yIndex2 = 4; // max.y
            if (plane.Y < 0)
            {
                yIndex1 = 4;
                yIndex2 = 1;
            }
            int zIndex1 = 2; // min.z
            int zIndex2 = 5; // max.z
            if (plane.Z < 0)
            {
                zIndex1 = 5;
                zIndex2 = 2;
            }
            return new FrustumPlane()
            {
                Plane = plane,
                XIndex1 = xIndex1,
                XIndex2 = xIndex2,
                YIndex1 = yIndex1,
                YIndex2 = yIndex2,
                ZIndex1 = zIndex1,
                ZIndex2 = zIndex2
            };
        }

        /// <summary>
        /// Read back the offscreen target the 3D passes render into.
        ///
        /// Not the window's back buffer: a window that is never shown has no
        /// usable one under Mesa, which is why every headless capture came
        /// out black on Linux while the same code worked on Windows. This
        /// target is a texture the scene owns, so it holds the frame whether
        /// or not anything is on screen. It carries the world but not the HUD,
        /// which is drawn straight to the window afterwards.
        /// </summary>
        /// <summary>
        /// Whether the offscreen target this scene draws into is usable.
        /// Reported rather than asserted: a release build has no debugger to
        /// break into, and an incomplete framebuffer is silent otherwise.
        /// </summary>
#if ANDROID
        public FramebufferErrorCode FramebufferStatus { get; private set; }
            = FramebufferErrorCode.FramebufferComplete;

        /// <summary>
        /// The first GL error since the last time this was asked, drained.
        /// A driver that refuses immediate-mode calls raises InvalidOperation
        /// on every one of them and renders nothing, with no other symptom.
        /// </summary>
        public OpenTK.Graphics.OpenGL.ErrorCode DrainGlError()
        {
            var first = OpenTK.Graphics.OpenGL.ErrorCode.NoError;
            for (int i = 0; i < 64; i++)
            {
                OpenTK.Graphics.OpenGL.ErrorCode code = GL.GetError();
                if (code == OpenTK.Graphics.OpenGL.ErrorCode.NoError)
                {
                    break;
                }
                if (first == OpenTK.Graphics.OpenGL.ErrorCode.NoError)
                {
                    first = code;
                }
            }
            return first;
        }

        /// <summary>
        /// Read the window's own buffer, HUD and all.
        ///
        /// <see cref="ReadSceneTarget"/> reads the offscreen target, which the
        /// HUD is deliberately not drawn into -- it goes to the default
        /// framebuffer after the target is unbound (see OnRenderFrame), which
        /// is what lets every thumbnail and map preview come out without one.
        /// So no screenshot could ever show the HUD, and HUD work could not be
        /// checked by looking at it, only by playing.
        ///
        /// Called between the draw and SwapBuffers, and **only on a window
        /// that is actually visible**: a hidden window has no usable back
        /// buffer under Mesa, which is the whole reason the offscreen target
        /// is what everything else reads.
        /// </summary>
        public byte[]? ReadWindowBuffer(out int width, out int height)
        {
            width = Size.X;
            height = Size.Y;
            if (width <= 0 || height <= 0)
            {
                return null;
            }
            byte[] buffer = new byte[width * height * 3];
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            GL.ReadBuffer(ReadBufferMode.Back);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, buffer);
            return buffer;
        }

        public byte[]? ReadSceneTarget(out int width, out int height)
        {
            Vector2i target = _glesEnhancedActive
                ? _glesEnhanced.SceneCaptureSize : _targetSize;
            width = target.X;
            height = target.Y;
            int framebuffer = _glesEnhancedActive
                ? _glesEnhanced.SceneCaptureFramebuffer : _frameBuffer;
            if (framebuffer == 0)
            {
                return null;
            }
            byte[] buffer = new byte[width * height * 3];
            // Enhanced capture is always the post-tone-map/post-HUD RGBA8
            // display-linear target. It never reinterprets the FP scene.
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgb, PixelType.UnsignedByte, buffer);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            return buffer;
        }
#endif

        /// <summary>Called only after a window successfully presents this scene.</summary>
        public void OnFramePresented()
        {
            Mods.Network.AuthoritativePlay.Current?.CommitRemotePresentation(World);
            if (RenderBackendSelection.Current == RenderBackendKind.Sdl)
            {
                // The request has crossed the backend submission boundary. A
                // failed/minimized frame never reaches this callback and keeps
                // the same request pending for the next picture.
                _pendingSdlScreenshot = null;
            }
        }

        public void AfterRenderFrame()
        {
            if (_recording)
            {
#if ANDROID
                ScreenCapture.Record(Size.X, Size.Y, $"frame{_framesRecorded:0000}");
#else
                _pendingSdlRecordingRequest = null;
#endif
                _framesRecorded++;
            }
            if (RenderBackendSelection.Current == RenderBackendKind.Sdl)
            {
                _pendingSdlToolCaptures.Clear();
            }
            _advanceOneFrame = false;
        }

        /// <summary>
        /// Give the offscreen target a depth buffer the ink pass can read, or
        /// take it away again.
        ///
        /// A renderbuffer is the cheaper attachment and is what the target has
        /// whenever nothing needs to read the depth back -- which is every
        /// frame that is not cel shaded, and matters most on a phone, where a
        /// depth *texture* has to be written out to memory that a tiler would
        /// otherwise never touch. So the swap follows the setting rather than
        /// being made once at startup, and turning cel shading on from the
        /// pause menu grows outlines on the next frame instead of the next
        /// match.
        ///
        /// A driver that will not complete the framebuffer with a depth
        /// texture gets the renderbuffer back and is not asked again; the
        /// banding still works, and the alternative is a target that draws
        /// nothing at all.
        /// </summary>
#if ANDROID
        private void UpdateDepthAttachment(Vector2i target)
        {
            bool want = !_depthTextureRefused && Mods.RenderOptions.CelShading
                && Mods.RenderOptions.CelEdge > 0;
            if (want == (_depthTexture != 0))
            {
                return;
            }
            if (!want)
            {
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer,
                    _renderBuffer);
                GL.DeleteTexture(_depthTexture);
                _textureCount--;
                _depthTexture = 0;
                return;
            }
            _depthTexture = GL.GenTexture();
            _textureCount++;
            GL.BindTexture(TextureTarget.Texture2D, _depthTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Depth24Stencil8,
                target.X, target.Y, 0, PixelFormat.DepthStencil, PixelType.UnsignedInt248, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthStencilAttachment, TextureTarget.Texture2D, _depthTexture, 0);
            _claimedQuantum = MeasureDepthQuantum();
            // Until a frame has been looked at, the driver's own answer is the
            // best there is.
            _depthQuantum = _claimedQuantum;
            FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                Console.WriteLine($"[render] this driver will not read the scene's depth back ({status}); "
                    + "cel shading keeps its banding and goes without the outline.");
                _depthTextureRefused = true;
                GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer,
                    _renderBuffer);
                GL.DeleteTexture(_depthTexture);
                _textureCount--;
                _depthTexture = 0;
            }
        }

        /// <summary>
        /// One step of the depth buffer, as the ink pass has to treat it.
        ///
        /// Asked for rather than assumed. `Depth24Stencil8` is what this code
        /// requests, but a driver is free to satisfy it with less, and the ink
        /// pass is looking for a second difference around a millionth of the
        /// stored value -- so being wrong about this by a factor of two
        /// hundred is the difference between an outline and straight black
        /// lines drawn along every step of the buffer, on every flat wall.
        ///
        /// The depth *half* of the attachment is what gets asked: both GL and
        /// ES answer `INVALID_OPERATION` for a component size of
        /// `DEPTH_STENCIL_ATTACHMENT` itself. Anything unexpected falls back
        /// to the 24 bits that were requested, which is what this assumed
        /// before it asked at all.
        /// </summary>
        private float MeasureDepthQuantum()
        {
            const int requested = 24;
            int bits = requested;
            bool answered = false;
            try
            {
                while (GL.GetError() != OpenTK.Graphics.OpenGL.ErrorCode.NoError)
                {
                }
                GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.DepthAttachment,
                    FramebufferParameterName.FramebufferAttachmentDepthSize, out int answer);
                if (GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError
                    && answer >= 8 && answer <= 32)
                {
                    bits = answer;
                    answered = true;
                }
            }
            catch (Exception)
            {
                // A driver that will not answer is not a reason to stop drawing.
            }
            if (!_saidDepthSize)
            {
                // Once a process, and always rather than only when it
                // surprises: this is the one number that says whether the ink
                // pass has anything to work with, and the machine that needs
                // to be asked is the one nobody here can plug in.
                _saidDepthSize = true;
                Console.WriteLine(answered
                    ? $"[render] cel outline: the depth buffer is {bits} bits"
                    : $"[render] cel outline: the driver would not say how deep the depth "
                        + $"buffer is; assuming the {requested} that were asked for");
            }
            return 1f / (MathF.Pow(2, bits) - 1);
        }

        private static bool _saidDepthSize;

        /// <summary>
        /// One step of the depth buffer as the driver reports it, and the same
        /// thing as a frame of the ink pass found it to be. They are not the
        /// same number: a driver reports the bits it stores and says nothing
        /// about what its rasteriser's interpolation was worth on the way in,
        /// and the second is the one the threshold has to clear.
        /// </summary>
        private float _claimedQuantum = 1f / (MathF.Pow(2, 24) - 1);
#endif

        private float _depthQuantum = 1f / (MathF.Pow(2, 24) - 1);

        private static readonly float[] _presentationShiftTable = CreatePresentationShiftTable();

        // Dynamic HUD bindings (especially the font surface) can be rewritten
        // several times before one frame is submitted. The source identity is
        // stable for client ownership, so the frame uses a value variant for
        // each revision it actually draws.
        private readonly record struct DynamicTextureRevision(TextureIdentity Identity, long Revision);

        private static float[] CreatePresentationShiftTable()
        {
            var shifts = new float[64];
            for (int i = 0; i < shifts.Length; i++)
            {
                int value = (i & 32) != 0 ? 31 - (i & 31) : i & 31;
                shifts[i] = -((value - 16) << 12) / 4096f / 256f;
            }
            return shifts;
        }

        public float FramesPerSecond { get; private set; }

#if ANDROID
        /// <summary>
        /// Draw the ink line over the finished scene, inside the offscreen
        /// target.
        ///
        /// A silhouette is not visible to the fragment that is on it: it is a
        /// place where this surface and the one behind it differ, which only a
        /// pass that can look at its neighbours can find. So the scene is
        /// copied to a texture of its own -- on the GPU, with no round trip
        /// through the CPU -- and read back a texel at a time by
        /// <see cref="Mods.Render.EsShaders.CelFragmentShader"/>.
        ///
        /// Inside the target rather than over the window because the helmet,
        /// the HUD and the fade are drawn after it and must not be outlined,
        /// and because <see cref="ReadSceneTarget"/> is where every screenshot
        /// and every map preview comes from: a line drawn later would be in
        /// the game and missing from all of them.
        /// </summary>
        private void DrawCelOutline()
        {
            if (!Mods.RenderOptions.CelShading || Mods.RenderOptions.CelEdge <= 0
                || _celTexture == 0 || _celShaderProgramId == 0 || _depthTexture == 0)
            {
                return;
            }
            Vector2i target = _targetSize;
            // The scene, kept where the pass can read it. First, because
            // everything below draws over the target -- including the probe,
            // which is why it can afford to.
            GL.BindTexture(TextureTarget.Texture2D, _celTexture);
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, target.X, target.Y);
            if (_calibrateInk)
            {
                _calibrateInk = false;
                CalibrateInk(target);
            }
            DrawCelQuad(target, probe: false);
        }

        /// <summary>
        /// The ink pass's own target: the scene's colour texture and nothing
        /// else.
        ///
        /// Built once and kept. The colour attachment is refreshed only if the
        /// texture it names ever changes -- a resize does not change it, since
        /// <see cref="OnResize"/> re-specifies the same texture name rather
        /// than making a new one, so in practice this is one attachment call
        /// for the life of the renderer.
        /// </summary>
        private int CelFrameBuffer()
        {
            if (_celFrameBuffer == 0)
            {
                _celFrameBuffer = GL.GenFramebuffer();
            }
            if (_celFrameBufferColor != _screenTexture)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _celFrameBuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D,
                    _screenTexture, 0);
                _celFrameBufferColor = _screenTexture;
            }
            return _celFrameBuffer;
        }

        /// <summary>
        /// The pass itself: the finished scene out of <c>_celTexture</c>, the
        /// depth the scene left behind, and a quad over the whole target.
        /// </summary>
        private void DrawCelQuad(Vector2i target, bool probe)
        {
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, _depthTexture);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _celTexture);
            GL.UseProgram(_celShaderProgramId);
            GL.Uniform1(_shaderLocations.CelTexelWidth, 1f / target.X);
            GL.Uniform1(_shaderLocations.CelTexelHeight, 1f / target.Y);
            GL.Uniform1(_shaderLocations.CelOutline, Mods.RenderOptions.CelEdge);
            GL.Uniform1(_shaderLocations.CelNearPlane, _nearClip);
            GL.Uniform1(_shaderLocations.CelFarPlane, _useClip ? _farClip : 10000f);
            GL.Uniform1(_shaderLocations.CelDepthQuantum, _depthQuantum);
            GL.Uniform1(_shaderLocations.CelProbe, probe ? 1 : 0);
            // Off the framebuffer for the length of the pass. Sampling a
            // texture that is attached to the framebuffer being drawn into is
            // undefined in OpenGL ES whether or not anything writes to it --
            // desktop GL forgives the read-only case and ES does not -- and
            // this reads the depth while drawing colour into the same target.
            // Two calls a frame; the depth is already in memory as a texture,
            // so there is nothing here for a tiler to resolve that it was not
            // resolving anyway.
            // Draw through a target that does not have the depth attached,
            // rather than taking the depth off the one that does.
            //
            // Sampling a texture attached to the framebuffer being drawn into
            // is undefined in OpenGL ES whether or not anything writes to it,
            // and this reads the depth while drawing colour -- so the two had
            // to be separated somehow. Detaching and reattaching was the first
            // way and it is the expensive one: changing an attachment on a
            // bound framebuffer makes a tile-based GPU flush the tile buffer
            // and load it back, twice a frame, over the whole screen. Measured
            // on a Mali-G78 at 2400x1080 that alone was 32 ms a frame -- cel
            // shading cost 58 ms a frame against 25 ms without it, and 26 ms
            // once this replaced it. Practically the entire cost of the
            // feature was the two calls, not the pass.
            //
            // An ordinary framebuffer switch is a path every driver expects.
            // The colour attachment is the same texture, so the pass still
            // paints the scene the rest of the frame is drawing into.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, CelFrameBuffer());
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.CullFace);
            GL.DrawFullscreenQuad();
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            // Back to the target the rest of the frame is drawn into, before
            // anything else can find the wrong one bound.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _frameBuffer);
            GL.Enable(EnableCap.DepthTest);
        }

        /// <summary>
        /// What the ink has to clear before it is believed, in the pass's own
        /// units, measured on this machine. Zero until a frame has been looked
        /// at; the shader's fixed threshold applies until then.
        /// </summary>
        private bool _calibrateInk = true;

        /// <summary>
        /// Find out what a flat surface's kink really measures here, and put
        /// the ink threshold above it.
        ///
        /// The pass is looking for a second difference around a millionth of
        /// the stored depth, so it is only as good as the depth it is handed,
        /// and how good that is cannot be asked. A driver reports the bits it
        /// stores; it does not report what its rasteriser's interpolation was
        /// worth on the way in, and no arithmetic in the shader recovers what
        /// never arrived. That is the difference between this machine, where a
        /// flat wall measures 0.004 to 0.009 against a threshold of 1.1, and a
        /// phone drawing regular lines across every surface in the room.
        ///
        /// So it is measured. One frame of this pass in probe mode paints
        /// log2 of the kink instead of the picture, that frame is read back,
        /// and the median over what was drawn stands for the flat surfaces:
        /// most of any frame is flat surface, and an edge measures hundreds,
        /// so edges sit in the tail where they cannot move a median. Six times
        /// that becomes the floor.
        ///
        /// Once a scene, in the same frame as the real pass and before it, so
        /// nobody ever sees the probe -- the scene has already been copied out
        /// to <c>_celTexture</c> by then, and the real pass paints every pixel
        /// back from it. Colour is read rather than depth because colour is
        /// what a GL ES driver is required to hand back.
        /// </summary>
        private void CalibrateInk(Vector2i target)
        {
            // A centred patch: the middle of a frame is the room, and the
            // edges of one are often sky, or the weapon. Big enough to be a
            // sample, small enough not to be a stall.
            int side = Math.Min(384, Math.Min(target.X, target.Y));
            if (side < 32)
            {
                return;
            }
            int x = (target.X - side) / 2;
            int y = (target.Y - side) / 2;
            var pixels = new byte[side * side * 4];
            try
            {
                DrawCelQuad(target, probe: true);
                GL.ReadPixels(x, y, side, side, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[render] cel outline: could not measure the depth ({ex.Message}); "
                    + "keeping the fixed threshold");
                return;
            }
            var drawn = new List<byte>(side * side / 4);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                // Zero is "nothing was drawn here", which is not a measurement.
                if (pixels[i] != 0)
                {
                    drawn.Add(pixels[i]);
                }
            }
            if (drawn.Count < side * side / 8)
            {
                // Almost nothing in the middle of the frame: a fade, or a wall
                // in the camera. Leave it for the next scene rather than
                // calibrate against that.
                _calibrateInk = true;
                return;
            }
            // Only the pixels that measured *something*.
            //
            // This is the part that has to be got right, and the median over
            // everything gets it wrong: a depth error is a step, so it shows
            // up only where a step boundary falls, and on this phone that is
            // about one pixel in thirty. The other twenty-nine measure exactly
            // zero -- two neighbours rounded to the same value as the middle
            // one -- so a median over all of them is zero however coarse the
            // buffer is, and reports a perfect machine on the one that is not.
            //
            // Among the pixels that did measure something, the typical value
            // is the step itself. Measured on a Mali-G78 it came out at
            // 2.4e-4 over the walls, the middle distance and the floor alike,
            // against the 6e-8 the same driver says it stores: the same number
            // wherever the camera pointed, which is what a property of the
            // machine should look like and what the median of everything
            // never showed.
            var kinks = new List<byte>(drawn.Count / 8);
            foreach (byte value in drawn)
            {
                // 1 is the shader's clamp floor, which means "no kink here".
                if (value > 1)
                {
                    kinks.Add(value);
                }
            }
            if (kinks.Count < 64)
            {
                // A machine whose depth arrives intact, or a frame with no
                // surface in it to judge by. Either way the driver's own
                // answer is the best there is.
                Console.WriteLine("[render] cel outline: the depth arrives intact here; "
                    + "keeping the buffer's own step.");
                return;
            }
            kinks.Sort();
            byte median = kinks[kinks.Count / 2];
            // The probe paints log2 of the kink in the depth buffer's own
            // units, -32..0 over the byte. Most of a frame is flat surface and
            // a real edge measures orders of magnitude more, so the median is
            // what a flat surface costs here -- which is the depth error
            // itself, and the number the shader wants.
            float measured = MathF.Pow(2, (median / 255f - 1f) * 32f);
            // Never below what the buffer stores: the median can land under
            // one step of it on a frame of surfaces that happen to face the
            // camera, and a floor under the truth protects nothing.
            _depthQuantum = MathF.Max(_claimedQuantum, measured);
            float ratio = measured / _claimedQuantum;
            string howBad = ratio < 2f ? "which is what the buffer stores"
                : ratio < 64f ? "which is coarser than it stores, and the ink threshold rises to match"
                : "which is far coarser than it stores; only strong creases and silhouettes will ink";
            Console.WriteLine($"[render] cel outline: a flat surface's depth is off by "
                + $"{measured.ToString("0.#######e+0", System.Globalization.CultureInfo.InvariantCulture)} "
                + $"here, {ratio.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)}x "
                + $"the {_claimedQuantum.ToString("0.#######e+0", System.Globalization.CultureInfo.InvariantCulture)} "
                + $"the driver stores -- {howBad}.");
        }

        /// <summary>
        /// Frames a second, over the half second just gone.
        ///
        /// Measured here rather than in either head because both heads drive
        /// the same method: the desktop from its window's render callback and
        /// Android from the render thread's own loop. One number, measured the
        /// same way, so the two are worth comparing with each other -- which
        /// is most of what a counter is for.
        ///
        /// Wall clock, not the frame the engine thinks it is on: the point is
        /// to catch the frames that took too long, and a fixed-step counter
        /// cannot.
        /// </summary>
        private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
        private int _fpsFrames;

        private void CountFrame()
        {
            _fpsFrames++;
            double elapsed = _fpsClock.Elapsed.TotalSeconds;
            // Long enough to be steady, short enough to answer "is it this
            // corridor?" while you are still standing in it.
            if (elapsed >= 0.5)
            {
                FramesPerSecond = (float)(_fpsFrames / elapsed);
                _fpsFrames = 0;
                _fpsClock.Restart();
            }
        }

        public bool OnRenderFrame()
        {
            CountFrame();
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            GL.ClearStencil(0);

            UpdateUniforms();
            if (_exiting)
            {
                return false;
            }
            GlesBackend.RenderWorld(_renderFrame, _glesWorldContext);

            if (_glesEnhancedActive)
            {
                // Resolve HDR/LDR scene lighting into an SDR display-linear
                // target before any authored HUD content is composed.
                _glesEnhanced.ResolveScene(_renderFrame);
                _glesEnhanced.BindComposition();
                GL.UseProgram(_glesEnhanced.SceneProgram);
            }

            if (World.LocalPlayer!.LoadFlags.TestFlag(LoadFlags.Active) && CameraMode == CameraMode.Player)
            {
                SetHudLayerUniforms();
                World.LocalPlayer!.GetPresentation().DrawHudModels();
                UnsetHudLayerUniforms();
            }
            else if (ScoreboardOverFreeCamera)
            {
                // Only the filter that dims the scene behind the scoreboard;
                // PlayerHud draws nothing else on the free camera.
                SetHudLayerUniforms();
                World.LocalPlayer!.GetPresentation().DrawHudModels();
                UnsetHudLayerUniforms();
            }

            if (_glesEnhancedActive)
            {
                // Scene-target captures include weapon/HUD-scene models, but
                // intentionally exclude disruption, visor overlays and fade.
                _glesEnhanced.CaptureScene();
                _glesEnhanced.BindComposition();
            }

            // After the weapon, so it is drawn around too, and before the
            // target is put on screen, so the helmet and the HUD are not.
            if (!_glesEnhancedActive)
            {
                DrawCelOutline();
            }

            GL.Disable(EnableCap.CullFace);
            _shaderLocations = _glesEnhancedActive
                ? _glesEnhanced.CompositionLocations : _legacyShaderLocations;
            GL.UseProgram(_glesEnhancedActive
                ? _glesEnhanced.CompositionProgram : _rttShaderProgramId);
            GL.Uniform1(_shaderLocations.LayerAlpha, 1f);
            GL.Uniform4(_shaderLocations.FadeColor, Vector4.Zero);

            if (World.LocalPlayer!.GetPresentation().HudDisruptedState != 0 || World.LocalPlayer!.GetPresentation().HudWhiteoutState != -1)
            {
                float div = World.ElapsedTime / (1 / 30f);
                int index = (int)div;
                float factor = div % 1;
                if (_glesEnhancedActive)
                {
                    _glesEnhanced.ApplyDisruption(_shiftShaderProgramId,
                        _legacyShaderLocations,
                        World.LocalPlayer!.GetPresentation().HudDisruptionFactor,
                        index, factor,
                        World.LocalPlayer!.GetPresentation().HudWhiteoutFactor,
                        PlayerPresentation.HudWhiteoutTable);
                    _glesEnhanced.BindComposition();
                }
                else
                {
                    GL.UseProgram(_shiftShaderProgramId);
                    GL.Uniform1(_shaderLocations.ShiftFactor, World.LocalPlayer!.GetPresentation().HudDisruptionFactor);
                    GL.Uniform1(_shaderLocations.ShiftIndex, index);
                    GL.Uniform1(_shaderLocations.LerpFactor, factor);
                    GL.Uniform1(_shaderLocations.WhiteoutFactor, World.LocalPlayer!.GetPresentation().HudWhiteoutFactor);
                    if (World.LocalPlayer!.GetPresentation().HudWhiteoutFactor != 0)
                    {
                        GL.Uniform1(_shaderLocations.WhiteoutTable, 192, PlayerPresentation.HudWhiteoutTable);
                    }
                }
            }

            if (!_glesEnhancedActive)
            {
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            }
            // Back to the window: everything from here down -- the quad, the
            // helmet, the HUD and the fade -- is drawn at full size through
            // the RTT program, not the one the scene was drawn with, so cel
            // shading is already behind us and there is nothing to turn off.
            GL.Viewport(0, 0, Size.X, Size.Y);
            if (!_glesEnhancedActive)
            {
                GL.Clear(ClearBufferMask.ColorBufferBit);
            }
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            if (!_glesEnhancedActive)
            {
                GL.BindTexture(TextureTarget.Texture2D, _screenTexture);
                GL.DrawFullscreenQuad();
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }

            if (World.LocalPlayer!.GetPresentation().HudDisruptedState != 0 || World.LocalPlayer!.GetPresentation().HudWhiteoutState != -1)
            {
                GL.UseProgram(_glesEnhancedActive
                    ? _glesEnhanced.CompositionProgram : _rttShaderProgramId);
            }
            GL.Uniform4(_shaderLocations.FadeColor, _fadeColor, _fadeColor, _fadeColor, 0);
            if (World.LocalPlayer!.LoadFlags.TestFlag(LoadFlags.Active) && CameraMode == CameraMode.Player)
            {
                DrawHudLayer(Layer4Info); // ice layer
                DrawHudLayer(Layer3Info); // helmet back
                DrawHudLayer(Layer1Info); // visor
                DrawHudLayer(Layer2Info); // helmet front
                DrawHudLayer(Layer5Info); // dialog overlay
                if (Layer1Info.MaskId != -1)
                {
                    GL.ActiveTexture(TextureUnit.Texture1);
                    GL.BindTexture(TextureTarget.Texture2D, Layer1Info.MaskId);
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.Uniform1(_shaderLocations.ViewWidth, (float)Size.X);
                    GL.Uniform1(_shaderLocations.ViewHeight, (float)Size.Y);
                }
                World.LocalPlayer!.GetPresentation().DrawHudObjects();
                GL.Uniform1(_shaderLocations.UseMask, 0);
                if (Layer1Info.MaskId != -1)
                {
                    GL.ActiveTexture(TextureUnit.Texture1);
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    GL.ActiveTexture(TextureUnit.Texture0);
                }
            }
            else if (ScoreboardOverFreeCamera)
            {
                // The scoreboard, and nothing else: none of the helmet and
                // visor layers above belong to a view that is not out of
                // anybody's eyes. PlayerHud decides that; this only lets it
                // be asked, since the HUD is otherwise not drawn at all while
                // the camera is not a player's.
                World.LocalPlayer!.GetPresentation().DrawHudObjects();
            }
            SpectatorCamera.Draw(this);
            AdditionalOverlay?.Invoke(this);
            if (!_isolatedPresentation) Mods.Network.ReplayControls.Draw(this);
            if (World.LocalPlayer!.LoadFlags.TestFlag(LoadFlags.Active) && CameraMode == CameraMode.Player && _fadeType != FadeType.None)
            {
                float percent = _fadePercent;
                if (_fadeIn)
                {
                    percent = 1 - percent;
                }
                if (percent > 0)
                {
                    GL.Uniform4(_shaderLocations.FadeColor, _fadeColor, _fadeColor, _fadeColor, percent);
                    GL.DrawFullscreenQuad();
                }
            }
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Blend);
            if (_faceCulling)
            {
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(TriangleFace.Back);
            }
            if (_glesEnhancedActive)
            {
                // The composition target is guaranteed RGBA8 and is also the
                // only Enhanced scene-capture source. This final pass performs
                // the sole linear-to-sRGB transfer.
                _glesEnhanced.Present(_faceCulling);
            }
            return true;
        }
#endif

        private void LoadAndUnload()
        {
            if (_loadQueue.Count > 0)
            {
                while (_loadQueue.TryDequeue(out (string Name, int Recolor, bool FirstHunt) item))
                {
                    try
                    {
                        // called after load -- entity needs init
                        EntityBase entity = AddModel(item.Name, item.Recolor, item.FirstHunt);
                        entity.Initialize();
                    }
                    catch (ProgramException) { }
                }
            }
            if (_unloadQueue.Count > 0)
            {
                Selection.Clear();
                while (_unloadQueue.TryDequeue(out EntityBase? entity))
                {
                    UnloadEntity(entity);
                }
            }
        }

        private void UnloadEntity(EntityBase entity)
        {
            if (entity.Type == EntityType.Room)
            {
                return;
            }
            entity.Destroy();
            World.RemoveEntity(entity);
            foreach (ModelInstance inst in entity.GetModels())
            {
                Model model = inst.Model;
                if (Metadata.PreloadResources.ContainsKey(model.Name))
                {
                    continue;
                }
                if (!World.IsModelInUse(model))
                {
                    UnloadModel(model);
                }
            }
        }

        public void UnloadModel(Model model)
        {
            ReleaseModelTextureResources(model);
            if (_texPalMap.TryGetValue(model.Id, out TextureMap? map))
            {
#if ANDROID
                foreach (KeyValuePair<int, (int BindingId, bool OnlyOpaque)> kvp in map)
                {
                    GL.DeleteTexture(kvp.Value.BindingId);
                }
#endif
                _texPalMap.Remove(model.Id);
            }
            foreach (Mesh mesh in model.Meshes)
            {
                _portableMeshes.Remove(mesh.GeometryIdentity);
#if ANDROID
                // Model unload runs on the EGL/render thread while its
                // context is current. Release only this model's static
                // objects; Reset handles whole-context loss.
                GL.ReleaseStaticMesh(mesh.GeometryIdentity);
#endif
            }
            Read.RemoveModel(model.Name, model.FirstHunt);
        }

        private void TransformCamera()
        {
            // todo: only update this when the camera values change
            _viewMatrix = Matrix4.Identity;
            _viewInvRotMatrix = Matrix4.Identity;
            _viewInvRotYMatrix = Matrix4.Identity;
            if (_cameraMode == CameraMode.Pivot)
            {
                _viewMatrix.Row3.Xyz = new Vector3(0, 0, _pivotDistance * -1);
                _viewMatrix = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(_pivotAngleX)) * _viewMatrix;
                _viewMatrix = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(_pivotAngleY)) * _viewMatrix;
                _viewInvRotMatrix = _viewInvRotYMatrix = Matrix4.CreateRotationY(MathHelper.DegreesToRadians(-1 * _pivotAngleY));
                _viewInvRotMatrix = Matrix4.CreateRotationX(MathHelper.DegreesToRadians(-1 * _pivotAngleX)) * _viewInvRotMatrix;
            }
            else if (_cameraMode == CameraMode.Roam || _cameraMode == CameraMode.Player)
            {
                if (_cameraMode == CameraMode.Player)
                {
                    _viewMatrix = World.LocalPlayer!.CameraInfo.ViewMatrix;
                    if (Mods.Network.AuthoritativePlay.Current is { } play)
                    {
                        _viewMatrix = Matrix4.CreateTranslation(-play.VisualOffset) * _viewMatrix;
                    }
                    float fov = World.LocalPlayer!.CameraInfo.Fov > 0 ? World.LocalPlayer!.CameraInfo.Fov : 78;
                    _cameraFov = MathHelper.DegreesToRadians(fov);
                }
                else
                {
                    _viewMatrix = Matrix4.LookAt(_cameraPosition, _cameraPosition + _cameraFacing, _cameraUp);
                }
                _viewInvRotMatrix = Matrix4.Transpose(_viewMatrix.ClearTranslation());
                if (_viewInvRotMatrix.Row0.X != 0 || _viewInvRotMatrix.Row0.Z != 0)
                {
                    _viewInvRotYMatrix.Row0.Xyz = new Vector3(_viewInvRotMatrix.Row0.X, 0, _viewInvRotMatrix.Row0.Z).Normalized();
                    _viewInvRotYMatrix.Row2.Xyz = new Vector3(_viewInvRotMatrix.Row2.X, 0, _viewInvRotMatrix.Row2.Z).Normalized();
                }
            }
            ApplyRenderCamera();
            if (SpectatorCamera.TryView(World, out Matrix4 spectatorView)) _viewMatrix = spectatorView;
            _viewInvRotMatrix = Matrix4.Transpose(_viewMatrix.ClearTranslation());
            if (_viewInvRotMatrix.Row0.X != 0 || _viewInvRotMatrix.Row0.Z != 0)
            {
                _viewInvRotYMatrix.Row0.Xyz = new Vector3(_viewInvRotMatrix.Row0.X, 0, _viewInvRotMatrix.Row0.Z).Normalized();
                _viewInvRotYMatrix.Row2.Xyz = new Vector3(_viewInvRotMatrix.Row2.X, 0, _viewInvRotMatrix.Row2.Z).Normalized();
            }
            if (Mods.SpectatorMode.IsSpectating) _cameraFov = MathHelper.DegreesToRadians(SpectatorCamera.FieldOfView);
#if ANDROID
            GL.UniformMatrix4(_shaderLocations.ViewMatrix, transpose: false, ref _viewMatrix);
#endif
        }

        private void UpdateCameraPosition()
        {
            if (_cameraMode == CameraMode.Pivot)
            {
                float angleY = _pivotAngleY + 90;
                if (angleY > 360)
                {
                    angleY -= 360;
                }
                float angleX = _pivotAngleX + 90;
                if (angleX > 360)
                {
                    angleX -= 360;
                }
                float theta = MathHelper.DegreesToRadians(angleY);
                float phi = MathHelper.DegreesToRadians(angleX);
                float x = MathF.Round(_pivotDistance * MathF.Cos(theta), 4);
                float y = MathF.Round(_pivotDistance * MathF.Sin(theta) * MathF.Cos(phi), 4) * -1;
                float z = MathF.Round(_pivotDistance * MathF.Sin(theta) * MathF.Sin(phi), 4);
                _cameraPosition = new Vector3(x, y, z);
            }
            else if (_cameraMode == CameraMode.Player)
            {
                _cameraPosition = _viewMatrix.Inverted().Row3.Xyz;
            }
        }

        private void ResetCamera()
        {
            if (_cameraMode == CameraMode.Roam)
            {
                _cameraPosition = Vector3.Zero;
                _cameraFacing = -Vector3.UnitZ;
                _cameraUp = Vector3.UnitY;
                _cameraRight = Vector3.UnitX;
            }
            else if (_cameraMode == CameraMode.Pivot)
            {
                _pivotAngleX = 0;
                _pivotAngleY = 0;
                _pivotDistance = 5.0f;
            }
        }

        private const float _almostHalfPi = MathF.PI / 2 - 0.000001f;

        private void UpdateCameraRotation(float stepH, float stepV)
        {
            float angleH = MathF.Atan2(_cameraFacing.X, -_cameraFacing.Z) + stepH;
            float angleV = MathF.Asin(_cameraFacing.Y) + stepV;
            angleV = Math.Clamp(angleV, -_almostHalfPi, _almostHalfPi);
            _cameraFacing = new Vector3(
                MathF.Cos(angleV) * MathF.Sin(angleH),
                MathF.Sin(angleV),
                -(MathF.Cos(angleV) * MathF.Cos(angleH))
            ).Normalized();
            _cameraRight = Vector3.Cross(_cameraFacing, Vector3.UnitY);
            _cameraUp = Vector3.Cross(_cameraRight, _cameraFacing);
        }

        public void StartCutscene(int id)
        {
            if (_activeCutscene == -1)
            {
                _activeCutscene = id;
                _priorCameraPos = _cameraPosition;
                _priorCameraFacing = _cameraFacing;
                _priorCameraFov = _cameraFov;
            }
        }

        public void EndCutscene(bool resetFade = false)
        {
            if (_activeCutscene != -1)
            {
                _activeCutscene = -1;
                _cameraPosition = _priorCameraPos;
                _cameraFacing = _priorCameraFacing;
                _cameraRight = Vector3.Cross(_cameraFacing, Vector3.UnitY);
                _cameraUp = Vector3.Cross(_cameraRight, _cameraFacing);
                _cameraFov = _priorCameraFov;
            }
            if (resetFade)
            {
                SetFade(FadeType.None, length: 0, overwrite: true);
            }
        }

        // in-game: 64 effects, 96 elements, 200 particles
        private static readonly int _effectEntryMax = 64;
        private static readonly int _effectElementMax = 96;
        private static readonly int _effectParticleMax = 200;
        private static readonly int _singleParticleMax = 200;
        private static readonly int _beamEffectMax = 100;

        private readonly Queue<EffectEntry> _inactiveEffects = new Queue<EffectEntry>(_effectEntryMax);
        private readonly Queue<EffectElementEntry> _inactiveElements = new Queue<EffectElementEntry>(_effectElementMax);
        private readonly List<EffectElementEntry> _activeElements = new List<EffectElementEntry>(_effectElementMax);
        private readonly Queue<EffectParticle> _inactiveParticles = new Queue<EffectParticle>(_effectParticleMax);
        private int _singleParticleCount = 0;
        private readonly List<SingleParticle> _singleParticles = new List<SingleParticle>(_singleParticleMax);
        private readonly Queue<BeamEffectEntity> _inactiveBeamEffects = new Queue<BeamEffectEntity>(_beamEffectMax);
        private readonly List<BeamEffectEntity> _activeBeamEffects = new List<BeamEffectEntity>(_beamEffectMax);

        private void AllocateEffects()
        {
            for (int i = 0; i < _effectEntryMax; i++)
            {
                _inactiveEffects.Enqueue(new EffectEntry());
            }
            for (int i = 0; i < _effectElementMax; i++)
            {
                _inactiveElements.Enqueue(new EffectElementEntry(World.Random));
            }
            for (int i = 0; i < _effectParticleMax; i++)
            {
                _inactiveParticles.Enqueue(new EffectParticle());
            }
            for (int i = 0; i < _singleParticleMax; i++)
            {
                _singleParticles.Add(new SingleParticle());
            }
            for (int i = 0; i < _beamEffectMax; i++)
            {
                _inactiveBeamEffects.Enqueue(new BeamEffectEntity(World));
            }
        }

        public BeamEffectEntity? InitBeamEffect(BeamEffectEntityData data)
        {
            if (_inactiveBeamEffects.Count == 0)
            {
                return null;
            }
            BeamEffectEntity entry = _inactiveBeamEffects.Dequeue();
            entry.Spawn(data);
            return entry;
        }

        public void UnlinkBeamEffect(BeamEffectEntity entry)
        {
            _activeBeamEffects.Remove(entry);
            _inactiveBeamEffects.Enqueue(entry);
        }

        public void AddSingleParticle(SingleType type, Vector3 position, Vector3 color, float alpha, float scale)
        {
            // note: skipping the room size limit check; singles get cleared every frame anyway
            if (_singleParticleCount < _singleParticleMax)
            {
                SingleParticle entry = _singleParticles[_singleParticleCount++];
                entry.Type = type;
                entry.ParticleDefinition = Read.GetSingleParticle(type);
                entry.Position = _submissionInterpolated ? Vector3.TransformPosition(position, _submissionDelta) : position;
                entry.Color = color;
                entry.Alpha = alpha;
                entry.Scale = scale;
                if (RenderBackendSelection.UsesPortableMeshPreparation(RenderBackendSelection.Current))
                {
                    PrepareCpuMeshes(entry.ParticleDefinition.Model, isRoom: false);
                    if (RenderBackendSelection.Current != RenderBackendKind.Sdl
                        && !_texPalMap.ContainsKey(entry.ParticleDefinition.Model.Id))
                    {
                        InitTextures(entry.ParticleDefinition.Model);
                    }
                }
                else if (!_texPalMap.ContainsKey(entry.ParticleDefinition.Model.Id))
                {
                    InitTextures(entry.ParticleDefinition.Model);
                }
            }
        }

        private EffectEntry? InitEffectEntry()
        {
            if (_inactiveEffects.Count == 0)
            {
                return null;
            }
            EffectEntry entry = _inactiveEffects.Dequeue();
            entry.EffectId = 0;
            Debug.Assert(entry.Elements.Count == 0);
            return entry;
        }

        public void UnlinkEffectEntry(EffectEntry entry)
        {
            for (int i = 0; i < entry.Elements.Count; i++)
            {
                EffectElementEntry element = entry.Elements[i];
                UnlinkEffectElement(element);
            }
            entry.Elements.Clear();
            _inactiveEffects.Enqueue(entry);
        }

        public void DetachEffectEntry(EffectEntry entry, bool setExpired)
        {
            for (int i = 0; i < entry.Elements.Count; i++)
            {
                EffectElementEntry element = entry.Elements[i];
                if (element.Flags.TestFlag(EffElemFlags.DestroyOnDetach))
                {
                    UnlinkEffectElement(element);
                }
                else
                {
                    element.Flags &= ~EffElemFlags.ElementExtension;
                    element.Flags |= EffElemFlags.KeepAlive; // keep alive until particles expire
                    element.EffectEntry = null;
                    if (setExpired)
                    {
                        element.Expired = true;
                    }
                }
            }
            entry.Elements.Clear();
            UnlinkEffectEntry(entry);
        }

        private EffectElementEntry? InitEffectElement(Effect effect, EffectElement element,
            int elementIndex, EntityCollision? entCol, bool child)
        {
            if (_inactiveElements.Count == 0)
            {
                return null;
            }
            EffectElementEntry entry = _inactiveElements.Dequeue();
            entry.EffectId = effect.Id;
            entry.EffectName = effect.Name;
            entry.ElementName = element.Name;
            SetSoftParticleProfile(entry, effect.Id, elementIndex);
            entry.BufferTime = element.BufferTime;
            // todo: FPS stuff
            entry.CreationTime = World.ElapsedTime + (child ? (1 / 60f) : 0);
            entry.DrainTime = element.DrainTime;
            entry.DrawType = element.DrawType;
            entry.Lifespan = element.Lifespan;
            entry.ExpirationTime = entry.CreationTime + entry.Lifespan;
            entry.Flags = element.Flags;
            entry.Flags |= EffElemFlags.DrawEnabled;
            entry.Func39Called = false;
            entry.Funcs = element.Funcs;
            entry.Actions = element.Actions;
            entry.OwnTransform = Matrix4.Identity;
            entry.Transform = Matrix4.Identity;
            entry.ParticleAmount = 0;
            entry.Expired = false;
            entry.ChildEffectId = (int)element.ChildEffectId;
            entry.Acceleration = element.Acceleration;
            entry.ParticleDefinitions.AddRange(element.Particles);
            entry.Parity = (int)(_effectFrame % 2);
            entry.EffectEntry = null;
            entry.EntityCollision = entCol;
            entry.Definition = element;
            entry.RoField1 = 0;
            entry.RoField2 = 0;
            entry.RoField3 = 0;
            entry.RoField4 = 0;
            _activeElements.Add(entry);
            return entry;
        }

        private void UnlinkEffectElement(EffectElementEntry element)
        {
            while (element.Particles.Count > 0)
            {
                EffectParticle particle = element.Particles[0];
                element.Particles.Remove(particle);
                UnlinkEffectParticle(particle);
            }
            _activeElements.Remove(element);
            element.EntityCollision = null;
            element.Definition = null;
            element.Model = null!;
            element.Nodes.Clear();
            element.EffectName = "";
            element.ElementName = "";
            element.ParticleDefinitions.Clear();
            GetEffectTextureBindings(element).Clear();
            ClearSoftParticleProfile(element);
            Debug.Assert(element.Particles.Count == 0);
            _inactiveElements.Enqueue(element);
        }

        private EffectParticle? InitEffectParticle()
        {
            if (_inactiveParticles.Count == 0)
            {
                return null;
            }
            EffectParticle particle = _inactiveParticles.Dequeue();
            particle.Position = Vector3.Zero;
            particle.Speed = Vector3.Zero;
            particle.ParticleId = 0;
            particle.RoField1 = 0;
            particle.RoField2 = 0;
            particle.RoField3 = 0;
            particle.RoField4 = 0;
            particle.RwField1 = 0;
            particle.RwField2 = 0;
            particle.RwField3 = 0;
            particle.RwField4 = 0;
            _particlePoses.Remove(particle); // pooled identity starts a fresh render history
            particle.CreationTime = World.ElapsedTime;
            return particle;
        }

        private void UnlinkEffectParticle(EffectParticle particle)
        {
            _inactiveParticles.Enqueue(particle);
        }

        private readonly Dictionary<int, Effect> _loadedEffects = new();
        public void LoadEffect(int effectId, bool persistent)
        {
            if (_loadedEffects.TryGetValue(effectId, out Effect? existing))
            {
                if (persistent && !existing.Persistent)
                {
                    existing.Persistent = true;
                    foreach (EffectElement child in existing.Elements)
                        if (child.ChildEffectId != 0) LoadEffect((int)child.ChildEffectId, persistent: true);
                }
                return;
            }
            Effect effect = Read.LoadEffect(effectId, persistent);
            _loadedEffects.Add(effectId, effect);
            foreach (EffectElement element in effect.Elements)
            {
                if (element.ChildEffectId != 0) LoadEffect((int)element.ChildEffectId, persistent);
                // the model may already be loaded; meshes with a ListId will be skipped
                Model model = Read.GetModelInstance(element.ModelName).Model;
                InitTextures(model);
                PrepareCpuMeshes(model, isRoom: false);
            }
        }

        public EffectEntry? SpawnEffectGetEntry(int effectId, Vector3 facing, Vector3 up, Vector3 position, EntityCollision? entCol = null)
        {
            Matrix4 transform = EntityBase.GetTransformMatrix(facing, up, position);
            return SpawnEffectGetEntry(effectId, transform, entCol);
        }

        public EffectEntry? SpawnEffectGetEntry(int effectId, Matrix4 transform, EntityCollision? entCol = null)
        {
            EffectEntry? entry = InitEffectEntry();
            if (entry == null)
            {
                return null;
            }
            entry.EffectId = effectId;
            SpawnEffect(effectId, transform, child: false, entry, entCol);
            return entry;
        }

        public void SpawnEffect(int effectId, Vector3 facing, Vector3 up, Vector3 position, bool child = false, EntityCollision? entCol = null)
        {
            Matrix4 transform = EntityBase.GetTransformMatrix(facing, up, position);
            SpawnEffect(effectId, transform, child, entry: null, entCol);
        }

        public void SpawnEffect(int effectId, Matrix4 transform, bool child = false, EntityCollision? entCol = null)
        {
            SpawnEffect(effectId, transform, child, entry: null, entCol);
        }

        private void SpawnEffect(int effectId, Matrix4 transform, bool child, EffectEntry? entry, EntityCollision? entCol)
        {
            Effect? effect = _loadedEffects.GetValueOrDefault(effectId);
            if (effect == null)
            {
                Debug.Assert(effectId == 162); // skdebug - unintended Omega Cannon damage effect
                return;
            }
            for (int i = 0; i < effect.Elements.Count; i++)
            {
                EffectElement elementDef = effect.Elements[i];
                EffectElementEntry? element = InitEffectElement(effect, elementDef, i, entCol, child);
                if (element == null)
                {
                    return;
                }
                if (entry != null)
                {
                    element.EffectEntry = entry;
                    entry.Elements.Add(element);
                }
                if (element.Flags.TestFlag(EffElemFlags.SpawnUnitVecs))
                {
                    Vector3 vec1 = Vector3.UnitY;
                    Vector3 vec2 = Vector3.UnitX;
                    transform = Matrix.GetTransform4(vec2, vec1, transform.Row3.Xyz);
                }
                element.Transform = element.OwnTransform = transform;
                for (int j = 0; j < elementDef.Particles.Count; j++)
                {
                    Particle particleDef = elementDef.Particles[j];
                    if (j == 0)
                    {
                        if (!_texPalMap.ContainsKey(particleDef.Model.Id))
                        {
                            InitTextures(particleDef.Model);
                        }
                        element.Model = particleDef.Model;
                    }
                    element.Nodes.Add(particleDef.Node);
                    Material material = particleDef.Model.Materials[particleDef.MaterialId];
                    SetTextureBindingId(material, _texPalMap[particleDef.Model.Id].Get(material.TextureId, material.PaletteId, 0).BindingId);
                    GetEffectTextureBindings(element).Add(GetTextureBindingId(material));
                }
            }
        }

        public int CountElements(int effectId)
        {
            Effect? effect = _loadedEffects.GetValueOrDefault(effectId);
            if (effect == null)
            {
                return 0;
            }
            int count = 0;
            for (int i = 0; i < effect.Elements.Count; i++)
            {
                EffectElement element = effect.Elements[i];
                for (int j = 0; j < _activeElements.Count; j++)
                {
                    if (_activeElements[j].Definition == element)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        public void ClearEffects()
        {
            for (int i = 0; i < _activeElements.Count; i++)
            {
                EffectElementEntry element = _activeElements[i];
                UnlinkEffectElement(element);
                i--;
            }
        }

        public void ClearNonPersistentEffects()
        {
            for (int i = 0; i < _activeElements.Count; i++)
            {
                EffectElementEntry element = _activeElements[i];
                Effect? effect = _loadedEffects.GetValueOrDefault(element.EffectId);
                if (effect == null || !effect.Persistent)
                {
                    UnlinkEffectElement(element);
                    i--;
                }
            }
        }

        private void ProcessEffects(ulong effectFrame)
        {
            for (int i = 0; i < _activeElements.Count; i++)
            {
                EffectElementEntry element = _activeElements[i];
                if (!element.Expired && World.ElapsedTime > element.ExpirationTime)
                {
                    if (element.EffectEntry == null && !element.Flags.TestFlag(EffElemFlags.KeepAlive))
                    {
                        UnlinkEffectElement(element);
                        i--;
                        continue;
                    }
                    element.Expired = true;
                }
                if (element.Expired)
                {
                    // if EffectEntry is non-null, keep the element alive indefinitely;
                    // else (if bit 4 of Flags is set), keep the element alive until its particles have all expired
                    if (element.EffectEntry == null && element.Particles.Count == 0)
                    {
                        UnlinkEffectElement(element);
                        i--;
                        continue;
                    }
                    element.Transform = element.OwnTransform;
                }
                else
                {
                    if (element.Flags.TestFlag(EffElemFlags.ElementExtension))
                    {
                        if (World.ElapsedTime - element.CreationTime > element.BufferTime)
                        {
                            element.CreationTime += element.BufferTime - element.DrainTime;
                            element.ExpirationTime += element.BufferTime - element.DrainTime;
                        }
                    }
                    if (element.EntityCollision != null)
                    {
                        element.Transform = element.OwnTransform * element.EntityCollision.Transform;
                    }
                    else
                    {
                        element.Transform = element.OwnTransform;
                    }
                    var times = new TimeValues(World.ElapsedTime, World.ElapsedTime - element.CreationTime, element.Lifespan);
                    if (effectFrame % 2 == (ulong)element.Parity
                        && element.Actions.TryGetValue(FuncAction.IncreaseParticleAmount, out FxFuncInfo? info))
                    {
                        // todo: maybe revisit this frame time hack
                        // --> halving the amount doesn't work because it breaks one-time return values of 1.0
                        float amount = element.InvokeFloatFunc(info, times);
                        element.ParticleAmount += amount;
                    }
                    int spawnCount = (int)MathF.Floor(element.ParticleAmount);
                    element.ParticleAmount -= spawnCount;
                    float portionTotal = 0;
                    for (int j = 0; j < spawnCount; j++)
                    {
                        Vector3 temp = Vector3.Zero;
                        EffectParticle? particle = InitEffectParticle();
                        if (particle == null)
                        {
                            break;
                        }
                        element.Particles.Add(particle);
                        ModEffectParticles++;
                        particle.Owner = element;
                        particle.SetFuncIds();
                        particle.PortionTotal = portionTotal;
                        particle.MaterialId = element.ParticleDefinitions[0].MaterialId;
                        if (element.Actions.TryGetValue(FuncAction.SetNewParticlePosition, out info))
                        {
                            particle.InvokeVecFunc(info, times, ref temp);
                            particle.Position = temp;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetNewParticleSpeed, out info))
                        {
                            particle.InvokeVecFunc(info, times, ref temp);
                            particle.Speed = temp;
                        }
                        if (!element.Flags.TestFlag(EffElemFlags.UseTransform))
                        {
                            particle.Position = Matrix.Vec3MultMtx4(particle.Position, element.Transform);
                            particle.Speed = Matrix.Vec3MultMtx3(particle.Speed, element.Transform);
                        }
                        // todo: these should really just be FxFuncInfo properties instead of a dictionary
                        // --> still need the dictionary for the offset lookups, though
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRoField1, out info))
                        {
                            particle.RoField1 = particle.InvokeFloatFunc(info, times);
                        }
                        else
                        {
                            particle.RoField1 = element.RoField1;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRoField2, out info))
                        {
                            particle.RoField2 = particle.InvokeFloatFunc(info, times);
                        }
                        else
                        {
                            particle.RoField2 = element.RoField2;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRoField3, out info))
                        {
                            particle.RoField3 = particle.InvokeFloatFunc(info, times);
                        }
                        else
                        {
                            particle.RoField3 = element.RoField3;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRoField4, out info))
                        {
                            particle.RoField4 = particle.InvokeFloatFunc(info, times);
                        }
                        else
                        {
                            particle.RoField4 = element.RoField4;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetNewParticleLifespan, out info))
                        {
                            var tempTimes = new TimeValues(World.ElapsedTime, 1.0f, element.Lifespan);
                            particle.Lifespan = particle.InvokeFloatFunc(info, tempTimes);
                            particle.ExpirationTime = particle.CreationTime + particle.Lifespan;
                        }
                        else
                        {
                            particle.Lifespan = element.Lifespan;
                            particle.ExpirationTime = element.ExpirationTime;
                        }
                        // in-game, if these one-time functions are called here, they are unset so they don't get called again
                        if (element.Actions.TryGetValue(FuncAction.UpdateParticleSpeed, out info) && info.FuncId == 4)
                        {
                            Vector3 temp2 = particle.Speed;
                            particle.InvokeVecFunc(info, times, ref temp2);
                            particle.Speed = temp2;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRed, out info) && info.FuncId == 42)
                        {
                            particle.Red = particle.InvokeFloatFunc(info, times);
                        }
                        else
                        {
                            particle.Red = 1;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleGreen, out info) && info.FuncId == 42)
                        {
                            particle.Green = particle.InvokeFloatFunc(info, times);
                        }
                        else
                        {
                            particle.Green = 1;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleBlue, out info) && info.FuncId == 42)
                        {
                            particle.Blue = particle.InvokeFloatFunc(info, times);
                        }
                        else
                        {
                            particle.Blue = 1;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleAlpha, out info) && info.FuncId == 42)
                        {
                            particle.Alpha = particle.InvokeFloatFunc(info, times);
                            if (particle.Alpha < 0)
                            {
                                particle.Alpha = 0;
                            }
                        }
                        else
                        {
                            particle.Alpha = 1;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleScale, out info) && info.FuncId == 42)
                        {
                            particle.Scale = particle.InvokeFloatFunc(info, times);
                        }
                        else
                        {
                            particle.Scale = 0;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRotation, out info) && info.FuncId == 42)
                        {
                            particle.Rotation = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRwField1, out info))
                        {
                            particle.RwField1 = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRwField2, out info))
                        {
                            particle.RwField2 = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRwField3, out info))
                        {
                            particle.RwField3 = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRwField4, out info))
                        {
                            particle.RwField4 = particle.InvokeFloatFunc(info, times);
                        }
                        portionTotal += 1f / spawnCount;
                    }
                }
                for (int j = 0; j < element.Particles.Count; j++)
                {
                    EffectParticle particle = element.Particles[j];
                    if (element.Flags.TestFlag(EffElemFlags.ElementExtension) && element.Flags.TestFlag(EffElemFlags.ParticleExtension))
                    {
                        if (World.ElapsedTime - particle.CreationTime > element.BufferTime)
                        {
                            particle.CreationTime += element.BufferTime - element.DrainTime;
                            particle.ExpirationTime += element.BufferTime - element.DrainTime;
                        }
                    }
                    if (World.ElapsedTime < particle.ExpirationTime)
                    {
                        var times = new TimeValues(World.ElapsedTime, World.ElapsedTime - particle.CreationTime, particle.Lifespan);
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRwField1, out FxFuncInfo? info))
                        {
                            particle.RwField1 = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRwField2, out info))
                        {
                            particle.RwField2 = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRwField3, out info))
                        {
                            particle.RwField3 = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRwField4, out info))
                        {
                            particle.RwField4 = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleId, out info))
                        {
                            particle.ParticleId = (int)particle.InvokeFloatFunc(info, times);
                            if (particle.ParticleId >= element.ParticleDefinitions.Count)
                            {
                                particle.ParticleId = element.ParticleDefinitions.Count - 1;
                            }
                            particle.MaterialId = element.ParticleDefinitions[particle.ParticleId].MaterialId;
                        }
                        if (element.Actions.TryGetValue(FuncAction.UpdateParticleSpeed, out info))
                        {
                            Vector3 temp = particle.Speed;
                            particle.InvokeVecFunc(info, times, ref temp);
                            particle.Speed = temp;
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRed, out info))
                        {
                            particle.Red = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleGreen, out info))
                        {
                            particle.Green = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleBlue, out info))
                        {
                            particle.Blue = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleAlpha, out info))
                        {
                            particle.Alpha = particle.InvokeFloatFunc(info, times);
                            if (particle.Alpha < 0)
                            {
                                particle.Alpha = 0;
                            }
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleScale, out info))
                        {
                            particle.Scale = particle.InvokeFloatFunc(info, times);
                        }
                        if (element.Actions.TryGetValue(FuncAction.SetParticleRotation, out info))
                        {
                            particle.Rotation = particle.InvokeFloatFunc(info, times);
                        }
                        // todo: frame time scaling for speed/accel
                        if (element.Flags.TestFlag(EffElemFlags.UseAcceleration))
                        {
                            particle.Speed = new Vector3(
                                particle.Speed.X + element.Acceleration.X * (1 / 60f),
                                particle.Speed.Y + element.Acceleration.Y * (1 / 60f),
                                particle.Speed.Z + element.Acceleration.Z * (1 / 60f)
                            );
                        }
                        Vector3 prevPos = particle.Position;
                        particle.Position = new Vector3(
                            particle.Position.X + particle.Speed.X * (1 / 60f),
                            particle.Position.Y + particle.Speed.Y * (1 / 60f),
                            particle.Position.Z + particle.Speed.Z * (1 / 60f)
                        );
                        if (element.Flags.TestFlag(EffElemFlags.CheckCollision))
                        {
                            CollisionResult res = default;
                            if (CollisionDetection.CheckBetweenPoints(prevPos, particle.Position, TestFlags.None, World, ref res))
                            {
                                particle.Position = res.Position;
                                particle.ExpirationTime = World.ElapsedTime;
                            }
                        }
                    }
                    else
                    {
                        if (element.Flags.TestFlag(EffElemFlags.SpawnChildEffect) && element.ChildEffectId != 0)
                        {
                            Vector3 vec1 = (-particle.Speed).Normalized();
                            Vector3 vec2;
                            if (vec1.Z <= Fixed.ToFloat(-3686) || vec1.Z >= Fixed.ToFloat(3686))
                            {
                                vec2 = Vector3.UnitX;
                            }
                            else
                            {
                                vec2 = Vector3.UnitZ;
                            }
                            vec2 = Vector3.Cross(vec1, vec2).Normalized();
                            Matrix4 transform = Matrix.GetTransform4(vec2, vec1, particle.Position);
                            SpawnEffect(element.ChildEffectId, transform);
                        }
                        element.Particles.Remove(particle);
                        UnlinkEffectParticle(particle);
                        j--;
                    }
                }
            }
        }

        private const int _renderItemAlloc = RenderFrame.DefaultCapacity;
        private readonly RenderFrame _renderFrame = new RenderFrame(_renderItemAlloc);
        private readonly VisualLightIdentityAllocator _visualLightIdentities = new();
        // avoiding overhead by duplicating things in these lists
        /// <summary>
        /// Simulation steps taken since the last frame was drawn. Effects are
        /// advanced once per step inside <see cref="GetDrawItems"/>, which is
        /// where they have always been advanced, so their ordering against the
        /// entity draw pass is unchanged; this is only how many times.
        /// </summary>
        private int _pendingEffectSteps;

        /// <summary>
        /// Which simulation step the effect system is on.
        ///
        /// Effects spawn particles on every *other* step -- the DS ran them at
        /// 30 Hz and upstream's doubling to 60 is this parity check -- and an
        /// element records the parity it was created with so that its first
        /// advance is a spawning one. Many elements put their whole burst out
        /// on that first advance, through a function that only returns its
        /// value once; miss it and they emit nothing at all, ever.
        ///
        /// That is what happened when the step and the picture became two
        /// calls. Both halves used to read <c>World.FrameCount</c>, which
        /// upstream incremented *after* <c>GetDrawItems</c> -- so a spawn and
        /// the advance that followed it in the same frame saw the same number.
        /// Moving the increment into the simulation step put it before the
        /// draw, every element's first advance failed its own parity check,
        /// and the bursts stopped: no flash on a charging Missile, no
        /// explosion on a wall. The continuous elements of the same effects --
        /// smoke, debris -- carried on, which is why it read as "some of it is
        /// missing" rather than as no effects at all.
        ///
        /// So the effect system counts its own steps here, where nothing else
        /// can move the number, and the relationship upstream relied on is
        /// restored exactly.
        /// </summary>
        private ulong _effectFrame;

        /// <summary>
        /// Every particle any effect element has spawned this run.
        ///
        /// The harness asserts on it. A run that fires thousands of shots and
        /// spawns no effect particles is the failure above, and nothing else
        /// the audit measures moves at all when it happens -- the shots still
        /// fly, still hit, still do damage, and the pictures still come out
        /// lit. It is invisible to every other check by construction, so it
        /// gets one of its own.
        /// </summary>
        public long ModEffectParticles { get; private set; }

        /// <summary>
        /// Simulation steps owed to <see cref="UpdateFade"/>, which runs in
        /// the draw pass because the fade is drawn there, but whose delay is a
        /// simulation timer counted in frames. Left as one per drawn frame it
        /// would expire two and a half times too early on a 144 Hz screen, and
        /// what waits on it is the end of a fade -- a cutscene finishing, a
        /// room changing.
        /// </summary>
        private int _pendingFadeSteps;

        private readonly List<DrawSubmission> _decalItems = new List<DrawSubmission>();
        private readonly List<DrawSubmission> _nonDecalItems = new List<DrawSubmission>();
        private readonly List<DrawSubmission> _translucentItems = new List<DrawSubmission>();

        // Compatibility handles belong to the legacy backend, not to the
        // portable submission contract. Entries live exactly as long as the
        // frame and are looked up by submission when the GL adapter draws it.
        private readonly Dictionary<DrawSubmission, LegacySubmissionResources> _legacySubmissionResources
            = new Dictionary<DrawSubmission, LegacySubmissionResources>();
#if ANDROID
        private readonly GlesWorldContext _glesWorldContext = new GlesWorldContext();
#endif

        private readonly struct LegacySubmissionResources
        {
            public int ListId { get; }
            public int TextureId { get; }

            public LegacySubmissionResources(int listId, int textureId)
            {
                ListId = listId;
                TextureId = textureId;
            }
        }

        private DrawSubmission GetRenderItem() => _renderFrame.Acquire();

        private readonly float[] _scaleFactors = new float[16];

        // for meshes
        public void AddRenderItem(Material material, int polygonId, float alphaScale, Vector3 emission, LightInfo lightInfo, Matrix4 texcoordMatrix,
            Matrix4 transform, int listId, object geometryIdentity, int matrixStackCount, IReadOnlyList<float> matrixStack, Vector4? overrideColor, Vector4? paletteOverride,
            SelectionType selectionType, BillboardMode billboardMode, float scaleFactor = 1, int? bindingOverride = null,
            TextureIdentity? textureIdentity = null, TextureAssetKey? textureAssetKey = null,
            EnhancedForceFieldDrawState? enhancedForceField = null)
        {
            transform.Row0.X *= scaleFactor;
            transform.Row0.Y *= scaleFactor;
            transform.Row0.Z *= scaleFactor;
            transform.Row1.X *= scaleFactor;
            transform.Row1.Y *= scaleFactor;
            transform.Row1.Z *= scaleFactor;
            transform.Row2.X *= scaleFactor;
            transform.Row2.Y *= scaleFactor;
            transform.Row2.Z *= scaleFactor;
            _scaleFactors[0] = scaleFactor;
            _scaleFactors[1] = scaleFactor;
            _scaleFactors[2] = scaleFactor;
            _scaleFactors[3] = 1;
            _scaleFactors[4] = scaleFactor;
            _scaleFactors[5] = scaleFactor;
            _scaleFactors[6] = scaleFactor;
            _scaleFactors[7] = 1;
            _scaleFactors[8] = scaleFactor;
            _scaleFactors[9] = scaleFactor;
            _scaleFactors[10] = scaleFactor;
            _scaleFactors[11] = 1;
            _scaleFactors[12] = 1;
            _scaleFactors[13] = 1;
            _scaleFactors[14] = 1;
            _scaleFactors[15] = 1;
            DrawSubmission item = GetRenderItem();
            item.Primitive = RenderPrimitive.Mesh;
            item.PolygonId = polygonId;
            item.Alpha = material.CurrentAlpha * alphaScale;
            item.PolygonMode = material.PolygonMode;
            item.RenderMode = material.RenderMode;
            item.CullingMode = material.Culling;
            item.BillboardMode = billboardMode;
            item.Wireframe = material.Wireframe != 0;
            item.Lighting = material.Lighting != 0;
            item.NoLines = false;
            item.Diffuse = material.CurrentDiffuse;
            item.Ambient = material.CurrentAmbient;
            item.Specular = material.CurrentSpecular;
            item.Emission = emission;
            item.LightInfo = lightInfo;
            if (bindingOverride.HasValue)
            {
                // double damage
                item.TexgenMode = TexgenMode.Normal;
                item.XRepeat = RepeatMode.Mirror;
                item.YRepeat = RepeatMode.Mirror;
                item.HasTexture = true;
                SetLegacyTexture(item, bindingOverride.Value);
            }
            else
            {
                item.TexgenMode = material.TexgenMode;
                item.XRepeat = material.XRepeat;
                item.YRepeat = material.YRepeat;
                item.HasTexture = material.TextureId != -1;
                SetLegacyTexture(item, GetTextureBindingId(material));
            }
            item.TexcoordMatrix = texcoordMatrix;
            item.Transform = SubmissionTransform(transform);
            item.GeometryIdentity = geometryIdentity;
            item.TextureIdentity = item.HasTexture ? textureIdentity : null;
            item.TextureAssetKey = item.HasTexture && !bindingOverride.HasValue
                ? textureAssetKey : null;
            item.EnhancedForceField = enhancedForceField;
            SetLegacyList(item, listId);
            Debug.Assert(matrixStack.Count == 16 * matrixStackCount);
            item.MatrixStackCount = matrixStackCount;
            for (int i = 0; i < matrixStack.Count; i++)
            {
                float value = matrixStack[i];
                item.MatrixStack[i] = value * _scaleFactors[i - (i / 16) * 16];
            }
            if (_submissionInterpolated) TransformCopiedStack(item.MatrixStack, matrixStackCount, _submissionDelta);
            item.OverrideColor = overrideColor;
            item.PaletteOverride = paletteOverride;
            item.Points = Array.Empty<Vector3>();
            item.ScaleS = 1;
            item.ScaleT = 1;
            if (selectionType != SelectionType.None)
            {
                overrideColor = Selection.GetSelectionColor(selectionType);
                if (overrideColor != null)
                {
                    item.OverrideColor = overrideColor;
                    item.PaletteOverride = null;
                }
            }
            if (item.TextureIdentity is TextureIdentity identity)
            {
                item.TextureIdentity = identity.WithPaletteOverride(item.PaletteOverride);
            }
            AddRenderItem(item);
        }

        // for volumes/planes
        public void AddRenderItem(CullingMode cullingMode, int polygonId, Vector4 overrideColor, RenderPrimitive type,
            Vector3[] vertices, int vertexCount = 0, bool noLines = false,
            float bloomStrength = 0)
        {
            DrawSubmission item = GetRenderItem();
            item.Primitive = type;
            item.PolygonId = polygonId;
            item.Alpha = 1;
            item.PolygonMode = PolygonMode.Modulate;
            item.RenderMode = RenderMode.Translucent;
            item.CullingMode = cullingMode;
            item.BillboardMode = BillboardMode.None;
            item.Wireframe = false;
            item.Lighting = false;
            item.NoLines = noLines;
            item.Diffuse = Vector3.Zero;
            item.Ambient = Vector3.Zero;
            item.Specular = Vector3.Zero;
            item.Emission = Vector3.Zero;
            item.BloomStrength = float.IsFinite(bloomStrength)
                ? Math.Clamp(bloomStrength, 0, 1) : 0;
            item.BloomEligible = item.BloomStrength > 0;
            item.LightInfo = LightInfo.Zero;
            item.TexgenMode = TexgenMode.None;
            item.XRepeat = RepeatMode.Clamp;
            item.YRepeat = RepeatMode.Clamp;
            item.HasTexture = false;
            item.TexcoordMatrix = Matrix4.Identity;
            item.Transform = Matrix4.Identity;
            item.MatrixStackCount = 0;
            item.OverrideColor = overrideColor;
            item.PaletteOverride = null;
            item.Points = vertices;
            item.ScaleS = 1;
            item.ScaleT = 1;
            if (type == RenderPrimitive.Ngon)
            {
                item.EdgeColor = GetNgonEdgeColor(_showCollision, ColDisplayColor, ColDisplayAlpha);
            }
            Debug.Assert(type != RenderPrimitive.Ngon || vertexCount >= 3);
            item.ItemCount = vertexCount;
            AddRenderItem(item);
        }

        internal static Vector4 GetNgonEdgeColor(bool showCollision, CollisionColor displayColor,
            float displayAlpha)
        {
            return showCollision && displayColor == CollisionColor.None && displayAlpha == 1
                ? new Vector4(0f, 0f, 1f, 1f)
                : new Vector4(1f, 0f, 0f, 1f);
        }

        // for effects/trails
        public void AddRenderItem(RenderPrimitive type, float alpha, int polygonId, Vector3 color,
            RepeatMode xRepeat, RepeatMode yRepeat, float scaleS, float scaleT, Matrix4 transform, Vector3[] uvsAndVerts,
            TextureIdentity? textureIdentity, int bindingId,
            BillboardMode billboardMode = BillboardMode.None, int trailCount = 8,
            float bloomStrength = 0, SoftParticleProfile? softParticleProfile = null,
            EnhancedBeamDrawState? enhancedBeam = null, TextureAssetKey? textureAssetKey = null)
        {
            DrawSubmission item = GetRenderItem();
            item.Primitive = type;
            item.PolygonId = polygonId;
            item.Alpha = alpha;
            item.PolygonMode = PolygonMode.Modulate;
            item.RenderMode = RenderMode.Translucent;
            item.CullingMode = CullingMode.Neither;
            item.BillboardMode = billboardMode;
            item.Wireframe = false;
            item.Lighting = false;
            item.NoLines = false;
            item.Diffuse = color;
            item.Ambient = Vector3.Zero;
            item.Specular = Vector3.Zero;
            item.Emission = Vector3.Zero;
            item.BloomStrength = float.IsFinite(bloomStrength)
                ? Math.Clamp(bloomStrength, 0, 1) : 0;
            item.BloomEligible = item.BloomStrength > 0;
            item.SoftParticleProfile = softParticleProfile;
            item.EnhancedBeam = enhancedBeam;
            item.TextureAssetKey = textureAssetKey;
            item.LightInfo = LightInfo.Zero;
            item.TexgenMode = TexgenMode.None;
            item.XRepeat = xRepeat;
            item.YRepeat = yRepeat;
            item.HasTexture = true;
            item.TextureIdentity = textureIdentity;
            SetLegacyTexture(item, bindingId);
            item.TexcoordMatrix = Matrix4.Identity;
            item.Transform = SubmissionTransform(transform);
            item.MatrixStackCount = 0;
            item.OverrideColor = null;
            item.PaletteOverride = null;
            item.Points = uvsAndVerts;
            item.ScaleS = scaleS;
            item.ScaleT = scaleT;
            item.ItemCount = trailCount;
            AddRenderItem(item);
        }

        // for Morph Ball trails
        public void AddRenderItem(RenderPrimitive type, int polygonId, Vector3 color, RepeatMode xRepeat, RepeatMode yRepeat, float scaleS,
            float scaleT, int matrixStackCount, IReadOnlyList<float> matrixStack, Vector3[] uvsAndVerts, int segmentCount,
            TextureIdentity? textureIdentity, int bindingId)
        {
            DrawSubmission item = GetRenderItem();
            item.Primitive = type;
            item.PolygonId = polygonId;
            item.Alpha = 1;
            item.PolygonMode = PolygonMode.Modulate;
            item.RenderMode = RenderMode.Translucent;
            item.CullingMode = CullingMode.Neither;
            item.BillboardMode = BillboardMode.None;
            item.Wireframe = false;
            item.Lighting = false;
            item.NoLines = false;
            item.Diffuse = color;
            item.Ambient = Vector3.Zero;
            item.Specular = Vector3.Zero;
            item.Emission = Vector3.Zero;
            item.BloomStrength = 0;
            item.BloomEligible = false;
            item.LightInfo = LightInfo.Zero;
            item.TexgenMode = TexgenMode.None;
            item.XRepeat = xRepeat;
            item.YRepeat = yRepeat;
            item.HasTexture = true;
            item.TextureIdentity = textureIdentity;
            SetLegacyTexture(item, bindingId);
            item.TexcoordMatrix = Matrix4.Identity;
            item.Transform = Matrix4.Identity;
            Debug.Assert(matrixStack.Count >= 16 * matrixStackCount);
            item.MatrixStackCount = matrixStackCount;
            for (int i = 0; i < 16 * matrixStackCount; i++)
            {
                item.MatrixStack[i] = matrixStack[i];
            }
            item.OverrideColor = null;
            item.PaletteOverride = null;
            item.Points = uvsAndVerts;
            item.ScaleS = scaleS;
            item.ScaleT = scaleT;
            item.ItemCount = segmentCount;
            AddRenderItem(item);
        }

        private void SetLegacyList(DrawSubmission item, int listId)
        {
            _legacySubmissionResources[item] = new LegacySubmissionResources(listId,
                _legacySubmissionResources.TryGetValue(item, out LegacySubmissionResources existing)
                    ? existing.TextureId : 0);
        }

        private void SetLegacyTexture(DrawSubmission item, int textureId)
        {
            _legacySubmissionResources[item] = new LegacySubmissionResources(
                _legacySubmissionResources.TryGetValue(item, out LegacySubmissionResources existing)
                    ? existing.ListId : 0, textureId);
        }

        private int GetLegacyList(DrawSubmission item)
            => _legacySubmissionResources.TryGetValue(item, out LegacySubmissionResources resources)
                ? resources.ListId : 0;

        private int GetLegacyTexture(DrawSubmission item)
            => _legacySubmissionResources.TryGetValue(item, out LegacySubmissionResources resources)
                ? resources.TextureId : 0;

        private void AddRenderItem(DrawSubmission item)
        {
            ResolveEnhancedMaterial(item);
            _renderFrame.Add(item);
        }

        /// <summary>
        /// Record a bounded render-only light while the current frame is being
        /// prepared.  The option is checked here, at the presentation
        /// boundary, so gameplay entities never need to know whether the
        /// selected backend supports the enhancement.
        /// </summary>
        public bool TryAddVisualLight(RenderVisualLight light)
        {
            if (!Mods.RenderOptions.DynamicVisualLights)
            {
                return false;
            }
            return _renderFrame.AddVisualLight(light);
        }

        public bool TryAddVisualLight(Vector3 position, Vector3 color, float radius,
            float intensity, int priority)
            => TryAddVisualLight(new RenderVisualLight(position, color, radius, intensity, priority));

        public bool TryAddVisualLight(ulong stableSourceKey, Vector3 position,
            VisualLightProfile profile)
            => _renderFrame.AddVisualLightCandidate(
                new VisualLightCandidate(stableSourceKey, position, profile));

        internal ulong GetVisualLightSourceKey(VisualLightSourceKind kind,
            object source, uint generation = 0)
            => _visualLightIdentities.GetSourceKey(kind, source, generation);

        internal TimeSpan CapturedPresentationTime { get; private set; }

        private int _nextPolygonId = 1;

        public int GetNextPolygonId()
        {
            return _nextPolygonId++;
        }

        private void GetDrawItems()
        {
            if (World.Room != null)
            {
                EntityPresentation.Get(World.Room, this).GetDrawInfo();
                EntityPresentation.Get(World.Room, this).GetDisplayVolumes();
            }
            foreach (PlayerEntity player in World.GetPlayerEntities())
            {
                if (!player.Initialized)
                {
                    continue;
                }
                if (player.LoadFlags.TestFlag(LoadFlags.Active))
                {
                    BeginEntitySubmission(player);
                    try { player.GetPresentation().Draw(); }
                    finally { EndEntitySubmission(); }
                    // skdebug
                    EntityPresentation.Get(player, this).GetDisplayVolumes();
                }
            }
            foreach (EntityBase entity in World.Entities)
            {
                if (!entity.Initialized || entity.Type == EntityType.Player || entity.Type == EntityType.Room)
                {
                    continue;
                }
                if (entity.ShouldDraw)
                {
                    BeginEntitySubmission(entity);
                    try { EntityPresentation.Get(entity, this).GetDrawInfo(); }
                    finally { EndEntitySubmission(); }
                }
                if (_showVolumes != VolumeDisplay.None)
                {
                    EntityPresentation.Get(entity, this).GetDisplayVolumes();
                }
            }

            // A host-authorized, server-selected QZ1 diagnostic is rendered
            // as bounded world geometry. It never feeds the simulation or
            // accepts a rewind/query request from this client.
            if (Mods.Network.AuthoritativePlay.Current?.Client.HistoricalDebug is { } historicalDebug)
                HistoricalCollisionDebugPresentation.Draw(this, historicalDebug);

            if (ProcessFrame && World.Match.LegacyState == MatchState.InProgress)
            {
                for (int i = 0; i < _pendingEffectSteps; i++)
                {
                    // Oldest owed step first, and each advanced under its own
                    // effect frame -- so an element spawned during that step
                    // sees the parity it was created with.
                    ulong owed = (ulong)(_pendingEffectSteps - 1 - i);
                    ulong effectTick = _effectFrame >= owed ? _effectFrame - owed : _effectFrame;
                    ProcessEffects(effectTick);
                    CaptureParticlePoses(effectTick);
                }
            }
            _pendingEffectSteps = 0;

            for (int i = 0; i < _activeElements.Count; i++)
            {
                EffectElementEntry element = _activeElements[i];
                if (element.Flags.TestFlag(EffElemFlags.DrawEnabled))
                {
                    for (int j = 0; j < element.Particles.Count; j++)
                    {
                        EffectParticle particle = element.Particles[j];
                        Matrix4 matrix = _viewMatrix;
                        if (particle.Owner.Flags.TestFlag(EffElemFlags.UseTransform) && !particle.Owner.Flags.TestFlag(EffElemFlags.UseMesh))
                        {
                            matrix = particle.Owner.Transform * matrix;
                        }
                        particle.InvokeSetVecsFunc(matrix);
                        particle.InvokeDrawFunc(1);
                        if (particle.ShouldDraw)
                        {
                            particle.AddRenderItem(this);
                        }
                    }
                }
            }
            for (int i = 0; i < _singleParticleCount; i++)
            {
                SingleParticle single = _singleParticles[i];
                single.Process();
            }
            for (int i = 0; i < _singleParticleCount; i++)
            {
                SingleParticle single = _singleParticles[i];
                if (single.ShouldDraw)
                {
                    single.AddRenderItem(this);
                }
            }
            SubmitEnvironmentalParticles();
        }

#if ANDROID
        private void UpdateUniforms()
        {
            UseRoomLights();
            GL.Uniform1(_shaderLocations.UseFog, _hasFog && FogOn ? 1 : 0);
            GL.Uniform1(_shaderLocations.CelBands, Mods.RenderOptions.CelShading
                ? Mods.RenderOptions.CelBands : 0);
            GL.Uniform1(_shaderLocations.ShowColors, _showColors ? 1 : 0);
            if (ProcessFrame)
            {
                UpdateFade();
            }
        }

        private void UseRoomLights()
        {
            GL.Uniform3(_shaderLocations.Light1Vector, _light1Vector);
            GL.Uniform3(_shaderLocations.Light1Color, _light1Color);
            GL.Uniform3(_shaderLocations.Light2Vector, _light2Vector);
            GL.Uniform3(_shaderLocations.Light2Color, _light2Color);
        }

        private void UseLight1(Vector3 vector, Vector3 color)
        {
            GL.Uniform3(_shaderLocations.Light1Vector, vector);
            GL.Uniform3(_shaderLocations.Light1Color, color);
        }

        private void UseLight2(Vector3 vector, Vector3 color)
        {
            GL.Uniform3(_shaderLocations.Light2Vector, vector);
            GL.Uniform3(_shaderLocations.Light2Color, color);
        }
#endif

        private FadeType _fadeType = FadeType.None;
        public FadeType FadeType => _fadeType;
        private float _fadeColor = 0;
        private bool _fadeIn = false;
        private float _fadeStart = 0;
        private float _fadeLength = 0;
        private float _fadePercent = 0;
        private float _fadeDelay = 0;
        private bool _fadeEnded = false;
        private AfterFade _afterFade = AfterFade.None;

        public void SetFade(FadeType type, float length, bool overwrite, AfterFade afterFade = AfterFade.None, float delay = 0)
        {
            if (!overwrite && _fadeType != FadeType.None)
            {
                return;
            }
            _fadeType = type;
            _fadeDelay = delay;
            _fadePercent = 0;
            if (type == FadeType.None)
            {
                _fadeType = type;
                _fadeColor = 0;
                _fadeIn = false;
                _fadeStart = 0;
                _fadeLength = 0;
            }
            else if (type == FadeType.FadeInWhite)
            {
                _fadeColor = 1;
                _fadeIn = true;
            }
            else if (type == FadeType.FadeInBlack)
            {
                _fadeColor = 0;
                _fadeIn = true;
            }
            else if (type == FadeType.FadeOutWhite || type == FadeType.FadeOutInWhite)
            {
                _fadeColor = 1;
                _fadeIn = false;
            }
            else if (type == FadeType.FadeOutBlack || type == FadeType.FadeOutInBlack)
            {
                _fadeColor = 0;
                _fadeIn = false;
            }
            _fadeStart = World.GlobalElapsedTime;
            _fadeLength = length;
            _afterFade = afterFade;
            _fadeEnded = false;
        }

        private void UpdateFade(bool updateDevice = true)
        {
            Color4 clearColor = _clearColor;
            if (_fadeType != FadeType.None)
            {
                if (_fadeDelay > 0)
                {
                    _fadeDelay -= World.FrameTime * _pendingFadeSteps;
                    _fadeStart = World.GlobalElapsedTime;
                }
                _fadePercent = (World.GlobalElapsedTime - _fadeStart) / _fadeLength;
                if (_fadePercent >= 1)
                {
                    _fadePercent = 1;
                    if (!_fadeEnded)
                    {
                        EndFade();
                        _fadeEnded = true;
                    }
                }
                else
                {
                    _fadeEnded = false;
                }
            }
            else
            {
                _fadeEnded = false;
            }
            _pendingFadeSteps = 0;
#if ANDROID
            if (updateDevice) GL.ClearColor(_clearColor);
#endif
        }

        private void QuitGame()
        {
            _fadeType = FadeType.None;
            DoCleanup();
            _close.Invoke();
        }

        public void DoCleanup(bool preserveSharedAudio = false)
        {
            if (!_exiting)
            {
                _exiting = true;
                DisposeAnnouncerAudio();
                World.CloseWorld();
                if (!preserveSharedAudio)
                {
                    Music.Stop();
                    Sound.Sfx.ShutDown();
                    Selection.Clear();
                }
                if (!_isolatedPresentation) OutputStop();
            }
        }

        internal void SetPresentationAudio(bool active)
        {
            _audioActive = active;
        }

        internal void SetGameplayInputSuppressed(bool suppressed)
        {
            _gameplayInputSuppressed = suppressed;
        }

        private void EndFade()
        {
            if (_afterFade == AfterFade.Exit)
            {
                QuitGame();
                return;
            }
            AfterFade afterFade = _afterFade; // may get updated
            if (_fadeType == FadeType.FadeOutInBlack)
            {
                SetFade(FadeType.FadeInBlack, _fadeLength, overwrite: true);
            }
            else if (_fadeType == FadeType.FadeOutInWhite)
            {
                SetFade(FadeType.FadeInWhite, _fadeLength, overwrite: true);
            }
            else if (afterFade == AfterFade.LoadRoom)
            {
                Debug.Assert(World.Room != null);
                _visualLightIdentities.ResetScope();
                ResetTransientVisualLights();
                World.Room.LoadRoom(resume: false);
                FadeType fadeType = _fadeType == FadeType.FadeOutWhite ? FadeType.FadeInWhite : FadeType.FadeInBlack;
                SetFade(fadeType, 10 / 30f, overwrite: true);
            }
            else
            {
                _fadeType = FadeType.None;
                _fadeColor = 0;
                _fadeIn = false;
                _fadePercent = 0;
                _fadeStart = 0;
                _fadeLength = 0;
            }
        }

        public LayerInfo Layer1Info { get; } = new LayerInfo();
        public LayerInfo Layer2Info { get; } = new LayerInfo();
        public LayerInfo Layer3Info { get; } = new LayerInfo();
        public LayerInfo Layer4Info { get; } = new LayerInfo();
        public LayerInfo Layer5Info { get; } = new LayerInfo();

#if !ANDROID
        public void DrawCustomCrosshair(Vector3 color, Vector2 position) => CaptureCustomCrosshair(color, position);

        public void DrawHudRadialSector(int sector, Vector4 color)
            => CaptureHudRadialSector(sector, color);

        public void DrawHudFlatBox(float left, float top, float right, float bottom, Vector4 color)
            => CaptureHudFlatBox(left, top, right, bottom, color);

        public void DrawHudTexture(TextureIdentity texture, float left, float top, float right,
            float bottom, Vector4 color, Vector4 uvRect, float rotation = 0, float alpha = 1)
            => CaptureHudTexture(texture, left, top, right, bottom, color, uvRect, rotation, alpha);

        public void DrawHudGeometry(IReadOnlyList<HudGeometryVertex> geometry)
            => CaptureHudGeometry(geometry);

        public void DrawHudObject(HudObjectInstance instance, int mode = 0, float scale = 1)
            => CaptureHudObject(instance, mode, scale);

        public void DrawIconModel(Vector2 position, float angle, ModelInstance instance,
            ColorRgb color, float alpha)
            => CaptureHudIconModel(position, angle, instance, color, alpha);

        public void DrawHudFilterModel(ModelInstance instance, float alpha = 1)
            => CaptureHudFilterModel(instance, alpha);

        public void DrawHudDamageModel(ModelInstance instance)
            => CaptureHudDamageModel(instance);
#else
        private void SetHudLayerUniforms()
        {
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            Matrix4 identity = Matrix4.Identity;
            GL.UniformMatrix4(_shaderLocations.MatrixStack, transpose: false, ref identity);
            GL.UniformMatrix4(_shaderLocations.ViewInvMatrix, transpose: false, ref identity);
            GL.Uniform1(_shaderLocations.UseLight, 0);
            GL.Color3(Vector3.One);
            GL.Uniform3(_shaderLocations.Diffuse, Vector3.One);
            GL.Uniform3(_shaderLocations.Ambient, Vector3.One);
            GL.Uniform3(_shaderLocations.Specular, Vector3.One);
            GL.Uniform3(_shaderLocations.Emission,
                _glesEnhancedActive ? Vector3.Zero : Vector3.One);
            if (_glesEnhancedActive)
            {
                _glesEnhanced.PrepareHudScene();
            }
            GL.Uniform1(_shaderLocations.MaterialMode, (int)PolygonMode.Modulate);
            GL.Uniform1(_shaderLocations.TexgenMode, (int)TexgenMode.None);
            GL.UniformMatrix4(_shaderLocations.TextureMatrix, transpose: false, ref identity);
            GL.Uniform1(_shaderLocations.UseTexture, 1);
            GL.Uniform1(_shaderLocations.UseOverride, 0);
            GL.Uniform1(_shaderLocations.UsePaletteOverride, 0);
            GL.Uniform1(_shaderLocations.UseFog, 0);
            // The damage flash, the locator icons and the intro filter are HUD
            // drawn through the scene's program because they are models. They
            // are not part of the world, so they are not flattened and not
            // banded; UpdateUniforms puts the bands back next frame.
            GL.Uniform1(_shaderLocations.UseFlat, 0);
            GL.Uniform1(_shaderLocations.CelBands, 0);
            if (_faceCulling)
            {
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(TriangleFace.Back);
            }
            GL.UniformMatrix4(_shaderLocations.ViewMatrix, transpose: false, ref identity);
            var orthoMatrix = Matrix4.CreateOrthographic(Size.X, Size.Y, 0.5f, 1.5f);
            GL.UniformMatrix4(_shaderLocations.ProjectionMatrix, transpose: false, ref orthoMatrix);
        }

        private void UnsetHudLayerUniforms()
        {
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.DepthTest);
            if (Mods.SpectatorMode.IsSpectating) _cameraFov = MathHelper.DegreesToRadians(SpectatorCamera.FieldOfView);
            GL.UniformMatrix4(_shaderLocations.ViewMatrix, transpose: false, ref _viewMatrix);
            GL.UniformMatrix4(_shaderLocations.ProjectionMatrix, transpose: false, ref _perspectiveMatrix);
        }

        private void DrawHudLayer(LayerInfo info)
        {
            if (_capturingPresentationFrame)
            {
                CaptureHudLayer(info);
                return;
            }
            if (info.BindingId == -1)
            {
                return;
            }
            GL.Uniform1(_shaderLocations.LayerAlpha, info.Alpha);
            GL.BindTexture(TextureTarget.Texture2D, info.BindingId);
            int minParameter = (int)TextureMinFilter.Nearest;
            int magParameter = (int)TextureMagFilter.Nearest;
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, minParameter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, magParameter);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            float viewWidth = Size.X;
            float viewHeight = Size.Y;
            float width;
            float height;
            if (info.ScaleX == -1 || info.ScaleY == -1)
            {
                float size = MathF.Max(viewWidth, viewHeight) / 2;
                width = size / (viewWidth / 2);
                height = size / (viewHeight / 2);
            }
            else
            {
                width = viewWidth * info.ScaleX / 2 / (viewWidth / 2);
                height = viewHeight * info.ScaleY / 2 / (viewHeight / 2);
            }
            GL.DrawTexturedQuad(
                new Vector3(width + info.ShiftX, height + info.ShiftY, 0f),
                new Vector3(-width + info.ShiftX, height + info.ShiftY, 0f),
                new Vector3(width + info.ShiftX, -height + info.ShiftY, 0f),
                new Vector3(-width + info.ShiftX, -height + info.ShiftY, 0f));
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>
        /// The player's crosshair at the canonical projected aim position, drawn with none
        /// of the game's sprite assets -- the Quake-Live-style alternative to
        /// the reticle. Reuses the RTT shader's fade_color path (normally the
        /// full-screen fade) as a flat-fill: with its alpha above zero the
        /// fragment shader outputs that colour outright instead of sampling
        /// the bound texture, which is exactly "draw a solid shape with no
        /// asset."
        ///
        /// Which shape, and how big, come from
        /// <see cref="Mods.Render.Crosshair"/> -- the same table the settings
        /// screen draws its preview from.
        /// </summary>
        public void DrawCustomCrosshair(Vector3 color, Vector2 position)
        {
            if (_capturingPresentationFrame)
            {
                CaptureCustomCrosshair(color, position);
                return;
            }
            float halfW = Size.X / 2f;
            float halfH = Size.Y / 2f;
            Vector2 center = Mods.Render.Crosshair.ToNdc(position);
            Mods.Render.CrosshairStyle style = Mods.Render.Crosshair.Style;
            float scale = Mods.Render.Crosshair.Scale;
            GL.Uniform4(_shaderLocations.FadeColor, color.X, color.Y, color.Z, 1f);
            Span<Mods.Render.CrosshairBar> bars = stackalloc Mods.Render.CrosshairBar[8];
            int barCount = Mods.Render.Crosshair.FillBars(style, scale, bars);
            for (int i = 0; i < barCount; i++)
            {
                (float left, float right, float bottom, float top) =
                    Mods.Render.Crosshair.EdgesOf(bars[i]);
                GL.DrawSolidQuad(
                    new Vector3(center.X + right / halfW, center.Y + top / halfH, 0f),
                    new Vector3(center.X + left / halfW, center.Y + top / halfH, 0f),
                    new Vector3(center.X + right / halfW, center.Y + bottom / halfH, 0f),
                    new Vector3(center.X + left / halfW, center.Y + bottom / halfH, 0f),
                    Vector4.One);
            }
            (float radius, float thickness) = Mods.Render.Crosshair.RingOf(style, scale);
            if (thickness > 0)
            {
                // An annulus as one triangle strip: outer point, inner point,
                // round the circle and back to the start. Enough segments that
                // the flats are under a pixel at the sizes this is drawn at,
                // and it is four dozen vertices once a frame either way.
                const int segments = 40;
                GL.DrawCrosshairRing(radius, thickness, halfW, halfH, center, segments);
            }
            GL.Uniform4(_shaderLocations.FadeColor, Vector4.Zero);
        }

        /// <summary>
        /// A flat-coloured, unbordered rectangle in the same 256x192 virtual
        /// space <see cref="DrawHudObject"/>'s mode 2 and the HUD text draw
        /// use -- for the modern HUD's equipped-weapon highlight, which has
        /// no sprite asset of its own. Same fade_color flat-fill trick as
        /// <see cref="DrawCustomCrosshair"/>.
        /// </summary>
        public void DrawHudRadialSector(int sector, Vector4 color)
        {
            if (_capturingPresentationFrame)
            {
                CaptureHudRadialSector(sector, color);
                return;
            }
            GL.Uniform4(_shaderLocations.FadeColor, color);
            GL.PrepareDynamicMesh(MeshPrimitiveTopology.TriangleStrip, 14);
            for (int step = 0; step <= 6; step++)
            {
                Vector2 outer = Mods.Input.WeaponRadialSelection.SectorPoint(sector, step / 6f, 87, 58);
                Vector2 inner = Mods.Input.WeaponRadialSelection.SectorPoint(sector, step / 6f, 19, 13);
                GL.SetDynamicMeshVertex(step * 2,
                    new Vector3(outer.X / 128, (4 - outer.Y) / 96, 0f));
                GL.SetDynamicMeshVertex(step * 2 + 1,
                    new Vector3(inner.X / 128, (4 - inner.Y) / 96, 0f));
            }
            GL.DrawPreparedDynamicMesh();
            GL.Uniform4(_shaderLocations.FadeColor, Vector4.Zero);
        }

        public void DrawHudFlatBox(float left, float top, float right, float bottom, Vector4 color)
        {
            if (_capturingPresentationFrame)
            {
                CaptureHudFlatBox(left, top, right, bottom, color);
                return;
            }
            float halfW = Size.X / 2f;
            float halfH = Size.Y / 2f;
            float x0 = (left / 256f * Size.X - halfW) / halfW;
            float x1 = (right / 256f * Size.X - halfW) / halfW;
            float y0 = (halfH - top / 192f * Size.Y) / halfH;
            float y1 = (halfH - bottom / 192f * Size.Y) / halfH;
            GL.Uniform4(_shaderLocations.FadeColor, color);
            GL.DrawSolidQuad(
                new Vector3(x1, y0, 0f), new Vector3(x0, y0, 0f),
                new Vector3(x1, y1, 0f), new Vector3(x0, y1, 0f), Vector4.One);
            GL.Uniform4(_shaderLocations.FadeColor, Vector4.Zero);
        }

        public void DrawHudTexture(TextureIdentity texture, float left, float top, float right,
            float bottom, Vector4 color, Vector4 uvRect, float rotation = 0, float alpha = 1)
        {
            if (_capturingPresentationFrame)
            {
                CaptureHudTexture(texture, left, top, right, bottom, color, uvRect, rotation, alpha);
                return;
            }
            if (!_dynamicTextureBindings.TryGetValue(texture, out int binding)) return;
            RenderOverlayCommand command = CreateHudTextureCommand(Size, texture, left, top, right,
                bottom, color, uvRect, rotation, alpha, RenderPresentationStage.HudOverlay);
            GL.Uniform1(_shaderLocations.LayerAlpha, alpha);
            GL.Uniform1(_shaderLocations.UseHudVertexColor, 1);
            GL.Uniform1(_shaderLocations.UseHudTexture, 1);
            GL.BindTexture(TextureTarget.Texture2D, binding);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);
            GL.PrepareDynamicMesh(MeshPrimitiveTopology.TriangleStrip, command.Vertices.Count);
            for (int i = 0; i < command.Vertices.Count; i++)
            {
                RenderOverlayVertex vertex = command.Vertices[i];
                GL.SetDynamicMeshVertex(i, vertex.Position, vertex.TexCoord, vertex.Color,
                    explicitColor: true);
            }
            GL.DrawPreparedDynamicMesh();
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.Uniform1(_shaderLocations.UseHudVertexColor, 0);
            GL.Uniform1(_shaderLocations.UseHudTexture, 0);
            GL.Uniform1(_shaderLocations.LayerAlpha, 1f);
        }

        public void DrawHudGeometry(IReadOnlyList<HudGeometryVertex> geometry)
        {
            if (_capturingPresentationFrame)
            {
                CaptureHudGeometry(geometry);
                return;
            }
            RenderOverlayCommand command = CreateHudGeometryCommand(Size, geometry,
                RenderPresentationStage.HudOverlay);
            GL.Uniform4(_shaderLocations.FadeColor, Vector4.Zero);
            GL.Uniform1(_shaderLocations.UseHudVertexColor, 1);
            GL.Uniform1(_shaderLocations.UseHudTexture, 0);
            GL.Uniform1(_shaderLocations.LayerAlpha, 1f);
            GL.PrepareDynamicMesh(MeshPrimitiveTopology.TriangleStrip, command.Vertices.Count);
            for (int i = 0; i < command.Vertices.Count; i++)
            {
                RenderOverlayVertex vertex = command.Vertices[i];
                GL.SetDynamicMeshVertex(i, vertex.Position, Vector2.Zero, vertex.Color,
                    explicitColor: true);
            }
            GL.DrawPreparedDynamicMesh();
            GL.Uniform1(_shaderLocations.UseHudVertexColor, 0);
            GL.Uniform4(_shaderLocations.FadeColor, Vector4.Zero);
        }

        /// <param name="scale">
        /// Multiplies the size the object is drawn at, without touching the
        /// size it is *cut out* at. Those are the same field on the instance --
        /// Width and Height say how big a glyph is in the character data as
        /// well as how big it lands on screen -- so shrinking text by setting
        /// them re-cuts the font at the wrong size and draws confetti. This is
        /// the destination, and only the destination.
        /// </param>
        public void DrawHudObject(HudObjectInstance inst, int mode = 0, float scale = 1)
        {
            if (_capturingPresentationFrame)
            {
                CaptureHudObject(inst, mode, scale);
                return;
            }
            if (!inst.Enabled)
            {
                return;
            }
            float x = inst.PositionX;
            float y = inst.PositionY;
            float width = inst.Width;
            float height = inst.Height;
            bool center = inst.Center;
            GL.Uniform1(_shaderLocations.LayerAlpha, inst.Alpha);
            GL.Uniform1(_shaderLocations.UseMask, inst.UseMask ? 1 : 0);
            GL.BindTexture(TextureTarget.Texture2D, inst.BindingId);
            int minParameter = (int)TextureMinFilter.Nearest;
            int magParameter = (int)TextureMagFilter.Nearest;
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, minParameter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, magParameter);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            float viewWidth = Size.X;
            float viewHeight = Size.Y;
            if (mode == 2)
            {
                width = width / 256 * viewWidth;
                height = height / 192 * viewHeight;
            }
            else if (mode == 1)
            {
                float aspect = height / width;
                height = height / 192 * viewHeight;
                width = height / aspect;
            }
            else // if (mode == 0)
            {
                float aspect = width / height;
                width = width / 256 * viewWidth;
                height = width / aspect;
            }
            if (scale != 1)
            {
                width *= scale;
                height *= scale;
            }
            float viewLeft = -viewWidth / 2;
            float viewTop = viewHeight / 2;
            float leftPos = viewLeft + x * viewWidth - (center ? (width / 2) : 0);
            float rightPos = leftPos + width;
            float topPos = viewTop - y * viewHeight + (center ? (height / 2) : 0);
            float bottomPos = topPos - height;
            leftPos /= (viewWidth / 2);
            rightPos /= (viewWidth / 2);
            topPos /= (viewHeight / 2);
            bottomPos /= (viewHeight / 2);
            if (inst.FlipHorizontal)
            {
                (rightPos, leftPos) = (leftPos, rightPos);
            }
            if (inst.FlipVertical)
            {
                (bottomPos, topPos) = (topPos, bottomPos);
            }
            GL.DrawTexturedQuad(
                new Vector3(rightPos, topPos, 0f), new Vector3(leftPos, topPos, 0f),
                new Vector3(rightPos, bottomPos, 0f), new Vector3(leftPos, bottomPos, 0f));
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void DrawIconModel(Vector2 position, float angle, ModelInstance inst, ColorRgb color, float alpha)
        {
            if (_capturingPresentationFrame)
            {
                CaptureHudIconModel(position, angle, inst, color, alpha);
                return;
            }
            float scale = Size.Y / 192f;
            var position3d = new Vector3(position.X * Size.X - Size.X / 2, (1 - position.Y) * Size.Y - (Size.Y / 2), -1f);
            Matrix4 transform = Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(angle))
                * Matrix4.CreateScale(scale, scale, 1) * Matrix4.CreateTranslation(position3d);
            GL.UniformMatrix4(_shaderLocations.MatrixStack, transpose: false, ref transform);
            Model model = inst.Model;
            UpdateMaterials(model, 0);
            GL.Uniform1(_shaderLocations.MaterialAlpha, alpha);
            GL.BindTexture(TextureTarget.Texture2D, GetTextureBindingId(model.Materials[0]));
            int minParameter = (int)TextureMinFilter.Nearest;
            int magParameter = (int)TextureMagFilter.Nearest;
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, minParameter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, magParameter);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.Color3(new Vector3(color.Red / 31f, color.Green / 31f, color.Blue / 31f));
            Mesh iconMesh = model.Meshes[0];
            if (!_portableMeshes.TryGetValue(iconMesh.GeometryIdentity, out CpuMesh? iconCpuMesh))
            {
                throw new ProgramException("Android GLES HUD icon has no prepared CpuMesh.");
            }
            GL.DrawStaticMesh(iconMesh.GeometryIdentity, iconCpuMesh);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            Matrix4 identity = Matrix4.Identity;
            GL.UniformMatrix4(_shaderLocations.MatrixStack, transpose: false, ref identity);
        }

        public void DrawHudFilterModel(ModelInstance inst, float alpha = 1)
        {
            if (_capturingPresentationFrame)
            {
                CaptureHudFilterModel(inst, alpha);
                return;
            }
            Model model = inst.Model;
            UpdateMaterials(model, 0);
            Material material = model.Materials[0];
            GL.Uniform1(_shaderLocations.MaterialAlpha, material.Alpha / 31f * alpha);
            GL.BindTexture(TextureTarget.Texture2D, GetTextureBindingId(material));
            int minParameter = (int)TextureMinFilter.Nearest;
            int magParameter = (int)TextureMagFilter.Nearest;
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, minParameter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, magParameter);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            float viewWidth = Size.X;
            float viewHeight = Size.Y;
            GL.DrawTexturedQuad(
                new Vector3(viewWidth, viewHeight, -1f), new Vector3(-viewWidth, viewHeight, -1f),
                new Vector3(viewWidth, -viewHeight, -1f), new Vector3(-viewWidth, -viewHeight, -1f));
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private readonly float[] _hudMatrixStack = new float[16 * 31];

        public void DrawHudDamageModel(ModelInstance inst)
        {
            if (_capturingPresentationFrame)
            {
                CaptureHudDamageModel(inst);
                return;
            }
            Model model = inst.Model;
            UpdateMaterials(model, 0);
            GL.Uniform1(_shaderLocations.MaterialAlpha, 1f);
            GL.BindTexture(TextureTarget.Texture2D, GetTextureBindingId(model.Materials[0]));
            int minParameter = (int)TextureMinFilter.Nearest;
            int magParameter = (int)TextureMagFilter.Nearest;
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, minParameter);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, magParameter);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            float viewWidth = Size.X;
            float viewHeight = Size.Y;
            float xOffset = -viewWidth / 2;
            float yOffset = -viewHeight / 2;
            // ltodo: we only need to update this loop and matrix stack update if the viewport changes
            for (int i = 1; i < 9; i++)
            {
                Node node = inst.Model.Nodes[i];
                if (node.Enabled)
                {
                    float width = node.MaxBounds.X - node.MinBounds.X;
                    float height = node.MaxBounds.Y - node.MinBounds.Y;
                    float newWidth = width / 256 * viewWidth;
                    float newHeight = height / 192 * viewHeight;
                    newWidth *= model.Scale.X;
                    newHeight *= model.Scale.Y;
                    var transform = Matrix4.CreateScale(newWidth / width, newHeight / height, 1);
                    transform.Row3.Xyz = new Vector3(xOffset, yOffset, -1);
                    node.Animation = transform;
                }
            }
            model.UpdateMatrixStack();
            Array.Copy(model.MatrixStackValues.ToArray(), _hudMatrixStack, model.MatrixStackValues.Count);
            GL.UniformMatrix4(_shaderLocations.MatrixStack, model.NodeMatrixIds.Count, transpose: false, _hudMatrixStack);
            for (int i = 1; i < 9; i++)
            {
                Node node = inst.Model.Nodes[i];
                if (node.Enabled)
                {
                    Mesh mesh = model.Meshes[node.MeshId / 2];
                    if (!_portableMeshes.TryGetValue(mesh.GeometryIdentity, out CpuMesh? cpuMesh))
                    {
                        throw new ProgramException("Android GLES HUD model has no prepared CpuMesh.");
                    }
                    GL.DrawStaticMesh(mesh.GeometryIdentity, cpuMesh);
                }
            }
            GL.BindTexture(TextureTarget.Texture2D, 0);
            Matrix4 identity = Matrix4.Identity;
            GL.UniformMatrix4(_shaderLocations.MatrixStack, transpose: false, ref identity);
        }
#endif

        public void LookAt(Vector3 target)
        {
            _cameraMode = CameraMode.Roam;
            _inputMode = InputMode.CameraOnly;
            _cameraPosition = target.AddZ(5);
            _cameraFacing = -Vector3.UnitZ;
            _cameraUp = Vector3.UnitY;
            _cameraRight = Vector3.UnitX;
        }

        /// <summary>
        /// True while the spectator's free camera specifically (as opposed to
        /// the room/model viewer's own Roam camera, which is CameraMode.Roam
        /// too) is up -- lets <see cref="OnMouseMove"/> turn it without
        /// requiring the viewer tool's held-left-click gesture, since this
        /// is meant to feel like the game's ordinary first-person look.
        /// </summary>
        private bool _freeCam;
        public bool IsFreeCam => _freeCam;
        /// <summary>
        /// Whether the scoreboard should be drawn over the spectator's free
        /// camera: they are on it, and holding the button for it.
        ///
        /// The free camera is CameraMode.Roam, which is what gets it "no HUD"
        /// for free -- the HUD is only drawn for a player's own camera. A
        /// scoreboard is the match's rather than a player's, though, and
        /// somebody watching from the map is exactly who wants to read one,
        /// so this is the one thing that reaches past that.
        /// </summary>
        private bool ScoreboardOverFreeCamera => Mods.SpectatorMode.FreeCamera
            && Mods.SpectatorMode.ShowScoreboard
            && World.LocalPlayer!.LoadFlags.TestFlag(LoadFlags.Active);

        /// <summary>
        /// The spectator's own no-clip camera: an independent view of the map
        /// with no HUD, instead of riding along with whoever spectator mode
        /// has the camera on. What Space toggles while watching, and where
        /// spectating now starts -- looking at the map rather than out of
        /// somebody's eyes, the way Quake's spectator does.
        ///
        /// Reuses the same Roam camera the room/model viewer tooling already
        /// has -- WASD and the arrow keys already move and turn it in
        /// <see cref="OnKeyHeld"/>, and the HUD draw calls already gate on
        /// <c>CameraMode == CameraMode.Player</c>, so switching to Roam gets
        /// "no HUD" for free rather than needing a separate suppression
        /// somewhere.
        /// </summary>
        public void SetFreeCamera(bool on)
        {
            if (on == _freeCam && (on || _cameraMode == CameraMode.Player))
            {
                return;
            }
            if (!on)
            {
                _cameraMode = CameraMode.Player;
                _inputMode = InputMode.All;
                _freeCam = false;
                if (!_isolatedPresentation)
                {
                    Mods.SpectatorMode.NoteFreeCamera(false);
                }
                return;
            }
            // Starts where the view already was, so turning it on is a change
            // of control and not a cut to somewhere else in the room.
            _cameraPosition = World.LocalPlayer!.CameraInfo.Position;
            _cameraFacing = World.LocalPlayer!.CameraInfo.Facing;
            if (_cameraFacing.LengthSquared < 0.0001f)
            {
                _cameraFacing = -Vector3.UnitZ;
            }
            _cameraFacing = _cameraFacing.Normalized();
            _cameraRight = Vector3.Cross(_cameraFacing, Vector3.UnitY);
            _cameraUp = Vector3.Cross(_cameraRight, _cameraFacing);
            _cameraMode = CameraMode.Roam;
            _inputMode = InputMode.CameraOnly;
            _freeCam = true;
            if (!_isolatedPresentation)
            {
                Mods.SpectatorMode.NoteFreeCamera(true);
            }
        }

        public void ToggleFreeCamera()
        {
            SetFreeCamera(!_freeCam);
        }

        public void OnMouseClick(bool down)
        {
            if (_inputMode != InputMode.PlayerOnly)
            {
                _leftMouse = down;
            }
        }

        public void OnMouseMove(float deltaX, float deltaY)
        {
            if ((_leftMouse || _freeCam) && AllowCameraMovement && _inputMode != InputMode.PlayerOnly)
            {
                // The spectator's free camera is the player's look, so it
                // obeys the player's settings.
                //
                // The /1.5 below is the model viewer's own feel and stays that
                // for the viewer's Pivot and Roam cameras -- those are a tool,
                // not a game. But the replay/spectator free camera is reached
                // from a match, with the same mouse, and it ignored mouse
                // sensitivity and both invert axes outright: turning it on
                // changed how fast the view turned and which way up it went,
                // with nothing in the settings able to say otherwise.
                float sensitivity = 1f;
                float invertX = 1f;
                float invertY = 1f;
                if (_freeCam)
                {
                    sensitivity = Mods.InputSettings.MouseSensitivity;
                    invertX = Mods.InputSettings.InvertMouseX ? -1f : 1f;
                    invertY = Mods.InputSettings.InvertMouseY ? -1f : 1f;
                }
                float moveX = deltaX * sensitivity * invertX;
                float moveY = deltaY * sensitivity * invertY;
                if (_cameraMode == CameraMode.Pivot)
                {
                    _pivotAngleX += moveY / 1.5f;
                    _pivotAngleX = Math.Clamp(_pivotAngleX, -90.0f, 90.0f);
                    _pivotAngleY += moveX / 1.5f;
                    _pivotAngleY %= 360f;
                }
                else if (_cameraMode == CameraMode.Roam)
                {
                    UpdateCameraRotation(MathHelper.DegreesToRadians(moveX / 1.5f), MathHelper.DegreesToRadians(-moveY / 1.5f));
                }
            }
        }

        public void OnMouseWheel(float offsetY)
        {
            if (_cameraMode == CameraMode.Pivot && AllowCameraMovement && _inputMode != InputMode.PlayerOnly)
            {
                _pivotDistance += offsetY / -1.5f;
                if (_pivotDistance < 0)
                {
                    _pivotDistance = 0;
                }
                else if (_pivotDistance > 1000)
                {
                    _pivotDistance = 1000;
                }
            }
        }

        public bool ShowCollision => _showCollision;
        public EntityType ColEntDisplay { get; private set; } = EntityType.Room;
        public Terrain ColTerDisplay { get; private set; } = Terrain.All;
        public CollisionType ColTypeDisplay { get; private set; } = CollisionType.Any;
        public CollisionColor ColDisplayColor { get; private set; } = CollisionColor.None;
        public float ColDisplayAlpha { get; private set; } = 0.5f;
        private int _colMenuSelect = 0; // 0-4

        public void OnKeyDown(WindowKeyEvent e)
        {
#if DEBUG
            if (Selection.OnKeyDown(e, World))
            {
                return;
            }
            if (e.Key == Keys.R)
            {
                if (e.Control && e.Shift)
                {
                    if (_recording)
                    {
                        // SDL readbacks are delivered through the backend
                        // completion channel, but stopping the shared CPU
                        // recording consumer is still required. Stop has no
                        // graphics API calls and is safe for both frontends.
                        ScreenCapture.StopRecording();
                    }
                    _recording = !_recording;
                    if (_recording && RenderBackendSelection.Current == RenderBackendKind.Sdl)
                    {
                        ScreenCapture.StartRecording();
                    }
                    _framesRecorded = 0;
                    _pendingSdlRecordingRequest = null;
                }
                else if (AllowCameraMovement && _inputMode != InputMode.PlayerOnly)
                {
                    ResetCamera();
                }
            }
            if (e.Key == Keys.P)
            {
                if (e.Alt)
                {
                    UpdatePointModule();
                }
                else if (e.Shift)
                {
                    if (_cameraMode != CameraMode.Player)
                    {
                        if (_inputMode == InputMode.All)
                        {
                            _inputMode = InputMode.PlayerOnly;
                        }
                        else if (_inputMode == InputMode.PlayerOnly)
                        {
                            _inputMode = InputMode.CameraOnly;
                        }
                        else
                        {
                            _inputMode = InputMode.All;
                        }
                    }
                }
                else
                {
                    if (_cameraMode == CameraMode.Pivot)
                    {
                        _cameraMode = CameraMode.Roam;
                        _inputMode = InputMode.CameraOnly;
                    }
                    else if (_cameraMode == CameraMode.Roam)
                    {
                        _cameraMode = CameraMode.Player;
                        _inputMode = InputMode.All;
                    }
                    else
                    {
                        _cameraMode = CameraMode.Pivot;
                        _inputMode = InputMode.CameraOnly;
                    }
                    ResetCamera();
                }
            }
            else if (e.Key == Keys.Enter)
            {
                _frameAdvanceOn = !_frameAdvanceOn;
            }
            else if (e.Key == Keys.Period)
            {
                if (_frameAdvanceOn)
                {
                    _advanceOneFrame = true;
                }
            }
            if (_inputMode == InputMode.PlayerOnly)
            {
                return;
            }
            if (e.Key == Keys.J && _showCollision)
            {
                if (_colMenuSelect == 0)
                {
                    if (e.Control)
                    {
                        ColEntDisplay = EntityType.All;
                    }
                    else if (e.Shift)
                    {
                        if (ColEntDisplay == EntityType.Room)
                        {
                            ColEntDisplay = EntityType.All;
                        }
                        else if (ColEntDisplay == EntityType.Object)
                        {
                            ColEntDisplay = EntityType.Platform;
                        }
                        else if (ColEntDisplay == EntityType.All)
                        {
                            ColEntDisplay = EntityType.Object;
                        }
                        else
                        {
                            ColEntDisplay = EntityType.Room;
                        }
                    }
                    else
                    {
                        if (ColEntDisplay == EntityType.Room)
                        {
                            ColEntDisplay = EntityType.Platform;
                        }
                        else if (ColEntDisplay == EntityType.Platform)
                        {
                            ColEntDisplay = EntityType.Object;
                        }
                        else if (ColEntDisplay == EntityType.Object)
                        {
                            ColEntDisplay = EntityType.All;
                        }
                        else
                        {
                            ColEntDisplay = EntityType.Room;
                        }
                    }
                }
                else if (_colMenuSelect == 1)
                {
                    if (e.Control)
                    {
                        ColTerDisplay = Terrain.All;
                    }
                    else if (e.Shift)
                    {
                        ColTerDisplay--;
                        if ((byte)ColTerDisplay == 255)
                        {
                            ColTerDisplay = Terrain.All;
                        }
                    }
                    else
                    {
                        ColTerDisplay++;
                        if (ColTerDisplay > Terrain.All)
                        {
                            ColTerDisplay = Terrain.Metal;
                        }
                    }
                }
                else if (_colMenuSelect == 2)
                {
                    if (e.Control)
                    {
                        ColTypeDisplay = CollisionType.Any;
                    }
                    else if (e.Shift)
                    {
                        ColTypeDisplay--;
                        if (ColTypeDisplay < 0)
                        {
                            ColTypeDisplay = CollisionType.Both;
                        }
                    }
                    else
                    {
                        ColTypeDisplay++;
                        if (ColTypeDisplay > CollisionType.Both)
                        {
                            ColTypeDisplay = CollisionType.Any;
                        }
                    }
                }
                else if (_colMenuSelect == 3)
                {
                    if (e.Control)
                    {
                        ColDisplayColor = CollisionColor.None;
                    }
                    else if (e.Shift)
                    {
                        ColDisplayColor--;
                        if (ColDisplayColor < 0)
                        {
                            ColDisplayColor = CollisionColor.Type;
                        }
                    }
                    else
                    {
                        ColDisplayColor++;
                        if (ColDisplayColor > CollisionColor.Type)
                        {
                            ColDisplayColor = CollisionColor.None;
                        }
                    }
                }
                else if (_colMenuSelect == 4)
                {
                    if (ColDisplayAlpha == 1)
                    {
                        ColDisplayAlpha = 0.5f;
                    }
                    else
                    {
                        ColDisplayAlpha = 1;
                    }
                }
            }
            else if (e.Key == Keys.J && _showBotAiSlot != -1)
            {
                if (e.Shift)
                {
                    if (_showBotAiSlot <= 0)
                    {
                        _showBotAiSlot = 3;
                    }
                    else
                    {
                        _showBotAiSlot--;
                    }
                }
                else if (_showBotAiSlot >= 3)
                {
                    _showBotAiSlot = 0;
                }
                else
                {
                    _showBotAiSlot++;
                }
            }
            else if (e.Key == Keys.K)
            {
                if (e.Alt)
                {
                    _showCollision = !_showCollision;
                }
                else if (_showCollision)
                {
                    if (e.Control)
                    {
                        _colMenuSelect = 0;
                        ColEntDisplay = EntityType.Room;
                        ColTerDisplay = Terrain.All;
                        ColTypeDisplay = CollisionType.Any;
                        ColDisplayColor = CollisionColor.None;
                        ColDisplayAlpha = 0.5f;
                    }
                    else if (e.Shift)
                    {
                        _colMenuSelect--;
                        if (_colMenuSelect < 0)
                        {
                            _colMenuSelect = 4;
                        }
                    }
                    else
                    {
                        _colMenuSelect++;
                        if (_colMenuSelect > 4)
                        {
                            _colMenuSelect = 0;
                        }
                    }
                }
            }
            else if (e.Key == Keys.D5 && e.Shift)
            {
                if (!_recording)
                {
#if ANDROID
                    ScreenCapture.Screenshot(Size.X, Size.Y);
#else
                    _pendingSdlScreenshot ??= new RenderCaptureRequest(
                        Guid.NewGuid(),
                        Mods.Render.FrameTiming.TotalFrames,
                        CaptureTargetKind.FinalPresentedFrame,
                        Size.X,
                        Size.Y,
                        CapturePixelFormat.Rgb8,
                        CaptureRowOrientation.BottomUp,
                        CaptureDeliveryKind.Screenshot,
                        DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString());
#endif
                }
            }
            else if (e.Key == Keys.T)
            {
                _showTextures = !_showTextures;
            }
            else if (e.Key == Keys.C)
            {
                if (e.Alt)
                {
                    if (e.Shift)
                    {
                        _promptState = PromptState.CameraPos;
                    }
                    else
                    {
                        _outputCameraPos = !_outputCameraPos;
                    }
                }
                else if (e.Control)
                {
                    _showColors = !_showColors;
                }
            }
            else if (e.Key == Keys.Q)
            {
                if (e.Alt)
                {
                    if (e.Shift)
                    {
                        _volumeEdges--;
                        if (_volumeEdges < 0)
                        {
                            _volumeEdges = 2;
                        }
                    }
                    else
                    {
                        _volumeEdges++;
                        if (_volumeEdges > 2)
                        {
                            _volumeEdges = 0;
                        }
                    }
                }
                else if (e.Control)
                {
                    _wireframe = !_wireframe;
                }
            }
            else if (e.Key == Keys.B)
            {
                if (e.Alt)
                {
                    _showBotAiSlot = _showBotAiSlot == -1 ? 1 : -1;
                }
                else
                {
                    _faceCulling = !_faceCulling;
                    if (!_faceCulling)
                    {
                        GL.Disable(EnableCap.CullFace);
                    }
                }
            }
            else if (e.Key == Keys.F)
            {
                FilteringOn = !FilteringOn;
            }
            else if (e.Key == Keys.L)
            {
                LightingOn = !LightingOn;
            }
            else if (e.Key == Keys.Z)
            {
                if (e.Control)
                {
                    _showVolumes = VolumeDisplay.None;
                }
                else if (e.Shift)
                {
                    _showVolumes--;
                    if (_showVolumes < VolumeDisplay.None)
                    {
                        _showVolumes = VolumeDisplay.Portal;
                    }
                }
                else
                {
                    _showVolumes++;
                    if (_showVolumes > VolumeDisplay.Portal)
                    {
                        _showVolumes = VolumeDisplay.None;
                    }
                }
            }
            else if (e.Key == Keys.G)
            {
                if (e.Alt)
                {
                    _useClip = !_useClip;
                }
                else
                {
                    FogOn = !FogOn;
                }
            }
            else if (e.Key == Keys.N)
            {
                if (e.Alt)
                {
                    _showAllNodes = !_showAllNodes;
                }
                else
                {
                    _transformRoomNodes = !_transformRoomNodes;
                }
            }
            else if (e.Key == Keys.H)
            {
                if (e.Alt)
                {
                    Selection.ToggleUnselectedVolumes();
                }
                else
                {
                    Selection.ToggleShowSelection();
                }
            }
            else if (e.Key == Keys.I)
            {
                if (e.Alt)
                {
                    if (_showInvisible == 2)
                    {
                        _showInvisible = 0;
                    }
                    else
                    {
                        _showInvisible = 2;
                    }
                }
                else if (_showInvisible == 0)
                {
                    _showInvisible = 1;
                }
                else
                {
                    _showInvisible = 0;
                }
            }
            else if (e.Key == Keys.Y)
            {
                _showNodeData = !_showNodeData;
            }
            else if (e.Key == Keys.E && e.Shift && !e.Alt)
            {
                _scanVisor = !_scanVisor;
            }
            else if (e.Control && e.Key == Keys.O)
            {
                _promptState = PromptState.Load;
            }
            else if (e.Control && e.Key == Keys.U)
            {
                if (Selection.Entity != null)
                {
                    _unloadQueue.Enqueue(Selection.Entity);
                }
            }
#endif
        }

        private enum InputMode
        {
            All,
            PlayerOnly,
            CameraOnly
        }

        private InputMode _inputMode = InputMode.All;

        private void OnKeyHeld()
        {
            if (_keyboardState.IsKeyDown(Keys.LeftAlt) || _keyboardState.IsKeyDown(Keys.RightAlt))
            {
                Selection.OnKeyHeld(_keyboardState);
                return;
            }
            if (!AllowCameraMovement || _inputMode == InputMode.PlayerOnly)
            {
                return;
            }
            if (_cameraMode == CameraMode.Roam)
            {
                float moveStep = _keyboardState.IsKeyDown(Keys.LeftShift) || _keyboardState.IsKeyDown(Keys.RightShift) ? 0.5f : 0.1f;
                if (Mods.SpectatorMode.IsSpectating) moveStep *= SpectatorCamera.SpeedScale;
                float rotStepDeg = _keyboardState.IsKeyDown(Keys.LeftShift) || _keyboardState.IsKeyDown(Keys.RightShift) ? 3 : 1.5f;
                float rotStep = MathHelper.DegreesToRadians(rotStepDeg);
                if (_keyboardState.IsKeyDown(Keys.W)) // move forward
                {
                    _cameraPosition += _cameraFacing * moveStep;
                }
                else if (_keyboardState.IsKeyDown(Keys.S)) // move backward
                {
                    _cameraPosition -= _cameraFacing * moveStep;
                }
                if (_keyboardState.IsKeyDown(Keys.Space)) // move up
                {
                    _cameraPosition = _cameraPosition.WithY(_cameraPosition.Y + moveStep);
                }
                else if (_keyboardState.IsKeyDown(Keys.V)) // move down
                {
                    _cameraPosition = _cameraPosition.WithY(_cameraPosition.Y - moveStep);
                }
                if (_keyboardState.IsKeyDown(Keys.A)) // move left
                {
                    _cameraPosition -= _cameraRight * moveStep;
                }
                else if (_keyboardState.IsKeyDown(Keys.D)) // move right
                {
                    _cameraPosition += _cameraRight * moveStep;
                }
                if (_keyboardState.IsKeyDown(Keys.Left) || _keyboardState.IsKeyDown(Keys.Right)
                    || _keyboardState.IsKeyDown(Keys.Up) || _keyboardState.IsKeyDown(Keys.Down))
                {
                    float stepH = 0;
                    float stepV = 0;
                    if (_keyboardState.IsKeyDown(Keys.Left)) // rotate left
                    {
                        stepH = -rotStep;
                    }
                    else if (_keyboardState.IsKeyDown(Keys.Right)) // rotate right
                    {
                        stepH = rotStep;
                    }
                    if (_keyboardState.IsKeyDown(Keys.Up)) // rotate up
                    {
                        stepV = rotStep;
                    }
                    else if (_keyboardState.IsKeyDown(Keys.Down)) // rotate down
                    {
                        stepV = -rotStep;
                    }
                    UpdateCameraRotation(stepH, stepV);
                }
            }
            else if (_cameraMode == CameraMode.Pivot)
            {
                float rotStep = _keyboardState.IsKeyDown(Keys.LeftShift) || _keyboardState.IsKeyDown(Keys.RightShift) ? -3 : -1.5f;
                if (_keyboardState.IsKeyDown(Keys.Up)) // rotate up
                {
                    _pivotAngleX += rotStep;
                    _pivotAngleX = Math.Clamp(_pivotAngleX, -90.0f, 90.0f);
                }
                else if (_keyboardState.IsKeyDown(Keys.Down)) // rotate down
                {
                    _pivotAngleX -= rotStep;
                    _pivotAngleX = Math.Clamp(_pivotAngleX, -90.0f, 90.0f);
                }
                if (_keyboardState.IsKeyDown(Keys.Left)) // rotate left
                {
                    _pivotAngleY += rotStep;
                    _pivotAngleY %= 360f;
                }
                else if (_keyboardState.IsKeyDown(Keys.Right)) // rotate right
                {
                    _pivotAngleY -= rotStep;
                    _pivotAngleY %= 360f;
                }
            }
        }

        private void UpdatePointModule()
        {
            if (World.SpecialEntities.PointModule == null)
            {
                if (World.TryGetEntity(PointModuleEntity.StartId, out EntityBase? entity) && entity is PointModuleEntity module)
                {
                    module.SetCurrent();
                }
            }
            else
            {
                PointModuleEntity? next = World.SpecialEntities.PointModule.Next ?? World.SpecialEntities.PointModule.Prev;
                if (next != null && next != World.SpecialEntities.PointModule)
                {
                    next.SetCurrent();
                }
                else
                {
                    if (World.TryGetEntity(PointModuleEntity.StartId, out EntityBase? entity) && entity is PointModuleEntity module)
                    {
                        module.SetCurrent();
                    }
                }
            }
        }

        private enum PromptState
        {
            None,
            Load,
            CameraPos
        }

        private PromptState _promptState = PromptState.None;
        private readonly ConcurrentQueue<(string Name, int Recolor, bool FirstHunt)> _loadQueue = new ConcurrentQueue<(string, int, bool)>();
        private readonly ConcurrentQueue<EntityBase> _unloadQueue = new ConcurrentQueue<EntityBase>();

        private readonly CancellationTokenSource _outputCts = new CancellationTokenSource();
        private string _currentOutput = "";
        private readonly StringBuilder _sb = new StringBuilder();

        private void OutputStart()
        {
            Task.Run(async () => await OutputUpdate(_outputCts.Token), _outputCts.Token);
        }

        private void OutputStop()
        {
            _outputCts.Cancel();
        }

        private async Task OutputUpdate(CancellationToken token)
        {
            if (OperatingSystem.IsAndroid()) return;
            while (!token.IsCancellationRequested)
            {
                if (_promptState == PromptState.Load)
                {
                    OutputLoadPrompt();
                    _promptState = PromptState.None;
                    _currentOutput = "";
                }
                else if (_promptState == PromptState.CameraPos)
                {
                    OutputCameraPrompt();
                    _promptState = PromptState.None;
                    _currentOutput = "";
                }
                string output = OutputGetAll();
                if (output != _currentOutput)
                {
                    Console.Clear(); // todo: this causes flickering
                    Console.WriteLine(output);
                    _currentOutput = output;
                }
                try
                {
                    await Task.Delay(100, token);
                }
                catch (TaskCanceledException) { }
            }
        }

        private void OutputLoadPrompt()
        {
            if (OperatingSystem.IsAndroid()) return;
            Console.Clear();
            Console.Write("Enter model name: ");
            string[] input = (Console.ReadLine() ?? "").Trim().Split(' ');
            string name = input[0].Trim();
            if (name.Length > 0)
            {
                int recolor = 0;
                bool firstHunt = false;
                if (input.Length > 1)
                {
                    if (UInt32.TryParse(input[1].Trim(), out uint value))
                    {
                        recolor = (int)value;
                    }
                    if (input.Length > 2)
                    {
                        firstHunt = input[2].Trim() == "-fh";
                    }
                }
                _loadQueue.Enqueue((name, recolor, firstHunt));
            }
        }

        private void OutputCameraPrompt()
        {
            if (OperatingSystem.IsAndroid()) return;
            Console.Clear();
            Console.Write("Enter camera position: ");
            string[] input = (Console.ReadLine() ?? "").Trim().Replace(",", "").Split(' ');
            float x = 0;
            float y = 0;
            float z = 0;
            for (int i = 0; i < input.Length && i < 3; i++)
            {
                string item = input[i];
                float coord = 0;
                if (item.StartsWith("0x"))
                {
                    if (Int32.TryParse(item.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out int value))
                    {
                        coord = value / 4096f;
                    }
                }
                else if (Single.TryParse(item, out float value))
                {
                    coord = value;
                }
                if (i == 0)
                {
                    x = coord;
                }
                else if (i == 1)
                {
                    y = coord;
                }
                else if (i == 2)
                {
                    z = coord;
                }
            }
            _cameraPosition = new Vector3(x, y, z);
        }

        private string OutputGetAll()
        {
            _sb.Clear();
            string recording = _recording ? " - Recording" : "";
            string frameAdvance = _frameAdvanceOn ? " - Frame Advance" : "";
            _sb.AppendLine($"{Mods.Branding.Name} {Mods.Branding.EngineVersion}{recording}{frameAdvance}");
            if (_showBotAiSlot >= 0 && _showBotAiSlot <= 3)
            {
                OutputGetBotAi();
            }
            else
            {
                if (_showCollision)
                {
                    OutputGetCollisionMenu();
                }
                if (Selection.Entity != null)
                {
                    OutputGetEntityInfo();
                    if (Selection.Instance != null)
                    {
                        OutputGetModel();
                        if (Selection.Node != null)
                        {
                            OutputGetNode();
                            if (Selection.Mesh != null)
                            {
                                OutputGetMesh();
                            }
                        }
                    }
                }
                else if (!_showCollision)
                {
                    OutputGetMenu();
                }
            }
            return _sb.ToString();
        }

        private void OutputGetBotAi()
        {
            PlayerEntity player = World.Players[_showBotAiSlot];
            _sb.AppendLine();
            _sb.AppendLine($"Bot AI slot: {_showBotAiSlot} (J: Next slot, Shift+J: Previous slot)");
            if (!player.LoadFlags.TestFlag(LoadFlags.SlotActive) && !player.LoadFlags.TestFlag(LoadFlags.Active))
            {
                _sb.AppendLine("none");
            }
            else if (!player.IsBot)
            {
                _sb.AppendLine($"Player - {player.Hunter}");
            }
            else
            {
                _sb.AppendLine($"Bot - {player.Hunter}");
                player.AiData.GetOuptut(_sb);
            }
        }

        private void OutputGetCollisionMenu()
        {
            _sb.AppendLine();
            _sb.AppendLine($"[{(_colMenuSelect == 0 ? "x" : " ")}] Entities ({ColEntDisplay})");
            _sb.AppendLine($"[{(_colMenuSelect == 1 ? "x" : " ")}] Terrain ({ColTerDisplay})");
            _sb.AppendLine($"[{(_colMenuSelect == 2 ? "x" : " ")}] Interaction ({ColTypeDisplay})");
            _sb.AppendLine($"[{(_colMenuSelect == 3 ? "x" : " ")}] Color mode ({ColDisplayColor})");
            _sb.AppendLine($"[{(_colMenuSelect == 4 ? "x" : " ")}] Opacity ({ColDisplayAlpha})");
            _sb.AppendLine();
            _sb.AppendLine("K: Next option, Shift+K: Previous option, Alt+K: Hide collision");
            _sb.AppendLine("J: Next value, Shift+J: Previous value, Ctrl+J: Reset value, Ctrl+K: Reset all");
        }

        private void OutputGetMenu()
        {
            _sb.AppendLine();
            if (_cameraMode == CameraMode.Pivot)
            {
                _sb.AppendLine(" - Scroll mouse wheel to zoom");
            }
            else if (_cameraMode == CameraMode.Roam)
            {
                _sb.AppendLine(" - Use WASD, Space, and V to move");
            }
            string volume = _showVolumes switch
            {
                VolumeDisplay.LightColor1 => "light sources, color 1",
                VolumeDisplay.LightColor2 => "light sources, color 2",
                VolumeDisplay.TriggerParent => "trigger volumes, parent event",
                VolumeDisplay.TriggerChild => "trigger volumes, child event",
                VolumeDisplay.AreaInside => "area volumes, inside event",
                VolumeDisplay.AreaExit => "area volumes, exit event",
                VolumeDisplay.MorphCamera => "morph cameras",
                VolumeDisplay.JumpPad => "jump pads",
                VolumeDisplay.Teleporter => "teleporters",
                VolumeDisplay.EnemyHurt => "enemy hurtboxes",
                VolumeDisplay.Object => "objects",
                VolumeDisplay.FlagBase => "flag bases",
                VolumeDisplay.DefenseNode => "defense nodes",
                VolumeDisplay.KillPlane => "kill plane",
                VolumeDisplay.PlayerLimit => "room limits (player)",
                VolumeDisplay.CameraLimit => "room limits (camera)",
                VolumeDisplay.NodeBounds => "room node bounds",
                VolumeDisplay.NodeData => "node data radius",
                VolumeDisplay.Portal => "portals",
                _ => "off"
            };
            string invisible = _showInvisible switch
            {
                2 => "all",
                1 => "placeholders",
                _ => "off"
            };
            string input = _inputMode switch
            {
                InputMode.PlayerOnly => "player only",
                InputMode.CameraOnly => "camera only",
                _ => "all",
            };
            _sb.AppendLine(" - Hold left mouse button or use arrow keys to rotate");
            _sb.AppendLine(" - Hold Shift to move the camera faster");
            _sb.AppendLine($" - T toggles texturing ({OnOff(_showTextures)})");
            _sb.AppendLine($" - Ctrl+C toggles vertex colors ({OnOff(_showColors)})");
            _sb.AppendLine($" - Ctrl+Q toggles wireframe ({OnOff(_wireframe)})");
            _sb.AppendLine($" - B toggles face culling ({OnOff(_faceCulling)})");
            _sb.AppendLine($" - F toggles texture filtering ({OnOff(FilteringOn)})");
            _sb.AppendLine($" - L toggles lighting ({OnOff(LightingOn)})");
            _sb.AppendLine($" - G toggles fog ({OnOff(FogOn)})");
            _sb.AppendLine($" - Shift+E toggles Scan Visor ({OnOff(_scanVisor)})");
            _sb.AppendLine($" - I toggles invisible entities ({invisible})");
            _sb.AppendLine($" - Z toggles volume display ({volume})");
            _sb.AppendLine($" - P switches camera mode ({(_cameraMode == CameraMode.Pivot ? "pivot" : "roam")})");
            _sb.AppendLine($" - Shift+P switches input mode ({input})");
            _sb.AppendLine(" - R resets the camera");
            _sb.AppendLine(" - Ctrl+O then enter \"model_name [recolor]\" to load");
            _sb.AppendLine(" - Ctrl+U then enter \"model_id\" to unload");
            _sb.AppendLine(" - Esc closes the viewer");
        }

        private void OutputGetEntityInfo()
        {
            EntityBase? entity = Selection.Entity;
            Debug.Assert(entity != null);
            _sb.AppendLine();
            if (World.RoomLoaded)
            {
                string string1 = $"{(int)(_light1Color.X * 255)};{(int)(_light1Color.Y * 255)};{(int)(_light1Color.Z * 255)}";
                string string2 = $"{(int)(_light2Color.X * 255)};{(int)(_light2Color.Y * 255)};{(int)(_light2Color.Z * 255)}";
                _sb.Append($"Room \u001b[38;2;{string1}m████\u001b[0m \u001b[38;2;{string2}m████\u001b[0m");
                _sb.AppendLine($" ({_light1Vector.X}, {_light1Vector.Y}, {_light1Vector.Z}) ({_light2Vector.X}, {_light2Vector.Y}, {_light2Vector.Z})");
            }
            else
            {
                _sb.AppendLine("No room loaded");
            }
            if (_outputCameraPos)
            {
                _sb.AppendLine($"Camera ({_cameraPosition.X}, {_cameraPosition.Y}, {_cameraPosition.Z})");
            }
            else
            {
                _sb.AppendLine($"Camera (?, ?, ?)");
            }
            _sb.AppendLine();
            _sb.Append($"Entity: {entity.Type}");
            IReadOnlyList<ModelInstance> models = entity.GetModels();
            if (entity.Type == EntityType.Model)
            {
                Debug.Assert(models.Count > 0);
                _sb.Append($" ({models[0].Model.Name})");
            }
            string color = "";
            if (models.Count > 0 && !models[0].IsPlaceholder)
            {
                color = $" - Color {entity.Recolor}";
            }
            _sb.Append($" [{entity.Id}] {(entity.Active ? "On " : "Off")}{color}");
            if (entity.Type == EntityType.Room)
            {
                _sb.Append($" ({entity.GetModels()[0].Model.Nodes.Count(n => n.RoomPartId >= 0)})");
            }
            else if (entity is LightSourceEntity light)
            {
                Vector3 color1 = light.Light1Color;
                Vector3 color2 = light.Light2Color;
                string string1 = $"{(int)(color1.X * 255)};{(int)(color1.Y * 255)};{(int)(color1.Z * 255)}";
                string string2 = $"{(int)(color2.X * 255)};{(int)(color2.Y * 255)};{(int)(color2.Z * 255)}";
                _sb.Append($" \u001b[38;2;{string1}m████\u001b[0m \u001b[38;2;{string2}m████\u001b[0m");
                _sb.Append($" {light.Light1Enabled} / {light.Light2Enabled}");
                Vector3 vector1 = light.Light1Vector;
                Vector3 vector2 = light.Light2Vector;
                _sb.Append($" ({vector1.X}, {vector1.Y}, {vector1.Z}) ({vector2.X}, {vector2.Y}, {vector2.Z})");
            }
            else if (entity is AreaVolumeEntity area)
            {
                EntityBase? parent = area.GetParent();
                EntityBase? child = area.GetChild();
                _sb.Append($" ({area.Data.TriggerFlags})");
                _sb.AppendLine();
                _sb.Append($"Entry: {area.Data.InsideMessage}");
                _sb.Append($", Param1: {area.Data.InsideMsgParam1}, Param2: {area.Data.InsideMsgParam2}");
                _sb.Append($", Target: {parent?.Type.ToString() ?? "None"} ({area.Data.ParentId})");
                _sb.AppendLine();
                _sb.Append($" Exit: {area.Data.ExitMessage}");
                _sb.Append($", Param1: {area.Data.ExitMsgParam1}, Param2: {area.Data.ExitMsgParam2}");
                _sb.Append($", Target: {child?.Type.ToString() ?? "None"} ({area.Data.ChildId})");
            }
            else if (entity is FhAreaVolumeEntity fhArea)
            {
                _sb.Append($" ({fhArea.Data.TriggerFlags})");
                _sb.AppendLine();
                _sb.Append($"Entry: {fhArea.Data.InsideMessage}");
                _sb.Append($", Param1: {fhArea.Data.InsideMsgParam1}, Param2: 0");
                _sb.AppendLine();
                _sb.Append($" Exit: {fhArea.Data.ExitMessage}");
                _sb.Append($", Param1: {fhArea.Data.ExitMsgParam1}, Param2: 0");
            }
            else if (entity is TriggerVolumeEntity trigger)
            {
                EntityBase? parent = trigger.GetParent();
                EntityBase? child = trigger.GetChild();
                _sb.Append($" ({trigger.Data.Subtype}");
                if (trigger.Data.Subtype == TriggerType.Threshold)
                {
                    _sb.Append($" x{trigger.Data.TriggerThreshold}");
                }
                _sb.Append(')');
                _sb.Append($" ({trigger.Data.TriggerFlags})");
                _sb.AppendLine();
                _sb.Append($"Parent: {trigger.Data.ParentMessage}");
                _sb.Append($", Param1: {trigger.Data.ParentMsgParam1}, Param2: {trigger.Data.ParentMsgParam2}");
                _sb.Append($", Target: {parent?.Type.ToString() ?? "None"} ({trigger.Data.ParentId})");
                _sb.AppendLine();
                _sb.Append($" Child: {trigger.Data.ChildMessage}");
                _sb.Append($", Param1: {trigger.Data.ChildMsgParam1}, Param2: {trigger.Data.ChildMsgParam2}");
                _sb.Append($", Target: {child?.Type.ToString() ?? "None"} ({trigger.Data.ChildId})");
            }
            else if (entity is FhTriggerVolumeEntity fhTrigger)
            {
                if (fhTrigger.Data.Subtype == FhTriggerType.Threshold)
                {
                    _sb.Append($" x{fhTrigger.Data.Threshold}");
                }
                _sb.Append($" ({fhTrigger.Data.TriggerFlags})");
                _sb.AppendLine();
                _sb.Append($"Parent: {fhTrigger.Data.ParentMessage}");
                _sb.Append($", Param1: {fhTrigger.Data.ParentMsgParam1}, Param2: 0");
                // rtodo: use entity fields for parent/child
                if (fhTrigger.Data.ParentMessage != FhMessage.None && World.TryGetEntity(fhTrigger.Data.ParentId, out EntityBase? parent))
                {
                    _sb.Append($", Target: {parent.Type} ({fhTrigger.Data.ParentId})");
                }
                else
                {
                    _sb.Append(", Target: None");
                }
                _sb.AppendLine();
                _sb.Append($" Child: {fhTrigger.Data.ChildMessage}");
                _sb.Append($", Param1: {fhTrigger.Data.ChildMsgParam1}, Param2: 0");
                if (fhTrigger.Data.ChildMessage != FhMessage.None && World.TryGetEntity(fhTrigger.Data.ChildId, out EntityBase? child))
                {
                    _sb.Append($", Target: {child.Type} ({fhTrigger.Data.ChildId})");
                }
                else
                {
                    _sb.Append(", Target: None");
                }
            }
            else if (entity is ItemSpawnEntity itemSpawn)
            {
                _sb.Append($" ({itemSpawn.Data.ItemType})");
            }
            else if (entity is ObjectEntity obj)
            {
                if (obj.Data.EffectId > 0)
                {
                    _sb.Append($" ({obj.Data.EffectId}, {Metadata.Effects[obj.Data.EffectId].Name})");
                }
            }
            else if (entity is CamSeqEntity cam)
            {
                _sb.Append($" (ID {cam.Data.SequenceId})");
            }
            else if (entity is PlayerEntity player)
            {
                _sb.Append($" (Health: {player.Health})");
            }
            _sb.AppendLine();
            _sb.AppendLine($"Position ({entity.Position.X}, {entity.Position.Y}, {entity.Position.Z})");
            _sb.AppendLine($"Rotation ({entity.Rotation.X}, {entity.Rotation.Y}, {entity.Rotation.Z})");
            _sb.AppendLine($"   Scale ({entity.Scale.X}, {entity.Scale.Y}, {entity.Scale.Z})");
        }

        private void OutputGetModel()
        {
            ModelInstance? inst = Selection.Instance;
            Debug.Assert(inst != null);
            _sb.AppendLine();
            _sb.AppendLine($"Model: {inst.Model.Name}, Scale: {inst.Model.Scale.X}, Active: {YesNo(inst.Active)}" +
                $"{(inst.IsPlaceholder ? ", Placeholder" : "")}");
            _sb.AppendLine($"Nodes {inst.Model.Nodes.Count}, Meshes {inst.Model.Meshes.Count}, Materials {inst.Model.Materials.Count}," +
                $" Textures {inst.Model.Recolors[0].Textures.Count}, Palettes {inst.Model.Recolors[0].Palettes.Count}");
            AnimationInfo a = inst.AnimInfo;
            AnimationGroups g = inst.Model.AnimationGroups;
            _sb.AppendLine($"Anim: {a.Index[0]}, {a.Frame[1]}" +
                $" (Node {(a.Node.Group?.Count > 0 ? a.NodeIndex : -1)} / {g.Node.Count}," +
                $" Mat {(a.Material.Group?.Count > 0 ? a.MaterialIndex : -1)} / {g.Material.Count}," +
                $" UV {(a.Texcoord.Group?.Count > 0 ? a.TexcoordIndex : -1)} / {g.Texcoord.Count}," +
                $" Tex {(a.Texture.Group?.Count > 0 ? a.TextureIndex : -1)} / {g.Texture.Count})");
        }

        private void OutputGetNode()
        {
            static string FormatNode(Model model, int otherId)
            {
                if (otherId == -1)
                {
                    return "None";
                }
                return $"{model.Nodes[otherId].Name} [{otherId}]";
            }
            Node? node = Selection.Node;
            ModelInstance? inst = Selection.Instance;
            Debug.Assert(node != null && inst != null);
            _sb.AppendLine();
            string mesh = $" - Meshes {node.MeshCount}";
            IEnumerable<int> meshIds = node.GetMeshIds().OrderBy(m => m);
            if (meshIds.Count() == 1)
            {
                mesh += $" ({meshIds.First()})";
            }
            else if (meshIds.Count() > 1)
            {
                mesh += $" ({meshIds.First()} - {meshIds.Last()})";
            }
            int index = inst.Model.Nodes.IndexOf(n => n == node);
            string enabled = node.Enabled ? (inst.Model.NodeParentsEnabled(node) ? "On " : "On*") : "Off";
            string billboard = node.BillboardMode != BillboardMode.None ? $" - {node.BillboardMode} Billboard" : "";
            _sb.AppendLine($"Node: {node.Name} [{index}] {enabled}{mesh}{billboard}");
            _sb.AppendLine($"Parent {FormatNode(inst.Model, node.ParentIndex)}");
            _sb.AppendLine($" Child {FormatNode(inst.Model, node.ChildIndex)}");
            _sb.AppendLine($"  Next {FormatNode(inst.Model, node.NextIndex)}");
            _sb.AppendLine($"Position ({node.Position.X}, {node.Position.Y}, {node.Position.Z})");
            _sb.AppendLine($"Rotation ({node.Angle.X}, {node.Angle.Y}, {node.Angle.Z})");
            _sb.AppendLine($"   Scale ({node.Scale.X}, {node.Scale.Y}, {node.Scale.Z})");
        }

        private void OutputGetMesh()
        {
            Mesh? mesh = Selection.Mesh;
            ModelInstance? inst = Selection.Instance;
            Debug.Assert(mesh != null && inst != null);
            int index = inst.Model.Meshes.IndexOf(n => n == mesh);
            _sb.AppendLine();
            _sb.AppendLine($"Mesh: [{index}] {(mesh.Visible ? "On " : "Off")} - " +
                $"Material ID {mesh.MaterialId}, DList ID {mesh.DlistId}");
            _sb.AppendLine();
            Material material = inst.Model.Materials[mesh.MaterialId];
            _sb.AppendLine($"Material: {material.Name} [{mesh.MaterialId}] - {material.RenderMode}, {material.PolygonMode}" +
                $" - {material.TexgenMode}");
            _sb.AppendLine($"Lighting {material.Lighting}, Alpha {material.Alpha}, " +
                $"XRepeat {material.XRepeat}, YRepeat {material.YRepeat}");
            _sb.AppendLine($"Texture ID {material.CurrentTextureId}, Palette ID {material.CurrentPaletteId}");
            _sb.AppendLine($"Diffuse ({material.Diffuse.Red}, {material.Diffuse.Green}, {material.Diffuse.Blue})" +
                $" Ambient ({material.Ambient.Red}, {material.Ambient.Green}, {material.Ambient.Blue})" +
                $" Specular ({material.Specular.Red}, {material.Specular.Green}, {material.Specular.Blue})");
        }

        private string OnOff(bool setting)
        {
            return setting ? "on" : "off";
        }

        private string YesNo(bool setting)
        {
            return setting ? "yes" : "no ";
        }
    }

    public class TextureMap : Dictionary<int, (int BindingId, bool OnlyOpaque)>
    {
        private int GetKey(int textureId, int paletteId, int recolorId)
        {
            if (paletteId == -1)
            {
                paletteId = 4095;
            }
            Debug.Assert(textureId >= 0 && textureId < 4096);
            Debug.Assert(paletteId >= 0 && paletteId < 4096);
            Debug.Assert(recolorId >= 0 && recolorId < 255);
            return textureId | (paletteId << 12) | (recolorId << 24);
        }

        public (int BindingId, bool OnlyOpaque) Get(int textureId, int paletteId, int recolorId)
        {
            return this[GetKey(textureId, paletteId, recolorId)];
        }

        public void Add(int textureId, int paletteId, int recolorId, int bindingId, bool onlyOpaque)
        {
            this[GetKey(textureId, paletteId, recolorId)] = (bindingId, onlyOpaque);
        }
    }
}
