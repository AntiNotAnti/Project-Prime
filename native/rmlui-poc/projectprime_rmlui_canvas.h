#pragma once
// Included inside the bridge's private namespace, after its document/thread owners.
class PrimeCanvasElement;
struct CanvasRegistration { PrimeCanvasElement* element; uint64_t document; std::string id; };
std::unordered_map<uint64_t, CanvasRegistration> g_canvases;
uint64_t g_next_canvas_epoch = 0; // Never reset/reused across native generations.
bool CanvasCanRender(const PrimeCanvasElement* element);

class PrimeCanvasElement final : public Rml::Element {
public:
    explicit PrimeCanvasElement(const Rml::String& tag) : Rml::Element(tag) {}
    ~PrimeCanvasElement() override { Retire(); }
    uint64_t epoch = 0, revision = 0;
    bool table_set = false, failed = false;
    uint32_t BatchCount() const { return uint32_t(batches.size()); }
    uint32_t TextureCount() const { return uint32_t(textures.size()); }
    void Retire()
    {
        if (epoch) { g_canvases.erase(epoch); epoch = 0; DirtyVisual(); }
        // Release capacity as well as resources. Retired DOM elements may remain
        // alive, and must not retain multi-megabyte packets outside the live-lease budget.
        decltype(geometry){}.swap(geometry);
        decltype(textures){}.swap(textures);
        decltype(vertices){}.swap(vertices);
        decltype(indices){}.swap(indices);
        decltype(batches){}.swap(batches);
        revision = 0; table_set = failed = false; geometry_dirty = true;
    }
    bool SetTextures(const PrimeCanvasTexture* source, uint32_t count, const uint8_t* pixels, uint32_t length)
    {
        if (table_set || count > 64 || length > 4 * 1024 * 1024 || (count && !source) || (length && !pixels)) return false;
        uint64_t end = 0;
        for (uint32_t i = 0; i < count; ++i) {
            const auto& t = source[i];
            if (t.size != sizeof(t) || t.version != 1 || t.reserved || !t.width || !t.height
                || t.width > 512 || t.height > 512 || t.filter > 1 || t.wrap_s > 1 || t.wrap_t > 1
                || t.offset != end || uint64_t(t.width) * t.height * 4 != t.length) return false;
            end += t.length;
            if (end > length) return false;
        }
        if (end != length || !GetRenderManager()) return false;
        std::vector<Rml::CallbackTexture> staged;
        staged.reserve(count);
        for (uint32_t i = 0; i < count; ++i) {
            const auto& t = source[i];
            std::vector<uint8_t> rgba(pixels + t.offset, pixels + t.offset + t.length);
            for (size_t p = 0; p < rgba.size(); p += 4)
                for (size_t c = 0; c < 3; ++c) rgba[p + c] = uint8_t(unsigned(rgba[p + c]) * rgba[p + 3] / 255);
            PrimeTextureSampling sampling;
            sampling.filter = t.filter; sampling.wrap_s = t.wrap_s; sampling.wrap_t = t.wrap_t; sampling.flags = 1;
            Rml::Vector2i dimensions(int(t.width), int(t.height));
            staged.emplace_back(GetRenderManager()->MakeCallbackTexture(
                [this, rgba = std::move(rgba), dimensions, sampling](const Rml::CallbackTextureInterface& target) {
                    try {
                        auto handle = g_renderer->CreateCanvasTexture(rgba.data(), dimensions.x, dimensions.y, sampling);
                        if (!handle) { failed = true; return false; }
                        target.SetTextureHandle(handle, dimensions); return true;
                    } catch (...) { failed = true; return false; }
                }));
        }
        textures.swap(staged); table_set = true; DirtyVisual(); return true;
    }
    bool SetFrame(const PrimeCanvasFrame& frame, const PrimeCanvasVertex* source,
        const int32_t* source_indices, const PrimeCanvasBatch* source_batches)
    {
        if (!table_set || frame.size != sizeof(frame) || frame.version != 1 || frame.reserved
            || frame.revision <= revision || !std::isfinite(frame.virtual_width) || !std::isfinite(frame.virtual_height)
            || frame.virtual_width < 1 || frame.virtual_height < 1 || frame.virtual_width > 16384 || frame.virtual_height > 16384
            || frame.vertex_count > 65536 || frame.index_count > 196608 || frame.batch_count > 4096
            || frame.index_count % 3 || (frame.vertex_count && !source) || (frame.index_count && !source_indices)
            || (frame.batch_count && !source_batches)) return false;
        if ((!frame.vertex_count || !frame.index_count || !frame.batch_count)
            && (frame.vertex_count || frame.index_count || frame.batch_count)) return false;
        for (uint32_t i = 0; i < frame.vertex_count; ++i) {
            const auto& v = source[i];
            for (float value : {v.x, v.y, v.u, v.v})
                if (!std::isfinite(value) || std::abs(value) > 1048576) return false;
        }
        for (uint32_t i = 0; i < frame.index_count; ++i)
            if (source_indices[i] < 0 || uint32_t(source_indices[i]) >= frame.vertex_count) return false;
        uint64_t end = 0;
        for (uint32_t i = 0; i < frame.batch_count; ++i) {
            const auto& b = source_batches[i];
            if (b.reserved || b.first != end || !b.count || b.count % 3
                || (b.texture != UINT32_MAX && b.texture >= textures.size())) return false;
            end += b.count;
            if (end > frame.index_count) return false;
        }
        if (end != frame.index_count) return false;
        std::vector<Rml::Vertex> staged_vertices; staged_vertices.reserve(frame.vertex_count);
        for (uint32_t i = 0; i < frame.vertex_count; ++i) {
            const auto& v = source[i];
            Rml::Vertex vertex;
            vertex.position = {v.x, v.y}; vertex.tex_coord = {v.u, v.v};
            vertex.colour = Rml::ColourbPremultiplied(uint8_t(unsigned(v.r) * v.a / 255),
                uint8_t(unsigned(v.g) * v.a / 255), uint8_t(unsigned(v.b) * v.a / 255), v.a);
            staged_vertices.push_back(vertex);
        }
        std::vector<int32_t> staged_indices;
        std::vector<PrimeCanvasBatch> staged_batches;
        if (frame.index_count) staged_indices.assign(source_indices, source_indices + frame.index_count);
        if (frame.batch_count) staged_batches.assign(source_batches, source_batches + frame.batch_count);
        // Publish only after the entire packet validates and all copies succeed.
        vertices.swap(staged_vertices); indices.swap(staged_indices); batches.swap(staged_batches);
        virtual_width = frame.virtual_width; virtual_height = frame.virtual_height; revision = frame.revision;
        geometry.clear(); geometry_dirty = true; DirtyVisual(); return true;
    }
protected:
    void OnResize() override { geometry_dirty = true; }
    void OnChildRemove(Rml::Element* child) override
    {
        if (child == this) Retire();
        Rml::Element::OnChildRemove(child);
    }
    void OnRender() override
    {
        if (!epoch || !revision || failed || !CanvasCanRender(this) || batches.empty()) return;
        // Resolve all callback images before emitting any canvas geometry.
        try {
            for (auto& texture : textures) {
                Rml::Texture view = texture;
                if (view.GetDimensions().x <= 0 || failed) { failed = true; return; }
            }
        } catch (...) { failed = true; return; }
        auto dimensions = GetBox().GetSize(Rml::BoxArea::Content);
        auto offset = GetAbsoluteOffset(Rml::BoxArea::Content);
        if (!std::isfinite(dimensions.x) || !std::isfinite(dimensions.y) || dimensions.x <= 0 || dimensions.y <= 0) return;
        if (dimensions != last_dimensions || offset != last_offset) geometry_dirty = true;
        if (geometry_dirty) {
            try {
                const float scale = std::min(dimensions.x / virtual_width, dimensions.y / virtual_height);
                const Rml::Vector2f origin = offset + (dimensions - Rml::Vector2f(virtual_width, virtual_height) * scale) * .5f;
                std::vector<Rml::Geometry> staged;
                staged.reserve(batches.size());
                for (const auto& batch : batches) {
                    Rml::Mesh mesh;
                    mesh.vertices.reserve(batch.count); mesh.indices.reserve(batch.count);
                    // Copy only referenced vertices; copying the entire frame per batch would defeat aggregate bounds.
                    for (uint32_t i = 0; i < batch.count; ++i) {
                        Rml::Vertex vertex = vertices[indices[batch.first + i]];
                        vertex.position = origin + vertex.position * scale;
                        if (!std::isfinite(vertex.position.x) || !std::isfinite(vertex.position.y)) { failed = true; return; }
                        mesh.vertices.push_back(vertex); mesh.indices.push_back(int(i));
                    }
                    staged.emplace_back(GetRenderManager()->MakeGeometry(std::move(mesh)));
                }
                geometry.swap(staged); last_dimensions = dimensions; last_offset = offset; geometry_dirty = false;
            } catch (...) { failed = true; return; }
        }
        // Geometry.Render rounds its translation. Embed fractional absolute content origin above and render at zero.
        try {
            auto& manager = *GetRenderManager();
            const auto previous = manager.GetState();
            struct RestoreState {
                Rml::RenderManager& manager;
                const Rml::RenderState& previous;
                ~RestoreState() { manager.SetState(previous); }
            } restore{manager, previous};
            // RmlUi may omit overflow clipping when layout boxes fit the parent,
            // because it cannot infer custom mesh extents. Force the canvas's
            // own viewport clip, including transform/rounded-mask handling.
            if (!Rml::ElementUtilities::SetClippingRegion(this, true)) return;
            for (size_t i = 0; i < geometry.size(); ++i) {
                const auto texture = batches[i].texture;
                geometry[i].Render({0, 0}, texture == UINT32_MAX ? Rml::Texture{} : Rml::Texture(textures[texture]));
            }
        } catch (...) { failed = true; }
    }
private:
    bool geometry_dirty = true;
    float virtual_width = 1, virtual_height = 1;
    Rml::Vector2f last_dimensions{-1, -1}, last_offset;
    std::vector<Rml::Geometry> geometry; // Release before callback textures.
    std::vector<Rml::CallbackTexture> textures;
    std::vector<Rml::Vertex> vertices;
    std::vector<int32_t> indices;
    std::vector<PrimeCanvasBatch> batches;
};
Rml::ElementInstancerGeneric<PrimeCanvasElement> g_canvas_instancer;

PrimeCanvasElement* CanvasGet(const PrimeCanvasLease* lease, bool foreground)
{
    if (!OwnerThread() || !lease || lease->size != sizeof(*lease) || lease->version != 1
        || lease->generation != g_generation || !lease->epoch) return nullptr;
    if (foreground) EnsureUpdated();
    auto found = g_canvases.find(lease->epoch);
    if (found == g_canvases.end() || found->second.document != lease->document_id) return nullptr;
    auto* document = GetDocument(lease->document_id);
    auto* element = found->second.element;
    if (!document || element->GetOwnerDocument() != document->element
        || document->element->GetElementById(found->second.id) != element) return nullptr;
    if (foreground && (!element->IsVisible(true) || !document->element->IsVisible() || !ForegroundDocument(document->element))) return nullptr;
    return element;
}
bool CanvasCanRender(const PrimeCanvasElement* element)
{
    auto found = g_canvases.find(element->epoch);
    if (found == g_canvases.end()) return false;
    auto* document = GetDocument(found->second.document);
    return document && found->second.element == element && element->GetOwnerDocument() == document->element
        && document->element->GetElementById(found->second.id) == element && element->IsVisible(true)
        && document->element->IsVisible() && ForegroundDocument(document->element);
}
void CanvasRetireDocument(uint64_t document)
{
    for (auto it = g_canvases.begin(); it != g_canvases.end();) {
        if (it->second.document != document) { ++it; continue; }
        auto* element = it->second.element; ++it; element->Retire();
    }
}
void CanvasRetireSubtree(Rml::Element* root)
{
    for (auto it = g_canvases.begin(); it != g_canvases.end();) {
        auto* element = it->second.element; ++it;
        for (auto* at = static_cast<Rml::Element*>(element); at; at = at->GetParentNode())
            if (at == root) { element->Retire(); break; }
    }
}
