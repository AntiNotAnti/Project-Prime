using MphRead.Mods.Render.Characters;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private CharacterSourceArsenalDroneOwner? _sourceArsenalDrone;
    internal CharacterSourceArsenalDroneOwner? ModSourceArsenalDrone => _sourceArsenalDrone;
    private void ModRetireSourceArsenalDrone()
    { _sourceArsenalDrone?.Dispose(); _sourceArsenalDrone = null; }
}
