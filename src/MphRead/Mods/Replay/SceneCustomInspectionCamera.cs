using MphRead.Entities;
using MphRead.Formats;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

namespace MphRead;

public partial class Scene
{
    // Private picture ownership, deliberately outside the canonical world
    // schema. MainPlayerIndex itself is canonical and may only be borrowed
    // inside a draw scope, never changed by this camera selection.
    private int _customInspectionFirstPersonSlot = -1;
    private ushort _customInspectionCameraGeneration, _customInspectionCameraLife;
    internal int CustomInspectionFirstPersonSlot => _customInspectionFirstPersonSlot;

    internal float CustomInspectionCameraFov(PlayerEntity player, float recordedFov)
    {
        if (_customInspectionFirstPersonSlot != player.SlotIndex
            || !TryReplayCustomPresentation(out _, out var custom)) return recordedFov;
        var hud = CustomWeaponHud(player);
        if (!hud.Ready || !hud.Visible) return recordedFov;
        var optics = custom.Context.Definition.Mechanics.OpticMagnificationsMilli;
        int optic = hud.State.Optic;
        if (optic > optics.Count) return recordedFov;
        // The accepted optic is exact; authority packets do not capture the
        // owner's lens transition. Reconstruct its settled projection without
        // changing CameraInfo or simulating a local zoom controller.
        return CustomScopeMath.TargetFov(Fixed.ToFloat(player.Values.NormalFov) * 2,
            optic == 0 ? 1000 : optics[optic - 1]);
    }

    internal bool SetCustomInspectionFirstPerson(int slot)
    {
        if (!TryCustomInspectionCameraSubject(slot, out _, out var state)) return false;
        _customInspectionFirstPersonSlot = slot;
        _customInspectionCameraGeneration = state.SlotGeneration;
        _customInspectionCameraLife = state.LifeId;
        _cameraMode = CameraMode.Player;
        return true;
    }

    internal void ClearCustomInspectionFirstPerson()
    {
        if (_customInspectionFirstPersonSlot < 0) return;
        _customInspectionFirstPersonSlot = -1;
        _customInspectionCameraGeneration = _customInspectionCameraLife = 0;
        // Retain the last picture's eye as a stationary inspection camera.
        // No live controls or global spectator state participate.
        _cameraMode = CameraMode.Roam;
    }

    private bool TryCustomInspectionCameraSubject(int slot, out int index, out PlayerState state)
    {
        index = -1; state = default;
        if ((uint)slot >= PlayerEntity.SlotCapacity
            || Services is not ReplaySceneServices replay || !replay.Session.IsActive
            || !replay.Session.IsCustomArchiveInspection || ReplayPoses == null
            || !replay.State.TryGetPlayer(slot, out state) || state.Health == 0
            || state.SlotGeneration == 0 || state.LifeId == 0) return false;
        for (int i = 0; i < Players.Items.Count; i++)
        {
            var player = Players.Items[i];
            if (player.SlotIndex != slot) continue;
            if (player.Health == 0 || !player.LoadFlags.TestFlag(LoadFlags.Active | LoadFlags.Spawned)
                || player.Flags2.TestFlag(PlayerFlags2.Spectating) || player.IsAltForm
                || player.IsMorphing || player.IsUnmorphing || player.CameraType != CameraType.First) return false;
            index = i; return true;
        }
        return false;
    }

    private DirectorSubjectScope BeginCustomInspectionPicture()
    {
        int index = -1;
        if (_customInspectionFirstPersonSlot >= 0)
        {
            if (TryCustomInspectionCameraSubject(_customInspectionFirstPersonSlot, out index, out var state)
                && state.SlotGeneration == _customInspectionCameraGeneration && state.LifeId == _customInspectionCameraLife)
                _cameraMode = CameraMode.Player;
            else
            {
                ClearCustomInspectionFirstPerson();
                index = -1;
            }
        }
        return new(this, index);
    }
}
