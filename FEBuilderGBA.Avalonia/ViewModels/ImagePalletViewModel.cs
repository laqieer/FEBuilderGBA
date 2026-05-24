// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia counterpart of WinForms ImagePalletForm. Gap-sweep fix
// (#400) replaces a 3-control stub with a full 16-color BGR15 palette
// editor backed by FEBuilderGBA.Core/PaletteCore.cs.
//
// Each loaded entry has 16 (R, G, B) byte tuples surfaced as 48
// individual properties (R1..R16, G1..G16, B1..B16) so the AXAML
// NumericUpDown bindings can target each value directly.
//
// PaletteAddress is always stored as a GBA pointer (the value passed
// in by callers like ImagePortraitView.JumpToPalette_Click); the
// pointer-vs-offset normalization is centralized inside PaletteCore.

using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for the Avalonia palette editor (#400 gap-sweep fix).
    ///
    /// NOTE: deliberately does NOT participate in the --data-verify
    /// sweep. The palette editor is opened directly via
    /// <c>JumpTo(...)</c> from caller editors (eg. ImagePortraitView),
    /// it is not on the FormMappings list, and it does not have an
    /// iterable "load every entry" surface (LoadList returns a single
    /// placeholder row).
    /// </summary>
    public class ImagePalletViewModel : ViewModelBase
    {
        // ---- backing fields ----

        uint _paletteAddress;
        int _paletteIndex;
        int _maxPaletteCount = PaletteCore.MAX_PALETTE_COUNT;
        int _zoomIndex;
        bool _isLoaded;
        IReadOnlyList<string> _paletteIndexNames = Array.Empty<string>();

        // 48 RGB fields - one byte per channel per slot.
        byte _r1, _r2, _r3, _r4, _r5, _r6, _r7, _r8, _r9, _r10, _r11, _r12, _r13, _r14, _r15, _r16;
        byte _g1, _g2, _g3, _g4, _g5, _g6, _g7, _g8, _g9, _g10, _g11, _g12, _g13, _g14, _g15, _g16;
        byte _b1, _b2, _b3, _b4, _b5, _b6, _b7, _b8, _b9, _b10, _b11, _b12, _b13, _b14, _b15, _b16;

        // ---- public properties ----

        /// <summary>Palette base address (GBA pointer or raw offset).
        /// Pointer/offset normalization happens inside PaletteCore.</summary>
        public uint PaletteAddress { get => _paletteAddress; set => SetField(ref _paletteAddress, value); }

        /// <summary>Index of the currently edited palette block (0..MaxPaletteCount-1).</summary>
        public int PaletteIndex { get => _paletteIndex; set => SetField(ref _paletteIndex, value); }

        /// <summary>Number of contiguous palettes at PaletteAddress (1..16).</summary>
        public int MaxPaletteCount { get => _maxPaletteCount; set => SetField(ref _maxPaletteCount, value); }

        /// <summary>Currently selected zoom mode (0..4) for the preview image.</summary>
        public int ZoomIndex { get => _zoomIndex; set => SetField(ref _zoomIndex, value); }

        /// <summary>True once LoadEntry has populated the ViewModel.</summary>
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        /// <summary>Display names for the palette-index combo box.</summary>
        public IReadOnlyList<string> PaletteIndexNames
        {
            get => _paletteIndexNames;
            set => SetField(ref _paletteIndexNames, value ?? Array.Empty<string>());
        }

        public byte R1 { get => _r1; set => SetField(ref _r1, value); }
        public byte R2 { get => _r2; set => SetField(ref _r2, value); }
        public byte R3 { get => _r3; set => SetField(ref _r3, value); }
        public byte R4 { get => _r4; set => SetField(ref _r4, value); }
        public byte R5 { get => _r5; set => SetField(ref _r5, value); }
        public byte R6 { get => _r6; set => SetField(ref _r6, value); }
        public byte R7 { get => _r7; set => SetField(ref _r7, value); }
        public byte R8 { get => _r8; set => SetField(ref _r8, value); }
        public byte R9 { get => _r9; set => SetField(ref _r9, value); }
        public byte R10 { get => _r10; set => SetField(ref _r10, value); }
        public byte R11 { get => _r11; set => SetField(ref _r11, value); }
        public byte R12 { get => _r12; set => SetField(ref _r12, value); }
        public byte R13 { get => _r13; set => SetField(ref _r13, value); }
        public byte R14 { get => _r14; set => SetField(ref _r14, value); }
        public byte R15 { get => _r15; set => SetField(ref _r15, value); }
        public byte R16 { get => _r16; set => SetField(ref _r16, value); }

        public byte G1 { get => _g1; set => SetField(ref _g1, value); }
        public byte G2 { get => _g2; set => SetField(ref _g2, value); }
        public byte G3 { get => _g3; set => SetField(ref _g3, value); }
        public byte G4 { get => _g4; set => SetField(ref _g4, value); }
        public byte G5 { get => _g5; set => SetField(ref _g5, value); }
        public byte G6 { get => _g6; set => SetField(ref _g6, value); }
        public byte G7 { get => _g7; set => SetField(ref _g7, value); }
        public byte G8 { get => _g8; set => SetField(ref _g8, value); }
        public byte G9 { get => _g9; set => SetField(ref _g9, value); }
        public byte G10 { get => _g10; set => SetField(ref _g10, value); }
        public byte G11 { get => _g11; set => SetField(ref _g11, value); }
        public byte G12 { get => _g12; set => SetField(ref _g12, value); }
        public byte G13 { get => _g13; set => SetField(ref _g13, value); }
        public byte G14 { get => _g14; set => SetField(ref _g14, value); }
        public byte G15 { get => _g15; set => SetField(ref _g15, value); }
        public byte G16 { get => _g16; set => SetField(ref _g16, value); }

        public byte B1 { get => _b1; set => SetField(ref _b1, value); }
        public byte B2 { get => _b2; set => SetField(ref _b2, value); }
        public byte B3 { get => _b3; set => SetField(ref _b3, value); }
        public byte B4 { get => _b4; set => SetField(ref _b4, value); }
        public byte B5 { get => _b5; set => SetField(ref _b5, value); }
        public byte B6 { get => _b6; set => SetField(ref _b6, value); }
        public byte B7 { get => _b7; set => SetField(ref _b7, value); }
        public byte B8 { get => _b8; set => SetField(ref _b8, value); }
        public byte B9 { get => _b9; set => SetField(ref _b9, value); }
        public byte B10 { get => _b10; set => SetField(ref _b10, value); }
        public byte B11 { get => _b11; set => SetField(ref _b11, value); }
        public byte B12 { get => _b12; set => SetField(ref _b12, value); }
        public byte B13 { get => _b13; set => SetField(ref _b13, value); }
        public byte B14 { get => _b14; set => SetField(ref _b14, value); }
        public byte B15 { get => _b15; set => SetField(ref _b15, value); }
        public byte B16 { get => _b16; set => SetField(ref _b16, value); }

        // ---- list / entry loading ----

        /// <summary>
        /// Returns a single placeholder entry. The palette editor is
        /// opened directly via JumpTo(...) from caller editors (eg.
        /// ImagePortraitView), so this list is a UI placeholder for
        /// the AddressListControl - it has no functional meaning.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            var result = new List<AddrResult>();
            result.Add(new AddrResult(0u, "Palette Editor", 0u));
            return result;
        }

        /// <summary>
        /// Single-arg overload mirroring the generic ViewModel
        /// contract used by the AddressListControl wiring. Uses
        /// MAX_PALETTE_COUNT=16 default, PaletteIndex=0, no palette
        /// names.
        /// </summary>
        public void LoadEntry(uint addr) => LoadEntry(addr, PaletteCore.MAX_PALETTE_COUNT, 0, null);

        /// <summary>
        /// Full LoadEntry mirrors WF ImagePalletForm.JumpTo. Reads 16
        /// colors from ROM at <paramref name="paletteAddress"/> +
        /// <paramref name="defaultSelectPalette"/> * 32 and exposes
        /// them as R1..R16/G1..G16/B1..B16 (so AXAML NumericUpDown
        /// bindings can target each value).
        /// </summary>
        public void LoadEntry(uint paletteAddress, int maxPaletteCount, int defaultSelectPalette, string[]? paletteNames)
        {
            var rom = CoreState.ROM;
            if (rom == null) return;
            if (maxPaletteCount < 1) maxPaletteCount = 1;
            if (maxPaletteCount > PaletteCore.MAX_PALETTE_COUNT)
                maxPaletteCount = PaletteCore.MAX_PALETTE_COUNT;
            if (defaultSelectPalette < 0) defaultSelectPalette = 0;
            if (defaultSelectPalette >= maxPaletteCount) defaultSelectPalette = maxPaletteCount - 1;

            IsLoading = true;
            try
            {
                PaletteAddress = paletteAddress;
                MaxPaletteCount = maxPaletteCount;
                PaletteIndex = defaultSelectPalette;
                PaletteIndexNames = BuildIndexNames(maxPaletteCount, paletteNames);

                ApplyPaletteFromRom();
                IsLoaded = true;
            }
            finally
            {
                IsLoading = false;
                MarkClean();
            }
        }

        /// <summary>
        /// Re-read the palette from ROM at the current
        /// (PaletteAddress, PaletteIndex) and refresh all 48 R/G/B
        /// fields. Call after PaletteIndex changes so the displayed
        /// values match the new slot.
        /// </summary>
        public void ApplyPaletteFromRom()
        {
            var rom = CoreState.ROM;
            if (rom == null) return;
            var colors = PaletteCore.ReadPalette(rom.Data, PaletteAddress, PaletteIndex);
            for (int i = 0; i < 16; i++)
                SetSlot(i, colors[i].r, colors[i].g, colors[i].b);
        }

        /// <summary>
        /// Pack the 48 R/G/B fields into a 32-byte BGR15 blob and
        /// write to ROM at (PaletteAddress, PaletteIndex). Returns
        /// the destination offset (or U.NOT_FOUND when no-op).
        /// </summary>
        public uint Write()
        {
            var rom = CoreState.ROM;
            if (rom == null) return U.NOT_FOUND;
            if (PaletteAddress == 0) return U.NOT_FOUND;

            var colors = GetSlots();
            PaletteCore.WritePalette(rom, PaletteAddress, PaletteIndex, colors);
            uint offset = U.toOffset(PaletteAddress) + (uint)(PaletteIndex * PaletteCore.PALETTE_BLOCK_SIZE);
            return offset;
        }

        /// <summary>Return the current 16-tuple slot array (helper for tests + Write).</summary>
        public (byte r, byte g, byte b)[] GetSlots()
        {
            return new (byte, byte, byte)[]
            {
                (_r1,  _g1,  _b1),  (_r2,  _g2,  _b2),
                (_r3,  _g3,  _b3),  (_r4,  _g4,  _b4),
                (_r5,  _g5,  _b5),  (_r6,  _g6,  _b6),
                (_r7,  _g7,  _b7),  (_r8,  _g8,  _b8),
                (_r9,  _g9,  _b9),  (_r10, _g10, _b10),
                (_r11, _g11, _b11), (_r12, _g12, _b12),
                (_r13, _g13, _b13), (_r14, _g14, _b14),
                (_r15, _g15, _b15), (_r16, _g16, _b16),
            };
        }

        /// <summary>Set slot N's R/G/B (N is 0-based, 0..15).</summary>
        public void SetSlot(int n, byte r, byte g, byte b)
        {
            switch (n)
            {
                case 0:  R1  = r; G1  = g; B1  = b; break;
                case 1:  R2  = r; G2  = g; B2  = b; break;
                case 2:  R3  = r; G3  = g; B3  = b; break;
                case 3:  R4  = r; G4  = g; B4  = b; break;
                case 4:  R5  = r; G5  = g; B5  = b; break;
                case 5:  R6  = r; G6  = g; B6  = b; break;
                case 6:  R7  = r; G7  = g; B7  = b; break;
                case 7:  R8  = r; G8  = g; B8  = b; break;
                case 8:  R9  = r; G9  = g; B9  = b; break;
                case 9:  R10 = r; G10 = g; B10 = b; break;
                case 10: R11 = r; G11 = g; B11 = b; break;
                case 11: R12 = r; G12 = g; B12 = b; break;
                case 12: R13 = r; G13 = g; B13 = b; break;
                case 13: R14 = r; G14 = g; B14 = b; break;
                case 14: R15 = r; G15 = g; B15 = b; break;
                case 15: R16 = r; G16 = g; B16 = b; break;
            }
        }

        static IReadOnlyList<string> BuildIndexNames(int count, string[]? overrides)
        {
            var list = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                if (overrides != null && i < overrides.Length && !string.IsNullOrEmpty(overrides[i]))
                    list.Add(overrides[i]);
                else
                    list.Add($"Palette No {i}");
            }
            return list;
        }

    }
}
