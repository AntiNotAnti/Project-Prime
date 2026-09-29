using MphRead.Mods.EnhancedHunters;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Entities;
public partial class PlayerEntity
{
    public partial class PlayerAiData
    {
        private bool SafeVolcanicLanding()
        {
            if (_nodeList.Count == 0 || !_player.Flags1.TestFlag(PlayerFlags1.Grounded)) return false;
            Vector3 position = _player.Position.AddY(.6f);
            Vector3 velocity = _player.Speed + Vector3.UnitY * .3f + _player.FacingVector.WithY(0) * .12f;
            float gravity = Fixed.ToFloat(_player.IsAltForm ? _player.Values.AltAirGravity : _player.Values.BipedGravity) / 2;
            if (gravity >= 0) return false;
            for (int frame = 0; frame < 120; frame++)
            {
                Vector3 next = position + velocity;
                CollisionResult hit = default;
                var candidates = CollisionDetection.GetCandidatesForLimits(position, next, .5f, null,
                    Vector3.Zero, includeEntities: true, _scene);
                if (CollisionDetection.CheckBetweenPoints(candidates, position, next, TestFlags.Beams, _scene, ref hit))
                {
                    if (velocity.Y >= 0 || hit.Plane.Y < .85f || hit.Terrain >= Terrain.Lava) return false;
                    var node = FindClosestNonHazardNodeToPosition(hit.Position);
                    return node.NodeType != NodeType.Hazard && (node.Position - hit.Position).LengthSquared < 4;
                }
                position = next; velocity.Y += gravity;
            }
            return false;
        }
        private void ApplyEnhancedHunterChoices()
        {
            if (!EnhancedHunters.Authority(_player)) return;
            var state = _player.EnhancedState;
            var mark = EnhancedHunters.Target(_player);
            if (mark != null && _player.Hunter is Hunter.Kanden or Hunter.Sylux or Hunter.Noxus)
                Func21356C0(mark);
            float distance = mark == null ? float.MaxValue : (mark.Position - _player.Position).LengthSquared;
            switch (_player.Hunter)
            {
                case Hunter.Samus when _player.CurrentWeapon == BeamType.Missile && !_player.IsAltForm:
                    if (state.ValueA is 1 or 2) _buttons.R.IsDown = true;
                    else if (state.ValueA == 3 && _player.EquipInfo.ChargeLevel >= _player.EquipInfo.Weapon.FullCharge * 2)
                        _buttons.R.IsDown = false;
                    break;
                case Hunter.Kanden when mark != null:
                    if (distance < 9 && !_player.IsAltForm) _touchButtons.Morph.IsDown = true;
                    if (_player.IsAltForm && distance < 16) _buttons.R.IsDown = true;
                    break;
                case Hunter.Trace when mark != null:
                    if (distance < 144 && EnhancedHunters.Visible(_player, mark))
                    {
                        if (!_player.IsAltForm) _touchButtons.Morph.IsDown = true;
                        else _buttons.R.IsDown = true;
                    }
                    else if (!_player.IsAltForm && _scene.FrameCount % 180 < 24)
                    { _buttons.R.IsDown = false; _buttons.Down.IsDown = true; }
                    break;
                case Hunter.Sylux when state.Flags != 0 && !_player.IsAltForm:
                    _buttons.R.IsDown = state.ValueA < 100;
                    break;
                case Hunter.Noxus when mark != null && mark.ModFrozen && state.Flags != 0:
                    if (distance < 6.25f)
                    {
                        if (!_player.IsAltForm) _touchButtons.Morph.IsDown = true;
                        else _buttons.R.IsDown = true;
                    }
                    else if (_player.CurrentWeapon == BeamType.Judicator)
                        _buttons.R.IsDown = distance < 144 ? _scene.FrameCount % 12 == 0 : _player.EquipInfo.ChargeLevel < _player.EquipInfo.Weapon.FullCharge * 2;
                    break;
                case Hunter.Weavel when state.ValueA >= 2 && !_player.IsAltForm:
                    if (_targetPlayer != null && (_targetPlayer.Position - _player.Position).LengthSquared < 225
                        && EnhancedHunters.Visible(_player, _targetPlayer)) _touchButtons.Morph.IsDown = true;
                    break;
                case Hunter.Spire when _targetPlayer != null && _player.CurrentWeapon == BeamType.Magmaul:
                    foreach (var zone in _scene.EnhancedWorld.Zones)
                        if (zone.Type == EnhancedZoneType.MagmaPool && EnhancedHunterWorld.Owned(_player, zone)
                            && EnhancedHunterWorld.Contains(zone, _targetPlayer.Position)
                            && (!EnhancedHunterWorld.Contains(zone, _player.Position) || SafeVolcanicLanding()))
                        { _buttons.R.IsDown = _scene.FrameCount % 12 == 0; break; }
                    break;
            }
        }
    }
}
