using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the OAM Sprite Viewer.
    /// Renders individual animation frames from battle animation data using OAM sprite layout.
    /// Lists the 32-byte animation data records directly so users can browse all animations
    /// and inspect their OAM-assembled sprite frames.
    /// </summary>
    public class OAMSpriteViewerViewModel : ViewModelBase
    {
        const uint ANIME_RECORD_SIZE = 32;

        uint _currentAddr;
        bool _isLoaded;
        string _statusText = "Select an animation to view OAM sprites.";

        // Section / Frame navigation
        int _currentSection;
        int _currentFrame;
        int _frameCount;
        string _frameInfoText = "";
        bool _hasFrameData;
        IImage _frameImage;

        // Cached data for rendering
        byte[] _cachedFrameData;
        byte[] _cachedOamData;
        byte[] _cachedPaletteData;
        uint _cachedSectionOffset;
        List<BattleAnimeRendererCore.FrameInfo> _cachedFrames;

        // Tile sheet
        IImage _tileSheetImage;
        string _tileSheetInfo = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }

        public int CurrentSection { get => _currentSection; set => SetField(ref _currentSection, value); }
        public int CurrentFrame { get => _currentFrame; set => SetField(ref _currentFrame, value); }
        public int FrameCount { get => _frameCount; set => SetField(ref _frameCount, value); }
        public string FrameInfoText { get => _frameInfoText; set => SetField(ref _frameInfoText, value); }
        public bool HasFrameData { get => _hasFrameData; set => SetField(ref _hasFrameData, value); }
        public IImage FrameImage { get => _frameImage; set => SetField(ref _frameImage, value); }
        public IImage TileSheetImage { get => _tileSheetImage; set => SetField(ref _tileSheetImage, value); }
        public string TileSheetInfo { get => _tileSheetInfo; set => SetField(ref _tileSheetInfo, value); }

        /// <summary>
        /// Load the animation data table (32-byte records) for the address list.
        /// Each record contains the animation name (12 bytes), section/frame/OAM/palette pointers.
        /// </summary>
        public List<AddrResult> LoadAnimationList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint pointer = rom.RomInfo.image_battle_animelist_pointer;
            if (pointer == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(pointer);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (int i = 0; i < 512; i++)
            {
                uint addr = baseAddr + (uint)(i * ANIME_RECORD_SIZE);
                if (addr + ANIME_RECORD_SIZE > (uint)rom.Data.Length) break;

                // Validate the record has valid pointers
                if (!U.isPointer(rom.u32(addr + 12))
                    || !U.isPointer(rom.u32(addr + 20))
                    || !U.isPointer(rom.u32(addr + 24)))
                    break;

                string name = rom.getString(addr, 12);
                result.Add(new AddrResult(addr, $"0x{i + 1:X2} {name}", (uint)(i + 1)));
            }
            return result;
        }

        /// <summary>
        /// Load a 32-byte animation data record and prepare for OAM frame rendering.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            CurrentAddr = addr;
            IsLoaded = false;
            HasFrameData = false;
            FrameCount = 0;
            CurrentFrame = 0;
            FrameImage = null;
            TileSheetImage = null;
            TileSheetInfo = "";
            _cachedFrameData = null;
            _cachedOamData = null;
            _cachedPaletteData = null;
            _cachedFrames = null;

            ROM rom = CoreState.ROM;
            if (rom == null) { StatusText = "No ROM loaded."; return; }
            if (addr + ANIME_RECORD_SIZE > (uint)rom.Data.Length) { StatusText = "Address out of range."; return; }

            string animeName = rom.getString(addr, 12);

            // Read pointers from the 32-byte animation data record
            uint sectionRaw = rom.u32(addr + 12);
            uint frameRaw = rom.u32(addr + 16);
            uint oamRtLRaw = rom.u32(addr + 20);
            uint paletteRaw = rom.u32(addr + 28);

            // Section data
            if (!U.isPointer(sectionRaw)) { StatusText = $"{animeName}: Invalid section pointer."; return; }
            _cachedSectionOffset = U.toOffset(sectionRaw);
            if (!U.isSafetyOffset(_cachedSectionOffset, rom)) { StatusText = $"{animeName}: Section offset out of range."; return; }

            // Decompress frame data
            _cachedFrameData = BattleAnimeRendererCore.DecompressFrameData(rom, frameRaw);
            if (_cachedFrameData == null || _cachedFrameData.Length == 0)
            { StatusText = $"{animeName}: Failed to decompress frame data."; return; }

            // Decompress OAM data
            if (U.isPointer(oamRtLRaw))
            {
                uint oamOff = U.toOffset(oamRtLRaw);
                if (U.isSafetyOffset(oamOff, rom))
                    _cachedOamData = LZ77.decompress(rom.Data, oamOff);
            }

            // Decompress palette data
            if (U.isPointer(paletteRaw))
            {
                uint palOff = U.toOffset(paletteRaw);
                if (U.isSafetyOffset(palOff, rom))
                    _cachedPaletteData = LZ77.decompress(rom.Data, palOff);
            }

            if (_cachedOamData == null) { StatusText = $"{animeName}: Failed to decompress OAM data."; return; }
            if (_cachedPaletteData == null) { StatusText = $"{animeName}: Failed to decompress palette data."; return; }

            // Render tile sheet for reference
            try
            {
                TileSheetImage = BattleAnimeRendererCore.RenderAnimationTileSheet(addr, 32);
                if (TileSheetImage != null)
                    TileSheetInfo = $"Tile sheet: {TileSheetImage.Width}x{TileSheetImage.Height}px";
            }
            catch { TileSheetImage = null; }

            IsLoaded = true;
            HasFrameData = true;
            StatusText = $"{animeName} loaded. Frame data: {_cachedFrameData.Length} bytes, OAM: {_cachedOamData.Length} bytes.";

            LoadSectionFrames(0);
        }

        /// <summary>
        /// Load frames for a specific section index.
        /// </summary>
        public void LoadSectionFrames(int sectionIndex)
        {
            if (!HasFrameData || _cachedFrameData == null) return;

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentSection = sectionIndex;
            CurrentFrame = 0;

            BattleAnimeRendererCore.GetSectionRange(sectionIndex, _cachedSectionOffset,
                (uint)_cachedFrameData.Length, rom, out uint start, out uint end);

            _cachedFrames = BattleAnimeRendererCore.ParseFramesInRange(_cachedFrameData, start, end);
            FrameCount = _cachedFrames.Count;

            if (FrameCount > 0)
            {
                RenderCurrentFrame();
            }
            else
            {
                FrameImage = null;
                string sectionName = sectionIndex < BattleAnimeRendererCore.SectionNames.Length
                    ? BattleAnimeRendererCore.SectionNames[sectionIndex]
                    : $"Section {sectionIndex}";
                FrameInfoText = $"{sectionName}: no frames found";
            }
        }

        /// <summary>
        /// Navigate to a specific frame index.
        /// </summary>
        public void GoToFrame(int frameIndex)
        {
            if (_cachedFrames == null || _cachedFrames.Count == 0) return;
            if (frameIndex < 0) frameIndex = 0;
            if (frameIndex >= _cachedFrames.Count) frameIndex = _cachedFrames.Count - 1;

            CurrentFrame = frameIndex;
            RenderCurrentFrame();
        }

        void RenderCurrentFrame()
        {
            if (_cachedFrames == null || CurrentFrame < 0 || CurrentFrame >= _cachedFrames.Count)
            {
                FrameImage = null;
                FrameInfoText = "";
                return;
            }

            var fi = _cachedFrames[CurrentFrame];
            try
            {
                FrameImage = BattleAnimeRendererCore.RenderSingleFrame(fi, _cachedOamData, _cachedPaletteData);
            }
            catch
            {
                FrameImage = null;
            }

            // Update tile sheet to match current frame — clear first to avoid stale data
            var oldSheet = TileSheetImage;
            TileSheetImage = null;
            TileSheetInfo = "";
            try
            {
                var newSheet = BattleAnimeRendererCore.RenderFrameTileSheet(
                    fi.GraphicsPointer, _cachedPaletteData, 32);
                if (newSheet != null)
                {
                    TileSheetImage = newSheet;
                    TileSheetInfo = $"Tile sheet: {newSheet.Width}x{newSheet.Height}px (frame {CurrentFrame + 1})";
                }
            }
            catch (Exception ex) { Log.Error("RenderCurrentFrame tile sheet failed: " + ex.Message); }
            if (oldSheet is IDisposable d) d.Dispose();

            string sectionName = CurrentSection < BattleAnimeRendererCore.SectionNames.Length
                ? BattleAnimeRendererCore.SectionNames[CurrentSection]
                : $"Section {CurrentSection}";
            FrameInfoText = $"Frame {CurrentFrame + 1}/{FrameCount} | {sectionName} | " +
                            $"Gfx: 0x{fi.GraphicsPointer:X08}, OAM offset: 0x{fi.OamOffset:X}";
        }
    }
}
