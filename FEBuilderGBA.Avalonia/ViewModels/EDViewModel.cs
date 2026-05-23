// SPDX-License-Identifier: GPL-3.0-or-later
// EDForm (Ending Demo) ViewModel - gap-sweep #411 parity raise.
//
// Mirrors WinForms `EDForm` exactly. EDForm has three sub-surfaces, one per
// tab, each operating on a different ROM data structure:
//
//   tabPage1  "Retreat"  - 4-byte records at `ed_1_pointer`.
//                          Term: u32 == 0.
//                          Fields B0 (UnitId), B1 (Condition),
//                          B2 and B3 (unknown).
//
//   tabPage2  "Epithet"  - 8-byte records at `ed_2_pointer`.
//                          Term: u8 == 0.
//                          Fields W0 (UnitId, 2 bytes - matches WF
//                          `N1_W0`) + D4 (Epithet text id, 4 bytes
//                          - matches WF `N1_D4` and EDForm.cs line 67
//                          `u32(addr + 4)`).
//                          Bytes +2..+3 are reserved/padding - WF
//                          designer does not expose them.
//
//   tabPage3  "Epilogue" - 8-byte records at `ed_3a_pointer`
//                          (Eirika route) or `ed_3b_pointer`
//                          (Ephraim route), selected via
//                          `EpilogueRoute` enum.
//                          Term: u32 == 0.
//                          Fields B0 (PairFlag designation,
//                          1=Solo, 2=Support, "??" otherwise),
//                          B1 (UnitId1), B2 (UnitId2),
//                          B3 (StoryFlag), D4 (Epilogue text id).
//
// Copilot CLI v1 plan review surfaced four issues that v2 corrected
// and the EDParityTests pin in place:
//   C1 - Text fields are `D4` (DWord at +4), not `W4`.
//   C2 - Epithet unit field is `W0` (Word at +0), not `B0`.
//   C3 - FE6JP has `ed_3b == 0`; `EpilogueAvailability` reports
//        `EirikaOnly` so the View can disable the Ephraim combo
//        option.
//   C4 - `Expand*List()` methods call `DataExpansionCore.ExpandTable`
//        with the correct per-tab entry sizes (4/8/8). The View
//        wraps each in its own `UndoService.Begin("Expand ED ...")`
//        scope.
//
// Backward-compat shims preserve the original three-method surface
// (`LoadEDList` / `LoadED` / `WriteED`) so `ListParityHelper.BuildEDList`
// and any `INavigationTargetSource` callers keep working unchanged.
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class EDViewModel : ViewModelBase, IDataVerifiable
    {
        // --- Field-set declarations (parsed once per class) ----------

        /// <summary>Retreat record: 4 bytes - B0/B1/B2/B3.</summary>
        static readonly List<EditorFormRef.FieldDef> _retreatFields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3" });

        /// <summary>Epithet record: 8 bytes - W0 (Word UnitId), D4 (DWord
        /// text id). Bytes +2..+3 are reserved/padding (KnownGap - WF
        /// designer does not expose them).</summary>
        static readonly List<EditorFormRef.FieldDef> _epithetFields =
            EditorFormRef.DetectFields(new[] { "W0", "D4" });

        /// <summary>Epilogue record: 8 bytes - B0/B1/B2/B3, D4.</summary>
        static readonly List<EditorFormRef.FieldDef> _epilogueFields =
            EditorFormRef.DetectFields(new[] { "B0", "B1", "B2", "B3", "D4" });

        public const uint RETREAT_BLOCK_SIZE = 4;
        public const uint EPITHET_BLOCK_SIZE = 8;
        public const uint EPILOGUE_BLOCK_SIZE = 8;

        // --- Per-version pointer availability (Copilot C3) -----------

        /// <summary>
        /// Which epilogue routes a given ROM exposes. FE7/FE8 always
        /// have `ed_3a` (Eirika/Eliwood) + `ed_3b` (Ephraim/Hector).
        /// FE6JP only has `ed_3a` (post-game epilogue); `ed_3b == 0`.
        /// </summary>
        public enum EpilogueAvailabilityKind
        {
            /// <summary>Both pointers are set.</summary>
            BothRoutes,
            /// <summary>Only `ed_3a_pointer` is set (FE6JP).</summary>
            EirikaOnly,
            /// <summary>Neither pointer is set (paranoid path).</summary>
            None,
        }

        /// <summary>Which epilogue route the user is editing.</summary>
        public enum EpilogueRouteKind
        {
            /// <summary>`ed_3a_pointer` route (FE6 / FE7 Eliwood / FE8 Eirika).</summary>
            Eirika,
            /// <summary>`ed_3b_pointer` route (FE7 Hector / FE8 Ephraim).</summary>
            Ephraim,
        }

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
        EpilogueRouteKind _epilogueRoute = EpilogueRouteKind.Eirika;
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
        /// each time `LoadEpilogueList()` runs. Exposed so the View can
        /// read it back without a second ROM scan.</summary>
        public List<AddrResult> EpilogueList => _epilogueList;

        /// <summary>Per-version pointer availability for the epilogue tab.</summary>
        public EpilogueAvailabilityKind EpilogueAvailability
        {
            get
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return EpilogueAvailabilityKind.None;
                bool hasA = rom.RomInfo.ed_3a_pointer != 0;
                bool hasB = rom.RomInfo.ed_3b_pointer != 0;
                if (hasA && hasB) return EpilogueAvailabilityKind.BothRoutes;
                if (hasA) return EpilogueAvailabilityKind.EirikaOnly;
                return EpilogueAvailabilityKind.None;
            }
        }

        // ============================================================
        // Retreat tab (ed_1_pointer)
        // ============================================================

        /// <summary>Iterate the retreat list until `u32 == 0` (matches
        /// the WF `Init` lambda exactly).</summary>
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

        /// <summary>Expand the retreat table by one entry. Caller wraps
        /// in `UndoService.Begin("Expand ED Retreat")` (Copilot C4).</summary>
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

        /// <summary>Iterate the epithet list until `u8 == 0` (matches
        /// the WF `N1_Init` lambda at EDForm.cs line 62).</summary>
        public List<AddrResult> LoadEpithetList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();
            return LoadListInternal(rom, rom.RomInfo.ed_2_pointer,
                EPITHET_BLOCK_SIZE,
                (addr) => rom.u8(addr) != 0x00,
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
            EpithetUnitId = v["W0"];   // Word - matches WF N1_W0
            EpithetTextId = v["D4"];   // DWord - matches WF N1_D4
            EpithetCanWrite = true;
        }

        public void WriteEpithet()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || EpithetAddr == 0) return;
            if (EpithetAddr + EPITHET_BLOCK_SIZE > (uint)rom.Data.Length) return;

            var values = new Dictionary<string, uint>
            {
                ["W0"] = EpithetUnitId,
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

        /// <summary>Return the active epilogue base pointer per the
        /// current `EpilogueRoute`. Returns 0 when the route's pointer
        /// is not defined on this ROM (FE6JP Ephraim).</summary>
        uint GetEpiloguePointerAddr()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return EpilogueRoute == EpilogueRouteKind.Ephraim
                ? rom.RomInfo.ed_3b_pointer
                : rom.RomInfo.ed_3a_pointer;
        }

        /// <summary>Iterate the epilogue list until `u32 == 0` (matches
        /// the WF `N2_Init` lambda at EDForm.cs line 82). Updates
        /// `EpilogueList` so the View can read it back.</summary>
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
            uint currentCount = (uint)_epilogueList.Count;
            if (currentCount == 0)
                currentCount = (uint)LoadEpilogueList().Count;
            return ExpandTerminatedTable(rom, ptrAddr, EPILOGUE_BLOCK_SIZE, currentCount,
                seedB0FromCloneOrDefault: false, defaultB0: 0x01);
        }

        // ============================================================
        // Backward-compat shims (preserve callers from the old single-
        // surface VM: ListParityHelper.BuildEDList, INavigationTargetSource).
        // ============================================================

        /// <summary>Legacy shim - forwards to <see cref="LoadRetreatList"/>.</summary>
        public List<AddrResult> LoadEDList() => LoadRetreatList();

        /// <summary>Legacy shim - forwards to <see cref="LoadRetreat"/>.</summary>
        public void LoadED(uint addr) => LoadRetreat(addr);

        /// <summary>Legacy shim - forwards to <see cref="WriteRetreat"/>.</summary>
        public void WriteED() => WriteRetreat();

        /// <summary>Legacy alias - matches the original VM's surface so
        /// IDataVerifiable / INavigationTargetSource hosts can stay
        /// untouched while we restructure the VM.</summary>
        public uint CurrentAddr { get => RetreatAddr; set => RetreatAddr = value; }
        public bool CanWrite { get => RetreatCanWrite; set => RetreatCanWrite = value; }
        public uint UnitId { get => RetreatUnitId; set => RetreatUnitId = value; }
        public uint Condition { get => RetreatCondition; set => RetreatCondition = value; }
        public uint Unknown2 { get => RetreatB2; set => RetreatB2 = value; }
        public uint Unknown3 { get => RetreatB3; set => RetreatB3 = value; }

        // ============================================================
        // IDataVerifiable
        // ============================================================

        public int GetListCount() => LoadRetreatList().Count;

        public Dictionary<string, string> GetDataReport() => new()
        {
            ["addr"] = $"0x{RetreatAddr:X08}",
            ["UnitId"] = $"0x{RetreatUnitId:X02}",
            ["Condition"] = $"0x{RetreatCondition:X02}",
            ["Unknown2"] = $"0x{RetreatB2:X02}",
            ["Unknown3"] = $"0x{RetreatB3:X02}",
        };

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || RetreatAddr == 0) return new Dictionary<string, string>();

            uint a = RetreatAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u8@0x00_UnitId"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01_Condition"] = $"0x{rom.u8(a + 1):X02}",
                ["u8@0x02_Unknown2"] = $"0x{rom.u8(a + 2):X02}",
                ["u8@0x03_Unknown3"] = $"0x{rom.u8(a + 3):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["UnitId"] = "u8@0x00_UnitId",
            ["Condition"] = "u8@0x01_Condition",
            ["Unknown2"] = "u8@0x02_Unknown2",
            ["Unknown3"] = "u8@0x03_Unknown3",
        };

        // ============================================================
        // Private helpers
        // ============================================================

        /// <summary>
        /// Resolve a stored ED UnitId to a display name. ED tables (and
        /// every other WF unit-id field) store a 1-based id where `0` is
        /// reserved as the list terminator; the underlying unit-table
        /// index is `uid - 1`. This matches WF `UnitForm.GetUnitName(uid)`
        /// which decrements internally.
        ///
        /// Routes through <see cref="SupportUnitNavigation.ResolveUnitTableName"/>
        /// (NOT `NameResolver.GetUnitName`) so FE6's dummy-entry skip
        /// (which adds one extra `unit_datasize` to the base before the
        /// table starts) is applied identically to the View's
        /// `ResolveUnitNameForUid` helper. Without that skip FE6 ED list
        /// labels would resolve to the dummy unit / off-by-one vs the
        /// `IdField` preview the View also renders.
        ///
        /// Copilot PR #561 fourth CLI review: cross-platform FE6
        /// consistency fix.
        /// </summary>
        static string GetUnitNameForUid(uint uid)
        {
            if (uid == 0) return "";
            return SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, uid - 1);
        }

        /// <summary>
        /// After <see cref="DataExpansionCore.ExpandTable"/> appends a
        /// zero-initialized entry, our ED iterators (which terminate on
        /// either `u32 == 0` for retreat/epilogue or `u8 == 0` for epithet)
        /// would treat the new row as the terminator and the user would
        /// never see it. Seed B0 with a non-zero byte so the new row is
        /// visible AND editable.
        ///
        /// When <paramref name="seedB0FromCloneOrDefault"/> is true, copy
        /// B0 from the just-cloned previous-last entry (so the new slot
        /// looks like the unit before it - same behavior as WF
        /// `MoveToFreeSapceForm.CalcFillDataOnListExpamds`). When the
        /// table is empty, fall back to <paramref name="defaultB0"/>.
        ///
        /// Copilot CLI PR #561 review: blocking finding "List Expand
        /// controls are visible but do not leave a new editable row".
        /// This helper closes the gap by seeding B0 so the new entry
        /// survives the next LoadList scan.
        /// </summary>
        /// <summary>
        /// Expand a "zero-terminated" ED table by one entry. ED tables
        /// use a sentinel-row terminator (`u32 == 0` for retreat/epilogue,
        /// `u8 == 0` for epithet) instead of an explicit count field.
        ///
        /// This wraps <see cref="DataExpansionCore.ExpandTable"/> but
        /// passes <c>currentCount + 1</c> as the "current count" so it
        /// reserves space for <c>currentCount + 2</c> entries total:
        /// the <c>currentCount</c> live entries, plus the existing
        /// terminator entry, plus the new appended (zero) entry from
        /// ExpandTable itself. We then overwrite what was the terminator
        /// (now the second-to-last entry) with seeded data so the user
        /// sees a fresh editable row; the final zero entry from
        /// ExpandTable serves as the new terminator and lives entirely
        /// within the reserved free-space region (no out-of-reservation
        /// writes).
        ///
        /// Copilot CLI PR #561 third review: this replaces the previous
        /// "skip terminator on exact-fit" approach which still left the
        /// iterator running into 0xFF garbage up to the 0x200-row cap.
        /// </summary>
        static DataExpansionCore.ExpandResult ExpandTerminatedTable(
            ROM rom, uint pointerAddr, uint blockSize, uint liveCount,
            bool seedB0FromCloneOrDefault, byte defaultB0 = 0x01)
        {
            // Read the live-table base BEFORE ExpandTable relocates it
            // so we can clone the predecessor's B0 byte if requested.
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

            // Pass `liveCount + 1` so ExpandTable copies the live entries
            // PLUS the existing terminator, then appends one more zero
            // entry. After this call the layout in the new free-space
            // region is:
            //   [live entries] [existing terminator] [new zero entry]
            // and ALL of it is within the reserved region.
            uint reservedAsCount = liveCount + 1;
            var result = DataExpansionCore.ExpandTable(rom, pointerAddr, blockSize, reservedAsCount);
            if (!result.Success) return result;

            // The new editable row sits where the terminator used to be:
            // at offset `liveCount * blockSize` from the new base.
            uint newRowAddr = result.NewBaseAddress + liveCount * blockSize;
            if (newRowAddr + blockSize > (uint)rom.Data.Length)
                return result; // shouldn't happen, ExpandTable already bounds-checked

            byte seed = defaultB0;
            if (seedB0FromCloneOrDefault && prevB0 != 0) seed = prevB0;
            if (seed == 0) seed = defaultB0;
            rom.write_u8(newRowAddr, seed);

            // Report the user-visible new count: ExpandTable reports
            // `reservedAsCount + 1 = liveCount + 2`, but the user only
            // gained one editable row. Adjust so callers see the right
            // number.
            return new DataExpansionCore.ExpandResult
            {
                Success = true,
                NewBaseAddress = result.NewBaseAddress,
                NewCount = liveCount + 1,
            };
        }

        /// <summary>Shared list iteration loop with caller-supplied
        /// termination predicate + name formatter. Matches the WF
        /// `InputFormRef` lambdas one-for-one.</summary>
        static List<AddrResult> LoadListInternal(ROM rom, uint pointerAddr,
            uint blockSize,
            Func<uint, bool> isValid,
            Func<uint, string> nameFor)
        {
            var result = new List<AddrResult>();
            if (rom == null || pointerAddr == 0) return result;

            uint baseAddr = rom.p32(pointerAddr);
            // Pass the ROM explicitly so we don't fall back to CoreState.ROM
            // (which can be null in tests or point at a different ROM).
            // Copilot PR #561 inline review #2.
            if (!U.isSafetyOffset(baseAddr, rom)) return result;

            // Same upper bound the prior VM used so a corrupt
            // terminator doesn't run away with the loop.
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
