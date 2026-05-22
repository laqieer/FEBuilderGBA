using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ImageBattleBGViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 12;
        const string ResourceCacheKeyPrefix = "BattleBG_";

        uint _currentAddr;
        int _currentIndex;
        bool _isLoaded;
        bool _canWrite;
        uint _imagePointer, _tsaPointer, _palettePointer;

        // Read-config bar (mirrors the WinForms panel1 — read-only display).
        uint _readStartAddress;
        int _readCount;
        uint _blockSize = SIZE;
        uint _selectAddress;

        // Comment + cross-references (mirror the WinForms panel2 Comment +
        // X_REF list).
        string _comment = string.Empty;
        List<AddrResult> _xrefEntries = new();

        // Source-file affordance (mirrors the WinForms `OpenSourceButton` +
        // `SelectSourceButton` visibility logic — reads from
        // `CoreState.ResourceCache` under the key `"BattleBG_{index}"`).
        bool _isSourceFileAvailable;
        string _sourceFilePath = string.Empty;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public int CurrentIndex { get => _currentIndex; set => SetField(ref _currentIndex, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // D0: Image data pointer
        public uint ImagePointer { get => _imagePointer; set => SetField(ref _imagePointer, value); }
        // D4: TSA data pointer
        public uint TSAPointer { get => _tsaPointer; set => SetField(ref _tsaPointer, value); }
        // D8: Palette data pointer
        public uint PalettePointer { get => _palettePointer; set => SetField(ref _palettePointer, value); }

        // Read-config bar.
        public uint ReadStartAddress { get => _readStartAddress; set => SetField(ref _readStartAddress, value); }
        public int ReadCount { get => _readCount; set => SetField(ref _readCount, value); }
        public uint BlockSize { get => _blockSize; set => SetField(ref _blockSize, value); }
        public uint SelectAddress { get => _selectAddress; set => SetField(ref _selectAddress, value); }

        // Comment + xrefs.
        public string Comment { get => _comment; set => SetField(ref _comment, value); }
        public List<AddrResult> XRefEntries { get => _xrefEntries; set => SetField(ref _xrefEntries, value); }

        // Source-file affordance.
        public bool IsSourceFileAvailable { get => _isSourceFileAvailable; set => SetField(ref _isSourceFileAvailable, value); }
        public string SourceFilePath { get => _sourceFilePath; set => SetField(ref _sourceFilePath, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.battle_bg_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;

                uint img = rom.u32(addr + 0);
                uint tsa = rom.u32(addr + 4);
                if (!U.isPointer(img) || !U.isPointer(tsa)) break;

                string name = U.ToHexString(i) + " Battle BG";
                result.Add(new AddrResult(addr, name, i));
            }

            // Update read-config bar values so the View can reflect the
            // currently-loaded range (mirrors the WinForms ReadStartAddress
            // + ReadCount NumericUpDowns).
            ReadStartAddress = baseAddr;
            ReadCount = result.Count;
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + SIZE > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            SelectAddress = addr;

            ImagePointer = rom.u32(addr + 0);
            TSAPointer = rom.u32(addr + 4);
            PalettePointer = rom.u32(addr + 8);

            // Compute the row index relative to the BG table base so the
            // Comment + X_REF + ResourceCache lookups use the same `i`
            // the WinForms form does.
            uint ptr = rom.RomInfo.battle_bg_pointer;
            uint baseAddr = ptr != 0 ? rom.p32(ptr) : 0;
            int index = (baseAddr != 0 && addr >= baseAddr)
                ? (int)((addr - baseAddr) / SIZE)
                : 0;
            CurrentIndex = index;

            // Refresh the comment / xrefs / source-file affordance.
            RefreshComment();
            RefreshXrefs((uint)index);
            RefreshSourceFile((uint)index);

            IsLoaded = true;
            CanWrite = true;
        }

        /// <summary>
        /// Refresh the Comment string from <c>CoreState.CommentCache</c>.
        /// Mirrors the WinForms `InputFormRef.GetCommentSA(addr)` path.
        /// </summary>
        public void RefreshComment()
        {
            var cache = CoreState.CommentCache;
            if (cache == null)
            {
                Comment = string.Empty;
                return;
            }
            Comment = cache.S_At(CurrentAddr) ?? string.Empty;
        }

        /// <summary>
        /// Save the current Comment to <c>CoreState.CommentCache</c>.
        /// Mirrors the WinForms `InputFormRef.OnComment_TextChanged` path.
        /// </summary>
        public void SaveComment(string text)
        {
            Comment = text ?? string.Empty;
            var cache = CoreState.CommentCache;
            if (cache == null) return;
            if (CurrentAddr == 0) return;
            cache.Update(CurrentAddr, Comment);
        }

        /// <summary>
        /// Refresh the X_REF cross-reference list by walking the BG-side
        /// terrain lookup tables for entries that reference the given
        /// row index. Delegates to <see cref="ImageBattleBGCore.MakeListByUseTerrain"/>.
        /// </summary>
        public void RefreshXrefs(uint terrainId)
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
            {
                XRefEntries = new List<AddrResult>();
                return;
            }
            XRefEntries = FEBuilderGBA.ImageBattleBGCore.MakeListByUseTerrain(rom, terrainId);
        }

        /// <summary>
        /// Refresh the source-file affordance: reads
        /// <c>CoreState.ResourceCache</c> (typed as <see cref="EtcCacheResource"/>)
        /// under the key <c>"BattleBG_{hexIndex}"</c> and sets
        /// <see cref="IsSourceFileAvailable"/> based on whether the file
        /// exists on disk. Mirrors the WinForms
        /// `AddressList_SelectedIndexChanged` source-file path.
        /// </summary>
        public void RefreshSourceFile(uint index)
        {
            var cache = CoreState.ResourceCache as FEBuilderGBA.EtcCacheResource;
            if (cache == null)
            {
                IsSourceFileAvailable = false;
                SourceFilePath = string.Empty;
                return;
            }
            string key = ResourceCacheKeyPrefix + U.ToHexString(index);
            string path = cache.At(key, string.Empty);
            SourceFilePath = path ?? string.Empty;
            IsSourceFileAvailable = !string.IsNullOrEmpty(SourceFilePath)
                && System.IO.File.Exists(SourceFilePath);
        }

        /// <summary>
        /// Record a new source-file path after a successful image import.
        /// Mirrors the WinForms `ImportButton_Click` update path.
        /// </summary>
        public void RecordSourceFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var cache = CoreState.ResourceCache as FEBuilderGBA.EtcCacheResource;
            if (cache == null) return;
            string key = ResourceCacheKeyPrefix + U.ToHexString((uint)CurrentIndex);
            cache.Update(key, path);
            SourceFilePath = path;
            IsSourceFileAvailable = System.IO.File.Exists(path);
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, ImagePointer);
            rom.write_u32(addr + 4, TSAPointer);
            rom.write_u32(addr + 8, PalettePointer);
        }

        /// <summary>
        /// Expand the battle-BG pointer table to the requested count.
        /// Delegates to <see cref="ImageBattleBGCore.ExpandList"/>; the
        /// caller (the View) owns the undo scope.
        /// </summary>
        /// <returns>New base ROM offset on success, or <see cref="U.NOT_FOUND"/>
        /// on failure.</returns>
        public uint ExpandList(uint newCount, Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            // Determine oldCount from the currently-loaded list — caller
            // must have called LoadList() at least once so ReadCount is
            // populated.
            uint oldCount = (uint)Math.Max(1, ReadCount);
            return FEBuilderGBA.ImageBattleBGCore.ExpandList(rom, oldCount, newCount, undo);
        }

        /// <summary>
        /// Render the battle BG image. All 3 components (image, TSA, palette) are LZ77-compressed.
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                if (!U.isPointer(ImagePointer) || !U.isPointer(TSAPointer) || !U.isPointer(PalettePointer))
                    return null;

                uint imgAddr = U.toOffset(ImagePointer);
                uint tsaAddr = U.toOffset(TSAPointer);
                uint palAddr = U.toOffset(PalettePointer);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(tsaAddr) || !U.isSafetyOffset(palAddr))
                    return null;

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;
                byte[] palette = LZ77.decompress(rom.Data, palAddr);
                if (palette == null || palette.Length == 0) return null;
                byte[] tsaData = LZ77.decompress(rom.Data, tsaAddr);
                if (tsaData == null || tsaData.Length == 0) return null;

                return ImageUtilCore.DecodeTSA(tileData, tsaData, palette, 30, 20, true);
            }
            catch { return null; }
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["ImagePointer"] = $"0x{ImagePointer:X08}",
                ["TSAPointer"] = $"0x{TSAPointer:X08}",
                ["PalettePointer"] = $"0x{PalettePointer:X08}",
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
                ["u32@0"] = $"0x{rom.u32(a + 0):X08}",
                ["u32@4"] = $"0x{rom.u32(a + 4):X08}",
                ["u32@8"] = $"0x{rom.u32(a + 8):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["ImagePointer"] = "u32@0",
            ["TSAPointer"] = "u32@4",
            ["PalettePointer"] = "u32@8",
        };
    }
}
