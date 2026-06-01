// SPDX-License-Identifier: GPL-3.0-or-later
// ImageBattleScreen VM rebuild for #393 gap-sweep parity. Mirrors the WF
// `ImageBattleScreenForm` battle-screen layout editor.
//
// Per Plan v2 + Copilot CLI plan-review round 1:
//   - Read/Write paths use FEBuilderGBA.Core/ImageBattleScreenCore which
//     delegates palette work to PaletteCore (Finding #3) and uses the
//     ambient ROM.BeginUndoScope only (Finding #2 -- no Undo.UndoData
//     parameter anywhere).
//   - Image2/4 = Name, Image3/5 = Item per WF designer (Finding #1).
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class ImageBattleScreenViewModel : ViewModelBase
    {
        /// <summary>Total cells in the TSA map (32 * 20 = 640).</summary>
        public const int MAP_SIZE = ImageBattleScreenCore.MAP_SIZE;

        ushort[] _map = new ushort[MAP_SIZE];
        uint _tsa1Addr;
        uint _paletteAddr;
        int _paletteIndex;
        int _zoomIndex;
        uint _image1Pointer;
        uint _image2Pointer;
        uint _image3Pointer;
        uint _image4Pointer;
        uint _image5Pointer;
        bool _isLoaded;
        bool _canExportBattle;

        // 16 R/G/B value cells (0..255) -- packed into BGR15 5-bit channels on write.
        readonly byte[] _r = new byte[16];
        readonly byte[] _g = new byte[16];
        readonly byte[] _b = new byte[16];

        // Test injection point. The override receives (ROM, map) and returns
        // success/failure. When set, the VM forwards Write() through it
        // instead of calling ImageBattleScreenCore.WriteBattleScreen directly.
        Func<ROM, ushort[], bool> _writerOverride;

        public ushort[] Map => _map;
        /// <summary>Resolved offset of the TSA1 region (top-left corner). Display-only.</summary>
        public uint TSA1Address { get => _tsa1Addr; set => SetField(ref _tsa1Addr, value); }
        public uint PaletteAddress { get => _paletteAddr; set => SetField(ref _paletteAddr, value); }
        public int PaletteIndex { get => _paletteIndex; set => SetField(ref _paletteIndex, value); }
        public int ZoomIndex { get => _zoomIndex; set => SetField(ref _zoomIndex, value); }
        public uint Image1Pointer { get => _image1Pointer; set => SetField(ref _image1Pointer, value); }
        public uint Image2Pointer { get => _image2Pointer; set => SetField(ref _image2Pointer, value); }
        public uint Image3Pointer { get => _image3Pointer; set => SetField(ref _image3Pointer, value); }
        public uint Image4Pointer { get => _image4Pointer; set => SetField(ref _image4Pointer, value); }
        public uint Image5Pointer { get => _image5Pointer; set => SetField(ref _image5Pointer, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>
        /// True only when the composited battle-screen preview rendered
        /// successfully (see <c>RenderBattlePreview</c>). Gates the read-only
        /// Export PNG button. Default false until the first successful render.
        /// </summary>
        public bool CanExportBattle { get => _canExportBattle; set => SetField(ref _canExportBattle, value); }

        public byte GetR(int index) => _r[index];
        public byte GetG(int index) => _g[index];
        public byte GetB(int index) => _b[index];

        public void SetR(int index, byte value) => _r[index] = ClampToFiveBit(value);
        public void SetG(int index, byte value) => _g[index] = ClampToFiveBit(value);
        public void SetB(int index, byte value) => _b[index] = ClampToFiveBit(value);

        static byte ClampToFiveBit(byte value) => (byte)((value >> 3) << 3);

        /// <summary>Test-only writer override.</summary>
        public void SetWriterOverrideForTests(Func<ROM, ushort[], bool> writer)
        {
            _writerOverride = writer;
        }

        /// <summary>
        /// Address-list adapter -- returns a single entry representing the
        /// one battle-screen layout (there's only one per ROM). Lets the
        /// view's `AddressListControl` participate in the parity gap-sweep.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;

            // The "address" is the TSA1 pointer (acts as the canonical
            // battle-screen identifier -- the editor's top-left corner).
            uint tsa1 = rom.p32(rom.RomInfo.battle_screen_TSA1_pointer);
            result.Add(new AddrResult(tsa1, "Battle Screen Layout", 0));
            return result;
        }

        /// <summary>
        /// Load the battle-screen TSA grid + palette block + 5 image pointers
        /// from CoreState.ROM into the VM properties.
        /// </summary>
        public void LoadEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            ushort[] map = ImageBattleScreenCore.LoadBattleScreen(rom);
            if (map != null) _map = map;

            _tsa1Addr = rom.p32(rom.RomInfo.battle_screen_TSA1_pointer);
            _paletteAddr = rom.p32(rom.RomInfo.battle_screen_palette_pointer);

            var colors = ImageBattleScreenCore.ReadPaletteBlock(rom, _paletteIndex);
            if (colors != null)
            {
                for (int i = 0; i < 16; i++)
                {
                    _r[i] = colors[i].r;
                    _g[i] = colors[i].g;
                    _b[i] = colors[i].b;
                }
            }

            _image1Pointer = ImageBattleScreenCore.ReadImagePointer(rom, rom.RomInfo.battle_screen_image1_pointer);
            _image2Pointer = ImageBattleScreenCore.ReadImagePointer(rom, rom.RomInfo.battle_screen_image2_pointer);
            _image3Pointer = ImageBattleScreenCore.ReadImagePointer(rom, rom.RomInfo.battle_screen_image3_pointer);
            _image4Pointer = ImageBattleScreenCore.ReadImagePointer(rom, rom.RomInfo.battle_screen_image4_pointer);
            _image5Pointer = ImageBattleScreenCore.ReadImagePointer(rom, rom.RomInfo.battle_screen_image5_pointer);

            IsLoaded = true;
            OnPropertyChanged(nameof(Map));
            OnPropertyChanged(nameof(TSA1Address));
            OnPropertyChanged(nameof(PaletteAddress));
            OnPropertyChanged(nameof(Image1Pointer));
            OnPropertyChanged(nameof(Image2Pointer));
            OnPropertyChanged(nameof(Image3Pointer));
            OnPropertyChanged(nameof(Image4Pointer));
            OnPropertyChanged(nameof(Image5Pointer));
        }

        /// <summary>
        /// Reload ONLY the palette R/G/B rows at the current
        /// <see cref="PaletteIndex"/>. Does NOT touch the TSA map or the
        /// 5 image pointers -- this preserves any in-flight image-pointer
        /// edits when the user switches palette types (Copilot CLI PR
        /// review round 1 finding #2: WF
        /// PaletteFormRef.MakePaletteROMToUI only reloads the palette UI
        /// on palette-index changes, not the image pointer fields).
        /// </summary>
        public void LoadPalette()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            var colors = ImageBattleScreenCore.ReadPaletteBlock(rom, _paletteIndex);
            if (colors != null)
            {
                for (int i = 0; i < 16; i++)
                {
                    _r[i] = colors[i].r;
                    _g[i] = colors[i].g;
                    _b[i] = colors[i].b;
                }
            }
        }

        /// <summary>
        /// Render the live 256x160 battle-screen preview (#802). Delegates to
        /// <see cref="ImageBattleScreenCore.RenderBattleScreenPreview"/> which
        /// composites the 5 LZ77 image strips + RAW 16-bank palette + the
        /// normalized TSA grid. Returns <c>null</c> (caller shows a blank
        /// surface) if any required source is missing/corrupt.
        /// </summary>
        public IImage RenderBattlePreview()
        {
            return ImageBattleScreenCore.RenderBattleScreenPreview(CoreState.ROM);
        }

        /// <summary>
        /// Render the chipset chip-list preview (#805, follow-up to #802).
        /// Delegates to <see cref="ImageBattleScreenCore.RenderChipsetPreview"/>
        /// which mirrors the WF <c>MakeCHIPLIST()</c> 8-column flip/palette-bank
        /// permutation grid (canvas <c>ChipCache.Width*4*2</c> x
        /// <c>ChipCache.Height</c>). Returns <c>null</c> (caller shows a blank
        /// surface) if any required source is missing/corrupt.
        /// </summary>
        public IImage RenderChipsetPreview()
        {
            return ImageBattleScreenCore.RenderChipsetPreview(CoreState.ROM);
        }

        /// <summary>
        /// Render the per-image preview for <paramref name="imageIndex"/> (0..4
        /// -> image1..image5) at its WinForms per-image dimensions (#816,
        /// follow-up to #802/#804/#807). Delegates to
        /// <see cref="ImageBattleScreenCore.RenderSingleImagePreview"/>:
        /// image1 = natural W x H, image2..image5 = liner-width x 8px, palette
        /// bank 0, index 0 opaque. Returns <c>null</c> (caller shows a blank
        /// surface) for a bad index or any corrupt/missing strip.
        /// </summary>
        public IImage RenderImagePreview(int imageIndex)
        {
            return ImageBattleScreenCore.RenderSingleImagePreview(CoreState.ROM, imageIndex);
        }

        /// <summary>
        /// Bulk-write the TSA grid + 5 image pointers to ROM. Caller MUST
        /// wrap this in <c>UndoService.Begin/Commit/Rollback</c>. Returns
        /// <c>true</c> on success.
        /// </summary>
        public bool Write()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;

            bool ok = _writerOverride != null
                ? _writerOverride(rom, _map)
                : ImageBattleScreenCore.WriteBattleScreen(rom, _map);

            if (!ok) return false;

            // Image pointer writes are also part of the bulk Write per WF
            // ImageBattleScreenForm.WriteButton_Click. They run under the
            // same ambient undo scope so the entire batch is atomic.
            //
            // Per Copilot CLI PR #594 round 3: propagate each WriteImagePointer
            // boolean result. If any of the 5 pointer slot writes fails
            // bounds validation (slot 0 / NOT_FOUND / out-of-range), return
            // false so WriteButton_Click rolls back the entire scope rather
            // than committing a partial write.
            if (!ImageBattleScreenCore.WriteImagePointer(rom, rom.RomInfo.battle_screen_image1_pointer, _image1Pointer)) return false;
            if (!ImageBattleScreenCore.WriteImagePointer(rom, rom.RomInfo.battle_screen_image2_pointer, _image2Pointer)) return false;
            if (!ImageBattleScreenCore.WriteImagePointer(rom, rom.RomInfo.battle_screen_image3_pointer, _image3Pointer)) return false;
            if (!ImageBattleScreenCore.WriteImagePointer(rom, rom.RomInfo.battle_screen_image4_pointer, _image4Pointer)) return false;
            if (!ImageBattleScreenCore.WriteImagePointer(rom, rom.RomInfo.battle_screen_image5_pointer, _image5Pointer)) return false;

            return true;
        }

        /// <summary>
        /// Write the palette block at <c>PaletteIndex</c> to ROM. Caller
        /// MUST wrap this in <c>UndoService.Begin/Commit/Rollback</c>.
        /// Returns <c>true</c> on success.
        /// </summary>
        public bool WritePalette()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return false;

            var colors = new (byte r, byte g, byte b)[16];
            for (int i = 0; i < 16; i++)
            {
                colors[i] = (_r[i], _g[i], _b[i]);
            }
            return ImageBattleScreenCore.WritePaletteBlock(rom, _paletteIndex, colors);
        }
    }
}
