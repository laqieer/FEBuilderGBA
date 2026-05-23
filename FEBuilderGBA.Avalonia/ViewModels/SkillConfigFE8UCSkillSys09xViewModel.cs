using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel backing <see cref="Views.SkillConfigFE8UCSkillSys09xView"/>.
    /// Replaces the previous dual `...ViewViewModel` + unused `...ViewModel`
    /// stub pair so the canonical name matches the JumpParityScanner's
    /// `XxxViewModel -> XxxView` derivation (gap-sweep #430).
    ///
    /// Declared <c>partial</c> so the Phase 4 navigation manifest can live
    /// in a sibling <c>.NavigationTargets.cs</c> file.
    /// </summary>
    public partial class SkillConfigFE8UCSkillSys09xViewModel : ViewModelBase, IDataVerifiable
    {
        // CSkillSys 0.9.x fixed addresses (mirrors WinForms
        // SkillConfigCSkillSystem09xForm constants).
        public const uint gpSkillInfos = 0xB2A614;
        public const uint gpEfxSkillAnims = 0xB2A630;
        public const uint SkillPalettePointer = 0x22370;
        public const uint SIZE = 8;
        public const uint MAX_COUNT = 0x400;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _selectedId;

        // Read-config bar (WF panel3).
        uint _readStartAddress;
        uint _readCount;

        // Skill info entry fields (8 bytes: u32 icon ptr / u16 nameMsg / u16 descMsg).
        uint _iconAddr;
        uint _skillNameMsg;
        uint _descriptionMsg;
        string _skillNameText = "";
        string _descriptionText = "";

        // Animation pointer (read from gpEfxSkillAnims + 4 * id).
        uint _animationPointer;
        bool _isAnimationValid;

        // Animation preview state.
        uint _selectedFrame;
        bool _showZoomed = true;
        string _binInfoText = "";

        // Status message rendered by the Avalonia view when the patch is missing.
        string _statusMessage = "Skill system editors require a compatible skill patch to be installed.\nUse the Patch Manager to install a skill system patch first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }

        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint BlockSize => SIZE;

        public uint IconAddr { get => _iconAddr; set => SetField(ref _iconAddr, value); }
        public uint SkillNameMsg { get => _skillNameMsg; set => SetField(ref _skillNameMsg, value); }
        public uint DescriptionMsg { get => _descriptionMsg; set => SetField(ref _descriptionMsg, value); }
        public string SkillNameText { get => _skillNameText; set => SetField(ref _skillNameText, value ?? ""); }
        public string DescriptionText { get => _descriptionText; set => SetField(ref _descriptionText, value ?? ""); }

        public uint AnimationPointer { get => _animationPointer; set => SetField(ref _animationPointer, value); }
        public bool IsAnimationValid { get => _isAnimationValid; set => SetField(ref _isAnimationValid, value); }

        public uint SelectedFrame { get => _selectedFrame; set => SetField(ref _selectedFrame, value); }
        public bool ShowZoomed { get => _showZoomed; set => SetField(ref _showZoomed, value); }
        public string BinInfoText { get => _binInfoText; set => SetField(ref _binInfoText, value ?? ""); }

        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// Resolve the human-readable skill name for the given entry id.
        /// Mirrors WinForms `SkillConfigCSkillSystem09xForm.GetSkillName(index)`:
        /// 1) read u16 nameMsg @ +4 of the skill info row;
        /// 2) if nameMsg != 0, look up the text;
        /// 3) otherwise fall back to extracting the prefix from the description
        ///    text (colon-split, like WF `SkillTextToName`).
        /// </summary>
        public static string ResolveSkillName(ROM rom, uint id)
        {
            if (rom == null) return "";
            try
            {
                if (!U.isSafetyOffset(gpSkillInfos + 4, rom)) return "";
                uint baseAddr = rom.p32(gpSkillInfos);
                if (!U.isSafetyOffset(baseAddr, rom)) return "";
                uint entryAddr = baseAddr + SIZE * id;
                if (!U.isSafetyOffset(entryAddr + 6, rom)) return "";

                uint nameMsg = rom.u16(entryAddr + 4);
                if (nameMsg != 0)
                {
                    string text = NameResolver.GetTextById(nameMsg);
                    if (!string.IsNullOrEmpty(text) && text != "???") return text;
                }

                // Fallback: pull the prefix out of the description text.
                uint descMsg = rom.u16(entryAddr + 6);
                if (descMsg != 0)
                {
                    string desc = NameResolver.GetTextById(descMsg);
                    if (!string.IsNullOrEmpty(desc) && desc != "???")
                    {
                        int colon = desc.IndexOf(':');
                        if (colon > 0) return desc.Substring(0, colon).Trim();
                    }
                }
            }
            catch { /* swallow - return empty string */ }
            return "";
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            // The skill-info table base is at the fixed pointer gpSkillInfos.
            // CSkillSys 0.9.x ROMs that don't have the patch installed will
            // have an invalid pointer here, so we bail safely instead of
            // populating bogus rows.
            if (!U.isSafetyOffset(gpSkillInfos + 4, rom)) return new List<AddrResult>();
            uint baseAddr = rom.p32(gpSkillInfos);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            ReadStartAddress = baseAddr;

            var result = new List<AddrResult>();
            // Mirror WinForms predicate `i < 0x400` - iterate ALL rows. Do NOT
            // early-exit on null/invalid mid-table entries (Copilot CLI plan
            // review finding 5). The only bound is ROM length.
            for (uint i = 0; i < MAX_COUNT; i++)
            {
                uint addr = baseAddr + i * SIZE;
                if (addr + SIZE > (uint)rom.Data.Length) break;

                string skillName = ResolveSkillName(rom, i);
                string label = skillName.Length > 0
                    ? $"0x{i:X02} {skillName}"
                    : $"0x{i:X02}";
                result.Add(new AddrResult(addr, label, i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Derive SelectedId from (addr - ReadStartAddress) / SIZE; if
            // LoadList hasn't run yet (deep-linked NavigateTo), resolve the
            // base lazily.
            if (ReadStartAddress == 0 && U.isSafetyOffset(gpSkillInfos + 4, rom))
            {
                uint baseAddr = rom.p32(gpSkillInfos);
                if (U.isSafetyOffset(baseAddr, rom)) ReadStartAddress = baseAddr;
            }
            SelectedId = ReadStartAddress > 0 && addr >= ReadStartAddress
                ? (addr - ReadStartAddress) / SIZE
                : 0;

            IconAddr = rom.u32(addr + 0);
            SkillNameMsg = rom.u16(addr + 4);
            DescriptionMsg = rom.u16(addr + 6);

            SkillNameText = SkillNameMsg != 0 ? NameResolver.GetTextById(SkillNameMsg) : "";
            DescriptionText = DescriptionMsg != 0 ? NameResolver.GetTextById(DescriptionMsg) : "";

            // Resolve animation pointer at gpEfxSkillAnims + 4 * id.
            //
            // CRITICAL parity contract (Copilot CLI review on PR #516):
            // WinForms stores the editor value as a ROM OFFSET, not a raw
            // GBA pointer. The WF load does `Program.ROM.p32(anime)` which
            // both reads the u32 and converts it from GBA-pointer form
            // (high bit set) to a ROM offset; the WF write does
            // `Program.ROM.write_p32(anime, value)` which serializes the
            // offset back as a GBA pointer. Our VM MUST mirror that
            // contract or a load+write cycle will corrupt the slot.
            uint animPtr = 0;
            if (U.isSafetyOffset(gpEfxSkillAnims + 4, rom))
            {
                uint animBase = rom.p32(gpEfxSkillAnims);
                if (U.isSafetyOffset(animBase, rom))
                {
                    uint animSlot = animBase + 4 * SelectedId;
                    if (U.isSafetyOffset(animSlot + 4, rom))
                    {
                        // p32 = u32 + GBA->offset conversion. The returned
                        // value is the offset (e.g. 0x00100000), not the
                        // raw pointer (e.g. 0x08100000).
                        animPtr = rom.p32(animSlot);
                    }
                }
            }
            AnimationPointer = animPtr;

            // AnimationPointer is already an offset (p32 above did the
            // conversion). Don't toOffset it again — that's a no-op for
            // already-offset values but matches WF intent.
            IsAnimationValid = animPtr != 0 && U.isSafetyOffset(animPtr, rom);

            SelectedFrame = 0;
            BinInfoText = IsAnimationValid
                ? $"Animation @ 0x{animPtr:X08} (preview unavailable - see #500)"
                : "";

            IsLoaded = true;
            CanWrite = true;
        }

        /// <summary>
        /// Write the editable fields back to ROM:
        ///   (a) u16 SkillNameMsg @ addr+4
        ///   (b) u16 DescriptionMsg @ addr+6
        ///   (c) u32 AnimationPointer @ gpEfxSkillAnims_base + 4 * id
        ///
        /// Mirrors the WF `WriteButton` dual-handler behavior: the auto-attached
        /// `InputFormRef.WriteHandler` writes W4 / W6 (Skill Name / Description
        /// text IDs), and the explicit `WriteButton_Click` writes the animation
        /// pointer to the separate gpEfxSkillAnims table.
        ///
        /// Caller (the View) is expected to wrap this call in a single
        /// `_undoService.Begin/Commit` scope per the single-owner contract.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            // (a) + (b) - skill name / description text IDs.
            if (addr + 8 <= (uint)rom.Data.Length)
            {
                rom.write_u16(addr + 4, SkillNameMsg);
                rom.write_u16(addr + 6, DescriptionMsg);
            }

            // (c) - animation pointer.
            //
            // AnimationPointer is held as a ROM OFFSET (see LoadEntry). The
            // WF write uses `Program.ROM.write_p32(anime, ANIMATION.Value)`
            // which converts the offset back to a GBA pointer (`U.toOffset` +
            // OR with 0x08000000) before serializing. We MUST use `write_p32`
            // here, not `write_u32`, or the slot will hold a raw offset
            // instead of a GBA pointer and break the WF reader.
            if (U.isSafetyOffset(gpEfxSkillAnims + 4, rom))
            {
                uint animBase = rom.p32(gpEfxSkillAnims);
                if (U.isSafetyOffset(animBase, rom))
                {
                    uint animSlot = animBase + 4 * SelectedId;
                    if (U.isSafetyOffset(animSlot + 4, rom))
                    {
                        rom.write_p32(animSlot, AnimationPointer);
                    }
                }
            }
        }

        public void Initialize() { IsLoaded = true; }

        /// <summary>
        /// Reports the count of rows this editor exposes. The cached
        /// <see cref="ReadCount"/> is populated by <see cref="LoadList"/> on
        /// every refresh; before the first LoadList we fall back to a fresh
        /// LoadList call (cheap on patch-missing ROMs - returns 0 quickly).
        /// Returning 0 unconditionally would have caused the IDataVerifiable
        /// sweeps to treat this as a sub-editor and mask list-population
        /// regressions (Copilot bot review on PR #516).
        /// </summary>
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
                ["IconAddr"] = $"0x{IconAddr:X08}",
                ["SkillNameMsg"] = $"0x{SkillNameMsg:X04}",
                ["DescriptionMsg"] = $"0x{DescriptionMsg:X04}",
                ["AnimationPointer"] = $"0x{AnimationPointer:X08}",
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
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06"] = $"0x{rom.u16(a + 6):X04}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["IconAddr"] = "u32@0x00",
            ["SkillNameMsg"] = "u16@0x04",
            ["DescriptionMsg"] = "u16@0x06",
        };
    }
}
