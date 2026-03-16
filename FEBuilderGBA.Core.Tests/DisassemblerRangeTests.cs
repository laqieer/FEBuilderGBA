using System;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class DisassemblerRangeTests
    {
        static ROM MakeMinimalRom(int size = 256)
        {
            byte[] data = new byte[size];
            // Fill with NOP (0x46C0) instructions
            for (int i = 0; i < data.Length; i += 2)
            {
                data[i] = 0xC0;
                data[i + 1] = 0x46;
            }
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        [Fact]
        public void DisassembleRange_ThrowsWithNoRom()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var core = new DisassemblerCore();
                Assert.Throws<InvalidOperationException>(() =>
                    core.DisassembleRange(0, 0x10));
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void DisassembleRange_ProducesOutput_WithMinimalRom()
        {
            var origRom = CoreState.ROM;
            var origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.ROM = MakeMinimalRom(256);
                CoreState.BaseDirectory = "";

                var core = new DisassemblerCore();
                var lines = core.DisassembleRange(0, 0x10);

                Assert.NotNull(lines);
                Assert.True(lines.Count > 2, "Should have header + instruction lines");
                Assert.Contains("0x00000000", lines[0]);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.BaseDirectory = origBase;
            }
        }

        [Fact]
        public void DisassembleRange_BeyondRomSize_ReturnsError()
        {
            var origRom = CoreState.ROM;
            var origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.ROM = MakeMinimalRom(16);
                CoreState.BaseDirectory = "";

                var core = new DisassemblerCore();
                var lines = core.DisassembleRange(0x10000, 0x10);

                Assert.NotNull(lines);
                Assert.Single(lines);
                Assert.Contains("beyond ROM size", lines[0]);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.BaseDirectory = origBase;
            }
        }

        [Fact]
        public void DisassembleRange_ClipsToRomEnd()
        {
            var origRom = CoreState.ROM;
            var origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.ROM = MakeMinimalRom(32);
                CoreState.BaseDirectory = "";

                var core = new DisassemblerCore();
                // Request more than available
                var lines = core.DisassembleRange(0, 0x1000);

                Assert.NotNull(lines);
                // Should still produce output, clipped to ROM size
                Assert.True(lines.Count >= 2);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.BaseDirectory = origBase;
            }
        }

        [Fact]
        public void DisassembleRange_MidRomOffset()
        {
            var origRom = CoreState.ROM;
            var origBase = CoreState.BaseDirectory;
            try
            {
                CoreState.ROM = MakeMinimalRom(256);
                CoreState.BaseDirectory = "";

                var core = new DisassemblerCore();
                var lines = core.DisassembleRange(0x80, 0x10);

                Assert.NotNull(lines);
                Assert.True(lines.Count >= 2);
                // Header should mention the offset
                Assert.Contains("0x08000080", lines[0]);
            }
            finally
            {
                CoreState.ROM = origRom;
                CoreState.BaseDirectory = origBase;
            }
        }
    }
}
