namespace MphRead.Mods.Render.Characters;

// Structural admission works during construction; currentness requires a published attachment.
internal interface ICharacterSourcePresentationOwner
{
    bool Owns(Scene scene, Model native, CharacterModelPack pack, PrivateCharacterAppearance appearance,
        CharacterSourcePreviewInstance instance);
    bool IsCurrent(CharacterModelPart part);
}
