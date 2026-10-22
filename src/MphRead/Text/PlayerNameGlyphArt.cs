using System;

namespace MphRead.Text;

/// <summary>
/// Deterministic 8x8 native-font name glyphs. Preserve original ROM graphics
/// whenever they are present; reserve 0x200..0x207 for the eight new symbols.
/// </summary>
internal static class PlayerNameGlyphArt
{
    private const int Cell = 8;
    private static readonly (ushort Code, bool Replace, string[] Rows)[] Glyphs =
    {
        (0x1C2, false, new[] { "........", "....##..", "...##...", "..##....", "........", "........", "........", "........" }),
        (0x1C3, false, new[] { "..#.....", "...#.##.", "....##..", "...##...", "..##....", "........", "........", "........" }),
        (0x1C6, false, new[] { "........", "..##.##.", "..#..#..", ".#..#...", "........", "........", "........", "........" }),
        (0x1C7, false, new[] { "...##...", "..#..#..", ".######.", "...##...", "...##...", ".######.", "........", "........" }),
        (0x1C9, false, new[] { "........", ".....##.", "....##..", ".##.##..", "...##...", "..#..#..", "........", "........" }),
        (0x1CA, false, new[] { "........", "..####..", ".##..##.", ".#....#.", ".#....#.", ".##..##.", "..####..", "........" }),
        (0x1D1, false, new[] { "........", "..#..#..", "..#..#..", "..#..#..", "..#..#..", "..#..#..", "........", "........" }),
        (0x1D4, false, new[] { "........", "........", "........", "........", "..##.##.", "..##.##.", "........", "........" }),
        (0x1E3, false, new[] { "........", "..#.#...", ".#.#....", "#.#.....", ".#.#....", "..#.#...", "........", "........" }),
        (0x1E4, false, new[] { "........", "...#.#..", "....#.#.", ".....#.#", "....#.#.", "...#.#..", "........", "........" }),
        (0x1E7, false, new[] { "..######", "..#.....", "..#.....", "..#.....", "..#.....", "..#..##.", "..####..", "........" }),
        (0x1E8, false, new[] { "######..", ".....#..", ".....#..", ".....#..", ".....#..", ".##..#..", "..####..", "........" }),
        (0x1E9, false, new[] { "..####..", "..##....", "..##....", "..##....", "..##....", "..##....", "..####..", "........" }),
        (0x1EA, false, new[] { "..####..", "....##..", "....##..", "....##..", "....##..", "....##..", "..####..", "........" }),
        (0x1F1, false, new[] { "........", "........", ".######.", "....#...", "...#....", ".######.", "..#.....", "........" }),
        (0x1F2, false, new[] { "........", "....##..", "..##....", "##......", "..##....", "....##..", "........", "........" }),
        (0x1F3, false, new[] { "........", "..##....", "....##..", "......##", "....##..", "..##....", "........", "........" }),
        (0x1F4, false, new[] { "........", "....##..", "..##....", "##......", "..##....", "....##..", ".######.", "........" }),
        (0x1F5, false, new[] { "........", "..##....", "....##..", "......##", "....##..", "..##....", ".######.", "........" }),
        (0x1F8, false, new[] { "....##..", "....#.#.", "...####.", ".##..##.", ".##..##.", "..####..", "........", "........" }),
        (0x1F9, false, new[] { "........", "..####..", ".##..##.", "..####..", "...##...", ".######.", "...##...", "........" }),
        (0x1FD, false, new[] { "..##....", ".#..#...", "..##.##.", ".....#..", ".....#..", "......##", "..####..", "........" }),
        (0x1FE, false, new[] { "........", ".##..##.", "..####..", "...##...", ".######.", "...##...", ".######.", "........" }),
        (0x1FC, false, new[] { "........", "..##.##.", "..##.##.", ".##.##..", ".#..#...", "........", "........", "........" }),
        (0x1FF, false, new[] { "...##...", ".######.", ".##.....", "..####..", ".....##.", ".######.", "...##...", "........" }),
        (0x200, true , new[] { "........", "..##.##.", ".##.##..", ".##.##..", ".#..#...", "........", "........", "........" }),
        (0x201, true , new[] { "........", "........", "........", "..#..#..", ".##.##..", ".##.##..", ".##.##..", "........" }),
        (0x202, true , new[] { "........", ".######.", "........", "...##...", ".######.", "...##...", "........", "........" }),
        (0x203, true , new[] { "#......#", ".#....#.", "..#..#..", "...##...", "...##...", "..#..#..", ".#....#.", "#......#" }),
        (0x204, true , new[] { "........", "........", "........", "........", "........", "........", "##.##.##", "........" }),
        (0x205, true , new[] { "........", ".######.", "........", "........", "........", "........", "........", "........" }),
        (0x206, true , new[] { "........", "........", "........", "..####..", "........", "........", "........", "........" }),
        (0x207, true , new[] { "...##...", "...##...", "..##....", "..#.....", "........", "........", "........", "........" }),
    };

    // If the ROM left a full-width punctuation tile empty, copy its familiar
    // ASCII/native-equivalent art instead of producing an invisible name.
    // The Unicode glyph code is still distinct; only the last-resort pixels match.
    private static readonly (ushort Code, ushort Source)[] FallbackCopies =
    {
        (0x1B3, 0x2C),
        (0x1B4, 0x2E),
        (0x1B6, 0x3A),
        (0x1B7, 0x3B),
        (0x1B8, 0x3F),
        (0x1B9, 0x21),
        (0x1BD, 0x60),
        (0x1BF, 0x5E),
        (0x1C1, 0x5F),
        (0x1CC, 0x2D),
        (0x1D2, 0x7C),
        (0x1D5, 0x27),
        (0x1D6, 0x27),
        (0x1D7, 0x22),
        (0x1D8, 0x22),
        (0x1D9, 0x28),
        (0x1DA, 0x29),
        (0x1DB, 0x28),
        (0x1DC, 0x29),
        (0x1DD, 0x5B),
        (0x1DE, 0x5D),
        (0x1DF, 0x7B),
        (0x1E0, 0x7D),
        (0x1E1, 0x3C),
        (0x1E2, 0x3E),
        (0x1EB, 0x2B),
        (0x1EC, 0x2D),
        (0x1ED, 0xB1),
        (0x1EE, 0xD7),
        (0x1EF, 0xF7),
        (0x1F0, 0x3D),
    };

    internal static void Install(ref int[] widths, ref int[] offsets, ref byte[] pixels, int minCharacter)
    {
        if (minCharacter != 32) return; // Only the packed normal font.
        int required = 0x208 - minCharacter;
        if (widths.Length < required) Array.Resize(ref widths, required);
        if (offsets.Length < required) Array.Resize(ref offsets, required);
        if (pixels.Length < required * Cell * Cell) Array.Resize(ref pixels, required * Cell * Cell);

        foreach (var (code, source) in FallbackCopies)
        {
            int index = code - minCharacter, sourceIndex = source - minCharacter;
            int start = index * Cell * Cell, sourceStart = sourceIndex * Cell * Cell;
            if (widths[index] > 0 && HasInk(pixels, start)) continue;
            if (widths[sourceIndex] == 0 || !HasInk(pixels, sourceStart)) continue;
            Array.Copy(pixels, sourceStart, pixels, start, Cell * Cell);
            widths[index] = widths[sourceIndex];
            offsets[index] = offsets[sourceIndex];
        }

        foreach (var glyph in Glyphs)
        {
            int index = glyph.Code - minCharacter;
            int start = index * Cell * Cell;
            if (!glyph.Replace && widths[index] > 0 && HasInk(pixels, start)) continue;
            Array.Clear(pixels, start, Cell * Cell);
            for (int y = 0; y < Cell; y++)
                for (int x = 0; x < Cell; x++)
                    if (glyph.Rows[y][x] == '#') pixels[start + y * Cell + x] = 1;
            widths[index] = Cell;
            offsets[index] = 0;
        }
    }

    private static bool HasInk(byte[] pixels, int start)
    {
        for (int i = 0; i < Cell * Cell; i++)
            if (pixels[start + i] != 0) return true;
        return false;
    }
}
