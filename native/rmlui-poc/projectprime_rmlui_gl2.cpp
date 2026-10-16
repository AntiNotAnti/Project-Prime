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
        // Overlay in the engine's frame: never clear its color buffer.
#if !defined(_WIN32)
        glActiveTexture(GL_TEXTURE0);
#endif
        glDisable(GL_DEPTH_TEST);
        glDepthMask(GL_FALSE);
        glDisable(GL_CULL_FACE);
        glDisable(GL_ALPHA_TEST);
        renderer.BeginFrame();
        glClearStencil(0);
        glClear(GL_STENCIL_BUFFER_BIT);
    }
    void EndFrame() override
    {
        renderer.EndFrame();
        glDepthMask(GL_TRUE);
        glDisableClientState(GL_VERTEX_ARRAY);
        glDisableClientState(GL_COLOR_ARRAY);
        glDisableClientState(GL_TEXTURE_COORD_ARRAY);
        glDisable(GL_SCISSOR_TEST);
        glDisable(GL_STENCIL_TEST);
        glDisable(GL_TEXTURE_2D);
        glBindTexture(GL_TEXTURE_2D, 0);
        glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        glEnable(GL_DEPTH_TEST);
        glMatrixMode(GL_MODELVIEW);
    }
private:
    RenderInterface_GL2 renderer;
};
}
std::unique_ptr<PrimeRenderer> CreatePrimeGl2Renderer()
{
    return std::make_unique<PrimeGl2Renderer>();
}
