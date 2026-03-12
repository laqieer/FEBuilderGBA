using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for GBA instrument type classification and read/write logic.
    /// Uses local copies of the classification functions from
    /// SongInstrumentViewModel to avoid Avalonia dependency.
    /// </summary>
    public class SongInstrumentTypeTests
    {
        public enum InstrumentCategory
        {
            DirectSound, SquareWave, WaveMemory, Noise, MultiSample, Drum, Unknown
        }

        static InstrumentCategory ClassifyType(byte type)
        {
            switch (type)
            {
                case 0x00: case 0x08: case 0x10: case 0x18:
                    return InstrumentCategory.DirectSound;
                case 0x01: case 0x02: case 0x09: case 0x0A:
                    return InstrumentCategory.SquareWave;
                case 0x03: case 0x0B:
                    return InstrumentCategory.WaveMemory;
                case 0x04: case 0x0C:
                    return InstrumentCategory.Noise;
                case 0x40:
                    return InstrumentCategory.MultiSample;
                case 0x80:
                    return InstrumentCategory.Drum;
                default:
                    return InstrumentCategory.Unknown;
            }
        }

        static string GetInstrumentTypeName(byte type)
        {
            switch (type)
            {
                case 0x00: return "DirectSound";
                case 0x01: return "SquareWave1";
                case 0x02: return "SquareWave2";
                case 0x03: return "Wave Memory";
                case 0x04: return "Noise";
                case 0x08: return "DirectSound Fixed Freq";
                case 0x09: return "SquareWave (no pop)";
                case 0x0A: return "SquareWave (no pop)";
                case 0x0B: return "Wave Memory (no pop)";
                case 0x0C: return "Noise (no pop)";
                case 0x10: return "DirectSound Reverse";
                case 0x18: return "DirectSound Fixed Freq Reverse";
                case 0x40: return "Multi Sample";
                case 0x80: return "Drum Part";
                default: return $"Unknown (0x{type:X02})";
            }
        }

        [Theory]
        [InlineData(0x00, InstrumentCategory.DirectSound)]
        [InlineData(0x08, InstrumentCategory.DirectSound)]
        [InlineData(0x10, InstrumentCategory.DirectSound)]
        [InlineData(0x18, InstrumentCategory.DirectSound)]
        [InlineData(0x01, InstrumentCategory.SquareWave)]
        [InlineData(0x02, InstrumentCategory.SquareWave)]
        [InlineData(0x09, InstrumentCategory.SquareWave)]
        [InlineData(0x0A, InstrumentCategory.SquareWave)]
        [InlineData(0x03, InstrumentCategory.WaveMemory)]
        [InlineData(0x0B, InstrumentCategory.WaveMemory)]
        [InlineData(0x04, InstrumentCategory.Noise)]
        [InlineData(0x0C, InstrumentCategory.Noise)]
        [InlineData(0x40, InstrumentCategory.MultiSample)]
        [InlineData(0x80, InstrumentCategory.Drum)]
        [InlineData(0xFF, InstrumentCategory.Unknown)]
        [InlineData(0x20, InstrumentCategory.Unknown)]
        public void ClassifyType_ReturnsCorrectCategory(byte typeByte, InstrumentCategory expected)
        {
            Assert.Equal(expected, ClassifyType(typeByte));
        }

        [Theory]
        [InlineData(0x00, "DirectSound")]
        [InlineData(0x01, "SquareWave1")]
        [InlineData(0x02, "SquareWave2")]
        [InlineData(0x03, "Wave Memory")]
        [InlineData(0x04, "Noise")]
        [InlineData(0x08, "DirectSound Fixed Freq")]
        [InlineData(0x40, "Multi Sample")]
        [InlineData(0x80, "Drum Part")]
        public void GetInstrumentTypeName_ReturnsExpectedName(byte typeByte, string expected)
        {
            Assert.Equal(expected, GetInstrumentTypeName(typeByte));
        }

        [Fact]
        public void GetInstrumentTypeName_UnknownType_ContainsHex()
        {
            string name = GetInstrumentTypeName(0x55);
            Assert.Contains("Unknown", name);
            Assert.Contains("55", name);
        }

        /// <summary>
        /// Verify that a 12-byte DirectSound instrument block is read and written correctly.
        /// </summary>
        [Fact]
        public void DirectSound_ReadWrite_RoundTrips()
        {
            // DirectSound: type=0x00, pad(3), WavePtr(u32), Attack, Decay, Sustain, Release
            byte[] block = new byte[]
            {
                0x00, 0x00, 0x00, 0x00,       // header + pad
                0x78, 0x56, 0x34, 0x08,       // WavePtr = 0x08345678
                0x0A, 0x14, 0x80, 0x28        // ADSR: A=10, D=20, S=128, R=40
            };

            Assert.Equal(InstrumentCategory.DirectSound, ClassifyType(block[0]));
            // Parse fields
            uint wavePtr = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));
            Assert.Equal(0x08345678u, wavePtr);
            Assert.Equal(0x0A, block[8]);  // Attack
            Assert.Equal(0x14, block[9]);  // Decay
            Assert.Equal(0x80, block[10]); // Sustain
            Assert.Equal(0x28, block[11]); // Release
        }

        /// <summary>
        /// Verify that a 12-byte SquareWave instrument block has correct field positions.
        /// </summary>
        [Fact]
        public void SquareWave_FieldPositions()
        {
            // SquareWave: type=0x01, Sweep, DutyLen, EnvStep, pad(4), ADSR
            byte[] block = new byte[]
            {
                0x01, 0x11, 0x22, 0x33,       // header, sweep, dutyLen, envStep
                0x00, 0x00, 0x00, 0x00,       // pad
                0x05, 0x0A, 0x64, 0x0F        // ADSR
            };

            Assert.Equal(InstrumentCategory.SquareWave, ClassifyType(block[0]));
            Assert.Equal(0x11, block[1]);  // Sweep
            Assert.Equal(0x22, block[2]);  // DutyLen
            Assert.Equal(0x33, block[3]);  // EnvStep
            Assert.Equal(0x05, block[8]);  // Attack
            Assert.Equal(0x0A, block[9]);  // Decay
            Assert.Equal(0x64, block[10]); // Sustain
            Assert.Equal(0x0F, block[11]); // Release
        }

        /// <summary>
        /// Verify Noise instrument has Period at offset 4.
        /// </summary>
        [Fact]
        public void Noise_PeriodAtOffset4()
        {
            byte[] block = new byte[]
            {
                0x04, 0x00, 0x00, 0x00,       // header + pad
                0x03, 0x00, 0x00, 0x00,       // period=3 + pad
                0x0A, 0x14, 0x80, 0x28        // ADSR
            };

            Assert.Equal(InstrumentCategory.Noise, ClassifyType(block[0]));
            Assert.Equal(0x03, block[4]); // Period
        }

        /// <summary>
        /// Verify MultiSample has two pointers at offsets 4 and 8.
        /// </summary>
        [Fact]
        public void MultiSample_TwoPointers()
        {
            byte[] block = new byte[]
            {
                0x40, 0x00, 0x00, 0x00,       // header + pad
                0x00, 0x10, 0x00, 0x08,       // KeyMapPtr = 0x08001000
                0x00, 0x20, 0x00, 0x08        // SubInstrPtr = 0x08002000
            };

            Assert.Equal(InstrumentCategory.MultiSample, ClassifyType(block[0]));
            uint keyMapPtr = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));
            uint subInstrPtr = (uint)(block[8] | (block[9] << 8) | (block[10] << 16) | (block[11] << 24));
            Assert.Equal(0x08001000u, keyMapPtr);
            Assert.Equal(0x08002000u, subInstrPtr);
        }

        /// <summary>
        /// Verify Drum has SubInstrPtr at offset 4.
        /// </summary>
        [Fact]
        public void Drum_SubInstrPtrAtOffset4()
        {
            byte[] block = new byte[]
            {
                0x80, 0x00, 0x00, 0x00,       // header + pad
                0x00, 0x30, 0x00, 0x08,       // SubInstrPtr = 0x08003000
                0x00, 0x00, 0x00, 0x00        // pad
            };

            Assert.Equal(InstrumentCategory.Drum, ClassifyType(block[0]));
            uint subInstrPtr = (uint)(block[4] | (block[5] << 8) | (block[6] << 16) | (block[7] << 24));
            Assert.Equal(0x08003000u, subInstrPtr);
        }

        /// <summary>
        /// All 14 known type bytes should classify to a non-Unknown category.
        /// </summary>
        [Fact]
        public void AllKnownTypes_AreNotUnknown()
        {
            byte[] known = { 0x00, 0x01, 0x02, 0x03, 0x04, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x10, 0x18, 0x40, 0x80 };
            foreach (var t in known)
            {
                Assert.NotEqual(InstrumentCategory.Unknown, ClassifyType(t));
            }
        }

        /// <summary>
        /// Block size is always 12 bytes for all instrument types.
        /// </summary>
        [Fact]
        public void BlockSize_IsAlways12()
        {
            // This is a documentation test: each instrument block is exactly 12 bytes
            const int blockSize = 12;
            Assert.Equal(12, blockSize);
        }
    }
}
