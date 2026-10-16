using System;
using System.Runtime.InteropServices;

namespace MphRead.Mods.Launcher.RmlUi.Host
{
    [Flags]
    public enum RmlUiTextInputCapabilities : uint { Composition = 1, Bounds = 2, Protected = 4 }
    public enum RmlUiInputDevice { Pointer, Keyboard, Gamepad, Touch, Stylus, InputMethod }
    public enum RmlUiPlatformInputKind
    {
        PointerMove, PointerDown, PointerUp, Wheel, KeyDown, KeyUp, TextCommitted,
        FocusLost, CompositionBegin, CompositionUpdate, CompositionCommit, CompositionCancel
    }
    public enum RmlUiInputResult { Accepted, Inactive, StaleDocument, HiddenDocument, StaleFocus, Unsupported, Invalid }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RmlUiNativeTextInputState
    {
        public uint Size, Version;
        public ulong Generation, DocumentId, FocusEpoch;
        public float X, Y, Width, Height;
        public int SelectionStart, SelectionEnd, Composing;
        public RmlUiTextInputCapabilities Capabilities;
        public static RmlUiNativeTextInputState Request() => new() { Size = 64, Version = 1 };
    }

    public readonly record struct RmlUiTextInputBounds(float X, float Y, float Width, float Height);
    public readonly record struct RmlUiTextInputState(RmlUiDocumentToken Document, ulong FocusEpoch,
        RmlUiTextInputBounds Bounds, int SelectionStart, int SelectionEnd, bool Composing,
        RmlUiTextInputCapabilities Capabilities);

    /// <summary>
    /// Platform callbacks capture document and text-focus lifetimes. A delayed
    /// commit or preedit cannot target a different field after focus changes.
    /// </summary>
    public readonly record struct RmlUiPlatformInputEvent(
        RmlUiDocumentToken Document, RmlUiPlatformInputKind Kind,
        RmlUiInputDevice Device = RmlUiInputDevice.Keyboard, ulong FocusEpoch = 0,
        double X = 0, double Y = 0, double Delta = 0, int Code = 0,
        RmlUiInputModifiers Modifiers = default, string Text = "", int Cursor = -1, int SelectionLength = 0)
    {
        // Record-generated ToString would expose committed/preedit/password text.
        public override string ToString() => $"RmlUi input {Device}/{Kind}, document {Document.DocumentId}, focus {FocusEpoch}";
    }

    /// <summary>Contains authored element IDs and numeric input metadata only.</summary>
    public readonly record struct RmlUiInputDiagnostics(
        ulong Sequence, RmlUiInputDevice Device, RmlUiPlatformInputKind Kind, RmlUiInputResult Result,
        double WindowX, double WindowY, int FramebufferX, int FramebufferY,
        string HoveredElement, string FocusedElement, RmlUiIntentKind? Intent, int IntentArgument)
    {
        public string Display => $"{Device}/{Kind} {Result} // WINDOW {WindowX:0.##},{WindowY:0.##}"
            + $" FRAMEBUFFER {FramebufferX},{FramebufferY} // HIT {HoveredElement} FOCUS {FocusedElement}"
            + (Intent.HasValue ? $" // INTENT {Intent}/{IntentArgument}" : "");
    }
}
