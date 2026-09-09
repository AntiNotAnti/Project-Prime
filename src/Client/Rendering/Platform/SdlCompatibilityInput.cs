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
        {
            var currentKeys = new HashSet<Keys>();
            foreach (int raw in snapshot.Keys)
            {
                Keys key = (Keys)raw;
                if (key != Keys.Unknown) currentKeys.Add(key);
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

            var currentButtons = new HashSet<MouseButton>();
            foreach (int raw in snapshot.MouseButtons) currentButtons.Add((MouseButton)raw);
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
