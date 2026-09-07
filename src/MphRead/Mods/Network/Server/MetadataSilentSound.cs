using System;

namespace MphRead
{
    public static partial class Metadata
    {
        // Gameplay call sites index these tables before reaching the silent
        // sound sink. No sound/font data from executable overlays is needed.
        internal static void UseSilentSoundTables()
        {
            PlatformSfx = SilentTable(45, 4);
            HunterSfx = SilentTable(8, 17);
            BeamSfx = SilentTable(9, 10);
            TerrainSfx = SilentTable(12, 6);
            EnemyDamageSfx = new int[52];
            EnemyDeathSfx = new int[52];
            Array.Fill(EnemyDamageSfx, -1);
            Array.Fill(EnemyDeathSfx, -1);
        }

        private static int[,] SilentTable(int rows, int columns)
        {
            var values = new int[rows, columns];
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    values[row, column] = -1;
                }
            }
            return values;
        }
    }
}
