using System;
namespace MphRead.Mods
{
 public static class ClientInputState
 {
  public static Func<bool>? ReadPauseOpen { get; set; }
  public static bool PauseOpen => ReadPauseOpen?.Invoke() ?? false;
  /// <summary>
  /// Whether the game window/activity currently owns input focus. Platform
  /// adapters update this before dispatching their per-frame input snapshot;
  /// shared look capture treats an unfocused surface as ineligible.
  /// </summary>
  public static volatile bool WindowFocused = true;
 }
}
