using System.Collections.Immutable;
using System.Diagnostics;
using MphRead;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;
using ProjectPrime.Editor.Commands;
using ProjectPrime.Editor.Documents;
using ProjectPrime.Editor.Panels;
using ProjectPrime.Editor.Playtest;
using ProjectPrime.Editor.Viewport;

namespace ProjectPrime.Editor.App;

public sealed class EditorApplication
{
    private enum TransformMode { Move, Rotate, Scale }

    private readonly MapDocument _document;
    private readonly EditorBuildService _builds;
    private readonly EditorViewport _viewport = new();
    private readonly ImmediateEditorUi _ui = new();
    private readonly MapPlaytestService _playtest = new();
    private readonly AutosaveService _autosave;
    private ImmutableArray<MapDiagnostic> _diagnostics = [];
    private MapBuildStatistics? _statistics;
    private Task<MapBuildResult>? _pendingBuild;
    private Task<MapBundleWriteResult>? _pendingExport;
    private Task<MapBuildResult>? _pendingPlay;
    private string? _pendingPlaySnapshot;
    private bool _pendingPlayBots;
    private string _status = "READY";
    private TransformMode _transformMode;
    private Guid? _previewRequest;
    private string? _previewPath;
    private long _frameNumber;
    private string? _recoveryPath;

    public EditorApplication(MapDocument document, EditorBuildService builds)
    {
        _document = document;
        _builds = builds;
        _autosave = new AutosaveService(Path.Combine(MapStoragePaths.DataRoot, "maps", "autosave"));
        _recoveryPath = _autosave.FindRecovery(document);
        Validate();
        if (_recoveryPath != null) _status = "RECOVERED VERSION AVAILABLE";
    }

    public int Run()
    {
        using var surface = new SdlRenderSurface(new Vector2i(1440, 900), Title());
        var clock = Stopwatch.StartNew();
        double previous = clock.Elapsed.TotalSeconds;
        while (!surface.Closed)
        {
            RenderSurfaceInput input = surface.PumpEvents();
            if (input.Pressed(RenderSurfaceKey.Escape)) surface.Close();
            double now = clock.Elapsed.TotalSeconds;
            float elapsed = (float)Math.Clamp(now - previous, 0, 0.1);
            previous = now;
            CompleteBuild();
            CompleteExport();
            CompletePlay();
            HandleKeyboard(input);
            EditorViewportLayout viewportLayout = _ui.Viewport(
                surface.LogicalSize, surface.FramebufferSize);
            _viewport.Update(_document, input, viewportLayout, elapsed);
            RenderFrame frame = _viewport.BuildFrame(_document, surface.FramebufferSize,
                viewportLayout);
            if (_previewRequest is Guid requestId)
            {
                frame.AddCaptureRequest(new RenderCaptureRequest(requestId, _frameNumber,
                    CaptureTargetKind.SceneTarget, viewportLayout.PixelRect.Width,
                    viewportLayout.PixelRect.Height,
                    CapturePixelFormat.Rgb8, CaptureRowOrientation.TopDown,
                    CaptureDeliveryKind.Screenshot, _previewPath));
                _previewRequest = null;
            }
            EditorPresentationStages.BeginHudOverlay(frame);
            string? action = _ui.Draw(frame, surface.LogicalSize, input, _document,
                _diagnostics, _statistics, _status, _recoveryPath != null);
            EditorPresentationStages.Complete(frame);
            if (action != null) HandleAction(action);
            frame.Seal();
            surface.Render(frame);
            DrainCaptures(surface);
            _frameNumber++;
            _autosave.Tick(_document, TimeSpan.FromSeconds(30));
            surface.SetTitle(Title());
            Thread.Sleep(1);
        }
        DeletePendingPlaySnapshot();
        return 0;
    }

    private string Title() => $"Project Prime Editor - {_document.Project.Metadata.Name}"
        + (_document.IsDirty ? " *" : "");

    private void HandleAction(string action)
    {
        switch (action)
        {
            case "save": Save(); break;
            case "build": BeginBuild(force: true); break;
            case "export": Export(); break;
            case "play-solo": Play(bots: false); break;
            case "play-bots": Play(bots: true); break;
            case "preview": GeneratePreview(); break;
            case "undo": _document.Undo(); Validate(); break;
            case "redo": _document.Redo(); Validate(); break;
            case "box": AddBrush(MapPrimitiveKind.Box); break;
            case "wedge": AddBrush(MapPrimitiveKind.Wedge); break;
            case "cylinder": AddBrush(MapPrimitiveKind.Cylinder); break;
            case "stairs": AddBrush(MapPrimitiveKind.Stairs); break;
            case "spawn": AddEntity(MapEntityKind.PlayerSpawn); break;
            case "item": AddEntity(MapEntityKind.ItemSpawn); break;
            case "jump": AddEntity(MapEntityKind.JumpPad); break;
            case "damage": AddEntity(MapEntityKind.DamageVolume); break;
            case "kill": AddEntity(MapEntityKind.KillVolume); break;
            case "team-spawn": AddEntity(MapEntityKind.TeamSpawn); break;
            case "capture-base": AddEntity(MapEntityKind.CaptureBase); break;
            case "bounty-base": AddEntity(MapEntityKind.BountyBase); break;
            case "node": AddEntity(MapEntityKind.NodeObjective); break;
            case "x-": NudgeSelected(new(-1, 0, 0)); break;
            case "x+": NudgeSelected(new(1, 0, 0)); break;
            case "y-": NudgeSelected(new(0, -1, 0)); break;
            case "y+": NudgeSelected(new(0, 1, 0)); break;
            case "z-": NudgeSelected(new(0, 0, -1)); break;
            case "z+": NudgeSelected(new(0, 0, 1)); break;
            case "face-next": SelectNextFace(); break;
            case "material-next": SelectNextMaterial(faceOnly: false); break;
            case "material-face-next": SelectNextMaterial(faceOnly: true); break;
            case "brush-solid": ToggleSelectedBrushSolid(); break;
            case "material-tiling-down": ModifySelectedMaterial("Change Material Tiling",
                value => value.Tiling = Math.Max(0.25f, value.Tiling / 2)); break;
            case "material-tiling-up": ModifySelectedMaterial("Change Material Tiling",
                value => value.Tiling = Math.Min(4096, value.Tiling * 2)); break;
            case "face-scale-down": ModifySelectedFace("Change Face UV Scale",
                value => value.TextureScale = Math.Max(0.0625f, value.TextureScale / 2)); break;
            case "face-scale-up": ModifySelectedFace("Change Face UV Scale",
                value => value.TextureScale = Math.Min(64, value.TextureScale * 2)); break;
            case "face-rotate": ModifySelectedFace("Rotate Face UV",
                value => value.TextureRotation = (value.TextureRotation + 15) % 360); break;
            case "face-offset-x-down": ModifySelectedFace("Offset Face UV",
                value => value.TextureOffset[0] -= 0.25f); break;
            case "face-offset-x-up": ModifySelectedFace("Offset Face UV",
                value => value.TextureOffset[0] += 0.25f); break;
            case "face-offset-y-down": ModifySelectedFace("Offset Face UV",
                value => value.TextureOffset[1] -= 0.25f); break;
            case "face-offset-y-up": ModifySelectedFace("Offset Face UV",
                value => value.TextureOffset[1] += 0.25f); break;
            case "material-terrain": CycleSelectedMaterialTerrain(); break;
            case "team-next": SelectNextTeam(); break;
            case "toggle-collision": ToggleOverlay(settings => settings.ShowCollision = !settings.ShowCollision); break;
            case "toggle-entities": ToggleOverlay(settings => settings.ShowEntities = !settings.ShowEntities); break;
            case "toggle-bounds": ToggleOverlay(settings => settings.ShowWorldBounds = !settings.ShowWorldBounds); break;
            case "recover": Recover(); break;
            case "discard-recovery": DiscardRecovery(); break;
            case "mode-battle": ToggleMode(MapMode.Battle); break;
            case "mode-survival": ToggleMode(MapMode.Survival); break;
            case "mode-capture": ToggleMode(MapMode.Capture); break;
            case "mode-bounty": ToggleMode(MapMode.Bounty); break;
            case "mode-nodes": ToggleMode(MapMode.Nodes); break;
            case "environment-fog": ModifyEnvironment(value => value.FogEnabled = !value.FogEnabled); break;
            case "environment-kill-down": ModifyEnvironment(value => value.KillHeight -= 1); break;
            case "environment-kill-up": ModifyEnvironment(value => value.KillHeight += 1); break;
            case "environment-clip-down": ModifyEnvironment(value => value.FarClip = Math.Max(25, value.FarClip - 25)); break;
            case "environment-clip-up": ModifyEnvironment(value => value.FarClip += 25); break;
            case "view-perspective": _viewport.Camera.SetMode(EditorViewMode.Perspective); break;
            case "view-top": _viewport.Camera.SetMode(EditorViewMode.Top); break;
            case "view-front": _viewport.Camera.SetMode(EditorViewMode.Front); break;
            case "view-side": _viewport.Camera.SetMode(EditorViewMode.Side); break;
        }
    }

    private void HandleKeyboard(RenderSurfaceInput input)
    {
        if (input.Pressed(RenderSurfaceKey.Delete) && _document.SelectedObjectId is { } selected)
        {
            _document.Execute(new DeleteObjectCommand(_document, selected));
            _document.SelectedObjectId = null;
            Validate();
        }
        if (input.Down(RenderSurfaceKey.LeftControl) || input.Down(RenderSurfaceKey.RightControl))
        {
            if (input.Pressed(RenderSurfaceKey.Z)) { _document.Undo(); Validate(); }
            if (input.Pressed(RenderSurfaceKey.Y)) { _document.Redo(); Validate(); }
        }
        if (input.Pressed(RenderSurfaceKey.W)) _transformMode = TransformMode.Move;
        if (input.Pressed(RenderSurfaceKey.E)) _transformMode = TransformMode.Rotate;
        if (input.Pressed(RenderSurfaceKey.R)) _transformMode = TransformMode.Scale;
        Vector3 delta = Vector3.Zero;
        if (input.Pressed(RenderSurfaceKey.Left)) delta.X--;
        if (input.Pressed(RenderSurfaceKey.Right)) delta.X++;
        if (input.Pressed(RenderSurfaceKey.Up)) delta.Z--;
        if (input.Pressed(RenderSurfaceKey.Down)) delta.Z++;
        if (delta != Vector3.Zero) TransformSelected(delta);
    }

    private void TransformSelected(Vector3 delta)
    {
        if (_document.SelectedObjectId is not { } id || _document.Project.Authoring is not { } scene)
            return;
        MapTransform? current = scene.Brushes.FirstOrDefault(value => value.Id == id)?.Transform
            ?? scene.Entities.FirstOrDefault(value => value.Id == id)?.Transform;
        if (current == null) return;
        var next = new MapTransform
        {
            Position = (float[])current.Position.Clone(),
            Rotation = (float[])current.Rotation.Clone(),
            Scale = (float[])current.Scale.Clone()
        };
        MapEditorSettings settings = scene.Editor;
        if (_transformMode == TransformMode.Move)
        {
            next.Position[0] += delta.X * settings.PositionSnap;
            next.Position[2] += delta.Z * settings.PositionSnap;
        }
        else if (_transformMode == TransformMode.Rotate)
            next.Rotation[1] += (delta.X + delta.Z) * settings.RotationSnap;
        else
        {
            float value = (delta.X + delta.Z) * settings.ScaleSnap;
            for (int index = 0; index < 3; index++) next.Scale[index] = Math.Max(0.01f, next.Scale[index] + value);
        }
        _document.Execute(new TransformObjectCommand(_document, id, next));
        Validate();
    }

    private void NudgeSelected(Vector3 delta)
    {
        TransformMode previous = _transformMode;
        _transformMode = TransformMode.Move;
        TransformSelected(delta);
        _transformMode = previous;
    }

    private void SelectNextFace()
    {
        if (_document.SelectedObjectId is not { } id || _document.Project.Authoring is not { } scene)
            return;
        ConvexBrush? brush = scene.Brushes.FirstOrDefault(value => value.Id == id);
        if (brush == null || brush.Faces.Count == 0) return;
        _document.SelectedFaceIndex = ((_document.SelectedFaceIndex ?? -1) + 1) % brush.Faces.Count;
    }

    private void SelectNextMaterial(bool faceOnly)
    {
        if (_document.SelectedObjectId is not { } id || _document.Project.Authoring is not { } scene
            || scene.Materials.Count == 0) return;
        ConvexBrush? brush = scene.Brushes.FirstOrDefault(value => value.Id == id);
        if (brush == null) return;
        int? face = faceOnly ? _document.SelectedFaceIndex ?? 0 : null;
        if (face >= brush.Faces.Count) face = 0;
        string current = face.HasValue
            ? brush.Faces[face.Value].MaterialId
            : brush.Faces.FirstOrDefault()?.MaterialId ?? scene.Materials[0].Id;
        int index = scene.Materials.FindIndex(value => value.Id == current);
        string next = scene.Materials[(index + 1 + scene.Materials.Count) % scene.Materials.Count].Id;
        _document.Execute(new ChangeMaterialCommand(_document, id, next, face));
        Validate();
    }

    private void ToggleSelectedBrushSolid()
    {
        if (_document.SelectedObjectId is not { } id) return;
        _document.Execute(new DeltaEditorCommand<bool>(_document, "Toggle Brush Collision",
            EditorChangeKind.Geometry,
            project => project.Authoring!.Brushes.Single(value => value.Id == id).Solid,
            (project, value) => project.Authoring!.Brushes
                .Single(item => item.Id == id).Solid = value,
            static value => value, static value => !value));
        Validate();
    }

    private void ModifySelectedMaterial(string commandName, Action<MapAuthoringMaterial> change)
    {
        if (_document.SelectedObjectId is not { } id || _document.Project.Authoring is not { } scene)
            return;
        ConvexBrush? selected = scene.Brushes.FirstOrDefault(value => value.Id == id);
        if (selected == null || selected.Faces.Count == 0) return;
        int face = Math.Clamp(_document.SelectedFaceIndex ?? 0, 0, selected.Faces.Count - 1);
        string materialId = selected.Faces[face].MaterialId;
        _document.Execute(new DeltaEditorCommand<MapAuthoringMaterial>(_document, commandName,
            EditorChangeKind.Material,
            project => project.Authoring!.Materials.Single(value => value.Id == materialId),
            (project, value) =>
            {
                List<MapAuthoringMaterial> materials = project.Authoring!.Materials;
                materials[materials.FindIndex(item => item.Id == materialId)] = value;
            }, EditorCommandCopies.Material,
            value => { change(value); return value; }, approximateMemoryBytes: 512));
        Validate();
    }

    private void ModifySelectedFace(string commandName, Action<ConvexBrushFace> change)
    {
        if (_document.SelectedObjectId is not { } id || _document.Project.Authoring is not { } scene)
            return;
        ConvexBrush? selected = scene.Brushes.FirstOrDefault(value => value.Id == id);
        if (selected == null || selected.Faces.Count == 0) return;
        int face = Math.Clamp(_document.SelectedFaceIndex ?? 0, 0, selected.Faces.Count - 1);
        _document.Execute(new DeltaEditorCommand<ConvexBrushFace>(_document, commandName,
            EditorChangeKind.Material,
            project => project.Authoring!.Brushes.Single(value => value.Id == id).Faces[face],
            (project, value) => project.Authoring!.Brushes
                .Single(item => item.Id == id).Faces[face] = value,
            EditorCommandCopies.Face,
            value => { change(value); return value; }, approximateMemoryBytes: 256));
        Validate();
    }

    private void CycleSelectedMaterialTerrain()
    {
        string[] terrains = ["Metal", "OrangeHolo", "GreenHolo", "BlueHolo", "Ice",
            "Snow", "Sand", "Rock", "Lava", "Acid", "Gorea"];
        ModifySelectedMaterial("Change Material Terrain", material =>
        {
            int index = Array.IndexOf(terrains, material.Terrain);
            material.Terrain = terrains[(index + 1 + terrains.Length) % terrains.Length];
        });
    }

    private void SelectNextTeam()
    {
        if (_document.SelectedObjectId is not { } id) return;
        _document.Execute(new DeltaEditorCommand<int>(_document, "Change Team",
            EditorChangeKind.Entity,
            project => project.Authoring!.Entities.Single(value => value.Id == id).Team,
            (project, value) => project.Authoring!.Entities
                .Single(item => item.Id == id).Team = value,
            static value => value, static value => value == 0 ? 1 : 0));
        Validate();
    }

    private void ToggleMode(MapMode mode)
    {
        _document.Execute(new DeltaEditorCommand<List<MapMode>>(_document, "Toggle Mode",
            EditorChangeKind.Metadata, project => project.SupportedModes,
            (project, value) => project.SupportedModes = value,
            static value => [.. value], value =>
            {
                if (!value.Remove(mode)) value.Add(mode);
                value.Sort();
                return value;
            }, approximateMemoryBytes: 256));
        Validate();
    }

    private void ToggleOverlay(Action<MapEditorSettings> change)
    {
        _document.Execute(new DeltaEditorCommand<MapEditorSettings>(_document, "Toggle Overlay",
            EditorChangeKind.Overlay,
            project => (project.Authoring ?? throw new InvalidOperationException(
                "Project is not natively editable.")).Editor,
            (project, value) => project.Authoring!.Editor = value,
            EditorCommandCopies.Settings,
            value => { change(value); return value; }, approximateMemoryBytes: 256));
        Validate();
    }

    private void ModifyEnvironment(Action<MapEnvironment> change)
    {
        _document.Execute(new DeltaEditorCommand<MapEnvironment>(_document, "Change Environment",
            EditorChangeKind.Environment,
            project => project.Environment ?? MapEnvironment.From(project.Map),
            (project, value) =>
            {
                project.Environment = value;
                value.ApplyTo(project.Map);
            }, EditorCommandCopies.Environment,
            value => { change(value); return value; }, approximateMemoryBytes: 512));
        Validate();
    }

    private void Recover()
    {
        if (_recoveryPath == null) return;
        string path = _recoveryPath;
        _document.Recover(path);
        _autosave.Discard(path);
        _recoveryPath = null;
        Validate();
        _status = "RECOVERED AUTOSAVE - SAVE TO KEEP IT";
    }

    private void DiscardRecovery()
    {
        if (_recoveryPath == null) return;
        _autosave.Discard(_recoveryPath);
        _recoveryPath = null;
        _status = "AUTOSAVE DISCARDED";
    }

    private void AddBrush(MapPrimitiveKind kind)
    {
        string id = NextId("brush");
        IReadOnlyList<ConvexBrush> brushes = kind switch
        {
            MapPrimitiveKind.Wedge => [ConvexBrushFactory.Wedge(id, "material.default", new(3, 2, 3))],
            MapPrimitiveKind.Cylinder => [ConvexBrushFactory.Cylinder(id, "material.default", 1.5f, 2)],
            MapPrimitiveKind.Stairs => ConvexBrushFactory.Stairs(id, "material.default", new(4, 3, 6)),
            _ => [ConvexBrushFactory.Box(id, "material.default", new(3, 2, 3))]
        };
        foreach (ConvexBrush brush in brushes)
        {
            brush.Transform.Position = [_viewport.Camera.Position.X + _viewport.Camera.Forward.X * 5,
                1, _viewport.Camera.Position.Z + _viewport.Camera.Forward.Z * 5];
            _document.Execute(new CreateBrushCommand(_document, brush));
            _document.SelectedObjectId = brush.Id;
        }
        Validate();
    }

    private void AddEntity(MapEntityKind kind)
    {
        string id = NextId(kind switch
        {
            MapEntityKind.PlayerSpawn => "spawn",
            MapEntityKind.TeamSpawn => "team-spawn",
            MapEntityKind.ItemSpawn => "item",
            MapEntityKind.JumpPad => "jump",
            MapEntityKind.DamageVolume => "damage",
            MapEntityKind.KillVolume => "kill",
            MapEntityKind.CaptureBase => "capture-base",
            MapEntityKind.BountyBase => "bounty-base",
            MapEntityKind.NodeObjective => "node",
            _ => "entity"
        });
        Vector3 location = _viewport.Camera.Position + _viewport.Camera.Forward * 5;
        var entity = new MapEntityDefinition
        {
            Id = id, Kind = kind,
            Transform = new MapTransform { Position = [location.X, location.Y, location.Z] },
            Target = kind == MapEntityKind.JumpPad ? [location.X, location.Y + 4, location.Z + 6] : null,
            Team = kind is MapEntityKind.TeamSpawn or MapEntityKind.CaptureBase ? 0 : -1,
            DamagePerTick = kind == MapEntityKind.KillVolume ? 1000
                : kind == MapEntityKind.DamageVolume ? 10 : 0,
            Size = kind == MapEntityKind.NodeObjective ? [4, 2, 4] : [1, 1, 1]
        };
        _document.Execute(new CreateEntityCommand(_document, entity));
        _document.SelectedObjectId = id;
        Validate();
    }

    private string NextId(string prefix)
    {
        HashSet<string> used = _document.Project.Authoring == null ? []
            : [.. _document.Project.Authoring.Brushes.Select(value => value.Id),
                .. _document.Project.Authoring.Entities.Select(value => value.Id)];
        for (int index = 1; ; index++)
        {
            string candidate = $"{prefix}.{index}";
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private void Save()
    {
        string path = _document.Path ?? Path.Combine(MapStoragePaths.Projects,
            _document.Project.StableId, "map.project.json");
        _document.Save(path);
        _status = $"SAVED {path}";
    }

    private void BeginBuild(bool force)
    {
        if (_pendingBuild is { IsCompleted: false } || _pendingExport is { IsCompleted: false }
            || _pendingPlay is { IsCompleted: false }) return;
        Save();
        _status = "BUILDING";
        _pendingBuild = _builds.BuildAsync(_document.Project, force, CancellationToken.None,
            new Progress<MapBuildProgress>(progress =>
            {
                int percent = progress.TotalStages == 0 ? 0
                    : progress.CompletedStages * 100 / progress.TotalStages;
                _status = $"{progress.Stage.ToUpperInvariant()} {percent}%";
            }));
    }

    private void CompleteBuild()
    {
        if (_pendingBuild is not { IsCompleted: true } task) return;
        _pendingBuild = null;
        try
        {
            MapBuildResult result = task.GetAwaiter().GetResult();
            _diagnostics = result.Diagnostics;
            _statistics = result.Statistics;
            _status = result.Success
                ? $"{(result.CacheHit ? "CACHE HIT" : "BUILD COMPLETE")} {result.BuildFingerprint[..12]}"
                : "BUILD FAILED";
        }
        catch (Exception exception)
        {
            _diagnostics = [new("MAP-EDT-999", MapDiagnosticSeverity.Error, exception.Message)];
            _status = "BUILD FAILED";
        }
    }

    private void Export()
    {
        if (_pendingExport is { IsCompleted: false } || _pendingBuild is { IsCompleted: false }
            || _pendingPlay is { IsCompleted: false }) return;
        try
        {
            Save();
            string destination = Path.ChangeExtension(_document.Path!, MapBundle.Extension);
            _status = "EXPORTING";
            _pendingExport = _builds.ExportAsync(_document.Project, _document.Path!, destination,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _diagnostics = [new("MAP-EDT-003", MapDiagnosticSeverity.Error, exception.Message)];
            _status = "EXPORT FAILED";
        }
    }

    private void CompleteExport()
    {
        if (_pendingExport is not { IsCompleted: true } task) return;
        _pendingExport = null;
        try
        {
            MapBundleWriteResult result = task.GetAwaiter().GetResult();
            _status = $"EXPORTED {Path.GetFileName(result.Path)} {result.ArtifactHash[..12]}";
        }
        catch (Exception exception)
        {
            _diagnostics = [new("MAP-EDT-003", MapDiagnosticSeverity.Error, exception.Message)];
            _status = "EXPORT FAILED";
        }
    }

    private void Play(bool bots)
    {
        if (_pendingPlay is { IsCompleted: false } || _pendingBuild is { IsCompleted: false }
            || _pendingExport is { IsCompleted: false }) return;
        try
        {
            string projectDirectory = _document.Path == null
                ? Path.Combine(MapStoragePaths.Projects, _document.Project.StableId)
                : Path.GetDirectoryName(_document.Path)!;
            Directory.CreateDirectory(projectDirectory);
            _pendingPlaySnapshot = Path.Combine(projectDirectory,
                $".project-prime-playtest-{Guid.NewGuid():N}.json");
            _document.SaveSnapshot(_pendingPlaySnapshot);
            MapProject snapshot = MapProjectIO.Load(_pendingPlaySnapshot);
            _pendingPlayBots = bots;
            _status = "PREPARING PLAYTEST";
            _pendingPlay = _builds.BuildAsync(snapshot, false, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _diagnostics = [new("MAP-EDT-004", MapDiagnosticSeverity.Error, exception.Message)];
            _status = "PLAYTEST FAILED";
            DeletePendingPlaySnapshot();
        }
    }

    private void CompletePlay()
    {
        if (_pendingPlay is not { IsCompleted: true } task) return;
        _pendingPlay = null;
        try
        {
            MapBuildResult result = task.GetAwaiter().GetResult();
            _diagnostics = result.Diagnostics;
            _statistics = result.Statistics;
            if (!result.Success)
            {
                _status = "PLAYTEST BLOCKED BY VALIDATION";
                DeletePendingPlaySnapshot();
                return;
            }
            string snapshotPath = _pendingPlaySnapshot
                ?? throw new InvalidOperationException("Playtest snapshot is unavailable.");
            _playtest.Launch(snapshotPath, _pendingPlayBots, deleteProjectOnExit: true);
            _pendingPlaySnapshot = null;
            _status = "PLAYTEST RUNNING - RETURN BY CLOSING THE GAME";
        }
        catch (Exception exception)
        {
            _diagnostics = [new("MAP-EDT-004", MapDiagnosticSeverity.Error, exception.Message)];
            _status = "PLAYTEST FAILED";
            DeletePendingPlaySnapshot();
        }
    }

    private void DeletePendingPlaySnapshot()
    {
        string? path = _pendingPlaySnapshot;
        _pendingPlaySnapshot = null;
        if (path == null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void GeneratePreview()
    {
        Save();
        Vector3 position = _viewport.Camera.Position;
        Vector3 target = position + _viewport.Camera.Forward * 10;
        _document.Execute(new ModifyPropertyCommand(_document, "Set Preview Camera", project =>
        {
            project.Map.Preview = new MapPreview
            {
                Position = [position.X, position.Y, position.Z],
                Target = [target.X, target.Y, target.Z]
            };
            project.PreviewImage = "preview.png";
            if (project.Authoring != null)
            {
                project.Authoring.Editor.CameraPosition = [position.X, position.Y, position.Z];
                project.Authoring.Editor.CameraTarget = [target.X, target.Y, target.Z];
            }
        }));
        _previewPath = Path.Combine(Path.GetDirectoryName(_document.Path!)!, "preview.png");
        _previewRequest = Guid.NewGuid();
        _status = "CAPTURING PREVIEW";
    }

    private void DrainCaptures(SdlRenderSurface surface)
    {
        while (surface.TryDequeueCapture(out RenderCaptureResult? result))
        {
            if (result?.OutputName == null) continue;
            RenderCapturePng.Write(result, result.OutputName);
            _document.Save();
            _status = $"PREVIEW SAVED {result.Width} X {result.Height}";
        }
        while (surface.TryDequeueCaptureFailure(out RenderCaptureFailure? failure))
        {
            if (failure == null) continue;
            _diagnostics = [new("MAP-PREVIEW-001", MapDiagnosticSeverity.Error, failure.Error)];
            _status = "PREVIEW FAILED";
        }
    }

    private void Validate()
    {
        _diagnostics = new MapValidator().ValidateProject(_document.Project);
        _status = _diagnostics.Any(value => value.Severity == MapDiagnosticSeverity.Error)
            ? "VALIDATION ERRORS" : "READY";
    }
}
