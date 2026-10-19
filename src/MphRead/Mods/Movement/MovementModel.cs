using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.Movement;

// Protocol 59 fixes Momentum's bit to the version-one profile. Changing its
// simulation contract requires a reviewed protocol/replay compatibility change.
public enum MovementModel : byte { Native = 0, Momentum = 1 }

public static class MovementModelRules
{
    public static bool IsDefined(MovementModel model) => model is MovementModel.Native or MovementModel.Momentum;

    public static bool TryParse(string? value, out MovementModel model)
    {
        model = MovementModel.Native;
        if (String.IsNullOrEmpty(value) || String.Equals(value, "Native", StringComparison.OrdinalIgnoreCase)) return true;
        if (!String.Equals(value, "Momentum", StringComparison.OrdinalIgnoreCase)) return false;
        model = MovementModel.Momentum;
        return true;
    }

    public static MatchModifierFlags Modifier(MovementModel model) => model switch
    {
        MovementModel.Native => MatchModifierFlags.None,
        MovementModel.Momentum => MatchModifierFlags.MomentumMovement,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown movement model.")
    };

    public static MovementModel FromRules(MatchModifierFlags rules) =>
        (rules & MatchModifierFlags.MomentumMovement) != 0 ? MovementModel.Momentum : MovementModel.Native;
}
