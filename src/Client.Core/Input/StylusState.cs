namespace MphRead.Mods.Input
{
    public readonly record struct StylusState(bool Contact, long PointerId,
        float X, float Y, float Pressure, StylusButtons Buttons,
        StylusButtons PressedButtons, StylusButtons ReleasedButtons,
        long Timestamp, bool PressureFireActive, bool DoubleTapJump,
        bool FlickBoost, float FlickX, float FlickY,
        bool InProximity = false)
    {
        public bool PressureFirePressed { get; init; }
        public bool PressureFireReleased { get; init; }

        public static StylusState Empty => new(false, -1, 0, 0, 0,
            StylusButtons.None, StylusButtons.None, StylusButtons.None, 0, false,
            false, false, 0, 0);
    }
}
