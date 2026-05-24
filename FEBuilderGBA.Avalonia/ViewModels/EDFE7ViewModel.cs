// SPDX-License-Identifier: GPL-3.0-or-later
// EDFE7Form (Ending Demo - FE7) ViewModel - gap-sweep #403 parity raise.
//
// Mirrors WinForms `EDFE7Form` exactly. EDFE7Form has FOUR sub-surfaces (one
// more than FE8's EDForm), each operating on a different ROM data structure:
//
//   tabPage1  "Lyn Arc"  - 12-byte records at `ed_3c_pointer` USED AS
//                          DIRECT BASE (not a pointer to dereference; WF
//                          `N3_Init` constructs with pointer=0 then calls
//                          `ifr.ReInit(ed_3c_pointer)` which sets the
//                          BaseAddress directly).
//                          Term: u32 == 0 (on the UnitId field).
//                          Fields D0 (UnitId), D4 (ClearedAfter TextId),
//                          D8 (RetreatAfter TextId). All three DWords.
//                          Lyn is read/write-only - WF has NO list-expand
//                          button because the direct-base table can't be
//                          relocated through the standard expansion path.
//
//   tabPage2  "Retreat"  - 4-byte records at `ed_1_pointer`.
//                          Term: u32 == 0.
//                          Fields B0 (UnitId), B1 (Condition),
//                          B2 and B3 (unknown). FE7-specific help text on
//                          the WF N4_L_1 label exposes codes 03=Hawkeye,
//                          04=Pent and Louise, 05=Athos.
//
//   tabPage3  "Epithet"  - 8-byte records at `ed_2_pointer`.
//                          Term: u32 == 0 (FE7 uses u32, NOT u8 like FE8).
//                          Fields D0 (UnitId, 4 bytes - DISTINCT from FE8's
//                          W0!) + D4 (Epithet text id, 4 bytes). The D0 +0..+3
//                          range is the full UnitId; there are NO reserved
//                          bytes here (unlike FE8 EDView's KnownGap).
//
//   tabPage4  "Epilogue" - 8-byte records at `ed_3a_pointer` (Eliwood
//                          route) or `ed_3b_pointer` (Hector route).
//                          Term: u32 == 0.
//                          Fields B0 (PairFlag designation, 1=Solo, 2=Support,
//                          "??" otherwise), B1 (UnitId1), B2 (UnitId2),
//                          B3 (StoryFlag), D4 (Epilogue text id).
//
// Copilot CLI v1 plan review (#403) surfaced seven issues that v2
// corrected and the EDFE7ParityTests pin in place:
//   C1 - Lyn is DIRECT-BASE (ed_3c_pointer is the table base, not a
//        pointer to dereference). VM reads via `rom.RomInfo.ed_3c_pointer`
//        directly (no `rom.p32()`). NO `ExpandLynList()` method.
//   C2 - Epithet field is D0 (DWord at +0), NOT W0 like FE8.
//        Writing 4 bytes at +0 spans the full UnitId entry.
//   C3 - Terminator predicates are u32==0 for all four surfaces.
//   C4 - List Expand applies ONLY to Retreat / Epithet / Epilogue (pointer-
//        backed). Each tab uses its OWN UndoService scope name. Route-
//        specific Epilogue expand updates only `ed_3a` OR `ed_3b`.
//   C5 - Undo tests are BEHAVIORAL.
//   C6 - Helper returns both Eliwood/Hector addresses.
//   C7 - Retreat help text wired to AV `EDFE7_Retreat_HelpText_TextBlock`.
//
// Backward-compat shims preserve the original three-method surface
// (`LoadList` / `LoadEntry` / `CurrentAddr` / `IsLoaded`) so any pre-
// existing `ListParityHelper` / `INavigationTargetSource` callers keep
// working unchanged. They forward to the LYN surface (the first WF tab).
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EDFE7ViewModel : ViewModelBase, IDataVerifiable
    {
        // --- Field-set declarations (parsed once per class) ----------

        /// <summary>Lyn record: 12 bytes - D0 / D4 / D8 (three DWords).</summary>
        static readonly List<EditorFormRef.FieldDef> _lynFields =
            EditorFormRef.DetectFields(new[] { "D0", "D4", "D8" });

        /// <summary>Retreat record: 4 bytes - B0/B1/B2/B3.</summary>
        static readonly List<EditorFormRef.FieldDef> _retreatFields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3" });

        /// <summary>Epithet record: 8 bytes - D0 (DWord UnitId), D4 (DWord
        /// text id). Distinct from FE8's W0/D4 - FE7 uses D0 (DWord) so the
        /// full +0..+3 range IS the UnitId, not just +0..+1.</summary>
        static readonly List<EditorFormRef.FieldDef> _epithetFields =
            EditorFormRef.DetectFields(new[] { "D0", "D4" });

        /// <summary>Epilogue record: 8 bytes - B0/B1/B2/B3, D4.</summary>
        static readonly List<EditorFormRef.FieldDef> _epilogueFields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "D4" });

        public const uint LYN_BLOCK_SIZE = 12;
        public const uint RETREAT_BLOCK_SIZE = 4;
        public const uint EPITHET_BLOCK_SIZE = 8;
        public const uint EPILOGUE_BLOCK_SIZE = 8;

        // --- Epilogue route ------------------------------------------

        /// <summary>Which epilogue route the user is editing.</summary>
        public enum EpilogueRouteKind
        {
            /// <summary>`ed_3a_pointer` route (Eliwood).</summary>
            Eliwood,
            /// <summary>`ed_3b_pointer` route (Hector).</summary>
            Hector,
        }

        // --- Lyn state -----------------------------------------------

        uint _lynAddr;
        bool _lynCanWrite;
        uint _lynUnitId;
        uint _lynClearedTextId;
        uint _lynRetreatTextId;

        public uint LynAddr { get => _lynAddr; set => SetField(ref _lynAddr, value); }
        public bool LynCanWrite { get => _lynCanWrite; set => SetField(ref _lynCanWrite, value); }
        public uint LynUnitId { get => _lynUnitId; set => SetField(ref _lynUnitId, value); }
        public uint LynClearedTextId { get => _lynClearedTextId; set => SetField(ref _lynClearedTextId, value); }
        public uint LynRetreatTextId { get => _lynRetreatTextId; set => SetField(ref _lynRetreatTextId, value); }

        // --- Retreat state -------------------------------------------

        uint _retreatAddr;
        bool _retreatCanWrite;
        uint _retreatUnitId;
        uint _retreatCondition;
        uint _retreatB2;
        uint _retreatB3;

        public uint RetreatAddr { get => _retreatAddr; set => SetField(ref _retreatAddr, value); }
        public bool RetreatCanWrite { get => _retreatCanWrite; set => SetField(ref _retreatCanWrite, value); }
        public uint RetreatUnitId { get => _retreatUnitId; set => SetField(ref _retreatUnitId, value); }
        public uint RetreatCondition { get => _retreatCondition; set => SetField(ref _retreatCondition, value); }
        public uint RetreatB2 { get => _retreatB2; set => SetField(ref _retreatB2, value); }
        public uint RetreatB3 { get => _retreatB3; set => SetField(ref _retreatB3, value); }

        // --- Epithet state -------------------------------------------

        uint _epithetAddr;
        bool _epithetCanWrite;
        uint _epithetUnitId;
        uint _epithetTextId;

        public uint EpithetAddr { get => _epithetAddr; set => SetField(ref _epithetAddr, value); }
        public bool EpithetCanWrite { get => _epithetCanWrite; set => SetField(ref _epithetCanWrite, value); }
        public uint EpithetUnitId { get => _epithetUnitId; set => SetField(ref _epithetUnitId, value); }
        public uint EpithetTextId { get => _epithetTextId; set => SetField(ref _epithetTextId, value); }

        // --- Epilogue state ------------------------------------------

        uint _epilogueAddr;
        bool _epilogueCanWrite;
        uint _epiloguePairFlag;
        uint _epilogueUnitId1;
        uint _epilogueUnitId2;
        uint _epilogueStoryFlag;
        uint _epilogueTextId;
        EpilogueRouteKind _epilogueRoute = EpilogueRouteKind.Eliwood;
        List<AddrResult> _epilogueList = new();

        public uint EpilogueAddr { get => _epilogueAddr; set => SetField(ref _epilogueAddr, value); }
        public bool EpilogueCanWrite { get => _epilogueCanWrite; set => SetField(ref _epilogueCanWrite, value); }
        public uint EpiloguePairFlag { get => _epiloguePairFlag; set => SetField(ref _epiloguePairFlag, value); }
        public uint EpilogueUnitId1 { get => _epilogueUnitId1; set => SetField(ref _epilogueUnitId1, value); }
        public uint EpilogueUnitId2 { get => _epilogueUnitId2; set => SetField(ref _epilogueUnitId2, value); }
        public uint EpilogueStoryFlag { get => _epilogueStoryFlag; set => SetField(ref _epilogueStoryFlag, value); }
        public uint EpilogueTextId { get => _epilogueTextId; set => SetField(ref _epilogueTextId, value); }
        public EpilogueRouteKind EpilogueRoute { get => _epilogueRoute; set => SetField(ref _epilogueRoute, value); }

        /// <summary>The most recently loaded epilogue list; refreshed
        /// each time `LoadEpilogueList()` runs.</summary>
        public List<AddrResult> EpilogueList => _epilogueList;

        // ============================================================
        // Lyn tab (ed_3c_pointer USED AS DIRECT BASE)
        // ============================================================

        /// <summary>Iterate the Lyn list. ed_3c_pointer is the table base
        /// address directly (NOT a pointer to dereference). Mirrors WF
        /// EDFE7Form.N3_Init which constructs with pointer=0 then calls
        /// `ifr.ReInit(ed_3c_pointer)` setting the BaseAddress directly.
        /// Terminator: u32 == 0 on the UnitId field.</summary>
        public List<AddrResult> LoadLynList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            uint baseAddr = rom.RomInfo.ed_3c_pointer;
            if (baseAddr == 0) return new List<AddrResult>();
            return LoadListInternalDirect(rom, baseAddr,
                LYN_BLOCK_SIZE,
                (addr) => rom.u32(addr) != 0x00,
                (addr) => {
                    uint uid = rom.u8(addr);
                    return $"{U.ToHexString(uid)} {GetUnitNameForUid(uid)}";
                });
        }

        public void LoadLyn(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + LYN_BLOCK_SIZE > (uint)rom.Data.Length) return;

            LynAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _lynFields);
            LynUnitId = v["D0"];
            LynClearedTextId = v["D4"];
            LynRetreatTextId = v["D8"];
            LynCanWrite = true;
        }

        public void WriteLyn()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || LynAddr == 0) return;
            if (LynAddr + LYN_BLOCK_SIZE > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["D0"] = LynUnitId,
                ["D4"] = LynClearedTextId,
                ["D8"] = LynRetreatTextId,
            };
            EditorFormRef.WriteFields(rom, LynAddr, values, _lynFields);
        }

        // NOTE: NO ExpandLynList() method. The Lyn table is direct-base
        // (ed_3c_pointer IS the address, not a pointer field to dereference).
        // Relocating it would require updating any consumers that read the
        // hard-coded address, which is out of scope. WF designer has no
        // Lyn list-expand button either - parity with WF.

        // ============================================================
        // Retreat tab (ed_1_pointer)
        // ============================================================

        public List<AddrResult> LoadRetreatList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            return LoadListInternal(rom, rom.RomInfo.ed_1_pointer,
                RETREAT_BLOCK_SIZE,
                (addr) => rom.u32(addr) != 0x00,
                (addr) => {
                    uint uid = rom.u8(addr);
                    return $"{U.ToHexString(uid)} {GetUnitNameForUid(uid)}";
                });
        }

        public void LoadRetreat(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + RETREAT_BLOCK_SIZE > (uint)rom.Data.Length) return;

            RetreatAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _retreatFields);
            RetreatUnitId = v["B0"];
            RetreatCondition = v["B1"];
            RetreatB2 = v["B2"];
            RetreatB3 = v["B3"];
            RetreatCanWrite = true;
        }

        public void WriteRetreat()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || RetreatAddr == 0) return;
            if (RetreatAddr + RETREAT_BLOCK_SIZE > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = RetreatUnitId,
                ["B1"] = RetreatCondition,
                ["B2"] = RetreatB2,
                ["B3"] = RetreatB3,
            };
            EditorFormRef.WriteFields(rom, RetreatAddr, values, _retreatFields);
        }

        public DataExpansionCore.ExpandResult ExpandRetreatList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
                return new DataExpansionCore.ExpandResult { Success = false, Error = "ROM not loaded." };
            uint ptrAddr = rom.RomInfo.ed_1_pointer;
            if (ptrAddr == 0)
                return new DataExpansionCore.ExpandResult { Success = false, Error = "ed_1_pointer not set." };
            uint currentCount = (uint)LoadRetreatList().Count;
            return ExpandTerminatedTable(rom, ptrAddr, RETREAT_BLOCK_SIZE, currentCount,
                seedB0FromCloneOrDefault: true);
        }

        // ============================================================
        // Epithet tab (ed_2_pointer)
        // ============================================================

        /// <summary>Iterate the epithet list. FE7 uses u32==0 terminator
        /// (the predicate in WF EDFE7Form.N1_Init at line 47 reads
        /// `Program.ROM.u32(addr) != 0x00`), NOT u8==0 like FE8 EDForm.</summary>
        public List<AddrResult> LoadEpithetList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            return LoadListInternal(rom, rom.RomInfo.ed_2_pointer,
                EPITHET_BLOCK_SIZE,
                (addr) => rom.u32(addr) != 0x00,
                (addr) => {
                    uint uid = rom.u8(addr);
                    uint textId = rom.u32(addr + 4);
                    string textPreview = textId != 0 ? NameResolver.GetTextById(textId) : "";
                    return $"{U.ToHexString(uid)} {GetUnitNameForUid(uid)} {textPreview}".Trim();
                });
        }

        public void LoadEpithet(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + EPITHET_BLOCK_SIZE > (uint)rom.Data.Length) return;

            EpithetAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _epithetFields);
            EpithetUnitId = v["D0"];   // DWord at +0 - distinct from FE8's W0
            EpithetTextId = v["D4"];   // DWord at +4
            EpithetCanWrite = true;
        }

        public void WriteEpithet()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || EpithetAddr == 0) return;
            if (EpithetAddr + EPITHET_BLOCK_SIZE > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["D0"] = EpithetUnitId,
                ["D4"] = EpithetTextId,
            };
            EditorFormRef.WriteFields(rom, EpithetAddr, values, _epithetFields);
        }

        public DataExpansionCore.ExpandResult ExpandEpithetList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
                return new DataExpansionCore.ExpandResult { Success = false, Error = "ROM not loaded." };
            uint ptrAddr = rom.RomInfo.ed_2_pointer;
            if (ptrAddr == 0)
                return new DataExpansionCore.ExpandResult { Success = false, Error = "ed_2_pointer not set." };
            uint currentCount = (uint)LoadEpithetList().Count;
            return ExpandTerminatedTable(rom, ptrAddr, EPITHET_BLOCK_SIZE, currentCount,
                seedB0FromCloneOrDefault: true);
        }

        // ============================================================
        // Epilogue tab (ed_3a_pointer or ed_3b_pointer)
        // ============================================================

        uint GetEpiloguePointerAddr()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return EpilogueRoute == EpilogueRouteKind.Hector
                ? rom.RomInfo.ed_3b_pointer
                : rom.RomInfo.ed_3a_pointer;
        }

        public List<AddrResult> LoadEpilogueList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                _epilogueList = new List<AddrResult>();
                return _epilogueList;
            }
            uint ptrAddr = GetEpiloguePointerAddr();
            _epilogueList = LoadListInternal(rom, ptrAddr,
                EPILOGUE_BLOCK_SIZE,
                (addr) => rom.u32(addr) != 0x00,
                (addr) => {
                    uint flag = rom.u8(addr);
                    uint uid1 = rom.u8(addr + 1);
                    uint uid2 = rom.u8(addr + 2);
                    string name1 = GetUnitNameForUid(uid1);
                    if (flag == 1)
                        return $"{U.ToHexString(uid1)} {name1}";
                    if (flag == 2)
                        return $"{U.ToHexString(uid1)} {name1} & {U.ToHexString(uid2)} {GetUnitNameForUid(uid2)}";
                    return $"{U.ToHexString(uid1)} {name1} ?? {U.ToHexString(uid2)} {GetUnitNameForUid(uid2)}";
                });
            return _epilogueList;
        }

        public void LoadEpilogue(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + EPILOGUE_BLOCK_SIZE > (uint)rom.Data.Length) return;

            EpilogueAddr = addr;
            var v = EditorFormRef.ReadFields(rom, addr, _epilogueFields);
            EpiloguePairFlag = v["B0"];
            EpilogueUnitId1 = v["B1"];
            EpilogueUnitId2 = v["B2"];
            EpilogueStoryFlag = v["B3"];
            EpilogueTextId = v["D4"];
            EpilogueCanWrite = true;
        }

        public void WriteEpilogue()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || EpilogueAddr == 0) return;
            if (EpilogueAddr + EPILOGUE_BLOCK_SIZE > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["B0"] = EpiloguePairFlag,
                ["B1"] = EpilogueUnitId1,
                ["B2"] = EpilogueUnitId2,
                ["B3"] = EpilogueStoryFlag,
                ["D4"] = EpilogueTextId,
            };
            EditorFormRef.WriteFields(rom, EpilogueAddr, values, _epilogueFields);
        }

        public DataExpansionCore.ExpandResult ExpandEpilogueList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
                return new DataExpansionCore.ExpandResult { Success = false, Error = "ROM not loaded." };
            uint ptrAddr = GetEpiloguePointerAddr();
            if (ptrAddr == 0)
                return new DataExpansionCore.ExpandResult { Success = false, Error = "Epilogue pointer not set for this route." };
            uint currentCount = (uint)LoadEpilogueList().Count;
            return ExpandTerminatedTable(rom, ptrAddr, EPILOGUE_BLOCK_SIZE, currentCount,
                seedB0FromCloneOrDefault: false, defaultB0: 0x01);
        }

        // ============================================================
        // Backward-compat shims (preserve callers from the original VM).
        // ============================================================

        /// <summary>Legacy shim - forwards to <see cref="LoadLynList"/>.</summary>
        public List<AddrResult> LoadList() => LoadLynList();

        /// <summary>Legacy shim - forwards to <see cref="LoadLyn"/>.</summary>
        public void LoadEntry(uint addr) => LoadLyn(addr);

        /// <summary>Legacy alias - matches the original VM's surface so
        /// IDataVerifiable / INavigationTargetSource hosts can stay
        /// untouched while we restructure the VM.</summary>
        public uint CurrentAddr { get => LynAddr; set => LynAddr = value; }
        public bool IsLoaded { get => LynCanWrite; set => LynCanWrite = value; }

        // ============================================================
        // IDataVerifiable
        // ============================================================

        public int GetListCount() => LoadLynList().Count;

        public Dictionary<string, string> GetDataReport() => new()
        {
            ["addr"] = $"0x{LynAddr:X08}",
            // Lyn surface
            ["LynUnitId"] = $"0x{LynUnitId:X08}",
            ["LynClearedTextId"] = $"0x{LynClearedTextId:X08}",
            ["LynRetreatTextId"] = $"0x{LynRetreatTextId:X08}",
            // Retreat surface
            ["RetreatUnitId"] = $"0x{RetreatUnitId:X02}",
            ["RetreatCondition"] = $"0x{RetreatCondition:X02}",
            ["RetreatB2"] = $"0x{RetreatB2:X02}",
            ["RetreatB3"] = $"0x{RetreatB3:X02}",
            // Epithet surface
            ["EpithetUnitId"] = $"0x{EpithetUnitId:X08}",
            ["EpithetTextId"] = $"0x{EpithetTextId:X08}",
            // Epilogue surface
            ["EpiloguePairFlag"] = $"0x{EpiloguePairFlag:X02}",
            ["EpilogueUnitId1"] = $"0x{EpilogueUnitId1:X02}",
            ["EpilogueUnitId2"] = $"0x{EpilogueUnitId2:X02}",
            ["EpilogueStoryFlag"] = $"0x{EpilogueStoryFlag:X02}",
            ["EpilogueTextId"] = $"0x{EpilogueTextId:X08}",
        };

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return new Dictionary<string, string>();

            // Report covers all four tab surfaces. The IDataVerifiable
            // surface assumes one current entry; we report the Lyn entry
            // as the primary, plus the most-recently-loaded entries from
            // each other tab if they're set. Coverage test (#403) needs
            // entries for every distinct ROM read offset the four
            // Load*() methods perform.
            var report = new Dictionary<string, string>();
            if (LynAddr != 0)
            {
                uint a = LynAddr;
                report["addr"] = $"0x{a:X08}";
                report["u32@0x00_LynUnitId"] = $"0x{rom.u32(a + 0):X08}";
                report["u32@0x04_LynClearedTextId"] = $"0x{rom.u32(a + 4):X08}";
                report["u32@0x08_LynRetreatTextId"] = $"0x{rom.u32(a + 8):X08}";
            }
            if (RetreatAddr != 0)
            {
                uint a = RetreatAddr;
                report["retreat_addr"] = $"0x{a:X08}";
                report["u8@0x00_RetreatUnitId"] = $"0x{rom.u8(a + 0):X02}";
                report["u8@0x01_RetreatCondition"] = $"0x{rom.u8(a + 1):X02}";
                report["u8@0x02_RetreatB2"] = $"0x{rom.u8(a + 2):X02}";
                report["u8@0x03_RetreatB3"] = $"0x{rom.u8(a + 3):X02}";
            }
            if (EpithetAddr != 0)
            {
                uint a = EpithetAddr;
                report["epithet_addr"] = $"0x{a:X08}";
                report["u32@0x00_EpithetUnitId"] = $"0x{rom.u32(a + 0):X08}";
                report["u32@0x04_EpithetTextId"] = $"0x{rom.u32(a + 4):X08}";
            }
            if (EpilogueAddr != 0)
            {
                uint a = EpilogueAddr;
                report["epilogue_addr"] = $"0x{a:X08}";
                report["u8@0x00_EpiloguePairFlag"] = $"0x{rom.u8(a + 0):X02}";
                report["u8@0x01_EpilogueUnitId1"] = $"0x{rom.u8(a + 1):X02}";
                report["u8@0x02_EpilogueUnitId2"] = $"0x{rom.u8(a + 2):X02}";
                report["u8@0x03_EpilogueStoryFlag"] = $"0x{rom.u8(a + 3):X02}";
                report["u32@0x04_EpilogueTextId"] = $"0x{rom.u32(a + 4):X08}";
            }
            return report;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["LynUnitId"] = "u32@0x00_LynUnitId",
            ["LynClearedTextId"] = "u32@0x04_LynClearedTextId",
            ["LynRetreatTextId"] = "u32@0x08_LynRetreatTextId",
            ["RetreatUnitId"] = "u8@0x00_RetreatUnitId",
            ["RetreatCondition"] = "u8@0x01_RetreatCondition",
            ["RetreatB2"] = "u8@0x02_RetreatB2",
            ["RetreatB3"] = "u8@0x03_RetreatB3",
            ["EpithetUnitId"] = "u32@0x00_EpithetUnitId",
            ["EpithetTextId"] = "u32@0x04_EpithetTextId",
            ["EpiloguePairFlag"] = "u8@0x00_EpiloguePairFlag",
            ["EpilogueUnitId1"] = "u8@0x01_EpilogueUnitId1",
            ["EpilogueUnitId2"] = "u8@0x02_EpilogueUnitId2",
            ["EpilogueStoryFlag"] = "u8@0x03_EpilogueStoryFlag",
            ["EpilogueTextId"] = "u32@0x04_EpilogueTextId",
        };

        // ============================================================
        // Private helpers
        // ============================================================

        /// <summary>
        /// Resolve a stored ED UnitId to a display name. ED tables (and
        /// every other WF unit-id field) store a 1-based id where `0` is
        /// reserved as the list terminator; the underlying unit-table
        /// index is `uid - 1`. Mirrors `EDViewModel.GetUnitNameForUid`.
        /// </summary>
        static string GetUnitNameForUid(uint uid)
        {
            if (uid == 0) return "";
            return SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, uid - 1);
        }

        /// <summary>
        /// Expand a "zero-terminated" ED table by one entry. Mirrors
        /// EDViewModel.ExpandTerminatedTable from PR #561. Reserves
        /// `liveCount + 2` rows so the new editable row + the new
        /// terminator both fit inside the reserved free-space region.
        /// </summary>
        static DataExpansionCore.ExpandResult ExpandTerminatedTable(
            ROM rom, uint pointerAddr, uint blockSize, uint liveCount,
            bool seedB0FromCloneOrDefault, byte defaultB0 = 0x01)
        {
            byte prevB0 = 0;
            if (seedB0FromCloneOrDefault && liveCount > 0)
            {
                uint oldBase = rom.p32(pointerAddr);
                if (U.isSafetyOffset(oldBase, rom))
                {
                    uint prevRowAddr = oldBase + (liveCount - 1) * blockSize;
                    if (prevRowAddr + blockSize <= (uint)rom.Data.Length)
                        prevB0 = (byte)(rom.u8(prevRowAddr) & 0xFF);
                }
            }

            uint reservedAsCount = liveCount + 1;
            var result = DataExpansionCore.ExpandTable(rom, pointerAddr, blockSize, reservedAsCount);
            if (!result.Success) return result;

            uint newRowAddr = result.NewBaseAddress + liveCount * blockSize;
            if (newRowAddr + blockSize > (uint)rom.Data.Length)
                return result;

            byte seed = defaultB0;
            if (seedB0FromCloneOrDefault && prevB0 != 0) seed = prevB0;
            if (seed == 0) seed = defaultB0;
            rom.write_u8(newRowAddr, seed);

            return new DataExpansionCore.ExpandResult
            {
                Success = true,
                NewBaseAddress = result.NewBaseAddress,
                NewCount = liveCount + 1,
            };
        }

        /// <summary>Shared list iteration loop with caller-supplied
        /// termination predicate + name formatter, with POINTER indirection
        /// (mirrors WF InputFormRef lambdas).</summary>
        static List<AddrResult> LoadListInternal(ROM rom, uint pointerAddr,
            uint blockSize,
            Func<uint, bool> isValid,
            Func<uint, string> nameFor)
        {
            var result = new List<AddrResult>();
            if (rom == null || pointerAddr == 0) return result;

            uint baseAddr = rom.p32(pointerAddr);
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!isValid(addr)) break;
                result.Add(new AddrResult(addr, nameFor(addr), i));
            }
            return result;
        }

        /// <summary>Shared list iteration loop WITHOUT pointer indirection
        /// (mirrors WF EDFE7Form.N3_Init where ed_3c_pointer IS the table
        /// base, set via `ifr.ReInit(ed_3c_pointer)`).</summary>
        static List<AddrResult> LoadListInternalDirect(ROM rom, uint baseAddr,
            uint blockSize,
            Func<uint, bool> isValid,
            Func<uint, string> nameFor)
        {
            var result = new List<AddrResult>();
            if (rom == null || baseAddr == 0) return result;
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = baseAddr + i * blockSize;
                if (addr + blockSize > (uint)rom.Data.Length) break;
                if (!isValid(addr)) break;
                result.Add(new AddrResult(addr, nameFor(addr), i));
            }
            return result;
        }
    }
}
