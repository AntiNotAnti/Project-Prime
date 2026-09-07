namespace MphRead.Mods.Network
{
    /// <summary>Actual Spawn results, after partial-charge interpolation and affinity selection.</summary>
    public readonly record struct BeamMechanics(BeamType Beam, BeamType BeamKind, bool Continuous,
        bool InstantArea, float Homing, float Speed, float Lifespan);

}
