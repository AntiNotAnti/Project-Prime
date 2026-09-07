using System;

namespace MphRead.Formats.Sound
{
    public static class SoundPitch
    {
        public static float CalculatePitchDiv(float pitchFac)
        {
            if (pitchFac == 0)
            {
                pitchFac = 1;
            }
            int pitchInt = (int)pitchFac;
            if (pitchFac <= 0xFFF)
            {
                pitchInt = -((0x600000 / pitchInt) >> 1);
            }
            else if (pitchFac <= 0x1FFF)
            {
                pitchInt = (768 * (pitchInt - 0x2000)) >> 12;
            }
            else
            {
                pitchInt = (768 * (pitchInt - 0x2000)) >> 13;
            }
            float semitones = pitchInt / 64f;
            float octaves = MathF.Abs(semitones / 12f);
            if (semitones >= 0)
            {
                return MathF.Pow(2, octaves);
            }
            return MathF.Pow(0.5f, octaves);
        }

    }
}
