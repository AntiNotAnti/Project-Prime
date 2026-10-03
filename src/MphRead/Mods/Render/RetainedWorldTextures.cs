namespace MphRead.Mods.Render
{
    internal readonly record struct RetainedTextureBinding(
        int Id,
        TextureSamplerDescriptor Sampling,
        bool ApplySampling)
    {
        internal bool IsBound => Id != 0;
    }

    internal readonly record struct RetainedWorldTextureSet(
        RetainedTextureBinding Albedo,
        RetainedTextureBinding Normal,
        RetainedTextureBinding Specular,
        RetainedTextureBinding Emissive)
    {
        internal bool Advanced =>
            Normal.IsBound || Specular.IsBound || Emissive.IsBound;

        internal RetainedTextureBinding At(int unit) => unit switch
        {
            0 => Albedo,
            1 => Normal,
            2 => Specular,
            3 => Emissive,
            _ => default
        };
    }
}
