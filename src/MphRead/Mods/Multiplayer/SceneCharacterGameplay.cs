namespace MphRead;

public partial class Scene
{
    // This Scene has observed mechanics outside the replay setup/state schema.
    // Retirement cannot erase that history; only a fresh Scene is recordable.
    internal bool PrivateCharacterGameplayUnsupported { get; private set; }
    internal void MarkPrivateCharacterGameplayUnsupported()
    {
        PrivateCharacterGameplayUnsupported = true;
        Mods.KillCam.Reset();
    }
}
