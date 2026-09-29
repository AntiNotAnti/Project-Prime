namespace MphRead.Entities;
public partial class PlayerEntity
{
    internal void ModTrainingHide(bool hidden)
    {
        if (hidden) { Flags2 |= PlayerFlags2.HideModel | PlayerFlags2.Spectating; LoadFlags &= ~LoadFlags.Spawned; }
        else Flags2 &= ~(PlayerFlags2.HideModel | PlayerFlags2.Spectating);
    }
}
