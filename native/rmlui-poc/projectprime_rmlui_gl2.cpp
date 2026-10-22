#include "projectprime_rmlui_renderer.h"
#include "RmlUi_Renderer_GL2.h"
#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#include <GL/gl.h>
#elif defined(__APPLE__)
#include <OpenGL/gl.h>
#else
#define GL_GLEXT_PROTOTYPES
#include <GL/gl.h>
#endif
#include <cstdlib>
#include <cstring>
#if !defined(_WIN32)
#include <dlfcn.h>
#endif

#ifndef GL_ACTIVE_TEXTURE
#define GL_ACTIVE_TEXTURE 0x84E0
#define GL_TEXTURE0 0x84C0
#endif
#ifndef GL_PIXEL_UNPACK_BUFFER
#define GL_PIXEL_UNPACK_BUFFER 0x88EC
#define GL_PIXEL_UNPACK_BUFFER_BINDING 0x88EF
#endif
#ifndef GL_CLAMP_TO_EDGE
#define GL_CLAMP_TO_EDGE 0x812F
#endif

namespace {
class PrimeGl2Renderer final : public PrimeRenderer {
public:
    Rml::RenderInterface* Interface() override { return &renderer; }
    void SetViewport(int width, int height) override { renderer.SetViewport(width, height); }
    Rml::TextureHandle CreateCanvasTexture(const uint8_t* pixels, int width, int height, const PrimeTextureSampling& sampling) override
    {
        if (!pixels || width <= 0 || height <= 0 || width > 512 || height > 512) return 0;
#if defined(_WIN32)
        // WGL entry points are context-specific. Resolve at upload time, never cache across context recreation.
        using ActiveTexture = void (APIENTRY*)(GLenum);
        using BindBuffer = void (APIENTRY*)(GLenum, GLuint);
        auto resolve = [](const char* core, const char* extension) {
            auto valid = [](PROC value) { const auto n = reinterpret_cast<uintptr_t>(value); return n > 3 && n != UINTPTR_MAX; };
            PROC value = wglGetProcAddress(core);
            if (!valid(value)) value = wglGetProcAddress(extension);
            return valid(value) ? value : nullptr;
        };
        const auto active = reinterpret_cast<ActiveTexture>(resolve("glActiveTexture", "glActiveTextureARB"));
        const auto bind_buffer = reinterpret_cast<BindBuffer>(resolve("glBindBuffer", "glBindBufferARB"));
        if (!active) return 0;
#else
        const auto active = glActiveTexture;
        const auto bind_buffer = glBindBuffer;
#endif
        const char* version = reinterpret_cast<const char*>(glGetString(GL_VERSION));
        if (!version) return 0;
        int major = std::atoi(version), minor = 0;
        if (const char* dot = std::strchr(version, '.')) minor = std::atoi(dot + 1);
        bool pbo = major > 2 || (major == 2 && minor >= 1);
        if (!pbo) {
            const char* extensions = reinterpret_cast<const char*>(glGetString(GL_EXTENSIONS));
            pbo = extensions && std::strstr(extensions, "GL_ARB_pixel_buffer_object");
        }
        if (pbo && !bind_buffer) return 0;
        GLint old_unit = 0, old_texture = 0, old_pbo = 0;
        GLint alignment = 0, row_length = 0, skip_rows = 0, skip_pixels = 0;
        glGetIntegerv(GL_ACTIVE_TEXTURE, &old_unit); active(GL_TEXTURE0);
        glGetIntegerv(GL_TEXTURE_BINDING_2D, &old_texture);
        if (pbo) { glGetIntegerv(GL_PIXEL_UNPACK_BUFFER_BINDING, &old_pbo); bind_buffer(GL_PIXEL_UNPACK_BUFFER, 0); }
        glGetIntegerv(GL_UNPACK_ALIGNMENT, &alignment); glGetIntegerv(GL_UNPACK_ROW_LENGTH, &row_length);
        glGetIntegerv(GL_UNPACK_SKIP_ROWS, &skip_rows); glGetIntegerv(GL_UNPACK_SKIP_PIXELS, &skip_pixels);
        glPixelStorei(GL_UNPACK_ALIGNMENT, 1); glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
        glPixelStorei(GL_UNPACK_SKIP_ROWS, 0); glPixelStorei(GL_UNPACK_SKIP_PIXELS, 0);
        auto handle = renderer.GenerateTexture({pixels, size_t(width) * size_t(height) * 4}, {width, height});
        if (handle) {
            GLint actual_width = 0, actual_height = 0;
            glGetTexLevelParameteriv(GL_TEXTURE_2D, 0, GL_TEXTURE_WIDTH, &actual_width);
            glGetTexLevelParameteriv(GL_TEXTURE_2D, 0, GL_TEXTURE_HEIGHT, &actual_height);
            if (actual_width != width || actual_height != height) { renderer.ReleaseTexture(handle); handle = 0; }
        }
        if (handle) {
            glBindTexture(GL_TEXTURE_2D, GLuint(handle));
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, sampling.filter ? GL_LINEAR : GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, sampling.filter ? GL_LINEAR : GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, sampling.wrap_s ? GL_REPEAT : GL_CLAMP_TO_EDGE);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, sampling.wrap_t ? GL_REPEAT : GL_CLAMP_TO_EDGE);
        }
        glPixelStorei(GL_UNPACK_ALIGNMENT, alignment); glPixelStorei(GL_UNPACK_ROW_LENGTH, row_length);
        glPixelStorei(GL_UNPACK_SKIP_ROWS, skip_rows); glPixelStorei(GL_UNPACK_SKIP_PIXELS, skip_pixels);
        if (pbo) bind_buffer(GL_PIXEL_UNPACK_BUFFER, GLuint(old_pbo));
        glBindTexture(GL_TEXTURE_2D, GLuint(old_texture)); active(GLenum(old_unit));
        return handle;
    }

    void BeginFrame() override
    {
        // The game and the RmlUi painter share one *compatibility* GL context.
        // RmlUi's GL2 backend uses fixed-function matrices/client pointers,
        // blending and stencil. Restore those exact states instead of assuming
        // a particular "default" state at the next world/HUD draw.
        glPushAttrib(GL_ALL_ATTRIB_BITS);
        glPushClientAttrib(GL_CLIENT_ALL_ATTRIB_BITS);
        SaveSampler();
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
        if (restore_sampler) { restore_sampler(0, GLuint(saved_sampler)); restore_sampler = nullptr; }
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
#if defined(_WIN32)
    using BindSampler = void (APIENTRY*)(GLuint, GLuint);
#else
    using BindSampler = void (*)(GLuint, GLuint);
#endif
    BindSampler restore_sampler = nullptr;
    GLint saved_sampler = 0;
    void SaveSampler()
    {
        // Sampler objects override texture-object policy. Neutralize unit0 for the entire pass, then restore it.
        restore_sampler = nullptr;
        const char* version = reinterpret_cast<const char*>(glGetString(GL_VERSION));
        if (!version) return;
        int major = std::atoi(version), minor = 0;
        if (const char* dot = std::strchr(version, '.')) minor = std::atoi(dot + 1);
        bool supported = major > 3 || (major == 3 && minor >= 3);
        if (!supported) {
            const char* extensions = reinterpret_cast<const char*>(glGetString(GL_EXTENSIONS));
            supported = extensions && std::strstr(extensions, "GL_ARB_sampler_objects");
        }
        if (!supported) return;
#if defined(_WIN32)
        auto resolve = [](const char* name) {
            auto value = wglGetProcAddress(name); const auto n = reinterpret_cast<uintptr_t>(value);
            return n > 3 && n != UINTPTR_MAX ? value : nullptr;
        };
        using Active = void (APIENTRY*)(GLenum);
        const auto active = reinterpret_cast<Active>(resolve("glActiveTexture"));
        auto sampler = reinterpret_cast<BindSampler>(resolve("glBindSampler"));
        if (!active || !sampler) return;
#else
        const auto active = glActiveTexture;
        auto sampler = reinterpret_cast<BindSampler>(dlsym(RTLD_DEFAULT, "glBindSampler"));
        if (!sampler) return;
#endif
        GLint unit = 0; glGetIntegerv(GL_ACTIVE_TEXTURE, &unit); active(GL_TEXTURE0);
        glGetIntegerv(0x8919 /*GL_SAMPLER_BINDING*/, &saved_sampler);
        sampler(0, 0); active(GLenum(unit)); restore_sampler = sampler;
    }
    GLint matrix_mode = GL_MODELVIEW;
    RenderInterface_GL2 renderer;
};
}
std::unique_ptr<PrimeRenderer> CreatePrimeGl2Renderer()
{
    return std::make_unique<PrimeGl2Renderer>();
}
