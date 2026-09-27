using System;
using OpenTK.Mathematics;
using MphRead.Mods.Cosmetics.Skins;
using MphRead.Mods.Cosmetics.Armor;
using MphRead.Mods.Cosmetics.Death;
namespace MphRead.Mods.Cosmetics
{
    public sealed class CosmeticAppearance
    {
        public CosmeticLoadout Loadout { get; }
        public SkinDefinition Skin { get; }
        public ArmorEffectDefinition Armor { get; }
        public DeathPresentationDefinition Death { get; }
        public CosmeticAppearance(Hunter hunter, CosmeticLoadout loadout)
        {
            Loadout = CosmeticCatalog.Resolve(hunter, loadout);
            Skin = CosmeticCatalog.ResolveSkin(Loadout.SkinKey, hunter);
            Armor = CosmeticCatalog.ResolveArmor(Loadout.ArmorEffectKey);
            Death = CosmeticCatalog.ResolveDeath(Loadout.DeathEffectKey);
        }
        public static readonly CosmeticAppearance Default = new(Hunter.Samus, CosmeticLoadout.Default);
    }
    public readonly record struct CosmeticSurface(int Skin, int Effect, float Time, Vector3 Primary,
        Vector3 Secondary, float Intensity, float Pulse, float Scroll, float Dissolve = 0);
    public static class CosmeticRuntime
    {
        public static CosmeticAppearance Get(Scene scene, int slot, Hunter hunter, bool local)
        {
            if (!RenderOptions.ShowCustomCosmetics || (int)hunter >= 7) return CosmeticAppearance.Default;
            if (scene.Services is Network.ReplaySceneServices replica)
                return replica.State.Cosmetics.Get(slot, hunter, replica.State.Occupant(slot).Generation);
            if (Network.NetSession.Active)
                return Network.NetCosmetics.Live.Get(slot, hunter, Network.NetPlayerLifecycle.Generation(slot));
            return local ? Local(hunter) : CosmeticAppearance.Default;
        }
        private static readonly CosmeticAppearance?[] LocalCache = new CosmeticAppearance?[7];
        public static CosmeticAppearance Local(Hunter hunter)
        {
            if ((int)hunter >= 7) return CosmeticAppearance.Default;
            var value = CosmeticDebug.Override ?? CosmeticPersistence.Get(hunter);
            var cached = LocalCache[(int)hunter];
            if (cached == null || cached.Loadout != value) LocalCache[(int)hunter] = cached = new(hunter, value);
            return cached;
        }
        public static CosmeticLod Lod(float distance) => distance < 8 ? CosmeticLod.Near
            : distance < 18 ? CosmeticLod.Medium : distance < 35 ? CosmeticLod.Far : CosmeticLod.Hidden;
        public static uint Seed(ushort match, int slot, ushort generation) =>
            unchecked((uint)(match * 73856093) ^ (uint)(slot * 19349663) ^ (uint)(generation * 83492791));
        public static CosmeticSurface Surface(CosmeticAppearance appearance, float time, bool suppressed,
            bool firstPerson = false, bool alt = false, float distance = 0, bool team = false)
        {
            if (!RenderOptions.ShowCustomCosmetics || suppressed) return default;
            time = CosmeticDebug.FixedTime ?? time;
            var armor = appearance.Armor;
            var lod = Lod(distance);
            bool effect = RenderOptions.CosmeticQuality != CosmeticEffectQuality.Off && lod != CosmeticLod.Hidden
                && (!alt || armor.SupportsAltForm);
            return new(team ? 0 : appearance.Skin.SurfaceTreatment,
                effect ? (int)(lod == CosmeticLod.Far ? SurfaceStyle.Fresnel : armor.ShaderStyle) : 0,
                time, armor.PrimaryColor, armor.SecondaryColor,
                effect ? armor.Intensity * (firstPerson ? armor.FirstPersonIntensity : alt ? armor.AltFormIntensity : 1) : 0,
                armor.PulseSpeed, armor.ScrollSpeed);
        }
    }
}
