using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the In-ROM Magic Animation editor (#1176). Wraps the Core
    /// <see cref="RomAnimeCore"/> render + per-frame import/export over the
    /// <c>romanime_</c> table. The list is keyed by the config ID; each selection
    /// resolves the entry and exposes its frame count + the current frame preview.
    /// </summary>
    public class ImageRomAnimeViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentId;
        int _frameCount;
        int _currentFrame;
        bool _isLoaded;
        string _info = "";

        RomAnimeCore.RomAnimeEntry _entry;

        public uint CurrentId { get => _currentId; set => SetField(ref _currentId, value); }
        public int FrameCount { get => _frameCount; set => SetField(ref _frameCount, value); }
        public int CurrentFrame { get => _currentFrame; set => SetField(ref _currentFrame, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string Info { get => _info; set => SetField(ref _info, value); }

        /// <summary>The currently resolved entry (null until a row is selected).</summary>
        public RomAnimeCore.RomAnimeEntry Entry => _entry;
        /// <summary>Frame image width in pixels (for import validation).</summary>
        public int FrameWidthPx => (_entry?.ImageWidthTiles ?? 0) * 8;

        /// <summary>
        /// Build the AddressList from every <c>romanime_</c> config row. The
        /// <see cref="AddrResult"/> address is the config ID (the editor is keyed by
        /// ID, not a ROM address); the index is the list position.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            var result = new List<AddrResult>();
            if (rom?.RomInfo == null) return result;

            List<RomAnimeCore.RomAnimeEntry> entries = RomAnimeCore.LoadList(rom);
            for (int i = 0; i < entries.Count; i++)
            {
                RomAnimeCore.RomAnimeEntry e = entries[i];
                string name = U.ToHexString(e.Id) + " " + e.Name;
                result.Add(new AddrResult(e.Id, name, (uint)i));
            }
            return result;
        }

        /// <summary>
        /// Resolve the selected config ID into an entry, compute its frame count,
        /// reset the current frame to 0, and build the info text. A row whose ID is
        /// not in the config leaves the editor unloaded.
        /// </summary>
        public void LoadEntry(uint id)
        {
            ROM rom = CoreState.ROM;
            _entry = null;
            FrameCount = 0;
            CurrentFrame = 0;
            IsLoaded = false;
            Info = "";
            if (rom?.RomInfo == null) return;

            List<RomAnimeCore.RomAnimeEntry> entries = RomAnimeCore.LoadList(rom);
            foreach (RomAnimeCore.RomAnimeEntry e in entries)
            {
                if (e.Id != id) continue;
                _entry = e;
                break;
            }
            if (_entry == null) return;

            CurrentId = id;
            FrameCount = Math.Max(RomAnimeCore.GetFrameCount(rom, _entry), 1);
            IsLoaded = true;
            Info = BuildInfo();
        }

        string BuildInfo()
        {
            if (_entry == null) return "";
            return string.Join("\n", new[]
            {
                R._("Name: {0}", _entry.Name),
                R._("ImageWidth: {0}", _entry.ImageWidthTiles * 8),
                R._("Frames: {0}", FrameCount),
                R._("TSA count: {0}", _entry.TSAList.Count),
                R._("Image count: {0}", _entry.ImageList.Count),
                R._("Palette count: {0}", _entry.PaletteList.Count),
            });
        }

        /// <summary>Render the current frame (null when nothing is renderable).</summary>
        public IImage TryLoadImage()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _entry == null) return null;
            return RomAnimeCore.TryRenderFrame(rom, _entry, CurrentFrame);
        }

        /// <summary>
        /// Import quantized indexed pixels + a RAW 16-color GBA palette into the
        /// current frame's IMAGE/TSA/PALETTE slots. Returns "" on success or a
        /// user-facing error (ZERO mutation on failure).
        /// </summary>
        public string Import(byte[] indexedPixels, byte[] gbaPalette16, int width, int height)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _entry == null) return R._("No animation entry selected.");
            RomAnimeCore.ImportFrame(rom, _entry, CurrentFrame,
                indexedPixels, gbaPalette16, width, height, out string error);
            return error;
        }

        /// <summary>
        /// Export the WHOLE animation as a <c>wait png</c> script + per-frame PNGs
        /// (multi-frame, #1230). Returns "" on success or a user-facing error.
        /// </summary>
        public string ExportScript(string scriptPath)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _entry == null) return R._("No animation entry selected.");
            return RomAnimeMultiFrameCore.ExportTxt(rom, _entry, scriptPath);
        }

        /// <summary>
        /// Export the WHOLE animation as an animated GIF (multi-frame, #1230).
        /// Returns "" on success or a user-facing error.
        /// </summary>
        public string ExportGif(string gifPath)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _entry == null) return R._("No animation entry selected.");
            return RomAnimeMultiFrameCore.ExportGif(rom, _entry, gifPath);
        }

        /// <summary>
        /// Import a multi-frame <c>wait png</c> script and rebuild the WHOLE animation
        /// as one atomic transaction (#1230). The <paramref name="frameLoader"/> turns a
        /// resolved PNG path into a quantized <c>(indexed, gbaPalette16, w, h)</c> tuple
        /// (or null when missing). Returns "" on success or a user-facing error (ZERO
        /// ROM mutation on failure).
        /// </summary>
        public string ImportScript(string scriptPath,
            Func<string, (byte[] indexedPixels, byte[] gbaPalette16, int width, int height)?> frameLoader)
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _entry == null) return R._("No animation entry selected.");
            RomAnimeMultiFrameCore.ImportTxt(rom, _entry, scriptPath, frameLoader, out string error);
            return error;
        }

        // ---- IDataVerifiable ----

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            if (_entry == null) return new Dictionary<string, string>();
            return new Dictionary<string, string>
            {
                ["id"] = U.ToHexString(_entry.Id),
                ["width"] = (_entry.ImageWidthTiles * 8).ToString(),
                ["frames"] = FrameCount.ToString(),
                ["FramePtr"] = $"0x{_entry.FramePointer:X08}",
                ["TSAPtr"] = $"0x{_entry.TSAPointer:X08}",
                ["ImagePtr"] = $"0x{_entry.ImagePointer:X08}",
                ["PalettePtr"] = $"0x{_entry.PalettePointer:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _entry == null) return new Dictionary<string, string>();
            var d = new Dictionary<string, string> { ["id"] = U.ToHexString(_entry.Id) };
            if (U.isSafetyOffset(_entry.TSAPointer, rom))
                d["TSA@p32"] = $"0x{rom.u32(_entry.TSAPointer):X08}";
            if (U.isSafetyOffset(_entry.ImagePointer, rom))
                d["Image@p32"] = $"0x{rom.u32(_entry.ImagePointer):X08}";
            if (U.isSafetyOffset(_entry.PalettePointer, rom))
                d["Palette@p32"] = $"0x{rom.u32(_entry.PalettePointer):X08}";
            return d;
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["TSAPtr"] = "TSA@p32",
            ["ImagePtr"] = "Image@p32",
            ["PalettePtr"] = "Palette@p32",
        };
    }
}
