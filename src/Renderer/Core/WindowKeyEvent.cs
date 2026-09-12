using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead;

/// <summary>
/// One translated keyboard edge. This is a Project Prime value type; SDL and
/// Android may populate it without depending on an OpenTK desktop window.
/// </summary>
public readonly record struct WindowKeyEvent(
    Keys Key,
    bool Down,
    bool Repeat,
    KeyModifiers Modifiers)
{
    public bool Alt => (Modifiers & KeyModifiers.Alt) != 0;
    public bool Control => (Modifiers & KeyModifiers.Control) != 0;
    public bool Shift => (Modifiers & KeyModifiers.Shift) != 0;
    public bool Command => (Modifiers & KeyModifiers.Super) != 0;
}
