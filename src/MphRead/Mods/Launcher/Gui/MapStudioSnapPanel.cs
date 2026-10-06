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
        private void SnapInspector()
        {
            _inspector.Children.Clear();if(_viewport==null)return;
            var grid=new TextBox {Text=_viewport.Snap.ToString(CultureInfo.InvariantCulture)};
            var angle=new TextBox {Text=_viewport.AngleSnap.ToString(CultureInfo.InvariantCulture)};
            var scale=new TextBox {Text=_viewport.ScaleSnap.ToString(CultureInfo.InvariantCulture)};
            var local=new CheckBox {Content="Local transform axes",IsChecked=_viewport.LocalAxes};
            var pivot=new ComboBox {ItemsSource=new[]{"Individual","Center","Active","World","Cursor"},SelectedItem=_viewport.PivotMode};
            var cursor=new TextBox {Text=$"{_viewport.CursorPivot.X},{_viewport.CursorPivot.Y},{_viewport.CursorPivot.Z}"};
            _inspector.Children.Add(Text("Pivot"));_inspector.Children.Add(pivot);
            _inspector.Children.Add(Text("Custom cursor X, Y, Z"));_inspector.Children.Add(cursor);
            pivot.SelectionChanged+=(_,_)=>{if(pivot.SelectedItem is string value)_viewport.PivotMode=value;};
            AddButton(_inspector,"Set cursor",()=>{try{var v=ParseVector(cursor.Text??"",3);_viewport.CursorPivot=new(v[0],v[1],v[2]);}catch(Exception ex){Failure(ex);}});
            _inspector.Children.Add(Text("Grid spacing (0 disables snapping)"));_inspector.Children.Add(grid);
            _inspector.Children.Add(Text("Rotation step (degrees)"));_inspector.Children.Add(angle);
            _inspector.Children.Add(Text("Scale step"));_inspector.Children.Add(scale);_inspector.Children.Add(local);
            AddButton(_inspector,"Apply",()=>{try{float g=Number(grid.Text??""),a=Number(angle.Text??""),s=Number(scale.Text??"");if(g<0||g>100||a<1||a>180||s<=0||s>10)throw new FormatException("Use grid spacing 0–100, rotation step 1–180 and scale step above 0 through 10.");_viewport.Snap=g;_viewport.AngleSnap=a;_viewport.ScaleSnap=s;_viewport.LocalAxes=local.IsChecked==true;}catch(Exception ex){Failure(ex);}});
        }
    }
}
