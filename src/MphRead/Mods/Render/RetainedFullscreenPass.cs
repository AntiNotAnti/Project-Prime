using System;
using OpenTK.Graphics.OpenGL;

namespace MphRead
{
    public partial class Scene
    {
        private int _retainedFullscreenQuadList;

        /// <summary>
        /// Reuse one display-list-backed fullscreen quad across outline,
        /// post-process, tone-map and final-composite passes. On modern
        /// backends the list is retained as native vertex/index buffers, so
        /// these passes no longer rebuild immediate-mode geometry every time.
        /// </summary>
        private void DrawRetainedFullscreenQuad()
        {
            if (_retainedFullscreenQuadList == 0)
            {
                _retainedFullscreenQuadList = GL.GenLists(1);
                if (_retainedFullscreenQuadList == 0)
                    throw new InvalidOperationException(
                        "The renderer could not allocate the retained fullscreen quad.");

                GL.NewList(_retainedFullscreenQuadList, ListMode.Compile);
                GL.Begin(PrimitiveType.TriangleStrip);
                GL.TexCoord3(1f, 1f, 0f); GL.Vertex3(1f, 1f, 0f);
                GL.TexCoord3(0f, 1f, 0f); GL.Vertex3(-1f, 1f, 0f);
                GL.TexCoord3(1f, 0f, 0f); GL.Vertex3(1f, -1f, 0f);
                GL.TexCoord3(0f, 0f, 0f); GL.Vertex3(-1f, -1f, 0f);
                GL.End();
                GL.EndList();
            }
            GL.CallList(_retainedFullscreenQuadList);
        }

        private void DisposeRetainedFullscreenQuad()
        {
            if (_retainedFullscreenQuadList == 0)
                return;
            GL.DeleteLists(_retainedFullscreenQuadList, 1);
            _retainedFullscreenQuadList = 0;
        }
    }
}
