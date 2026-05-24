using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Class OP Demo Editor ViewModel — Avalonia parity for orphan
    /// WinForms `ClassOPDemoForm`. Rebuilt for gap-sweep #405.
    ///
    /// The WinForms peer `ClassOPDemoForm.cs` is `<Compile Remove>`'d from
    /// the build but its `Designer.cs` is parsed by the gap-sweep tooling
    /// — so this ViewModel matches the orphan surface, not the canonical
    /// `OPClassDemoForm` (which lives in `OPClassDemoViewerViewModel`).
    ///
    /// Differences from `OPClassDemoViewerViewModel`:
    ///   - N1 (JP-name font glyphs) has NO 16-entry cap — orphan validator
    ///     stops only at the 0xFF terminator.
    ///   - N2 (anime spec tuple) is a SINGLE 6-byte tuple, not a repeating
    ///     (Cmd, Arg) command stream. The orphan validator is `i < 1`.
    ///   - No patch-aware UI (orphan constructor has no PatchUtil calls).
    ///   - The orphan's `N1_AddressListExpandsButton` expands the N1
    ///     sub-block (not the main table).
    ///
    /// Pointer-aware fields use `EditorFormRef.FieldType.Pointer` so
    /// `rom.write_p32`/`rom.p32` apply the `0x08000000` high bit.
    /// </summary>
    public class ClassOPDemoViewModel : ViewModelBase, IDataVerifiable
    {
        // Field map mirrors orphan ClassOPDemoForm: P0/D4/P8/B12..B17/D18/B22/B23/P24.
        // Pointer fields (P0=English name, P8=Japanese name, P24=Anime spec)
        // route through `rom.p32`/`rom.write_p32`.
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[]
            {
                "P0", "D4", "P8",
                "B12", "B13", "B14", "B15", "B16", "B17",
                "D18", "B22", "B23",
                "P24",
            });

        uint _currentAddr;
        bool _isLoaded;
        uint _p0;
        uint _d4;
        uint _p8;
        uint _b12;
        uint _b13;
        uint _b14;
        uint _b15;
        uint _b16;
        uint _b17;
        uint _d18;
        uint _b22;
        uint _b23;
        uint _p24;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint D4 { get => _d4; set => SetField(ref _d4, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        public uint B12 { get => _b12; set => SetField(ref _b12, value); }
        public uint B13 { get => _b13; set => SetField(ref _b13, value); }
        public uint B14 { get => _b14; set => SetField(ref _b14, value); }
        public uint B15 { get => _b15; set => SetField(ref _b15, value); }
        public uint B16 { get => _b16; set => SetField(ref _b16, value); }
        public uint B17 { get => _b17; set => SetField(ref _b17, value); }
        public uint D18 { get => _d18; set => SetField(ref _d18, value); }
        public uint B22 { get => _b22; set => SetField(ref _b22, value); }
        public uint B23 { get => _b23; set => SetField(ref _b23, value); }
        public uint P24 { get => _p24; set => SetField(ref _p24, value); }

        // -----------------------------------------------------------------
        // Main table walkers (Class OP Demo entries, 28 bytes each).
        // -----------------------------------------------------------------

        public List<AddrResult> LoadList() => LoadClassOPDemoList();
        public void LoadEntry(uint addr) => LoadClassOPDemo(addr);

        public List<AddrResult> LoadClassOPDemoList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptrAddr = rom.RomInfo.op_class_demo_pointer;
            if (ptrAddr == 0) return new List<AddrResult>();
            if (!U.isSafetyOffset(ptrAddr)) return new List<AddrResult>();

            // Single dereference, matching the canonical OPClassDemoForm
            // InputFormRef path and ListParityHelper.BuildClassOPDemoList.
            uint baseAddr = rom.p32(ptrAddr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 28);
                if (addr + 28 > (uint)rom.Data.Length) break;
                if (!U.isPointer(rom.u32(addr))) break;

                uint cid = rom.u8(addr + 14);
                string name = U.ToHexString(i) + " " + ClassFormBridge.GetClassName(cid);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadClassOPDemo(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 28 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _fields);
            P0 = v["P0"];
            D4 = v["D4"];
            P8 = v["P8"];
            B12 = v["B12"];
            B13 = v["B13"];
            B14 = v["B14"];
            B15 = v["B15"];
            B16 = v["B16"];
            B17 = v["B17"];
            D18 = v["D18"];
            B22 = v["B22"];
            B23 = v["B23"];
            P24 = v["P24"];
            IsLoaded = true;
        }

        /// <summary>
        /// Persist all 13 main fields. Caller must wrap in
        /// `_undoService.Begin / Commit` for undo support.
        /// </summary>
        public void WriteClassOPDemo()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint>
            {
                ["P0"] = P0, ["D4"] = D4, ["P8"] = P8,
                ["B12"] = B12, ["B13"] = B13, ["B14"] = B14, ["B15"] = B15,
                ["B16"] = B16, ["B17"] = B17,
                ["D18"] = D18, ["B22"] = B22, ["B23"] = B23,
                ["P24"] = P24,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadClassOPDemoList().Count;

        // -----------------------------------------------------------------
        // N1 sub-list — JP name font glyphs.
        // Block size 1 byte, 0xFF terminator, NO 16-entry cap (orphan-faithful).
        // -----------------------------------------------------------------

        /// <summary>One row of the JP-name font glyph sub-list.</summary>
        public sealed class N1Row
        {
            public uint Addr { get; init; }
            public uint Index { get; init; }
            public uint GlyphId { get; init; }
        }

        /// <summary>Sanity cap to prevent runaway scans on hostile ROMs.</summary>
        public const int N1SafetyCap = 4096;

        /// <summary>
        /// Walk the JP-name font sub-list at the dereferenced pointer.
        /// Stops at the first 0xFF, or at the ROM-bounds, or after
        /// <see cref="N1SafetyCap"/> entries. Returns empty for invalid input.
        /// </summary>
        public List<N1Row> LoadN1FontList(uint pointerSlotAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new List<N1Row>();
            if (pointerSlotAddr == 0 || pointerSlotAddr + 4 > (uint)rom.Data.Length)
                return new List<N1Row>();
            uint baseAddr = rom.p32(pointerSlotAddr);
            return LoadN1FontListFromOffset(baseAddr);
        }

        /// <summary>
        /// Walk the JP-name font sub-list from an already-dereferenced ROM
        /// offset. Used when previewing an unsaved spinner edit (mirrors
        /// the PR #544 Copilot-bot fix `PRRT_kwDOH0Mc1M6ETj_F`).
        /// </summary>
        public List<N1Row> LoadN1FontListFromOffset(uint baseAddr)
        {
            var result = new List<N1Row>();
            ROM rom = CoreState.ROM;
            if (rom == null) return result;
            if (!U.isSafetyOffset(baseAddr)) return result;

            for (int i = 0; i < N1SafetyCap; i++)
            {
                uint addr = baseAddr + (uint)i;
                if (addr >= (uint)rom.Data.Length) break;
                uint b = rom.u8(addr);
                if (b == 0xFF) break;
                result.Add(new N1Row { Addr = addr, Index = (uint)i, GlyphId = b });
            }
            return result;
        }

        /// <summary>
        /// Write a single byte at the given N1 row address. Caller wraps
        /// in `_undoService.Begin / Commit`.
        /// </summary>
        public void WriteN1Entry(uint addr, uint glyphId)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 1 > (uint)rom.Data.Length) return;
            rom.write_u8(addr, glyphId);
        }

        /// <summary>
        /// Expand the N1 (JP-name font) sub-block at the pointer slot.
        /// Mirrors the orphan's `N1_AddressListExpandsButton`.
        /// </summary>
        public DataExpansionCore.ExpandResult ExpandN1Block(uint pointerSlotAddr, uint currentCount)
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
                return new DataExpansionCore.ExpandResult { Success = false, Error = "ROM not loaded." };
            return DataExpansionCore.ExpandTable(rom, pointerSlotAddr, entrySize: 1, currentCount);
        }

        // -----------------------------------------------------------------
        // N2 single 6-byte tuple — anime spec.
        // Orphan validator is `i < 1` (exactly ONE row). Bytes:
        //   B0 = 0x05 fixed (start marker)
        //   B1 = wait frames (display /60 = seconds)
        //   B2 = anime special spec (combo: 1=Normal, 2=Critical, 3=Ranged/MagicSword)
        //   B3 = 0x00 fixed
        //   B4 = ?? (unknown semantics)
        //   B5 = 0x00 fixed
        // -----------------------------------------------------------------

        /// <summary>The single 6-byte tuple stored at the dereferenced P24.</summary>
        public readonly struct N2Tuple
        {
            public uint Addr { get; init; }
            public uint B0 { get; init; }
            public uint B1 { get; init; }
            public uint B2 { get; init; }
            public uint B3 { get; init; }
            public uint B4 { get; init; }
            public uint B5 { get; init; }
        }

        /// <summary>
        /// Read the anime-spec tuple at the dereferenced pointer slot.
        /// Returns null when the pointer is invalid.
        /// </summary>
        public N2Tuple? LoadN2Tuple(uint pointerSlotAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;
            if (pointerSlotAddr == 0 || pointerSlotAddr + 4 > (uint)rom.Data.Length) return null;
            uint baseAddr = rom.p32(pointerSlotAddr);
            return LoadN2TupleFromOffset(baseAddr);
        }

        /// <summary>
        /// Read the anime-spec tuple from an already-dereferenced ROM offset.
        /// Used to preview an unsaved spinner edit.
        /// </summary>
        public N2Tuple? LoadN2TupleFromOffset(uint baseAddr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;
            if (!U.isSafetyOffset(baseAddr)) return null;
            if (baseAddr + 6 > (uint)rom.Data.Length) return null;

            return new N2Tuple
            {
                Addr = baseAddr,
                B0 = rom.u8(baseAddr + 0),
                B1 = rom.u8(baseAddr + 1),
                B2 = rom.u8(baseAddr + 2),
                B3 = rom.u8(baseAddr + 3),
                B4 = rom.u8(baseAddr + 4),
                B5 = rom.u8(baseAddr + 5),
            };
        }

        /// <summary>
        /// Write all 6 bytes of the anime-spec tuple. Caller wraps in
        /// `_undoService.Begin / Commit`.
        /// </summary>
        public void WriteN2Tuple(uint baseAddr, uint b0, uint b1, uint b2, uint b3, uint b4, uint b5)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (baseAddr + 6 > (uint)rom.Data.Length) return;
            rom.write_u8(baseAddr + 0, b0);
            rom.write_u8(baseAddr + 1, b1);
            rom.write_u8(baseAddr + 2, b2);
            rom.write_u8(baseAddr + 3, b3);
            rom.write_u8(baseAddr + 4, b4);
            rom.write_u8(baseAddr + 5, b5);
        }

        // -----------------------------------------------------------------
        // Anime Spec Shared — scan the main table for sibling entries that
        // share the same P24 (anime-spec pointer offset). Mirrors the WF
        // ANIME_LIST "アニメ指定共有" read-only list.
        // -----------------------------------------------------------------

        /// <summary>
        /// Return main-table entries (excluding <paramref name="currentEntryAddr"/>)
        /// whose P24 equals <paramref name="p24Offset"/>. The offset is the
        /// rom-only address (no 0x08000000 high bit).
        /// </summary>
        public List<AddrResult> LoadAnimeSharedList(uint p24Offset, uint currentEntryAddr)
        {
            var siblings = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom == null) return siblings;

            foreach (var entry in LoadClassOPDemoList())
            {
                if (entry.addr == currentEntryAddr) continue;
                uint entryP24 = rom.p32(entry.addr + 24);
                if (entryP24 == p24Offset)
                {
                    siblings.Add(entry);
                }
            }
            return siblings;
        }

        // -----------------------------------------------------------------
        // IDataVerifiable
        // -----------------------------------------------------------------

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
                ["D4"] = $"0x{D4:X08}",
                ["P8"] = $"0x{P8:X08}",
                ["B12"] = $"0x{B12:X02}",
                ["B13"] = $"0x{B13:X02}",
                ["B14"] = $"0x{B14:X02}",
                ["B15"] = $"0x{B15:X02}",
                ["B16"] = $"0x{B16:X02}",
                ["B17"] = $"0x{B17:X02}",
                ["D18"] = $"0x{D18:X08}",
                ["B22"] = $"0x{B22:X02}",
                ["B23"] = $"0x{B23:X02}",
                ["P24"] = $"0x{P24:X08}",
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
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
                ["u8@0x10"] = $"0x{rom.u8(a + 16):X02}",
                ["u8@0x11"] = $"0x{rom.u8(a + 17):X02}",
                ["u32@0x12"] = $"0x{rom.u32(a + 18):X08}",
                ["u8@0x16"] = $"0x{rom.u8(a + 22):X02}",
                ["u8@0x17"] = $"0x{rom.u8(a + 23):X02}",
                ["u32@0x18"] = $"0x{rom.u32(a + 24):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["P0"] = "u32@0x00",
            ["D4"] = "u32@0x04",
            ["P8"] = "u32@0x08",
            ["B12"] = "u8@0x0C",
            ["B13"] = "u8@0x0D",
            ["B14"] = "u8@0x0E",
            ["B15"] = "u8@0x0F",
            ["B16"] = "u8@0x10",
            ["B17"] = "u8@0x11",
            ["D18"] = "u32@0x12",
            ["B22"] = "u8@0x16",
            ["B23"] = "u8@0x17",
            ["P24"] = "u32@0x18",
        };
    }

    /// <summary>
    /// Thin shim around <see cref="NameResolver.GetClassName"/> that
    /// swallows any resolution exception and falls back to a hex display
    /// string. Kept as a separate type so the ViewModel does not take a
    /// hard reference on WinForms-only ClassForm helpers, and so the
    /// fallback behaviour is auditable in one place.
    /// </summary>
    internal static class ClassFormBridge
    {
        public static string GetClassName(uint id)
        {
            try { return NameResolver.GetClassName(id); }
            catch { return $"Class 0x{id:X02}"; }
        }
    }
}
