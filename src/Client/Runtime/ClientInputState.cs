using System;
namespace MphRead.Mods
{
 public static class ClientInputState
 {
  public static Func<bool>? ReadPauseOpen { get; set; }
  public static bool PauseOpen => ReadPauseOpen?.Invoke() ?? false;
 }
}
