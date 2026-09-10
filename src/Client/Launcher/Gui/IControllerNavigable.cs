using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Gui
{
    /// <summary>Native focus action; no pointer or cursor synthesis.</summary>
    internal interface IControllerNavigable
    {
        void ControllerActivate();
        void ControllerAdjust(int direction);
    }

    internal enum ControllerNavigationSurface
    {
        Launcher,
        Lobby,
        Settings,
        HunterSelection,
        MapSelection,
        Vote,
        Results,
        Rematch,
        ReturnToLobby,
        Dialogs
    }

    /// <summary>
    /// Reviewable inventory of controller-reachable UI surfaces. Shell controls
    /// use focus activation/adjustment; in-game vote and results controls consume
    /// normalized bindings directly. Neither route synthesizes pointer input.
    /// </summary>
    internal static class ControllerNavigationContract
    {
        private static readonly ControllerNavigationSurface[] _surfaces =
        [
            ControllerNavigationSurface.Launcher,
            ControllerNavigationSurface.Lobby,
            ControllerNavigationSurface.Settings,
            ControllerNavigationSurface.HunterSelection,
            ControllerNavigationSurface.MapSelection,
            ControllerNavigationSurface.Vote,
            ControllerNavigationSurface.Results,
            ControllerNavigationSurface.Rematch,
            ControllerNavigationSurface.ReturnToLobby,
            ControllerNavigationSurface.Dialogs
        ];

        public static ReadOnlySpan<ControllerNavigationSurface> Surfaces => _surfaces;
        public const bool UsesFocusOrNormalizedBindings = true;
        public const bool SynthesizesPointerInput = false;
    }

    internal enum ControllerComboSelector { Generic, Map, Mode, Bots, Hunter }
    internal enum ControllerComboCommand { Accept, Cancel, Previous, Next }

    internal readonly record struct ControllerComboState(
        ControllerComboSelector Selector, int SelectedIndex, int OriginalIndex,
        bool IsOpen);

    internal static class ControllerComboNavigation
    {
        public static ControllerComboState Transition(in ControllerComboState state,
            int itemCount, ControllerComboCommand command)
        {
            if (command == ControllerComboCommand.Accept)
                return state with
                {
                    OriginalIndex = state.IsOpen ? state.OriginalIndex : state.SelectedIndex,
                    IsOpen = !state.IsOpen
                };
            if (command == ControllerComboCommand.Cancel)
                return state with { SelectedIndex = state.OriginalIndex, IsOpen = false };
            if (!state.IsOpen || itemCount <= 0) return state;
            int step = command == ControllerComboCommand.Next ? 1 : -1;
            int selected = Math.Clamp(state.SelectedIndex + step, 0, itemCount - 1);
            return state with { SelectedIndex = selected };
        }
    }

    /// <summary>
    /// Defers a controller-opened selector's external callback until accept.
    /// Preview and cancel remain entirely local to the current control tree.
    /// </summary>
    internal sealed class DeferredControllerSelection<T>
    {
        private readonly Action<T> _commit;
        private T _committed;
        private T _preview;

        public DeferredControllerSelection(T committed, Action<T> commit)
        {
            _committed = _preview = committed;
            _commit = commit;
        }

        public bool Active { get; private set; }
        public T PreviewValue => _preview;

        public void Begin(T value)
        {
            _committed = _preview = value;
            Active = true;
        }

        public void Preview(T value)
        {
            if (Active) _preview = value;
        }

        public T Cancel()
        {
            _preview = _committed;
            Active = false;
            return _committed;
        }

        public bool Accept()
        {
            if (!Active) return false;
            Active = false;
            if (EqualityComparer<T>.Default.Equals(_preview, _committed)) return false;
            _committed = _preview;
            _commit(_committed);
            return true;
        }

        public bool CommitImmediate(T value)
        {
            if (Active || EqualityComparer<T>.Default.Equals(value, _committed))
                return false;
            _committed = _preview = value;
            _commit(value);
            return true;
        }
    }
}
