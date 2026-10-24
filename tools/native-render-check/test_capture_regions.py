"""Execute current readback/call-site code with hostile pack state; no native GL."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]


def method(text, marker):
    start = text.index(marker)
    opening = text.index('{', start)
    depth = 0
    for end in range(opening, len(text)):
        if text[end] == '{':
            depth += 1
        elif text[end] == '}':
            depth -= 1
            if depth == 0:
                return text[start:end + 1]
    raise AssertionError('unterminated method: ' + marker)


@unittest.skipUnless(os.name == 'nt', 'Windows-only execution')
class CaptureRegionTests(unittest.TestCase):
    def test_real_region_thumbnail_and_calibration_restore_hostile_state(self):
        dotnet = os.environ.get('DOTNET_EXE') or shutil.which('dotnet') or shutil.which('dotnet.exe')
        if not dotnet:
            self.skipTest('native Windows dotnet SDK unavailable')
        screen = (ROOT / 'src/MphRead/Mods/ScreenCapture.cs').read_text()
        renderer = (ROOT / 'src/MphRead/Renderer.cs').read_text()
        preview = (ROOT / 'src/MphRead/Mods/Render/PreviewPass.cs').read_text()
        thumbnail = method(screen, 'internal static void QueueCosmeticThumbnail(')
        calibration = method(renderer, 'private void CalibrateInk(')
        self.assertNotIn('GL.ReadPixels', thumbnail + calibration)
        self.assertNotIn('GL.PixelStore', thumbnail + calibration)
        self.assertIn('GL.GetInteger(GetPName.DrawFramebufferBinding), target.X, target.Y, "cosmetic-preview"', preview)
        code = r'''
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;
class Program
{
 static int checks;
 static void Check(bool value,string why) { checks++; if(!value)throw new Exception(why); }
 static void Restored(string why) => Check(GL.Framebuffer==7 && GL.ReadBuffers[7]==19
  && GL.Alignment==8 && GL.RowLength==17 && GL.SkipRows==2 && GL.SkipPixels==3 && GL.PackBuffer==99,why);
 static void Main()
 {
  var source=new CaptureSource(11,80,60,"fixture");
  byte[] rgb=CaptureReadback.ReadRegion(source,7,9,3,2);
  Check(rgb.Length==18 && rgb[0]==7 && rgb[1]==9 && rgb[2]==123 && rgb[9]==7 && rgb[10]==10
   && rgb[^3]==9 && rgb[^2]==10,"RGB crop coordinates, channels and bottom-up rows preserved");
  Restored("RGB success restores PBO/rows/skips/alignment/read FBO and read buffer");
  byte[] rgba=CaptureReadback.ReadRgbaRegion(source,7,9,3,2);
  Check(rgba.Length==24&&rgba[0]==7&&rgba[1]==9&&rgba[3]==255&&rgba[^3]==10,"RGBA preserves alpha and crop");
  Restored("RGBA success restores all caller state");
  foreach(int failure in new[]{1,2,3})
  {
   GL.Failure=failure;bool rejected=false;
   try{CaptureReadback.ReadRegion(source,1,2,3,4);}catch(InvalidOperationException){rejected=true;}
   Check(rejected,"incomplete framebuffer/read exception/GL error fails");Restored("failure restores caller state");
  }
  GL.Failure=0;
  foreach(var region in new[]{(-1,0,1,1),(0,-1,1,1),(79,0,2,1),(0,59,1,2),(int.MaxValue,0,2,1),(0,0,0,1)})
  {
   int before=GL.Calls;bool rejected=false;
   try{CaptureReadback.ReadRegion(source,region.Item1,region.Item2,region.Item3,region.Item4);}catch(ArgumentOutOfRangeException){rejected=true;}
   Check(rejected&&GL.Calls==before,"invalid/overflowing region rejected before any GL operation");
  }
  bool budget=false;try{CaptureReadback.Read(new CaptureSource(11,8192,8192,"budget"));}
  catch(ArgumentOutOfRangeException){budget=true;}Check(budget,"full-read budget still enforced");
  string directory=Path.Combine(Path.GetTempPath(),"prime-region-thumb-"+Guid.NewGuid().ToString("N"));
  try
  {
   byte[]? delivered=null;int deliveredWidth=0,deliveredHeight=0;using var ready=new ManualResetEventSlim();
   MphRead.Mods.ScreenProbe.PngWriter=(pixels,width,height,_)=>{delivered=pixels;deliveredWidth=width;deliveredHeight=height;ready.Set();};
   MphRead.Mods.ScreenProbe.QueueCosmeticThumbnail(source,7,9,3,2,Path.Combine(directory,"fixture.png"));
   Restored("actual thumbnail call restores state before its worker completes");
   Check(ready.Wait(5000)&&SpinWait.SpinUntil(()=>MphRead.Mods.ScreenProbe.Slots==1,5000),"actual thumbnail worker releases bounded slot");
   Check(deliveredWidth==3&&deliveredHeight==2&&delivered!=null&&delivered.AsSpan().SequenceEqual(rgb),"thumbnail writer receives unchanged RGB bytes/dimensions/orientation");
   int before=GL.Calls;MphRead.Mods.ScreenProbe.QueueCosmeticThumbnail(source,-1,9,3,2,Path.Combine(directory,"clipped.png"));
   Check(GL.Calls==before&&MphRead.Mods.ScreenProbe.Slots==1,"offscreen thumbnail rectangle is skipped without taking down preview");
   GL.Failure=2;bool failed=false;
   try{MphRead.Mods.ScreenProbe.QueueCosmeticThumbnail(source,7,9,3,2,Path.Combine(directory,"fail.png"));}catch(InvalidOperationException){failed=true;}
   Check(failed&&MphRead.Mods.ScreenProbe.Slots==1,"thumbnail read failure releases slot without worker");Restored("thumbnail failure restores caller GL state");
  }
  finally{GL.Failure=0;MphRead.Mods.ScreenProbe.PngWriter=null;if(Directory.Exists(directory))Directory.Delete(directory,true);}
  var calibration=new MphRead.CalibrationProbe();GL.CalibrationValue=64;
  calibration.Run(64,48);
  float expected=MathF.Pow(2,(64/255f-1)*32);
  Check(GL.Last==(11,8,0,48,48),"actual calibration reads its centered region from scene framebuffer");
  Check(calibration.Quantum==expected&&calibration.Probes==1,"actual calibration red-channel median/threshold behavior preserved");
  Restored("actual calibration restores hostile pack/read state");
  calibration=new();GL.Failure=2;calibration.Run(64,48);
  Check(calibration.Quantum==1e-9f,"failed calibration retains fixed threshold");Restored("failed calibration restores hostile pack state");
  GL.Failure=0;GL.CalibrationValue=0;calibration=new();calibration.Run(64,48);
  Check(calibration.Retry&&calibration.Quantum==1e-9f,"empty calibration retries next scene without changing threshold");
  calibration=new();calibration.Run(31,31);Check(calibration.Probes==0,"too-small calibration does not draw or read");
  Console.WriteLine("CAPTURE_REGIONS_SOURCE_PASS checks="+checks+"; actual helper/thumbnail/calibration code; native GPU NOT_RUN");
 }
}
namespace MphRead.Mods
{
 static class ScreenProbe
 {
  private static readonly SemaphoreSlim ThumbnailSlots=new(1);private static readonly object PngGate=new();
  internal static int Slots=>ThumbnailSlots.CurrentCount;
  public static Action<byte[],int,int,string>? PngWriter;
  ACTUAL_THUMBNAIL
 }
 static class PngEncoder { internal static void Write<T>(byte[] p,int w,int h,ReFuel.Stb.StbiImageFormat f,Stream s){} }
}
namespace ReFuel.Stb { enum StbiImageFormat {Rgb} static class StbImage {public static bool FlipVerticallyOnSave{get;set;}} }
namespace MphRead
{
 readonly record struct Vector2i(int X,int Y);
 class CalibrationProbe
 {
  int _frameBuffer=11;bool _calibrateInk;float _depthQuantum=1e-9f,_claimedQuantum=1e-9f;
  internal float Quantum=>_depthQuantum;internal bool Retry=>_calibrateInk;internal int Probes;
  internal void Run(int width,int height)=>CalibrateInk(new(width,height));
  void DrawCelQuad(Vector2i target,bool probe){if(probe)Probes++;}
  ACTUAL_CALIBRATION
 }
}
namespace OpenTK.Graphics.OpenGL
{
 enum GetPName {ReadFramebufferBinding,ReadBuffer,PackAlignment,PackRowLength,PackSkipRows,PackSkipPixels,PixelPackBufferBinding}
 enum FramebufferTarget {ReadFramebuffer} enum ReadBufferMode {Back,ColorAttachment0}
 enum FramebufferErrorCode {FramebufferComplete,FramebufferIncompleteAttachment}
 enum PixelStoreParameter {PackAlignment,PackRowLength,PackSkipRows,PackSkipPixels}
 enum PixelFormat {Rgba} enum PixelType {UnsignedByte} enum BufferTarget {PixelPackBuffer}
 enum ErrorCode {NoError,InvalidOperation}
 static class GL
 {
  internal static int Framebuffer=7,Alignment=8,RowLength=17,SkipRows=2,SkipPixels=3,PackBuffer=99,Failure,Calls;
  internal static int CalibrationValue=-1;
  internal static (int,int,int,int,int) Last;
  internal static readonly Dictionary<int,int> ReadBuffers=new(){{7,19},{11,23}};
  internal static int GetInteger(GetPName name){Calls++;return name switch{
   GetPName.ReadFramebufferBinding=>Framebuffer,GetPName.ReadBuffer=>ReadBuffers[Framebuffer],GetPName.PackAlignment=>Alignment,
   GetPName.PackRowLength=>RowLength,GetPName.PackSkipRows=>SkipRows,GetPName.PackSkipPixels=>SkipPixels,_=>PackBuffer};}
  internal static void BindFramebuffer(FramebufferTarget t,int value){Calls++;Framebuffer=value;}
  internal static void ReadBuffer(ReadBufferMode value){Calls++;ReadBuffers[Framebuffer]=(int)value;}
  internal static FramebufferErrorCode CheckFramebufferStatus(FramebufferTarget t){Calls++;return Failure==1?FramebufferErrorCode.FramebufferIncompleteAttachment:FramebufferErrorCode.FramebufferComplete;}
  internal static void BindBuffer(BufferTarget t,int value){Calls++;PackBuffer=value;}
  internal static void PixelStore(PixelStoreParameter name,int value){Calls++;switch(name){case PixelStoreParameter.PackAlignment:Alignment=value;break;case PixelStoreParameter.PackRowLength:RowLength=value;break;case PixelStoreParameter.PackSkipRows:SkipRows=value;break;default:SkipPixels=value;break;}}
  internal static void ReadPixels(int x,int y,int width,int height,PixelFormat format,PixelType type,byte[] pixels)
  {
   Calls++;Last=(Framebuffer,x,y,width,height);
   if(PackBuffer!=0||Alignment!=1||RowLength!=0||SkipRows!=0||SkipPixels!=0)throw new Exception("unsafe managed pack state");
   if(Failure==2)throw new InvalidOperationException("injected read failure");
   for(int i=0;i<width*height;i++){pixels[i*4]=(byte)(CalibrationValue>=0?CalibrationValue:x+i%width);pixels[i*4+1]=(byte)(y+i/width);pixels[i*4+2]=123;pixels[i*4+3]=255;}
  }
  internal static ErrorCode GetError(){Calls++;return Failure==3?ErrorCode.InvalidOperation:ErrorCode.NoError;}
 }
}
'''.replace('ACTUAL_THUMBNAIL', thumbnail).replace('ACTUAL_CALIBRATION', calibration)
        code = code.replace('using System.Threading;', 'using System.Threading;\nusing ReFuel.Stb;')
        with tempfile.TemporaryDirectory(prefix='prime-capture-regions-') as temporary:
            scratch = Path(temporary)
            (scratch / 'Program.cs').write_text(code)
            helper = escape(str(ROOT / 'src/MphRead/Mods/Render/CaptureSource.cs'))
            (scratch / 'check.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
                '<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>'
                '<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit><UseSharedCompilation>false</UseSharedCompilation>'
                '</PropertyGroup><ItemGroup><Compile Include="' + helper + '" /></ItemGroup></Project>')
            env = dict(os.environ, MSBuildEnableWorkloadResolver='false', DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true')
            result = subprocess.run([dotnet, 'run', '--project', str(scratch / 'check.csproj'), '-c', 'Release', '--verbosity', 'quiet'],
                                    cwd=scratch, env=env, capture_output=True, text=True, timeout=90)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('CAPTURE_REGIONS_SOURCE_PASS', result.stdout)
            self.assertNotRegex(result.stdout + result.stderr, r'warning (CS|NU|MSB)')


if __name__ == '__main__':
    unittest.main(verbosity=2)
