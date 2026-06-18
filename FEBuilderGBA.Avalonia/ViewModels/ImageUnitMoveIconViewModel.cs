using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// View model for the Unit Move Icon editor (#1177) — the per-class
    /// moving/walk icon sprite sheet, sibling of the Wait Icon editor (#991).
    /// 8-byte table entries: P0 (image pointer) @ +0, P4 (AP pointer) @ +4.
    /// </summary>
    public class ImageUnitMoveIconViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 8;

        uint _currentAddr;
        int _currentIndex;
        bool _isLoaded;
        uint _p0, _p4;
        int _paletteType;
        int _step;
        string _comment = string.Empty;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public int CurrentIndex { get => _currentIndex; set => SetField(ref _currentIndex, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }

        /// <summary>0=self,1=npc,2=enemy,3=gray,4=four (the 5 selectable types).</summary>
        public int PaletteType { get => _paletteType; set => SetField(ref _paletteType, value); }
        /// <summary>Frame step for the single-frame preview (0-based walk step).</summary>
        public int Step { get => _step; set => SetField(ref _step, value); }
        public string Comment { get => _comment; set => SetField(ref _comment, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.unit_move_icon_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;

                uint imgPtr = rom.u32(addr + 0);
                if (!U.isPointer(imgPtr)) break;

                // #1177: append the owning class name (WF
                // GetClassNameWhereNo(i) = GetClassName(i+1) — move-icon row i
                // maps directly to class id i+1). Lockstep with
                // ListParityHelper.BuildImageUnitMoveIconList.
                string className = NameResolver.GetClassName(i + 1) ?? string.Empty;
                string name = U.ToHexString(i) + U.SA(className) + " MoveIcon";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            P0 = rom.u32(addr + 0);
            P4 = rom.u32(addr + 4);

            // Row index relative to the move-icon table base (= moveIconIndex).
            uint ptr = rom.RomInfo.unit_move_icon_pointer;
            uint baseAddr = ptr != 0 ? rom.p32(ptr) : 0;
            CurrentIndex = (baseAddr != 0 && addr >= baseAddr)
                ? (int)((addr - baseAddr) / SIZE)
                : 0;

            LoadComment();
            RefreshCurrentApName();
            IsLoaded = true;
        }

        /// <summary>Re-read the entry fields + comment from ROM (post-import refresh).</summary>
        public void ReloadEntry()
        {
            if (CurrentAddr != 0) LoadEntry(CurrentAddr);
        }

        /// <summary>The move-icon table base offset (for the AP shared-ref scan).</summary>
        public uint TableBase
        {
            get
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return 0;
                uint ptr = rom.RomInfo.unit_move_icon_pointer;
                return ptr != 0 ? rom.p32(ptr) : 0;
            }
        }

        /// <summary>The move-icon row count (for the AP shared-ref scan range).</summary>
        public int DataCount => LoadList().Count;

        // ----------------------------------------------------------------
        // Comment (mirror ImageUnitWaitIconViewModel)
        // ----------------------------------------------------------------
        public void LoadComment()
        {
            var cache = CoreState.CommentCache;
            Comment = (cache != null) ? (cache.S_At(CurrentAddr) ?? string.Empty) : string.Empty;
        }

        public void SaveComment(string text)
        {
            Comment = text ?? string.Empty;
            var cache = CoreState.CommentCache;
            if (cache == null || CurrentAddr == 0) return;
            cache.Update(CurrentAddr, Comment);
        }

        // ----------------------------------------------------------------
        // Write fields back (P0/P4 as RAW GBA pointers)
        // ----------------------------------------------------------------
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            // P0/P4 are RAW GBA pointer fields — write the values verbatim
            // (write_u32, NOT write_p32 which would re-apply the 0x08000000
            // offset).
            rom.write_u32(addr + 0, P0);
            rom.write_u32(addr + 4, P4);
            SaveComment(Comment);
        }

        // ----------------------------------------------------------------
        // Previews
        // ----------------------------------------------------------------
        /// <summary>Render the full decoded sprite sheet (no crop) for X_PIC.</summary>
        public IImage RenderFullSheet()
        {
            ROM rom = CoreState.ROM;
            var svc = CoreState.ImageService;
            if (rom == null || svc == null) return null;
            return UnitMoveIconRenderCore.RenderFullSheet(rom, (uint)CurrentIndex, svc, PaletteType);
        }

        /// <summary>Render the single <see cref="Step"/> walk frame for X_ONE_PIC.</summary>
        public IImage RenderFrame()
        {
            ROM rom = CoreState.ROM;
            var svc = CoreState.ImageService;
            if (rom == null || svc == null) return null;
            return UnitMoveIconRenderCore.RenderFrame(rom, (uint)CurrentIndex, Step, svc, PaletteType);
        }

        // ----------------------------------------------------------------
        // Export
        // ----------------------------------------------------------------
        /// <summary>Export the full decoded sheet as an indexed PNG.</summary>
        public bool ExportPng(string path)
        {
            using IImage img = RenderFullSheet();
            if (img == null) return false;
            img.Save(path);
            return true;
        }

        /// <summary>
        /// Export the walk-animation frames (step 0..0xE) as an animated GIF.
        /// Mirrors WF <c>ImageUnitMoveIconFrom.SaveAnimeGif</c> (0xF frames,
        /// wait=10 game frames per frame). Stops early when a step crops short.
        /// </summary>
        public bool ExportGif(string path)
        {
            ROM rom = CoreState.ROM;
            var svc = CoreState.ImageService;
            if (rom == null || svc == null) return false;

            var gifFrames = new List<GifEncoderCore.GifFrame>();
            var rendered = new List<IImage>();
            try
            {
                for (int step = 0; step < 0xF; step++)
                {
                    IImage img = UnitMoveIconRenderCore.RenderFrame(rom, (uint)CurrentIndex, step, svc, PaletteType);
                    if (img == null) break; // short sheet — stop at the last valid frame
                    rendered.Add(img);

                    byte[] rgba;
                    if (img.IsIndexed)
                    {
                        byte[] indexed = img.GetPixelData();
                        byte[] palette = img.GetPaletteRGBA();
                        rgba = GifEncoderCore.IndexedToRgba(indexed, palette, img.Width, img.Height);
                    }
                    else
                    {
                        rgba = img.GetPixelData();
                    }

                    gifFrames.Add(new GifEncoderCore.GifFrame
                    {
                        Width = img.Width,
                        Height = img.Height,
                        RgbaPixels = rgba,
                        DelayCs = U.GameFrameSecToGifFrameSec(10),
                    });
                }

                if (gifFrames.Count == 0) return false;
                GifEncoderCore.Encode(gifFrames, path);
                return true;
            }
            finally
            {
                foreach (var img in rendered) img.Dispose();
            }
        }

        /// <summary>
        /// Read the RAW AP bytes for the current entry (padded length, WF-parity),
        /// or null when there is no resolvable / parseable AP region.
        /// </summary>
        public byte[] ReadApBytes()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return null;
            return UnitMoveIconImportCore.ReadApBytes(rom, P4);
        }

        /// <summary>True when the +4 AP pointer resolves to a parseable region (gates ExportAP).</summary>
        public bool HasAp()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || P4 == 0) return false;
            uint apOff = U.toOffset(P4);
            if (!U.isSafetyOffset(apOff, rom)) return false;
            return ImageUtilAPCore.CalcAPLength(rom.Data, apOff) > 0;
        }

        /// <summary>
        /// True when the current entry's AP region is shared by &gt;=2 table
        /// entries — advisory so the View can warn before re-pointing (WF
        /// ImportAPButton_Click protects shared regions; here ImportAP always
        /// appends fresh, so the warning is informational only).
        /// </summary>
        public bool IsApShared()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || P4 == 0) return false;
            return UnitMoveIconImportCore.IsApRegionShared(rom, P4, TableBase, DataCount);
        }

        /// <summary>
        /// Resolve the ROM ADDRESS of the Wait Icon list entry the "Jump to Wait
        /// Icon" button should navigate to, or null when there is no owning class
        /// / wait icon. The move-icon row i maps to class id i+1 (WF
        /// GetClassNameWhereNo); that class's wait-icon field (u8 @ class+6) is a
        /// 0-based table id (the wait-icon table is 0-based by row, matching
        /// ImageUnitWaitIconViewModel.LoadList), so the target entry is at
        /// `baseAddr + id * 8`. Guards every pointer/address — never throws.
        /// Reciprocal of ImageUnitWaitIconViewModel.ResolveMoveIconEntryAddress.
        /// </summary>
        public uint? ResolveWaitIconEntryAddress()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return null;

            uint classId = (uint)CurrentIndex + 1;
            uint waitIcon = ClassFormCore.GetClassWaitIcon(rom, classId);
            if (waitIcon == U.NOT_FOUND) return null;

            uint ptr = rom.RomInfo.unit_wait_icon_pointer;
            if (ptr == 0) return null;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return null;

            uint entryAddr = baseAddr + waitIcon * 8;
            if (entryAddr + 8 > (uint)rom.Data.Length) return null;
            return entryAddr;
        }

        // ----------------------------------------------------------------
        // AP MD5-dictionary selector (#1226) — port of WF
        // ImageUnitMoveIconFrom's ap_list_ / ap_vanilla_list_ combo.
        //
        // ap_list_ALL.txt (name\tmd5) → {md5 → name} via LoadTSVResourcePair.
        // ap_vanilla_list_FE6/7/8.txt (offsetHex\tmd5\t{J}) → {offset → md5}
        // via LoadTSVResource1. The combo lets the user pick a known AP pattern
        // by name; selecting it RE-POINTS the current entry's +4 (P4) pointer to
        // an EXISTING ROM AP region matching that pattern's MD5 (an entry's
        // current AP, or a still-intact vanilla AP). It NEVER writes AP bytes —
        // that's the separate ImportAP path.
        // ----------------------------------------------------------------

        // {md5 → display name} catalog (ap_list_).
        Dictionary<string, string> _apComboDic;
        // Display names in file order (combo item order, mirrors WF Items.Add loop).
        List<string> _apCatalogNames;
        // {vanilla offset → md5} (ap_vanilla_list_).
        Dictionary<uint, string> _apVanillaDic;
        string _currentApName = string.Empty;

        void EnsureCatalogLoaded()
        {
            if (_apComboDic != null) return;

            _apComboDic = U.LoadTSVResourcePair(U.ConfigDataFilename("ap_list_"));

            _apCatalogNames = new List<string>();
            foreach (var pair in _apComboDic)
                _apCatalogNames.Add(pair.Value);

            // Vanilla list: only keep safely-offset addresses (WF MakeAPBanillaDic).
            _apVanillaDic = new Dictionary<uint, string>();
            ROM rom = CoreState.ROM;
            Dictionary<uint, string> raw = U.LoadTSVResource1(U.ConfigDataFilename("ap_vanilla_list_"));
            foreach (var pair in raw)
            {
                uint apOff = pair.Key;
                if (rom != null && !U.isSafetyOffset(apOff, rom)) continue;
                _apVanillaDic[apOff] = pair.Value;
            }
        }

        /// <summary>
        /// The AP pattern display names (combo items), in resource file order.
        /// Loaded lazily from <c>config/data/ap_list_*.txt</c>.
        /// </summary>
        public List<string> ApCatalogNames
        {
            get { EnsureCatalogLoaded(); return _apCatalogNames; }
        }

        /// <summary>
        /// The catalog name matched to the CURRENT entry's AP (by its P4 MD5), or
        /// "" when the AP is unknown / unparseable. Mirrors WF
        /// <c>SelectAPComboFromAPAddresss</c> selecting the combo entry.
        /// </summary>
        public string CurrentApName { get => _currentApName; private set => SetField(ref _currentApName, value); }

        /// <summary>
        /// Recompute <see cref="CurrentApName"/> from the current P4 AP's MD5.
        /// READ-ONLY — never mutates the ROM. Call after LoadEntry / a P4 change.
        /// </summary>
        public void RefreshCurrentApName()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || P4 == 0) { CurrentApName = string.Empty; return; }

            EnsureCatalogLoaded();
            uint apOff = U.toOffset(P4);
            string md5 = ImageUtilAPCore.CalcAPMD5(rom.Data, apOff);
            CurrentApName = _apComboDic.TryGetValue(md5, out string name) ? name : string.Empty;
        }

        /// <summary>
        /// Resolve a chosen catalog AP <paramref name="name"/> to an EXISTING ROM
        /// AP offset and re-point the current entry's in-memory P4 to it (the View
        /// then commits the single P4 write under its ambient undo scope, byte
        /// minimal). READ-ONLY resolution: scans the move-icon table's current AP
        /// hashes and the still-intact vanilla APs. Returns "" on success; a
        /// localized error string (and NO P4 change) when the chosen AP is not
        /// present in this ROM — mirroring WF's "AP not in this ROM" stop error.
        /// </summary>
        public string ApplyApByName(string name)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM is not loaded.");
            if (string.IsNullOrEmpty(name)) return string.Empty;

            EnsureCatalogLoaded();

            // name → md5 (WF SelectAPAddresssFromAPComboLow first loop).
            string targetMd5 = string.Empty;
            foreach (var pair in _apComboDic)
            {
                if (pair.Value == name) { targetMd5 = pair.Key; break; }
            }
            if (string.IsNullOrEmpty(targetMd5)) return string.Empty; // unknown name → no-op

            // Already pointing at a region with this MD5 → nothing to do (avoids a
            // redundant write / undo entry when the combo re-selects the current AP).
            if (P4 != 0 && ImageUtilAPCore.CalcAPMD5(rom.Data, U.toOffset(P4)) == targetMd5)
                return string.Empty;

            Dictionary<uint, string> entryApMd5 =
                ImageUtilAPCore.MakeAPAddressDic(rom.Data, TableBase, DataCount);

            uint apOff = ImageUtilAPCore.ResolveApOffsetByMd5(rom.Data, targetMd5, entryApMd5, _apVanillaDic);
            if (apOff == U.NOT_FOUND)
            {
                return R._("This AP pattern is not present in this ROM. Import the AP data, or enter the AP pointer manually.");
            }

            P4 = U.toPointer(apOff);
            RefreshCurrentApName();
            return string.Empty;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
                ["P4"] = $"0x{P4:X08}",
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
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["P0"] = "u32@0x00",
            ["P4"] = "u32@0x04",
        };
    }
}
