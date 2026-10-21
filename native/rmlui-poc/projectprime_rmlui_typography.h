// Included within the bridge's owner-thread namespace after document state.
class PrimeTypography final : public Rml::Plugin {
    struct Saved {
        std::array<std::optional<Rml::Property>, 3> local;
        std::array<Rml::Property, 3> authored;
        int role = 0;
    };
    const std::array<Rml::PropertyId, 3> properties = {
        Rml::PropertyId::FontFamily, Rml::PropertyId::FontStyle, Rml::PropertyId::FontWeight};
    std::unordered_map<Rml::Element*, Saved> saved;
    std::array<int, 3> fonts = {-1, -1, -1};
    std::array<bool, 34> loaded{};
    bool dirty = false;

    bool Enabled() const { return fonts[0] >= 0 || fonts[1] >= 0 || fonts[2] >= 0; }
    static std::string Alias(int font) { return "PrimeHudFont" + std::to_string(font); }
    static int Role(Rml::Element* element) {
        std::string family = element->GetProperty<Rml::String>("font-family");
        std::transform(family.begin(), family.end(), family.begin(), [](unsigned char c) { return char(std::tolower(c)); });
        const auto& tag = element->GetTagName();
        if (family.find("mono") != std::string::npos || tag == "code" || tag == "pre") return 2;
        if ((tag.size() == 2 && tag[0] == 'h' && tag[1] >= '1' && tag[1] <= '6')
            || element->IsClassSet("eyebrow") || element->GetProperty<int>("font-weight") >= 700) return 1;
        return 0;
    }
    void Capture(Rml::Element* element) {
        if (element->GetTagName() != "#text") {
            Saved state;
            state.role = Role(element);
            for (size_t i = 0; i < properties.size(); ++i) {
                if (const auto* property = element->GetLocalProperty(properties[i])) state.local[i] = *property;
                if (const auto* property = element->GetProperty(properties[i])) state.authored[i] = *property;
            }
            saved.emplace(element, std::move(state));
        }
        for (int i = 0; i < element->GetNumChildren(); ++i) Capture(element->GetChild(i));
    }
public:
    int GetEventClasses() override { return EVT_ELEMENT | EVT_BASIC; }
    void OnElementCreate(Rml::Element* element) override {
        if (Enabled() && element->GetTagName() != "#text") dirty = true;
    }
    void OnElementDestroy(Rml::Element* element) override { saved.erase(element); }
    void OnShutdown() override { saved.clear(); fonts = {-1, -1, -1}; loaded = {}; dirty = false; }
    bool Set(int body, int headings, int data) {
        const std::array<int, 3> next = {body, headings, data};
        for (int font : next) if (font < -1 || font >= int(loaded.size())) return false;
        if (next == fonts) return true;
        for (int font : next) if (font >= 0 && !loaded[font]) {
            const auto filename = std::string(font < 10 ? "font0" : "font") + std::to_string(font) + ".ttf";
            // Each licensed face is registered under an exact private alias.
            // This preserves bold/italic glyphs without family-name collisions.
            if (!Rml::LoadFontFace((g_asset_root / "hud-fonts" / filename).string(), Alias(font),
                Rml::Style::FontStyle::Normal, Rml::Style::FontWeight::Normal)) return false;
            loaded[font] = true;
        }
        fonts = next; dirty = true; DirtyVisual(); return true;
    }
    void Apply() {
        if (!dirty) return;
        dirty = false;
        // Restore before inspecting the authored roles, including inherited
        // styles. Original properties live only as long as their DOM element.
        for (auto& pair : saved)
            for (size_t i = 0; i < properties.size(); ++i) {
                if (pair.second.local[i]) pair.first->SetProperty(properties[i], *pair.second.local[i]);
                else pair.first->RemoveProperty(properties[i]);
            }
        saved.clear();
        if (!Enabled()) { DirtyVisual(); return; }
        for (auto& document : g_documents) Capture(document.second.element);
        for (auto& pair : saved) {
            const int font = fonts[pair.second.role];
            if (font < 0) {
                // An overridden body must not leak into an independently
                // disabled heading/data role through CSS inheritance.
                for (size_t i = 0; i < properties.size(); ++i)
                    pair.first->SetProperty(properties[i], pair.second.authored[i]);
                continue;
            }
            pair.first->SetProperty("font-family", Alias(font));
            pair.first->SetProperty("font-style", "normal");
            pair.first->SetProperty("font-weight", "normal");
        }
        DirtyVisual();
    }
};
PrimeTypography g_typography;
