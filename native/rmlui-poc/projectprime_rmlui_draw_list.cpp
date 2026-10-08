#include "projectprime_rmlui_renderer.h"
#include <RmlUi/Core.h>
#include <algorithm>
#include <cstring>
#include <limits>
#include <map>
#include <type_traits>
#include <vector>

namespace {
uint64_t g_resource_id = 0;
constexpr size_t MaxTextureBytes = 64 * 1024 * 1024;
struct Geometry { std::vector<Rml::Vertex> vertices; std::vector<int32_t> indices; };
struct Texture { Rml::Vector2i dimensions; std::vector<uint8_t> pixels; };
static_assert(sizeof(Rml::Vertex) == 20, "Draw-list vertex ABI changed");
static_assert(sizeof(int) == sizeof(int32_t), "Draw-list index ABI changed");

class PrimeDrawListRenderer final : public PrimeRenderer, public Rml::RenderInterface {
public:
    Rml::RenderInterface* Interface() override { return this; }
    void SetViewport(int, int) override {}
    void BeginFrame() override
    {
        commands.clear();
        retired_geometries.clear(); retired_textures.clear();
        EnableScissorRegion(false);
        EnableClipMask(false);
        SetTransform(nullptr);
    }
    void EndFrame() override {}
    Rml::CompiledGeometryHandle CompileGeometry(Rml::Span<const Rml::Vertex> vertices, Rml::Span<const int> indices) override
    {
        if (vertices.empty() || indices.empty()) return 0;
        const auto id = NextId();
        if (!id) return 0;
        Geometry geometry;
        geometry.vertices.assign(vertices.begin(), vertices.end());
        geometry.indices.assign(indices.begin(), indices.end());
        geometries.emplace(id, std::move(geometry));
        return Rml::CompiledGeometryHandle(id);
    }
    void RenderGeometry(Rml::CompiledGeometryHandle geometry, Rml::Vector2f translation, Rml::TextureHandle texture) override
    {
        if (!geometries.count(geometry)) return;
        auto& command = Add(PrimeDrawCommandKind::Geometry);
        command.geometry = geometry;
        command.texture = texture;
        command.translation_x = translation.x;
        command.translation_y = translation.y;
    }
    void ReleaseGeometry(Rml::CompiledGeometryHandle geometry) override
    {
        auto found = geometries.find(geometry);
        if (found == geometries.end()) return;
        // RenderInterface permits RmlUi to release temporary geometry during
        // Render(). Retain resources needed by this deferred frame until the
        // next BeginFrame, after the engine has copied/submitted the draw list.
        if (std::any_of(commands.begin(), commands.end(), [geometry](const PrimeDrawCommand& command) { return command.geometry == geometry; }))
            retired_geometries.emplace(geometry, std::move(found->second));
        geometries.erase(found);
    }
    Rml::TextureHandle LoadTexture(Rml::Vector2i& dimensions, const Rml::String& source) override
    {
        // Matches the development GL adapter's supported format, with checked
        // sizes. General PNG/JPEG decoding belongs to the engine resource adapter.
        auto* files = Rml::GetFileInterface();
        const auto file = files->Open(source);
        if (!file) return 0;
        const size_t length = files->Length(file);
        if (length < 18 || length > MaxTextureBytes) { files->Close(file); return 0; }
        std::vector<uint8_t> bytes(length);
        const size_t read = files->Read(bytes.data(), length, file);
        files->Close(file);
        if (read != length || bytes[1] != 0 || bytes[2] != 2 || (bytes[16] != 24 && bytes[16] != 32)) return 0;
        const int width = bytes[12] | (bytes[13] << 8);
        const int height = bytes[14] | (bytes[15] << 8);
        const size_t channels = bytes[16] / 8;
        const size_t pixels = size_t(width) * size_t(height);
        const size_t offset = 18 + bytes[0];
        if (width <= 0 || height <= 0 || pixels > MaxTextureBytes / 4 || offset > length || pixels > (length - offset) / channels) return 0;
        std::vector<Rml::byte> rgba(pixels * 4);
        for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
            const size_t input = offset + (size_t(y) * width + x) * channels;
            const int out_y = bytes[17] & 32 ? y : height - 1 - y;
            const int out_x = bytes[17] & 16 ? width - 1 - x : x;
            const size_t output = (size_t(out_y) * width + out_x) * 4;
            const uint8_t alpha = channels == 4 ? bytes[input + 3] : 255;
            rgba[output] = uint8_t(unsigned(bytes[input + 2]) * alpha / 255);
            rgba[output + 1] = uint8_t(unsigned(bytes[input + 1]) * alpha / 255);
            rgba[output + 2] = uint8_t(unsigned(bytes[input]) * alpha / 255);
            rgba[output + 3] = alpha;
        }
        dimensions = {width, height};
        return GenerateTexture({rgba.data(), rgba.size()}, dimensions);
    }
    Rml::TextureHandle GenerateTexture(Rml::Span<const Rml::byte> source, Rml::Vector2i dimensions) override
    {
        if (dimensions.x <= 0 || dimensions.y <= 0) return 0;
        const size_t pixels = size_t(dimensions.x) * size_t(dimensions.y);
        if (pixels > MaxTextureBytes / 4 || source.size() != pixels * 4) return 0;
        const auto id = NextId();
        if (!id) return 0;
        Texture texture;
        texture.dimensions = dimensions;
        texture.pixels.assign(source.begin(), source.end());
        textures.emplace(id, std::move(texture));
        return Rml::TextureHandle(id);
    }
    void ReleaseTexture(Rml::TextureHandle texture) override
    {
        auto found = textures.find(texture);
        if (found == textures.end()) return;
        if (std::any_of(commands.begin(), commands.end(), [texture](const PrimeDrawCommand& command) { return command.texture == texture; }))
            retired_textures.emplace(texture, std::move(found->second));
        textures.erase(found);
    }
    void EnableScissorRegion(bool enabled) override { Add(PrimeDrawCommandKind::ScissorEnable).enabled = enabled ? 1 : 0; }
    void SetScissorRegion(Rml::Rectanglei region) override
    {
        auto& command = Add(PrimeDrawCommandKind::ScissorRegion);
        command.x = region.Left(); command.y = region.Top();
        command.width = region.Width(); command.height = region.Height();
    }
    void EnableClipMask(bool enabled) override { Add(PrimeDrawCommandKind::ClipEnable).enabled = enabled ? 1 : 0; }
    void RenderToClipMask(Rml::ClipMaskOperation operation, Rml::CompiledGeometryHandle geometry, Rml::Vector2f translation) override
    {
        if (!geometries.count(geometry)) return;
        auto& command = Add(PrimeDrawCommandKind::ClipGeometry);
        command.operation = uint32_t(operation);
        command.geometry = geometry;
        command.translation_x = translation.x;
        command.translation_y = translation.y;
    }
    void SetTransform(const Rml::Matrix4f* transform) override
    {
        auto& command = Add(PrimeDrawCommandKind::Transform);
        command.enabled = transform ? 1 : 0;
        if (transform) {
            if constexpr (std::is_same_v<Rml::Matrix4f, Rml::ColumnMajorMatrix4f>)
                std::memcpy(command.transform, transform->data(), sizeof(command.transform));
            else
                std::memcpy(command.transform, transform->Transpose().data(), sizeof(command.transform));
        } else for (int index = 0; index < 16; ++index) command.transform[index] = index % 5 == 0 ? 1.f : 0.f;
    }
    Rml::LayerHandle PushLayer() override { unsupported |= 1; return 0; }
    void CompositeLayers(Rml::LayerHandle, Rml::LayerHandle, Rml::BlendMode, Rml::Span<const Rml::CompiledFilterHandle>) override { unsupported |= 1; }
    void PopLayer() override { unsupported |= 1; }
    Rml::TextureHandle SaveLayerAsTexture() override { unsupported |= 1; return 0; }
    Rml::CompiledFilterHandle SaveLayerAsMaskImage() override { unsupported |= 1; return 0; }
    Rml::CompiledFilterHandle CompileFilter(const Rml::String&, const Rml::Dictionary&) override { unsupported |= 1; return 0; }
    Rml::CompiledShaderHandle CompileShader(const Rml::String&, const Rml::Dictionary&) override { unsupported |= 1; return 0; }
    void RenderShader(Rml::CompiledShaderHandle, Rml::CompiledGeometryHandle, Rml::Vector2f, Rml::TextureHandle) override { unsupported |= 1; }

    int GeometryCount() const override { return int(geometries.size() + retired_geometries.size()); }
    int CommandCount() const override { return int(commands.size()); }
    bool ReadCommand(int index, PrimeDrawCommand& command) const override
    {
        if (index < 0 || size_t(index) >= commands.size()) return false;
        command = commands[size_t(index)]; return true;
    }
    bool ReadGeometry(uint64_t handle, PrimeDrawGeometry& geometry) const override
    {
        const auto live = geometries.find(handle);
        const auto retired = retired_geometries.find(handle);
        const Geometry* resource = live != geometries.end() ? &live->second
            : retired != retired_geometries.end() ? &retired->second : nullptr;
        if (!resource) return false;
        geometry = {};
        geometry.vertex_count = uint32_t(resource->vertices.size());
        geometry.index_count = uint32_t(resource->indices.size());
        geometry.vertices = resource->vertices.data();
        geometry.indices = resource->indices.data();
        return true;
    }
    int TextureCount() const override { return int(textures.size() + retired_textures.size()); }
    bool ReadTexture(int index, PrimeDrawTexture& texture) const override
    {
        if (index < 0 || index >= TextureCount()) return false;
        const auto& resources = size_t(index) < textures.size() ? textures : retired_textures;
        if (size_t(index) >= textures.size()) index -= int(textures.size());
        auto found = resources.begin(); std::advance(found, index);
        texture = {};
        texture.handle = found->first;
        texture.width = uint32_t(found->second.dimensions.x);
        texture.height = uint32_t(found->second.dimensions.y);
        texture.pixels = found->second.pixels.data();
        return true;
    }
    uint32_t UnsupportedFeatures() const override { return unsupported; }
private:
    std::map<uint64_t, Geometry> geometries;
    std::map<uint64_t, Texture> textures;
    std::map<uint64_t, Geometry> retired_geometries;
    std::map<uint64_t, Texture> retired_textures;
    std::vector<PrimeDrawCommand> commands;
    uint32_t unsupported = 0;
    static uint64_t NextId()
    {
        if (g_resource_id == uint64_t(std::numeric_limits<uintptr_t>::max())) return 0;
        return ++g_resource_id;
    }
    PrimeDrawCommand& Add(PrimeDrawCommandKind kind)
    {
        commands.emplace_back(); commands.back().kind = uint32_t(kind); return commands.back();
    }
};
}
std::unique_ptr<PrimeRenderer> CreatePrimeDrawListRenderer() { return std::make_unique<PrimeDrawListRenderer>(); }
