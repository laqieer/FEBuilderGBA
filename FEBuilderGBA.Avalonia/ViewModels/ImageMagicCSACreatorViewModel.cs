using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Avalonia counterpart of WinForms <c>ImageMagicCSACreatorForm</c>.
    /// CSA Magic Creator entry editor: each row holds 5 pointers (FrameData,
    /// OBJRightToLeft, OBJLeftToRight, OBJBGRightToLeft, OBJBGLeftToRight)
    /// at <c>CurrentAddr+0/4/8/12/16</c>, and a separate dim/no-dim/empty
    /// data-mode pointer stored at <c>TagAddress</c> (the slot in the
    /// magic-effect pointer table - mirrors WF <c>WriteDim</c> using
    /// <c>ar.tag</c> separately from <c>this.Address.Value</c>).
    ///
    /// Gap-sweep fix (#417) raises density from 3 to MEDIUM verdict and
    /// wires the tag-aware Dim/NoDim/Empty round-trip.
    /// </summary>
    public class ImageMagicCSACreatorViewModel : ViewModelBase, IDataVerifiable
    {
        // Block size for the CSA struct: 5 x 4-byte pointers = 20 bytes.
        public const uint BLOCK_SIZE = 20;

        // Field def for the 5 frame-data pointers (P0..P16).
        // All five fields are P-prefixed so EditorFormRef.ReadFields /
        // WriteFields round-trip via rom.p32 / rom.write_p32 (the high bit
        // is stripped on read and re-added on write - Copilot CLI plan
        // review #1 for ImageMagicCSACreator).
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "P0", "P4", "P8", "P12", "P16" });

        /// <summary>Public read-only view of the field defs (for parity tests).</summary>
        public static IReadOnlyList<EditorFormRef.FieldDef> FieldDefs => _fields;

        // ---- backing fields ----

        uint _currentAddr;
        uint _tagAddress;
        bool _isLoaded;
        uint _p0, _p4, _p8, _p12, _p16;
        uint _dimMode; // 0=dim_pc, 1=dim, 2=NULL(EMPTY)
        string _comment = "";
        uint _frame;
        uint _zoom;
        string _binInfo = "";

        uint _dimAddr = U.NOT_FOUND;
        uint _noDimAddr = U.NOT_FOUND;
        uint _csaTable = U.NOT_FOUND;
        uint _spellDataCount;
        MagicSystemKind _magicKind = MagicSystemKind.None;

        // ---- properties ----

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }

        /// <summary>
        /// Address of the pointer-table slot (the entry in the magic_effect
        /// pointer table where the dim/no-dim/empty data pointer is stored).
        /// In WinForms this is the <c>AddrResult.tag</c> field; the Avalonia
        /// flow carries it through <see cref="LoadEntry(uint, uint)"/> +
        /// <see cref="Write"/> so the dim selector edits the correct slot
        /// (Copilot CLI plan review #1).
        /// </summary>
        public uint TagAddress { get => _tagAddress; set => SetField(ref _tagAddress, value); }

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }
        public uint P12 { get => _p12; set => SetField(ref _p12, value); }
        public uint P16 { get => _p16; set => SetField(ref _p16, value); }

        /// <summary>0=dim_pc, 1=dim, 2=NULL(EMPTY). Mirrors WF DimComboBox.SelectedIndex.</summary>
        public uint DimMode { get => _dimMode; set => SetField(ref _dimMode, value); }

        public string Comment { get => _comment; set => SetField(ref _comment, value ?? ""); }
        public uint Frame { get => _frame; set => SetField(ref _frame, value); }
        public uint Zoom { get => _zoom; set => SetField(ref _zoom, value); }
        public string BinInfo { get => _binInfo; set => SetField(ref _binInfo, value ?? ""); }

        public uint BlockSize => BLOCK_SIZE;

        /// <summary>Resolved dim address (from <see cref="MagicCSACore.SearchMagicSystem"/>).</summary>
        public uint DimAddrResolved => _dimAddr;

        /// <summary>Resolved no-dim address (from <see cref="MagicCSACore.SearchMagicSystem"/>).</summary>
        public uint NoDimAddrResolved => _noDimAddr;

        /// <summary>Resolved CSA spell-table address.</summary>
        public uint CsaTableAddress => _csaTable;

        public MagicSystemKind MagicKind => _magicKind;

        /// <summary>Computed spell data count (mirrors WF SpellDataCount).</summary>
        public uint SpellDataCount => _spellDataCount;

        // ---- list / entry loading ----

        /// <summary>
        /// Build the entry list for the editor. Returns an empty list when
        /// the CSA magic system is absent (mirrors WF behaviour - the form
        /// shows a warning then renders an empty list). Side-effect: caches
        /// the dim/no-dim/csa addresses + spell-data count so subsequent
        /// LoadEntry / Write calls have the resolved targets.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                _magicKind = MagicSystemKind.None;
                _dimAddr = U.NOT_FOUND;
                _noDimAddr = U.NOT_FOUND;
                _csaTable = U.NOT_FOUND;
                _spellDataCount = 0;
                return result;
            }

            _magicKind = MagicCSACore.SearchMagicSystem(rom,
                out _, out _dimAddr, out _noDimAddr,
                out _csaTable, out _);

            if (_magicKind != MagicSystemKind.CsaCreator) return result;
            if (_csaTable == U.NOT_FOUND) return result;

            _spellDataCount = MagicCSACore.ComputeSpellDataCount(rom);
            var entries = MagicCSACore.ScanCsaEntries(rom, _magicKind,
                _dimAddr, _noDimAddr, _csaTable, _spellDataCount);

            foreach (var entry in entries)
            {
                result.Add(new AddrResult(entry.Addr, entry.Name, entry.TagAddr));
            }
            return result;
        }

        /// <summary>
        /// Load the CSA struct at <paramref name="addr"/> + dim mode from
        /// <paramref name="tagAddr"/>. The view's selection handler extracts
        /// the tag via <c>EntryList.SelectedItem?.tag</c> and passes it
        /// here so the dim selector edits the correct pointer-table slot
        /// (Copilot CLI plan review #1).
        /// </summary>
        public void LoadEntry(uint addr, uint tagAddr)
        {
            var rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + BLOCK_SIZE > (uint)rom.Data.Length) return;

            IsLoading = true;
            try
            {
                CurrentAddr = addr;
                TagAddress = tagAddr;

                var values = EditorFormRef.ReadFields(rom, addr, _fields);
                P0 = values["P0"];
                P4 = values["P4"];
                P8 = values["P8"];
                P12 = values["P12"];
                P16 = values["P16"];

                // Resolve dim mode from the data pointer stored at tagAddr.
                if (tagAddr != 0 && tagAddr + 4 <= (uint)rom.Data.Length)
                {
                    uint data = rom.p32(tagAddr);
                    if (data == _dimAddr) DimMode = 0;          // dim_pc
                    else if (data == _noDimAddr) DimMode = 1;   // dim
                    else DimMode = 2;                            // NULL(EMPTY)
                }
                else
                {
                    DimMode = 2;
                }

                // Load the user comment annotation from CommentCache (mirrors
                // WF Program.CommentCache.At(csaaddress) - Copilot CLI inline
                // review #3 on PR #547).
                Comment = CoreState.CommentCache?.At(addr, "") ?? "";

                BinInfo = $"u32@0=0x{rom.u32(addr + 0):X08} u32@4=0x{rom.u32(addr + 4):X08} u32@8=0x{rom.u32(addr + 8):X08} u32@12=0x{rom.u32(addr + 12):X08} u32@16=0x{rom.u32(addr + 16):X08}";

                IsLoaded = true;
            }
            finally
            {
                IsLoading = false;
                MarkClean();
            }
        }

        /// <summary>
        /// Single-argument overload for IDataVerifiable contract. Loads with
        /// tagAddr=0 (Dim mode will fall back to EMPTY).
        /// </summary>
        public void LoadEntry(uint addr) => LoadEntry(addr, 0);

        /// <summary>
        /// Write the five frame-data pointers to ROM at <see cref="CurrentAddr"/>,
        /// and write the dim/no-dim/empty pointer to ROM at
        /// <see cref="TagAddress"/>. The Dim mode write target is INTENTIONALLY
        /// separate from CurrentAddr - the dim pointer lives in the
        /// magic-effect pointer table (slot index * 4 from
        /// <c>rom.RomInfo.magic_effect_pointer</c>), NOT inside the CSA
        /// struct (Copilot CLI plan review #1).
        /// </summary>
        public void Write()
        {
            var rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            // 1) Write the 5 pointer fields.
            var values = new Dictionary<string, uint>
            {
                ["P0"] = P0, ["P4"] = P4, ["P8"] = P8, ["P12"] = P12, ["P16"] = P16,
            };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);

            // 2) Write the dim mode pointer to TagAddress (NOT CurrentAddr).
            // dim_pc -> _dimAddr; dim -> _noDimAddr; NULL(EMPTY) -> 0.
            if (TagAddress != 0 && TagAddress + 4 <= (uint)rom.Data.Length)
            {
                if (DimMode == 0 && _dimAddr != U.NOT_FOUND)
                {
                    rom.write_p32(TagAddress, _dimAddr);
                }
                else if (DimMode == 1 && _noDimAddr != U.NOT_FOUND)
                {
                    rom.write_p32(TagAddress, _noDimAddr);
                }
                else if (DimMode == 2)
                {
                    rom.write_u32(TagAddress, 0);
                }
            }

            // 3) Persist the user comment annotation back to CommentCache
            // (mirrors WF WriteMagicName -> Program.CommentCache.Update).
            // This is outside the ROM undo scope intentionally - the
            // CommentCache lives in the EtcCache layer, not the ROM bytes
            // (Copilot CLI inline review #4 on PR #547).
            CoreState.CommentCache?.Update(CurrentAddr, Comment ?? "");
        }

        // ---- IDataVerifiable ----

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["tag"] = $"0x{TagAddress:X08}",
                ["P0"] = $"0x{P0:X08}",
                ["P4"] = $"0x{P4:X08}",
                ["P8"] = $"0x{P8:X08}",
                ["P12"] = $"0x{P12:X08}",
                ["P16"] = $"0x{P16:X08}",
                ["DimMode"] = DimMode.ToString(),
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            var rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["tag_u32"] = TagAddress == 0 ? "0x00000000" : $"0x{rom.u32(TagAddress):X08}",
                ["u32@0"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@4"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@12"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@16"] = $"0x{rom.u32(a + 16):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["P0"] = "u32@0",
            ["P4"] = "u32@4",
            ["P8"] = "u32@8",
            ["P12"] = "u32@12",
            ["P16"] = "u32@16",
            ["DimMode"] = "tag_u32",
        };
    }
}
