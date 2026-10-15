#define GL_SILENCE_DEPRECATION
#include <OpenGL/OpenGL.h>
#include <OpenGL/CGLIOSurface.h>
#include <OpenGL/gl3.h>
#include <IOSurface/IOSurface.h>
#include <CoreFoundation/CoreFoundation.h>
#include <stdio.h>
#include <stdlib.h>
static void ck(CGLError e,const char*s){if(e){fprintf(stderr,"%s: %s\n",s,CGLErrorString(e));exit(1);}}
static CGLContextObj context(int profile){CGLPixelFormatAttribute a[]={kCGLPFAOpenGLProfile,profile,kCGLPFAAccelerated,0};CGLPixelFormatObj p;GLint n;CGLContextObj c;ck(CGLChoosePixelFormat(a,&p,&n),"choose");ck(CGLCreateContext(p,NULL,&c),"create");CGLDestroyPixelFormat(p);return c;}
static IOSurfaceRef surface(int w,int h){CFMutableDictionaryRef d=CFDictionaryCreateMutable(NULL,0,&kCFTypeDictionaryKeyCallBacks,&kCFTypeDictionaryValueCallBacks);const CFStringRef keys[]={kIOSurfaceWidth,kIOSurfaceHeight,kIOSurfaceBytesPerElement,kIOSurfaceBytesPerRow,kIOSurfaceAllocSize,kIOSurfacePixelFormat};int values[]={w,h,4,w*4,w*h*4,0x42475241};for(int i=0;i<6;i++){CFNumberRef n=CFNumberCreate(NULL,kCFNumberIntType,&values[i]);CFDictionarySetValue(d,keys[i],n);CFRelease(n);}IOSurfaceRef s=IOSurfaceCreate(d);CFRelease(d);return s;}
static GLuint bind(CGLContextObj c,IOSurfaceRef s,int w,int h){ck(CGLSetCurrentContext(c),"current");GLuint t,f;glGenTextures(1,&t);glBindTexture(GL_TEXTURE_RECTANGLE,t);ck(CGLTexImageIOSurface2D(c,GL_TEXTURE_RECTANGLE,GL_RGBA8,w,h,GL_BGRA,GL_UNSIGNED_INT_8_8_8_8_REV,s,0),"import");glGenFramebuffers(1,&f);glBindFramebuffer(GL_FRAMEBUFFER,f);glFramebufferTexture2D(GL_FRAMEBUFFER,GL_COLOR_ATTACHMENT0,GL_TEXTURE_RECTANGLE,t,0);printf("%s FBO=%x error=%x\n",glGetString(GL_VERSION),glCheckFramebufferStatus(GL_FRAMEBUFFER),glGetError());return f;}
int main(){CGLContextObj l=context(kCGLOGLPVersion_Legacy),c=context(kCGLOGLPVersion_3_2_Core);IOSurfaceRef s=surface(64,64);if(!s)return 2;GLuint lf=bind(l,s,64,64);glViewport(0,0,64,64);glClearColor(.25,.5,.75,1);glClear(GL_COLOR_BUFFER_BIT);glFinish();GLuint cf=bind(c,s,64,64);unsigned char p[4]={0};glReadPixels(32,32,1,1,GL_RGBA,GL_UNSIGNED_BYTE,p);printf("pixel=%d,%d,%d,%d error=%x\n",p[0],p[1],p[2],p[3],glGetError());return !(p[0]>=63&&p[0]<=65&&p[1]>=127&&p[1]<=129&&p[2]>=190&&p[2]<=192&&p[3]==255);}
