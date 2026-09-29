// Offline command bridge. All protocol responses go to stdout, core logs to stderr.
#include "NDS.h"
#include "GPU3D_Soft.h"
#include "Savestate.h"
#include "probe.h"
#include <fstream>
#include <iostream>
#include <sstream>
#include <vector>
#include <iomanip>
using namespace melonDS;
static std::vector<u8> readFile(const std::string&p){std::ifstream f(p,std::ios::binary);if(!f)throw std::runtime_error("cannot open "+p);return {std::istreambuf_iterator<char>(f),{}};}
static void writeFile(const std::string&p,const void*d,size_t n){if(std::ifstream(p).good())throw std::runtime_error("output exists: "+p);std::ofstream f(p,std::ios::binary);f.write((const char*)d,n);if(!f)throw std::runtime_error("write failed");}
int main(int argc,char**argv){try{
 if(argc!=2)throw std::runtime_error("usage: prime-native ROM.nds");
 auto rom=readFile(argv[1]); if(rom.size()<512)throw std::runtime_error("short ROM");
 auto nds=std::make_unique<NDS>();NDS::Current=nds.get();
 nds->SetNDSCart(NDSCart::ParseROM(rom.data(),rom.size()));
 if(!nds->CartInserted())throw std::runtime_error("invalid ROM");
 nds->SetRenderer3D(std::make_unique<SoftRenderer>(false));
 nds->Reset();nds->SetupDirectBoot(argv[1]);nds->Start();nds->SetKeyMask(0xfff);
 std::cout<<"READY "<<std::string((char*)rom.data()+12,4)<<" "<<(int)rom[30]<<std::endl;
 std::string line;
 while(std::getline(std::cin,line)) {try{
  std::istringstream s(line);std::string cmd;s>>cmd;
  if(cmd=="quit")break;
  if(cmd=="step"){unsigned count,mask;s>>count>>std::hex>>mask;if(!s||count>18000)throw std::runtime_error("bad step");nds->SetKeyMask(mask);for(unsigned i=0;i<count;i++)nds->RunFrame();}
  else if(cmd=="probe"){u32 addr;s>>std::hex>>addr;if(!s||addr!=0x020daf94)throw std::runtime_error("unsupported probe player");PrimeProbeBegin(*nds,addr);}
  else if(cmd=="events"){std::cout<<"JSON "<<PrimeProbeEnd()<<std::endl;continue;}
  else if(cmd=="touch"){int x,y;s>>x>>y;if(!s||x<0||x>255||y<0||y>191)throw std::runtime_error("bad touch");nds->TouchScreen(x,y);}
  else if(cmd=="release")nds->ReleaseScreen();
  else if(cmd=="read"){u32 addr,count;s>>std::hex>>addr>>count;if(!s||count>0x400000||addr<0x02000000||(u64)addr+count>0x02400000)throw std::runtime_error("bad read");std::cout<<"DATA ";for(u32 i=0;i<count;i++)std::cout<<std::hex<<std::setw(2)<<std::setfill('0')<<(unsigned)nds->ARM9Read8(addr+i);std::cout<<std::dec<<std::endl;continue;}
  else if(cmd=="write32"){u32 addr,v;s>>std::hex>>addr>>v;if(!s||addr<0x02000000||addr>0x023ffffc||addr%4)throw std::runtime_error("bad write");nds->ARM9Write32(addr,v);}
  else if(cmd=="ram"){std::string p;s>>std::quoted(p);writeFile(p,nds->MainRAM,0x400000);}
  else if(cmd=="save"){std::string p;s>>std::quoted(p);Savestate state;nds->DoSavestate(&state);state.Finish();if(state.Error)throw std::runtime_error("save failed");writeFile(p,state.Buffer(),state.Length());}
  else if(cmd=="load"){std::string p;s>>std::quoted(p);auto b=readFile(p);Savestate state(b.data(),b.size(),false);if(state.Error||!nds->DoSavestate(&state))throw std::runtime_error("load failed");}
  else if(cmd=="screen"){std::string p;s>>std::quoted(p);std::ostringstream ppm;ppm<<"P6\n256 384\n255\n";for(int screen=0;screen<2;screen++)for(int i=0;i<256*192;i++){u32 v=nds->GPU.Framebuffer[nds->GPU.FrontBuffer][screen][i];ppm.put((v>>16)&255);ppm.put((v>>8)&255);ppm.put(v&255);}auto b=ppm.str();writeFile(p,b.data(),b.size());}
  else throw std::runtime_error("unknown command");
  std::cout<<"OK"<<std::endl;
 }catch(const std::exception&e){std::cout<<"ERROR "<<e.what()<<std::endl;}}
 return 0;
}catch(const std::exception&e){std::cerr<<e.what()<<std::endl;return 1;}}
