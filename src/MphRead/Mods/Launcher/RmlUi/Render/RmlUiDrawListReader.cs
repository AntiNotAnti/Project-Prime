#if MPHREAD_RMLUI_POC && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;

namespace MphRead.Mods.Launcher.RmlUi.Render;

// Copies the native frame before any subsequent native mutation. Cached
// immutable geometry/font atlases are copied once per handle and generation.
internal sealed unsafe class RmlUiDrawListReader
{
    private const string Library = "ProjectPrime.RmlUi.Native";
    private ulong _generation;
    private ulong _visualRevision;
    private bool? _updateStatusAbi;
    private RmlUiDrawListFrame? _frame;
    internal long CaptureRequests { get; private set; }
    internal long CapturedFrames { get; private set; }
    internal long ReusedFrames { get; private set; }
    private readonly Dictionary<ulong, RmlUiDrawGeometry> _geometry = new();
    private readonly Dictionary<ulong, RmlUiDrawTexture> _textures = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeGeometry
    {
        internal uint Size, VertexCount, IndexCount, Reserved;
        internal nint Vertices, Indices;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeTexture
    {
        internal uint Size, Width, Height, Reserved;
        internal ulong Handle;
        internal nint Pixels;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_generation")]
    private static extern ulong Generation();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_update_status")]
    private static extern int UpdateStatus(ref RmlUiNativeUpdateState status);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_draw_features")]
    private static extern uint Features();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_draw_command_count")]
    private static extern int CommandCount();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_draw_command")]
    private static extern int Command(int index, ref RmlUiDrawCommand command);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_draw_geometry")]
    private static extern int Geometry(ulong handle, ref NativeGeometry geometry);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_draw_texture_count")]
    private static extern int TextureCount();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "pp_rmlui_draw_texture")]
    private static extern int Texture(int index, ref NativeTexture texture);

    internal RmlUiDrawListFrame Capture()
    {
        CaptureRequests++;
        if (Features() != 0)
            throw new NotSupportedException("The RmlUi theme requested GPU layers, filters, or custom shaders which the draw-list compositor does not implement.");
        // Called after native Render, which updates the retained list when DOM,
        // input, animation, viewport or resource lifetime has changed. A copied
        // frame remains usable across engine device recreation; GPU resources
        // are separately rebuilt by ModernGraphicsCompat.
        bool statusAvailable = TryUpdateStatus(out var status);
        ulong generation = statusAvailable ? status.Generation : Generation();
        if (_generation != generation) { Forget(); _generation = generation; }
        if (statusAvailable && (status.Flags & RmlUiUpdateFlags.DrawListValid) != 0
            && _frame != null && _visualRevision == status.VisualRevision)
        {
            ReusedFrames++;
            return _frame;
        }
        int count = CommandCount();
        if ((uint)count > 1_000_000) throw new InvalidOperationException("RmlUi native command count is invalid.");
        var commands = new RmlUiDrawCommand[count];
        var usedGeometry = new HashSet<ulong>();
        for (int i = 0; i < count; i++)
        {
            commands[i].Size = (uint)sizeof(RmlUiDrawCommand);
            if (Command(i, ref commands[i]) == 0 || commands[i].Size != sizeof(RmlUiDrawCommand))
                throw new InvalidOperationException("RmlUi draw command ABI is incompatible.");
            ref RmlUiDrawCommand command = ref commands[i];
            if (command.Kind < RmlUiDrawCommandKind.Geometry || command.Kind > RmlUiDrawCommandKind.ClipMask
                || !float.IsFinite(command.TranslationX) || !float.IsFinite(command.TranslationY))
                throw new InvalidOperationException("RmlUi native draw command is invalid.");
            if (command.Kind == RmlUiDrawCommandKind.Transform)
                for (int n = 0; n < 16; n++)
                    if (!float.IsFinite(command.Transform[n]))
                        throw new InvalidOperationException("RmlUi transform contains a nonfinite element.");
            if (command.Kind == RmlUiDrawCommandKind.ClipMask && command.Operation > RmlUiClipOperation.Intersect)
                throw new InvalidOperationException("RmlUi clip operation is invalid.");
            if (command.Kind is not (RmlUiDrawCommandKind.Geometry or RmlUiDrawCommandKind.ClipMask)) continue;
            if (command.Geometry == 0) throw new InvalidOperationException("RmlUi geometry handle is invalid.");
            usedGeometry.Add(command.Geometry);
            if (_geometry.ContainsKey(command.Geometry)) continue;
            var native = new NativeGeometry { Size = (uint)sizeof(NativeGeometry) };
            if (Geometry(command.Geometry, ref native) == 0 || native.Size != sizeof(NativeGeometry)
                || native.Vertices == 0 || native.Indices == 0
                || native.VertexCount == 0 || native.VertexCount > 4_000_000
                || native.IndexCount == 0 || native.IndexCount > 12_000_000)
                throw new InvalidOperationException("RmlUi native geometry is invalid.");
            _geometry.Add(command.Geometry, new RmlUiDrawGeometry(
                new ReadOnlySpan<RmlUiVertex>((void*)native.Vertices, checked((int)native.VertexCount)).ToArray(),
                new ReadOnlySpan<int>((void*)native.Indices, checked((int)native.IndexCount)).ToArray()));
        }
        int textureCount = TextureCount();
        if ((uint)textureCount > 65_536) throw new InvalidOperationException("RmlUi native texture count is invalid.");
        var usedTextures = new HashSet<ulong>();
        for (int i = 0; i < textureCount; i++)
        {
            var native = new NativeTexture { Size = (uint)sizeof(NativeTexture) };
            if (Texture(i, ref native) == 0 || native.Size != sizeof(NativeTexture)
                || native.Handle == 0 || native.Pixels == 0 || native.Width == 0 || native.Height == 0
                || native.Width > 16_384 || native.Height > 16_384
                || (ulong)native.Width * native.Height * 4 > 256UL * 1024 * 1024)
                throw new InvalidOperationException("RmlUi native texture is invalid.");
            usedTextures.Add(native.Handle);
            if (_textures.ContainsKey(native.Handle)) continue;
            int bytes = checked((int)(native.Width * native.Height * 4));
            _textures.Add(native.Handle, new RmlUiDrawTexture((int)native.Width, (int)native.Height,
                new ReadOnlySpan<byte>((void*)native.Pixels, bytes).ToArray()));
        }
        foreach (RmlUiDrawCommand command in commands)
            if (command.Kind == RmlUiDrawCommandKind.Geometry && command.Texture != 0 && !usedTextures.Contains(command.Texture))
                throw new InvalidOperationException("RmlUi draw command references a released texture.");
        Prune(_geometry, usedGeometry); Prune(_textures, usedTextures);
        var frame = new RmlUiDrawListFrame(generation, commands, _geometry, _textures);
        CapturedFrames++;
        _frame = statusAvailable && (status.Flags & RmlUiUpdateFlags.DrawListValid) != 0 ? frame : null;
        _visualRevision = _frame != null ? status.VisualRevision : 0;
        return frame;
    }

    private bool TryUpdateStatus(out RmlUiNativeUpdateState status)
    {
        status = RmlUiNativeUpdateState.Request();
        if (_updateStatusAbi == false) return false;
        try
        {
            if (UpdateStatus(ref status) == 0 || status.Size != 40 || status.Version != 1
                || status.Generation == 0 || status.VisualRevision == 0 || status.Reserved != 0
                || (status.Flags & ~((RmlUiUpdateFlags)3)) != 0
                || double.IsNaN(status.NextUpdateDelaySeconds) || status.NextUpdateDelaySeconds < 0)
            { _updateStatusAbi = false; return false; }
            _updateStatusAbi = true;
            return true;
        }
        catch (EntryPointNotFoundException) { _updateStatusAbi = false; return false; }
    }

    private static void Prune<T>(Dictionary<ulong, T> values, HashSet<ulong> used)
    {
        List<ulong>? released = null;
        foreach (ulong handle in values.Keys)
            if (!used.Contains(handle)) (released ??= new()).Add(handle);
        if (released != null) foreach (ulong handle in released) values.Remove(handle);
    }
    internal void Forget()
    {
        _generation = 0; _visualRevision = 0; _frame = null;
        _geometry.Clear(); _textures.Clear();
    }
}
#endif
