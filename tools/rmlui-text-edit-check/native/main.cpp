// Actual C-ABI owner regression. Link against the coordinated native bridge. Uses actual RmlUi TextInputContext + draw-list
// backend, no window/OpenGL/AppKit/game assets/network/user data.
#include "projectprime_rmlui_api.h"
#include "projectprime_rmlui_text_input.h"
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>

static_assert(PP_RMLUI_PROTOCOL_VERSION == 1 && sizeof(PrimeIntent) == 40, "old intent ABI unchanged");
static_assert(uint32_t(PrimeIntentKind::AdventureCancelSaveChange) == 132, "Adventure append-only family unchanged");
static_assert(PP_RMLUI_REPLAY_ACTION_COUNT == 135 && PP_RMLUI_STUDIO_ACTION_COUNT == 200, "current root action counts preserved");
static_assert(sizeof(PrimeTextInputState) == 64 && sizeof(PrimeTextEditRequest) == 80 && sizeof(PrimeTextEditState) == 88, "old/new IME ABI sizes");
static_assert(noexcept(std::declval<PrimeTextInputHandler&>().Cancel()), "callback cancellation cannot unwind");
static_assert(noexcept(std::declval<PrimeTextInputHandler&>().ExternalMutation()), "new entrypoint callback cannot unwind");
static_assert(noexcept(std::declval<PrimeTextInputHandler&>().AbandonAfterFailure()), "failure cleanup cannot unwind or restore");

PP_EXPORT void pp_rmlui_update();
PP_EXPORT int pp_rmlui_key(int key, int down, int modifiers);
PP_EXPORT int pp_rmlui_text(unsigned int codepoint);
PP_EXPORT int pp_rmlui_mouse_move(int x, int y, int modifiers);
PP_EXPORT int pp_rmlui_mouse_button(int button, int down, int modifiers);

static int checks = 0;
static uint64_t sequence = 0;
static void Check(bool value, const char* reason) { ++checks; if (!value) throw std::runtime_error(reason); }
static PrimeTextEditState State()
{
    PrimeTextEditState state;
    Check(pp_rmlui_text_edit_state_v1(&state) == 1, "actual current owner numeric state"); return state;
}
static PrimeTextEditRequest Request(const PrimeTextEditState& state, uint32_t kind)
{
    PrimeTextEditRequest request;
    request.generation = state.generation; request.document_id = state.document_id; request.focus_epoch = state.focus_epoch;
    request.revision = state.revision; request.sequence = ++sequence; request.kind = kind; return request;
}
static int Edit(PrimeTextEditRequest& request, const std::string& text = {})
{
    return pp_rmlui_text_edit_v1(&request, reinterpret_cast<const uint8_t*>(text.data()), uint32_t(text.size()));
}
static std::string Field(uint64_t document, const char* id = "field")
{
    unsigned char bytes[1024]{};
    Check(pp_rmlui_document_read_field(document, id, bytes, sizeof(bytes)) >= 0, "actual field read for synthetic fixture only");
    return reinterpret_cast<const char*>(bytes);
}
int main(int argc, char** argv) try {
    if (argc != 2) return 2;
    auto root = std::filesystem::canonical(argv[1]);
    const auto name = root.string();
    if (name.find("/tmp/") != 0 && name.find("/private/tmp/") != 0) throw std::runtime_error("requires private temporary assets CLONE");
    const auto fixture = root / "pages/ime-owned-private.rml";
    if (std::filesystem::exists(fixture)) throw std::runtime_error("refuses to overwrite preexisting fixture");
    std::ofstream(fixture) << R"(<rml><head><style>body {font-family:Rajdhani;font-size:24px;} input {display:block;width:240px;height:48px;margin:8px;} #owner {display:block;}</style></head><body><div id="owner"><input id="field" type="text"/><input id="limited" type="text" maxlength="4"/><input id="other" type="text"/></div></body></rml>)";
    struct Cleanup { std::filesystem::path file; ~Cleanup() noexcept {
        try { pp_rmlui_shutdown(); } catch (...) { }
        std::error_code ignored; std::filesystem::remove(file, ignored);
    } } cleanup{fixture};
    Check(pp_rmlui_initialize_backend(640, 360, 1, root.c_str(), 1) == 1, "actual CPU draw-list native initializes");
    auto document = pp_rmlui_document_open("pages/ime-owned-private.rml", 0);
    Check(document != 0 && pp_rmlui_document_set_field(document, "field", "A😀B") == 1, "actual synthetic field seeded");
    Check(pp_rmlui_document_focus(document, "field") == 1, "actual widget focuses");
    auto state = State();
    auto marked = Request(state, 3); marked.start = 1; marked.length = 2; marked.selected_start = 3; marked.selected_length = 0;
    Check(Edit(marked, "日😀") == 1 && Field(document) == "A日😀B", "actual explicit UTF16 replacement preedit uses existing widget");
    state = State();
    Check(state.marked_start == 1 && state.marked_length == 3 && state.selection_start == 4, "actual selection and marking map UTF16");
    auto cancel = Request(state, 5);
    Check(Edit(cancel) == 1 && Field(document) == "A😀B", "actual current composition cancel restores only selected bytes");
    Check(Edit(cancel) == -3, "actual consumed callback sequence cannot replay");
    state = State(); marked = Request(state, 3); marked.start = 1; marked.length = 2; marked.selected_start = 3; marked.selected_length = 0;
    Check(Edit(marked, "日😀") == 1, "actual marked text before unmark");
    state = State(); auto unmark = Request(state, 4); unmark.flags = 1;
    Check(Edit(unmark) == 1 && Field(document) == "A日😀B", "actual unmark retains current bytes, does not restore original");
    auto accepted = State();
    Check(accepted.marked_start == -1 && accepted.selection_start == state.selection_start && accepted.selection_length == state.selection_length,
        "actual normal unmark clears marker retaining selection");
    unmark = Request(accepted, 4); unmark.flags = 1;
    Check(Edit(unmark) == 1 && Field(document) == "A日😀B", "actual unmark with no marking is a no-op");
    state = State(); auto bad = Request(state, 2); bad.start = 3; bad.length = 0;
    Check(Edit(bad, "x") == -4, "actual UTF16 interior surrogate rejected without rounding");
    bad.start = 0;
    Check(Edit(bad, std::string("\xf0\x80\x80\x80", 4)) == -4, "actual malformed UTF8 rejected");
    Check(Edit(bad, "x") == 1, "invalid packets do not consume a sequence or mutate owner");

    Check(pp_rmlui_document_set_field(document, "limited", "A😀Z") == 1 && pp_rmlui_document_focus(document, "limited") == 1,
        "actual authored maxlength owner prepared");
    state = State(); marked = Request(state, 3); marked.start = 1; marked.length = 2; marked.selected_start = 3; marked.selected_length = 2;
    Check(Edit(marked, "日😀語文") == 1 && Field(document, "limited") == "A日😀語文Z", "temporary actual preedit may exceed maxlength");
    state = State(); unmark = Request(state, 4); unmark.flags = 1;
    Check(Edit(unmark) == 1 && Field(document, "limited") == "A日😀Z", "actual unmark truncates only marked substring at scalar boundary");
    state = State();
    Check(state.selection_start == 4 && state.selection_length == 0 && state.marked_start == -1, "actual overlimit unmark remaps selection without splitting supplementary scalar");

    Check(pp_rmlui_document_focus(document, "field") == 1, "refocus ordinary owner");
    state = State(); auto old = Request(state, 2); old.start = 0; old.length = 0;
    pp_rmlui_key(7, 1, 0); pp_rmlui_key(7, 0, 0);
    Check(Edit(old, "forbidden") != 1, "actual physical caret key invalidates captured revision");
    state = State(); old = Request(state, 2); old.start = 0; old.length = 0;
    pp_rmlui_text('q');
    Check(Edit(old, "forbidden") != 1, "actual physical committed text invalidates captured revision");
    state = State(); old = Request(state, 2); old.start = 0; old.length = 0;
    float x, y, width, height;
    Check(pp_rmlui_document_element_bounds(document, "field", &x, &y, &width, &height) == 1, "actual pointer field bounds");
    pp_rmlui_mouse_move(int(x + width / 2), int(y + height / 2), 0);
    pp_rmlui_mouse_button(0, 1, 0); pp_rmlui_mouse_button(0, 0, 0);
    Check(Edit(old, "forbidden") != 1, "actual pointer caret/selection invalidates captured revision");
    state = State(); old = Request(state, 2); old.start = 0; old.length = 0;
    Check(pp_rmlui_document_set_field(document, "field", "authoritative") == 1 && Edit(old, "forbidden") != 1
        && Field(document) == "authoritative", "actual programmatic owner write retires old mutation authority");
    state = State(); marked = Request(state, 3); marked.selected_start = 3; marked.selected_length = 0;
    Check(Edit(marked, "new") == 1, "mark before external replacement");
    state = State(); cancel = Request(state, 5);
    pp_rmlui_document_set_field(document, "field", "raced");
    Check(Edit(cancel) != 1 && Field(document) == "raced", "old cancellation never restores over actual new owner bytes");
    state = State(); old = Request(state, 2); old.start = 0; old.length = 0;
    pp_rmlui_document_set_bool(document, "visible:owner", 0); pp_rmlui_document_set_bool(document, "visible:owner", 1);
    Check(Edit(old, "forbidden") != 1 && pp_rmlui_document_focus(document, "field") == 1, "hide-show retires and can establish a fresh real focus");
    state = State();
    Check(state.focus_epoch != old.focus_epoch, "actual reactivated field has new lifetime");
    old = Request(state, 2); old.start = 0; old.length = 0;
    Check(pp_rmlui_document_set_enabled(document, "field", 0) == 1 && pp_rmlui_document_set_enabled(document, "field", 1) == 1,
        "actual disabled-enabled binding roundtrip");
    Check(Edit(old, "forbidden") != 1 && pp_rmlui_document_focus(document, "field") == 1,
        "disabled-enabled roundtrip cannot resurrect an old edit witness");
    state = State(); old = Request(state, 2); old.start = 0; old.length = 0;
    const auto background_bytes = Field(document);
    auto modal = pp_rmlui_document_open("pages/ime-owned-private.rml", 1);
    Check(modal != 0 && Edit(old, "forbidden") != 1 && Field(document) == background_bytes,
        "actual modal foreground fences a still-live background document");
    Check(pp_rmlui_document_close(modal) == 1 && pp_rmlui_document_focus(document, "field") == 1,
        "actual modal retirement restores fresh parent editing");
    state = State(); bad = Request(state, 3); bad.flags = 1; bad.selected_start = 0;
    Check(Edit(bad, "x") == -4, "unmark flag is rejected on non-Commit even with a valid scope");
    bad = Request(state, 2); bad.start = 0; bad.length = INT32_MAX;
    Check(Edit(bad, "x") == -4 && Field(document) == background_bytes, "overflowing range rejects without mutation");
    bool rejected_thread = false;
    std::thread worker([&] { PrimeTextEditState background; rejected_thread = pp_rmlui_text_edit_state_v1(&background) == 0; }); worker.join();
    Check(rejected_thread, "actual optional native exports enforce owner thread");
    std::cout << "PASS " << checks << " actual CPU native text owner checks; no AppKit or GL qualification\n";
} catch (const std::exception& error) { std::cerr << "FAIL CPU text owner fixture: " << error.what() << '\n'; return 1; }
catch (...) { std::cerr << "FAIL CPU text owner fixture: unknown exception\n"; return 1; }
