// SPDX-License-Identifier: GPL-3.0-or-later
// ImageTSAEditorViewModel — Avalonia parity raise (gap-sweep #398).
//
// The TSA editor is context-injected in WinForms (callers pass width/height,
// ZIMG pointer, TSA pointer, palette pointer, palette address, and the
// header/LZ77 flags via Init). The Avalonia ViewModel mirrors that contract:
//
//   - Init(...) captures the caller-context and sets IsContextLoaded=true.
//   - LoadPalette(addr, idx) unpacks a 16-color 5-5-5 little-endian palette.
//   - WritePalette(addr, idx, rgb) packs 5-5-5 little-endian and writes the
//     16 ushort entries through rom.write_u16(...) so an ambient
//     UndoService.Begin scope captures them.
//
// Out-of-scope (KnownGap markers in the AXAML cover the labels-sweep audit):
//   - BattleCanvasRender    — live LZ77 + TSA blit through ImageUtil.BitBlt
//   - ChipsetListRender     — permuted (orig / Hflip / Vflip / HVflip) chipset
//   - TSAByteWrite          — ImageFormRef.WriteImageData pointer-aware realloc
//   - PaletteToClipboard    — system clipboard interop (WinForms-only API)
//   - MainImageImportExport — image1_Import / image1_Export through ImageFormRef
using System;
using System.Collections.Generic;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Avalonia ViewModel for the TSA Tile Editor. Mirrors the WinForms
    /// ImageTSAEditorForm.Init context (width8/height8/ZIMG/TSA pointers
    /// + palette pointer + palette address + LZ77 / header flags) but
    /// strips the live Bitmap rendering since System.Drawing is WinForms-
    /// only. The 16-color RGB 5-5-5 palette round-trip ships here; the
    /// TSA-byte-write + chipset/canvas rendering are deferred per the
    /// gap-sweep #398 plan.
    /// </summary>
    public class ImageTSAEditorViewModel : ViewModelBase
    {
        uint _currentAddr;
        bool _isLoaded;
        bool _isContextLoaded;
        uint _width8;
        uint _height8;
        uint _zimgPointer;
        bool _isHeaderTSA;
        bool _isLZ77TSA;
        uint _tsaPointer;
        uint _palettePointer;
        uint _paletteAddress;
        int _paletteCount;

        /// <summary>Currently selected entry address (kept for IEditorView contract).</summary>
        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }

        /// <summary>Generic-loaded flag (IEditorView contract).</summary>
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>
        /// True once a caller has invoked <see cref="Init"/> with valid
        /// context. The View binds Write / PaletteWrite IsEnabled to this
        /// flag so the default standalone open path never writes against
        /// uninitialised pointers.
        /// </summary>
        public bool IsContextLoaded { get => _isContextLoaded; set => SetField(ref _isContextLoaded, value); }

        // Caller-context, read by the view's Write_Click / PaletteWrite_Click
        // handlers and the static unit tests. Settable via Init only.

        /// <summary>Image width in 8-pixel tiles.</summary>
        public uint Width8 => _width8;
        /// <summary>Image height in 8-pixel tiles.</summary>
        public uint Height8 => _height8;
        /// <summary>Pointer-table address that holds the LZ77-compressed image pointer.</summary>
        public uint ZImgPointer => _zimgPointer;
        /// <summary>True if the TSA stream carries an FE-style {width8, height8} header.</summary>
        public bool IsHeaderTSA => _isHeaderTSA;
        /// <summary>True if the TSA stream is LZ77-compressed.</summary>
        public bool IsLZ77TSA => _isLZ77TSA;
        /// <summary>Pointer-table address that holds the TSA stream pointer.</summary>
        public uint TSAPointer => _tsaPointer;
        /// <summary>Pointer-table address that holds the palette pointer (U.NOT_FOUND means use raw address).</summary>
        public uint PalettePointer => _palettePointer;
        /// <summary>Direct palette address (only used if PalettePointer == U.NOT_FOUND).</summary>
        public uint PaletteAddress => _paletteAddress;
        /// <summary>Number of palette slots to expose to the UI (clamps the slot combo).</summary>
        public int PaletteCount => _paletteCount;

        public ImageTSAEditorViewModel()
        {
            _palettePointer = U.NOT_FOUND;
        }

        /// <summary>
        /// Mirrors WinForms ImageTSAEditorForm.Init: snapshot caller context
        /// and flip IsContextLoaded to enable Write / PaletteWrite.
        ///
        /// The IsHeaderTSA path mirrors WF: the effective canvas is forced
        /// to at least 256x160 (32x20 tiles) so embedded header dimensions
        /// don't shrink the editable region.
        /// </summary>
        public void Init(uint width8,
                         uint height8,
                         uint zimgPointer,
                         bool isHeaderTSA,
                         bool isLZ77TSA,
                         uint tsaPointer,
                         uint palettePointer,
                         uint paletteAddress,
                         int paletteCount)
        {
            _zimgPointer = zimgPointer;
            _isHeaderTSA = isHeaderTSA;
            _isLZ77TSA = isLZ77TSA;
            _tsaPointer = tsaPointer;
            _palettePointer = palettePointer;
            _paletteAddress = paletteAddress;
            _paletteCount = paletteCount;

            if (isHeaderTSA)
            {
                // WF: Width8 = Math.Max(256/8, width8); Height8 = Math.Max(160/8, height8);
                _width8 = Math.Max(256u / 8u, width8);
                _height8 = Math.Max(160u / 8u, height8);
            }
            else
            {
                _width8 = width8;
                _height8 = height8;
            }

            IsContextLoaded = true;
            IsLoaded = true;
        }

        /// <summary>
        /// Resolve the active palette address. If PalettePointer is a real
        /// pointer slot (i.e. not U.NOT_FOUND), follow the slot; otherwise
        /// fall back to the literal PaletteAddress. Mirrors WF logic in
        /// ImageTSAEditorForm.Init.
        /// </summary>
        public uint ResolveActivePaletteAddress()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return 0;

            if (_palettePointer == U.NOT_FOUND)
            {
                return U.toOffset(_paletteAddress);
            }
            return rom.p32(_palettePointer);
        }

        /// <summary>
        /// Read 16 GBA 5-5-5 palette entries starting at
        /// <paramref name="paletteAddr"/> + <paramref name="paletteIndex"/> * 0x20.
        /// Each entry is a little-endian ushort: 0RRRRR GGGGG BBBBB.
        /// Returned RGB bytes are 8-bit by left-shifting each 5-bit field by 3.
        ///
        /// Out-of-bounds inputs (negative index, invalid address, range that
        /// does not fit in <c>ROM.Data</c>) return a 16-entry zero palette
        /// instead of throwing, so a user typing an invalid Palette Address
        /// cannot crash the load path. Callers that need to know about the
        /// rejection should use <see cref="TryLoadPalette"/> instead.
        /// </summary>
        public (byte R, byte G, byte B)[] LoadPalette(uint paletteAddr, int paletteIndex)
        {
            TryLoadPalette(paletteAddr, paletteIndex, out var result);
            return result;
        }

        /// <summary>
        /// Bounds-checked variant of <see cref="LoadPalette"/>. Returns true
        /// when the full 16-entry 0x20-byte block is readable; otherwise
        /// fills <paramref name="result"/> with a 16-entry zero palette and
        /// returns false so the caller can surface a UI error.
        /// </summary>
        public bool TryLoadPalette(uint paletteAddr, int paletteIndex, out (byte R, byte G, byte B)[] result)
        {
            result = new (byte R, byte G, byte B)[16];
            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (paletteIndex < 0) return false;

            uint baseAddr = paletteAddr + (uint)(paletteIndex * 0x20);
            // 0x20 bytes (16 ushorts) must all be in-ROM.
            if (!U.isSafetyOffset(baseAddr, rom)) return false;
            if (!U.isSafetyOffset(baseAddr + 0x1F, rom)) return false;

            for (int i = 0; i < 16; i++)
            {
                uint word = rom.u16(baseAddr + (uint)(i * 2));
                int r5 = (int)((word >> 0) & 0x1F);
                int g5 = (int)((word >> 5) & 0x1F);
                int b5 = (int)((word >> 10) & 0x1F);
                result[i] = ((byte)(r5 << 3), (byte)(g5 << 3), (byte)(b5 << 3));
            }
            return true;
        }

        /// <summary>
        /// Pack 16 8-bit RGB tuples into GBA 5-5-5 little-endian ushorts and
        /// write them at <paramref name="paletteAddr"/> +
        /// <paramref name="paletteIndex"/> * 0x20. Caller must wrap this in
        /// an ambient UndoService.Begin / Commit scope.
        ///
        /// Throws when <paramref name="paletteIndex"/> is negative or
        /// (when PaletteCount is known) >= PaletteCount, or when the target
        /// 0x20-byte range does not fit in ROM.Data. Failing fast before any
        /// writes lets the surrounding UndoService.Rollback unwind cleanly
        /// without touching the ROM.
        /// </summary>
        public void WritePalette(uint paletteAddr, int paletteIndex, (byte R, byte G, byte B)[] rgb)
        {
            if (rgb == null) throw new ArgumentNullException(nameof(rgb));
            if (rgb.Length != 16) throw new ArgumentException("WritePalette expects exactly 16 entries", nameof(rgb));
            if (paletteIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(paletteIndex),
                    "WritePalette: paletteIndex must be >= 0");
            }
            // Clamp against the caller-supplied PaletteCount when one was
            // injected via Init (PaletteCount > 0). PaletteCount==0 means the
            // ViewModel was never Init'd, which the View already gates with
            // IsContextLoaded; we still guard here so direct VM callers and
            // tests get a consistent failure mode.
            if (_paletteCount > 0 && paletteIndex >= _paletteCount)
            {
                throw new ArgumentOutOfRangeException(nameof(paletteIndex),
                    $"WritePalette: paletteIndex {paletteIndex} is out of range for PaletteCount {_paletteCount}");
            }

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint baseAddr = paletteAddr + (uint)(paletteIndex * 0x20);
            if (!U.isSafetyOffset(baseAddr, rom) || !U.isSafetyOffset(baseAddr + 0x1F, rom))
            {
                throw new ArgumentOutOfRangeException(nameof(paletteAddr),
                    $"WritePalette: 0x20-byte range [0x{baseAddr:X}..0x{baseAddr + 0x1F:X}] is outside ROM bounds");
            }

            for (int i = 0; i < 16; i++)
            {
                ushort word = PackRgb555(rgb[i].R, rgb[i].G, rgb[i].B);
                rom.write_u16(baseAddr + (uint)(i * 2), word);
            }
        }

        /// <summary>
        /// Pack an 8-bit RGB triplet into a GBA 5-5-5 ushort. The high bit
        /// is always 0; the layout is 0BBBBB GGGGG RRRRR (the GBA stores
        /// little-endian, so byte 0 carries R[7:3] and G[2:0]).
        /// </summary>
        internal static ushort PackRgb555(byte r, byte g, byte b)
        {
            int r5 = (r >> 3) & 0x1F;
            int g5 = (g >> 3) & 0x1F;
            int b5 = (b >> 3) & 0x1F;
            return (ushort)((b5 << 10) | (g5 << 5) | r5);
        }

        /// <summary>
        /// Build the static one-row entry list. The TSA editor is invoked
        /// context-dependently (callers wire in width/height/pointers via
        /// Init) so the entry list mirrors that pattern with a single row.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            var result = new List<AddrResult>();
            result.Add(new AddrResult(0, "TSA Tile Editor", 0));
            return result;
        }

        /// <summary>
        /// Selecting the (only) entry just records the address and flips
        /// IsLoaded — the actual context comes from a subsequent Init call.
        /// </summary>
        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            CurrentAddr = addr;
            IsLoaded = true;
        }
    }
}
