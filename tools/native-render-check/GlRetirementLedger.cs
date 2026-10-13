using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MphRead;
using OpenTK.Graphics.OpenGL;

// Diagnostic name queries, not a native allocation/driver residency census.
sealed class GlRetirementLedger
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly HashSet<int> _lists = new(), _buffers = new(), _programs = new(), _shaders = new(), _textures = new(), _framebuffers = new(), _renderbuffers = new();
    private readonly HashSet<(int Id, string Api)> _vaos = new();
    private readonly object[] _models;
    private readonly int _liveListsBefore;
    private GlRetirementLedger(Scene scene)
    {
        object? Field(object value, string name) => value.GetType().GetField(name, Instance)?.GetValue(value)
            ?? value.GetType().GetProperty(name, Instance)?.GetValue(value);
        _models = ((IEnumerable)Field(scene, "_modelLeases")!).Cast<object>().ToArray();
        foreach (object model in _models)
            foreach (object mesh in (IEnumerable)Field(model, "Meshes")!) Add(_lists, Field(mesh, "ListId"));
        // Editor lists have their own owner, separate from retail model leases.
        Add(_lists, Field(Field(scene, "_editorGrid") ?? new object(), "ListId"));
        if (Field(scene, "_editorMeshes") is IDictionary editors)
            foreach (object mesh in editors.Values)
                foreach (object part in (IEnumerable)Field(mesh, "Parts")!)
                    foreach (string side in new[] { "Fill", "Edges" }) Add(_lists, Field(Field(part, side)!, "ListId"));
        Type characters = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.Characters.CharacterModelRuntime")!;
        object table = characters.GetField("_scenes", Static)!.GetValue(null)!;
        object?[] lookup = { scene, null };
        if ((bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, lookup)!)
            foreach (string kind in new[] { "RigidModels", "WeightedModels" })
                foreach (object model in ((IDictionary)Field(lookup[1]!, kind)!).Values)
                    foreach (object segment in (IEnumerable)Field(model, "Segments")!) Add(_lists, Field(segment, "ListId"));
        Type retained = typeof(Scene).Assembly.GetType("MphRead.Mods.Render.DesktopRetainedGeometry")!;
        void Geometry(object geometry)
        {
            Add(_buffers, Field(geometry, "Vbo")); Add(_buffers, Field(geometry, "Ibo"));
            if (Field(geometry, "Vao") is int id && id != 0) _vaos.Add((id, Field(geometry, "VaoApi")!.ToString()!));
        }
        foreach (string kind in new[] { "_lists", "_combinedBatches" })
            if (retained.GetField(kind, Static)!.GetValue(null) is IDictionary geometry)
                foreach (object value in geometry.Values) Geometry(Field(value, "Geometry") ?? value);
        foreach (FieldInfo field in typeof(Scene).GetFields(Instance))
        {
            object? value = field.GetValue(scene); string name = field.Name.ToLowerInvariant();
            if (value is int)
            {
                if (name.EndsWith("program") || name.EndsWith("programid")) Add(_programs, value);
                else if (name.EndsWith("framebuffer")) Add(_framebuffers, value);
                else if (name.EndsWith("renderbuffer")) Add(_renderbuffers, value);
                else if (name.EndsWith("texture")) Add(_textures, value);
            }
            else if (value?.GetType().Name == "WorldShaderProgram") Add(_programs, Field(value, "Program"));
        }
        foreach (int id in (IEnumerable<int>)Field(scene, "_ownedTextures")!) Add(_textures, id);
        foreach (int id in _programs)
        {
            GL.GetProgram(id, GetProgramParameterName.AttachedShaders, out int count);
            if (count == 0) continue;
            var names = new int[count]; GL.GetAttachedShaders(id, count, out int written, names);
            foreach (int shader in names.Take(written)) Add(_shaders, shader);
        }
        _liveListsBefore = _lists.Count(GL.IsList);
    }
    private static void Add(HashSet<int> ids, object? value) { if (value is int id && id != 0) ids.Add(id); }
    internal static GlRetirementLedger Capture(Scene scene) => new(scene);
    internal object Verify(Scene scene)
    {
        int lists = _lists.Count(GL.IsList), buffers = _buffers.Count(GL.IsBuffer), textures = _textures.Count(GL.IsTexture);
        int framebuffers = _framebuffers.Count(GL.IsFramebuffer), renderbuffers = _renderbuffers.Count(GL.IsRenderbuffer);
        int vaos = _vaos.Count(v => v.Api == "Apple" ? GL.Apple.IsVertexArray(v.Id) : GL.IsVertexArray(v.Id));
        int programs = 0, pendingPrograms = 0, shaders = 0, pendingShaders = 0;
        foreach (int id in _programs)
        {
            if (!GL.IsProgram(id)) continue;
            programs++; GL.GetProgram(id, GetProgramParameterName.DeleteStatus, out int deleted);
            if (deleted == 0) throw new InvalidOperationException("Unretired owned program: " + id);
            pendingPrograms++;
        }
        foreach (int id in _shaders)
        {
            if (!GL.IsShader(id)) continue;
            shaders++; GL.GetShader(id, ShaderParameter.DeleteStatus, out int deleted);
            if (deleted == 0) throw new InvalidOperationException("Unretired attached shader: " + id);
            pendingShaders++;
        }
        int meshNamesAfter = _models.Sum(model => ((IEnumerable)model.GetType().GetProperty("Meshes")!.GetValue(model)!).Cast<object>()
            .Count(mesh => (int)mesh.GetType().GetProperty("ListId")!.GetValue(mesh)! != 0));
        object leases = typeof(Scene).GetField("_modelLeases", Instance)!.GetValue(scene)!;
        int leasesAfter = (int)leases.GetType().GetProperty("Count")!.GetValue(leases)!;
        if (lists != 0 || buffers != 0 || vaos != 0 || textures != 0 || framebuffers != 0 || renderbuffers != 0 || meshNamesAfter != 0 || leasesAfter != 0)
            throw new InvalidOperationException($"GL retirement failed: lists={lists},buffers={buffers},vaos={vaos},textures={textures},fbos={framebuffers},rbos={renderbuffers},meshes={meshNamesAfter},leases={leasesAfter}");
        return new { legacyListNamesBefore = _lists.Count, legacyListsRecognizedBefore = _liveListsBefore, legacyListsRecognizedAfter = lists,
            meshListFieldsAfter = meshNamesAfter, sceneModelLeasesAfter = leasesAfter,
            bufferNamesBefore = _buffers.Count, bufferNamesRecognizedAfter = buffers, vertexArrayNamesBefore = _vaos.Count, vertexArraysRecognizedAfter = vaos,
            programNamesBefore = _programs.Count, programsRecognizedAfter = programs, deletionPendingProgramsAfter = pendingPrograms,
            attachedShaderNamesBefore = _shaders.Count, shadersRecognizedAfter = shaders, deletionPendingShadersAfter = pendingShaders,
            textureNamesBefore = _textures.Count, texturesRecognizedAfter = textures, framebufferNamesBefore = _framebuffers.Count, framebuffersRecognizedAfter = framebuffers,
            renderbufferNamesBefore = _renderbuffers.Count, renderbuffersRecognizedAfter = renderbuffers,
            scope = "Retail model leases, current character/editor lists, retained/combined VBO/IBO/APPLE or core VAO names, scene texture/FBO/RBO/program fields and world variants, attached shaders. Owner/name retirement, not driver residency or a complete context object enumeration; disabled procedural/depth-specific owners retain separate existing counters." };
    }
}
