#pragma once
#include <stdint.h>
#include <algorithm>
#include <string>
#include <vector>

struct PrimeTextEditRequest {
    uint32_t size = 80, version = 1;
    uint64_t generation = 0, document_id = 0, focus_epoch = 0, revision = 0, sequence = 0;
    uint32_t kind = 0, flags = 0;
    int32_t start = -1, length = 0, selected_start = -1, selected_length = 0;
    uint64_t reserved = 0;
};
struct PrimeTextEditState {
    uint32_t size = 88, version = 1;
    uint64_t generation = 0, document_id = 0, focus_epoch = 0, revision = 0;
    uint32_t capabilities = 0, flags = 0;
    int32_t length = 0, selection_start = 0, selection_length = 0, marked_start = -1, marked_length = 0;
    float x = 0, y = 0, width = 0, height = 0;
    uint32_t reserved = 0;
};
static_assert(sizeof(PrimeTextEditRequest) == 80, "text edit request ABI");
static_assert(sizeof(PrimeTextEditState) == 88, "text edit state ABI");

// Per-call numeric boundary map, not a cached public editable value. No malformed
// scalar, NUL, oversized field or interior-surrogate endpoint reaches RmlUi.
struct PrimeTextIndexMap {
    std::vector<int> bytes, units;
    bool Read(const std::string& text)
    {
        bytes.clear(); units.clear(); bytes.push_back(0); units.push_back(0);
        if (text.size() > 262144) return false;
        size_t position = 0; int utf16 = 0;
        while (position < text.size()) {
            uint32_t value = static_cast<unsigned char>(text[position++]);
            int following = 0; uint32_t minimum = 0;
            if (value == 0) return false;
            if (value <= 0x7f) {}
            else if (value >= 0xc2 && value <= 0xdf) { value &= 0x1f; following = 1; minimum = 0x80; }
            else if (value >= 0xe0 && value <= 0xef) { value &= 0x0f; following = 2; minimum = 0x800; }
            else if (value >= 0xf0 && value <= 0xf4) { value &= 0x07; following = 3; minimum = 0x10000; }
            else return false;
            if (position + following > text.size()) return false;
            while (following--) {
                uint32_t next = static_cast<unsigned char>(text[position++]);
                if ((next & 0xc0) != 0x80) return false;
                value = (value << 6) | (next & 0x3f);
            }
            if (value < minimum || value > 0x10ffff || (value >= 0xd800 && value <= 0xdfff)) return false;
            utf16 += value > 0xffff ? 2 : 1;
            if (utf16 > 65536) return false;
            bytes.push_back(int(position)); units.push_back(utf16);
        }
        return true;
    }
    int Scalars() const { return int(units.size()) - 1; }
    bool Range(int start, int length, int& scalar, int& count) const
    {
        if (start < 0 || length < 0 || start > units.back() || length > units.back() - start) return false;
        auto first = std::lower_bound(units.begin(), units.end(), start);
        auto last = std::lower_bound(units.begin(), units.end(), start + length);
        if (first == units.end() || last == units.end() || *first != start || *last != start + length) return false;
        scalar = int(first - units.begin()); count = int(last - first); return true;
    }
};
