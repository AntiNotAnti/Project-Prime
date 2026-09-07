using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;
using ReFuel.Stb;
namespace MphRead.Mods
{
 internal static class DesktopPresentationServices
 {
  [ModuleInitializer]
  internal static void Initialize()
  {
   MapImageDecoding.Decoder = Imaging.StbImageDecoder.Decode;
   ScreenCapture.PngWriter = WritePng;
   ThumbnailHost.Current = new DesktopThumbnailHost();
   ClientInputState.ReadPauseOpen = () => PauseMenu.Open;
   Network.NetHostSession.Host = new Network.DesktopNetHostSession();
  }
  private static void WritePng(byte[] pixels, int width, int height, string path)
  {
   using FileStream stream = File.Create(path);
   StbImage.FlipVerticallyOnSave = true;
   StbImage.WritePng<byte>(pixels, width, height, StbiImageFormat.Rgb, stream);
  }
 }
 internal sealed class DesktopThumbnailHost : IThumbnailHost
 {
  public Task<int> RenderAsync(IReadOnlyList<string> rooms, Action<string> report)
   => ThumbnailBatch.CanRun ? Task.Run(() => ThumbnailBatch.Run(rooms, ThumbnailBatch.DefaultParallelism,
      ThumbnailGenerator.ThumbnailWidth, ThumbnailGenerator.ThumbnailHeight, report)) : Task.FromResult(0);
 }
}
