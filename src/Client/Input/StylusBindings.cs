namespace MphRead.Mods.Input
{
    public enum StylusAction
    {
        None,
        Fire,
        Zoom
    }

    public readonly record struct StylusBindings(StylusAction Primary,
        StylusAction Secondary)
    {
        public static StylusBindings Default => new(StylusAction.Fire,
            StylusAction.Zoom);

        public bool IsDown(in StylusState state, StylusAction action)
        {
            StylusButtons active = state.Buttons | state.PressedButtons;
            return action != StylusAction.None
                && (Primary == action && active.HasFlag(StylusButtons.Primary)
                    || Secondary == action && active.HasFlag(StylusButtons.Secondary));
        }

        public bool IsPressed(in StylusState state, StylusAction action)
            => action != StylusAction.None
                && (Primary == action
                    && (state.PressedButtons & StylusButtons.Primary) != 0
                    || Secondary == action
                    && (state.PressedButtons & StylusButtons.Secondary) != 0);
    }
}
