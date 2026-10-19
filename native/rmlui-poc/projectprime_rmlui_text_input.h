#pragma once
#include "projectprime_rmlui_text_protocol.h"
#include "projectprime_rmlui_text_edit_protocol.h"
#include <cmath>
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
    using ForegroundLookup = bool (*)(Rml::ElementDocument*);
    void Attach(Rml::Context* context, uint64_t generation, DocumentLookup lookup, ForegroundLookup foreground)
    {
        Detach();
        context_ = context; generation_ = generation; lookup_ = lookup; foreground_ = foreground;
    }
    void Detach()
    {
        Cancel(); input_ = nullptr; field_ = nullptr; context_ = nullptr; lookup_ = nullptr; foreground_ = nullptr; InvalidateCaret(); Advance();
        generation_ = 0; pending_focus_.reset(); consumed_focus_serial_ = pending_focus_serial_; ++focus_epoch_;
    }
    void OnActivate(Rml::TextInputContext* input) noexcept override
    {
        try {
        if (input_ == input && !retired_) {
            if (pending_focus_.get() == field_) consumed_focus_serial_ = pending_focus_serial_;
            return;
        }
        // Pinned Context::OnFocusChange dispatches the Focus event before
        // GetFocusElement changes. Only the captured actual target can bind this
        // new TextInputContext; a stale/missing capture never adopts old focus.
        auto* target = pending_focus_.get();
        if (!input || !target || pending_focus_serial_ == consumed_focus_serial_ || !lookup_
            || !lookup_(target->GetOwnerDocument())) { AbandonAfterFailure(); return; }
        const uint64_t capture_serial = pending_focus_serial_;
        consumed_focus_serial_ = capture_serial;
        Cancel();
        if (pending_focus_serial_ != capture_serial || pending_focus_.get() != target) { AbandonAfterFailure(); return; }
        input_ = input; field_ = target;
        retired_ = false; owner_failed_ = false; last_sequence_ = 0; ++focus_epoch_; Advance();
        // ActivateKeyboard precedes OnActivate in pinned WidgetTextInput::Focus.
        if (caret_field_ != field_) InvalidateCaret();

        } catch (...) { AbandonAfterFailure(); }
    }
    void OnDeactivate(Rml::TextInputContext* input) noexcept override
    {
        try {
        if (input_ == input) { Cancel(); input_ = nullptr; field_ = nullptr; InvalidateCaret(); ++focus_epoch_; Advance(); }

        } catch (...) { AbandonAfterFailure(); }
    }
    void OnDestroy(Rml::TextInputContext* input) noexcept override
    {
        try {
        InvalidateCaret(); // discard even a pre-activation sample for a destroyed field
        // OnDestroy can run after the element has stopped supporting edits.
        if (input_ == input) { input_ = nullptr; field_ = nullptr; ResetComposition(); InvalidateCaret(); ++focus_epoch_; Advance(); }

        } catch (...) { AbandonAfterFailure(); }
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
        const char* utf8, int cursor, int selection_length) noexcept
    {
        if (internal_depth_) return false;
        try { return ComposeOwned(generation, document, focus_epoch, stage, utf8, cursor, selection_length); }
        catch (...) { AbandonAfterFailure(); return false; }
    }
    bool ComposeOwned(uint64_t generation, uint64_t document, uint64_t focus_epoch, int stage,
        const char* utf8, int cursor, int selection_length)
    {
        if (!input_ || !document || generation != generation_ || focus_epoch != focus_epoch_
            || document != ActiveDocument() || stage < 0 || stage > 3 || cursor < -1 || selection_length < 0 || !CurrentEditable()) return false;
        InternalMutation mutation(*this);
        if (stage == 0) return Begin();
        if (stage == 3) { CancelOwned(); return mutation.Current(); }
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
        if (!mutation.Current()) return false;
        range_end_ = range_start_ + static_cast<int>(Rml::StringUtilities::LengthUTF8(text));
        input_->SetCompositionRange(range_start_, range_end_);
        if (!mutation.Current()) return false;
        if (stage == 2) {
            if (range_end_ > range_start_) input_->CommitComposition(text);
            if (!mutation.Current()) return false;
            const int final_cursor = range_end_;
            ResetComposition();
            if (!mutation.Current()) return false;
            input_->SetCursorPosition(final_cursor);
        } else if (cursor < 0) input_->SetSelectionRange(range_start_, range_end_);
        else input_->SetSelectionRange(range_start_ + cursor, range_start_ + cursor + selection_length);
        return mutation.Current();
    }
    void Cancel() noexcept
    {
        if (internal_depth_) { DropComposition(); return; }
        try { CancelOwned(); } catch (...) { AbandonAfterFailure(); }
    }
    void CancelOwned()
    {
        InternalMutation mutation(*this);
        if (composing_ && input_ && ActiveDocument() && composition_revision_ == revision_) {
            input_->SetText(original_selection_, range_start_, range_end_);
            if (!mutation.Current()) { DropComposition(); return; }
            input_->SetSelectionRange(range_start_, range_start_ + original_selection_length_);
        }
        ResetComposition();
    }
    bool IsComposing() const { return composing_; }

    // Bridge entrypoints call this before external owner mutations. Restoring a
    // composition is only appropriate before a physical edit, not before a
    // programmatic replacement or an already-dispatched unknown change event.
    void ExternalMutation(Rml::Element* target = nullptr, bool restore = false) noexcept
    {
        try {
        if (!input_ || (target && target != field_)) return;
        if (internal_depth_) { external_during_edit_ = true; Advance(); return; }
        if (restore) Cancel(); else ResetComposition();
        InvalidateCaret(); Advance();

        } catch (...) { AbandonAfterFailure(); }
    }
    void Unavailable(Rml::Element* ancestor) noexcept
    {
        try {
        for (auto* element = field_; element; element = element->GetParentNode())
            if (element == ancestor) {
                ResetComposition(); retired_ = true; InvalidateCaret(); Advance();
                if (auto* field = field_) field->Blur();
                return;
            }

        } catch (...) { AbandonAfterFailure(); }
    }
    void ValidateAvailability() noexcept
    {
        try {
        if (input_ && !CurrentEditable()) if (auto* field = field_) field->Blur();

        } catch (...) { AbandonAfterFailure(); }
    }
    void ObserveEditEvent(Rml::Event& event) noexcept
    {
        try {
        if (event.GetType() == "focus") {
            auto* target = event.GetTargetElement();
            auto* field = dynamic_cast<Rml::ElementFormControl*>(target);
            if (field) {
                const auto tag = target->GetTagName();
                const auto type = target->GetAttribute<Rml::String>("type", "text");
                if (tag == "textarea" || (tag == "input" && (type == "text" || type == "password" || type == "number"))) {
                    if (++pending_focus_serial_ == 0) { AbandonAfterFailure(); return; }
                    pending_focus_ = target->GetObserverPtr();
                }
            }
            return;
        }
        if (!internal_depth_ && (event.GetType() == "input" || event.GetType() == "change")) ExternalMutation(event.GetTargetElement());

        } catch (...) { AbandonAfterFailure(); }
    }
    void AbandonAfterFailure() noexcept
    {
        owner_failed_ = true; retired_ = true; pending_focus_.reset(); consumed_focus_serial_ = pending_focus_serial_;
        DropComposition(); InvalidateCaret(); Advance();
    }
    bool OwnerFailed() const noexcept { return owner_failed_; }
    void InvalidateCaret() { caret_field_ = nullptr; caret_height_ = 0; }
    Rml::Element* CaretOwner() const
    {
        if (pending_focus_serial_ != consumed_focus_serial_ && pending_focus_) return pending_focus_.get();
        return ActiveDocument() ? field_ : nullptr;
    }
    void CaptureCaret(Rml::Element* field, Rml::Vector2f position, float line_height)
    {
        if (!field || !std::isfinite(position.x) || !std::isfinite(position.y)
            || !std::isfinite(line_height) || line_height <= 0) { InvalidateCaret(); return; }
        caret_field_ = field; caret_position_ = position; caret_height_ = line_height;
    }
    bool EditState(PrimeTextEditState& result)
    {
        if (internal_depth_) return false;
        if (result.size != 88 || result.version != 1 || result.reserved || !CurrentEditable()) return false;
        PrimeTextIndexMap map;
        auto* field = ActiveField();
        if (!map.Read(field->GetValue())) return false;
        int start = 0, end = 0; input_->GetSelectionRange(start, end);
        if (start < 0 || end < start || end > map.Scalars()) return false;
        result = PrimeTextEditState{};
        result.generation = generation_; result.document_id = ActiveDocument(); result.focus_epoch = focus_epoch_; result.revision = revision_;
        result.capabilities = 1; // select/replace/marked/commit/cancel, UTF-16 v1
        if (field->GetAttribute<Rml::String>("type", "") == "password") result.flags |= 1;
        result.length = map.units.back(); result.selection_start = map.units[start]; result.selection_length = map.units[end] - map.units[start];
        if (composing_ && composition_revision_ == revision_) {
            if (range_start_ < 0 || range_end_ < range_start_ || range_end_ > map.Scalars()) return false;
            result.marked_start = map.units[range_start_]; result.marked_length = map.units[range_end_] - result.marked_start;
        }
        if (start == end && caret_field_ == field_ && caret_height_ > 0) {
            result.flags |= 2; result.x = caret_position_.x; result.y = caret_position_.y;
            result.width = 0; result.height = caret_height_; // true position/height; width unavailable, not guessed
        } else {
            Rml::Rectanglef box;
            if (input_->GetBoundingBox(box)) {
                result.flags |= 4; result.x = box.Left(); result.y = box.Top(); result.width = box.Width(); result.height = box.Height();
            }
        }
        return true;
    }
    int Edit(const PrimeTextEditRequest& request, const uint8_t* utf8, uint32_t bytes)
    {
        if (internal_depth_) return -7;
        if (request.size != 80 || request.version != 1 || request.reserved || (request.flags & ~1u) || (request.flags && request.kind != 4) || request.kind < 1 || request.kind > 5
            || bytes > 262144 || (!utf8 && bytes)) return -4;
        if (request.generation != generation_ || request.document_id != ActiveDocument() || request.focus_epoch != focus_epoch_) return -1;
        if (!CurrentEditable()) return -6;
        if (!request.sequence || request.sequence <= last_sequence_) return -3;
        if (request.revision != revision_) return -2;
        if (composing_ && composition_revision_ != revision_) { ResetComposition(); return -2; }
        Rml::String text = bytes ? Rml::String(reinterpret_cast<const char*>(utf8), bytes) : Rml::String{};
        Rml::String value = ActiveField()->GetValue();
        PrimeTextIndexMap existing, inserted;
        if (!existing.Read(value) || !inserted.Read(text)) return -4;
        if (request.flags == 1) {
            if (bytes || request.start != -1 || request.length || request.selected_start != -1 || request.selected_length) return -4;
            last_sequence_ = request.sequence;
            if (!composing_) return 1; // AppKit unmark with no marked text is a no-op
            if (range_start_ < 0 || range_end_ < range_start_ || range_end_ > existing.Scalars()) return -7;
            int before_start = 0, before_end = 0; input_->GetSelectionRange(before_start, before_end);
            const int maximum = ActiveField()->GetAttribute<int>("maxlength", -1);
            InternalMutation mutation(*this);
            if (maximum >= 0 && existing.Scalars() > maximum) {
                // Exceptional over-limit preedit: only the EXACT marked range is
                // read/replaced through the existing owner. No whole-field write.
                const int old_start = range_start_, old_end = range_end_;
                const int count = std::min(old_end - old_start, std::max(0, maximum - (existing.Scalars() - (old_end - old_start))));
                Rml::String marked = value.substr(size_t(existing.bytes[old_start]), size_t(existing.bytes[old_start + count] - existing.bytes[old_start]));
                value.replace(size_t(existing.bytes[old_start]), size_t(existing.bytes[old_end] - existing.bytes[old_start]), marked);
                if (!ComposeOwned(generation_, request.document_id, focus_epoch_, 2, marked.c_str(), -1, 0)) return -7;
                auto mapped = [old_start, old_end, count](int position) {
                    return position <= old_start ? position : position >= old_end ? position + count - (old_end - old_start) : std::min(position, old_start + count);
                };
                before_start = mapped(before_start); before_end = mapped(before_end);
                if (!mutation.Current()) return -7;
                input_->SetSelectionRange(before_start, before_end);
            } else ResetComposition();
            if (!mutation.Current()) return -7;
            int after_start = 0, after_end = 0; input_->GetSelectionRange(after_start, after_end);
            return !external_during_edit_ && ActiveField() && ActiveField()->GetValue() == value
                && before_start == after_start && before_end == after_end ? 1 : -7;
        }
        int begin = 0, removed = 0, selected = 0, selected_length = 0;
        if (request.kind == 5) {
            if (bytes || request.start != -1 || request.length || request.selected_start != -1 || request.selected_length) return -4;
            if (!composing_) return -5;
            if (range_start_ < 0 || range_end_ < range_start_ || range_end_ > existing.Scalars()) return -7;
            Rml::String expected = value;
            expected.replace(size_t(existing.bytes[range_start_]), size_t(existing.bytes[range_end_] - existing.bytes[range_start_]), original_selection_);
            const int selected_start = range_start_, selected_end = range_start_ + original_selection_length_;
            last_sequence_ = request.sequence; CancelOwned();
            if (owner_failed_ || request.focus_epoch != focus_epoch_ || request.document_id != ActiveDocument()) return -7;
            int after_start = 0, after_end = 0;
            if (input_) input_->GetSelectionRange(after_start, after_end);
            return !external_during_edit_ && ActiveField() && ActiveField()->GetValue() == expected
                && after_start == selected_start && after_end == selected_end ? 1 : -7;
        }
        if (request.kind == 1) {
            if (composing_) return -5;
            if (bytes || request.selected_start != -1 || request.selected_length || !existing.Range(request.start, request.length, begin, removed)) return -4;
            last_sequence_ = request.sequence; InternalMutation mutation(*this); input_->SetSelectionRange(begin, begin + removed);
            if (!mutation.Current()) return -7;
            int after_start = 0, after_end = 0; input_->GetSelectionRange(after_start, after_end);
            return mutation.Current() && after_start == begin && after_end == begin + removed ? 1 : -7;
        }
        if ((request.kind == 2 && composing_) || (request.kind == 4 && !composing_)) return -5;
        if (request.start == -1 && request.length == 0) {
            if (composing_) { begin = range_start_; removed = range_end_ - range_start_; }
            else { int end = 0; input_->GetSelectionRange(begin, end); removed = end - begin; }
        } else if (!existing.Range(request.start, request.length, begin, removed)) return -4;
        if (begin < 0 || removed < 0 || begin > existing.Scalars() || removed > existing.Scalars() - begin) return -4;
        if (composing_ && (begin != range_start_ || removed != range_end_ - range_start_)) return -4;
        if (request.kind == 3) {
            if (!inserted.Range(request.selected_start, request.selected_length, selected, selected_length)) return -4;
        } else if (request.selected_start != -1 || request.selected_length) return -4;
        const int maximum = ActiveField()->GetAttribute<int>("maxlength", -1);
        if (request.kind != 3 && maximum >= 0) {
            int available = std::max(0, maximum - (existing.Scalars() - removed));
            text.resize(size_t(inserted.bytes[std::min(inserted.Scalars(), available)]));
            if (!inserted.Read(text)) return -4;
        }
        Rml::String expected = value;
        expected.replace(size_t(existing.bytes[begin]), size_t(existing.bytes[begin + removed] - existing.bytes[begin]), text);
        PrimeTextIndexMap expected_map;
        if (!expected_map.Read(expected)) return -4;
        last_sequence_ = request.sequence; // consumed before any effect, including a throwing owner
        try {
            InternalMutation mutation(*this);
            if (request.kind == 2) {
                input_->SetText(text, begin, begin + removed);
                if (!mutation.Current()) return -7;
                input_->SetCursorPosition(begin + inserted.Scalars());
            }
            else {
                if (!composing_) {
                    input_->SetSelectionRange(begin, begin + removed);
                    if (!mutation.Current() || !Begin()) return -7;
                }
                if (!ComposeOwned(generation_, request.document_id, focus_epoch_, request.kind == 3 ? 1 : 2, text.c_str(),
                    request.kind == 3 ? selected : -1, request.kind == 3 ? selected_length : 0)) return -7;
            }
            if (!mutation.Current()) return -7;
            int after_start = 0, after_end = 0;
            input_->GetSelectionRange(after_start, after_end);
            const int selected_start = request.kind == 3 ? begin + selected : begin + inserted.Scalars();
            const int selected_end = request.kind == 3 ? selected_start + selected_length : selected_start;
            if (external_during_edit_ || !ActiveField() || ActiveField()->GetValue() != expected
                || after_start != selected_start || after_end != selected_end) { ResetComposition(); InvalidateCaret(); return -7; }
            InvalidateCaret(); // text may await widget formatting; do not export a stale pre-layout sample
            return 1;
        } catch (...) { AbandonAfterFailure(); return -7; }
    }

private:
    struct InternalMutation {
        PrimeTextInputHandler& owner;
        Rml::TextInputContext* input;
        Rml::Element* field;
        uint64_t focus;
        explicit InternalMutation(PrimeTextInputHandler& value) : owner(value), input(value.input_), field(value.field_), focus(value.focus_epoch_)
        { if (owner.internal_depth_++ == 0) owner.external_during_edit_ = false; }
        bool Current() const
        { return !owner.owner_failed_ && !owner.external_during_edit_ && owner.input_ == input && owner.field_ == field
            && owner.focus_epoch_ == focus && owner.ActiveDocument() != 0; }
        ~InternalMutation() noexcept
        {
            if (--owner.internal_depth_ != 0) return;
            try {
                owner.Advance();
                if (!Current()) { if (owner.composition_input_ == input) owner.DropComposition(); }
                else if (owner.composing_) owner.composition_revision_ = owner.revision_;
            } catch (...) { owner.AbandonAfterFailure(); }
        }
    };
    Rml::Context* context_ = nullptr;
    Rml::Element* field_ = nullptr;
    Rml::ObserverPtr<Rml::Element> pending_focus_;
    uint64_t pending_focus_serial_ = 0, consumed_focus_serial_ = 0;
    ForegroundLookup foreground_ = nullptr;
    uint64_t revision_ = 1, composition_revision_ = 0, last_sequence_ = 0;
    int internal_depth_ = 0;
    bool external_during_edit_ = false;
    bool retired_ = false, owner_failed_ = false;
    Rml::TextInputContext* composition_input_ = nullptr;
    Rml::Element* caret_field_ = nullptr;
    Rml::Vector2f caret_position_{};
    float caret_height_ = 0;
    void Advance() { if (++revision_ == 0) { revision_ = 1; retired_ = true; } }
    bool CurrentEditable()
    {
        auto* field = ActiveField();
        if (!ActiveDocument() || !field || !field->IsVisible(true) || field->IsDisabled() || field->HasAttribute("readonly")) {
            ResetComposition(); InvalidateCaret(); retired_ = true; return false;
        }
        return true;
    }
    Rml::TextInputContext* input_ = nullptr;
    DocumentLookup lookup_ = nullptr;
    uint64_t generation_ = 0, focus_epoch_ = 0;
    bool composing_ = false;
    int range_start_ = 0, range_end_ = 0, original_length_ = 0, original_selection_length_ = 0;
    Rml::String original_selection_;

    uint64_t ActiveDocument() const
    {
        if (!context_ || !lookup_ || !foreground_ || !input_ || retired_) return 0;
        auto* element = context_->GetFocusElement();
        auto* document = element ? element->GetOwnerDocument() : nullptr;
        return element && element == field_ && element->IsVisible(true) && document && document->IsVisible() && foreground_(document) ? lookup_(document) : 0;
    }
    Rml::ElementFormControl* ActiveField() const
    {
        return context_ && context_->GetFocusElement() == field_ ? dynamic_cast<Rml::ElementFormControl*>(field_) : nullptr;
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
        composing_ = true; composition_input_ = input_; composition_revision_ = revision_;
        return true;
    }
    void DropComposition() noexcept
    {
        composing_ = false; composition_input_ = nullptr;
        range_start_ = range_end_ = original_length_ = original_selection_length_ = 0;
        original_selection_.clear();
    }
    void ResetComposition()
    {
        // A reentrant focus change must never clear the NEW owner's marker.
        auto* marked_owner = composition_input_;
        const bool current = marked_owner && marked_owner == input_ && ActiveDocument() && !owner_failed_;
        DropComposition();
        if (current) marked_owner->SetCompositionRange(0, 0);
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
