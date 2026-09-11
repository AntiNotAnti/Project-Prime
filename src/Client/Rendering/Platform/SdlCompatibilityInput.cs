using System;
using System.Collections.Generic;
using System.Reflection;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead
{
    /// <summary>
    /// Feeds SDL's neutral snapshot into the existing OpenTK-compatible
    /// input objects. OpenTK keeps constructors/setters internal, the same
    /// constraint Android already handles with these delegates; no gameplay
    /// input fork is introduced for SDL.
    /// </summary>
    internal sealed class SdlCompatibilityInput
    {
        public KeyboardState Keyboard { get; }
        public MouseState Mouse { get; }

        private readonly Action<KeyboardState, Keys, bool> _setKey;
        private readonly Action<MouseState, Vector2> _setPosition;
        private readonly Action<MouseState, MouseButton, bool> _setButton;
        private readonly HashSet<Keys> _previousKeys = new();
        private readonly HashSet<MouseButton> _previousButtons = new();
        private readonly HashSet<Keys> _quarantinedKeys = new();
        private readonly HashSet<MouseButton> _quarantinedButtons = new();

        public SdlCompatibilityInput()
        {
            Type keyboardType = typeof(KeyboardState);
            Type mouseType = typeof(MouseState);
            Keyboard = (KeyboardState)Activator.CreateInstance(keyboardType, nonPublic: true)!;
            Mouse = (MouseState)Activator.CreateInstance(mouseType, nonPublic: true)!;
            _setKey = Bind<Action<KeyboardState, Keys, bool>>(
                keyboardType.GetMethod("SetKeyState", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                "KeyboardState.SetKeyState");
            _setPosition = Bind<Action<MouseState, Vector2>>(
                mouseType.GetProperty("Position", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetMethod,
                "MouseState.Position setter");
            _setButton = Bind<Action<MouseState, MouseButton, bool>>(
                mouseType.GetProperty("Item", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetMethod,
                "MouseState button setter");
        }

        public void Apply(WindowInputSnapshot snapshot)
            => Apply(snapshot, suppressGameplayInput: false);

        public void Apply(WindowInputSnapshot snapshot, bool suppressGameplayInput)
        {
            // A default value is used when the native window has no event
            // snapshot. The readonly struct's auto-properties are null in
            // that case, so treat it as an empty frame before enumerating.
            if (snapshot.Keys == null && snapshot.MouseButtons == null)
            {
                foreach (Keys key in _previousKeys) _setKey(Keyboard, key, false);
                _previousKeys.Clear();
                foreach (MouseButton button in _previousButtons)
                    _setButton(Mouse, button, false);
                _previousButtons.Clear();
                _quarantinedKeys.Clear();
                _quarantinedButtons.Clear();
                _setPosition(Mouse, snapshot.MousePosition);
                return;
            }
            // Normalize each collection independently. A partially populated
            // snapshot is still valid, and must not rely on the both-null
            // default-frame check above to satisfy nullable flow analysis.
            IEnumerable<int> snapshotKeys = snapshot.Keys is { } keys
                ? keys
                : Array.Empty<int>();
            IEnumerable<int> snapshotMouseButtons = snapshot.MouseButtons is { } buttons
                ? buttons
                : Array.Empty<int>();
            var currentKeys = new HashSet<Keys>();
            foreach (int raw in snapshotKeys)
            {
                Keys key = (Keys)raw;
                if (key != Keys.Unknown) currentKeys.Add(key);
            }
            var currentButtons = new HashSet<MouseButton>();
            foreach (int raw in snapshotMouseButtons)
            {
                MouseButton button = (MouseButton)raw;
                if (button != MouseButton.Last) currentButtons.Add(button);
            }

            if (suppressGameplayInput)
            {
                // Snapshot the physical hold before neutralizing the
                // compatibility state. The quarantine survives the replay
                // until each key/button is physically released.
                _quarantinedKeys.UnionWith(currentKeys);
                _quarantinedButtons.UnionWith(currentButtons);
                foreach (Keys key in _previousKeys) _setKey(Keyboard, key, false);
                foreach (Keys key in currentKeys) _setKey(Keyboard, key, false);
                foreach (MouseButton button in _previousButtons)
                    _setButton(Mouse, button, false);
                foreach (MouseButton button in currentButtons)
                    _setButton(Mouse, button, false);
                _previousKeys.Clear();
                _previousButtons.Clear();
                _setPosition(Mouse, snapshot.MousePosition);
                return;
            }
            else
            {
                _quarantinedKeys.RemoveWhere(key => !currentKeys.Contains(key));
                _quarantinedButtons.RemoveWhere(button => !currentButtons.Contains(button));
                currentKeys.ExceptWith(_quarantinedKeys);
                currentButtons.ExceptWith(_quarantinedButtons);
            }

            foreach (Keys key in _previousKeys)
            {
                if (!currentKeys.Contains(key)) _setKey(Keyboard, key, false);
            }
            foreach (Keys key in currentKeys)
            {
                _setKey(Keyboard, key, true);
            }
            _previousKeys.Clear();
            _previousKeys.UnionWith(currentKeys);

            foreach (MouseButton button in _previousButtons)
            {
                if (!currentButtons.Contains(button)) _setButton(Mouse, button, false);
            }
            foreach (MouseButton button in currentButtons) _setButton(Mouse, button, true);
            _previousButtons.Clear();
            _previousButtons.UnionWith(currentButtons);
            _setPosition(Mouse, snapshot.MousePosition);
        }

        private static T Bind<T>(MethodInfo? method, string what) where T : Delegate
        {
            if (method == null) throw new ProgramException($"{what} is unavailable in this OpenTK build.");
            return (T)method.CreateDelegate(typeof(T));
        }
    }
}
