using Avalonia.Input;
using ProjectPrime.Studio.Settings;

namespace ProjectPrime.Studio.Shell;

/// <summary>Persistent command bindings; an empty override disables a binding.</summary>
public static class StudioHotkeys
{
    private static string Primary => OperatingSystem.IsMacOS() ? "Meta" : "Ctrl";
    public static IReadOnlyDictionary<StudioCommand,string> Defaults => new Dictionary<StudioCommand,string>
    {
        [StudioCommand.OpenMap]=Primary+"+O", [StudioCommand.Save]=Primary+"+S",
        [StudioCommand.SaveAs]=Primary+"+Shift+S", [StudioCommand.Close]=Primary+"+W",
        [StudioCommand.Undo]=Primary+"+Z", [StudioCommand.Redo]=Primary+"+Shift+Z",
        [StudioCommand.GlobalSearch]=Primary+"+K", [StudioCommand.ConfigureHotkeys]=Primary+"+Alt+K",
        [StudioCommand.MapPlaytest]=Primary+"+Shift+P", [StudioCommand.ReplayPlayPause]="Space"
    };
    public static string Binding(StudioSettings settings, StudioCommand command)
        => settings.CustomHotkeys.TryGetValue(command.ToString(),out string? value) ? value
            : Defaults.TryGetValue(command,out var defaultValue) ? defaultValue : "";
    public static StudioCommand? Match(StudioSettings settings, KeyEventArgs key, IEnumerable<StudioCommand> available)
    {
        if (key.Handled) return null;
        // Plain text entry owns Space and other unmodified keys.
        bool editing = key.Source is Avalonia.Controls.TextBox;
        foreach (StudioCommand command in available)
        {
            if (editing && command is StudioCommand.Undo or StudioCommand.Redo) continue;
            string binding=Binding(settings,command);
            if (string.IsNullOrWhiteSpace(binding)) continue;
            try
            {
                var gesture=KeyGesture.Parse(binding);
                if (editing && gesture.KeyModifiers == KeyModifiers.None) continue;
                if (gesture.Matches(key)) return command;
            }
            catch (FormatException) { }
            catch (ArgumentException) { }
        }
        return null;
    }
    public static string? Validate(IReadOnlyDictionary<string,string> bindings)
    {
        var used=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (StudioCommand command in Enum.GetValues<StudioCommand>())
        {
            string value=bindings.TryGetValue(command.ToString(),out var custom) ? custom
                : Defaults.TryGetValue(command,out var original) ? original : "";
            if (string.IsNullOrWhiteSpace(value)) continue;
            try
            {
                var gesture=KeyGesture.Parse(value);
                string key=gesture.ToString();
                if (used.TryGetValue(key,out string? other)) return $"{command} and {other} use the same shortcut.";
                used.Add(key,command.ToString());
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            { return $"Invalid shortcut for {command}: {value}"; }
        }
        return null;
    }
}
