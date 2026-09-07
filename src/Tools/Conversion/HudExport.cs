using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MphRead.Hud;
using static MphRead.Hud.HudFormat;

namespace MphRead.Export
{
    public static class HudExport
    {
        private static void TestAnimation(IReadOnlyList<UiAnimParams> animParams, int pInitial, int pStart, int pTimer, int pTarget)
        {
            int frame = pInitial;
            int timer = 0;
            int target = 0;
            bool updating = false;

            void SetAnim()
            {
                frame = pStart - 1;
                timer = pTimer - 1;
                target = pTarget;
                updating = true;
            }

            void ProcessAnim()
            {
                if (updating)
                {
                    frame++;
                    if (frame == timer)
                    {
                        if (target >= 0)
                        {
                            updating = false;
                            frame = target - 1;
                        }
                        else
                        {
                            // loop
                            frame = -1 - target;
                        }
                    }
                }
            }

            (int, int) CheckThing(int pFrame)
            {
                for (int i = 0; i < animParams.Count; i++)
                {
                    int value = animParams[i].Delay;
                    if (pFrame < value)
                    {
                        return (i, animParams[i].ImageIndex);
                    }
                    pFrame -= value;
                }
                return (0, animParams[0].ImageIndex);
            }

            (int sn, int sidx) = CheckThing(frame);
            Console.WriteLine($"start: {sn} --> {sidx}");
            SetAnim();

            int frameCount = -1;
            while (updating)
            {
                frameCount++;
                ProcessAnim();
                (int n, int idx) = CheckThing(frame);
                Console.WriteLine($"f{frameCount}: {n} --> {idx}");
                Nop();
            }
            Console.WriteLine();
            Nop();
        }

        public static void TestObjects(string? filename, int pInitial, int pStart, int pTimer, int pTarget,
            bool export = false)
        {
            // for ease of palette sharing, hud_msgBox needs to come before message_spacer,
            // and each hud_targetcircle needs to come before each hud_snipercircle
            var files = new List<string>()
            {
                "_archives/spSamus/hud_msgBox.bin",
                "_archives/spSamus/message_spacer.bin",
                "_archives/spSamus/message_pickupframe.bin",
                "_archives/spSamus/message_pickups.bin",
                "_archives/spSamus/message_crystalpickup.bin",
                "_archives/spSamus/map_quit.bin",
                "_archives/spSamus/scan_lore.bin",
                "_archives/spSamus/scan_lore_dim.bin",
                "_archives/spSamus/scan_enemy.bin",
                "_archives/spSamus/scan_enemy_dim.bin",
                "_archives/spSamus/scan_object.bin",
                "_archives/spSamus/scan_object_dim.bin",
                "_archives/spSamus/scan_equipment.bin",
                "_archives/spSamus/scan_equipment_dim.bin",
                "_archives/spSamus/scan_red.bin",
                "_archives/spSamus/scan_red_dim.bin",
                "_archives/spSamus/scan_arrow.bin",
                "_archives/spSamus/scan_ok.bin",
                "_archives/spSamus/scan_select.bin",
                "_archives/spSamus/scan_corner.bin",
                "_archives/spSamus/scan_cornerSm.bin",
                "_archives/spSamus/scan_horizline.bin",
                "_archives/spSamus/scan_vertline.bin",
                "_archives/spSamus/obj_wnd.bin",
                "_archives/spSamus/hud_etank.bin",
                "_archives/spSamus/map_portal.bin",
                "_archives/spSamus/map_crystalbig.bin",
                "_archives/spSamus/map_art_1.bin",
                "_archives/spSamus/map_art_2.bin",
                "_archives/spSamus/map_art_3.bin",
                "_archives/spSamus/map_art_4.bin",
                "_archives/spSamus/map_art_5.bin",
                "_archives/spSamus/map_art_6.bin",
                "_archives/spSamus/map_art_7.bin",
                "_archives/spSamus/map_art_8.bin",
                "_archives/spSamus/map_crystalred.bin",
                "_archives/spSamus/map_legendOthers.bin",
                "_archives/spSamus/map_legendDoors.bin",
                "hud/rad_NodesOG.bin",
                "hud/rad_NodesRB.bin",
                "_archives/commonMP/radar_octolithLARGE.bin",
                "_archives/commonMP/radar_octolithSMALL.bin",
                "_archives/commonMP/hud_systemload.bin",
                "_archives/commonMP/wifi_strength.bin",
                "_archives/commonMP/stars.bin",
                "_archives/common/rad_radplyred.bin",
                "_archives/common/rad_key.bin",
                "_archives/common/hud_boost.bin",
                "_archives/common/hud_bombs.bin",
                "_archives/common/enemy_samus.bin",
                "_archives/common/enemy_kanden.bin",
                "_archives/common/enemy_noxus.bin",
                "_archives/common/enemy_spyre.bin",
                "_archives/common/enemy_sylux.bin",
                "_archives/common/enemy_trace.bin",
                "_archives/common/enemy_weavel.bin",
                "_archives/localSamus/rad_wepsel.bin",
                "_archives/localSamus/wepsel_icon.bin",
                "_archives/localSamus/wepsel_extra.bin",
                "_archives/localSamus/wepsel_box.bin",
                "_archives/localSamus/wepsel_hotdot.bin",
                "_archives/localSamus/hud_targetcircle.bin",
                "_archives/localSamus/hud_snipercircle.bin",
                "_archives/localSamus/hud_primehunter.bin",
                "_archives/localSamus/cloaking.bin",
                "_archives/localSamus/hud_damage.bin",
                "_archives/localSamus/hud_weaponicon.bin",
                "_archives/localSamus/hud_ammobar.bin",
                "_archives/localSamus/hud_energybar.bin",
                "_archives/localSamus/hud_energybar2.bin",
                "_archives/localSamus/rad_lights.bin",
                "_archives/localSamus/rad_ammobar.bin",
                "_archives/localKanden/rad_wepsel.bin",
                "_archives/localKanden/wepsel_icon.bin",
                "_archives/localKanden/wepsel_extra.bin",
                "_archives/localKanden/wepsel_box.bin",
                "_archives/localKanden/wepsel_hotdot.bin",
                "_archives/localKanden/hud_targetcircle.bin",
                "_archives/localKanden/hud_snipercircle.bin",
                "_archives/localKanden/hud_primehunter.bin",
                "_archives/localKanden/cloaking.bin",
                "_archives/localKanden/hud_damage.bin",
                "_archives/localKanden/hud_weaponicon.bin",
                "_archives/localKanden/hud_ammobar.bin",
                "_archives/localKanden/hud_energybar.bin",
                "_archives/localKanden/hud_energybar2.bin",
                "_archives/localKanden/rad_lights.bin",
                "_archives/localKanden/rad_ammobar.bin",
                "_archives/localNox/rad_wepsel.bin",
                "_archives/localNox/wepsel_icon.bin",
                "_archives/localNox/wepsel_extra.bin",
                "_archives/localNox/wepsel_box.bin",
                "_archives/localNox/wepsel_hotdot.bin",
                "_archives/localNox/hud_targetcircle.bin",
                "_archives/localNox/hud_snipercircle.bin",
                "_archives/localNox/hud_primehunter.bin",
                "_archives/localNox/cloaking.bin",
                "_archives/localNox/hud_damage.bin",
                "_archives/localNox/hud_weaponicon.bin",
                "_archives/localNox/hud_ammobar.bin",
                "_archives/localNox/hud_energybar.bin",
                "_archives/localNox/hud_energybar2.bin",
                "_archives/localNox/rad_lights.bin",
                "_archives/localNox/rad_ammobar.bin",
                "_archives/localSpire/rad_wepsel.bin",
                "_archives/localSpire/wepsel_icon.bin",
                "_archives/localSpire/wepsel_extra.bin",
                "_archives/localSpire/wepsel_box.bin",
                "_archives/localSpire/wepsel_hotdot.bin",
                "_archives/localSpire/hud_targetcircle.bin",
                "_archives/localSpire/hud_snipercircle.bin",
                "_archives/localSpire/hud_primehunter.bin",
                "_archives/localSpire/cloaking.bin",
                "_archives/localSpire/hud_damage.bin",
                "_archives/localSpire/hud_weaponicon.bin",
                "_archives/localSpire/hud_ammobar.bin",
                "_archives/localSpire/hud_energybar.bin",
                "_archives/localSpire/hud_energybar2.bin",
                "_archives/localSpire/rad_lights.bin",
                "_archives/localSpire/rad_ammobar.bin",
                "_archives/localSylux/rad_wepsel.bin",
                "_archives/localSylux/wepsel_icon.bin",
                "_archives/localSylux/wepsel_extra.bin",
                "_archives/localSylux/wepsel_box.bin",
                "_archives/localSylux/wepsel_hotdot.bin",
                "_archives/localSylux/hud_targetcircle.bin",
                "_archives/localSylux/hud_snipercircle.bin",
                "_archives/localSylux/hud_primehunter.bin",
                "_archives/localSylux/cloaking.bin",
                "_archives/localSylux/hud_damage.bin",
                "_archives/localSylux/hud_weaponicon.bin",
                "_archives/localSylux/hud_ammobar.bin",
                "_archives/localSylux/hud_energybar.bin",
                "_archives/localSylux/hud_energybar2.bin",
                "_archives/localSylux/rad_lights.bin",
                "_archives/localSylux/rad_ammobar.bin",
                "_archives/localTrace/rad_wepsel.bin",
                "_archives/localTrace/wepsel_icon.bin",
                "_archives/localTrace/wepsel_extra.bin",
                "_archives/localTrace/wepsel_box.bin",
                "_archives/localTrace/wepsel_hotdot.bin",
                "_archives/localTrace/hud_targetcircle.bin",
                "_archives/localTrace/hud_snipercircle.bin",
                "_archives/localTrace/hud_primehunter.bin",
                "_archives/localTrace/cloaking.bin",
                "_archives/localTrace/hud_damage.bin",
                "_archives/localTrace/hud_weaponicon.bin",
                "_archives/localTrace/hud_ammobar.bin",
                "_archives/localTrace/hud_energybar.bin",
                "_archives/localTrace/hud_energybar2.bin",
                "_archives/localTrace/rad_lights.bin",
                "_archives/localTrace/rad_ammobar.bin",
                "_archives/localWeavel/rad_wepsel.bin",
                "_archives/localWeavel/wepsel_icon.bin",
                "_archives/localWeavel/wepsel_extra.bin",
                "_archives/localWeavel/wepsel_box.bin",
                "_archives/localWeavel/wepsel_hotdot.bin",
                "_archives/localWeavel/hud_targetcircle.bin",
                "_archives/localWeavel/hud_snipercircle.bin",
                "_archives/localWeavel/hud_primehunter.bin",
                "_archives/localWeavel/cloaking.bin",
                "_archives/localWeavel/hud_damage.bin",
                "_archives/localWeavel/hud_weaponicon.bin",
                "_archives/localWeavel/hud_ammobar.bin",
                "_archives/localWeavel/hud_energybar.bin",
                "_archives/localWeavel/hud_energybar2.bin",
                "_archives/localWeavel/rad_lights.bin",
                "_archives/localWeavel/rad_ammobar.bin"
            };
            FrozenDictionary<string, IReadOnlyList<int>> exportPalettes = Frozen.Create<string, IReadOnlyList<int>>(
            [
                new("hud_etank", new List<int>() { 0, 1, 2 }),
                new("hud_ammobar", new List<int>() { 0, 1, 2 }),
                new("hud_energybar", new List<int>() { 0, 1, 2 }),
                new("hud_energybar2", new List<int>() { 0, 1, 2 }),
                new("hud_msgBox", new List<int>() { 0, 2, 3 }),
                new("message_spacer", new List<int>() { 0, 2, 3 }),
                new("rad_wepsel", new List<int>() { 0, 1 })
            ]);
            if (filename != null)
            {
                files.Clear();
                files.Add(filename);
            }
            IReadOnlyList<ushort>? targetCirclePalColors = null;
            IReadOnlyList<ushort>? messageBoxPalColors = null;
            foreach (string file in files)
            {
                string name = file.Split('/').Last().Split('.')[0];
                var bytes = new ReadOnlySpan<byte>(ContentFiles.ReadBytes(Paths.Combine(Paths.FileSystem, file)));
                UiObjectHeader header = Read.ReadStruct<UiObjectHeader>(bytes);
                int offset = ObjectHeaderSize;
                Debug.Assert(header.ParamDataSize % AnimationParameterSize == 0);
                int count = header.ParamDataSize / AnimationParameterSize;
                Debug.Assert(count == header.FrameCount);
                IReadOnlyList<UiAnimParams> animParams = Read.DoOffsets<UiAnimParams>(bytes, offset, count);
                offset += header.ParamDataSize;
                Debug.Assert(header.AttrDataSize % OamAttributeSize == 0);
                count = header.AttrDataSize / OamAttributeSize;
                IReadOnlyList<RawUiOamAttrs> rawOamAttrs = Read.DoOffsets<RawUiOamAttrs>(bytes, offset, count);
                IReadOnlyList<UiOamAttrs> oamAttrs = rawOamAttrs.Select(r => new UiOamAttrs(r)).ToList();
                offset += header.AttrDataSize;
                IReadOnlyList<byte> characterData = Read.DoOffsets<byte>(bytes, offset, header.CharDataSize);
                offset += header.CharDataSize;
                Debug.Assert(header.PalDataSize % 2 == 0);
                IReadOnlyList<ushort> paletteData = Read.DoOffsets<ushort>(bytes, offset, header.PalDataSize / 2);
                offset += header.PalDataSize;
                Debug.Assert(offset == bytes.Length);

                if (name == "hud_targetcircle")
                {
                    targetCirclePalColors = paletteData;
                }
                else if (name == "hud_snipercircle")
                {
                    Debug.Assert(targetCirclePalColors != null);
                    paletteData = targetCirclePalColors;
                }
                else if (name == "hud_msgBox")
                {
                    messageBoxPalColors = paletteData;
                }
                else if (name == "message_spacer")
                {
                    Debug.Assert(messageBoxPalColors != null);
                    paletteData = messageBoxPalColors;
                }

                if (filename != null)
                {
                    TestAnimation(animParams, pInitial, pStart, pTimer, pTarget);
                }

                var palIndexData = new List<byte>();
                for (int i = 0; i < characterData.Count; i++)
                {
                    byte data = characterData[i];
                    palIndexData.Add((byte)(data & 0xF));
                    palIndexData.Add((byte)((data & 0xF0) >> 4));
                }

                var palColorData = new List<ColorRgba>();
                for (int i = 0; i < paletteData.Count; i++)
                {
                    palColorData.Add(new ColorRgba(paletteData[i]));
                }

                // todo: share this
                var characters = new List<List<List<ColorRgba>>>();
                Debug.Assert(characterData.Count % 32 == 0);
                Debug.Assert(paletteData.Count % 16 == 0);
                for (int p = 0; p < paletteData.Count / 16; p++)
                {
                    var palChars = new List<List<ColorRgba>>();
                    for (int i = 0; i < characterData.Count / 32; i++)
                    {
                        // todo?: if 256 colors were used, these would need to be parsed differently
                        var character = new List<ColorRgba>();
                        for (int y = 0; y < 8; y++)
                        {
                            for (int x = 0; x < 4; x++)
                            {
                                byte data = characterData[i * 32 + y * 4 + x];
                                int index1 = data & 0xF;
                                character.Add(index1 == 0 ? new ColorRgba() : new ColorRgba(paletteData[p * 16 + index1]));
                                int index2 = (data & 0xF0) >> 4;
                                character.Add(index2 == 0 ? new ColorRgba() : new ColorRgba(paletteData[p * 16 + index2]));
                            }
                        }
                        palChars.Add(character);
                    }
                    characters.Add(palChars);
                }
                UiOamAttrs attrs = oamAttrs[0];
                Debug.Assert(attrs.CharacterId == 0);
                if (attrs.PaletteId != 0)
                {
                    // 1 for rad_wepsel in localSamus, localKanden, and localWeavel
                    //Console.WriteLine(file);
                }
                Debug.Assert(!attrs.FlipHorizontal);
                Debug.Assert(!attrs.FlipVertical);
                // ltodo: parse characters for each palette that's actually specified by the attr, I guess?
                // --> some just have 1 in all the attrs instead of 0 in all the attrs, so just use that?
                (int width, int height) = ObjectDimensions[(int)attrs.Shape, (int)attrs.Size];
                int tiles = width * height;
                ushort texWidth = (ushort)(width * 8);
                ushort texHeight = (ushort)(height * 8);
                int size = texWidth * texHeight;
                for (int i = 0; i < header.ImageCount; i++)
                {
                    var distinctPalettes = new List<List<ushort>>();
                    for (int p = 0; p < paletteData.Count / 16; p++)
                    {
                        var palette = new List<ushort>();
                        for (int j = 0; j < 16; j++)
                        {
                            palette.Add(paletteData[p * 16 + j]);
                        }
                        if (distinctPalettes.Any(d => d.SequenceEqual(palette)))
                        {
                            continue;
                        }
                        distinctPalettes.Add(palette);
                        var texture = new ColorRgba[size];
                        for (int ty = 0; ty < height; ty++)
                        {
                            for (int tx = 0; tx < width; tx++)
                            {
                                List<ColorRgba> character = characters[p][i * tiles + ty * width + tx];
                                int start = ty * width * 8 * 8 + tx * 8;
                                for (int py = 0; py < 8; py++)
                                {
                                    for (int px = 0; px < 8; px++)
                                    {
                                        ColorRgba pixel = character[py * 8 + px];
                                        int index = start + py * width * 8 + px;
                                        texture[index] = pixel;
                                    }
                                }
                            }
                        }
                        if (export)
                        {
                            bool skip = p != 0;
                            if (exportPalettes.TryGetValue(name, out IReadOnlyList<int>? exportPalette))
                            {
                                skip = !exportPalette.Contains(p);
                            }
                            if (!skip)
                            {
                                string dir = file.Replace("_archives/", "").Replace(".bin", "");
                                dir = Paths.Combine(Paths.Export, "_2D/Objects", dir, $"pal_{p.ToString().PadLeft(2, '0')}");
                                Directory.CreateDirectory(dir);
                                string number = i.ToString().PadLeft(3, '0');
                                Export.Images.SaveTexture(dir, number, texWidth, texHeight, texture);
                            }
                        }

                    }
                }
                Nop();
            }
            Nop();
        }

        public static void TestLayers(bool exportScreens = false, bool exportChars = false)
        {
            var files = new List<string>()
            {
                "hud/samus/bg_bottom.bin",
                "hud/samus/bg_bottomL.bin",
                "hud/samus/bg_altform.bin",
                "hud/samus/bg_altformL.bin",
                "hud/samus/bg_wepsel.bin",
                "hud/samus/bg_wepselL.bin",
                "_archives/localSamus/map_grid.bin",
                "_archives/localSamus/bg_top_ovl.bin",
                "_archives/localSamus/bg_top.bin",
                "_archives/localSamus/bg_top_drop.bin",
                "hud/kanden/bg_bottom.bin",
                "hud/kanden/bg_bottomL.bin",
                "hud/kanden/bg_altform.bin",
                "hud/kanden/bg_altformL.bin",
                "hud/kanden/bg_wepsel.bin",
                "hud/kanden/bg_wepselL.bin",
                "_archives/localKanden/map_grid.bin",
                "_archives/localKanden/bg_top_ovl.bin",
                "_archives/localKanden/bg_top.bin",
                "_archives/localKanden/bg_top_drop.bin",
                "hud/nox/bg_bottom.bin",
                "hud/nox/bg_bottomL.bin",
                "hud/nox/bg_altform.bin",
                "hud/nox/bg_altformL.bin",
                "hud/nox/bg_wepsel.bin",
                "hud/nox/bg_wepselL.bin",
                "_archives/localNox/map_grid.bin",
                "_archives/localNox/bg_top_ovl.bin",
                "_archives/localNox/bg_top.bin",
                "_archives/localNox/bg_top_drop.bin",
                "hud/spire/bg_bottom.bin",
                "hud/spire/bg_bottomL.bin",
                "hud/spire/bg_altform.bin",
                "hud/spire/bg_altformL.bin",
                "hud/spire/bg_wepsel.bin",
                "hud/spire/bg_wepselL.bin",
                "_archives/localSpire/map_grid.bin",
                "_archives/localSpire/bg_top_ovl.bin",
                "_archives/localSpire/bg_top.bin",
                "_archives/localSpire/bg_top_drop.bin",
                "hud/sylux/bg_bottom.bin",
                "hud/sylux/bg_bottomL.bin",
                "hud/sylux/bg_altform.bin",
                "hud/sylux/bg_altformL.bin",
                "hud/sylux/bg_wepsel.bin",
                "hud/sylux/bg_wepselL.bin",
                "_archives/localSylux/map_grid.bin",
                "_archives/localSylux/bg_top_ovl.bin",
                "_archives/localSylux/bg_top.bin",
                "_archives/localSylux/bg_top_drop.bin",
                "hud/trace/bg_bottom.bin",
                "hud/trace/bg_bottomL.bin",
                "hud/trace/bg_altform.bin",
                "hud/trace/bg_altformL.bin",
                "hud/trace/bg_wepsel.bin",
                "hud/trace/bg_wepselL.bin",
                "_archives/localTrace/map_grid.bin",
                "_archives/localTrace/bg_top_ovl.bin",
                "_archives/localTrace/bg_top.bin",
                "_archives/localTrace/bg_top_drop.bin",
                "hud/weavel/bg_bottom.bin",
                "hud/weavel/bg_bottomL.bin",
                "hud/weavel/bg_altform.bin",
                "hud/weavel/bg_altformL.bin",
                "hud/weavel/bg_wepsel.bin",
                "hud/weavel/bg_wepselL.bin",
                "_archives/localWeavel/map_grid.bin",
                "_archives/localWeavel/bg_top_ovl.bin",
                "_archives/localWeavel/bg_top.bin",
                "_archives/localWeavel/bg_top_drop.bin",
                "_archives/spSamus/bg_scanjewel.bin",
                "_archives/spSamus/map_scan.bin",
                "_archives/common/bg_ice.bin"
            };
            foreach (string file in files)
            {
                var bytes = new ReadOnlySpan<byte>(ContentFiles.ReadBytes(Paths.Combine(Paths.FileSystem, file)));
                UiPartHeader header = Read.ReadStruct<UiPartHeader>(bytes);
                Debug.Assert(header.Magic == 0);
                int offset = LayerHeaderSize;
                IReadOnlyList<byte> characterData = Read.DoOffsets<byte>(bytes, offset, header.CharDataSize);
                offset += header.CharDataSize;
                Debug.Assert(header.PalDataSize % 2 == 0);
                IReadOnlyList<ushort> paletteData = Read.DoOffsets<ushort>(bytes, offset, header.PalDataSize / 2);
                offset += header.PalDataSize;
                ScrDatInfo info = Read.DoOffset<ScrDatInfo>(bytes, offset);
                offset += ScreenDataInfoSize;
                Debug.Assert(info.ScrDataSize % 2 == 0);
                IReadOnlyList<ushort> screenDataValues = Read.DoOffsets<ushort>(bytes, offset, info.ScrDataSize / 2);
                Debug.Assert(info.CharsX * info.CharsY == screenDataValues.Count);

                var vramStuff = new List<ushort>();
                int charSlot = (header.CharDataSize + 32) / 32;
                int palSlot = 1;
                int size = info.ScrDataSize;

                int count = size / 2;
                uint minCharId = 1024;
                uint minPalId = 16;
                for (int i = 0; i < count; ++i)
                {
                    uint data = screenDataValues[i];
                    uint charId = data & 0x3FF;
                    if (charId < minCharId)
                    {
                        minCharId = charId;
                    }
                    uint palId = (data & 0xF000) >> 12;
                    if (palId < minPalId)
                    {
                        minPalId = palId;
                    }
                }
                for (int j = 0; j < count;)
                {
                    uint val = screenDataValues[j++];
                    uint curFlip = val & 0xC00;
                    uint curPalId = (val & 0xF000) >> 12;
                    uint curCharId = val & 0x3FF;
                    long newPalId = palSlot + curPalId - minPalId;
                    long newCharId = charSlot + curCharId - minCharId;
                    vramStuff.Add((ushort)(curCharId | curFlip | (curPalId << 12)));
                }

                IReadOnlyList<ScreenData> screenData = screenDataValues.Select(v => new ScreenData(v)).ToList();

                // every character's data is 32 bytes big -- each byte contains palette index data for two pixels
                // (going left to right, top to bottom) -- each row is 4 bytes (4 * 2 = 8 pixels)

                var characters = new List<List<ColorRgba>>();
                Debug.Assert(characterData.Count % 32 == 0);
                for (int i = 0; i < characterData.Count / 32; i++)
                {
                    var character = new List<ColorRgba>();
                    for (int y = 0; y < 8; y++)
                    {
                        for (int x = 0; x < 4; x++)
                        {
                            byte data = characterData[i * 32 + y * 4 + x];
                            int index1 = data & 0xF;
                            character.Add(index1 == 0 ? new ColorRgba() : new ColorRgba(paletteData[index1]));
                            int index2 = (data & 0xF0) >> 4;
                            character.Add(index2 == 0 ? new ColorRgba() : new ColorRgba(paletteData[index2]));
                        }
                    }
                    characters.Add(character);
                }

                ushort width = (ushort)(info.CharsX * 8);
                ushort height = (ushort)(info.CharsY * 8);
                var texture = new ColorRgba[width * height];
                for (int cy = 0; cy < info.CharsY; cy++)
                {
                    for (int cx = 0; cx < info.CharsX; cx++)
                    {
                        int idx = cy * (info.CharsX > 32 ? info.CharsX / 2 : info.CharsX)
                            + ((cx / 32) == 1 ? 0x400 + (cx - 32) : cx);
                        ScreenData screen = screenData[idx];
                        Debug.Assert(screen.PaletteId == 0);
                        List<ColorRgba> character = characters[screen.CharacterId];
                        int start = cy * info.CharsX * 8 * 8 + cx * 8;
                        for (int py = 0; py < 8; py++)
                        {
                            int iy = py;
                            if (screen.FlipVertical)
                            {
                                iy = 7 - py;
                            }
                            for (int px = 0; px < 8; px++)
                            {
                                int ix = px;
                                if (screen.FlipHorizontal)
                                {
                                    ix = 7 - px;
                                }
                                ColorRgba pixel = character[iy * 8 + ix];
                                int index = start + py * info.CharsX * 8 + px;
                                texture[index] = pixel;
                            }
                        }
                    }
                }
                string name = file.Replace("/", "--");
                if (exportChars)
                {
                    string directory = Paths.Combine(Paths.Export, @"_2D\Layers", name);
                    Directory.CreateDirectory(directory);
                    Export.Images.SaveTexture(directory, name, width, height, texture);
                    directory = Paths.Combine(directory, "Characters");
                    Directory.CreateDirectory(directory);
                    for (int i = 0; i < characters.Count; i++)
                    {
                        List<ColorRgba> character = characters[i];
                        string filename = $"{i.ToString().PadLeft(3, '0')}";
                        Export.Images.SaveTexture(directory, filename, 8, 8, character);
                    }
                }
                else if (exportScreens)
                {
                    string directory = Paths.Combine(Paths.Export, @"_2D\Layers");
                    Directory.CreateDirectory(directory);
                    Export.Images.SaveTexture(directory, name, width, height, texture);
                }
                offset += info.ScrDataSize;
                Debug.Assert(Read.DoOffsets<byte>(bytes, offset, bytes.Length - offset).All(x => x == 0));
                Nop();
            }
            Nop();
        }

        public static void Nop()
        {
        }
    }
}
