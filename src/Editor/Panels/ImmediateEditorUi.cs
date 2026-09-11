using MphRead;
using MphRead.Mods.MapGen;
using OpenTK.Mathematics;
using ProjectPrime.Editor.Documents;
using ProjectPrime.Editor.Viewport;

namespace ProjectPrime.Editor.Panels;

public sealed class ImmediateEditorUi
{
    public const float MenuHeight = 34;
    public const float LeftWidth = 230;
    public const float RightWidth = 270;
    public const float BottomHeight = 168;
    private static readonly Vector4 Panel = new(0.055f, 0.065f, 0.085f, 0.98f);
    private static readonly Vector4 PanelAlt = new(0.075f, 0.09f, 0.12f, 0.98f);
    private static readonly Vector4 Accent = new(0.2f, 0.72f, 0.86f, 1);
    private static readonly Vector4 TextColor = new(0.84f, 0.9f, 0.95f, 1);
    private static readonly Vector4 Muted = new(0.48f, 0.56f, 0.64f, 1);

    public EditorViewportLayout Viewport(Vector2i logicalSize, Vector2i framebufferSize)
        => EditorViewportLayout.Create(logicalSize, framebufferSize,
            new EditorRect(LeftWidth, MenuHeight,
                Math.Max(1, logicalSize.X - LeftWidth - RightWidth),
                Math.Max(1, logicalSize.Y - MenuHeight - BottomHeight)));

    public string? Draw(RenderFrame frame, Vector2i size, RenderSurfaceInput input,
        MapDocument document, IReadOnlyList<MapDiagnostic> diagnostics,
        MapBuildStatistics? statistics, string status, bool recoveryAvailable)
    {
        Box(frame, size, new(0, 0, size.X, MenuHeight), PanelAlt);
        Box(frame, size, new(0, MenuHeight, LeftWidth, size.Y - MenuHeight), Panel);
        Box(frame, size, new(size.X - RightWidth, MenuHeight, RightWidth, size.Y - MenuHeight), Panel);
        Box(frame, size, new(LeftWidth, size.Y - BottomHeight,
            size.X - LeftWidth - RightWidth, BottomHeight), PanelAlt);
        Text(frame, size, 12, 11, "PROJECT PRIME MAP EDITOR", TextColor, 2);
        string? action = null;
        float x = 330;
        foreach ((string label, string command) in new[]
        {
            ("SAVE", "save"), ("BUILD", "build"), ("PLAY SOLO", "play-solo"),
            ("PLAY BOTS", "play-bots"), ("EXPORT", "export"),
            ("UNDO", "undo"), ("REDO", "redo")
        })
        {
            float width = label.StartsWith("PLAY", StringComparison.Ordinal) ? 88 : 72;
            if (Button(frame, size, input, new(x, 5, width, 24), label)) action = command;
            x += width + 6;
        }
        if (recoveryAvailable)
        {
            if (Button(frame, size, input, new(size.X - 226, 5, 104, 24), "RECOVER")) action = "recover";
            if (Button(frame, size, input, new(size.X - 116, 5, 104, 24), "DISCARD")) action = "discard-recovery";
        }

        Text(frame, size, 12, 50, "OUTLINER", Accent, 1);
        float row = 72;
        MapAuthoringScene? authoring = document.Project.Authoring;
        if (authoring != null)
        {
            foreach (ConvexBrush brush in authoring.Brushes.Take(12))
            {
                EditorRect rect = new(8, row - 3, LeftWidth - 16, 18);
                if (document.SelectedObjectId == brush.Id) Box(frame, size, rect, new(0.12f, 0.28f, 0.36f, 1));
                Text(frame, size, 14, row, brush.Id.ToUpperInvariant(), TextColor, 1);
                if (input.LeftPressed && rect.Contains(input.MousePosition)) document.SelectedObjectId = brush.Id;
                row += 19;
            }
            foreach (MapEntityDefinition entity in authoring.Entities.Take(Math.Max(0, 12 - authoring.Brushes.Count)))
            {
                EditorRect rect = new(8, row - 3, LeftWidth - 16, 18);
                if (document.SelectedObjectId == entity.Id) Box(frame, size, rect, new(0.12f, 0.28f, 0.36f, 1));
                Text(frame, size, 14, row, entity.Id.ToUpperInvariant(), TextColor, 1);
                if (input.LeftPressed && rect.Contains(input.MousePosition)) document.SelectedObjectId = entity.Id;
                row += 19;
            }
        }
        row = size.Y - 154;
        foreach ((string label, string command) in new[]
        {
            ("+ BOX", "box"), ("+ WEDGE", "wedge"), ("+ CYLINDER", "cylinder"),
            ("+ STAIRS", "stairs"), ("+ SPAWN", "spawn"), ("+ ITEM", "item"), ("+ JUMP PAD", "jump")
        })
        {
            if (Button(frame, size, input, new(10, row, LeftWidth - 20, 18), label)) action = command;
            row += 20;
        }

        float inspectorX = size.X - RightWidth + 14;
        Text(frame, size, inspectorX, 50, "INSPECTOR", Accent, 1);
        Text(frame, size, inspectorX, 74,
            (document.SelectedObjectId ?? "NO SELECTION").ToUpperInvariant(), TextColor, 1);
        MapTransform? transform = SelectedTransform(document);
        if (transform != null)
        {
            Text(frame, size, inspectorX, 98,
                $"POS {transform.Position[0]:0.00} {transform.Position[1]:0.00} {transform.Position[2]:0.00}",
                TextColor, 1);
            string[] labels = ["X-", "X+", "Y-", "Y+", "Z-", "Z+"];
            string[] commands = ["x-", "x+", "y-", "y+", "z-", "z+"];
            for (int index = 0; index < labels.Length; index++)
                if (Button(frame, size, input,
                    new(inspectorX + index % 3 * 78, 116 + index / 3 * 25, 72, 21), labels[index]))
                    action = commands[index];
            ConvexBrush? selectedBrush = document.Project.Authoring?.Brushes
                .FirstOrDefault(brush => brush.Id == document.SelectedObjectId);
            if (selectedBrush != null)
            {
                int face = Math.Clamp(document.SelectedFaceIndex ?? 0, 0,
                    Math.Max(0, selectedBrush.Faces.Count - 1));
                if (Button(frame, size, input, new(inspectorX, 169, RightWidth - 28, 21),
                    $"FACE {face + 1}/{selectedBrush.Faces.Count} - SELECT NEXT")) action = "face-next";
                if (Button(frame, size, input, new(inspectorX, 194, 115, 21), "MAT WHOLE"))
                    action = "material-next";
                if (Button(frame, size, input, new(inspectorX + 121, 194, 115, 21), "MAT FACE"))
                    action = "material-face-next";
                if (Button(frame, size, input, new(inspectorX, 219, RightWidth - 28, 21),
                    selectedBrush.Solid ? "SOLID - COLLISION ON" : "DECORATIVE - COLLISION OFF"))
                    action = "brush-solid";
                MapAuthoringMaterial? material = document.Project.Authoring!.Materials
                    .FirstOrDefault(value => selectedBrush.Faces.ElementAtOrDefault(face)?.MaterialId == value.Id);
                if (material != null)
                {
                    if (Button(frame, size, input, new(inspectorX, 244, 115, 21),
                        $"TILE - {material.Tiling:0.##}")) action = "material-tiling-down";
                    if (Button(frame, size, input, new(inspectorX + 121, 244, 115, 21),
                        $"TILE + {material.Tiling:0.##}")) action = "material-tiling-up";
                    ConvexBrushFace selectedFace = selectedBrush.Faces[face];
                    if (Button(frame, size, input, new(inspectorX, 269, 115, 21),
                        $"UV - {selectedFace.TextureScale:0.##}")) action = "face-scale-down";
                    if (Button(frame, size, input, new(inspectorX + 121, 269, 115, 21),
                        $"UV + {selectedFace.TextureScale:0.##}")) action = "face-scale-up";
                    if (Button(frame, size, input, new(inspectorX, 294, 115, 21),
                        $"ROT + {selectedFace.TextureRotation:0}")) action = "face-rotate";
                    if (Button(frame, size, input, new(inspectorX + 121, 294, 115, 21),
                        material.Terrain.ToUpperInvariant())) action = "material-terrain";
                    string[] offsetLabels = ["U-", "U+", "V-", "V+"];
                    string[] offsetCommands = ["face-offset-x-down", "face-offset-x-up",
                        "face-offset-y-down", "face-offset-y-up"];
                    for (int index = 0; index < offsetLabels.Length; index++)
                        if (Button(frame, size, input, new(inspectorX + index * 60, 319, 55, 21),
                            $"{offsetLabels[index]} {(index < 2 ? selectedFace.TextureOffset[0] : selectedFace.TextureOffset[1]):0.##}"))
                            action = offsetCommands[index];
                }
            }
            MapEntityDefinition? selectedEntity = document.Project.Authoring?.Entities
                .FirstOrDefault(entity => entity.Id == document.SelectedObjectId);
            if (selectedEntity?.Kind is MapEntityKind.TeamSpawn or MapEntityKind.CaptureBase
                && Button(frame, size, input, new(inspectorX, 169, RightWidth - 28, 22),
                    $"TEAM {selectedEntity.Team} - CHANGE")) action = "team-next";
        }
        Text(frame, size, inspectorX, 348, "WASD FLY  RMB LOOK  F FRAME", Muted, 1);
        Text(frame, size, inspectorX, 364, "W/E/R MODE  ARROWS TRANSFORM", Muted, 1);
        Text(frame, size, inspectorX, 390, "OVERLAYS", Accent, 1);
        bool showCollision = authoring?.Editor.ShowCollision == true;
        bool showEntities = authoring?.Editor.ShowEntities == true;
        bool showBounds = authoring?.Editor.ShowWorldBounds == true;
        if (Button(frame, size, input, new(inspectorX, 410, RightWidth - 28, 22),
            showCollision ? "COLLISION ON" : "COLLISION OFF")) action = "toggle-collision";
        if (Button(frame, size, input, new(inspectorX, 436, RightWidth - 28, 22),
            showEntities ? "ENTITIES ON" : "ENTITIES OFF")) action = "toggle-entities";
        if (Button(frame, size, input, new(inspectorX, 462, RightWidth - 28, 22),
            showBounds ? "WORLD BOUNDS ON" : "WORLD BOUNDS OFF")) action = "toggle-bounds";
        if (Button(frame, size, input, new(inspectorX, 496, RightWidth - 28, 24),
            "SET + GENERATE PREVIEW")) action = "preview";
        Text(frame, size, inspectorX, 530, "GAMEPLAY ENTITIES", Accent, 1);
        (string Label, string Command)[] entityButtons =
        [
            ("DAMAGE", "damage"), ("KILL", "kill"),
            ("TEAM SPAWN", "team-spawn"), ("CAPTURE BASE", "capture-base"),
            ("BOUNTY BASE", "bounty-base"), ("NODE", "node")
        ];
        for (int index = 0; index < entityButtons.Length; index++)
        {
            EditorRect rect = new(inspectorX + index % 2 * 121, 550 + index / 2 * 25, 115, 21);
            if (Button(frame, size, input, rect, entityButtons[index].Label))
                action = entityButtons[index].Command;
        }
        Text(frame, size, inspectorX, 630, "SUPPORTED MODES", Accent, 1);
        MapMode[] modeValues = [MapMode.Battle, MapMode.Survival, MapMode.Capture, MapMode.Bounty, MapMode.Nodes];
        for (int index = 0; index < modeValues.Length; index++)
        {
            MapMode mode = modeValues[index];
            bool enabled = document.Project.SupportedModes.Contains(mode);
            if (Button(frame, size, input, new(inspectorX, 650 + index * 22, RightWidth - 28, 19),
                $"{mode} {(enabled ? "ON" : "OFF")}")) action = "mode-" + mode.ToString().ToLowerInvariant();
        }
        MapEnvironment environment = document.Project.Environment ?? MapEnvironment.From(document.Project.Map);
        Text(frame, size, inspectorX, 764, "ENVIRONMENT", Accent, 1);
        if (Button(frame, size, input, new(inspectorX, 784, RightWidth - 28, 19),
            environment.FogEnabled ? "FOG ON" : "FOG OFF")) action = "environment-fog";
        if (Button(frame, size, input, new(inspectorX, 809, 115, 19),
            $"KILL - {environment.KillHeight:0.#}")) action = "environment-kill-down";
        if (Button(frame, size, input, new(inspectorX + 121, 809, 115, 19),
            $"KILL + {environment.KillHeight:0.#}")) action = "environment-kill-up";
        if (Button(frame, size, input, new(inspectorX, 834, 115, 19),
            $"CLIP - {environment.FarClip:0}")) action = "environment-clip-down";
        if (Button(frame, size, input, new(inspectorX + 121, 834, 115, 19),
            $"CLIP + {environment.FarClip:0}")) action = "environment-clip-up";
        Text(frame, size, inspectorX, 859, "VIEW", Accent, 1);
        string[] viewLabels = ["3D", "TOP", "FRONT", "SIDE"];
        string[] viewCommands = ["view-perspective", "view-top", "view-front", "view-side"];
        for (int index = 0; index < viewLabels.Length; index++)
            if (Button(frame, size, input, new(inspectorX + index * 59, 879, 55, 19),
                viewLabels[index])) action = viewCommands[index];

        float bottomY = size.Y - BottomHeight + 12;
        Text(frame, size, LeftWidth + 12, bottomY, "DIAGNOSTICS", Accent, 1);
        int errors = diagnostics.Count(value => value.Severity == MapDiagnosticSeverity.Error);
        int warnings = diagnostics.Count(value => value.Severity == MapDiagnosticSeverity.Warning);
        Text(frame, size, LeftWidth + 132, bottomY,
            $"{errors} ERRORS  {warnings} WARNINGS", errors == 0 ? TextColor : new(1, 0.35f, 0.25f, 1), 1);
        float dy = bottomY + 22;
        foreach (MapDiagnostic diagnostic in diagnostics.Take(4))
        {
            Text(frame, size, LeftWidth + 12, dy,
                $"{diagnostic.Code} {diagnostic.Message}".ToUpperInvariant(),
                diagnostic.Severity == MapDiagnosticSeverity.Error ? new(1, 0.4f, 0.3f, 1) : Muted, 1);
            dy += 17;
        }
        float statsX = size.X - RightWidth - 250;
        Text(frame, size, statsX, bottomY, "MAP BUDGET", Accent, 1);
        if (statistics != null)
        {
            Text(frame, size, statsX, bottomY + 22, $"TRIANGLES {statistics.RenderTriangles:N0}", TextColor, 1);
            Text(frame, size, statsX, bottomY + 39, $"VERTICES  {statistics.RenderVertices:N0}", TextColor, 1);
            Text(frame, size, statsX, bottomY + 56, $"MATERIALS {statistics.Materials:N0}", TextColor, 1);
            Vector4 budgetColor = statistics.CollisionGridReferences >= ushort.MaxValue * .8 ? new(1, .65f, .2f, 1) : TextColor;
            Text(frame, size, statsX, bottomY + 73, $"COL FACES {statistics.CollisionFaces:N0}", TextColor, 1);
            Text(frame, size, statsX, bottomY + 90, $"COL POINTS {statistics.CollisionPoints:N0}", TextColor, 1);
            Text(frame, size, statsX, bottomY + 107,
                $"GRID REFS {statistics.CollisionGridReferences:N0} / 65,535", budgetColor, 1);
            Text(frame, size, statsX, bottomY + 124,
                $"ENT {statistics.Entities:N0}  SPAWN {statistics.Spawns:N0}  ITEM {statistics.Items:N0}", TextColor, 1);
            float width = statistics.WorldMax[0] - statistics.WorldMin[0];
            float height = statistics.WorldMax[1] - statistics.WorldMin[1];
            float depth = statistics.WorldMax[2] - statistics.WorldMin[2];
            Text(frame, size, statsX, bottomY + 141,
                $"WORLD {width:0.#} X {height:0.#} X {depth:0.#}", TextColor, 1);
        }
        Text(frame, size, LeftWidth + 12, size.Y - 20, status.ToUpperInvariant(), Muted, 1);
        return action;
    }

    private static MapTransform? SelectedTransform(MapDocument document)
    {
        if (document.SelectedObjectId is not { } id || document.Project.Authoring is not { } scene)
            return null;
        return scene.Brushes.FirstOrDefault(value => value.Id == id)?.Transform
            ?? scene.Entities.FirstOrDefault(value => value.Id == id)?.Transform;
    }

    private static bool Button(RenderFrame frame, Vector2i size, RenderSurfaceInput input,
        EditorRect rect, string label)
    {
        bool hover = rect.Contains(input.MousePosition);
        Box(frame, size, rect, hover ? new(0.15f, 0.35f, 0.44f, 1) : new(0.095f, 0.12f, 0.16f, 1));
        Text(frame, size, rect.X + 7, rect.Y + 7, label, TextColor, 1);
        return hover && input.LeftPressed;
    }

    private static void Box(RenderFrame frame, Vector2i size, EditorRect rect, Vector4 color)
    {
        float x0 = rect.X / size.X * 2 - 1, x1 = (rect.X + rect.Width) / size.X * 2 - 1;
        float y0 = 1 - rect.Y / size.Y * 2, y1 = 1 - (rect.Y + rect.Height) / size.Y * 2;
        frame.AddOverlayCommand(new RenderOverlayCommand(RenderOverlayKind.FlatBox,
        [
            new(new Vector3(x1, y0, 0), Vector2.Zero, color), new(new Vector3(x0, y0, 0), Vector2.Zero, color),
            new(new Vector3(x1, y1, 0), Vector2.Zero, color), new(new Vector3(x0, y1, 0), Vector2.Zero, color)
        ], color: color));
    }

    private static void Text(RenderFrame frame, Vector2i size, float x, float y,
        string value, Vector4 color, int scale)
    {
        foreach (char raw in value)
        {
            char c = char.ToUpperInvariant(raw);
            ulong bits = PixelFont.Glyph(c);
            for (int row = 0; row < 7; row++)
            for (int column = 0; column < 5; column++)
                if ((bits & (1UL << (row * 5 + column))) != 0)
                    Box(frame, size, new(x + column * scale, y + row * scale, scale, scale), color);
            x += 6 * scale;
            if (x > size.X - 8) break;
        }
    }
}

internal static class PixelFont
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-_:/+()";
    private static readonly string[] Rows =
    [
        "011101000110001111111000110001", "111101000111110100011000111110",
        "011111000010000100001000001111", "111101000110001100011000111110",
        "111111000011110100001000011111", "111111000011110100001000010000",
        "011111000010111100011000101111", "100011000111111100011000110001",
        "111110010000100001000010011111", "001110001000010000101001001100",
        "100011001011100100101000110001", "100001000010000100001000011111",
        "100011101110101101011000110001", "100011100110101100111000110001",
        "011101000110001100011000101110", "111101000110001111101000010000",
        "011101000110001101011001001101", "111101000110001111101001010001",
        "011111000001110000011000111110", "111110010000100001000010000100",
        "100011000110001100011000101110", "100011000110001100010101000100",
        "100011000110101101011010101010", "100010101000100010101000110001",
        "100011000101010001000010000100", "111110001000100010001000011111",
        "01110100011001110101110011000101110", "001000110000100001000010001110",
        "01110100010000100110010001011111", "111100000100001011100000111110",
        "000100011001010100101111100010", "111111000011110000010000111110",
        "011101000010000111101000101110", "111110000100010001000100001000",
        "011101000101110100011000101110", "011101000110001011110000101110",
        "00000000000000011100000000000000000", "00000001000010000100001000000000000",
        "00000000000000000000000001111100000", "00001000100010001000100001000000000",
        "00000100000010000000001000100000000", "00110010001000010000100000110000000",
        "00000001000010011111001000010000000", "00110010000100001000010000110000000",
        "01100000100001000010000100110000000"
    ];

    public static ulong Glyph(char value)
    {
        if (value == ' ') return 0;
        int index = Alphabet.IndexOf(value);
        if (index < 0 || index >= Rows.Length) return 0b111111000110001100011000111111u;
        ulong bits = 0;
        string rows = Rows[index];
        for (int i = 0; i < Math.Min(35, rows.Length); i++) if (rows[i] == '1') bits |= 1UL << i;
        return bits;
    }
}
