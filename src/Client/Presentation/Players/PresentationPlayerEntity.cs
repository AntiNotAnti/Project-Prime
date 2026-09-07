using System;
using System.Collections.Generic;
using System.Diagnostics;
using MphRead.Effects;
using MphRead.Formats;
using MphRead.Formats.Culling;
using MphRead.Hud;
using MphRead.Text;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        private int _trailBindingId1 = 0;
        private int _trailBindingId2 = 0;
        private int _doubleDmgBindingId = 0;
        public int DoubleDmgBindingId => _doubleDmgBindingId;

        private int _missileSfxHandle = -1;
        private float _walkSfxTimer = 0;
        private int _walkSfxIndex = 0;
        private float _burnSfxAmount = 0;
        private float _moveSfxAmount = 0;
        public void ShowNoAmmoMessage()
        {
            string message = Strings.GetHudMessage(9); // AMMO DEPLETED!
            QueueHudMessage(128, 120, Align.Center, 256, 8, new ColorRgba(0x295F), 1, 45 / 30f, 1, message);
        }
    }
}
