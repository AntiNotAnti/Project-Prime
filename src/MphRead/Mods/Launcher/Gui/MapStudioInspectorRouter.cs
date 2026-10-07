using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.MapEditor;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.Gui
{
    internal sealed partial class MapStudioScreen
    {
        private void ShowInspectorPage(string name, bool remember=true)
        {
            if (remember) _inspectorPage=name;
            _inspectorTitle.Text = name.ToUpperInvariant();
            _toolSelectionState.Text = (_viewport?.ElementMode ?? "Object").ToUpperInvariant()
                + " MODE  //  " + (_viewport?.Tool ?? "Move").ToUpperInvariant();
            switch(name)
            {
                case "Modeling": ModelingInspector(); break;
                case "Partitioning": PartitionInspector(); break;
                case "Collision repairs": CollisionRepairInspector(); break;
                case "Environment": EnvironmentInspector(); break;
                case "Materials": MaterialInspector(); break;
                case "UV": _inspector.Children.Clear(); FaceUvControls(_inspector); if (_inspector.Children.Count==0) _inspector.Children.Add(Text("Select one or more authored mesh faces to edit UVs.")); break;
                case "Assets & music": AssetInspector(); break;
                case "Snapping": SnapInspector(); break;
                case "Arrange": ArrangeInspector(); break;
                case "Layers": LayerInspector(); break;
                case "Statistics":
                case "Map health": Statistics(); break;
                case "Navigation path": NavigationInspector(); break;
                case "Gameplay analysis": GameplayInspector(); break;
                case "Structural diff": StructuralDiffInspector(); break;
                case "Prefabs": PrefabInspector(); break;
                default: Inspect(); break;
            }
            RefreshStudioChrome();
        }

        private void RefreshStudioChrome()
        {
            bool loaded = _document != null;
            _studioSave.IsEnabled = loaded;
            _studioValidate.IsEnabled = loaded;
            _studioBuild.IsEnabled = loaded;
            _studioPlaytest.IsEnabled = loaded;

            if (!loaded)
            {
                _projectTitle.Text = "MAP STUDIO";
                _projectState.Text = "NO PROJECT LOADED";
                _projectState.Foreground = PrimeTheme.TextSecondaryBrush;
                _selectionState.Text = "0 SELECTED";
                _toolSelectionState.Text = "OBJECT MODE";
                return;
            }

            MapDefinition definition = _document!.Project.Definition;
            int objects = MapObjects.All(definition).Count();
            int selected = _document.Selection.Count;
            string displayName = String.IsNullOrWhiteSpace(definition.InGameName)
                ? definition.Name : definition.InGameName!;
            _projectTitle.Text = displayName.ToUpperInvariant();
            _projectState.Text = _document.IsDirty
                ? $"UNSAVED CHANGES  //  {objects} OBJECTS"
                : $"PROJECT SAVED  //  {objects} OBJECTS";
            _projectState.Foreground = _document.IsDirty
                ? PrimeTheme.WarningBrush : PrimeTheme.GreenBrush;
            _selectionState.Text = selected == 0
                ? $"{objects} OBJECTS  //  NOTHING SELECTED"
                : $"{selected} SELECTED  //  {objects} OBJECTS";
            _toolSelectionState.Text = (_viewport?.ElementMode ?? "Object").ToUpperInvariant()
                + " MODE  //  " + (_viewport?.Tool ?? "Move").ToUpperInvariant();
        }

    }
}
