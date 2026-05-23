// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel backing <see cref="Views.SkillConfigSkillSystemView"/>.
    /// Phase 1/2/4/5/6 gap-sweep parity raise (#427).
    ///
    /// The SkillSystems patch (FE8U skill expansion) stores skill metadata in
    /// THREE separate tables, dynamically located via byte-pattern scan of the
    /// ROM body (see <see cref="PreviewIconHelper.FindSkillSystemIconBaseAddress"/>,
    /// <see cref="PreviewIconHelper.FindSkillSystemTextPointerLocation"/>,
    /// <see cref="PreviewIconHelper.FindSkillSystemAnimePointerLocation"/>):
    ///
    ///  - ICON  : striped per-index 16x16 4bpp tiles at a single base offset.
    ///  - TEXT  : u16 text-id per index, 2 bytes per row.
    ///  - ANIME : u32 GBA pointer per index, 4 bytes per row, separate base.
    ///
    /// LoadEntry populates both the u16 textId and the u32 animation pointer
    /// (read via <c>p32</c>, so the editor value is a ROM OFFSET not a raw
    /// GBA pointer). Write persists BOTH fields under a single undo scope
    /// owned by the View code-behind (matches PR #516 pattern). Real image
    /// import/export and animation creator/editor depend on Core extraction
    /// of <c>ImageUtilSkillSystemsAnimeCreator</c> tracked by #500; the
    /// corresponding buttons render with a tooltip but no-op until that
    /// lands.
    ///
    /// Declared <c>partial</c> so the Phase 4 navigation manifest can live in
    /// a sibling <c>.NavigationTargets.cs</c> file.
    /// </summary>
    public partial class SkillConfigSkillSystemViewModel : ViewModelBase, IDataVerifiable
    {
        // WF predicate: i < 255 (mirrors SkillConfigSkillSystemForm.Init).
        public const uint MAX_COUNT = 255;
        public const uint SIZE = 2;

        uint _currentAddr;
        bool _isLoaded;
        bool _canWrite;
        uint _selectedId;

        // Read-config bar (WF panel3).
        uint _readStartAddress;
        uint _readCount;

        // Cached pointer locations from the dynamic scan. Used by Write so
        // we don't have to repeat the byte-pattern search on every keystroke.
        // U.NOT_FOUND means "not scanned yet" or "patch not installed".
        uint _textPointerLocation = U.NOT_FOUND;
        uint _animePointerLocation = U.NOT_FOUND;
        uint _iconBaseAddress;

        // Per-row editable fields.
        uint _textDetail;
        uint _animationPointer;
        bool _isAnimationValid;

        // Animation preview state (mirrors WF Show*Zoom* + Show*Frame* widgets).
        uint _selectedFrame;
        bool _showZoomed = true;
        string _binInfoText = "";

        // Status banner rendered when the patch is missing.
        string _statusMessage = "Skill system editors require the SkillSystems patch to be installed.\nUse the Patch Manager to install it first.";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint SelectedId { get => _selectedId; set => SetField(ref _selectedId, value); }

        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public uint ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint BlockSize => SIZE;

        /// <summary>
        /// Address of the post-pattern u32 holding the text-base GBA pointer.
        /// Equivalent to the WF `textPointer` argument. Populated by
        /// <see cref="LoadList"/>; <see cref="U.NOT_FOUND"/> if the patch
        /// isn't installed.
        /// </summary>
        public uint TextPointerLocation { get => _textPointerLocation; set => SetField(ref _textPointerLocation, value); }

        /// <summary>
        /// Address of the post-pattern u32 holding the anime-base GBA pointer.
        /// Equivalent to the WF `AnimeBaseAddress` field. Populated by
        /// <see cref="LoadList"/>; <see cref="U.NOT_FOUND"/> if the patch
        /// isn't installed.
        /// </summary>
        public uint AnimePointerLocation { get => _animePointerLocation; set => SetField(ref _animePointerLocation, value); }

        /// <summary>
        /// Cached icon-base offset (dereferenced). 0 if the patch isn't
        /// installed.
        /// </summary>
        public uint IconBaseAddress { get => _iconBaseAddress; set => SetField(ref _iconBaseAddress, value); }

        /// <summary>
        /// u16 text id for the currently selected skill.
        /// Read from <c>textBase + 2 * id</c>; written via raw
        /// <c>rom.write_u16(addr, _)</c> in <see cref="Write"/>.
        /// </summary>
        public uint TextDetail { get => _textDetail; set => SetField(ref _textDetail, value); }

        /// <summary>
        /// u32 animation pointer for the currently selected skill, in
        /// ROM-OFFSET form (the WF parity contract: <c>p32</c> strips the
        /// 0x08000000 high bit, <c>write_p32</c> re-adds it). 0 = no
        /// animation. Read from <c>animeBase + 4 * id</c>.
        /// </summary>
        public uint AnimationPointer { get => _animationPointer; set => SetField(ref _animationPointer, value); }

        public bool IsAnimationValid { get => _isAnimationValid; set => SetField(ref _isAnimationValid, value); }

        public uint SelectedFrame { get => _selectedFrame; set => SetField(ref _selectedFrame, value); }
        public bool ShowZoomed { get => _showZoomed; set => SetField(ref _showZoomed, value); }
        public string BinInfoText { get => _binInfoText; set => SetField(ref _binInfoText, value ?? ""); }

        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null)
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            // Re-scan on every LoadList; cheap and avoids stale pointers after
            // a Patch install/uninstall. Bail with an empty list if any of
            // the three required pointers is missing.
            uint textLoc = PreviewIconHelper.FindSkillSystemTextPointerLocation();
            uint animeLoc = PreviewIconHelper.FindSkillSystemAnimePointerLocation();
            uint iconBase = PreviewIconHelper.FindSkillSystemIconBaseAddress();

            TextPointerLocation = textLoc;
            AnimePointerLocation = animeLoc;
            IconBaseAddress = iconBase;

            if (textLoc == U.NOT_FOUND || animeLoc == U.NOT_FOUND || iconBase == 0)
            {
                // Copilot bot review: zero derived state on the early-return
                // path so the view doesn't surface stale start/count values
                // when the SkillSystems patch is uninstalled in the same
                // session.
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            uint textBase = rom.p32(textLoc);
            if (!U.isSafetyOffset(textBase, rom))
            {
                ResetDerivedListState();
                return new List<AddrResult>();
            }

            ReadStartAddress = textBase;

            var result = new List<AddrResult>();
            // WF predicate: `i < 255`. Iterate the full range; the only
            // bound is ROM length. We pass `textBase` into `ResolveSkillName`
            // so the per-row name lookup reuses the scan we already did,
            // instead of re-running the byte-pattern scan 255 times per
            // refresh (Copilot bot review: performance flag).
            for (uint i = 0; i < MAX_COUNT; i++)
            {
                uint addr = textBase + i * SIZE;
                if (addr + SIZE > (uint)rom.Data.Length) break;

                string skillName = ResolveSkillName(rom, textBase, i);
                string label = skillName.Length > 0
                    ? $"0x{i:X02} {skillName}"
                    : $"0x{i:X02}";
                result.Add(new AddrResult(addr, label, i));
            }
            ReadCount = (uint)result.Count;
            return result;
        }

        /// <summary>
        /// Zero the derived list state (ReadStartAddress, ReadCount) so
        /// callers querying <see cref="GetListCount"/> after a patch uninstall
        /// don't see stale counts.
        /// </summary>
        void ResetDerivedListState()
        {
            ReadStartAddress = 0;
            ReadCount = 0;
        }

        /// <summary>
        /// Resolve the human-readable skill name for the given entry id.
        /// Mirrors WinForms `SkillConfigSkillSystemForm.GetSkillName(index)`:
        /// 1) read u16 textId @ textBase + 2*id;
        /// 2) if textId != 0, look up the text and try `Name: Description`
        ///    colon-split (WF `SkillTextToName`);
        /// 3) otherwise return empty (the LoadDicResource fallback in WF
        ///    requires a translation file we don't currently consume from
        ///    Avalonia - same trade-off as the FE8N family).
        ///
        /// <paramref name="textBase"/> is passed in from <see cref="LoadList"/>
        /// (which already ran the byte-pattern scan once) so we don't repeat
        /// the full scan per row - that was a hot-path freeze flagged by
        /// Copilot bot review.
        /// </summary>
        static string ResolveSkillName(ROM rom, uint textBase, uint id)
        {
            if (rom == null) return "";
            try
            {
                if (!U.isSafetyOffset(textBase, rom)) return "";

                uint entryAddr = textBase + SIZE * id;
                if (!U.isSafetyOffset(entryAddr + SIZE - 1, rom)) return "";

                uint textId = rom.u16(entryAddr);
                if (textId == 0 || textId == 0xFFFF) return "";

                string text = NameResolver.GetTextById(textId);
                if (string.IsNullOrEmpty(text) || text == "???") return "";

                int colon = text.IndexOf(':');
                if (colon > 0) return text.Substring(0, colon).Trim();
                return text;
            }
            catch { return ""; }
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;

            // Lazily resolve TextPointerLocation / AnimePointerLocation /
            // IconBaseAddress if LoadList hasn't run yet (deep-linked
            // NavigateTo). Without lazy IconBaseAddress resolution, deep-
            // linked nav would render IconAddrLabel from a 0 base and the
            // icon preview would stay blank even on a patched ROM - Copilot
            // bot review on PR #525.
            if (_textPointerLocation == U.NOT_FOUND)
                TextPointerLocation = PreviewIconHelper.FindSkillSystemTextPointerLocation();
            if (_animePointerLocation == U.NOT_FOUND)
                AnimePointerLocation = PreviewIconHelper.FindSkillSystemAnimePointerLocation();
            if (_iconBaseAddress == 0)
                IconBaseAddress = PreviewIconHelper.FindSkillSystemIconBaseAddress();

            // Derive SelectedId from (addr - textBase) / SIZE.
            if (ReadStartAddress == 0 && _textPointerLocation != U.NOT_FOUND)
            {
                uint textBase = rom.p32(_textPointerLocation);
                if (U.isSafetyOffset(textBase, rom)) ReadStartAddress = textBase;
            }
            SelectedId = ReadStartAddress > 0 && addr >= ReadStartAddress
                ? (addr - ReadStartAddress) / SIZE
                : 0;

            // Text id at the current row.
            TextDetail = rom.u16(addr);

            // Animation pointer at animeBase + 4*id (separate table).
            // WF parity contract: p32 strips the high bit, so AnimationPointer
            // holds a ROM OFFSET (not a raw pointer). Write_p32 re-adds the
            // high bit when persisting.
            uint animPtr = 0;
            if (_animePointerLocation != U.NOT_FOUND)
            {
                uint animeBase = rom.p32(_animePointerLocation);
                if (U.isSafetyOffset(animeBase, rom))
                {
                    uint animeSlot = animeBase + 4 * SelectedId;
                    if (U.isSafetyOffset(animeSlot + 3, rom))
                    {
                        animPtr = rom.p32(animeSlot);
                    }
                }
            }
            AnimationPointer = animPtr;
            IsAnimationValid = animPtr != 0 && U.isSafetyOffset(animPtr, rom);

            SelectedFrame = 0;
            // Runtime UI string runs through R._ so the ja/zh translation
            // entries pick it up - ViewTranslationHelper only translates
            // AXAML literals, not VM-set TextBox values (Copilot bot review
            // on PR #516 round 4).
            BinInfoText = IsAnimationValid
                ? string.Format(R._("Animation @ 0x{0:X08} ({1})"),
                    animPtr,
                    R._("preview unavailable - tracked by #500"))
                : "";

            IsLoaded = true;
            CanWrite = true;
        }

        /// <summary>
        /// Write the editable fields back to ROM:
        ///   (a) u16 TextDetail @ CurrentAddr (==textBase + 2*id);
        ///   (b) u32 AnimationPointer @ animeBase + 4*id (via write_p32 to
        ///       re-add the 0x08000000 high bit).
        ///
        /// Mirrors the WF `WriteButton_Click` dual-handler behavior: the
        /// InputFormRef auto-handler writes the textId, and the explicit
        /// WriteButton_Click writes the animation pointer.
        ///
        /// Caller (the View) is expected to wrap this call in a single
        /// <c>_undoService.Begin/Commit</c> scope per the single-owner
        /// contract.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;

            // (a) text id.
            if (addr + SIZE <= (uint)rom.Data.Length)
            {
                rom.write_u16(addr, TextDetail);
            }

            // (b) animation pointer.
            if (_animePointerLocation == U.NOT_FOUND) return;
            uint animeBase = rom.p32(_animePointerLocation);
            if (!U.isSafetyOffset(animeBase, rom)) return;
            uint animeSlot = animeBase + 4 * SelectedId;
            if (!U.isSafetyOffset(animeSlot + 3, rom)) return;

            // write_p32 = U.toOffset + OR with 0x08000000. AnimationPointer
            // is stored as an offset (see LoadEntry), so this serializes it
            // back to the GBA-pointer form expected by the WF reader.
            rom.write_p32(animeSlot, AnimationPointer);
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
                ["TextDetail"] = $"0x{TextDetail:X04}",
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
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["TextDetail"] = "u16@0x00",
        };
    }
}
