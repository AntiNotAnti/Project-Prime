using System;

namespace MphRead
{
    /// <summary>
    /// The neutral look sources understood by the game boundary.
    ///
    /// The values are flags as well as an enum so a consumed local frame can
    /// retain every contributor that arrived in the same native frame.  Game
    /// deliberately owns this type: SDL, Android and OpenTK never cross the
    /// simulation boundary.
    /// </summary>
    [Flags]
    public enum LookDeviceKind
    {
        None = 0,
        Mouse = 1 << 0,
        GamepadStick = 1 << 1,
        GamepadGyro = 1 << 2,
        Touch = 1 << 3,
        Stylus = 1 << 4
    }
}
