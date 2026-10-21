using System.IO;
using System.Threading;

namespace MphRead.Mods.Render.Characters;

// Distinct closed capability; an ordinary solo capture cannot be passed to an
// Arsenal owner. The existing exact source/proof/clip decoder remains shared.
internal sealed class CharacterSourceArsenalDroneSelection
{
    internal CharacterSourceDroneSelection Source { get; }
    private CharacterSourceArsenalDroneSelection(CharacterSourceDroneSelection source) => Source = source;
    internal static CharacterSourceArsenalDroneSelection Load(string id, CancellationToken cancellation, string? root = null)
    {
        if (CharacterGeneratedDroneProfile.Find(id) == null)
            throw new InvalidDataException("An exact Alpha or Gamma Arsenal drone profile is required.");
        return new(CharacterSourceDroneSelection.Load(id, cancellation, root));
    }
}
