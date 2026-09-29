// Minimal offline, volatile-save host for the GPL-3.0 melonDS core.
// No audio device, LAN, real-time pacing, or writes to the user's cartridge save.
#include "Platform.h"
#include <cstdio>
#include <cstdarg>
#include <filesystem>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <dlfcn.h>
#include <stdexcept>
namespace melonDS::Platform {
struct FileHandle { FILE* f; };
void Init(int,char**) {} void DeInit() {}
void SignalStop(StopReason r) { throw std::runtime_error("emulator stopped: " + std::to_string(r)); }
int InstanceID() { return 0; } std::string InstanceFileSuffix() { return ""; }
FileHandle* OpenFile(const std::string& p,FileMode m) {
 if ((m & NoCreate) && !std::filesystem::exists(p)) return nullptr;
 const char* mode = !(m & Write) ? "rb" : (m & Preserve) ? "r+b" : (m & Read) ? "w+b" : "wb";
 FILE* f=fopen(p.c_str(),mode);
 if(!f && (m&Write) && (m&Preserve) && !(m&NoCreate)) f=fopen(p.c_str(),"w+b");
 return f ? new FileHandle{f}:nullptr;
}
FileHandle* OpenLocalFile(const std::string& p,FileMode m){ return OpenFile(p,m); }
bool FileExists(const std::string&p){return std::filesystem::exists(p);} bool LocalFileExists(const std::string&p){return FileExists(p);}
bool CloseFile(FileHandle*f){int r=fclose(f->f);delete f;return r==0;}
bool IsEndOfFile(FileHandle*f){return feof(f->f);} bool FileReadLine(char*s,int n,FileHandle*f){return fgets(s,n,f->f)!=nullptr;}
bool FileSeek(FileHandle*f,s64 n,FileSeekOrigin o){return fseeko(f->f,n,o==FileSeekOrigin::Start?SEEK_SET:o==FileSeekOrigin::Current?SEEK_CUR:SEEK_END)==0;}
void FileRewind(FileHandle*f){rewind(f->f);} u64 FileRead(void*d,u64 s,u64 n,FileHandle*f){return fread(d,s,n,f->f);}
bool FileFlush(FileHandle*f){return fflush(f->f)==0;} u64 FileWrite(const void*d,u64 s,u64 n,FileHandle*f){return fwrite(d,s,n,f->f);}
u64 FileWriteFormatted(FileHandle*f,const char*fmt,...){va_list a;va_start(a,fmt);int r=vfprintf(f->f,fmt,a);va_end(a);return r<0?0:r;}
u64 FileLength(FileHandle*f){auto p=ftello(f->f);fseeko(f->f,0,SEEK_END);auto n=ftello(f->f);fseeko(f->f,p,SEEK_SET);return n;}
void Log(LogLevel,const char*fmt,...){va_list a;va_start(a,fmt);vfprintf(stderr,fmt,a);va_end(a);}
struct Thread{std::thread t;}; Thread* Thread_Create(std::function<void()>f){return new Thread{std::thread(f)};}
void Thread_Wait(Thread*t){if(t->t.joinable())t->t.join();} void Thread_Free(Thread*t){Thread_Wait(t);delete t;}
struct Semaphore{std::mutex m;std::condition_variable cv;int n=0;};
Semaphore* Semaphore_Create(){return new Semaphore;} void Semaphore_Free(Semaphore*s){delete s;}
void Semaphore_Reset(Semaphore*s){std::lock_guard<std::mutex> l(s->m);s->n=0;}
void Semaphore_Wait(Semaphore*s){std::unique_lock<std::mutex> l(s->m);s->cv.wait(l,[&]{return s->n>0;});--s->n;}
void Semaphore_Post(Semaphore*s,int n){std::lock_guard<std::mutex>l(s->m);s->n+=n;s->cv.notify_all();}
struct Mutex{std::mutex m;}; Mutex* Mutex_Create(){return new Mutex;}void Mutex_Free(Mutex*m){delete m;}
void Mutex_Lock(Mutex*m){m->m.lock();} void Mutex_Unlock(Mutex*m){m->m.unlock();} bool Mutex_TryLock(Mutex*m){return m->m.try_lock();}
void Sleep(u64 us){std::this_thread::sleep_for(std::chrono::microseconds(us));}
void WriteNDSSave(const u8*,u32,u32,u32){} void WriteGBASave(const u8*,u32,u32,u32){}
void WriteFirmware(const Firmware&,u32,u32){} void WriteDateTime(int,int,int,int,int,int){}
bool MP_Init(){return true;}void MP_DeInit(){}void MP_Begin(){}void MP_End(){}
int MP_SendPacket(u8*,int,u64){return 0;}int MP_RecvPacket(u8*,u64*){return 0;}
int MP_SendCmd(u8*,int,u64){return 0;}int MP_SendReply(u8*,int,u64,u16){return 0;}int MP_SendAck(u8*,int,u64){return 0;}
int MP_RecvHostPacket(u8*,u64*){return 0;}u16 MP_RecvReplies(u8*,u64,u16){return 0;}
bool LAN_Init(){return false;}void LAN_DeInit(){}int LAN_SendPacket(u8*,int){return 0;}int LAN_RecvPacket(u8*){return 0;}
void Camera_Start(int){}void Camera_Stop(int){}void Camera_CaptureFrame(int,u32*,int,int,bool){}
struct DynamicLibrary{void*p;};DynamicLibrary* DynamicLibrary_Load(const char*p){void*h=dlopen(p,RTLD_NOW);return h?new DynamicLibrary{h}:nullptr;}
void DynamicLibrary_Unload(DynamicLibrary*l){dlclose(l->p);delete l;}void*DynamicLibrary_LoadFunction(DynamicLibrary*l,const char*n){return dlsym(l->p,n);}
}
