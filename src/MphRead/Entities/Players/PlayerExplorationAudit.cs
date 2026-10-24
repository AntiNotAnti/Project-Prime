using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Read-only access to the controller's actual input basis for the opt-in
    // acceptance tour. Right input subtracts the left/right basis in both forms.
    internal void GetExplorationMovementBasis(out Vector3 forward, out Vector3 right)
    {
        bool rolling = IsAltForm && Values.AltFormStrafe == 0;
        forward = rolling ? new(_altRollFbX, 0, _altRollFbZ) : new(_field70, 0, _field74);
        right = rolling ? new(-_altRollLrX, 0, -_altRollLrZ) : new(-_field78, 0, -_field7C);
    }
}
