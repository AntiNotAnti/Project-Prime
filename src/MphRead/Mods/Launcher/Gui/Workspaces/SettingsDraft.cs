#if MPHREAD_AVALONIA
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using MphRead.Mods.Input;

namespace MphRead.Mods.Launcher.Gui
{
    // Capture values, not controls reconstructed from settings.json. This preserves
    // scroll, focus and category state during Apply/Discard and route changes.
    internal sealed class SettingsDraft
    {
        private readonly List<(Control? Owner, Func<object?> Read, Action<object?> Write, object? Saved)> _values = new();
        private readonly Dictionary<GamepadRuntimeConfig, string[]> _pads = new();
        private readonly Control? _controlsScope;

        public SettingsDraft(Control pages, Control? controlsScope = null)
        {
            _controlsScope = controlsScope;
            foreach (var node in pages.GetLogicalDescendants().OfType<Control>())
            {
                if (node.GetLogicalAncestors().OfType<GamepadSettingsPanel>().Any()) continue;
                if (node.Tag is "settings.archive.path") continue;
                switch (node)
                {
                    case ChoiceRow row: Add(node, () => row.Index, v => row.Index = (int)v!); break;
                    case SliderRow row: Add(node, () => row.Value, v => row.Value = (int)v!); break;
                    case ToggleRow row: Add(node, () => row.On, v => row.On = (bool)v!); break;
                    case ButtonToggleRow row: Add(node, () => row.On, v => row.On = (bool)v!); break;
                    case FieldRow row: Add(node, () => row.Value, v => row.Value = (string)v!); break;
                }
            }

            // Keyboard/mouse bindings and the static input options mutate outside
            // their drawn row objects. Associate them with the Controls category
            // so Reset Category can restore that whole surface without touching
            // Display, Graphics, Audio or the rest of the draft.
            foreach (var property in InputSettings.Bindings)
            {
                Add(_controlsScope, () =>
                {
                    var bind = InputSettings.Bind(property);
                    return (bind.Type, bind.Key, bind.MouseButton);
                }, v =>
                {
                    var saved = ((MphRead.Entities.ButtonType Type,
                        OpenTK.Windowing.GraphicsLibraryFramework.Keys Key,
                        OpenTK.Windowing.GraphicsLibraryFramework.MouseButton MouseButton))v!;
                    // Restore the exact snapshot, including unused key fields in
                    // default mouse bindings; Rebind normalizes those fields.
                    var bind = InputSettings.Bind(property);
                    bind.Type = saved.Type;
                    bind.Key = saved.Key;
                    bind.MouseButton = saved.MouseButton;
                });
            }
            foreach (var property in typeof(InputSettings).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (property.CanRead && property.CanWrite
                    && (property.PropertyType.IsValueType || property.PropertyType == typeof(string)))
                    Add(_controlsScope, () => property.GetValue(null), v => property.SetValue(null, v));
            }
            TrackController();
        }

        private void Add(Control? owner, Func<object?> read, Action<object?> write) =>
            _values.Add((owner, read, write, read()));

        public void TrackController()
        {
            var runtime = GamepadRuntimeConfig.Current;
            if (!_pads.ContainsKey(runtime))
                _pads.Add(runtime, Lines(runtime));
        }

        private static string[] Lines(GamepadRuntimeConfig runtime)
        {
            var lines = new List<string>();
            runtime.Options.Write(lines);
            runtime.Bindings.Write(lines);
            lines.Add("prime_preset=" + runtime.Bindings.Preset);
            return lines.ToArray();
        }

        public bool IsDirty => _values.Any(v => !Equals(v.Saved, v.Read()))
            || _pads.Any(p => !p.Value.SequenceEqual(Lines(p.Key)));

        public bool IsDirtyIn(Control scope)
        {
            bool values = _values.Any(v => v.Owner != null && InScope(v.Owner, scope)
                && !Equals(v.Saved, v.Read()));
            bool pads = _controlsScope != null && ReferenceEquals(scope, _controlsScope)
                && _pads.Any(p => !p.Value.SequenceEqual(Lines(p.Key)));
            return values || pads;
        }

        public void Accept()
        {
            for (int i = 0; i < _values.Count; i++)
            {
                var v = _values[i];
                _values[i] = (v.Owner, v.Read, v.Write, v.Read());
            }
            foreach (var pad in _pads.Keys.ToArray())
                _pads[pad] = Lines(pad);
        }

        /// <summary>
        /// Restore only values owned by one settings category to the snapshot
        /// taken when this Settings view opened.
        /// </summary>
        public void Reset(Control scope)
        {
            foreach (var value in _values)
            {
                if (value.Owner != null && InScope(value.Owner, scope))
                    value.Write(value.Saved);
            }
            if (_controlsScope != null && ReferenceEquals(scope, _controlsScope))
                RestorePads();
        }

        private static bool InScope(Control owner, Control scope) =>
            ReferenceEquals(owner, scope)
            || owner.GetLogicalAncestors().OfType<Control>().Any(ancestor => ReferenceEquals(ancestor, scope));

        public void Discard()
        {
            foreach (var v in _values)
                v.Write(v.Saved);
            RestorePads();
        }

        private void RestorePads()
        {
            foreach (var (pad, lines) in _pads)
            {
                pad.Options.Load(lines);
                pad.Bindings.Reset();
                foreach (var line in lines)
                {
                    int split = line.IndexOf('=');
                    if (split < 0) continue;
                    string key = line[..split], value = line[(split + 1)..];
                    if (key != "prime_preset")
                        pad.Bindings.TryLoad(key, value);
                }
                pad.Bindings.LoadSlots(lines);
                // Loading individual slots marks the runtime Custom; retain the
                // original preset only after all bindings have been restored.
                pad.Bindings.Preset = lines.First(line =>
                    line.StartsWith("prime_preset=", StringComparison.Ordinal))[13..];
            }
        }
    }
}
#endif
