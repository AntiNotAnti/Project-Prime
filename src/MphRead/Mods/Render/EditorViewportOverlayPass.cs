#if MPHREAD_SHELL
using System;
using System.Collections.Generic;
using MphRead.Mods.MapEditor;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    private MapViewportOverlay? _editorOverlaySource;
    private readonly List<RenderItem> _editorOverlayItems=new();
    private void DrawNativeEditorOverlay(MapViewportOverlay overlay)
    {
        // Called only inside DrawEditorFrame's shader, matrices, scissor and
        // PushAttrib guard. No renderer, shader, context or simulation is added.
        if(!ReferenceEquals(_editorOverlaySource,overlay))
        {
            ReleaseNativeEditorOverlay();
            try
            {
                foreach(var batch in overlay.Batches)
                {
                    if(_editorOverlayItems.Count>=8)break;
                    if(batch.Vertices.Count==0)continue;
                    int list=GL.GenLists(1);
                    if(list==0)throw new InvalidOperationException("The renderer could not allocate the editor selection overlay.");
                    try
                    {
                        GL.NewList(list,ListMode.Compile);GL.TexCoord3(0f,0f,0f);GL.Normal3(0f,1f,0f);
                        GL.Begin(batch.Primitive==MapViewportOverlayPrimitive.Lines?PrimitiveType.Lines:PrimitiveType.Points);
                        foreach(var vertex in batch.Vertices)GL.Vertex3(vertex.X,vertex.Y,vertex.Z);
                        GL.End();GL.EndList();
                        var item=EditorItem(list);item.OverrideColor=new(batch.Color.X,batch.Color.Y,batch.Color.Z,batch.Color.W);
                        item.Lighting=false;item.HasTexture=false;_editorOverlayItems.Add(item);
                    }
                    catch{GL.DeleteLists(list,1);throw;}
                }
                _editorOverlaySource=overlay;
            }
            catch{ReleaseNativeEditorOverlay();throw;}
        }
        GL.DepthMask(false);
        int itemIndex=0;
        foreach(var batch in overlay.Batches)
        {
            if(batch.Vertices.Count==0)continue;
            if(itemIndex>=_editorOverlayItems.Count)break;
            if(batch.DepthTest)GL.Enable(EnableCap.DepthTest);else GL.Disable(EnableCap.DepthTest);
            if(batch.Primitive==MapViewportOverlayPrimitive.Lines)GL.LineWidth(Math.Clamp(batch.Size,1,6));else OpenTK.Graphics.OpenGL.GL.PointSize(Math.Clamp(batch.Size,1,12));
            RenderItem(_editorOverlayItems[itemIndex++]);
        }
    }
    private void ReleaseNativeEditorOverlay()
    {
        foreach(var item in _editorOverlayItems)GL.DeleteLists(item.ListId,1);
        _editorOverlayItems.Clear();_editorOverlaySource=null;
    }
}
#endif
