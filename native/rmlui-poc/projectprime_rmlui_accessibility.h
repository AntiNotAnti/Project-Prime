#pragma once
#include <RmlUi/Core/Element.h>
#include <RmlUi/Core/ElementDocument.h>
#include <RmlUi/Core/ElementText.h>
#include <RmlUi/Core/ElementUtilities.h>
#include <RmlUi/Core/Context.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <iomanip>
#include <sstream>
#include <string>
#include <unordered_map>
#include <vector>

// Owner-thread semantic projection of the live DOM. This header never reads an
// editable value, including password fields, and never calls platform APIs.
class PrimeAccessibilityTree final {
public:
    static constexpr size_t MaximumNodes = 4096;
    static constexpr size_t MaximumBytes = 1024 * 1024;
    enum Actions { Focus = 1, Press = 2, Scroll = 4, SetText = 8 };
    const std::string& Snapshot(Rml::ElementDocument* document, Rml::Element* focus,
        uint64_t generation, uint64_t document_id)
    {
        auto& state = states[document_id];
        std::vector<Node> nodes;
        std::unordered_map<std::string, std::string> labels;
        if (document && document->IsVisible()) {
            CollectLabels(document, labels, 0);
            Collect(document, document, focus, "0", false, labels, nodes, 0);
        }
        std::string payload = Payload(nodes);
        std::string signature = payload;
        // Authored action identity participates privately in the revision. A
        // recycled row cannot retain authority merely because its label matches.
        for (const auto& node : nodes) signature += "\n" + node.key + "\n" + node.action;
        if (state.generation != generation || state.signature != signature) {
            state.generation = generation;
            state.signature = std::move(signature);
            state.revision = ++next_revision;
        }
        state.elements.clear(); state.action_masks.clear();
        for (const auto& node : nodes) { state.elements.emplace(node.key, node.element); state.action_masks.emplace(node.key, node.actions); }
        state.json = "{\"version\":1,\"generation\":" + std::to_string(generation)
            + ",\"document\":" + std::to_string(document_id) + ",\"revision\":"
            + std::to_string(state.revision) + ",\"nodes\":" + payload + "}";
        return state.json;
    }
    Rml::Element* Resolve(Rml::ElementDocument* document, Rml::Element* focus,
        uint64_t generation, uint64_t document_id, uint64_t revision, const char* key)
    {
        if (!document || !key || !*key || !document->IsVisible()) return nullptr;
        Snapshot(document, focus, generation, document_id);
        auto& state = states[document_id];
        if (state.generation != generation || state.revision != revision) return nullptr;
        const auto found = state.elements.find(key);
        return found == state.elements.end() || !Visible(found->second) || !Enabled(found->second)
            ? nullptr : found->second;
    }
    uint64_t Revision(uint64_t document_id) const {
        const auto found = states.find(document_id);
        return found == states.end() ? 0 : found->second.revision;
    }
    int ActionMask(uint64_t document_id, const char* key) const {
        const auto state = states.find(document_id);
        if (state == states.end() || !key) return 0;
        const auto found = state->second.action_masks.find(key);
        return found == state->second.action_masks.end() ? 0 : found->second;
    }
    void Clear() { states.clear(); }
    void Forget(uint64_t document_id) { states.erase(document_id); }
private:
    struct Node {
        Rml::Element* element = nullptr;
        std::string key, id, role, label, action;
        bool enabled = false, focused = false, protected_value = false, offscreen = false;
        float x = 0, y = 0, width = 0, height = 0;
        int actions = 0;
    };
    struct State {
        uint64_t generation = 0, revision = 0;
        std::string signature, json;
        std::unordered_map<std::string, Rml::Element*> elements;
        std::unordered_map<std::string, int> action_masks;
    };
    std::unordered_map<uint64_t, State> states;
    uint64_t next_revision = 0;
    static std::string Attribute(Rml::Element* element, const char* name) {
        return element->GetAttribute<std::string>(name, "");
    }
    static bool Visible(Rml::Element* element) {
        if (!element || !element->IsVisible(true)) return false;
        for (auto* current = element; current; current = current->GetParentNode())
            if (Attribute(current, "aria-hidden") == "true") return false;
        return true;
    }
    static bool Enabled(Rml::Element* element) {
        for (auto* current = element; current; current = current->GetParentNode())
            if (current->HasAttribute("disabled") || Attribute(current, "aria-disabled") == "true") return false;
        return true;
    }
    static std::string Clean(std::string text) {
        std::string result; bool whitespace = true;
        for (unsigned char c : text) {
            if (c <= 32) { if (!whitespace) result += ' '; whitespace = true; }
            else { result += static_cast<char>(c); whitespace = false; }
            if (result.size() >= 8192) break;
        }
        if (!result.empty() && result.back() == ' ') result.pop_back();
        // A bounded label never ends midway through a UTF-8 code point.
        if (result.size() >= 8192) {
            size_t end = result.size();
            while (end > 0 && (static_cast<unsigned char>(result[end - 1]) & 0xc0) == 0x80) --end;
            if (end < result.size()) result.resize(end > 0 ? end - 1 : 0);
            else if (!result.empty() && static_cast<unsigned char>(result.back()) >= 0xc0) result.pop_back();
        }
        return result;
    }
    static std::string Text(Rml::Element* element, int depth = 0) {
        if (!Visible(element) || depth > 64) return "";
        const auto& tag = element->GetTagName();
        if (tag == "input" || tag == "textarea") return "";
        if (auto* text = dynamic_cast<Rml::ElementText*>(element)) return Clean(text->GetText());
        std::string result;
        for (int i = 0; i < element->GetNumChildren(); ++i) {
            std::string child = Text(element->GetChild(i), depth + 1);
            if (!child.empty()) { if (!result.empty()) result += ' '; result += child; }
            if (result.size() >= 8192) break;
        }
        return Clean(result);
    }
    static void CollectLabels(Rml::Element* element, std::unordered_map<std::string, std::string>& labels, int depth) {
        if (!Visible(element) || depth > 64 || labels.size() >= MaximumNodes) return;
        if (element->GetTagName() == "label") {
            std::string target = Attribute(element, "for");
            if (!target.empty()) labels[target] = Text(element);
        }
        for (int i = 0; i < element->GetNumChildren(); ++i) CollectLabels(element->GetChild(i), labels, depth + 1);
    }
    static std::string Role(Rml::Element* element) {
        std::string explicit_role = Attribute(element, "role");
        if (explicit_role == "presentation" || explicit_role == "none") return "";
        static const std::vector<std::string> roles = {"button","checkbox","radio","switch","textbox","combobox",
            "slider","heading","link","tab","tablist","list","listitem","dialog","alert","status","img","group","region"};
        if (std::find(roles.begin(), roles.end(), explicit_role) != roles.end()) return explicit_role;
        const auto& tag = element->GetTagName();
        if (tag == "button") return "button";
        if (tag == "input") {
            std::string type = Attribute(element, "type");
            if (type == "checkbox") return "checkbox";
            if (type == "radio") return "radio";
            if (type == "range") return "slider";
            if (type == "button" || type == "submit") return "button";
            if (type == "hidden") return "";
            return "textbox";
        }
        if (tag == "textarea") return "textbox";
        if (tag == "select") return "combobox";
        if (tag == "h1" || tag == "h2" || tag == "h3" || tag == "h4" || tag == "h5" || tag == "h6") return "heading";
        if (tag == "a" && element->HasAttribute("href")) return "link";
        if (tag == "img" && element->HasAttribute("alt")) return "img";
        if (element->HasAttribute("data-action")) return "button";
        if (element->GetScrollHeight() > element->GetClientHeight() + 1.f && element->GetClientHeight() > 0.f) return "region";
        if (dynamic_cast<Rml::ElementText*>(element)) return "text";
        return "";
    }
    static std::string Label(Rml::Element* element, Rml::ElementDocument* document,
        const std::unordered_map<std::string, std::string>& labels, const std::string& role)
    {
        std::string label = Clean(Attribute(element, "aria-label"));
        if (!label.empty()) return label;
        std::istringstream ids(Attribute(element, "aria-labelledby")); std::string id;
        while (ids >> id) if (auto* target = document->GetElementById(id)) {
            std::string part = Text(target); if (!part.empty()) { if (!label.empty()) label += ' '; label += part; }
        }
        if (!label.empty()) return Clean(label);
        const auto found = labels.find(element->GetId());
        if (found != labels.end() && !found->second.empty()) return found->second;
        if (element->GetTagName() == "img") return Clean(Attribute(element, "alt"));
        if (role == "textbox" || role == "slider" || role == "combobox") {
            if (auto* previous = element->GetPreviousSibling()) {
                std::string classes = Attribute(previous, "class");
                if (previous->GetTagName() == "label" || classes.find("label") != std::string::npos) {
                    label = Text(previous); if (!label.empty()) return label;
                }
            }
            label = Clean(Attribute(element, "placeholder"));
            return label.empty() ? (role == "textbox" ? "Text field" : role == "slider" ? "Slider" : "Choice") : label;
        }
        return Text(element);
    }
    static void Collect(Rml::Element* element, Rml::ElementDocument* document, Rml::Element* focus,
        const std::string& path, bool in_control, const std::unordered_map<std::string, std::string>& labels,
        std::vector<Node>& nodes, int depth)
    {
        if (!Visible(element) || depth > 64 || nodes.size() >= MaximumNodes) return;
        const std::string role = Role(element);
        const bool editable = role == "textbox" || role == "slider" || role == "combobox";
        const bool control = editable || role == "button" || role == "checkbox" || role == "radio" || role == "switch" || role == "link" || role == "tab";
        if (!role.empty() && !(in_control && (role == "text" || role == "heading" || role == "img"))) {
            const auto origin = element->GetAbsoluteOffset(Rml::BoxArea::Border);
            const auto size = element->GetBox().GetSize(Rml::BoxArea::Border);
            if (std::isfinite(origin.x) && std::isfinite(origin.y) && std::isfinite(size.x) && std::isfinite(size.y)
                && size.x > 0.f && size.y > 0.f) {
                Node node; node.element = element; node.key = "n" + path; node.id = element->GetId(); node.role = role;
                node.label = Label(element, document, labels, role); node.enabled = Enabled(element); node.focused = element == focus;
                node.protected_value = Attribute(element,"type") == "password"; node.action = Attribute(element,"data-action");
                node.x = origin.x; node.y = origin.y; node.width = size.x; node.height = size.y;
                if (auto* context = document->GetContext()) { auto viewport = context->GetDimensions();
                    node.offscreen = origin.x + size.x <= 0 || origin.y + size.y <= 0 || origin.x >= viewport.x || origin.y >= viewport.y; }
                Rml::Rectanglei clip;
                if (Rml::ElementUtilities::GetClippingRegion(element, clip))
                    node.offscreen = node.offscreen || origin.x + size.x <= clip.Left() || origin.y + size.y <= clip.Top()
                        || origin.x >= clip.Right() || origin.y >= clip.Bottom();
                if (node.enabled) {
                    if (control) node.actions |= Focus;
                    if (control && !editable) node.actions |= Press;
                    if ((role == "textbox" || role == "slider") && !element->HasAttribute("readonly")) node.actions |= SetText;
                    if (element->GetScrollHeight() > element->GetClientHeight() + 1.f) node.actions |= Scroll;
                }
                if (!node.label.empty() || control || role == "region") nodes.push_back(std::move(node));
            }
        }
        for (int i = 0; i < element->GetNumChildren(); ++i)
            Collect(element->GetChild(i), document, focus, path + "." + std::to_string(i), in_control || control,
                labels, nodes, depth + 1);
    }
    static std::string Quote(const std::string& value) {
        std::string result = "\"";
        for (unsigned char c : value) {
            if (c == '"' || c == '\\') { result += '\\'; result += static_cast<char>(c); }
            else if (c < 32) { static const char* hex = "0123456789abcdef"; result += "\\u00"; result += hex[c >> 4]; result += hex[c & 15]; }
            else result += static_cast<char>(c);
        }
        return result + '"';
    }
    static std::string Payload(std::vector<Node>& nodes) {
        std::string result = "["; bool first = true; size_t published = 0;
        for (const auto& node : nodes) {
            std::ostringstream item; item.imbue(std::locale::classic()); item << std::setprecision(7);
            item << "{\"key\":" << Quote(node.key) << ",\"id\":" << Quote(node.id) << ",\"role\":" << Quote(node.role)
                << ",\"label\":" << Quote(node.label) << ",\"enabled\":" << (node.enabled ? "true" : "false")
                << ",\"focused\":" << (node.focused ? "true" : "false") << ",\"protected\":" << (node.protected_value ? "true" : "false")
                << ",\"offscreen\":" << (node.offscreen ? "true" : "false") << ",\"x\":" << node.x << ",\"y\":" << node.y
                << ",\"width\":" << node.width << ",\"height\":" << node.height << ",\"actions\":" << node.actions << "}";
            const auto entry = item.str();
            if (result.size() + entry.size() + 256 > MaximumBytes) break;
            if (!first) result += ','; first = false; result += entry; ++published;
        }
        nodes.resize(published); return result + ']';
    }
};
