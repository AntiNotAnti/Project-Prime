using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private void ApplyMeshEnhancement(MapMesh mesh,string label,Func<Func<MapMesh,MapMesh>> prepare)
    {
        try
        {
            if(_document is not {} document)throw new InvalidOperationException("Open a map first.");
            var operation=prepare(); // Capture selection and form values on their dispatcher owner.
            var snapshot=document.CaptureBuildSnapshot();
            _=Job(label,async token=>
            {
                var proposed=await Task.Run(()=>
                {
                    token.ThrowIfCancellationRequested();
                    var source=snapshot.CreateDefinition().Geometry.OfType<MapMesh>().Single(item=>item.Id==mesh.Id);
                    var result=operation(source);token.ThrowIfCancellationRequested();return result;
                },token);
                GuardJob(token);
                document.EditObjects(label,new[]{mesh.Id},definition=>
                {
                    int index=definition.Geometry.FindIndex(geometry=>geometry.Id==mesh.Id);
                    if(index<0)throw new InvalidOperationException("Mesh no longer exists.");
                    definition.Geometry[index]=proposed;
                });
                _viewport?.SubSelection.Clear();ModelingInspector();
                _status.Text=label+" complete · one undo step";
            });
        }
        catch(Exception ex){Failure(ex);}
    }
    private static Vector3 ModelingVector(TextBox field)
    {float[] values=ParseVector(field.Text??"",3);return new(values[0],values[1],values[2]);}
    private void AppendModelingEnhancements(MapMesh mesh)
    {
        _inspector.Children.Add(Text("MODIFIER STACK"));
        _inspector.Children.Add(Text("Mirror and array remain editable until baked. Bake the stack before changing vertices, faces, UVs or face materials."));
        var modifiers=(mesh.ModifierSource?.Modifiers ?? new()).ToArray();
        void Stack(string label,MapModelModifier[] values)=>ApplyMeshEnhancement(mesh,label,()=>source=>MapModelingEnhancements.WithModifierStack(source,values));
        for(int i=0;i<modifiers.Length;i++)
        {
            int index=i;var modifier=modifiers[i];
            _inspector.Children.Add(Text($"{i+1}. "+(modifier is MapMirrorModifier ? "Mirror" : "Array")));
            var enabled=new CheckBox {Content="Enabled",IsChecked=modifier.Enabled};_inspector.Children.Add(enabled);
            Func<MapModelModifier> Read;
            if(modifier is MapMirrorModifier mirror)
            {
                var axis=new ComboBox {ItemsSource=new[]{"X","Y","Z"},SelectedIndex=mirror.Axis};
                var offset=new TextBox {Text=mirror.Offset.ToString(CultureInfo.InvariantCulture)};
                var weld=new CheckBox {Content="Weld mirror plane",IsChecked=mirror.WeldPlane};
                _inspector.Children.Add(axis);_inspector.Children.Add(Text("Plane local offset"));_inspector.Children.Add(offset);_inspector.Children.Add(weld);
                Read=()=>new MapMirrorModifier(axis.SelectedIndex,Number(offset.Text??"0"),weld.IsChecked==true,enabled.IsChecked==true);
            }
            else if(modifier is MapArrayModifier array)
            {
                var count=new TextBox {Text=array.Count.ToString(CultureInfo.InvariantCulture)};
                var offset=new TextBox {Text=string.Join(",",new[]{array.Offset.X,array.Offset.Y,array.Offset.Z}.Select(value=>value.ToString(CultureInfo.InvariantCulture)))};
                _inspector.Children.Add(Text("Total count / local offset X,Y,Z"));_inspector.Children.Add(count);_inspector.Children.Add(offset);
                Read=()=>new MapArrayModifier(int.Parse(count.Text??"1",CultureInfo.InvariantCulture),ModelingVector(offset),enabled.IsChecked==true);
            }
            else continue;
            AddButton(_inspector,"Apply modifier settings",()=> {try {var values=modifiers.ToArray();values[index]=Read();Stack("Edit modifier",values);}catch(Exception ex){Failure(ex);} });
            if(index>0)AddButton(_inspector,"Move modifier up",()=> {var values=modifiers.ToArray();(values[index-1],values[index])=(values[index],values[index-1]);Stack("Reorder modifiers",values);});
            if(index+1<modifiers.Length)AddButton(_inspector,"Move modifier down",()=> {var values=modifiers.ToArray();(values[index],values[index+1])=(values[index+1],values[index]);Stack("Reorder modifiers",values);});
            AddButton(_inspector,"Remove modifier",()=>Stack("Remove modifier",modifiers.Where((_,position)=>position!=index).ToArray()));
        }
        var mirrorAxis=new ComboBox {ItemsSource=new[]{"X","Y","Z"},SelectedIndex=0};var mirrorOffset=new TextBox {Text="0"};
        var mirrorWeld=new CheckBox {Content="Weld mirror plane",IsChecked=true};
        _inspector.Children.Add(Text("Add mirror · local plane"));_inspector.Children.Add(mirrorAxis);_inspector.Children.Add(mirrorOffset);_inspector.Children.Add(mirrorWeld);
        AddButton(_inspector,"Add mirror modifier",()=> {try {Stack("Add mirror modifier",modifiers.Append(new MapMirrorModifier(mirrorAxis.SelectedIndex,Number(mirrorOffset.Text??"0"),mirrorWeld.IsChecked==true)).ToArray());}catch(Exception ex){Failure(ex);} });
        var arrayCount=new TextBox {Text="3"};var arrayOffset=new TextBox {Text="2,0,0"};
        _inspector.Children.Add(Text("Add array · total count / local offset X,Y,Z"));_inspector.Children.Add(arrayCount);_inspector.Children.Add(arrayOffset);
        AddButton(_inspector,"Add array modifier",()=> {try {Stack("Add array modifier",modifiers.Append(new MapArrayModifier(int.Parse(arrayCount.Text??"3",CultureInfo.InvariantCulture),ModelingVector(arrayOffset))).ToArray());}catch(Exception ex){Failure(ex);} });
        if(mesh.ModifierSource!=null)
        {
            AddButton(_inspector,"Bake modifier stack",()=>ApplyMeshEnhancement(mesh,"Bake modifier stack",()=>MapModelingEnhancements.BakeModifierStack));
            return;
        }

        _inspector.Children.Add(Text("PROPORTIONAL EDIT & TOPOLOGY"));
        var delta=new TextBox {Text="0,0.25,0"};var radius=new TextBox {Text="2"};
        var falloff=new ComboBox {ItemsSource=Enum.GetValues<MapProportionalFalloff>(),SelectedItem=MapProportionalFalloff.Smooth};
        var connected=new CheckBox {Content="Connected vertices only",IsChecked=true};
        _inspector.Children.Add(Text("World delta X,Y,Z / radius"));_inspector.Children.Add(delta);_inspector.Children.Add(radius);_inspector.Children.Add(falloff);_inspector.Children.Add(connected);
        AddButton(_inspector,"Move with falloff",()=>ApplyMeshEnhancement(mesh,"Proportional move",()=>
        {
            int[] vertices=_viewport?.SubSelection.VertexIndices(mesh,_viewport.ElementMode).ToArray()??Array.Empty<int>();
            var movement=ModelingVector(delta);float distance=Number(radius.Text??"2");var curve=(MapProportionalFalloff)(falloff.SelectedItem??MapProportionalFalloff.Smooth);bool linked=connected.IsChecked==true;
            return source=>MapModelingEnhancements.ProportionalMove(source,vertices,movement,distance,curve,linked);
        }));
        var inset=new TextBox {Text="0.25"};_inspector.Children.Add(Text("Inset constant world width"));_inspector.Children.Add(inset);
        AddButton(_inspector,"Inset selected region with constant width",()=>ApplyMeshEnhancement(mesh,"Constant-width inset",()=>
        {int[] faces=_viewport?.SelectedFaceIndices.ToArray()??Array.Empty<int>();float width=Number(inset.Text??".25");return source=>MapModelingEnhancements.InsetRegionWidth(source,faces,width);}));
        var bevelWidth=new TextBox {Text="0.1"};var bevelSegments=new TextBox {Text="1"};
        _inspector.Children.Add(Text("Bevel selected edge · width / segments"));_inspector.Children.Add(bevelWidth);_inspector.Children.Add(bevelSegments);
        AddButton(_inspector,"Bevel closed mesh edge",()=>ApplyMeshEnhancement(mesh,"Bevel mesh edge",()=>
        {var edge=_viewport?.SubSelection.ActiveEdge??throw new InvalidOperationException("Select one mesh edge.");float width=Number(bevelWidth.Text??".1");int count=int.Parse(bevelSegments.Text??"1",CultureInfo.InvariantCulture);return source=>MapModelingEnhancements.BevelEdge(source,edge,width,count);}));
        var percentage=new TextBox {Text="0.5"};var cuts=new TextBox {Text="1"};
        _inspector.Children.Add(Text("Loop cut percentage (0–1) / count"));_inspector.Children.Add(percentage);_inspector.Children.Add(cuts);
        AddButton(_inspector,"Cut quad loop",()=>ApplyMeshEnhancement(mesh,"Loop cut",()=>
        {var edge=_viewport?.SubSelection.ActiveEdge??throw new InvalidOperationException("Select a starting edge on a quad strip.");float fraction=Number(percentage.Text??".5");int count=int.Parse(cuts.Text??"1",CultureInfo.InvariantCulture);return source=>MapModelingEnhancements.LoopCut(source,edge,fraction,count);}));
        var normal=new TextBox {Text="1,0,0"};var plane=new TextBox {Text="0"};
        _inspector.Children.Add(Text("Knife plane world normal X,Y,Z / distance"));_inspector.Children.Add(normal);_inspector.Children.Add(plane);
        AddButton(_inspector,"Cut with plane",()=>ApplyMeshEnhancement(mesh,"Knife plane cut",()=>
        {var direction=ModelingVector(normal);float distance=Number(plane.Text??"0");return source=>MapModelingEnhancements.KnifeCut(source,direction,distance);}));
        var segments=new TextBox {Text="1"};var twist=new TextBox {Text="0"};
        _inspector.Children.Add(Text("Bridge complete boundary loops · segments / twist"));_inspector.Children.Add(segments);_inspector.Children.Add(twist);
        AddButton(_inspector,"Bridge boundary loops",()=>ApplyMeshEnhancement(mesh,"Bridge loops",()=>
        {var edges=_viewport?.SubSelection.Edges.ToArray()??throw new InvalidOperationException("Select boundary edges.");int count=int.Parse(segments.Text??"1",CultureInfo.InvariantCulture),rotation=int.Parse(twist.Text??"0",CultureInfo.InvariantCulture);return source=>MapModelingEnhancements.Bridge(source,edges,count,rotation);}));
        var columns=new TextBox {Text="1"};_inspector.Children.Add(Text("Grid fill planar convex boundary · columns"));_inspector.Children.Add(columns);
        AddButton(_inspector,"Fill boundary with quads",()=>ApplyMeshEnhancement(mesh,"Grid fill",()=>
        {var edges=_viewport?.SubSelection.Edges.ToArray()??throw new InvalidOperationException("Select boundary edges.");int count=int.Parse(columns.Text??"1",CultureInfo.InvariantCulture);return source=>MapModelingEnhancements.GridFill(source,edges,count);}));
        _inspector.Children.Add(Text("Operations prepare and validate a detached mesh in a background job. Cancellation, invalid topology or an intervening document edit leave the map unchanged."));
    }
}
