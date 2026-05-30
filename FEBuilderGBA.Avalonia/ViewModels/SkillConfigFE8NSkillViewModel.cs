// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel backing <see cref="Views.SkillConfigFE8NSkillView"/>.
    /// Phase 1/2/4 gap-sweep parity raise (#390).
    ///
    /// FE8N v1 is the ORIGINAL FE8N skill patch flavour (also reused by the
    /// <c>yugudora</c> patch — distinct from v2/v3). It stores skill metadata
    /// in a MULTI-PAGE table indexed by a list of GBA pointers discovered via
    /// byte-pattern scan (see <see cref="PreviewIconHelper.FindSkillFE8NVer1IconPointers"/>).
    /// The View exposes a FilterComboBox so the user can switch between pages.
    ///
    /// Row layout (sizeof-32):
    /// <code>
    /// u16 iconId @ +0   (W0)  — drives icon rendering via iconBase + 128 * W0
    /// u16 textId @ +2   (W2)  — skill name / description text id
    /// u8 condUnit1..4   (B4..B7)
    /// u8 condClass1..4  (B8..B11)
    /// u8 condItem1..4   (B12..B15)
    /// u8 ext0..15       (B16..B31) — split across 4 N0x sub-tabs (KnownGap #374)
    /// </code>
    ///
    /// Caller (the View) is expected to wrap <see cref="Write"/> in a single
    /// <c>_undoService.Begin/Commit</c> scope per the single-owner contract.
    ///
    /// Declared <c>partial</c> so the Phase 4 navigation manifest can live in
    /// a sibling <c>.NavigationTargets.cs</c> file.
    /// </summary>
    public partial class SkillConfigFE8NSkillViewModel : ViewModelBase, IDataVerifiable
    {
        /// <summary>FE8N v1 row stride is a fixed 32 bytes (not detected at runtime — unlike v2).</summary>
        public const uint RowStride = 32;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _selectedId;

        // Read-config bar (WF panel3).
        uint _readStartAddress;
        uint _readCount;

        // Multi-pointer fields (WF Pointers + FilterComboBox).
        uint[] _iconPointers = Array.Empty<uint>();
        int _selectedPointerIndex;
        uint _currentSkillBaseAddress;

        // Per-row editable fields.
        uint _iconId;          // W0 (u16)
        uint _textDetail;      // W2 (u16)
        uint _condUnit1, _condUnit2, _condUnit3, _condUnit4;       // B4..B7
        uint _condClass1, _condClass2, _condClass3, _condClass4;   // B8..B11
        uint _condItem1, _condItem2, _condItem3, _condItem4;       // B12..B15

        // Per-row editable ext-tab values (B16..B31). Surfaced as 16 two-way
        // Ext0..Ext15 properties below (#790). Stored in a fixed 16-element
        // backing array so the read loop + ExtValues stay zero-copy.
        readonly uint[] _ext = new uint[16];

        // Status banner rendered when the patch is missing.
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.\n\nSupported skill systems: CSkillSys, FE8N Skill System";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }

        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint BlockSize => RowStride;

        /// <summary>
        /// All pointer slot ADDRESSES discovered by
        /// <see cref="PreviewIconHelper.FindSkillFE8NVer1IconPointers"/>.
        /// Empty when the FE8N v1 patch isn't installed.
        /// </summary>
        public uint[] IconPointers
        {
            get => _iconPointers;
            set => SetField(ref _iconPointers, value ?? Array.Empty<uint>());
        }

        /// <summary>
        /// Which FilterComboBox entry is currently active. Range [0,
        /// <see cref="IconPointers"/>.Length).
        /// </summary>
        public int SelectedPointerIndex { get => _selectedPointerIndex; set => SetField(ref _selectedPointerIndex, value); }

        /// <summary>
        /// Dereferenced skill-table base offset of the currently selected
        /// pointer. 0 when no pointer is selected or the patch isn't installed.
        /// </summary>
        public uint CurrentSkillBaseAddress { get => _currentSkillBaseAddress; set => SetField(ref _currentSkillBaseAddress, value); }

        public uint IconId { get => _iconId; set => SetField(ref _iconId, value); }
        public uint TextDetail { get => _textDetail; set => SetField(ref _textDetail, value); }
        public uint CondUnit1 { get => _condUnit1; set => SetField(ref _condUnit1, value); }
        public uint CondUnit2 { get => _condUnit2; set => SetField(ref _condUnit2, value); }
        public uint CondUnit3 { get => _condUnit3; set => SetField(ref _condUnit3, value); }
        public uint CondUnit4 { get => _condUnit4; set => SetField(ref _condUnit4, value); }
        public uint CondClass1 { get => _condClass1; set => SetField(ref _condClass1, value); }
        public uint CondClass2 { get => _condClass2; set => SetField(ref _condClass2, value); }
        public uint CondClass3 { get => _condClass3; set => SetField(ref _condClass3, value); }
        public uint CondClass4 { get => _condClass4; set => SetField(ref _condClass4, value); }
        public uint CondItem1 { get => _condItem1; set => SetField(ref _condItem1, value); }
        public uint CondItem2 { get => _condItem2; set => SetField(ref _condItem2, value); }
        public uint CondItem3 { get => _condItem3; set => SetField(ref _condItem3, value); }
        public uint CondItem4 { get => _condItem4; set => SetField(ref _condItem4, value); }

        /// <summary>
        /// Read-only view over the per-row ext-tab values (B16..B31). Kept for
        /// callers that enumerate all 16 bytes; the editable surface is the
        /// <see cref="Ext0"/>..<see cref="Ext15"/> two-way properties below
        /// (#790). All share the same <c>_ext</c> backing array.
        /// </summary>
        public IReadOnlyList<uint> ExtValues => _ext;

        // Editable per-row ext-tab bytes B16..B31 (#790). Each is a two-way
        // u8 (0..255) backed by _ext[i]; offset = 16 + i. Naming mirrors the
        // existing CondUnit1..CondItem4 style so the View binding stays trivial.
        public uint Ext0 { get => _ext[0]; set => SetField(ref _ext[0], value); }
        public uint Ext1 { get => _ext[1]; set => SetField(ref _ext[1], value); }
        public uint Ext2 { get => _ext[2]; set => SetField(ref _ext[2], value); }
        public uint Ext3 { get => _ext[3]; set => SetField(ref _ext[3], value); }
        public uint Ext4 { get => _ext[4]; set => SetField(ref _ext[4], value); }
        public uint Ext5 { get => _ext[5]; set => SetField(ref _ext[5], value); }
        public uint Ext6 { get => _ext[6]; set => SetField(ref _ext[6], value); }
        public uint Ext7 { get => _ext[7]; set => SetField(ref _ext[7], value); }
        public uint Ext8 { get => _ext[8]; set => SetField(ref _ext[8], value); }
        public uint Ext9 { get => _ext[9]; set => SetField(ref _ext[9], value); }
        public uint Ext10 { get => _ext[10]; set => SetField(ref _ext[10], value); }
        public uint Ext11 { get => _ext[11]; set => SetField(ref _ext[11], value); }
        public uint Ext12 { get => _ext[12]; set => SetField(ref _ext[12], value); }
        public uint Ext13 { get => _ext[13]; set => SetField(ref _ext[13], value); }
        public uint Ext14 { get => _ext[14]; set => SetField(ref _ext[14], value); }
        public uint Ext15 { get => _ext[15]; set => SetField(ref _ext[15], value); }

        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// Reset cached state so a subsequent LoadList re-runs the scan.
        /// </summary>
        void ResetDerivedListState()
        {
            ReadStartAddress = 0;
            ReadCount = 0;
            CurrentSkillBaseAddress = 0;
            CurrentAddr = 0;
            SelectedId = 0;
            IsLoaded = false;
            CanWrite = false;
            IconId = 0;
            TextDetail = 0;
            CondUnit1 = CondUnit2 = CondUnit3 = CondUnit4 = 0;
            CondClass1 = CondClass2 = CondClass3 = CondClass4 = 0;
            CondItem1 = CondItem2 = CondItem3 = CondItem4 = 0;
            for (int i = 0; i < _ext.Length; i++) _ext[i] = 0;
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                ResetDerivedListState();
                IconPointers = Array.Empty<uint>();
                return new List<AddrResult>();
            }

            // Force a fresh scan on every LoadList (cheap; avoids stale
            // pointers after a Patch install/uninstall mid-session).
            PreviewIconHelper.ResetFE8NVer1Cache();

            uint[] pointers = PreviewIconHelper.FindSkillFE8NVer1IconPointers();
            IconPointers = pointers;

            if (pointers.Length == 0)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            // Clamp the selected pointer index to the discovered range.
            if (_selectedPointerIndex < 0 || _selectedPointerIndex >= pointers.Length)
            {
                SelectedPointerIndex = 0;
            }

            return BuildListForSelectedPointer(rom);
        }

        /// <summary>
        /// Switch the active FE8N page. Mirrors WF
        /// <c>FilterComboBox_SelectedIndexChanged</c> -> <c>InputFormRef.ReInit</c>.
        /// Clamps out-of-range indices to a valid page.
        /// </summary>
        public List<AddrResult> SelectPointer(int index)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _iconPointers.Length == 0)
            {
                return new List<AddrResult>();
            }
            if (index < 0) index = 0;
            if (index >= _iconPointers.Length) index = _iconPointers.Length - 1;
            SelectedPointerIndex = index;
            return BuildListForSelectedPointer(rom);
        }

        List<AddrResult> BuildListForSelectedPointer(ROM rom)
        {
            var result = new List<AddrResult>();
            if (_iconPointers.Length == 0)
            {
                CurrentSkillBaseAddress = 0;
                ReadStartAddress = 0;
                ReadCount = 0;
                return result;
            }

            uint slotAddr = _iconPointers[_selectedPointerIndex];
            if (!U.isSafetyOffset(slotAddr + 3, rom))
            {
                CurrentSkillBaseAddress = 0;
                ReadStartAddress = 0;
                ReadCount = 0;
                return result;
            }

            uint baseGba = rom.u32(slotAddr);
            if (!U.isSafetyPointer(baseGba))
            {
                CurrentSkillBaseAddress = 0;
                ReadStartAddress = 0;
                ReadCount = 0;
                return result;
            }

            uint baseAddr = U.toOffset(baseGba);
            CurrentSkillBaseAddress = baseAddr;
            ReadStartAddress = baseAddr;

            // WF iteration predicate: terminate on u16 == 0xFFFF or u16 == 0x0.
            for (uint i = 0; ; i++)
            {
                uint addr = baseAddr + i * RowStride;
                if (!U.isSafetyOffset(addr + (RowStride - 1), rom)) break;
                if (i >= 256) break; // sanity cap

                uint pp = rom.u16(addr);
                if (pp == 0xFFFF || pp == 0u) break;

                string skillName = ResolveSkillName(rom, addr);
                string label = skillName.Length > 0
                    ? $"0x{i:X02} {skillName}"
                    : $"0x{i:X02}";
                result.Add(new AddrResult(addr, label, i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        /// <summary>
        /// Resolve the human-readable skill name for the given row. Mirrors
        /// the WF <c>Init</c>-block name extraction:
        ///   1) read 12-byte name string at addr+4;
        ///   2) if empty / 0xFF / 0x00, fall back to TextForm.Direct via W2.
        /// </summary>
        static string ResolveSkillName(ROM rom, uint rowAddr)
        {
            if (rom == null) return "";
            try
            {
                // Try ascii name at addr+4 (12 bytes) first.
                if (U.isSafetyOffset(rowAddr + 4 + 12, rom))
                {
                    string name = rom.getString(rowAddr + 4, 12);
                    uint firstByte = rom.u8(rowAddr + 4);
                    if (name.Length > 0 && firstByte != 0xFF && firstByte != 0x00)
                    {
                        return name.Trim('\x00', '\xFF', ' ', '　');
                    }
                }

                // Fall back to TextForm.Direct via W2 + ParseTextToSkillName logic.
                if (!U.isSafetyOffset(rowAddr + 3, rom)) return "";
                uint textId = rom.u16(rowAddr + 2);
                if (textId == 0 || textId == 0xFFFF) return "";
                string text = NameResolver.GetTextById(textId);
                if (string.IsNullOrEmpty(text) || text == "???") return "";
                // Mirror WF ParseTextToSkillName: extract substring between
                // U+300E (LEFT WHITE CORNER BRACKET 『) and U+300F (RIGHT WHITE
                // CORNER BRACKET 』). NOTE: these are the white corner brackets
                // - distinct from the regular 「」 (U+300C / U+300D) which would
                // not match real FE8N skill texts.
                int start = text.IndexOf('『');
                int end = text.IndexOf('』', start + 1);
                if (start >= 0 && end > start) return text.Substring(start + 1, end - start - 1).Trim();
                return text;
            }
            catch { return ""; }
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (!U.isSafetyOffset(addr + (RowStride - 1), rom)) return;

            CurrentAddr = addr;

            // Lazy-resolve scan state if LoadList hasn't run yet (deep-linked nav).
            if (_iconPointers.Length == 0)
            {
                IconPointers = PreviewIconHelper.FindSkillFE8NVer1IconPointers();
                if (_iconPointers.Length > 0)
                {
                    uint slotAddr = _iconPointers[_selectedPointerIndex];
                    if (U.isSafetyOffset(slotAddr + 3, rom))
                    {
                        uint baseGba = rom.u32(slotAddr);
                        if (U.isSafetyPointer(baseGba))
                        {
                            CurrentSkillBaseAddress = U.toOffset(baseGba);
                        }
                    }
                }
            }

            if (_currentSkillBaseAddress > 0 && addr >= _currentSkillBaseAddress)
            {
                SelectedId = (addr - _currentSkillBaseAddress) / RowStride;
            }

            IconId = rom.u16(addr + 0);
            TextDetail = rom.u16(addr + 2);

            CondUnit1 = rom.u8(addr + 4);
            CondUnit2 = rom.u8(addr + 5);
            CondUnit3 = rom.u8(addr + 6);
            CondUnit4 = rom.u8(addr + 7);
            CondClass1 = rom.u8(addr + 8);
            CondClass2 = rom.u8(addr + 9);
            CondClass3 = rom.u8(addr + 10);
            CondClass4 = rom.u8(addr + 11);
            CondItem1 = rom.u8(addr + 12);
            CondItem2 = rom.u8(addr + 13);
            CondItem3 = rom.u8(addr + 14);
            CondItem4 = rom.u8(addr + 15);

            // Ext-tab bytes B16..B31 (#790 — now editable via Ext0..Ext15).
            // Assign through the properties (not the backing array) for
            // consistency with the other fields above, so IsLoading dirty-
            // tracking suppression applies uniformly. The View's UpdateUI()
            // pushes these into the NumericUpDowns — there is no AXAML binding.
            Ext0 = rom.u8(addr + 16u);
            Ext1 = rom.u8(addr + 17u);
            Ext2 = rom.u8(addr + 18u);
            Ext3 = rom.u8(addr + 19u);
            Ext4 = rom.u8(addr + 20u);
            Ext5 = rom.u8(addr + 21u);
            Ext6 = rom.u8(addr + 22u);
            Ext7 = rom.u8(addr + 23u);
            Ext8 = rom.u8(addr + 24u);
            Ext9 = rom.u8(addr + 25u);
            Ext10 = rom.u8(addr + 26u);
            Ext11 = rom.u8(addr + 27u);
            Ext12 = rom.u8(addr + 28u);
            Ext13 = rom.u8(addr + 29u);
            Ext14 = rom.u8(addr + 30u);
            Ext15 = rom.u8(addr + 31u);

            IsLoaded = true;
            CanWrite = true;
        }

        /// <summary>
        /// Write editable fields back to ROM. Mirrors WF InputFormRef
        /// auto-handlers:
        ///   - u16 iconId @ +0 (W0)
        ///   - u16 textId @ +2 (W2)
        ///   - u8 condUnit1..4 @ +4..+7
        ///   - u8 condClass1..4 @ +8..+11
        ///   - u8 condItem1..4 @ +12..+15
        ///   - u8 ext0..15 @ +16..+31 (B16..B31; #790)
        /// The B16..B31 ext-tab bytes are a fixed sub-region of the already
        /// allocated 32-byte row, so this is a PURE in-place write — no table
        /// relocation, no <c>DataExpansionCore</c>, no <c>RepointAllReferences</c>.
        ///
        /// Caller (the View) is expected to wrap this in a single
        /// <c>_undoService.Begin/Commit</c> scope.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            if (!U.isSafetyOffset(addr + (RowStride - 1), rom)) return;

            rom.write_u16(addr + 0, IconId);
            rom.write_u16(addr + 2, TextDetail);

            rom.write_u8(addr + 4, CondUnit1);
            rom.write_u8(addr + 5, CondUnit2);
            rom.write_u8(addr + 6, CondUnit3);
            rom.write_u8(addr + 7, CondUnit4);
            rom.write_u8(addr + 8, CondClass1);
            rom.write_u8(addr + 9, CondClass2);
            rom.write_u8(addr + 10, CondClass3);
            rom.write_u8(addr + 11, CondClass4);
            rom.write_u8(addr + 12, CondItem1);
            rom.write_u8(addr + 13, CondItem2);
            rom.write_u8(addr + 14, CondItem3);
            rom.write_u8(addr + 15, CondItem4);

            // Ext-tab bytes B16..B31 (#790). PURE in-place write at addr+16..+31;
            // the +31 bound is already covered by the RowStride-1 guard above.
            for (uint i = 0; i < 16; i++)
            {
                rom.write_u8(addr + 16u + i, _ext[i]);
            }
        }

        public void Initialize() { IsLoaded = true; }

        public int GetListCount()
        {
            if (ReadCount > 0) return (int)ReadCount;
            return LoadList().Count;
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["SelectedId"] = $"0x{SelectedId:X02}",
                ["PointerIndex"] = $"{SelectedPointerIndex}/{_iconPointers.Length}",
                ["IconId"] = $"0x{IconId:X04}",
                ["TextDetail"] = $"0x{TextDetail:X04}",
                ["CondUnit1"] = $"0x{CondUnit1:X02}",
                ["CondUnit2"] = $"0x{CondUnit2:X02}",
                ["CondUnit3"] = $"0x{CondUnit3:X02}",
                ["CondUnit4"] = $"0x{CondUnit4:X02}",
                ["CondClass1"] = $"0x{CondClass1:X02}",
                ["CondClass2"] = $"0x{CondClass2:X02}",
                ["CondClass3"] = $"0x{CondClass3:X02}",
                ["CondClass4"] = $"0x{CondClass4:X02}",
                ["CondItem1"] = $"0x{CondItem1:X02}",
                ["CondItem2"] = $"0x{CondItem2:X02}",
                ["CondItem3"] = $"0x{CondItem3:X02}",
                ["CondItem4"] = $"0x{CondItem4:X02}",
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
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u8@0x04"] = $"0x{rom.u8(a + 4):X02}",
                ["u8@0x05"] = $"0x{rom.u8(a + 5):X02}",
                ["u8@0x06"] = $"0x{rom.u8(a + 6):X02}",
                ["u8@0x07"] = $"0x{rom.u8(a + 7):X02}",
                ["u8@0x08"] = $"0x{rom.u8(a + 8):X02}",
                ["u8@0x09"] = $"0x{rom.u8(a + 9):X02}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
                ["u8@0x0C"] = $"0x{rom.u8(a + 12):X02}",
                ["u8@0x0D"] = $"0x{rom.u8(a + 13):X02}",
                ["u8@0x0E"] = $"0x{rom.u8(a + 14):X02}",
                ["u8@0x0F"] = $"0x{rom.u8(a + 15):X02}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["IconId"] = "u16@0x00",
            ["TextDetail"] = "u16@0x02",
            ["CondUnit1"] = "u8@0x04",
            ["CondUnit2"] = "u8@0x05",
            ["CondUnit3"] = "u8@0x06",
            ["CondUnit4"] = "u8@0x07",
            ["CondClass1"] = "u8@0x08",
            ["CondClass2"] = "u8@0x09",
            ["CondClass3"] = "u8@0x0A",
            ["CondClass4"] = "u8@0x0B",
            ["CondItem1"] = "u8@0x0C",
            ["CondItem2"] = "u8@0x0D",
            ["CondItem3"] = "u8@0x0E",
            ["CondItem4"] = "u8@0x0F",
        };
    }
}
