using System;
using System.Collections.Generic;
using System.IO;
using MphRead;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Graphics.OpenGL;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Actual keyboard movement through the continuous native scheduler. The shared
// crouch capture helper provides body-hidden controls, repeat RGB and PNG hashes.
sealed partial class CheckWindow
{
    private readonly bool _crouchGaitCheck;
    private readonly List<object> _gaitSamples = new();
    private readonly List<object> _gaitSegments = new();
    private readonly Keys[] _gaitDirections = { Keys.W, Keys.S, Keys.A, Keys.D, Keys.W };
    private readonly string[] _gaitNames = { "forward", "backward", "left", "right", "held-forward-slide" };
    private int _gaitSegment, _gaitTick;
    private ulong _gaitLastFrame;
    private Vector3 _gaitStart, _gaitPrevious, _gaitCameraOffset;
    private double _gaitDistance, _gaitCycle;
    private double? _gaitPreviousPhase;
    private int _gaitPictures;
    private readonly HashSet<int> _gaitPhaseBins = new();

    private void InitializeCrouchGaitCheck()
    {
        var controls = Scene.Players.Main.Controls;
        controls.MoveUp.Key = Keys.W; controls.MoveDown.Key = Keys.S;
        controls.MoveLeft.Key = Keys.A; controls.MoveRight.Key = Keys.D;
        Title = "Traversal crouch walk and held-forward slide renderer check";
    }

    private void BeforeCrouchGaitFrame()
    {
        BeforeCrouchRenderFrame();
        bool ready = _crouchStanding != null;
        bool moving = ready && (_gaitSegment == 4 ? _gaitTick >= 45 : _gaitTick >= 60);
        foreach (Keys key in new[] { Keys.W, Keys.S, Keys.A, Keys.D })
            _crouchKey(_crouchKeyboard, key, moving && key == _gaitDirections[_gaitSegment]);
        _crouchKey(_crouchKeyboard, Keys.LeftControl, ready
            && (_gaitSegment == 4 ? _gaitTick >= 75 : _gaitTick >= 45));
        if (ready) FollowCrouchGaitCamera();
    }

    private void FollowCrouchGaitCamera()
    {
        _crouchAim = Scene.Players.Main.Position + Vector3.UnitY * .3f;
        _crouchCamera = _crouchAim + _gaitCameraOffset;
        SetCrouchCamera();
    }

    private void AfterCrouchGaitFrame()
    {
        PlayerEntity player = Scene.Players.Main;
        if (!player.LoadFlags.TestFlag(LoadFlags.Spawned)) return;
        if (_crouchSpawnFrame == null)
        {
            _crouchSpawnFrame = Scene.FrameCount; Scene.CameraSequences.Current?.End(); return;
        }
        if (Scene.FrameCount - _crouchSpawnFrame.Value < 120) return;
        CrouchRequire(player.Health > 0 && !player.IsAltForm && !player.IsMorphing && !player.IsUnmorphing,
            "gait capture requires a living native biped");
        CrouchRequire((bool)CrouchGet(player, "ModMomentumEligible")!, "genuine Traversal movement admission");
        if (_crouchStanding == null)
        {
            ChooseCrouchCamera(); _gaitCameraOffset = _crouchCamera - _crouchAim;
            SetCrouchCamera(); _crouchStanding = CaptureCrouchSilhouette("gait-standing");
            StartCrouchGaitSegment(); return;
        }
        if (Scene.FrameCount == _gaitLastFrame) return;
        CrouchRequire(Scene.FrameCount == _gaitLastFrame + 1, "gait scheduler must not skip completed frames");
        _gaitLastFrame = Scene.FrameCount; _gaitTick++;
        _gaitDistance += (player.Position - _gaitPrevious).Xz.Length; _gaitPrevious = player.Position;
        FollowCrouchGaitCamera();
        bool slide = _gaitSegment == 4;
        bool measuring = slide ? _gaitTick >= 78 && _gaitTick <= 90 : _gaitTick >= 66;
        if (measuring)
        {
            object gait = CrouchGet(player, "_modCrouchGait")!;
            object walk = CrouchGet(player, "_modCrouchWalk")!;
            double phase = (double)CrouchGet(walk, "_phase")!;
            if (_gaitPreviousPhase is double previous)
            {
                double delta = phase - previous;
                if (delta < -.5) delta += 1;
                CrouchRequire(delta >= -.00001 && delta < .5, "continuous visual gait phase");
                _gaitCycle += delta;
            }
            _gaitPreviousPhase = phase;
            if (!slide && _gaitTick % 6 == 0 || slide && _gaitTick % 2 == 0)
            {
                CrouchRequire(player.Controls.Crouch.IsDown && (bool)CrouchGet(player, "ModCrouched")!,
                    "actual held Ctrl earns crouched capture");
                if (slide) CrouchRequire(player.Controls.MoveUp.IsDown && (bool)CrouchGet(player, "ModSliding")!,
                    "slide capture retains actual held W and native slide timer");
                string label = $"gait-{_gaitNames[_gaitSegment]}-{_gaitTick:D3}";
                object? canonicalClipBefore = CrouchGet(player, "Biped1Anim");
                object? canonicalFrameBefore = CrouchGet(player, "Biped1Frame");
                ulong sourceFrame = Scene.FrameCount;
                CaptureCrouchSilhouette(label);
                CrouchRequire(sourceFrame == Scene.FrameCount
                    && Equals(canonicalClipBefore, CrouchGet(player, "Biped1Anim"))
                    && Equals(canonicalFrameBefore, CrouchGet(player, "Biped1Frame")),
                    "frozen captures retain source frame and canonical animation cursor");
                object lower = CrouchGet(player, "_modCrouchLowerAnimation")!;
                int visualClip = ((int[])CrouchGet(lower, "Index")!)[0];
                int visualFrame = ((int[])CrouchGet(lower, "Frame")!)[0];
                _gaitPhaseBins.Add(Math.Clamp((int)(phase * 20), 0, 19));
                CrouchRequire(visualClip == (int)PlayerAnimation.Idle && visualFrame == 0,
                    "dedicated low footwork and slide sample a neutral native Idle0 lower skeleton");
                _gaitSamples.Add(new { label, sourceFrame, simulationFrame = Scene.FrameCount, phase,
                    canonicalClip = CrouchGet(player, "Biped1Anim")?.ToString(), canonicalFrame = CrouchGet(player, "Biped1Frame"),
                    visualClip, visualFrame, proceduralWalkAmount = CrouchGet(walk, "_amount"), sliding = CrouchGet(player, "ModSliding"),
                    gliding = CrouchGet(gait, "Gliding"), position = CrouchVector(player.Position),
                    durationSeconds = _gaitTick / 60d, distance = _gaitDistance, completedCycles = _gaitCycle });
                _gaitPictures++;
            }
        }
        if (slide ? _gaitTick >= 90 : _gaitCycle >= 1.05 && _gaitPictures >= 5)
        {
            CrouchRequire(slide || _gaitPhaseBins.Count >= 3, "walking cycle captures multiple distinct procedural phase bins");
            CrouchRequire(_gaitDistance > .1 && _gaitPictures >= 5, "actual travel and representative moving body captures required");
            _gaitSegments.Add(new { name = _gaitNames[_gaitSegment], durationSeconds = _gaitTick / 60d,
                distance = _gaitDistance, completedCycles = _gaitCycle, pictures = _gaitPictures,
                start = CrouchVector(_gaitStart), end = CrouchVector(player.Position) });
            if (++_gaitSegment == _gaitNames.Length) { Complete = true; Close(); return; }
            StartCrouchGaitSegment();
        }
        CrouchRequire(_gaitTick < 360, "full walking cycle not observed; choose an unobstructed native spawn/room");
        CrouchRequire(GL.GetError() == OpenTK.Graphics.OpenGL.ErrorCode.NoError, "no GL error during crouch gait capture");
    }

    private void StartCrouchGaitSegment()
    {
        _gaitTick = 0; _gaitLastFrame = Scene.FrameCount;
        _gaitStart = _gaitPrevious = Scene.Players.Main.Position;
        _gaitPhaseBins.Clear();
        _gaitDistance = _gaitCycle = 0; _gaitPreviousPhase = null; _gaitPictures = 0;
    }

    internal object CrouchGaitRenderResult() => new { schema = 1, status = Complete ? "PASS" : "FAIL", checkReady = Complete,
        scope = "Windows native continuous 60 Hz renderer; actual Ctrl+W/S/A/D full walk cycles and running held-W+Ctrl slide; external camera follows actual body; frozen body-hidden negative controls and exact RGB repeats; no actor state writes; straight support-foot planting is measured separately by CPU; turning and remote smoothing are not a world-foot-lock claim",
        scheduler = "production FrameTiming.Advance; presentation generation fence preserved",
        segments = _gaitSegments, samples = _gaitSamples, pictures = _crouchPictures,
        focusEvents = _crouchFocusEvents, continuousPresentationGeneration = _crouchContinuousGeneration,
        glVendor = _glVendor, glRenderer = _glRenderer, glVersion = _glVersion,
        clientMvid = typeof(Scene).Module.ModuleVersionId, room = _room };
}
