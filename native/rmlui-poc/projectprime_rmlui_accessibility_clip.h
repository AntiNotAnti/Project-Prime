#pragma once
#include <RmlUi/Core/ComputedValues.h>
#include <RmlUi/Core/Element.h>
#include <RmlUi/Core/RenderBox.h>
#include "TransformState.h"
#include <algorithm>
#include <cmath>
#include <vector>

// Semantic visibility uses framebuffer polygons throughout. RmlUi's raw scissor
// rectangle omits CSS transforms; rendering handles those through clip masks.
namespace PrimeAccessibilityClipping {
using Polygon = std::vector<Rml::Vector2f>;
inline double Cross(Rml::Vector2f a, Rml::Vector2f b, Rml::Vector2f p)
{ return double(b.x-a.x)*(p.y-a.y)-double(b.y-a.y)*(p.x-a.x); }
inline double Area(const Polygon& polygon)
{
    double area=0;
    for(size_t i=0;i<polygon.size();++i) {const auto a=polygon[i],b=polygon[(i+1)%polygon.size()];area+=double(a.x)*b.y-double(a.y)*b.x;}
    return area*.5;
}
inline bool Project(Rml::Element* element,Rml::Vector2f origin,Rml::Vector2f size,Polygon& polygon)
{
    polygon={origin,{origin.x+size.x,origin.y},{origin.x+size.x,origin.y+size.y},{origin.x,origin.y+size.y}};
    const auto* state=element->GetTransformState();const auto* transform=state?state->GetTransform():nullptr;
    for(auto& point:polygon) {
        if(transform) {
            const auto projected=*transform*Rml::Vector4f(point.x,point.y,0.f,1.f);
            if(!std::isfinite(projected.w)||projected.w<=.00001f)return false;
            point={projected.x/projected.w,projected.y/projected.w};
        }
        if(!std::isfinite(point.x)||!std::isfinite(point.y))return false;
    }
    return std::abs(Area(polygon))>.00001;
}
inline void Intersect(Polygon& subject,const Polygon& clip)
{
    const double direction=Area(clip)>=0?1.:-1.;
    for(size_t edge=0;edge<clip.size()&&!subject.empty();++edge) {
        const auto a=clip[edge],b=clip[(edge+1)%clip.size()];Polygon result;
        auto previous=subject.back();double previous_distance=direction*Cross(a,b,previous);
        for(const auto current:subject) {
            const double distance=direction*Cross(a,b,current);
            if((distance>0)!=(previous_distance>0)) {
                const double t=previous_distance/(previous_distance-distance);
                result.push_back({float(previous.x+(current.x-previous.x)*t),float(previous.y+(current.y-previous.y)*t)});
            }
            if(distance>=0)result.push_back(current);
            previous=current;previous_distance=distance;
        }
        subject=std::move(result);
    }
}
inline bool Offscreen(Rml::Element* element,Rml::Vector2i viewport)
{
    if(!element||viewport.x<=0||viewport.y<=0)return true;
    Polygon visible;
    if(!Project(element,element->GetAbsoluteOffset(Rml::BoxArea::Border),element->GetBox().GetSize(Rml::BoxArea::Border),visible))return true;
    Intersect(visible,{{0,0},{float(viewport.x),0},{float(viewport.x),float(viewport.y)},{0,float(viewport.y)}});
    // Match pinned6.3 ElementUtilities::GetClippingRegion's effective ancestor
    // chain and authored clip:none/clip:N semantics, but project each clip before
    // intersecting instead of mixing raw layout rectangles with projected nodes.
    using Rml::Style::Clip;
    const auto target_clip=element->GetComputedValues().clip();
    int ignored=target_clip.GetNumber();
    auto* ancestor=target_clip==Clip::Type::None?nullptr:element->GetOffsetParent();
    for(int depth=0;ancestor&&!visible.empty();ancestor=ancestor->GetOffsetParent(),++depth) {
        if(depth>=64)return true;
        const auto& computed=ancestor->GetComputedValues();
        const bool enabled=computed.overflow_x()!=Rml::Style::Overflow::Visible||computed.overflow_y()!=Rml::Style::Overflow::Visible;
        const bool always=computed.clip()==Clip::Type::Always;
        if((always||enabled)&&ignored==0) {
            const bool content=always||ancestor->GetClientWidth()<ancestor->GetScrollWidth()-.5f||ancestor->GetClientHeight()<ancestor->GetScrollHeight()-.5f;
            if(content) {
                const auto area=ancestor->GetClipArea();Polygon clip;
                if(!Project(ancestor,ancestor->GetAbsoluteOffset(area).Round(),ancestor->GetRenderBox(area).GetFillSize(),clip))return true;
                Intersect(visible,clip);
            }
        }
        if(ignored>0&&enabled)--ignored;
        ignored=std::max(ignored,computed.clip().GetNumber());
        if(computed.clip()==Clip::Type::None)break;
    }
    return visible.size()<3||std::abs(Area(visible))<=.00001;
}
}
