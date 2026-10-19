using System;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host;

internal enum RmlUiTextEditKind : uint { Select = 1, Replace = 2, Marked = 3, Commit = 4, Cancel = 5 }
internal enum RmlUiTextEditResult { Accepted = 1, StaleOwner = -1, StaleRevision = -2, Duplicate = -3,
    Invalid = -4, Unsupported = -5, Retired = -6, OwnerFailure = -7 }
[StructLayout(LayoutKind.Sequential)]
internal struct RmlUiNativeTextEditRequest
{
    public uint Size, Version;
    public ulong Generation, DocumentId, FocusEpoch, Revision, Sequence;
    public RmlUiTextEditKind Kind;
    public uint Flags;
    public int Start, Length, SelectedStart, SelectedLength;
    public ulong Reserved;
}
[StructLayout(LayoutKind.Sequential)]
internal struct RmlUiNativeTextEditState
{
    public uint Size, Version;
    public ulong Generation, DocumentId, FocusEpoch, Revision;
    public uint Capabilities, Flags;
    public int Length, SelectionStart, SelectionLength, MarkedStart, MarkedLength;
    public float X, Y, Width, Height;
    public uint Reserved;
}
internal readonly record struct RmlUiTextEditState(RmlUiDocumentToken Document, ulong FocusEpoch, ulong Revision,
    int Length, int SelectionStart, int SelectionLength, int MarkedStart, int MarkedLength,
    uint Flags, RmlUiTextInputBounds Bounds);
