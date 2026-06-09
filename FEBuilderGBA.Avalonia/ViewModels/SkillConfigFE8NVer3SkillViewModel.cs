// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel backing <see cref="Views.SkillConfigFE8NVer3SkillView"/>.
    /// Phase 1/2/4/5/6 gap-sweep parity raise (#392).
    ///
    /// FE8N v3 is the fourth skill-patch flavour (sibling of FE8N v2,
    /// distinct from SkillSystems and CSkillSys 0.9.x). Unlike v2 which
    /// uses a byte-pattern grep to locate the icon pointer array, v3
    /// reads FIXED ROM OFFSETS:
    /// <list type="bullet">
    ///   <item><description><c>rom.u32(0x89268+4)</c> -> iconExPointer sentinel
    ///   (must be a safe GBA pointer; absence = patch not installed).</description></item>
    ///   <item><description><c>rom.u32(0x892A8+4)</c> -> skill-table GBA pointer.
    ///   The WF <c>g_SkillBaseAddress</c> is the SLOT ADDRESS (0x892AC),
    ///   not the dereferenced offset.</description></item>
    ///   <item><description><c>rom.u32(0x892A8+8)</c> -> ICON_LIST_SIZE (row stride).
    ///   Range 24..100, multiple of 4 (Copilot plan-review #1: 24 is the
    ///   smallest stride that fits D4/D8/D12/D16/D20 - anything less
    ///   aliases CompositeSkillPointer onto the next row).</description></item>
    ///   <item><description><c>rom.u32(0x892A8+20)</c> -> anime-table GBA pointer.</description></item>
    /// </list>
    ///
    /// Row layout (sizeof-24+, fixed v3 structure):
    /// <code>
    /// u16 textId @ +0
    /// u16 palette @ +2
    /// u32 unit-skill-pointer (P4) @ +4
    /// u32 class-skill-pointer (P8) @ +8
    /// u32 item-skill-pointer (P12) @ +12
    /// u32 item2-skill-pointer (P16) @ +16
    /// u32 composite-skill-pointer (P20) @ +20
    /// </code>
    ///
    /// Caller (the View) is expected to wrap <see cref="Write"/> in a single
    /// <c>_undoService.Begin/Commit</c> scope per the single-owner contract.
    ///
    /// Declared <c>partial</c> so the Phase 4 navigation manifest can live in
    /// a sibling <c>.NavigationTargets.cs</c> file.
    /// </summary>
    public partial class SkillConfigFE8NVer3SkillViewModel : ViewModelBase, IDataVerifiable
    {
        // Default row stride; overridden at LoadList time. 24 is the minimum
        // valid stride for the v3 layout (fits D4/D8/D12/D16/D20).
        const uint DEFAULT_SIZE = 24;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _selectedId;

        // Read-config bar (WF panel3).
        uint _readStartAddress;
        uint _readCount;

        // Cached scan results. _skillPointerLocation = U.NOT_FOUND until LoadList
        // (or a deep-linked LoadEntry) re-runs the scan.
        uint _skillPointerLocation = U.NOT_FOUND;
        uint _skillBaseAddress;
        uint _animeBaseAddress;
        uint _iconListSize = DEFAULT_SIZE;

        // Per-row editable fields.
        uint _textDetail;
        uint _palette;
        uint _unitSkillPointer;
        uint _classSkillPointer;
        uint _itemSkillPointer;
        uint _item2SkillPointer;
        uint _compositeSkillPointer;
        uint _animationPointer;
        bool _isAnimationValid;

        // Animation preview state.
        uint _selectedFrame;
        bool _showZoomed = true;
        string _binInfoText = "";

        // Status banner rendered when the patch is missing.
        string _statusMessage = "Skill system editors require the FE8N v3 skill patch to be installed.\nUse the Patch Manager to install it first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }

        /// <summary>
        /// The skill-row base address of the currently loaded entry (== the
        /// <c>addr</c> last passed to <see cref="LoadEntry"/>). The host view's
        /// embedded sub-list editors load their pointer SLOTs at
        /// <c>CurrentRowAddr + 4/8/12/16/20</c>; after any sub-list op the view
        /// re-runs <see cref="LoadEntry"/>(CurrentRowAddr) to re-sync the cached
        /// Px offsets (issue #930 C1).
        /// </summary>
        public uint CurrentRowAddr => _currentAddr;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }

        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }

        /// <summary>
        /// Detected row stride (a.k.a. <c>ICON_LIST_SIZE</c>). Range 24..100,
        /// multiple of 4. Defaults to 24 before the first LoadList.
        /// </summary>
        public uint IconListSize { get => _iconListSize; set => SetField(ref _iconListSize, value); }

        public uint BlockSize => _iconListSize;

        /// <summary>
        /// Address of the iconPointers slot that holds the skill-table GBA
        /// pointer (WF <c>g_SkillBaseAddress</c> = 0x892AC). Populated by
        /// <see cref="LoadList"/>; <see cref="U.NOT_FOUND"/> if the patch
        /// isn't installed.
        /// </summary>
        public uint SkillPointerLocation { get => _skillPointerLocation; set => SetField(ref _skillPointerLocation, value); }

        /// <summary>
        /// Dereferenced skill-table base offset (0 if patch isn't installed).
        /// </summary>
        public uint SkillBaseAddress { get => _skillBaseAddress; set => SetField(ref _skillBaseAddress, value); }

        /// <summary>
        /// Dereferenced anime-pointer-table base offset (0 if patch isn't
        /// installed or the anime table couldn't be resolved).
        /// </summary>
        public uint AnimeBaseAddress { get => _animeBaseAddress; set => SetField(ref _animeBaseAddress, value); }

        public uint TextDetail { get => _textDetail; set => SetField(ref _textDetail, value); }
        public uint Palette { get => _palette; set => SetField(ref _palette, value); }
        public uint UnitSkillPointer { get => _unitSkillPointer; set => SetField(ref _unitSkillPointer, value); }
        public uint ClassSkillPointer { get => _classSkillPointer; set => SetField(ref _classSkillPointer, value); }
        public uint ItemSkillPointer { get => _itemSkillPointer; set => SetField(ref _itemSkillPointer, value); }
        public uint Item2SkillPointer { get => _item2SkillPointer; set => SetField(ref _item2SkillPointer, value); }
        public uint CompositeSkillPointer { get => _compositeSkillPointer; set => SetField(ref _compositeSkillPointer, value); }

        /// <summary>
        /// u32 animation pointer for the currently selected skill, in
        /// ROM-OFFSET form (WF parity contract: <c>p32</c> strips the
        /// 0x08000000 high bit, <c>write_p32</c> re-adds it). 0 = no
        /// animation. Read from <c>animeBase + 4 * id</c>.
        /// </summary>
        public uint AnimationPointer { get => _animationPointer; set => SetField(ref _animationPointer, value); }
        public bool IsAnimationValid { get => _isAnimationValid; set => SetField(ref _isAnimationValid, value); }

        public uint SelectedFrame { get => _selectedFrame; set => SetField(ref _selectedFrame, value); }
        public bool ShowZoomed { get => _showZoomed; set => SetField(ref _showZoomed, value); }
        public string BinInfoText { get => _binInfoText; set => SetField(ref _binInfoText, value ?? ""); }

        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        /// <summary>
        /// Reset cached scan state AND per-row state so a subsequent LoadList
        /// re-runs the scan from scratch (e.g. after a Patch install/uninstall
        /// mid-session, or after a failed scan that left stale stride/base
        /// fields populated). Addresses Copilot bot PR-review thread #2
        /// (round 2): the prior reset left IconListSize / SkillBaseAddress /
        /// AnimeBaseAddress / SkillPointerLocation intact, allowing
        /// LoadEntry to use a stale stride for bounds/ID arithmetic.
        /// </summary>
        void ResetDerivedListState()
        {
            ReadStartAddress = 0;
            ReadCount = 0;

            CurrentAddr = 0;
            SelectedId = 0;
            IsLoaded = false;
            CanWrite = false;
            TextDetail = 0;
            Palette = 0;
            UnitSkillPointer = 0;
            ClassSkillPointer = 0;
            ItemSkillPointer = 0;
            Item2SkillPointer = 0;
            CompositeSkillPointer = 0;
            AnimationPointer = 0;
            IsAnimationValid = false;
            SelectedFrame = 0;
            BinInfoText = "";

            // Clear cached scan-derived state too so a subsequent LoadList
            // call re-resolves the stride/base addresses from a clean slate.
            SkillPointerLocation = U.NOT_FOUND;
            SkillBaseAddress = 0;
            AnimeBaseAddress = 0;
            IconListSize = DEFAULT_SIZE;
        }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            // Force a fresh scan on every LoadList (cheap with fixed offsets
            // and avoids stale pointers after a Patch install/uninstall).
            PreviewIconHelper.ResetFE8NVer3Cache();

            uint loc = PreviewIconHelper.FindSkillFE8NVer3SkillPointerLocation();
            uint baseAddr = PreviewIconHelper.FindSkillFE8NVer3SkillBaseAddress();
            uint animeBase = PreviewIconHelper.FindSkillFE8NVer3AnimeBaseAddress();
            uint stride = PreviewIconHelper.GetFE8NVer3IconListSize();

            SkillPointerLocation = loc;
            SkillBaseAddress = baseAddr;
            AnimeBaseAddress = animeBase;
            // Out-of-range strides shouldn't occur (the helper validates
            // them), but defensively fail-closed if so. Note: 24 is the
            // floor for v3 (Copilot plan-review #1).
            if (stride < 24 || stride > 100 || stride % 4 != 0)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }
            IconListSize = stride;

            if (loc == U.NOT_FOUND || baseAddr == 0)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            ReadStartAddress = baseAddr;

            var result = new List<AddrResult>();
            // WF iteration predicate: terminate when u8(addr) == 0xFF.
            for (uint i = 0; ; i++)
            {
                uint addr = baseAddr + i * stride;
                if (addr + stride > (uint)rom.Data.Length) break;
                // Cap iteration at a sane maximum even if a corrupt ROM
                // never returns 0xFF.
                if (i >= 256) break;
                if (rom.u8(addr) == 0xFF) break;

                string skillName = ResolveSkillName(rom, baseAddr, i, stride);
                string label = skillName.Length > 0
                    ? $"0x{i:X02} {skillName}"
                    : $"0x{i:X02}";
                result.Add(new AddrResult(addr, label, i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        /// <summary>
        /// Resolve the human-readable skill name for the given entry id.
        /// Mirrors WinForms <c>SkillConfigFE8NVer3SkillForm.GetSkillText(id)</c>:
        ///   1) read u16 textId @ baseAddr + stride * id;
        ///   2) lookup via TextForm.Direct;
        ///   3) split on colon to extract the leading "Name" prefix.
        /// </summary>
        /// <summary>
        /// Public instance resolver for the Composite (N5) sub-list tab (issue
        /// #930 B1). A composite-skill id indexes the SAME FE8N main-list table,
        /// so it resolves to the main-list 『...』 skill text via
        /// <see cref="ResolveSkillName"/> using the cached skill-table base
        /// (<see cref="ReadStartAddress"/> / <see cref="SkillBaseAddress"/>) and
        /// stride (<see cref="IconListSize"/>). This is deliberately NOT
        /// <c>NameResolver.GetSkillName</c> (which delegates to the global
        /// <c>CoreState.SkillNameResolver</c> and would mislabel). Returns an
        /// empty string if the base/stride aren't resolved yet.
        /// </summary>
        public string ResolveCompositeName(uint id)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return "";
            uint baseAddr = _readStartAddress != 0 ? _readStartAddress : _skillBaseAddress;
            if (baseAddr == 0) return "";
            return ResolveSkillName(rom, baseAddr, id, _iconListSize);
        }

        static string ResolveSkillName(ROM rom, uint baseAddr, uint id, uint stride)
        {
            if (rom == null) return "";
            try
            {
                if (!U.isSafetyOffset(baseAddr, rom)) return "";
                uint entryAddr = baseAddr + stride * id;
                if (!U.isSafetyOffset(entryAddr + 1, rom)) return "";
                uint textId = rom.u16(entryAddr);
                if (textId == 0 || textId == 0xFFFF) return "";
                string text = NameResolver.GetTextById(textId);
                if (string.IsNullOrEmpty(text) || text == "???") return "";
                // ParseTextToSkillName parity: extract substring between
                // U+300E (『) and U+300F (』) - the FE skill-name delimiters.
                // Addresses Copilot bot PR-review thread #1 (round 2): the
                // prior colon-split heuristic produced different labels than WF.
                const string Open = "『";   // 『
                const string Close = "』";  // 』
                int start = text.IndexOf(Open, StringComparison.Ordinal);
                if (start < 0) return "";
                start += Open.Length;
                int end = text.IndexOf(Close, start, StringComparison.Ordinal);
                if (end < 0) return "";
                return text.Substring(start, end - start);
            }
            catch { return ""; }
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            uint stride = _iconListSize;
            if (addr + stride > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Lazy-resolve scan state if LoadList hasn't run yet (deep-linked
            // NavigateTo). Without this, deep-link nav would render with all
            // pointer fields at 0.
            if (_skillPointerLocation == U.NOT_FOUND)
            {
                SkillPointerLocation = PreviewIconHelper.FindSkillFE8NVer3SkillPointerLocation();
                SkillBaseAddress = PreviewIconHelper.FindSkillFE8NVer3SkillBaseAddress();
                AnimeBaseAddress = PreviewIconHelper.FindSkillFE8NVer3AnimeBaseAddress();
                uint detectedStride = PreviewIconHelper.GetFE8NVer3IconListSize();
                if (detectedStride >= 24 && detectedStride <= 100 && detectedStride % 4 == 0)
                {
                    IconListSize = detectedStride;
                    stride = detectedStride;
                }
            }

            if (ReadStartAddress == 0 && _skillBaseAddress != 0)
            {
                ReadStartAddress = _skillBaseAddress;
            }
            SelectedId = ReadStartAddress > 0 && addr >= ReadStartAddress
                ? (addr - ReadStartAddress) / stride
                : 0;

            // Row fields.
            TextDetail = rom.u16(addr + 0);
            Palette = rom.u16(addr + 2);

            UnitSkillPointer = ReadPointerOffset(rom, addr + 4);
            ClassSkillPointer = ReadPointerOffset(rom, addr + 8);
            ItemSkillPointer = ReadPointerOffset(rom, addr + 12);
            Item2SkillPointer = ReadPointerOffset(rom, addr + 16);
            CompositeSkillPointer = ReadPointerOffset(rom, addr + 20);

            // Animation pointer at animeBase + 4*id.
            uint animPtr = 0;
            if (_animeBaseAddress != 0 && U.isSafetyOffset(_animeBaseAddress, rom))
            {
                uint animeSlot = _animeBaseAddress + 4 * SelectedId;
                if (U.isSafetyOffset(animeSlot + 3, rom))
                {
                    animPtr = rom.p32(animeSlot);
                }
            }
            AnimationPointer = animPtr;
            IsAnimationValid = animPtr != 0 && U.isSafetyOffset(animPtr, rom);

            SelectedFrame = 0;
            // Runtime UI string runs through R._ so the ja/zh translation
            // entries pick it up (ViewTranslationHelper only translates AXAML
            // literals, not VM-set TextBox values). The per-frame preview is
            // rendered by the View via SkillConfigAnimePreview /
            // SkillSystemsAnimeExportCore (#1010), so the BinInfoText is now
            // just the factual address.
            BinInfoText = IsAnimationValid
                ? string.Format(R._("Animation @ 0x{0:X08}"), animPtr)
                : "";

            IsLoaded = true;
            CanWrite = true;
        }

        /// <summary>
        /// Read a u32 at the given offset and convert it to a ROM offset
        /// (strip the 0x08000000 high bit). Returns 0 if the slot is out of
        /// range or holds an unsafe value.
        /// </summary>
        static uint ReadPointerOffset(ROM rom, uint slotAddr)
        {
            if (!U.isSafetyOffset(slotAddr + 3, rom)) return 0;
            uint raw = rom.u32(slotAddr);
            if (raw == 0) return 0;
            if (!U.isSafetyPointer(raw)) return 0;
            return U.toOffset(raw);
        }

        /// <summary>
        /// Write the editable fields back to ROM. Mirrors WinForms
        /// <c>WriteButton_Click</c> + InputFormRef auto-handler dual behaviour:
        /// (a) u16 textId @ +0
        /// (b) u16 palette @ +2
        /// (c) u32 unit-skill-pointer @ +4 (via write_p32)
        /// (d) u32 class-skill-pointer @ +8 (via write_p32)
        /// (e) u32 item-skill-pointer @ +12 (via write_p32)
        /// (f) u32 item2-skill-pointer @ +16 (via write_p32)
        /// (g) u32 composite-skill-pointer @ +20 (via write_p32)
        /// (h) u32 animation pointer @ animeBase + 4*id (via write_p32)
        ///
        /// Caller (the View) is expected to wrap this call in a single
        /// <c>_undoService.Begin/Commit</c> scope per the single-owner contract.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            uint stride = _iconListSize;

            if (addr + stride > (uint)rom.Data.Length) return;

            // (a) + (b) text id + palette.
            rom.write_u16(addr + 0, TextDetail);
            rom.write_u16(addr + 2, Palette);

            // (c)..(g) sub-pointers.
            WritePointerOffset(rom, addr + 4, UnitSkillPointer);
            WritePointerOffset(rom, addr + 8, ClassSkillPointer);
            WritePointerOffset(rom, addr + 12, ItemSkillPointer);
            WritePointerOffset(rom, addr + 16, Item2SkillPointer);
            WritePointerOffset(rom, addr + 20, CompositeSkillPointer);

            // (h) animation pointer.
            if (_animeBaseAddress == 0) return;
            if (!U.isSafetyOffset(_animeBaseAddress, rom)) return;
            uint animeSlot = _animeBaseAddress + 4 * SelectedId;
            if (!U.isSafetyOffset(animeSlot + 3, rom)) return;
            rom.write_p32(animeSlot, AnimationPointer);
        }

        /// <summary>
        /// Write a u32 pointer slot. The editor value is a ROM OFFSET; we
        /// serialize it via <c>write_p32</c> which OR-s in the 0x08000000
        /// high bit so the slot holds a GBA pointer (matches WF reader).
        /// Treats offset==0 as "null pointer" and writes a literal 0 (so
        /// the WF reader sees a clean null, not 0x08000000).
        /// </summary>
        static void WritePointerOffset(ROM rom, uint slotAddr, uint offset)
        {
            if (!U.isSafetyOffset(slotAddr + 3, rom)) return;
            if (offset == 0)
            {
                rom.write_u32(slotAddr, 0);
            }
            else
            {
                rom.write_p32(slotAddr, offset);
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
                ["IconListSize"] = $"{IconListSize}",
                ["TextDetail"] = $"0x{TextDetail:X04}",
                ["Palette"] = $"0x{Palette:X04}",
                ["UnitSkillPointer"] = $"0x{UnitSkillPointer:X08}",
                ["ClassSkillPointer"] = $"0x{ClassSkillPointer:X08}",
                ["ItemSkillPointer"] = $"0x{ItemSkillPointer:X08}",
                ["Item2SkillPointer"] = $"0x{Item2SkillPointer:X08}",
                ["CompositeSkillPointer"] = $"0x{CompositeSkillPointer:X08}",
                ["AnimationPointer"] = $"0x{AnimationPointer:X08}",
                ["IsAnimationValid"] = IsAnimationValid ? "true" : "false",
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
                ["u32@0x04"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@0x08"] = $"0x{rom.u32(a + 8):X08}",
                ["u32@0x0C"] = $"0x{rom.u32(a + 12):X08}",
                ["u32@0x10"] = $"0x{rom.u32(a + 16):X08}",
                ["u32@0x14"] = $"0x{rom.u32(a + 20):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["TextDetail"] = "u16@0x00",
            ["Palette"] = "u16@0x02",
            ["UnitSkillPointer"] = "u32@0x04",
            ["ClassSkillPointer"] = "u32@0x08",
            ["ItemSkillPointer"] = "u32@0x0C",
            ["Item2SkillPointer"] = "u32@0x10",
            ["CompositeSkillPointer"] = "u32@0x14",
        };
    }
}
