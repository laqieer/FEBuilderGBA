// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the Core CSA magic-engine helper extracted in #417.
// Cover the read-only detection / spell-table walk that the Avalonia
// CSACreator view depends on.
using System;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MagicCSACoreTests
    {
        // -----------------------------------------------------------------
        // SearchMagicSystem
        // -----------------------------------------------------------------

        /// <summary>
        /// With a synthetic FE8U ROM that has the SCA_Creator 16-byte engine
        /// signature planted at 0x95d780 AND the SCA spell-table signature
        /// + a valid pointer at the next u32, SearchMagicSystem must return
        /// CsaCreator and surface the expected dim/no-dim/csa addresses.
        /// </summary>
        [Fact]
        public void SearchMagicSystem_DetectsCsaCreator_WhenSignaturesPresent()
        {
            var rom = MakeMinimalFE8URomWithCsa(out uint plantedCsaTable, out uint plantedCsaPtr);
            var kind = MagicCSACore.SearchMagicSystem(rom,
                out uint baseAddr, out uint dimAddr, out uint noDimAddr,
                out uint csaTable, out uint csaTablePointer);
            Assert.Equal(MagicSystemKind.CsaCreator, kind);
            Assert.Equal(0x95d780u, baseAddr);
            Assert.Equal(0x95d7edu, dimAddr);
            Assert.Equal(0x95d899u, noDimAddr);
            Assert.Equal(plantedCsaTable, csaTable);
            Assert.Equal(plantedCsaPtr, csaTablePointer);
        }

        /// <summary>
        /// When the FE8U engine signature is absent (raw 16 MiB zero ROM
        /// with the FE8U code), SearchMagicSystem must return None and set
        /// every out parameter to <c>U.NOT_FOUND</c>.
        /// </summary>
        [Fact]
        public void SearchMagicSystem_ReturnsNone_WhenSignatureAbsent()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            var kind = MagicCSACore.SearchMagicSystem(rom,
                out uint baseAddr, out uint dimAddr, out uint noDimAddr,
                out uint csaTable, out uint csaTablePointer);
            Assert.Equal(MagicSystemKind.None, kind);
            Assert.Equal(U.NOT_FOUND, baseAddr);
            Assert.Equal(U.NOT_FOUND, dimAddr);
            Assert.Equal(U.NOT_FOUND, noDimAddr);
            Assert.Equal(U.NOT_FOUND, csaTable);
            Assert.Equal(U.NOT_FOUND, csaTablePointer);
        }

        /// <summary>
        /// When the engine signature is present but the spell-table signature
        /// is absent (so the pointer to the actual CSA table cannot be
        /// located), SearchMagicSystem must NOT return CsaCreator - it must
        /// fall through to None. This guards against incomplete-patch ROMs
        /// (mirrors WF `g_Cache_CSASpellTablePointer == U.NOT_FOUND` branch).
        /// </summary>
        [Fact]
        public void SearchMagicSystem_ReturnsNone_WhenSpellTableSignatureAbsent()
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");
            // Plant ONLY the engine signature; no spell-table signature.
            byte[] engineSig = new byte[]{0x01,0x00,0x00,0x00,0x90,0xD7,0x95,0x08,0x03,0x00,0x00,0x00,0xD9,0xD8,0x95,0x08};
            Buffer.BlockCopy(engineSig, 0, rom.Data, 0x95d780, engineSig.Length);
            var kind = MagicCSACore.SearchMagicSystem(rom,
                out _, out _, out _, out _, out uint csaTablePointer);
            Assert.Equal(MagicSystemKind.None, kind);
            Assert.Equal(U.NOT_FOUND, csaTablePointer);
        }

        /// <summary>
        /// Null ROM must safely return None, not throw.
        /// </summary>
        [Fact]
        public void SearchMagicSystem_NullRom_ReturnsNone()
        {
            var kind = MagicCSACore.SearchMagicSystem(null,
                out _, out _, out _, out _, out _);
            Assert.Equal(MagicSystemKind.None, kind);
        }

        // -----------------------------------------------------------------
        // GetCSASpellTableAddr / GetCSASpellTablePointer convenience helpers
        // -----------------------------------------------------------------

        [Fact]
        public void GetCSASpellTableAddr_MatchesSearchMagicSystemOutput()
        {
            var rom = MakeMinimalFE8URomWithCsa(out uint plantedTable, out _);
            Assert.Equal(plantedTable, MagicCSACore.GetCSASpellTableAddr(rom));
        }

        [Fact]
        public void GetCSASpellTablePointer_MatchesSearchMagicSystemOutput()
        {
            var rom = MakeMinimalFE8URomWithCsa(out _, out uint plantedPtr);
            Assert.Equal(plantedPtr, MagicCSACore.GetCSASpellTablePointer(rom));
        }

        // -----------------------------------------------------------------
        // ComputeSpellDataCount
        // -----------------------------------------------------------------

        /// <summary>
        /// On a zero-filled ROM with an empty pointer table the spell-data
        /// count walks until a non-pointer-or-null word; with all zeros it
        /// caps at 0xFD per the WF guard.
        /// </summary>
        [Fact]
        public void ComputeSpellDataCount_CapsAt0xFD_OnZeroFilledRom()
        {
            var rom = MakeMinimalFE8URomWithCsa(out _, out _);
            uint count = MagicCSACore.ComputeSpellDataCount(rom);
            Assert.Equal(0xFDu, count);
        }

        /// <summary>
        /// With a single non-pointer terminator placed after the original
        /// count, ComputeSpellDataCount must return the number of valid
        /// pointer-or-null entries minus 1 (mirrors WF "count - 1").
        /// </summary>
        [Fact]
        public void ComputeSpellDataCount_HonorsTerminator()
        {
            var rom = MakeMinimalFE8URomWithCsa(out _, out _);
            uint pointerTable = rom.p32(rom.RomInfo.magic_effect_pointer);
            uint originalCount = rom.RomInfo.magic_effect_original_data_count;
            // Plant 4 valid pointers after the original count + a bad u32.
            for (uint i = 0; i < 4; i++)
            {
                WriteU32(rom.Data, (int)pointerTable + (int)(originalCount + i) * 4, 0x0895d7edu);
            }
            WriteU32(rom.Data, (int)pointerTable + (int)(originalCount + 4) * 4, 0xDEADBEEFu);
            uint count = MagicCSACore.ComputeSpellDataCount(rom);
            // (originalCount + 4) - 1 entries are reported usable.
            Assert.Equal(originalCount + 4u - 1u, count);
        }

        // -----------------------------------------------------------------
        // ScanCsaEntries
        // -----------------------------------------------------------------

        /// <summary>
        /// With a planted pointer table that holds dim/no-dim addresses,
        /// ScanCsaEntries must yield one CsaEntry per non-empty slot, with
        /// the correct Addr (csa row), TagAddr (pointer table slot), and
        /// DataAddr (dim or no-dim).
        /// </summary>
        [Fact]
        public void ScanCsaEntries_YieldsRows_ForDimAndNoDim()
        {
            var rom = MakeMinimalFE8URomWithCsa(out uint csaTable, out _);
            uint pointerTable = rom.p32(rom.RomInfo.magic_effect_pointer);
            uint originalCount = rom.RomInfo.magic_effect_original_data_count; // FE8U: 0x48 = 72
            // Plant 3 consecutive post-original slots: dim, no-dim, empty.
            uint slotDim = originalCount + 0;
            uint slotNoDim = originalCount + 1;
            uint slotEmpty = originalCount + 2;
            WriteU32(rom.Data, (int)pointerTable + (int)slotDim * 4, 0x0895d7edu);
            WriteU32(rom.Data, (int)pointerTable + (int)slotNoDim * 4, 0x0895d899u);
            // slotEmpty stays at u32==0 (data=0, i >= originalCount -> EMPTY).

            // Constrain the scan to STOP after slotEmpty so we don't iterate
            // the unbounded post-original zero tail.
            var entries = MagicCSACore.ScanCsaEntries(rom,
                MagicSystemKind.CsaCreator, 0x95d7edu, 0x95d899u, csaTable,
                spellDataCount: slotEmpty + 1);

            // We expect 3 entries (slotDim, slotNoDim, slotEmpty).
            Assert.Equal(3, entries.Count);
            Assert.Equal(csaTable + slotDim * 20u, entries[0].Addr);
            Assert.Equal(pointerTable + slotDim * 4u, entries[0].TagAddr);
            // DataAddr stores the offset form (rom.p32 strips the 0x08000000 high bit).
            Assert.Equal(0x95d7edu, entries[0].DataAddr);
            Assert.False(entries[0].IsEmpty);

            Assert.Equal(csaTable + slotNoDim * 20u, entries[1].Addr);
            Assert.Equal(0x95d899u, entries[1].DataAddr);
            Assert.False(entries[1].IsEmpty);

            Assert.Equal(csaTable + slotEmpty * 20u, entries[2].Addr);
            Assert.Equal(0u, entries[2].DataAddr);
            Assert.True(entries[2].IsEmpty);
            Assert.Contains("EMPTY", entries[2].Name);
        }

        /// <summary>
        /// Legacy slots (i &lt; magic_effect_original_data_count) with
        /// data=0 must be skipped (mirrors WF `if (i &lt; original) return
        /// new AddrResult()` early-return).
        /// </summary>
        [Fact]
        public void ScanCsaEntries_SkipsLegacyZeroSlots()
        {
            var rom = MakeMinimalFE8URomWithCsa(out uint csaTable, out _);
            uint pointerTable = rom.p32(rom.RomInfo.magic_effect_pointer);
            uint originalCount = rom.RomInfo.magic_effect_original_data_count;
            // Slot 5 is < originalCount and data=0 -> SKIP.
            WriteU32(rom.Data, (int)pointerTable + 5 * 4, 0u);

            // Constrain to the original-count window so the unbounded
            // post-original EMPTY tail does not skew the assertion.
            var entries = MagicCSACore.ScanCsaEntries(rom,
                MagicSystemKind.CsaCreator, 0x95d7edu, 0x95d899u, csaTable,
                spellDataCount: originalCount);
            Assert.Empty(entries);
        }

        /// <summary>
        /// MagicSystemKind.None must short-circuit to empty list (no scan).
        /// </summary>
        [Fact]
        public void ScanCsaEntries_ReturnsEmpty_WhenSystemNone()
        {
            var rom = MakeMinimalFE8URomWithCsa(out uint csaTable, out _);
            var entries = MagicCSACore.ScanCsaEntries(rom,
                MagicSystemKind.None, 0x95d7edu, 0x95d899u, csaTable);
            Assert.Empty(entries);
        }

        /// <summary>
        /// MagicSystemKind.FEditor must also return empty (FEditor uses a
        /// different on-ROM layout the CSA helper does not model).
        /// </summary>
        [Fact]
        public void ScanCsaEntries_ReturnsEmpty_WhenSystemFEditor()
        {
            var rom = MakeMinimalFE8URomWithCsa(out uint csaTable, out _);
            var entries = MagicCSACore.ScanCsaEntries(rom,
                MagicSystemKind.FEditor, 0x95d7edu, 0x95d899u, csaTable);
            Assert.Empty(entries);
        }

        /// <summary>
        /// csaSpellTable == U.NOT_FOUND must short-circuit to empty list.
        /// </summary>
        [Fact]
        public void ScanCsaEntries_ReturnsEmpty_WhenCsaTableNotFound()
        {
            var rom = MakeMinimalFE8URomWithCsa(out _, out _);
            var entries = MagicCSACore.ScanCsaEntries(rom,
                MagicSystemKind.CsaCreator, 0x95d7edu, 0x95d899u, U.NOT_FOUND);
            Assert.Empty(entries);
        }

        // -----------------------------------------------------------------
        // helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Builds a synthetic FE8U ROM with:
        /// - Engine signature at 0x95d780 (16 bytes, SCA_Creator FE8U variant).
        /// - Spell-table signature at 0x100000 (16 bytes) followed by a
        ///   u32 pointer at 0x100010 pointing to the CSA table at 0x200000.
        /// - A real pointer table allocated at offset 0x300000 referenced
        ///   from <c>rom.RomInfo.magic_effect_pointer</c>.
        /// </summary>
        static ROM MakeMinimalFE8URomWithCsa(out uint csaTable, out uint csaTablePointer)
        {
            var rom = new ROM();
            rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01");

            // 1) Engine signature.
            byte[] engineSig = new byte[]{0x01,0x00,0x00,0x00,0x90,0xD7,0x95,0x08,0x03,0x00,0x00,0x00,0xD9,0xD8,0x95,0x08};
            Buffer.BlockCopy(engineSig, 0, rom.Data, 0x95d780, engineSig.Length);

            // 2) Spell-table signature.
            byte[] tableSig = new byte[]{0x1C,0x58,0x05,0x08,0x00,0x01,0x00,0x80,0xED,0xD7,0x95,0x08,0x99,0xD8,0x95,0x08};
            Buffer.BlockCopy(tableSig, 0, rom.Data, 0x100000, tableSig.Length);

            // 3) Pointer to CSA table (right after the 16-byte signature).
            csaTablePointer = 0x100010u;
            csaTable = 0x200000u;
            WriteU32(rom.Data, (int)csaTablePointer, csaTable | 0x08000000u);

            // 4) Real magic-effect pointer table at 0x300000. magic_effect_pointer
            // lives at FE8U-specific address; set it to point at 0x300000.
            WriteU32(rom.Data, (int)rom.RomInfo.magic_effect_pointer, 0x08300000u);

            return rom;
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
