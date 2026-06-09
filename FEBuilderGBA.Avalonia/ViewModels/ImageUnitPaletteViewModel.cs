using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageUnitPaletteViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 16;

        /// <summary>Byte stride of one battle-animation list record (WF N_Init).</summary>
        const uint AnimeRecordStride = 32;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _id0, _id1, _id2, _id3, _id4, _id5, _id6, _id7, _id8, _id9, _id10, _id11;
        uint _palettePointer;
        string _identifierName = "";
        int _selectedPaletteSlot;   // 1-based unit-palette slot (WF AddressList.SelectedIndex + 1)
        int _paletteTypeIndex;      // sub-palette / SwapPalette index (WF PaletteIndexComboBox)
        uint _classId;              // class whose battle anime feeds the sample preview
        string _className = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        /// <summary>
        /// 1-based unit-palette slot of the selected entry — the WF
        /// <c>paletteno = AddressList.SelectedIndex + 1</c> that becomes the
        /// <c>custompalette</c> override in <c>DrawBattleAnime</c>. 0 = none selected.
        /// </summary>
        public int SelectedPaletteSlot { get => _selectedPaletteSlot; set => SetField(ref _selectedPaletteSlot, value); }

        /// <summary>
        /// Active sub-palette (palette-type) index — the WF
        /// <c>paletteIndex = PaletteIndexComboBox.SelectedIndex</c> (SwapPalette).
        /// Independent of <see cref="SelectedPaletteSlot"/>.
        /// </summary>
        public int PaletteTypeIndex { get => _paletteTypeIndex; set => SetField(ref _paletteTypeIndex, value); }

        /// <summary>Class whose battle animation is rendered in the sample preview.</summary>
        public uint ClassID { get => _classId; set => SetField(ref _classId, value); }

        /// <summary>Resolved class name for <see cref="ClassID"/> (display only).</summary>
        public string ClassName { get => _className; set => SetField(ref _className, value); }

        // B0-B11: Identifier string bytes (12 chars)
        public uint Id0 { get => _id0; set => SetField(ref _id0, value); }
        public uint Id1 { get => _id1; set => SetField(ref _id1, value); }
        public uint Id2 { get => _id2; set => SetField(ref _id2, value); }
        public uint Id3 { get => _id3; set => SetField(ref _id3, value); }
        public uint Id4 { get => _id4; set => SetField(ref _id4, value); }
        public uint Id5 { get => _id5; set => SetField(ref _id5, value); }
        public uint Id6 { get => _id6; set => SetField(ref _id6, value); }
        public uint Id7 { get => _id7; set => SetField(ref _id7, value); }
        public uint Id8 { get => _id8; set => SetField(ref _id8, value); }
        public uint Id9 { get => _id9; set => SetField(ref _id9, value); }
        public uint Id10 { get => _id10; set => SetField(ref _id10, value); }
        public uint Id11 { get => _id11; set => SetField(ref _id11, value); }

        // Decoded identifier name from B0-B11
        public string IdentifierName { get => _identifierName; set => SetField(ref _identifierName, value); }

        // P12: Palette data pointer (raw GBA pointer for write-back; converted only at read time)
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        // ----- Palette RGB editor channels (16 colors x 0-31 each) -----
        readonly uint[] _r = new uint[16];
        readonly uint[] _g = new uint[16];
        readonly uint[] _b = new uint[16];

        public uint[] RChannel => _r;
        public uint[] GChannel => _g;
        public uint[] BChannel => _b;

        /// <summary>
        /// Load the list of unit-palette rows from the currently-active ROM
        /// (<see cref="CoreState.ROM"/>). Matches WinForms
        /// <c>ImageUnitPaletteForm.Init()</c> row-acceptance: a row is accepted
        /// when its P12 slot holds a valid GBA pointer, OR P12 is zero AND the
        /// 12-byte name is non-empty (i.e. <c>rom.u32(addr+0) != 0</c>). Both
        /// P12 and name being zero is the terminator.
        /// </summary>
        public List<AddrResult> LoadList() => LoadList(CoreState.ROM);

        /// <summary>
        /// Test-friendly overload that scans <paramref name="rom"/> directly
        /// instead of reading <see cref="CoreState.ROM"/>. Allows synthetic-ROM
        /// parity tests to remain deterministic when xUnit collections run in
        /// parallel and another test may transiently mutate
        /// <see cref="CoreState.ROM"/> between the test's set and read points.
        /// </summary>
        public List<AddrResult> LoadList(ROM? rom)
        {
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.image_unit_palette_pointer;
            if (pointer == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;
                uint p = rom.u32(addr + 12);
                uint nameFirst = rom.u32(addr + 0);

                if (U.isPointer(p))
                {
                    // Valid row: read identifier and emit.
                }
                else if (p == 0 && nameFirst != 0)
                {
                    // Valid row per WF parity: P12==0 but name is non-empty.
                }
                else
                {
                    // Terminator (both p==0 and name==0) or invalid pointer.
                    break;
                }

                string ident = "";
                for (int j = 0; j < 12; j++)
                {
                    byte b = rom.Data[addr + (uint)j];
                    if (b >= 0x20 && b < 0x7F) ident += (char)b;
                    else if (b == 0) break;
                }
                result.Add(new AddrResult(addr, $"0x{i:X2} {ident}", (uint)i));
            }
            result.Add(new AddrResult(0, "Unit Palette Editor", 0));
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            Id0 = rom.u8(addr + 0);
            Id1 = rom.u8(addr + 1);
            Id2 = rom.u8(addr + 2);
            Id3 = rom.u8(addr + 3);
            Id4 = rom.u8(addr + 4);
            Id5 = rom.u8(addr + 5);
            Id6 = rom.u8(addr + 6);
            Id7 = rom.u8(addr + 7);
            Id8 = rom.u8(addr + 8);
            Id9 = rom.u8(addr + 9);
            Id10 = rom.u8(addr + 10);
            Id11 = rom.u8(addr + 11);
            PalettePointer = rom.u32(addr + 12);

            // Decode identifier as ASCII string
            var chars = new char[12];
            for (int i = 0; i < 12; i++)
            {
                uint b = rom.u8(addr + (uint)i);
                chars[i] = (b >= 0x20 && b < 0x7F) ? (char)b : '.';
            }
            IdentifierName = new string(chars).TrimEnd();

            // Load palette RGB channels from the LZ77 stream at P12 (when valid).
            LoadPaletteFromROM(PalettePointer);

            IsLoaded = true;
            CanWrite = true;
        }

        /// <summary>
        /// Decode the 16-color LZ77 palette referenced by the GBA pointer.
        /// Converts the raw 0x08xxxxxx pointer to a ROM offset via
        /// <see cref="U.toOffset"/> before passing to <see cref="LZ77.decompress"/>.
        /// On invalid/missing pointer, fills the channels with zero.
        /// </summary>
        public void LoadPaletteFromROM(uint palettePointer)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) { ClearPalette(); return; }
            if (!U.isPointer(palettePointer)) { ClearPalette(); return; }
            uint offset = U.toOffset(palettePointer);
            if (!U.isSafetyOffset(offset, rom)) { ClearPalette(); return; }
            if (LZ77.getCompressedSize(rom.Data, offset) == 0) { ClearPalette(); return; }
            byte[] raw = LZ77.decompress(rom.Data, offset);
            if (raw == null || raw.Length < 32) { ClearPalette(); return; }
            for (int i = 0; i < 16; i++)
            {
                ushort c = (ushort)(raw[i * 2] | (raw[i * 2 + 1] << 8));
                _r[i] = (uint)(c & 0x1F);
                _g[i] = (uint)((c >> 5) & 0x1F);
                _b[i] = (uint)((c >> 10) & 0x1F);
            }
            OnPropertyChanged(nameof(RChannel));
            OnPropertyChanged(nameof(GChannel));
            OnPropertyChanged(nameof(BChannel));
        }

        void ClearPalette()
        {
            for (int i = 0; i < 16; i++) { _r[i] = 0; _g[i] = 0; _b[i] = 0; }
            OnPropertyChanged(nameof(RChannel));
            OnPropertyChanged(nameof(GChannel));
            OnPropertyChanged(nameof(BChannel));
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u8(addr + 0, Id0);
            rom.write_u8(addr + 1, Id1);
            rom.write_u8(addr + 2, Id2);
            rom.write_u8(addr + 3, Id3);
            rom.write_u8(addr + 4, Id4);
            rom.write_u8(addr + 5, Id5);
            rom.write_u8(addr + 6, Id6);
            rom.write_u8(addr + 7, Id7);
            rom.write_u8(addr + 8, Id8);
            rom.write_u8(addr + 9, Id9);
            rom.write_u8(addr + 10, Id10);
            rom.write_u8(addr + 11, Id11);
            rom.write_u32(addr + 12, PalettePointer);
        }

        /// <summary>
        /// Render the class battle-anime sample-preview grid for the currently
        /// selected unit-palette slot (<see cref="SelectedPaletteSlot"/>), the
        /// active sub-palette (<see cref="PaletteTypeIndex"/>), and the chosen
        /// class (<see cref="ClassID"/>). Convenience wrapper over the explicit
        /// overload that reads the VM's current state.
        /// </summary>
        public IImage RenderClassSamplePreview()
            => RenderClassSamplePreview((int)ClassID, SelectedPaletteSlot, PaletteTypeIndex);

        /// <summary>
        /// Render the class battle-anime sample-preview grid, recolored with the
        /// UNIT palette at slot <paramref name="paletteno"/> (NOT the anime's own
        /// palette) and sub-palette <paramref name="paletteIndex"/>. Mirrors WinForms
        /// <c>ImageUnitPaletteForm.DrawSample(GetAnimeIDByClassID(classID), paletteno, paletteIndex)</c>:
        ///   1. resolve the class's battle-anime ID via
        ///      <see cref="ClassFormCore.GetAnimeIDByClassID"/> (the WF
        ///      <c>p32 + u16(ptr+2)</c> chain, FE6 <c>+48</c> / FE7-8 <c>+52</c>);
        ///   2. convert that 1-based anime ID to the record offset
        ///      (<c>base + (id-1)*0x20</c>, the WF <c>DrawBattleAnime id-1 / N_Init</c>
        ///      indexing);
        ///   3. resolve the UNIT-palette override address via
        ///      <see cref="BattleAnimeRendererCore.GetUnitPaletteAddr"/> (the WF
        ///      <c>GetPaletteAddr(paletteno)</c> = <c>p32(IDToAddr(paletteno-1)+12)</c>);
        ///   4. render via the palette-override overload of
        ///      <see cref="BattleAnimeRendererCore.RenderSampleBattleAnime(uint,int,uint,byte[])"/>,
        ///      so the BASE is the unit palette, not the anime palette.
        ///
        /// <para><b>Live-recolor (#1022):</b> when <paramref name="editedPaletteBlock"/>
        /// is a valid EXACT 32-byte block (the view's in-memory R/G/B spinners), it
        /// is forwarded to the renderer's 4th param and used DIRECTLY as the
        /// palette — the cross-platform mirror of WF
        /// <c>ImageUnitPaletteForm.OnChangeColor</c> (ImageUnitPaletteForm.cs:321-333),
        /// which live-recolors the preview as the user edits colors. When
        /// <paramref name="editedPaletteBlock"/> is null (the parameterless overload,
        /// or the previewed sub-palette is NOT the editable block), the SAVED on-ROM
        /// unit palette is rendered exactly as before.</para>
        ///
        /// <para>Null-safe: returns null on null ROM/ImageService, an
        /// unresolvable class / anime / palette slot, or a blank render — the
        /// caller (view) treats null as "clear preview".</para>
        /// </summary>
        /// <param name="classID">Class whose battle anime to render.</param>
        /// <param name="paletteno">1-based unit-palette slot (the WF
        /// <c>AddressList.SelectedIndex + 1</c> custompalette override).</param>
        /// <param name="paletteIndex">Sub-palette / SwapPalette index.</param>
        /// <param name="editedPaletteBlock">Optional EXACT 32-byte live-edited
        /// palette block (#1022). Forwarded to the renderer's 4th param; null =
        /// render the saved on-ROM palette.</param>
        public IImage RenderClassSamplePreview(int classID, int paletteno, int paletteIndex,
            byte[] editedPaletteBlock = null)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || rom.RomInfo == null || CoreState.ImageService == null) return null;
            if (classID <= 0 || paletteno <= 0) return null;

            // 1. class -> battle-anime ID (WF GetAnimeIDByClassID). 0 = unresolvable.
            uint animeId = ClassFormCore.GetAnimeIDByClassID(rom, classID);
            if (animeId == 0) return null;

            // 2. anime ID -> record offset (WF DrawBattleAnime: id-1 then
            //    N_Init.IDToAddr = p32(image_battle_animelist_pointer) + (id-1)*0x20).
            uint listPointer = rom.RomInfo.image_battle_animelist_pointer;
            if (listPointer == 0) return null;
            uint listBase = rom.p32(listPointer);
            if (!U.isSafetyOffset(listBase, rom)) return null;
            uint recordOffset = listBase + (animeId - 1) * AnimeRecordStride;
            if (!U.isSafetyOffset(recordOffset, rom)) return null;

            // 3. unit-palette override address (WF GetPaletteAddr(paletteno)).
            //    NOT_FOUND => no valid override; fall back to 0 so the render uses
            //    the anime's own palette rather than crashing (the WF
            //    `if (U.isSafetyOffset(addr)) palettes = p` guard).
            uint paletteOverride = BattleAnimeRendererCore.GetUnitPaletteAddr(rom, paletteno);
            if (paletteOverride == U.NOT_FOUND) paletteOverride = 0;

            // 4. render with the UNIT palette override + the paletteIndex sub-palette.
            //    #1022: forward the live-edited 32-byte block (or null) so an
            //    in-progress R/G/B edit recolors the preview directly.
            return BattleAnimeRendererCore.RenderSampleBattleAnime(
                recordOffset, paletteIndex, paletteOverride, editedPaletteBlock);
        }

        public int GetListCount() => LoadList().Count;

        /// <summary>Get the base address of the unit-palette table as a hex string, or "" if unavailable.</summary>
        public string LoadListBaseAddress()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return "";
            uint pointer = rom.RomInfo.image_unit_palette_pointer;
            if (pointer == 0) return "";
            uint baseAddr = rom.p32(pointer);
            return $"0x{baseAddr:X08}";
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["Identifier"] = IdentifierName,
                ["Id0"] = $"0x{Id0:X02}",
                ["Id1"] = $"0x{Id1:X02}",
                ["Id2"] = $"0x{Id2:X02}",
                ["Id3"] = $"0x{Id3:X02}",
                ["Id4"] = $"0x{Id4:X02}",
                ["Id5"] = $"0x{Id5:X02}",
                ["Id6"] = $"0x{Id6:X02}",
                ["Id7"] = $"0x{Id7:X02}",
                ["Id8"] = $"0x{Id8:X02}",
                ["Id9"] = $"0x{Id9:X02}",
                ["Id10"] = $"0x{Id10:X02}",
                ["Id11"] = $"0x{Id11:X02}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();

            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@1"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@3"] = $"0x{rom.u8(a + 3):X02}",
                ["u8@4"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@5"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@6"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@7"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@8"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@9"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@10"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@11"] = $"0x{rom.u8(a + 11):X02}",
                ["u32@12"] = $"0x{rom.u32(a + 12):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["Id0"] = "u8@0",
            ["Id1"] = "u8@1",
            ["Id2"] = "u8@2",
            ["Id3"] = "u8@3",
            ["Id4"] = "u8@4",
            ["Id5"] = "u8@5",
            ["Id6"] = "u8@6",
            ["Id7"] = "u8@7",
            ["Id8"] = "u8@8",
            ["Id9"] = "u8@9",
            ["Id10"] = "u8@10",
            ["Id11"] = "u8@11",
            ["PalettePointer"] = "u32@12",
        };
    }
}
