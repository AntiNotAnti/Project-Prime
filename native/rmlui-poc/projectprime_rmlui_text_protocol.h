#pragma once
#include <cstdint>

constexpr uint32_t PP_RMLUI_TEXT_INPUT_VERSION = 1;
struct PrimeTextInputState {
    uint32_t size = sizeof(PrimeTextInputState);
    uint32_t version = PP_RMLUI_TEXT_INPUT_VERSION;
    uint64_t generation = 0;
    uint64_t document_id = 0;
    uint64_t focus_epoch = 0;
    float x = 0, y = 0, width = 0, height = 0;
    int32_t selection_start = 0, selection_end = 0, composing = 0;
    uint32_t capabilities = 0; // 1 composition, 2 framebuffer text-area bounds, 4 protected field.
};
static_assert(sizeof(PrimeTextInputState) == 64, "Text input ABI layout changed");

// Composition stages: Begin=0, Update=1, Commit=2, Cancel=3. Cursor and
// selection lengths count Unicode scalar values, never UTF-8/UTF-16 units.
