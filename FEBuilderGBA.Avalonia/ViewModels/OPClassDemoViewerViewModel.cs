using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class OPClassDemoViewerViewModel : ViewModelBase, IDataVerifiable
    {
        // Pointer fields P0/P8/P24 use `rom.p32`/`write_p32` semantics
        // — store ROM offset (without the 0x08000000 GBA bit). This
        // mirrors WinForms `OPClassDemoForm` which uses InputFormRef
        // P-prefix fields for the same slots. (Copilot CLI re-review
        // on PR #544 flagged D8/D24 as bypassing pointer semantics.)
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "P0", "D4", "P8", "B12", "B13", "B14", "B15", "B16", "B17", "D18", "B22", "B23", "P24" });

        uint _currentAddr;
        bool _canWrite;
        uint _englishNamePointer;
        uint _descriptionTextId;
        uint _japaneseNamePointer;
        uint _japaneseNameLength;
        uint _paletteId;
        uint _displayWeapon;
        uint _allyEnemyColor;
        uint _battleAnime;
        uint _magicEffect;
        uint _unknown18;
        uint _terrainLeft;
        uint _terrainRight;
        uint _animePointer;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint EnglishNamePointer { get => _englishNamePointer; set => SetField(ref _englishNamePointer, value); }
        public uint DescriptionTextId { get => _descriptionTextId; set => SetField(ref _descriptionTextId, value); }
        public uint JapaneseNamePointer { get => _japaneseNamePointer; set => SetField(ref _japaneseNamePointer, value); }
        public uint JapaneseNameLength { get => _japaneseNameLength; set => SetField(ref _japaneseNameLength, value); }
        public uint PaletteId { get => _paletteId; set => SetField(ref _paletteId, value); }
        public uint DisplayWeapon { get => _displayWeapon; set => SetField(ref _displayWeapon, value); }
        public uint AllyEnemyColor { get => _allyEnemyColor; set => SetField(ref _allyEnemyColor, value); }
        public uint BattleAnime { get => _battleAnime; set => SetField(ref _battleAnime, value); }
        public uint MagicEffect { get => _magicEffect; set => SetField(ref _magicEffect, value); }
        public uint Unknown18 { get => _unknown18; set => SetField(ref _unknown18, value); }
        public uint TerrainLeft { get => _terrainLeft; set => SetField(ref _terrainLeft, value); }
        public uint TerrainRight { get => _terrainRight; set => SetField(ref _terrainRight, value); }
        public uint AnimePointer { get => _animePointer; set => SetField(ref _animePointer, value); }

        public List<AddrResult> LoadOPClassDemoList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();

            // Single dereference: OPClassDemoForm (used from MainFE8Form) passes
            // op_class_demo_pointer directly to InputFormRef, which does one p32.
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;

                // First dword should be a valid pointer
                uint p0 = rom.u32(addr);
                if (!U.isPointer(p0)) break;

                uint cid = rom.u8(addr + 14);
                string className = NameResolver.GetClassName(cid);
                string name = $"{U.ToHexString(i)} {className}";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadOPClassDemo(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            EnglishNamePointer = v["P0"];
            DescriptionTextId = v["D4"];
            JapaneseNamePointer = v["P8"];
            JapaneseNameLength = v["B12"];
            PaletteId = v["B13"];
            DisplayWeapon = v["B14"];
            AllyEnemyColor = v["B15"];
            BattleAnime = v["B16"];
            MagicEffect = v["B17"];
            Unknown18 = v["D18"];
            TerrainLeft = v["B22"];
            TerrainRight = v["B23"];
            AnimePointer = v["P24"];
            CanWrite = true;
        }

        public void WriteOPClassDemo()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["P0"] = EnglishNamePointer, ["D4"] = DescriptionTextId, ["P8"] = JapaneseNamePointer,
                ["B12"] = JapaneseNameLength, ["B13"] = PaletteId, ["B14"] = DisplayWeapon,
                ["B15"] = AllyEnemyColor, ["B16"] = BattleAnime, ["B17"] = MagicEffect,
                ["D18"] = Unknown18, ["B22"] = TerrainLeft, ["B23"] = TerrainRight, ["P24"] = AnimePointer,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadOPClassDemoList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["EnglishNamePointer"] = $"0x{EnglishNamePointer:X08}",
                ["DescriptionTextId"] = $"0x{DescriptionTextId:X08}",
                ["JapaneseNamePointer"] = $"0x{JapaneseNamePointer:X08}",
                ["JapaneseNameLength"] = $"0x{JapaneseNameLength:X02}",
                ["PaletteId"] = $"0x{PaletteId:X02}",
                ["DisplayWeapon"] = $"0x{DisplayWeapon:X02}",
                ["AllyEnemyColor"] = $"0x{AllyEnemyColor:X02}",
                ["BattleAnime"] = $"0x{BattleAnime:X02}",
                ["MagicEffect"] = $"0x{MagicEffect:X02}",
                ["Unknown18"] = $"0x{Unknown18:X08}",
                ["TerrainLeft"] = $"0x{TerrainLeft:X02}",
                ["TerrainRight"] = $"0x{TerrainRight:X02}",
                ["AnimePointer"] = $"0x{AnimePointer:X08}",
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
                ["u32@0x00_EnglishNamePointer"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04_DescriptionTextId"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08_JapaneseNamePointer"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@0x0C_JapaneseNameLength"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D_PaletteId"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E_DisplayWeapon"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F_AllyEnemyColor"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10_BattleAnime"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11_MagicEffect"] = $"0x{rom.u8(a + 17):X02}",
                ["u32@0x12_Unknown18"] = $"0x{rom.u32(a + 18):X08}",
                ["u8@0x16_TerrainLeft"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17_TerrainRight"] = $"0x{rom.u8(a + 23):X02}",
                ["u32@0x18_AnimePointer"] = $"0x{rom.u32(a + 24):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["EnglishNamePointer"] = "u32@0x00_EnglishNamePointer",
            ["DescriptionTextId"] = "u32@0x04_DescriptionTextId",
            ["JapaneseNamePointer"] = "u32@0x08_JapaneseNamePointer",
            ["JapaneseNameLength"] = "u8@0x0C_JapaneseNameLength",
            ["PaletteId"] = "u8@0x0D_PaletteId",
            ["DisplayWeapon"] = "u8@0x0E_DisplayWeapon",
            ["AllyEnemyColor"] = "u8@0x0F_AllyEnemyColor",
            ["BattleAnime"] = "u8@0x10_BattleAnime",
            ["MagicEffect"] = "u8@0x11_MagicEffect",
            ["Unknown18"] = "u32@0x12_Unknown18",
            ["TerrainLeft"] = "u8@0x16_TerrainLeft",
            ["TerrainRight"] = "u8@0x17_TerrainRight",
            ["AnimePointer"] = "u32@0x18_AnimePointer",
        };

        // -----------------------------------------------------------------
        // Sub-list walkers + patch presence flags (gap-sweep #419).
        //
        // WinForms OPClassDemoForm exposes two sub-lists:
        //   N1_ — Japanese-name font glyph IDs at the pointer P8.
        //         Block size 1 byte, max 16 entries, terminator 0xFF.
        //   N2_ — Animation command rows at the pointer P24.
        //         Block size 2 bytes (Cmd, Arg), terminator 0x00 in Cmd.
        // The walkers dereference the pointer slot first (matching WF
        // InputFormRef.ReInit semantics) so the caller can pass the
        // pointer-slot address directly.
        // -----------------------------------------------------------------

        /// <summary>Maximum entries in the N1 Japanese-name font sub-list.</summary>
        public const int N1MaxEntries = 16;

        /// <summary>Maximum entries in the N2 animation command sub-list (sanity cap).</summary>
        public const int N2MaxEntries = 256;

        /// <summary>One row of the Japanese-name font sub-list (single byte glyph ID).</summary>
        public sealed class N1Row
        {
            public uint Addr { get; init; }
            public uint Index { get; init; }
            public uint GlyphId { get; init; }
        }

        /// <summary>One row of the animation command sub-list.</summary>
        public sealed class N2Row
        {
            public uint Addr { get; init; }
            public uint Index { get; init; }
            public uint Command { get; init; }
            public uint Argument { get; init; }
        }

        /// <summary>
        /// Walk the Japanese-name font sub-list at the dereferenced pointer.
        /// Stops at the first 0xFF terminator or after <see cref="N1MaxEntries"/>.
        /// Returns an empty list if the pointer is invalid.
        /// </summary>
        public List<N1Row> LoadN1FontList(uint pointerSlotAddr)
        {
            var result = new List<N1Row>();
            ROM rom = CoreState.ROM;
            if (rom == null) return result;
            if (pointerSlotAddr == 0 || pointerSlotAddr + 4 > (uint)rom.Data.Length) return result;

            uint baseAddr = rom.p32(pointerSlotAddr);
            if (!U.isSafetyOffset(baseAddr)) return result;

            for (uint i = 0; i < N1MaxEntries; i++)
            {
                uint addr = baseAddr + i;
                if (addr >= (uint)rom.Data.Length) break;
                uint b = rom.u8(addr);
                if (b == 0xFF) break;
                result.Add(new N1Row { Addr = addr, Index = i, GlyphId = b });
            }
            return result;
        }

        /// <summary>
        /// Walk the animation command sub-list at the dereferenced pointer.
        /// Each row is (Cmd, Arg) — 2 bytes. Stops at the first 0x00 Cmd
        /// terminator or after <see cref="N2MaxEntries"/>.
        /// </summary>
        public List<N2Row> LoadN2CommandList(uint pointerSlotAddr)
        {
            var result = new List<N2Row>();
            ROM rom = CoreState.ROM;
            if (rom == null) return result;
            if (pointerSlotAddr == 0 || pointerSlotAddr + 4 > (uint)rom.Data.Length) return result;

            uint baseAddr = rom.p32(pointerSlotAddr);
            if (!U.isSafetyOffset(baseAddr)) return result;

            for (uint i = 0; i < N2MaxEntries; i++)
            {
                uint addr = baseAddr + i * 2;
                if (addr + 2 > (uint)rom.Data.Length) break;
                uint cmd = rom.u8(addr);
                if (cmd == 0x00) break;
                uint arg = rom.u8(addr + 1);
                result.Add(new N2Row { Addr = addr, Index = i, Command = cmd, Argument = arg });
            }
            return result;
        }

        /// <summary>
        /// Write a single byte (glyph ID) at the given N1 row address.
        /// Caller must wrap in a <c>ROM.BeginUndoScope</c> so the change
        /// is recorded.
        /// </summary>
        public void WriteN1Entry(uint addr, uint glyphId)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 > (uint)rom.Data.Length) return;
            rom.write_u8(addr, glyphId);
        }

        /// <summary>
        /// Write (Cmd, Arg) at the given N2 row address.
        /// Caller must wrap in a <c>ROM.BeginUndoScope</c>.
        /// </summary>
        public void WriteN2Entry(uint addr, uint cmd, uint arg)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 2 > (uint)rom.Data.Length) return;
            rom.write_u8(addr, cmd);
            rom.write_u8(addr + 1, arg);
        }

        /// <summary>
        /// True when the FE8J OPClassReelAnimationIDOver255 patch is
        /// installed. When true the WF UI swapped the B16 byte and the
        /// D18 dword field for the battle-anime ID (so the Avalonia
        /// view should mirror that swap).
        /// </summary>
        public bool IsOver255PatchActive => PatchDetection.OPClassReelAnimationIDOver255Detect(CoreState.ROM);

        /// <summary>
        /// True when the OPClassReelSort patch (FE8J or FE8U) is installed.
        /// When true the WF main-list "ListExpand" button was visible.
        /// </summary>
        public bool IsReelSortPatchActive => PatchDetection.OPClassReelSortPatchDetect(CoreState.ROM);

        /// <summary>
        /// Run a `DataExpansionCore.ExpandTable` call against the main
        /// OPClassDemo table pointer and refresh internal state.
        /// Caller is responsible for opening / committing the
        /// <c>_undoService</c> scope.
        /// </summary>
        public DataExpansionCore.ExpandResult ExpandList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
                return new DataExpansionCore.ExpandResult { Success = false, Error = "ROM not loaded." };

            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0)
                return new DataExpansionCore.ExpandResult { Success = false, Error = "op_class_demo_pointer not set." };

            uint currentCount = (uint)LoadOPClassDemoList().Count;
            return DataExpansionCore.ExpandTable(rom, ptrAddr, entrySize: 28, currentCount);
        }
    }
}
