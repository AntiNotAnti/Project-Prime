using System;

namespace MphRead.Mods.Multiplayer;

/// <summary>
/// One physical Volt Driver tap can wait for the weapon's native cooldown.
/// The buffer is deliberately one deep: repeated rapid taps never exceed the
/// native fire rate, and a held trigger cannot release a buffered uncharged shot
/// while the player is trying to charge.
/// </summary>
internal struct VoltDriverTapBuffer
{
    private int _remaining;

    internal static int MaxDelayFrames(int cooldownFrames) => Math.Max(0, cooldownFrames - 1);

    internal bool Step(bool enabled, bool pressed, bool held, int framesSinceShot, int cooldownFrames)
    {
        if (!enabled || cooldownFrames <= 1)
        {
            Reset();
            return false;
        }
        if (_remaining > 0) _remaining--;
        // Only real press edges during the cooldown create pending fire intent.
        if (pressed && framesSinceShot < cooldownFrames) _remaining = cooldownFrames;
        // Never turn one long hold into an extra uncharged shot.
        return _remaining > 0 && !held && framesSinceShot >= cooldownFrames;
    }

    internal void Reset() => _remaining = 0;
}
