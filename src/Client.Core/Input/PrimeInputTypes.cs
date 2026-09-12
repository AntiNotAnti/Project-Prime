namespace MphRead.Mods.Input;

/// <summary>
/// Platform-neutral key identities. Numeric values intentionally match GLFW
/// so the temporary engine keyboard adapter is a checked-free value cast;
/// persisted binding names remain unchanged.
/// </summary>
public enum PrimeKey
{
    Unknown = -1,
    Space = 32, Apostrophe = 39, Comma = 44, Minus = 45, Period = 46, Slash = 47,
    D0 = 48, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    Semicolon = 59, Equal = 61,
    A = 65, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    LeftBracket = 91, Backslash, RightBracket, GraveAccent = 96,
    Escape = 256, Enter, Tab, Backspace, Insert, Delete, Right, Left, Down, Up,
    PageUp, PageDown, Home, End,
    CapsLock = 280, ScrollLock, NumLock, PrintScreen, Pause,
    F1 = 290, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    KeyPad0 = 320, KeyPad1, KeyPad2, KeyPad3, KeyPad4, KeyPad5, KeyPad6, KeyPad7,
    KeyPad8, KeyPad9, KeyPadDecimal, KeyPadDivide, KeyPadMultiply, KeyPadSubtract,
    KeyPadAdd, KeyPadEnter, KeyPadEqual,
    LeftShift = 340, LeftControl, LeftAlt, LeftSuper,
    RightShift, RightControl, RightAlt, RightSuper, Menu
}

public enum PrimeMouseButton
{
    Left = 0,
    Right = 1,
    Middle = 2,
    Button4 = 3,
    Button5 = 4,
    Button6 = 5,
    Button7 = 6,
    Button8 = 7,
    Last = Button8
}

[System.Flags]
public enum PrimeGamepadButton
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    LeftBumper = 1 << 4,
    RightBumper = 1 << 5,
    Back = 1 << 6,
    Start = 1 << 7,
    LeftThumb = 1 << 8,
    RightThumb = 1 << 9,
    DpadUp = 1 << 10,
    DpadRight = 1 << 11,
    DpadDown = 1 << 12,
    DpadLeft = 1 << 13,
    LeftTrigger = 1 << 14,
    RightTrigger = 1 << 15
}

public enum PrimeGamepadAxis
{
    LeftX,
    LeftY,
    RightX,
    RightY,
    LeftTrigger,
    RightTrigger
}
