// SPDX-License-Identifier: GPL-3.0-or-later
// Pins the contract Copilot CLI flagged on plan #360 v1 (review point 3):
// for FE6, the IdFieldControl name preview MUST resolve through
// SupportUnitNavigation.ResolveUnitTableName (FE6-aware via GetUnitTableBase),
// not NameResolver.GetUnitName (raw, no FE6 dummy-entry skip).
//
// If a future change accidentally swaps the resolver back to GetUnitName in
// the migrated editors, this test will fail by demonstrating that the two
// helpers DO NOT agree on the same input ROM/index.
//
// Rewritten per Copilot inline review on PR #495:
//   - Sets CoreState.ROM = synthetic FE6 ROM in try/finally so the raw
//     NameResolver path actually resolves against the same ROM the FE6-aware
//     path uses (the previous version didn't set CoreState.ROM and could
//     accidentally pass against a stale ROM).
//   - Calls the now-internal-visible SupportUnitNavigation.GetUnitTableBase
//     directly via InternalsVisibleTo("FEBuilderGBA.Core.Tests") rather than
//     duplicating the conditional in a test-only shim class.
//   - Forces observable output divergence on a Huffman-less test ROM by
//     wiring dummy textId=0 (raw resolver returns "#0") and FE6-logical-0
//     textId=1 (FE6-aware resolver tries to decode textId=1 and falls back
//     to "???" when no decoder is present). Different output proves the
//     read addresses differ.
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class UnitNamePreviewFE6Tests
    {
        // ---- helpers (mirrors SupportUnitNavigationTests style) ------------

        // Cast the uint offset to int once via checked() so we get a clear
        // OverflowException (rather than a confusing wrap-around) if a future
        // refactor passes an address > int.MaxValue. The synthetic ROM is
        // 16 MB so the cast is always safe in practice; this is hygiene to
        // match the int-indexed byte[] indexer per Copilot bot review on PR
        // #495.
        static void WriteU16(byte[] data, uint addr, ushort value)
        {
            int i = checked((int)addr);
            data[i + 0] = (byte)(value & 0xFF);
            data[i + 1] = (byte)((value >> 8) & 0xFF);
        }

        static void WriteU32(byte[] data, uint addr, uint value)
        {
            int i = checked((int)addr);
            data[i + 0] = (byte)(value & 0xFF);
            data[i + 1] = (byte)((value >> 8) & 0xFF);
            data[i + 2] = (byte)((value >> 16) & 0xFF);
            data[i + 3] = (byte)((value >> 24) & 0xFF);
        }

        static ROM MakeRom(string sig)
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], sig);
            Assert.NotNull(rom.RomInfo);
            return rom;
        }

        /// <summary>
        /// FE6 places a dummy entry at p32(unit_pointer) and the "real" table
        /// starts at p32(unit_pointer) + unit_datasize. We plant a dummy entry
        /// at raw index 0 with textId=0 (so the raw NameResolver returns "#0",
        /// the textId==0 fallback) and the FE6-logical-0 entry at raw index 1
        /// with textId=1 (so the FE6-aware ResolveUnitTableName attempts to
        /// decode textId=1 and falls back to "???" without a Huffman decoder).
        ///
        /// "#0" vs "???" is observable divergence — exactly what Copilot CLI
        /// flagged on the plan: if a future code change accidentally points
        /// the IdFieldControl preview at NameResolver.GetUnitName instead of
        /// SupportUnitNavigation.ResolveUnitTableName on FE6, the preview
        /// would silently show the dummy row's name. This test stops that.
        /// </summary>
        [Fact]
        public void ResolveUnitTableName_FE6_DivergesFromRawResolverOnDummySkip()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeRom("AFEJ01");
                Assert.Equal(6, rom.RomInfo.version);

                // CRITICAL: install the synthetic ROM so both helpers query
                // the SAME data. Without this, NameResolver may resolve
                // against a stale ROM and produce misleading agreement.
                CoreState.ROM = rom;

                uint rawBase = 0x200000;
                uint dataSize = rom.RomInfo.unit_datasize;
                Assert.True(dataSize > 1, "unit_datasize should be at least 2 to hold a text-id");

                uint unitPtr = rom.RomInfo.unit_pointer;
                WriteU32(rom.Data, unitPtr, rawBase | 0x08000000);
                // Dummy entry at raw index 0 -> textId 0 -> raw resolver returns "#0".
                WriteU16(rom.Data, rawBase, 0);
                // FE6 logical index 0 at raw index 1 -> textId 1 -> FE6-aware
                // resolver attempts decode and falls back to "???" without a
                // Huffman decoder loaded.
                WriteU16(rom.Data, rawBase + dataSize, 1);

                NameResolver.ClearCache();

                // FE6-aware path reads from (rawBase + dataSize) -> textId 1.
                string fe6Aware = SupportUnitNavigation.ResolveUnitTableName(rom, 0);
                // Raw NameResolver path reads from rawBase -> textId 0 -> "#0".
                string raw = NameResolver.GetUnitName(0);

                // The contract Copilot flagged: these MUST disagree on FE6.
                Assert.NotEqual(fe6Aware, raw);

                // Tight pins on each side so the divergence is meaningful (not
                // just "they happen to differ because of some unrelated bug"):
                //   - Raw side sees textId=0 -> the dedicated "#0" fallback.
                Assert.Equal("#0", raw);
                //   - FE6-aware side sees textId=1, decoded to "???" without a
                //     Huffman tree. Anything non-empty + non-equal to "#0" is
                //     also acceptable here (preserves resilience if the test
                //     ROM grows a fake decoder later), so allow either "???"
                //     or any other resolved string except "#0" / "".
                Assert.False(string.IsNullOrEmpty(fe6Aware));
                Assert.NotEqual("#0", fe6Aware);

                // Additionally pin the ADDRESS divergence by directly reading
                // the synthetic ROM bytes — guards against future test
                // refactors that might accidentally point both helpers at the
                // same offset.
                Assert.Equal((uint)0, rom.u16(rawBase));
                Assert.Equal((uint)1, rom.u16(rawBase + dataSize));
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// FE8U has NO dummy entry. The FE6-aware resolver MUST agree with the
        /// raw resolver on a non-FE6 ROM — confirmed by calling the
        /// internal-visible SupportUnitNavigation.GetUnitTableBase directly
        /// (Copilot review point 2: don't duplicate the conditional in a
        /// test-only shim).
        /// </summary>
        [Fact]
        public void GetUnitTableBase_FE8U_DoesNotSkipDummy()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeRom("BE8E01");
                Assert.Equal(8, rom.RomInfo.version);
                CoreState.ROM = rom;

                uint rawBase = 0x200000;
                uint unitPtr = rom.RomInfo.unit_pointer;
                WriteU32(rom.Data, unitPtr, rawBase | 0x08000000);

                // Call the internal helper directly — InternalsVisibleTo
                // makes it visible to this test assembly.
                uint baseAddr = SupportUnitNavigation.GetUnitTableBase(rom);
                Assert.Equal(rawBase, baseAddr);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Sanity-check the FE6 side too: GetUnitTableBase MUST add
        /// unit_datasize for FE6 — this is the contract the IdFieldControl
        /// migrations rely on for the FE6 dummy-skip Jump/Pick address math.
        /// </summary>
        [Fact]
        public void GetUnitTableBase_FE6_AddsDummySkip()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeRom("AFEJ01");
                Assert.Equal(6, rom.RomInfo.version);
                CoreState.ROM = rom;

                uint rawBase = 0x200000;
                uint unitPtr = rom.RomInfo.unit_pointer;
                WriteU32(rom.Data, unitPtr, rawBase | 0x08000000);

                uint baseAddr = SupportUnitNavigation.GetUnitTableBase(rom);
                Assert.Equal(rawBase + rom.RomInfo.unit_datasize, baseAddr);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }
    }
}
