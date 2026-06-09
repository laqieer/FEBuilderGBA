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
// RESOLVED (#808): BattleCanvasRender — RenderMainImage delegates to
//   ImageTSAEditorCore.TryRenderMainImage (read-only TSA-composited main image).
// RESOLVED (#819): ChipsetListRender — RenderChipList delegates to
//   ImageTSAEditorCore.RenderChipList (read-only orig/Hflip/Vflip/HVflip strip).
//
// Out-of-scope (KnownGap markers in the AXAML cover the labels-sweep audit):
//   - TSAByteWrite          — ImageFormRef.WriteImageData pointer-aware realloc
//   - PaletteToClipboard    — system clipboard interop (WinForms-only API)
//   - MainImageExport       — image1_Export raw-tilesheet PNG export (deferred)
// RESOLVED (#901): MainImageImport — image1_Import wired tilesheet-only via
//   TSAImageImportCore.ImportTSAImage (SAME-SIZE PNG -> EncodeDirectTiles4bpp
//   -> LZ77 -> repoint ONLY the ZImg pointer; TSA + palette untouched).
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

        // Per-cell TSA editing (#1005) — NON-header TSA only. Populated in Init
        // when !isHeaderTSA; left null (HasCells=false) for header-TSA so the
        // View keeps the cell-edit panel disabled.
        ushort[] _cells;
        int _maxTileId;

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

        bool _canExportBattle;
        /// <summary>
        /// True only when the TSA-composited main image rendered successfully
        /// (set by the View's RefreshBattleCanvas). The Export PNG button binds
        /// IsEnabled to this -- a loaded context whose render failed must stay
        /// disabled, so this is intentionally NOT just <see cref="IsContextLoaded"/>.
        /// </summary>
        public bool CanExportBattle { get => _canExportBattle; set => SetField(ref _canExportBattle, value); }

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

        // ---- Per-cell TSA editing (#1005), NON-header TSA only ----

        /// <summary>True when the NON-header TSA decoded into an editable cell grid.</summary>
        public bool HasCells => _cells != null;

        /// <summary>Cell grid columns (= Width8). 0 when no cells are loaded.</summary>
        public int CellCols => _cells != null ? (int)_width8 : 0;

        /// <summary>Cell grid rows (= Height8). 0 when no cells are loaded.</summary>
        public int CellRows => _cells != null ? (int)_height8 : 0;

        /// <summary>
        /// Highest editable tile id = <c>min(0x3FF, tilesheetTileCount-1)</c>.
        /// Exposed so the View can clamp the Tile ID NumericUpDown. 0 when no
        /// cells / tilesheet are loaded.
        /// </summary>
        public int MaxTileId => _maxTileId;

        /// <summary>
        /// True when per-cell editing is available: the NON-header TSA decoded
        /// into a cell grid. Header-TSA editing is out of scope (#1005), so this
        /// is gated on <c>!IsHeaderTSA</c> as well.
        /// </summary>
        public bool CanEditCells => HasCells && !_isHeaderTSA;

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

            // Per-cell TSA editing (#1005): decode the NON-header TSA into an
            // editable cell grid + compute the tile-id clamp. Header-TSA editing
            // is out of scope, so leave _cells null (HasCells / CanEditCells
            // false) when isHeaderTSA.
            PopulateCells();
        }

        /// <summary>
        /// Decode the current NON-header TSA into the editable <c>_cells</c>
        /// grid and compute <see cref="MaxTileId"/>. No-op (clears the grid)
        /// for header-TSA, an unset image/TSA slot, or any corrupt/out-of-bounds
        /// input. Invoked from <see cref="Init"/>.
        /// </summary>
        void PopulateCells()
        {
            _cells = null;
            _maxTileId = 0;

            if (_isHeaderTSA) return;                  // #1005: non-header only
            if (_zimgPointer == U.NOT_FOUND) return;
            if (_tsaPointer == U.NOT_FOUND) return;

            ROM rom = CoreState.ROM;
            if (rom == null) return;

            uint imageAddr = rom.p32(_zimgPointer);
            uint tsaAddr = rom.p32(_tsaPointer);

            ushort[] cells = ImageTSAEditorCore.DecodeTsaCells(
                rom, _width8, _height8, _isLZ77TSA, tsaAddr);
            if (cells == null) return;

            int tileCount = ImageTSAEditorCore.GetTilesheetTileCount(rom, imageAddr);
            // Clamp tile-id range to [0, min(0x3FF, tilesheetTileCount-1)]. When
            // the tilesheet can't be read (tileCount 0), fall back to the GBA
            // 10-bit hardware max so the panel stays usable.
            _maxTileId = tileCount > 0 ? Math.Min(0x3FF, tileCount - 1) : 0x3FF;
            _cells = cells;
        }

        /// <summary>
        /// Read the GBA screen-entry u16 at cell (<paramref name="x"/>,
        /// <paramref name="y"/>). Returns 0 for out-of-range coordinates or when
        /// no cells are loaded.
        /// </summary>
        public ushort GetCell(int x, int y)
        {
            if (_cells == null) return 0;
            if (x < 0 || y < 0 || x >= CellCols || y >= CellRows) return 0;
            return _cells[y * CellCols + x];
        }

        /// <summary>
        /// Bit-pack a tile id + flip flags + palette bank into cell
        /// (<paramref name="x"/>, <paramref name="y"/>). The tile id is clamped
        /// to <c>[0, MaxTileId]</c>, the bank to <c>[0, 15]</c>. No-op for
        /// out-of-range coordinates or when no cells are loaded.
        /// </summary>
        public void SetCell(int x, int y, int tileId, bool hflip, bool vflip, int bank)
        {
            if (_cells == null) return;
            if (x < 0 || y < 0 || x >= CellCols || y >= CellRows) return;
            int clampedTile = Math.Clamp(tileId, 0, Math.Max(0, _maxTileId));
            int clampedBank = Math.Clamp(bank, 0, 0xF);
            _cells[y * CellCols + x] =
                ImageTSAEditorCore.SerializeCell(clampedTile, hflip, vflip, clampedBank);
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
        /// Render the TSA-composited main image (read-only) for the
        /// <c>BattlePreview</c> control and PNG export (#808). Resolves the
        /// pointer slots to data addresses exactly like WinForms
        /// ImageTSAEditorForm, then delegates to
        /// <see cref="ImageTSAEditorCore.TryRenderMainImage"/>.
        ///
        /// Returns null (no throw) when there is no context, when the image or
        /// TSA pointer slots are unset (U.NOT_FOUND), or when the underlying
        /// render fails. A NOT_FOUND palette pointer is NOT a failure -- it is
        /// the expected raw-address fallback handled by
        /// <see cref="ResolveActivePaletteAddress"/>.
        /// </summary>
        public IImage RenderMainImage()
        {
            if (!IsContextLoaded) return null;

            // The image and TSA pointer slots must be real before we follow
            // them with p32; only the palette pointer has a raw-address fallback.
            if (_zimgPointer == U.NOT_FOUND) return null;
            if (_tsaPointer == U.NOT_FOUND) return null;

            ROM rom = CoreState.ROM;
            if (rom == null) return null;

            uint imageAddr = rom.p32(_zimgPointer);
            uint tsaAddr = rom.p32(_tsaPointer);
            uint paletteAddr = ResolveActivePaletteAddress();

            // Per-cell editing (#1005): when an editable NON-header cell grid is
            // loaded, render from the IN-MEMORY cells so the preview reflects
            // unsaved edits. Otherwise fall back to the read-only ROM path.
            if (HasCells)
            {
                return ImageTSAEditorCore.RenderMainImageFromCells(
                    rom, _width8, _height8, imageAddr, _cells, paletteAddr);
            }

            return ImageTSAEditorCore.TryRenderMainImage(
                rom, _width8, _height8, imageAddr,
                _isHeaderTSA, _isLZ77TSA, tsaAddr, paletteAddr);
        }

        /// <summary>
        /// Write the edited NON-header TSA cells back to ROM (#1005). Resolves
        /// the TSA pointer SLOT (for the LZ77 repoint path) and the resolved
        /// data address (for the raw in-place path), then delegates to
        /// <see cref="ImageTSAEditorCore.WriteTsaCells"/>. Must run inside the
        /// caller's ambient UndoService.Begin / Commit scope. Returns "" on
        /// success or a localized error string (with ZERO surviving mutation on
        /// any failure — see WriteTsaCells' defensive snapshot restore).
        /// </summary>
        public string WriteTsa()
        {
            if (!CanEditCells) return R._("Per-cell TSA editing is not available.");

            ROM rom = CoreState.ROM;
            if (rom == null) return R._("ROM is not loaded.");
            if (_tsaPointer == U.NOT_FOUND) return R._("The TSA pointer is not set.");

            // _tsaPointer is the POINTER SLOT (RenderMainImage resolves it via
            // rom.p32). The LZ77 path repoints the slot; the raw path overwrites
            // the resolved data address in place.
            uint tsaDataAddr = rom.p32(_tsaPointer);

            return ImageTSAEditorCore.WriteTsaCells(
                rom, _width8, _height8, _isLZ77TSA, _tsaPointer, tsaDataAddr, _cells);
        }

        /// <summary>
        /// Render the chip-list thumbnail (read-only) for the
        /// <c>ChipListPreview</c> control (#819). Resolves the SAME image +
        /// palette addresses as <see cref="RenderMainImage"/> (the chip list is
        /// a pure tile-strip, so there is NO TSA pointer) and delegates to
        /// <see cref="ImageTSAEditorCore.RenderChipList"/>.
        ///
        /// Returns null (no throw) when there is no context, when the image
        /// pointer slot is unset (U.NOT_FOUND), or when the underlying render
        /// fails. A NOT_FOUND palette pointer is NOT a failure — it is the
        /// expected raw-address fallback handled by
        /// <see cref="ResolveActivePaletteAddress"/>.
        /// </summary>
        public IImage RenderChipList()
        {
            if (!IsContextLoaded) return null;

            // The image pointer slot must be real before we follow it with p32;
            // only the palette pointer has a raw-address fallback. The chip list
            // never reads the TSA stream, so _tsaPointer is irrelevant here.
            if (_zimgPointer == U.NOT_FOUND) return null;

            ROM rom = CoreState.ROM;
            if (rom == null) return null;

            uint imageAddr = rom.p32(_zimgPointer);
            uint paletteAddr = ResolveActivePaletteAddress();

            return ImageTSAEditorCore.RenderChipList(rom, imageAddr, paletteAddr);
        }

        /// <summary>
        /// Render the RAW tilesheet (read-only) for the Main Image tab's
        /// "Image Export" PNG (#974). Resolves the SAME image + palette
        /// addresses as <see cref="RenderChipList"/> (the raw tilesheet is a
        /// pure tile-strip, so there is NO TSA pointer) and delegates to
        /// <see cref="ImageTSAEditorCore.RenderRawTilesheet"/>.
        ///
        /// Returns null (no throw) when there is no context, when the image
        /// pointer slot is unset (U.NOT_FOUND), or when the underlying render
        /// fails. A NOT_FOUND palette pointer is NOT a failure — it is the
        /// expected raw-address fallback handled by
        /// <see cref="ResolveActivePaletteAddress"/>.
        /// </summary>
        public IImage RenderRawTilesheet()
        {
            if (!IsContextLoaded) return null;

            // The image pointer slot must be real before we follow it with p32;
            // only the palette pointer has a raw-address fallback. The raw
            // tilesheet never reads the TSA stream, so _tsaPointer is irrelevant.
            if (_zimgPointer == U.NOT_FOUND) return null;

            ROM rom = CoreState.ROM;
            if (rom == null) return null;

            uint imageAddr = rom.p32(_zimgPointer);
            uint paletteAddr = ResolveActivePaletteAddress();

            return ImageTSAEditorCore.RenderRawTilesheet(rom, imageAddr, paletteAddr);
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
        ///
        /// Uses 64-bit arithmetic for the base+offset computation so that
        /// pathological inputs (a near-<see cref="uint.MaxValue"/> address
        /// combined with a large palette index) cannot wrap into a "safe"
        /// in-ROM range and bypass <see cref="U.isSafetyOffset"/>.
        /// </summary>
        public bool TryLoadPalette(uint paletteAddr, int paletteIndex, out (byte R, byte G, byte B)[] result)
        {
            result = new (byte R, byte G, byte B)[16];
            ROM rom = CoreState.ROM;
            if (rom == null) return false;
            if (paletteIndex < 0) return false;

            // 64-bit math to catch overflow before it bypasses U.isSafetyOffset.
            ulong baseAddr64 = (ulong)paletteAddr + (ulong)((uint)paletteIndex) * 0x20UL;
            ulong endAddr64 = baseAddr64 + 0x1FUL;
            if (endAddr64 > uint.MaxValue) return false;

            uint baseAddr = (uint)baseAddr64;
            uint endAddr = (uint)endAddr64;
            // 0x20 bytes (16 ushorts) must all be in-ROM.
            if (!U.isSafetyOffset(baseAddr, rom)) return false;
            if (!U.isSafetyOffset(endAddr, rom)) return false;

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

            // 64-bit math so a near-uint.MaxValue paletteAddr + paletteIndex*0x20
            // cannot wrap and bypass U.isSafetyOffset (Copilot review feedback).
            ulong baseAddr64 = (ulong)paletteAddr + (ulong)((uint)paletteIndex) * 0x20UL;
            ulong endAddr64 = baseAddr64 + 0x1FUL;
            if (endAddr64 > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(paletteAddr),
                    $"WritePalette: address+index arithmetic overflows uint (base=0x{paletteAddr:X}, idx={paletteIndex})");
            }
            uint baseAddr = (uint)baseAddr64;
            uint endAddr = (uint)endAddr64;
            if (!U.isSafetyOffset(baseAddr, rom) || !U.isSafetyOffset(endAddr, rom))
            {
                throw new ArgumentOutOfRangeException(nameof(paletteAddr),
                    $"WritePalette: 0x20-byte range [0x{baseAddr:X}..0x{endAddr:X}] is outside ROM bounds");
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
        /// Build the palette-to-clipboard hex string (#974): a 64-character
        /// string of 16 entries, 4 big-endian hex chars each. Mirrors WinForms
        /// <c>PaletteFormRef.PALETTE_TO_CLIPBOARD_BUTTON_Click</c>, which packs
        /// each entry to its raw GBA bytes and formats with
        /// <c>U.big16(...).ToString("X04")</c>. <c>U.big16</c> byte-swaps the
        /// little-endian GBA 5-5-5 word into a big-endian display value, so the
        /// emitted hex equals <c>byteswap(PackRgb555(...))</c> — e.g. white
        /// (0x7FFF, LE bytes FF 7F) renders as "FF7F".
        /// </summary>
        /// <param name="rgb">Exactly 16 RGB triplets (the current palette UI).</param>
        internal static string BuildPaletteClipboardHex((byte R, byte G, byte B)[] rgb)
        {
            if (rgb == null) throw new ArgumentNullException(nameof(rgb));
            if (rgb.Length != 16)
                throw new ArgumentException("BuildPaletteClipboardHex expects exactly 16 entries", nameof(rgb));

            var sb = new System.Text.StringBuilder(64);
            for (int i = 0; i < 16; i++)
            {
                ushort word = PackRgb555(rgb[i].R, rgb[i].G, rgb[i].B);
                // U.big16-equivalent byte-swap of the little-endian GBA word.
                uint big = (uint)(((word & 0xFF) << 8) | (word >> 8));
                sb.Append(big.ToString("X04"));
            }
            return sb.ToString();
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
