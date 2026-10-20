using System;
using MphRead.Droid;
using MphRead.Mods.Input;
using MphRead.Mods.Render.Hud;

internal static class WeaponTouchChecks
{
    public static void Run()
    {
        var oldMode = TouchSettings.Mode;
        bool oldDirect = TouchSettings.DirectWeaponSelection;
        try
        {
            TouchSettings.Mode = TouchMode.Touch;
            TouchSettings.DirectWeaponSelection = true;
            var controls = new TouchControls();
            controls.Layout(1920, 1080, 3);
            controls.PointerDown(1, 1400, 500); // pre-existing aim cannot become ring owner
            var menu = TouchSettings.Geometry(TouchControl.WeaponMenu, 1920, 1080);
            controls.PointerDown(2, menu.X, menu.Y);
            if (controls.WeaponWheelPosition() != (true, menu.X, menu.Y)) throw new Exception("aim stole ring");
            controls.PointerDown(3, 800, 400);
            controls.PointerDown(4, 900, 600);
            controls.PointerMove(4, 1000, 650, 0, 0);
            if (controls.WeaponWheelPosition() != (true, 800f, 400f)) throw new Exception("third pointer stole ring");
            controls.PointerMove(3, 700, 300, 0, 0);
            controls.PointerUp(3);
            controls.PointerUp(2);
            if (controls.WeaponWheelPosition() != (true, 700f, 300f)) throw new Exception("release lost selection before equip");
            controls.ReleaseEverything();
            if (controls.WeaponWheelPosition().Down) throw new Exception("cancel retains selection");
            foreach (var size in new[] { (1920f,1080f), (1024f,768f), (1800f,1080f) })
            for (int slot = 0; slot < 6; slot++)
            {
                var bounds = HudSpriteBounds.Resolve(.3f + slot * .08f, .4f, 32, 32,
                    false, 1, .7f, size.Item1, size.Item2, 1.2f, 8, 4, .9f, .85f);
                float x = bounds.Left + bounds.Width / 2;
                float y = bounds.Top + bounds.Height / 2;
                if (!bounds.Contains(x,y) || bounds.Contains(bounds.Left - 1,y)
                    || bounds.Contains(float.NaN,y) || bounds.Contains(bounds.Left + bounds.Width,y))
                    throw new Exception("rendered HUD bounds hit test");
                if (MathF.Abs(bounds.Width - 32 * size.Item2 / 192 * .7f * 1.2f * .9f * .85f) > .001f)
                    throw new Exception("HUD draw geometry changed");
            }
            Console.WriteLine("TOUCHCHECK PASS: ring pointer ownership, completion/cancel, rendered HUD targets at 16:9, 4:3, 5:3");
        }
        finally { TouchSettings.Mode = oldMode; TouchSettings.DirectWeaponSelection = oldDirect; }
    }
}
