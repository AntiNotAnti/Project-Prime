"""Source-linked first-frame launcher ownership and frozen native event checks."""
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import unittest
from xml.sax.saxutils import escape


class StartupOwnershipTests(unittest.TestCase):
    def run_csharp(self, code, links=()):
        dotnet = os.environ.get('DOTNET_EXE') or shutil.which('dotnet')
        self.assertIsNotNone(dotnet, 'Set DOTNET_EXE to the pinned .NET 10 SDK')
        with tempfile.TemporaryDirectory(prefix='prime-startup-ownership-') as temporary:
            scratch = Path(temporary)
            (scratch / 'Program.cs').write_text(code)
            (scratch / 'check.csproj').write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                '<TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>'
                '<NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>'
                + ''.join('<Compile Include="' + escape(str(path)) + '" />' for path in links)
                + '</ItemGroup></Project>')
            result = subprocess.run([dotnet, 'run', '--project', str(scratch / 'check.csproj'),
                                     '-c', 'Release', '--verbosity', 'quiet'],
                                    capture_output=True, text=True, timeout=90)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_actual_after_draw_startup_block_requires_active_owner(self):
        root = Path(__file__).resolve().parents[2]
        source = (root / 'src/MphRead/Mods/Launcher/Gui/Shell.cs').read_text()
        match = re.search(r'internal static void AfterDraw\(RenderWindow window\)\s*\{(.*?)'
                          r'Diagnostics\.LauncherWindowCheck\.AfterDraw\(window\);', source, re.S)
        self.assertIsNotNone(match, 'actual AfterDraw startup block must remain inspectable')
        self.run_csharp(r'''
using System;
using System.Threading;
static class Checks {
 static void Check(bool value,string name){if(!value)throw new Exception(name);}
 static void Main(){
  var owner=new RenderWindow(); var foreign=new RenderWindow();
  Shell._window=owner;
  Shell.Active=false; Shell.AfterDraw(owner); Shell.AfterDraw(foreign);
  Check(Shell.Started==0 && Shell._firstFrameStarted==0 && LifecycleTiming.Frames==0,"inactive standalone draws cannot start/consume startup");
  Shell.Active=true; Shell.AfterDraw(foreign);
  Check(Shell.Started==0 && Shell._firstFrameStarted==0,"foreign/auxiliary window cannot consume active shell startup");
  Shell.AfterDraw(owner); Shell.AfterDraw(owner);
  Check(Shell.Started==1 && Shell._firstFrameStarted==1 && LifecycleTiming.Frames==1,"active owner starts exactly once");
  Shell._firstFrameStarted=0; Shell._window=null; Shell.AfterDraw(owner);
  Check(Shell.Started==1 && Shell._firstFrameStarted==0,"absent owner cannot schedule startup");
  Shell._window=owner; Shell.AfterDraw(owner);
  Check(Shell.Started==2 && LifecycleTiming.Frames==2,"a new owner session can start after explicit reset");
  Console.WriteLine("STARTUP_OWNERSHIP_SOURCE_PASS");
 }
}
sealed class RenderWindow{}
static class LifecycleTiming { internal static int Frames; internal static void FirstFrame()=>Frames++; }
static class Shell {
 internal static bool Active; internal static RenderWindow? _window;
 internal static int _firstFrameStarted,Started;
 static void StartBackgroundStartupWork()=>Started++;
 internal static void AfterDraw(RenderWindow window){ACTUAL_STARTUP}
}
'''.replace('ACTUAL_STARTUP', match.group(1)))

    def test_actual_native_overrides_suppress_and_record_escape_and_other_input(self):
        directory = Path(__file__).resolve().parent
        self.run_csharp(r'''
using System;
using System.Text.Json;
using OpenTK.Windowing.Common;
static class Checks {
 static void Check(bool value,string name){if(!value)throw new Exception(name);}
 static void Main(){
  var window=new CheckWindow(); window.Emit();
  Check(window.BaseCalls==0 && window.CloseRequests==0,"actual native handlers must not forward live Escape/text/mouse into production window");
  var receipt=JsonSerializer.SerializeToElement(window.Receipt());
  Check(receipt.GetProperty("keyDown").GetInt64()==1 && receipt.GetProperty("keyUp").GetInt64()==1,"keys counted");
  Check(receipt.GetProperty("recordedDiscreteEvents").GetInt32()==6 && receipt.GetProperty("mouseMove").GetInt64()==1,"motion counted without consuming discrete provenance");
  Check(receipt.GetProperty("lastKeyDown").GetProperty("Code").GetInt32()==256,"Escape remains observable");
  for(int i=0;i<1000;i++)window.EmitKey(65);
  window.EmitKey(256);
  receipt=JsonSerializer.SerializeToElement(window.Receipt());
  Check(receipt.GetProperty("recordedDiscreteEvents").GetInt32()==128 && receipt.GetProperty("droppedDiscreteObservations").GetInt64()==879,"provenance buffer remains bounded and overflow explicit");
  Check(receipt.GetProperty("lastKeyDown").GetProperty("Code").GetInt32()==256,"last Escape survives bounded detail overflow");
  var recorder=new NativeInputIsolation(); for(int i=0;i<128;i++)recorder.Observe(NativeInputKind.KeyDown,1);
  long start=GC.GetAllocatedBytesForCurrentThread();
  for(int i=0;i<1000;i++){recorder.Observe(NativeInputKind.KeyUp,1);recorder.Observe(NativeInputKind.MouseMove);}
  Check(GC.GetAllocatedBytesForCurrentThread()==start,"warmed value recording and overflow allocate no managed bytes");
  Console.WriteLine("FROZEN_NATIVE_INPUT_SOURCE_PASS");
 }
}
sealed partial class CheckWindow : WindowBase {
 internal object Receipt()=>_nativeInputIsolation.Receipt();
 internal void Emit(){OnKeyDown(new(256));OnKeyUp(new(256));OnTextInput(new(65));OnMouseDown(new(1));OnMouseUp(new(1));OnMouseWheel(new(0,1));OnMouseMove(new());}
 internal void EmitKey(int key)=>OnKeyDown(new(key));
}
class WindowBase {
 internal int BaseCalls,CloseRequests;
 protected virtual void OnKeyDown(KeyboardKeyEventArgs e){BaseCalls++;if(e.Key==256)CloseRequests++;}
 protected virtual void OnKeyUp(KeyboardKeyEventArgs e)=>BaseCalls++;
 protected virtual void OnTextInput(TextInputEventArgs e)=>BaseCalls++;
 protected virtual void OnMouseDown(MouseButtonEventArgs e)=>BaseCalls++;
 protected virtual void OnMouseUp(MouseButtonEventArgs e)=>BaseCalls++;
 protected virtual void OnMouseWheel(MouseWheelEventArgs e)=>BaseCalls++;
 protected virtual void OnMouseMove(MouseMoveEventArgs e)=>BaseCalls++;
}
namespace OpenTK.Windowing.Common {
 readonly record struct KeyboardKeyEventArgs(int Key);
 readonly record struct TextInputEventArgs(int Unicode);
 readonly record struct MouseButtonEventArgs(int Button);
 readonly record struct MouseWheelEventArgs(float OffsetX,float OffsetY);
 readonly record struct MouseMoveEventArgs;
}
''', [directory / 'NativeInputIsolation.cs', directory / 'FrozenNativeInput.cs'])


if __name__ == '__main__':
    unittest.main()
