#include "probe.h"
#include "ARM.h"
#include <cstring>
#include <array>
#include <map>
#include <sstream>
#include <vector>
#include <stdexcept>
namespace melonDS {
namespace {
NDS* host = nullptr;
u32 player = 0;
using Vec = std::array<s32,3>;
struct Contact { std::array<s32,4> plane; s32 depth; Vec before; Vec push; bool finished=false; };
std::map<std::string,Vec> stages;
std::vector<Contact> contacts;
u32 peek(u32 address) {
 auto& cpu=host->ARM9;u32 value;
 if((address & cpu.DTCMMask)==cpu.DTCMBase){std::memcpy(&value,cpu.DTCM+(address & (DTCMPhysicalSize-1)),4);return value;}
 if(address<cpu.ITCMSize){std::memcpy(&value,cpu.ITCM+(address & (ITCMPhysicalSize-1)),4);return value;}
 return host->ARM9Read32(address);
}
Vec vec(u32 address) { return {(s32)peek(address),(s32)peek(address+4),(s32)peek(address+8)}; }
void jsonVec(std::ostringstream& out, const Vec& v) { out<<"{\"x\":"<<v[0]/4096.0<<",\"y\":"<<v[1]/4096.0<<",\"z\":"<<v[2]/4096.0<<"}"; }
}
void PrimeProbeBegin(NDS& nds,u32 address) {
 // These ARM instructions anchor the ROM-specific observation points. Fail
 // closed if an overlay, different revision, or patched game moves them.
 for(auto entry: std::array<std::pair<u32,u32>,4>{{{0x02020c9c,0xe92d43f0},{0x020213dc,0xe59404c4},{0x02021558,0xe1d40dba},{0x0201fd28,0xe3e00004}}})
  if(nds.ARM9Read32(entry.first)!=entry.second)throw std::runtime_error("native probe instruction signature differs");
 host=&nds;player=address;stages.clear();contacts.clear();
}
void PrimeNativeProbe(u32 pc,const u32*r) {
 if(!host)return;
 if(pc==0x02020c9c && r[0]==player)stages["preMovement"]=vec(player+0x34);
 if(r[4]==player){
  if(pc==0x02020cd0)stages["afterAcceleration"]=vec(player+0x34);
  if(pc==0x020213dc)stages["afterDamping"]=vec(player+0x34);
  if(pc==0x02021558)stages["afterGravity"]=vec(player+0x34);
  if(pc==0x020215ac)stages["afterIntegration"]=vec(player+0x1c);
  if(pc==0x020218d0)stages["afterCollision"]=vec(player+0x34);
 }
 if(pc==0x0201fd28 && r[10]==player){
  Contact c{};for(int i=0;i<4;i++)c.plane[i]=(s32)peek(r[7]+4*i);
  c.depth=(s32)r[6];c.before=vec(player+0x1c);contacts.push_back(c);
 }
 if(pc==0x02020c58 && r[10]==player && !contacts.empty() && !contacts.back().finished){
  auto after=vec(player+0x1c);auto& c=contacts.back();for(int i=0;i<3;i++)c.push[i]=after[i]-c.before[i];c.finished=true;
 }
}
std::string PrimeProbeEnd(){
 std::ostringstream out;out.precision(12);out<<"{\"sampleHz\":30";
 for(const auto& entry:stages){out<<",\""<<entry.first<<"\":";jsonVec(out,entry.second);}
 out<<",\"contacts\":[";bool first=true;
 for(const auto& c:contacts){if(!c.finished)throw std::runtime_error("incomplete native collision probe");if(!first)out<<",";first=false;
  out<<"{\"plane\":{\"x\":"<<c.plane[0]/4096.0<<",\"y\":"<<c.plane[1]/4096.0<<",\"z\":"<<c.plane[2]/4096.0<<",\"w\":"<<c.plane[3]/4096.0
   <<"},\"penetrationDepth\":"<<c.depth/4096.0<<",\"pushout\":";jsonVec(out,c.push);out<<"}";
 }
 out<<"]}";host=nullptr;return out.str();
}
}
