from pathlib import Path
import subprocess
root=next(p for p in Path(__file__).resolve().parents if (p / "src/MphRead").exists())
source=(root/'native/rmlui-poc/projectprime_rmlui.cpp').read_text()
out=Path(__file__).resolve().parent/'obj/source-linked';out.mkdir(parents=True,exist_ok=True)
start=source.index('bool TimelineActivationCurrent(')
queue=source[start:source.index('void QueueHomeAction(',start)]
take=source[source.index('PP_EXPORT int pp_rmlui_take_intent('):source.index('PP_EXPORT uint64_t pp_rmlui_document_open(')]
legacy=source[source.index('PP_EXPORT int pp_rmlui_take_action('):source.index('// Actual RmlUi element bounds')]
prefix=r'''
#include <algorithm>
#include <cstring>
#include <deque>
#include <iostream>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <cstdint>
#define PP_EXPORT
constexpr uint32_t PP_RMLUI_PROTOCOL_VERSION=1;
namespace Rml {
struct ElementDocument {uint64_t id;};
struct Element {
 std::string id;ElementDocument* document;bool visible=true,disabled=false,can_focus=true;
 std::string GetId(){return id;} ElementDocument* GetOwnerDocument(){return document;}
 bool IsVisible(bool){return visible;}bool HasAttribute(const char*){return disabled;}bool Focus();
}; }
struct Context {Rml::Element* focus=nullptr;Rml::Element* GetFocusElement(){return focus;}};
Context context;Context* g_context=&context;
bool Rml::Element::Focus(){if(!can_focus)return false;g_context->focus=this;return true;}
struct PrimeIntent {uint32_t size,version=1;uint64_t generation=0,document_id=0,sequence=0;PrimeIntent():size(sizeof(PrimeIntent)){} };
struct QueuedAction {PrimeIntent intent;std::string legacy;std::string activation_source;};
std::deque<QueuedAction> g_actions;uint64_t g_generation=2,g_next_sequence=0;
Rml::ElementDocument document{1},other{2};
struct Document {Rml::ElementDocument* element=&document;std::unordered_map<std::string,bool> booleans;};Document doc;
Document* GetDocument(uint64_t id){return id==1?&doc:nullptr;}uint64_t DocumentId(Rml::ElementDocument* d){return d?d->id:0;}
void DirtyVisual(){}bool TranslateAction(const std::string& action,PrimeIntent&){return !action.empty();}bool OwnerThread(){return true;}
int CopyUtf8(const std::string& value,unsigned char* buffer,int capacity){if(int(value.size())>=capacity)return -1;std::memcpy(buffer,value.c_str(),value.size()+1);return int(value.size());}
'''
suffix=r'''
int main(){int count=0;auto check=[&](bool value,const char* text){if(!value)throw std::runtime_error(text);++count;};
Rml::Element a{"replay_timeline_key_0",&document},b{"replay_timeline_events_0",&document};
context.focus=&b;QueueAction("replay:timeline-activate",1,&a);
check(context.focus==&a&&g_actions.size()==1&&g_actions.front().activation_source==a.id,"a11y Press focuses and binds actual invoked source rather than old focused marker");
PrimeIntent request;request.size=sizeof(PrimeIntent);check(pp_rmlui_take_intent(&request)==1&&request.document_id==1,"same focused native source reaches bounded intent once");
check(pp_rmlui_take_intent(&request)==0,"no duplicate activation remains");
QueueAction("replay:timeline-activate",1,&a);context.focus=&b;
check(pp_rmlui_take_intent(&request)==0&&g_actions.empty(),"click then Tab before dequeue cannot retarget lane seek to new focus");
QueueAction("replay:timeline-activate",1,&a);QueueAction("replay:timeline-activate",1,&b);auto expected=g_actions.back().intent.sequence;
check(pp_rmlui_take_intent(&request)==1&&request.sequence==expected&&g_actions.empty(),"several queued generic activations retain only exact still-focused target");
a.disabled=true;QueueAction("replay:timeline-activate",1,&a);a.disabled=false;a.visible=false;QueueAction("replay:timeline-activate",1,&a);a.visible=true;
check(g_actions.empty(),"disabled or hidden marker cannot queue invocation");
a.document=&other;QueueAction("replay:timeline-activate",1,&a);a.document=&document;check(g_actions.empty(),"foreign document source cannot bind current timeline");
a.id=std::string(100,'a');QueueAction("replay:timeline-activate",1,&a);a.id="replay_timeline_key_0";check(g_actions.empty(),"bounded canonical authored source ids required");
context.focus=nullptr;QueueAction("replay:camera-tools-refresh",1);check(pp_rmlui_take_intent(&request)==1&&context.focus==nullptr,"all old action families keep existing focus-independent semantics");
QueueAction("replay:timeline-activate",1,&a);context.focus=&b;unsigned char bytes[128];check(pp_rmlui_take_action(bytes,128)==0,"legacy dequeue cannot retarget generic timeline activation either");
std::cout<<"PASS "<<count<<" actual staged native queue-source/a11y/focus guard source checks; stub DOM only\n";
}
'''
(out/'check.cpp').write_text(prefix+queue+take+legacy+suffix)
subprocess.run(['/usr/bin/clang++','-std=c++17','-Wall','-Wextra','-Werror',str(out/'check.cpp'),'-o',str(out/'check')],check=True)
subprocess.run([str(out/'check')],check=True)
