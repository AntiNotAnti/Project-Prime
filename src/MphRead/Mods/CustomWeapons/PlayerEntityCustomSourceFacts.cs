using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons
{
    internal readonly record struct CustomPrecombatFacts(Vector3 Body, Vector3 Aim, bool CanUseWeapon,
        CustomSourceModifierEvidence Modifiers = default, bool Crouched = false);
}

namespace MphRead.Entities
{
    public partial class PlayerEntity
    {
        /// <summary>Read source-phase facts without advancing aim, controls or
        /// native weapon state. The caller supplies the accepted source aim;
        /// these facts alone do not establish action or network admission.</summary>
        internal bool ModTryCaptureCustomPrecombatFacts(Vector3 acceptedAim, out CustomPrecombatFacts facts)
        {
            facts = default;
            Vector3 body = Position;
            float lengthSquared = acceptedAim.LengthSquared;
            if (!float.IsFinite(body.X) || !float.IsFinite(body.Y) || !float.IsFinite(body.Z)
                || !float.IsFinite(acceptedAim.X) || !float.IsFinite(acceptedAim.Y) || !float.IsFinite(acceptedAim.Z)
                || !float.IsFinite(lengthSquared) || lengthSquared <= 1e-8f) return false;

            bool canUse = LoadFlags.TestFlag(LoadFlags.Active) && ModIsInPlay && _health > 0
                && !Flags2.TestFlag(PlayerFlags2.Spectating)
                && _frozenTimer == 0 && !_field6D0
                && !IsAltForm && !IsMorphing && !IsUnmorphing
                && !ScanVisor && AttachedEnemy is null
                && GunAnimation != GunAnimation.UpDown && _disruptedTimer == 0
                && !(IsMainPlayer && _scene.CameraSequences.Current?.BlockInput == true)
                && !_scene.GameState.MenuPause && !_scene.GameState.DialogPause
                && _scene.GameState.MatchState == MatchState.InProgress;
            facts = new(body, acceptedAim.Normalized(), canUse, Crouched: ModCrouched);
            return true;
        }
    }
}
