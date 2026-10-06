#include <RmlUi/Core.h>
#include <RmlUi/Core/Input.h>
#include <RmlUi/Core/StringUtilities.h>
#include "RmlUi_Renderer_GL2.h"

#include <algorithm>
#include <chrono>
#include <cstring>
#include <deque>
#include <filesystem>
#include <memory>
#include <string>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#include <GL/gl.h>
#elif defined(__APPLE__)
#include <OpenGL/gl.h>
#else
#include <GL/gl.h>
#endif

#if defined(_WIN32)
#define PP_EXPORT extern "C" __declspec(dllexport)
#else
#define PP_EXPORT extern "C" __attribute__((visibility("default")))
#endif

PP_EXPORT void pp_rmlui_shutdown();

namespace {

class PrimeSystemInterface final : public Rml::SystemInterface {
public:
    PrimeSystemInterface() : started(std::chrono::steady_clock::now()) {}

    double GetElapsedTime() override
    {
        return std::chrono::duration<double>(std::chrono::steady_clock::now() - started).count();
    }

private:
    std::chrono::steady_clock::time_point started;
};

struct PrimeMenuData {
    Rml::String player_name = "PLAYER";
    Rml::String profile_state = "LOCAL PROFILE";
    Rml::String hunter_name = "SAMUS";
    Rml::String game_data_state = "GAME DATA UNKNOWN";
    Rml::String build_version = "local";
    Rml::String renderer_name = "OPENGL // RMLUI 6.3";
    Rml::String ui_cost = "RMLUI DIRECT GPU OVERLAY // MEASURING";
    bool reduce_motion = false;
    bool diagnostics_visible = false;

    int activity_index = 0;
    Rml::String activity_title = "QUICK PLAY";
    Rml::String activity_description = "Find the best compatible public hunt and deploy immediately.";
    Rml::String activity_hint = "MATCHMAKING ENTRY";
    Rml::String activity_action = "DEPLOY";
};

class PrimeMenuModel {
public:
    bool Setup(Rml::Context* context)
    {
        Rml::DataModelConstructor model = context->CreateDataModel("prime_menu");
        if (!model)
            return false;

        model.Bind("player_name", &data.player_name);
        model.Bind("profile_state", &data.profile_state);
        model.Bind("hunter_name", &data.hunter_name);
        model.Bind("game_data_state", &data.game_data_state);
        model.Bind("build_version", &data.build_version);
        model.Bind("renderer_name", &data.renderer_name);
        model.Bind("ui_cost", &data.ui_cost);
        model.Bind("reduce_motion", &data.reduce_motion);
        model.Bind("diagnostics_visible", &data.diagnostics_visible);
        model.Bind("activity_index", &data.activity_index);
        model.Bind("activity_title", &data.activity_title);
        model.Bind("activity_description", &data.activity_description);
        model.Bind("activity_hint", &data.activity_hint);
        model.Bind("activity_action", &data.activity_action);

        model.BindEventCallback("select_quick", &PrimeMenuModel::SelectQuick, this);
        model.BindEventCallback("select_browser", &PrimeMenuModel::SelectBrowser, this);
        model.BindEventCallback("select_offline", &PrimeMenuModel::SelectOffline, this);
        model.BindEventCallback("select_adventure", &PrimeMenuModel::SelectAdventure, this);
        model.BindEventCallback("deploy", &PrimeMenuModel::Deploy, this);
        model.BindEventCallback("nav_hunters", &PrimeMenuModel::OpenHunters, this);
        model.BindEventCallback("nav_community", &PrimeMenuModel::OpenCommunity, this);
        model.BindEventCallback("nav_studio", &PrimeMenuModel::OpenStudio, this);
        model.BindEventCallback("open_profile", &PrimeMenuModel::OpenProfile, this);
        model.BindEventCallback("open_settings", &PrimeMenuModel::OpenSettings, this);
        model.BindEventCallback("open_classic", &PrimeMenuModel::OpenClassic, this);
        model.BindEventCallback("quit_game", &PrimeMenuModel::Quit, this);
        model.BindEventCallback("noop", &PrimeMenuModel::Noop, this);

        handle = model.GetModelHandle();
        return true;
    }

    void SetText(const std::string& name, const std::string& value)
    {
        if (name == "player_name") data.player_name = value;
        else if (name == "profile_state") data.profile_state = value;
        else if (name == "hunter_name") data.hunter_name = value;
        else if (name == "game_data_state") data.game_data_state = value;
        else if (name == "build_version") data.build_version = value;
        else if (name == "renderer_name") data.renderer_name = value;
        else if (name == "ui_cost") data.ui_cost = value;
        else return;
        handle.DirtyVariable(name);
    }

    void SetBool(const std::string& name, bool value)
    {
        if (name == "reduce_motion") data.reduce_motion = value;
        else if (name == "diagnostics_visible") data.diagnostics_visible = value;
        else return;
        handle.DirtyVariable(name);
    }

    bool TakeAction(std::string& result)
    {
        if (actions.empty()) return false;
        result = std::move(actions.front());
        actions.pop_front();
        return true;
    }

private:
    PrimeMenuData data;
    Rml::DataModelHandle handle;
    std::deque<std::string> actions;

    void Emit(const char* action) { actions.emplace_back(action); }

    void SetActivity(int index, const char* title, const char* description,
        const char* hint, const char* action)
    {
        data.activity_index = index;
        data.activity_title = title;
        data.activity_description = description;
        data.activity_hint = hint;
        data.activity_action = action;
        handle.DirtyVariable("activity_index");
        handle.DirtyVariable("activity_title");
        handle.DirtyVariable("activity_description");
        handle.DirtyVariable("activity_hint");
        handle.DirtyVariable("activity_action");
    }

    void SelectQuick(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(0, "QUICK PLAY",
            "Find the best compatible public hunt and deploy immediately.",
            "MATCHMAKING ENTRY", "DEPLOY");
    }

    void SelectBrowser(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(1, "SERVER BROWSER",
            "Browse live lobbies, inspect arena and rules, then choose your hunt.",
            "LIVE DIRECTORY", "BROWSE SERVERS");
    }

    void SelectOffline(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(2, "OFFLINE BATTLE",
            "Configure bots, arena, rules and hunter loadout without going online.",
            "LOCAL SESSION", "CONFIGURE MATCH");
    }

    void SelectAdventure(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        SetActivity(3, "ADVENTURE",
            "Continue an existing save or begin a new solo campaign.",
            "SAVE DATA", "CONTINUE");
    }

    void Deploy(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&)
    {
        switch (data.activity_index) {
        case 0: Emit("route:play"); break;
        case 1: Emit("route:play"); break;
        case 2: Emit("route:offline"); break;
        case 3: Emit("route:offline"); break;
        default: break;
        }
    }

    void OpenHunters(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:hunter"); }
    void OpenCommunity(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:forge"); }
    void OpenStudio(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:theatre"); }
    void OpenProfile(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:hunter"); }
    void OpenSettings(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:settings"); }
    void OpenClassic(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("route:news"); }
    void Quit(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) { Emit("quit"); }
    void Noop(Rml::DataModelHandle, Rml::Event&, const Rml::VariantList&) {}
};

PrimeSystemInterface g_system;
std::unique_ptr<RenderInterface_GL2> g_renderer;
Rml::Context* g_context = nullptr;
Rml::ElementDocument* g_document = nullptr;
std::unique_ptr<PrimeMenuModel> g_model;
bool g_initialized = false;

int ConvertModifiers(int modifiers)
{
    int result = 0;
    if (modifiers & 1) result |= Rml::Input::KM_SHIFT;
    if (modifiers & 2) result |= Rml::Input::KM_CTRL;
    if (modifiers & 4) result |= Rml::Input::KM_ALT;
    if (modifiers & 8) result |= Rml::Input::KM_META;
    return result;
}

Rml::Input::KeyIdentifier ConvertKey(int key)
{
    switch (key) {
    case 1: return Rml::Input::KI_TAB;
    case 2: return Rml::Input::KI_RETURN;
    case 3: return Rml::Input::KI_ESCAPE;
    case 4: return Rml::Input::KI_SPACE;
    case 5: return Rml::Input::KI_UP;
    case 6: return Rml::Input::KI_DOWN;
    case 7: return Rml::Input::KI_LEFT;
    case 8: return Rml::Input::KI_RIGHT;
    case 9: return Rml::Input::KI_HOME;
    case 10: return Rml::Input::KI_END;
    case 11: return Rml::Input::KI_PRIOR;
    case 12: return Rml::Input::KI_NEXT;
    default: return Rml::Input::KI_UNKNOWN;
    }
}

std::filesystem::path RootPath(const char* root, const char* child)
{
    return std::filesystem::path(root ? root : "") / child;
}

void RestoreGlState()
{
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

} // namespace

PP_EXPORT int pp_rmlui_initialize(int width, int height, float density, const char* asset_root)
{
    if (g_initialized) return 1;
    if (!asset_root || width <= 0 || height <= 0) return 0;

    g_renderer = std::make_unique<RenderInterface_GL2>();
    g_renderer->SetViewport(width, height);
    Rml::SetSystemInterface(&g_system);
    Rml::SetRenderInterface(g_renderer.get());

    if (!Rml::Initialise()) return 0;
    g_initialized = true;

    const std::string regular = RootPath(asset_root, "fonts/Rajdhani-SemiBold.ttf").string();
    const std::string bold = RootPath(asset_root, "fonts/Rajdhani-Bold.ttf").string();
    const std::string mono = RootPath(asset_root, "fonts/JetBrainsMono-Regular.ttf").string();
    if (!Rml::LoadFontFace(regular) || !Rml::LoadFontFace(bold) || !Rml::LoadFontFace(mono)) {
        pp_rmlui_shutdown();
        return 0;
    }

    g_context = Rml::CreateContext("project-prime-rmlui-poc", {width, height});
    if (!g_context) {
        pp_rmlui_shutdown();
        return 0;
    }
    g_context->SetDensityIndependentPixelRatio(std::max(density, 1.0f));

    g_model = std::make_unique<PrimeMenuModel>();
    if (!g_model->Setup(g_context)) {
        pp_rmlui_shutdown();
        return 0;
    }

    const std::string document_path = RootPath(asset_root, "prime_home.rml").string();
    g_document = g_context->LoadDocument(document_path);
    if (!g_document) {
        pp_rmlui_shutdown();
        return 0;
    }
    g_document->Show();
    if (Rml::Element* first = g_document->GetElementById("mode_quick")) first->Focus();
    return 1;
}

PP_EXPORT void pp_rmlui_shutdown()
{
    if (!g_initialized && !g_renderer) return;
    if (g_document) {
        g_document->Close();
        g_document = nullptr;
    }
    g_model.reset();
    if (g_context) {
        Rml::RemoveContext("project-prime-rmlui-poc");
        g_context = nullptr;
    }
    if (g_initialized) Rml::Shutdown();
    g_initialized = false;
    Rml::SetRenderInterface(nullptr);
    Rml::SetSystemInterface(nullptr);
    g_renderer.reset();
}

PP_EXPORT void pp_rmlui_update()
{
    if (g_context) g_context->Update();
}

PP_EXPORT void pp_rmlui_render(int width, int height)
{
    if (!g_context || !g_renderer || width <= 0 || height <= 0) return;

    // Project Prime renders the cinematic ground and Hunter first. The sample
    // backend normally owns an empty framebuffer, while this bridge is an
    // overlay in somebody else's frame, so establish only the state RmlUi
    // needs and never clear color.
#if !defined(_WIN32)
    glActiveTexture(GL_TEXTURE0);
#endif
    glDisable(GL_DEPTH_TEST);
    glDepthMask(GL_FALSE);
    glDisable(GL_CULL_FACE);
    glDisable(GL_ALPHA_TEST);

    g_renderer->SetViewport(width, height);
    g_renderer->BeginFrame();
    glClearStencil(0);
    glClear(GL_STENCIL_BUFFER_BIT);
    g_context->Render();
    g_renderer->EndFrame();
    RestoreGlState();
}

PP_EXPORT void pp_rmlui_resize(int width, int height, float density)
{
    if (!g_context || !g_renderer || width <= 0 || height <= 0) return;
    g_renderer->SetViewport(width, height);
    g_context->SetDimensions({width, height});
    g_context->SetDensityIndependentPixelRatio(std::max(density, 1.0f));
}

PP_EXPORT int pp_rmlui_mouse_move(int x, int y, int modifiers)
{
    return g_context ? (g_context->ProcessMouseMove(x, y, ConvertModifiers(modifiers)) ? 1 : 0) : 0;
}

PP_EXPORT int pp_rmlui_mouse_button(int button, int down, int modifiers)
{
    if (!g_context) return 0;
    const bool propagated = down
        ? g_context->ProcessMouseButtonDown(button, ConvertModifiers(modifiers))
        : g_context->ProcessMouseButtonUp(button, ConvertModifiers(modifiers));
    return propagated ? 1 : 0;
}

PP_EXPORT int pp_rmlui_mouse_wheel(float delta_y, int modifiers)
{
    return g_context ? (g_context->ProcessMouseWheel(-delta_y, ConvertModifiers(modifiers)) ? 1 : 0) : 0;
}

PP_EXPORT int pp_rmlui_key(int key, int down, int modifiers)
{
    if (!g_context) return 0;
    const Rml::Input::KeyIdentifier converted = ConvertKey(key);
    if (converted == Rml::Input::KI_UNKNOWN) return 0;
    const bool propagated = down
        ? g_context->ProcessKeyDown(converted, ConvertModifiers(modifiers))
        : g_context->ProcessKeyUp(converted, ConvertModifiers(modifiers));
    return propagated ? 1 : 0;
}

PP_EXPORT int pp_rmlui_text(unsigned int codepoint)
{
    return g_context ? (g_context->ProcessTextInput(static_cast<Rml::Character>(codepoint)) ? 1 : 0) : 0;
}

PP_EXPORT void pp_rmlui_set_text(const char* name, const char* value)
{
    if (g_model && name) g_model->SetText(name, value ? value : "");
}

PP_EXPORT void pp_rmlui_set_bool(const char* name, int value)
{
    if (g_model && name) g_model->SetBool(name, value != 0);
}

PP_EXPORT int pp_rmlui_take_action(unsigned char* buffer, int capacity)
{
    if (!g_model || !buffer || capacity <= 1) return 0;
    std::string action;
    if (!g_model->TakeAction(action)) return 0;
    const int length = std::min(static_cast<int>(action.size()), capacity - 1);
    std::memcpy(buffer, action.data(), static_cast<size_t>(length));
    buffer[length] = 0;
    return length;
}
