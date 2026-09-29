using System.Text.RegularExpressions;
namespace MphRead.Mods.Training;
public static class TrainingLabels
{
    public static string Display(string value) => value switch
    {
        "KeyboardMouse" => "Mouse + Keyboard", "Gamepad" => "Controller",
        _ => Regex.Replace(value, "([a-z])([A-Z])", "$1 $2")
    };
}
