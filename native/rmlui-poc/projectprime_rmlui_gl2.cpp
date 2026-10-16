#include "projectprime_rmlui_renderer.h"
#include "RmlUi_Renderer_GL2.h"
#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#include <GL/gl.h>
#elif defined(__APPLE__)
#include <OpenGL/gl.h>
#else
#include <GL/gl.h>
#endif

namespace {
class PrimeGl2Renderer final : public PrimeRenderer {
public:
    Rml::RenderInterface* Interface() override { return &renderer; }
    void SetViewport(int width, int height) override { renderer.SetViewport(width, height); }

    void BeginFrame() override
    {
        // The game and the RmlUi painter share one *compatibility* GL context.
        // RmlUi's GL2 backend uses fixed-function matrices/client pointers,
        // blending and stencil. Restore those exact states instead of assuming
        // a particular "default" state at the next world/HUD draw.
        glPushAttrib(GL_ALL_ATTRIB_BITS);
        glPushClientAttrib(GL_CLIENT_ALL_ATTRIB_BITS);
        glGetIntegerv(GL_MATRIX_MODE, &matrix_mode);
        glMatrixMode(GL_PROJECTION);
        glPushMatrix();
        glMatrixMode(GL_MODELVIEW);
        glPushMatrix();
        glMatrixMode(GL_TEXTURE);
        glPushMatrix();
        glMatrixMode(matrix_mode);

        // Only the final overlay pass uses this backend. The engine binds the
        // default draw framebuffer first; color/depth belong to the completed
        // world frame and must not be cleared here.
#if !defined(_WIN32)
        glActiveTexture(GL_TEXTURE0);
#endif
        glDisable(GL_DEPTH_TEST);
        glDepthMask(GL_FALSE);
        glDisable(GL_CULL_FACE);
        glDisable(GL_ALPHA_TEST);
        glDisable(GL_SCISSOR_TEST);
        renderer.BeginFrame();

        // RmlUi GL2 uses stencil for nested rounded clipping. At terminal
        // composition, only the stencil attachment may be cleared, never
        // world color or depth. The next world pass initializes its own state.
        glStencilMask(~0u);
        glClearStencil(0);
        glClear(GL_STENCIL_BUFFER_BIT);
    }

    void EndFrame() override
    {
        renderer.EndFrame();
        glMatrixMode(GL_TEXTURE);
        glPopMatrix();
        glMatrixMode(GL_MODELVIEW);
        glPopMatrix();
        glMatrixMode(GL_PROJECTION);
        glPopMatrix();
        glPopClientAttrib();
        glPopAttrib();
        glMatrixMode(matrix_mode);
    }

private:
    GLint matrix_mode = GL_MODELVIEW;
    RenderInterface_GL2 renderer;
};
}
std::unique_ptr<PrimeRenderer> CreatePrimeGl2Renderer()
{
    return std::make_unique<PrimeGl2Renderer>();
}
