#pragma once
#include <RmlUi/Core/Element.h>
#include <RmlUi/Core/Matrix4.h>
#include "TransformState.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <limits>

// Border bounds in the same framebuffer coordinates used by rendering/hit
// testing. GetAbsoluteOffset alone excludes accumulated CSS transforms.
inline bool PrimeElementBounds(Rml::Element* element, float& x, float& y, float& width, float& height)
{
    if (!element || !element->IsVisible(true)) return false;
    const auto origin = element->GetAbsoluteOffset(Rml::BoxArea::Border);
    const auto size = element->GetBox().GetSize(Rml::BoxArea::Border);
    if (!std::isfinite(origin.x) || !std::isfinite(origin.y)
        || !std::isfinite(size.x) || !std::isfinite(size.y) || size.x <= 0.f || size.y <= 0.f) return false;
    const auto* state = element->GetTransformState();
    const auto* transform = state ? state->GetTransform() : nullptr;
    const std::array<Rml::Vector2f,4> corners = {{
        origin, {origin.x + size.x, origin.y}, {origin.x, origin.y + size.y},
        {origin.x + size.x, origin.y + size.y}
    }};
    float left = std::numeric_limits<float>::infinity(), top = left;
    float right = -left, bottom = right;
    for (auto point : corners) {
        if (transform) {
            const auto projected = *transform * Rml::Vector4f(point.x, point.y, 0.f, 1.f);
            if (!std::isfinite(projected.w) || projected.w <= 0.00001f) return false;
            point = {projected.x / projected.w, projected.y / projected.w};
        }
        if (!std::isfinite(point.x) || !std::isfinite(point.y)) return false;
        left = std::min(left,point.x); top = std::min(top,point.y);
        right = std::max(right,point.x); bottom = std::max(bottom,point.y);
    }
    x = left; y = top; width = right-left; height = bottom-top;
    return std::isfinite(width) && std::isfinite(height) && width > 0.f && height > 0.f;
}
