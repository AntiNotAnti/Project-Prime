using System;
using MphRead.Mods.Network;

namespace MphRead.Mods.Movement;

// Live protocol 60 adds Quake3; protocol 59's Momentum bit remains profile v1.
public enum MovementModel : byte { Native = 0, Momentum = 1, Quake3 = 2, Traversal = 3 }

public static class MovementModelRules
{
    public const uint KnownModifierMask = 524287;
    public static bool IsDefined(MovementModel model) => model is MovementModel.Native or MovementModel.Momentum or MovementModel.Quake3 or MovementModel.Traversal;
    public static bool UsesMomentumSolver(MovementModel model) => model is MovementModel.Momentum or MovementModel.Quake3 or MovementModel.Traversal;
    public static int ProfileVersion(MovementModel model) => model switch
    {
        MovementModel.Native => 0,
        MovementModel.Momentum => MomentumProfileV1.MomentumProfileVersion,
        MovementModel.Quake3 => Quake3MovementProfileV2.ProfileVersion,
        MovementModel.Traversal => TraversalMovementProfileV2.ProfileVersion,
        _ => throw new ArgumentOutOfRangeException(nameof(model))
    };
    public static MovementModel Next(MovementModel model) => model switch
    {
        MovementModel.Native => MovementModel.Momentum,
        MovementModel.Momentum => MovementModel.Quake3,
        MovementModel.Quake3 => MovementModel.Traversal,
        MovementModel.Traversal => MovementModel.Native,
        _ => throw new ArgumentOutOfRangeException(nameof(model))
    };
    public static string DisplayName(MovementModel model) => model switch
    {
        MovementModel.Native => "Classic",
        MovementModel.Momentum => "Momentum",
        MovementModel.Quake3 => "Quake III",
        MovementModel.Traversal => "Traversal",
        _ => "Unsupported"
    };
    public static bool TryParse(string? value, out MovementModel model)
    {
        model = MovementModel.Native;
        if (String.IsNullOrEmpty(value) || String.Equals(value, "Native", StringComparison.OrdinalIgnoreCase)
            || String.Equals(value, "Classic", StringComparison.OrdinalIgnoreCase)) return true;
        if (String.Equals(value, "Momentum", StringComparison.OrdinalIgnoreCase)) { model = MovementModel.Momentum; return true; }
        if (String.Equals(value, "Quake3", StringComparison.OrdinalIgnoreCase)
            || String.Equals(value, "Quake III", StringComparison.OrdinalIgnoreCase)) { model = MovementModel.Quake3; return true; }
        if (String.Equals(value, "Traversal", StringComparison.OrdinalIgnoreCase)) { model = MovementModel.Traversal; return true; }
        return false;
    }
    public static MatchModifierFlags Modifier(MovementModel model) => model switch
    {
        MovementModel.Native => MatchModifierFlags.None,
        MovementModel.Momentum => MatchModifierFlags.MomentumMovement,
        MovementModel.Quake3 => MatchModifierFlags.Quake3Movement,
        MovementModel.Traversal => MatchModifierFlags.TraversalMovement,
        _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown movement model.")
    };
    public static bool ValidRules(MatchModifierFlags rules)
    {
        uint movement = (uint)(rules & (MatchModifierFlags.MomentumMovement
            | MatchModifierFlags.Quake3Movement | MatchModifierFlags.TraversalMovement));
        return ((uint)rules & ~KnownModifierMask) == 0
            && (movement == 0 || (movement & (movement - 1)) == 0);
    }
    public static MovementModel FromRules(MatchModifierFlags rules) =>
        !ValidRules(rules) ? (MovementModel)byte.MaxValue
        : (rules & MatchModifierFlags.TraversalMovement) != 0 ? MovementModel.Traversal
        : (rules & MatchModifierFlags.Quake3Movement) != 0 ? MovementModel.Quake3
        : (rules & MatchModifierFlags.MomentumMovement) != 0 ? MovementModel.Momentum : MovementModel.Native;
}
