#pragma once
#include "projectprime_rmlui_text_protocol.h"
#include <RmlUi/Core.h>
#include <RmlUi/Core/TextInputContext.h>
#include <RmlUi/Core/TextInputHandler.h>
#include <RmlUi/Core/Elements/ElementFormControl.h>
#include <algorithm>
#include <limits>

// Platform-neutral RmlUi composition owner. Each focus change invalidates old
// platform callbacks, even when focus later returns to the same DOM element.
// It stores edit text only for cancel restoration and never logs it.
class PrimeTextInputHandler final : public Rml::TextInputHandler {
public:
    using DocumentLookup = uint64_t (*)(Rml::ElementDocument*);
    void Attach(Rml::Context* context, uint64_t generation, DocumentLookup lookup)
    {
        Detach();
        context_ = context; generation_ = generation; lookup_ = lookup;
    }
    void Detach()
    {
        Cancel(); input_ = nullptr; context_ = nullptr; lookup_ = nullptr;
        generation_ = 0; ++focus_epoch_;
    }
    void OnActivate(Rml::TextInputContext* input) override
    {
        if (input_ == input) return;
        Cancel(); input_ = input; ++focus_epoch_;
    }
    void OnDeactivate(Rml::TextInputContext* input) override
    {
        if (input_ == input) { Cancel(); input_ = nullptr; ++focus_epoch_; }
    }
    void OnDestroy(Rml::TextInputContext* input) override
    {
        // OnDestroy can run after the element has stopped supporting edits.
        if (input_ == input) { input_ = nullptr; ResetComposition(); ++focus_epoch_; }
    }
    bool State(PrimeTextInputState& result) const
    {
        if (result.size != sizeof(PrimeTextInputState) || result.version != PP_RMLUI_TEXT_INPUT_VERSION) return false;
        uint64_t document = ActiveDocument();
        if (!input_ || !document) return false;
        result = PrimeTextInputState{};
        result.generation = generation_; result.document_id = document; result.focus_epoch = focus_epoch_;
        input_->GetSelectionRange(result.selection_start, result.selection_end);
        result.composing = composing_ ? 1 : 0; result.capabilities = 1;
        if (auto* field = ActiveField())
            if (field->GetAttribute<Rml::String>("type", "") == "password") result.capabilities |= 4;
        Rml::Rectanglef rectangle;
        if (input_->GetBoundingBox(rectangle)) {
            result.x = rectangle.Left(); result.y = rectangle.Top();
            result.width = rectangle.Width(); result.height = rectangle.Height(); result.capabilities |= 2;
        }
        return true;
    }
    bool Compose(uint64_t generation, uint64_t document, uint64_t focus_epoch, int stage,
        const char* utf8, int cursor, int selection_length)
    {
        if (!input_ || !document || generation != generation_ || focus_epoch != focus_epoch_
            || document != ActiveDocument() || stage < 0 || stage > 3 || cursor < -1 || selection_length < 0) return false;
        if (stage == 0) return Begin();
        if (stage == 3) { Cancel(); return true; }
        if (!composing_ || !utf8) return false;
        Rml::String text(utf8);
        if (text.size() > 262144 || !ValidUtf8(text)) return false;
        const int length = static_cast<int>(Rml::StringUtilities::LengthUTF8(text));
        if (cursor > length || selection_length > length || (cursor >= 0 && selection_length > length - cursor)) return false;
        if (stage == 2) {
            // Respect the authored field length on commit. Temporary preedit
            // may be longer, matching native IME behavior.
            if (auto* field = ActiveField()) {
                const int maximum = field->GetAttribute<int>("maxlength", -1);
                if (maximum >= 0) {
                    const int available = std::max(0, maximum - (original_length_ - original_selection_length_));
                    text.resize(static_cast<size_t>(Rml::StringUtilities::ConvertCharacterOffsetToByteOffset(text, std::min(length, available))));
                }
            }
        }
        input_->SetText(text, range_start_, range_end_);
        range_end_ = range_start_ + static_cast<int>(Rml::StringUtilities::LengthUTF8(text));
        input_->SetCompositionRange(range_start_, range_end_);
        if (stage == 2) {
            if (range_end_ > range_start_) input_->CommitComposition(text);
            const int final_cursor = range_end_;
            ResetComposition(); input_->SetCursorPosition(final_cursor);
        } else if (cursor < 0) input_->SetSelectionRange(range_start_, range_end_);
        else input_->SetSelectionRange(range_start_ + cursor, range_start_ + cursor + selection_length);
        return true;
    }
    void Cancel()
    {
        if (composing_ && input_) {
            input_->SetText(original_selection_, range_start_, range_end_);
            input_->SetSelectionRange(range_start_, range_start_ + original_selection_length_);
        }
        ResetComposition();
    }
    bool IsComposing() const { return composing_; }

private:
    Rml::Context* context_ = nullptr;
    Rml::TextInputContext* input_ = nullptr;
    DocumentLookup lookup_ = nullptr;
    uint64_t generation_ = 0, focus_epoch_ = 0;
    bool composing_ = false;
    int range_start_ = 0, range_end_ = 0, original_length_ = 0, original_selection_length_ = 0;
    Rml::String original_selection_;

    uint64_t ActiveDocument() const
    {
        if (!context_ || !lookup_ || !input_) return 0;
        auto* element = context_->GetFocusElement();
        auto* document = element ? element->GetOwnerDocument() : nullptr;
        return document && document->IsVisible() ? lookup_(document) : 0;
    }
    Rml::ElementFormControl* ActiveField() const
    {
        return context_ ? dynamic_cast<Rml::ElementFormControl*>(context_->GetFocusElement()) : nullptr;
    }
    bool Begin()
    {
        if (composing_) return true;
        input_->GetSelectionRange(range_start_, range_end_);
        if (range_end_ < range_start_) std::swap(range_start_, range_end_);
        original_selection_.clear(); original_length_ = original_selection_length_ = 0;
        if (auto* field = ActiveField()) {
            const Rml::String value = field->GetValue();
            original_length_ = static_cast<int>(Rml::StringUtilities::LengthUTF8(value));
            const int begin = Rml::StringUtilities::ConvertCharacterOffsetToByteOffset(value, range_start_);
            const int end = Rml::StringUtilities::ConvertCharacterOffsetToByteOffset(value, range_end_);
            original_selection_ = value.substr(static_cast<size_t>(begin), static_cast<size_t>(end - begin));
            original_selection_length_ = range_end_ - range_start_;
        }
        composing_ = true;
        return true;
    }
    void ResetComposition()
    {
        if (input_) input_->SetCompositionRange(0, 0);
        composing_ = false; range_start_ = range_end_ = original_length_ = original_selection_length_ = 0;
        original_selection_.clear();
    }
    static bool ValidUtf8(const Rml::String& text)
    {
        // Reject malformed scalar sequences at the C ABI rather than allowing
        // invalid byte offsets to reach the widget's character-range methods.
        const auto* bytes = reinterpret_cast<const unsigned char*>(text.data());
        size_t position = 0;
        while (position < text.size()) {
            uint32_t value = bytes[position++]; int following = 0; uint32_t minimum = 0;
            if (value <= 0x7f) continue;
            if (value >= 0xc2 && value <= 0xdf) { value &= 0x1f; following = 1; minimum = 0x80; }
            else if (value >= 0xe0 && value <= 0xef) { value &= 0x0f; following = 2; minimum = 0x800; }
            else if (value >= 0xf0 && value <= 0xf4) { value &= 0x07; following = 3; minimum = 0x10000; }
            else return false;
            if (position + following > text.size()) return false;
            while (following--) {
                const uint32_t next = bytes[position++]; if ((next & 0xc0) != 0x80) return false;
                value = (value << 6) | (next & 0x3f);
            }
            if (value < minimum || value > 0x10ffff || (value >= 0xd800 && value <= 0xdfff)) return false;
        }
        return true;
    }
};
