// SPDX-License-Identifier: GPL-3.0-or-later
// ImageBattleAnimePallet VM rebuild for #399 gap-sweep parity. Mirrors WF
// `ImageBattleAnimePalletForm` LZ77-compressed palette editor.
//
// Per Copilot CLI plan-review v1..v7:
//   - Read/Write paths use FEBuilderGBA.Core/ImageBattleAnimePaletteCore which
//     handles LZ77 decompress/edit/recompress + LDR pointer rewrite under the
//     ambient UndoService scope (matches WF PaletteFormRef +
//     InputFormRef.WriteBinaryData semantics).
//   - Offsets-throughout contract: `_paletteOffset`, `_sourcePointerSlot` are
//     ROM offsets internally. The public PaletteAddress property converts
//     offset->pointer for display (user-facing).
//   - 32-color mode (Is32ColorMode / WarningVisible) is now driven by the Core
//     port BattleAnimeRendererCore.CountAnimationPaletteBanks (#1033) — an
//     animation-wide CONSERVATIVE OAM palette-bank scan replacing WF
//     ImageUtil.GetPalette16Count(DrawBitmap). The banner turns on when the scan
//     finds a non-affine sprite using a 16-color bank >= 1 (palette_count >= 2),
//     mirroring WF Is32ColorMode = (palette_count >= 2). This restores the
//     banner only; the WF JumpTo palette-index-0 redraw coupling remains out of
//     scope.
using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public partial class ImageBattleAnimePalletViewModel : ViewModelBase, INavigationTargetSource
    {
        /// <summary>Byte stride of one battle-animation list record.</summary>
        public const int AnimeRecordStride = 32; // 0x20

        /// <summary>Byte offset of the palette pointer slot inside an animation record.</summary>
        public const int PalettePointerOffsetInRecord = 28; // 0x1C

        // Internal offsets (per the offsets-throughout contract).
        uint _paletteOffset;
        uint _sourcePointerSlot;
        int _paletteTypeIndex;
        int _zoomIndex;
        bool _warningVisible;
        bool _is32ColorMode;
        bool _isLoaded;

        // 16 R/G/B value cells, stored as 0..255 (UI-facing) — packed back
        // into u16 5-bit components during Write.
        readonly byte[] _r = new byte[ImageBattleAnimePaletteCore.ColorsPerSlot];
        readonly byte[] _g = new byte[ImageBattleAnimePaletteCore.ColorsPerSlot];
        readonly byte[] _b = new byte[ImageBattleAnimePaletteCore.ColorsPerSlot];

        // Test injection point — passes through to Core.WritePalette.
        Func<ROM, byte[], uint> _writerOverride;

        /// <summary>
        /// Public palette address surface — display as GBA pointer
        /// (0x08...). On set, the value is converted back to an offset
        /// for internal use.
        /// </summary>
        public uint PaletteAddress
        {
            get => _paletteOffset == 0 ? 0u : U.toPointer(_paletteOffset);
            set
            {
                uint newOffset = value == 0 ? 0u : U.toOffset(value);
                if (SetField(ref _paletteOffset, newOffset))
                {
                    OnPropertyChanged(nameof(PaletteAddressDisplay));
                }
            }
        }

        /// <summary>Hex display string for the address (for status bars / labels).</summary>
        public string PaletteAddressDisplay => $"0x{PaletteAddress:X8}";

        /// <summary>Source pointer slot (offset) where the back-pointer lives.</summary>
        public uint SourcePointerSlot
        {
            get => _sourcePointerSlot;
            set
            {
                if (SetField(ref _sourcePointerSlot, value))
                {
                    OnPropertyChanged(nameof(SourcePointerSlotDisplay));
                }
            }
        }

        /// <summary>Hex display string for the source pointer slot.</summary>
        public string SourcePointerSlotDisplay => $"0x{_sourcePointerSlot:X8}";

        /// <summary>Active palette type (0=Player, 1=Enemy, 2=Other, 3=4th Army).</summary>
        public int PaletteTypeIndex
        {
            get => _paletteTypeIndex;
            set => SetField(ref _paletteTypeIndex, value);
        }

        /// <summary>Active zoom level (0..4).</summary>
        public int ZoomIndex
        {
            get => _zoomIndex;
            set => SetField(ref _zoomIndex, value);
        }

        /// <summary>
        /// Whether the 32-color-mode warning banner is visible. Set by
        /// <see cref="LoadEntry"/> to <see cref="Is32ColorMode"/> — true when the
        /// loaded animation's OAM sprites use more than one 16-color palette bank
        /// (#1033), detected via
        /// <c>BattleAnimeRendererCore.CountAnimationPaletteBanks</c>.
        /// </summary>
        public bool WarningVisible
        {
            get => _warningVisible;
            set => SetField(ref _warningVisible, value);
        }

        /// <summary>
        /// Whether the currently-loaded animation uses 32-color (multi-bank)
        /// mode. Set by <see cref="LoadEntry"/> from
        /// <c>BattleAnimeRendererCore.CountAnimationPaletteBanks(recordOffset) &gt;= 2</c>
        /// (#1033), mirroring WF <c>Is32ColorMode = (palette_count &gt;= 2)</c>.
        /// Drives <see cref="WarningVisible"/>.
        /// </summary>
        public bool Is32ColorMode
        {
            get => _is32ColorMode;
            set => SetField(ref _is32ColorMode, value);
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set => SetField(ref _isLoaded, value);
        }

        /// <summary>
        /// Returns the R/G/B value (0..255) at <paramref name="index"/> (0..15).
        /// </summary>
        public byte GetR(int index) => _r[index];
        public byte GetG(int index) => _g[index];
        public byte GetB(int index) => _b[index];

        /// <summary>Sets the R value (0..255) at <paramref name="index"/> (0..15).</summary>
        public void SetR(int index, byte value)
        {
            _r[index] = ClampToFiveBit(value);
        }
        /// <summary>Sets the G value (0..255) at <paramref name="index"/> (0..15).</summary>
        public void SetG(int index, byte value)
        {
            _g[index] = ClampToFiveBit(value);
        }
        /// <summary>Sets the B value (0..255) at <paramref name="index"/> (0..15).</summary>
        public void SetB(int index, byte value)
        {
            _b[index] = ClampToFiveBit(value);
        }

        static byte ClampToFiveBit(byte value)
        {
            // Round down to nearest multiple of 8 (5-bit GBA color step).
            return (byte)((value >> 3) << 3);
        }

        /// <summary>Test-only writer override (passes through to Core).</summary>
        public void SetWriterOverrideForTests(Func<ROM, byte[], uint> writer)
        {
            _writerOverride = writer;
        }

        /// <summary>
        /// Enumerate the battle-animation list at
        /// <see cref="ROMFEINFO.image_battle_animelist_pointer"/>. Each
        /// AddrResult carries:
        ///   - addr = palette offset (the LZ77 block)
        ///   - tag  = source pointer slot offset (entryOffset + 28)
        /// </summary>
        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;

            // Per PR #589 Copilot bot review round 4 #1: validate the
            // resolved table base with U.isSafetyOffset to avoid
            // following a corrupt pointer slot into the ROM header /
            // out-of-range regions.
            uint baseAddr = rom.p32(rom.RomInfo.image_battle_animelist_pointer);
            if (!U.isSafetyOffset(baseAddr, rom))
            {
                return result;
            }

            // Per PR #589 Copilot bot review round 4 #2: scan until we
            // hit either an invalid palette pointer (validated via
            // U.isSafetyOffset, which catches zero, header overlap, and
            // out-of-range slots in one check) or the ROM end.
            const int hardCap = 4096;
            for (int i = 0; i < hardCap; i++)
            {
                uint entryOffset = baseAddr + (uint)(i * AnimeRecordStride);
                if (entryOffset + AnimeRecordStride > (uint)rom.Data.Length)
                {
                    break;
                }

                uint sourceSlot = entryOffset + PalettePointerOffsetInRecord;
                uint paletteOffset = rom.p32(sourceSlot);

                // Single safety check: stop on any unsafe palette pointer
                // (0, header overlap, out of ROM). This replaces the
                // earlier two-step "zero stub then bounds check" -- a
                // genuinely-zero slot was always already caught by
                // U.isSafetyOffset, so the dedicated zero-stub branch
                // was redundant.
                if (!U.isSafetyOffset(paletteOffset, rom))
                {
                    break;
                }

                string name = $"{i:X2} BattleAnime";
                var ar = new AddrResult(paletteOffset, name, sourceSlot);
                result.Add(ar);
            }

            return result;
        }

        /// <summary>
        /// Load the palette block at <paramref name="paletteOffset"/>,
        /// populate all 16 R/G/B properties + swatches. Stores
        /// <paramref name="sourcePointerSlot"/> for later use by Write().
        /// </summary>
        public void LoadEntry(uint paletteOffset, uint sourcePointerSlot, int paletteIndex)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            _paletteOffset = U.toOffset(paletteOffset);
            _sourcePointerSlot = sourcePointerSlot;
            _paletteTypeIndex = paletteIndex;

            ushort[] colors = ImageBattleAnimePaletteCore.ReadPalette(rom, _paletteOffset, paletteIndex);
            if (colors != null)
            {
                for (int i = 0; i < ImageBattleAnimePaletteCore.ColorsPerSlot; i++)
                {
                    ushort gba = colors[i];
                    _r[i] = (byte)(((gba) & 0x1F) << 3);
                    _g[i] = (byte)(((gba >> 5) & 0x1F) << 3);
                    _b[i] = (byte)(((gba >> 10) & 0x1F) << 3);
                }
            }
            else
            {
                // Clear all rows if read failed.
                for (int i = 0; i < ImageBattleAnimePaletteCore.ColorsPerSlot; i++)
                {
                    _r[i] = 0; _g[i] = 0; _b[i] = 0;
                }
            }

            // #1033: 32-color mode detection — port of WF
            // ImageUtil.GetPalette16Count(DrawBitmap) via an OAM palette-bank scan.
            // The animation record begins at _sourcePointerSlot - 0x1C (the palette
            // pointer lives at record+0x1C), the same derivation
            // RenderSampleBattleAnime uses.
            int bankCount = 1;
            if (_sourcePointerSlot >= PalettePointerOffsetInRecord)
            {
                uint recordOffset = _sourcePointerSlot - PalettePointerOffsetInRecord;
                bankCount = BattleAnimeRendererCore.CountAnimationPaletteBanks(recordOffset);
            }
            _is32ColorMode = bankCount >= 2;   // mirrors WF: Is32ColorMode = (palette_count >= 2)
            _warningVisible = _is32ColorMode;

            IsLoaded = true;
            OnPropertyChanged(nameof(PaletteAddress));
            OnPropertyChanged(nameof(PaletteAddressDisplay));
            OnPropertyChanged(nameof(SourcePointerSlot));
            OnPropertyChanged(nameof(SourcePointerSlotDisplay));
            OnPropertyChanged(nameof(WarningVisible));
            OnPropertyChanged(nameof(Is32ColorMode));
        }

        /// <summary>
        /// Render the 12-frame battle-animation sample-preview grid for the
        /// currently-loaded entry, recolored with the active palette type
        /// (<see cref="PaletteTypeIndex"/>). Mirrors WinForms
        /// <c>ImageBattleAnimePalletForm.DrawSample(BattleAnimeID, paletteIndex)</c>
        /// (Phase 1 — uses the SAVED ROM palette, exactly like WF; live-spinner
        /// edits are out of scope because WF also previews the ROM palette).
        ///
        /// <para>The animation record offset is derived from the back-pointer
        /// slot: <c>_sourcePointerSlot</c> is <c>entryOffset + 0x1C</c> (the
        /// palette pointer lives at record+0x1C), so the record begins at
        /// <c>_sourcePointerSlot - 0x1C</c>. This is the same 0-based record
        /// the list loop produced (no WF <c>id-1</c> conversion).</para>
        ///
        /// <para>Returns null when no entry is loaded / the ROM or image
        /// service is unavailable / the record is unresolvable / there is no
        /// non-blank frame — the caller (view) treats null as "clear preview".</para>
        /// </summary>
        public IImage RenderSampleBattleAnime()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CoreState.ImageService == null) return null;
            if (_sourcePointerSlot < PalettePointerOffsetInRecord) return null;

            uint recordOffset = _sourcePointerSlot - PalettePointerOffsetInRecord;
            return BattleAnimeRendererCore.RenderSampleBattleAnime(recordOffset, _paletteTypeIndex);
        }

        /// <summary>
        /// Apply a 16-color GBA palette (32 bytes, RGB555 u16 LE pairs) to
        /// the VM R/G/B arrays. Mirrors WinForms
        /// <c>PaletteFormRef.MakePaletteBitmapToUI(bitmap, palette_index:0)</c>.
        ///
        /// <para>This is the injectable seam used by the file-import path and
        /// by unit tests (pass a hand-crafted 32-byte array instead of a
        /// file-dialog result). Caller wraps <see cref="Write"/> in
        /// <c>UndoService.Begin/Commit/Rollback</c> after this; this method
        /// itself is pure-state (no ROM writes).</para>
        /// </summary>
        /// <param name="gbaPalette16">
        /// 32 bytes: 16 GBA u16 colors in little-endian RGB555 format.
        /// Must be non-null and at least 32 bytes. Excess bytes are ignored.
        /// </param>
        /// <returns>
        /// <c>true</c> on success; <c>false</c> if null or shorter than 32 bytes.
        /// </returns>
        public bool DoImport(byte[] gbaPalette16)
        {
            if (gbaPalette16 == null || gbaPalette16.Length < ImageBattleAnimePaletteCore.SlotByteSize)
            {
                return false;
            }

            for (int i = 0; i < ImageBattleAnimePaletteCore.ColorsPerSlot; i++)
            {
                ushort gba = (ushort)(gbaPalette16[i * 2] | (gbaPalette16[i * 2 + 1] << 8));
                _r[i] = (byte)(((gba) & 0x1F) << 3);
                _g[i] = (byte)(((gba >> 5) & 0x1F) << 3);
                _b[i] = (byte)(((gba >> 10) & 0x1F) << 3);
            }
            return true;
        }

        /// <summary>
        /// Pack the UI R/G/B rows into GBA u16 colors and call
        /// <see cref="ImageBattleAnimePaletteCore.WritePalette"/>. Caller
        /// must wrap this in <c>UndoService.Begin/Commit/Rollback</c>.
        /// Returns the new palette offset, or <see cref="U.NOT_FOUND"/>
        /// on failure.
        /// </summary>
        public uint Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return U.NOT_FOUND;
            if (_paletteOffset == 0) return U.NOT_FOUND;

            ushort[] colors = new ushort[ImageBattleAnimePaletteCore.ColorsPerSlot];
            for (int i = 0; i < ImageBattleAnimePaletteCore.ColorsPerSlot; i++)
            {
                uint dr = (uint)((_r[i] >> 3) & 0x1F);
                uint dg = (uint)((_g[i] >> 3) & 0x1F);
                uint db = (uint)((_b[i] >> 3) & 0x1F);
                colors[i] = (ushort)(dr | (dg << 5) | (db << 10));
            }

            uint newOffset = ImageBattleAnimePaletteCore.WritePalette(
                rom,
                _paletteOffset,
                _paletteTypeIndex,
                colors,
                sourcePointerSlot: _sourcePointerSlot == 0 ? (uint?)null : _sourcePointerSlot,
                writerOverride: _writerOverride);

            if (newOffset != U.NOT_FOUND && newOffset != _paletteOffset)
            {
                _paletteOffset = newOffset;
                OnPropertyChanged(nameof(PaletteAddress));
                OnPropertyChanged(nameof(PaletteAddressDisplay));
            }
            return newOffset;
        }

        // -----------------------------------------------------------------
        // INavigationTargetSource — no follow-up jumps (KnownGap only).
        // -----------------------------------------------------------------
        public IReadOnlyList<NavigationTarget> GetNavigationTargets()
            => Array.Empty<NavigationTarget>();
    }
}
