#pragma once
#include "projectprime_rmlui_api.h"
#include <RmlUi/Core/RenderInterface.h>
#include <memory>

// The native core owns layout/input. Each window/device owner chooses an adapter;
// this interface contains no graphics API and permits an Android/modern consumer.
class PrimeRenderer {
public:
    virtual ~PrimeRenderer() = default;
    virtual Rml::RenderInterface* Interface() = 0;
    virtual void SetViewport(int width, int height) = 0;
    virtual void BeginFrame() = 0;
    virtual void EndFrame() = 0;
    virtual int GeometryCount() const { return 0; }
    virtual int CommandCount() const { return 0; }
    virtual bool ReadCommand(int, PrimeDrawCommand&) const { return false; }
    virtual bool ReadGeometry(uint64_t, PrimeDrawGeometry&) const { return false; }
    virtual int TextureCount() const { return 0; }
    virtual bool ReadTexture(int, PrimeDrawTexture&) const { return false; }
    virtual uint32_t UnsupportedFeatures() const { return 0; }
};
std::unique_ptr<PrimeRenderer> CreatePrimeDrawListRenderer();
#if defined(PP_RMLUI_GL2)
std::unique_ptr<PrimeRenderer> CreatePrimeGl2Renderer();
#endif
