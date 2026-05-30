using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ImageBGViewModel : ViewModelBase, IDataVerifiable
    {
        const uint SIZE = 12;
        const string ResourceCacheKeyPrefix = "BG_";

        uint _currentAddr;
        int _currentIndex;
        bool _isLoaded;
        bool _canWrite;
        uint _p0, _p4, _p8;

        // Read-config bar (mirrors the WinForms panel1 — read-only display).
        uint _readStartAddress;
        int _readCount;
        uint _blockSize = SIZE;
        uint _selectAddress;

        // Comment + cross-references (mirror the WinForms panel2 Comment +
        // X_REF list). X_REF is scaffolded with an empty list — the WF
        // path uses `InputFormRef.UpdateRef(..., BG)` which reads from
        // AsmMapFileAsmCache (WinForms-bound); a future PR can port the
        // Core scanner. See #429 plan.
        string _comment = string.Empty;
        List<AddrResult> _xrefEntries = new();

        // Source-file affordance (mirrors WF `OpenSourceButton` +
        // `SelectSourceButton` visibility — reads from
        // `CoreState.ResourceCache` under the key `"BG_{index}"`).
        bool _isSourceFileAvailable;
        string _sourceFilePath = string.Empty;

        // Reserve-BG warning banner (empty string = hidden). Mirrors
        // `ImageBGForm.ShowWarningMessage`.
        string _warningMessage = string.Empty;

        // Cached BG256 patch state — populated by LoadEntry so the View
        // can branch (GraphicsTool params, popup-open decision).
        bool _isBG256Patched;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public int CurrentIndex { get => _currentIndex; set => SetField(ref _currentIndex, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        // P0: Image data pointer (LZ77-compressed tiles)
        public uint P0 { get => _p0; set => SetField(ref _p0, value); }
        // P4: TSA pointer (raw header-packed TSA), or 0/1 flag under BG256
        public uint P4 { get => _p4; set => SetField(ref _p4, value); }
        // P8: Palette pointer (raw palette data)
        public uint P8 { get => _p8; set => SetField(ref _p8, value); }

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

        // Warning banner.
        public string WarningMessage { get => _warningMessage; set => SetField(ref _warningMessage, value); }

        // BG256 patch state.
        public bool IsBG256Patched { get => _isBG256Patched; set => SetField(ref _isBG256Patched, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.bg_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * SIZE);
                if (addr + SIZE > (uint)rom.Data.Length) break;

                uint a0 = rom.u32(addr + 0);
                uint a1 = rom.u32(addr + 4);
                // Mirror WF `ImageBGForm.Init` callback — under BG256
                // patch, P4=0 or P4=1 are valid mode flags; otherwise
                // both pointers must be pointer-or-NULL.
                if (!FEBuilderGBA.ImageBGCore.IsValidEntry(rom, a0, a1)) break;

                string name = U.ToHexString(i) + " Background";
                result.Add(new AddrResult(addr, name, i));
            }

            // Surface read-config bar values for the View.
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

            P0 = rom.u32(addr + 0);
            P4 = rom.u32(addr + 4);
            P8 = rom.u32(addr + 8);

            // Compute the row index relative to the BG table base so
            // Comment + ResourceCache lookups use the same `i` as the WF
            // form.
            uint ptr = rom.RomInfo.bg_pointer;
            uint baseAddr = ptr != 0 ? rom.p32(ptr) : 0;
            int index = (baseAddr != 0 && addr >= baseAddr)
                ? (int)((addr - baseAddr) / SIZE)
                : 0;
            CurrentIndex = index;

            IsBG256Patched = FEBuilderGBA.PatchDetection.HasBG256ColorPatch(rom);

            RefreshComment();
            RefreshSourceFile((uint)index);
            RefreshWarningMessage((uint)index);

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
        /// Refresh the source-file affordance: reads
        /// <c>CoreState.ResourceCache</c> under the key
        /// <c>"BG_{hexIndex}"</c> and sets
        /// <see cref="IsSourceFileAvailable"/> based on whether the file
        /// exists on disk.
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

        /// <summary>
        /// Compute the warning text for the selected BG slot. Mirrors
        /// <c>ImageBGForm.ShowWarningMessage</c> branching:
        /// reserve-black, reserve-random, or 255-color cutscene.
        /// </summary>
        public void RefreshWarningMessage(uint bgId)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) { WarningMessage = string.Empty; return; }

            uint black = rom.RomInfo.bg_reserve_black_bgid;
            uint random = rom.RomInfo.bg_reserve_random_bgid;

            if (black != U.NOT_FOUND && bgId == black)
            {
                WarningMessage = "This BG slot is reserved for the system black background. Changes are not recommended.";
                return;
            }
            if (random != U.NOT_FOUND && bgId == random)
            {
                WarningMessage = "This BG slot is reserved for the support-conversation random background.";
                return;
            }
            if (FEBuilderGBA.ImageBGCore.Is255BG(rom, P0, P4))
            {
                WarningMessage = "This entry is a 255-color cutscene background.";
                return;
            }
            WarningMessage = string.Empty;
        }

        /// <summary>
        /// Write the three pointer fields back to ROM. The caller MUST
        /// have opened an ambient undo scope.
        /// </summary>
        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;

            uint addr = CurrentAddr;
            rom.write_u32(addr + 0, P0);
            rom.write_u32(addr + 4, P4);
            rom.write_u32(addr + 8, P8);
        }

        /// <summary>
        /// Expand the BG pointer table to the requested count. Delegates
        /// to <see cref="FEBuilderGBA.ImageBGCore.ExpandList(ROM, uint, uint)"/>.
        /// The caller (View) MUST have opened an ambient ROM undo scope.
        /// </summary>
        public uint ExpandList(uint newCount)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            if (ReadCount <= 0) return U.NOT_FOUND;
            uint oldCount = (uint)ReadCount;
            return FEBuilderGBA.ImageBGCore.ExpandList(rom, oldCount, newCount);
        }

        /// <summary>
        /// Compatibility overload accepting an explicit UndoData.
        /// </summary>
        public uint ExpandList(uint newCount, Undo.UndoData undo)
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return U.NOT_FOUND;
            if (ReadCount <= 0) return U.NOT_FOUND;
            uint oldCount = (uint)ReadCount;
            return FEBuilderGBA.ImageBGCore.ExpandList(rom, oldCount, newCount, undo);
        }

        /// <summary>
        /// Try to load BG image. ROM layout: P0=image(LZ77), P4=TSA(raw header), P8=palette(raw).
        /// </summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return null;
            try
            {
                // P0=image, P4=TSA, P8=palette (matching WinForms ImageBGForm field order)
                if (!U.isPointer(P0) || !U.isPointer(P8)) return null;
                uint imgAddr = U.toOffset(P0);
                uint palAddr = U.toOffset(P8);
                if (!U.isSafetyOffset(imgAddr) || !U.isSafetyOffset(palAddr)) return null;

                // BG255/BG224 cutscene background: under the BG256Color
                // patch, P4 is a RAW mode flag (0 = 255-color, 1 = 224-color),
                // NOT a TSA pointer. Decode via the 8bpp path that mirrors WF
                // ImageBGForm.DrawBG (ByteToImage256Tile / ByteToImage224BGTile),
                // NOT Decode4bppTiles. This must run before the P4-pointer
                // branch below (#799). Previously these entries fell through to
                // the 4bpp fallback and rendered garbage.
                if (IsBG256Patched && P4 <= 1 && CoreState.ImageService != null)
                {
                    return ImageBG256ColorCore.Decode255ColorBG(
                        rom, P0, P8, is224: P4 == 1, CoreState.ImageService);
                }

                byte[] tileData = LZ77.decompress(rom.Data, imgAddr);
                if (tileData == null || tileData.Length == 0) return null;

                // Palette is raw ROM data (not LZ77 compressed) — read up to 256 colors
                byte[] palette = ImageUtilCore.GetPalette(palAddr, 256);
                if (palette == null || palette.Length == 0) return null;

                if (U.isPointer(P4))
                {
                    uint tsaAddr = U.toOffset(P4);
                    if (U.isSafetyOffset(tsaAddr))
                    {
                        // TSA is raw ROM data with header format
                        int tsaLen = Math.Min(32 * 20 * 2 + 4, (int)((uint)rom.Data.Length - tsaAddr));
                        if (tsaLen > 0)
                        {
                            byte[] tsaData = new byte[tsaLen];
                            Array.Copy(rom.Data, tsaAddr, tsaData, 0, tsaLen);
                            return ImageUtilCore.DecodeHeaderTSA(tileData, tsaData, palette, 32, 20);
                        }
                    }
                }

                if (CoreState.ImageService == null) return null;
                int totalTiles = tileData.Length / 32;
                if (totalTiles <= 0) return null;
                int tilesX = 30;
                int tilesY = (totalTiles + tilesX - 1) / tilesX;
                return CoreState.ImageService.Decode4bppTiles(tileData, 0, tilesX * 8, tilesY * 8, palette);
            }
            catch { return null; }
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
                ["P4"] = $"0x{P4:X08}",
                ["P8"] = $"0x{P8:X08}",
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
            ["P0"] = "u32@0",
            ["P4"] = "u32@4",
            ["P8"] = "u32@8",
        };
    }
}
