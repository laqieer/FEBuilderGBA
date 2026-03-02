using System;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for EAUtilLynDumpMode (batch 13 Core migration).
    /// Pure binary parser — no ROM or shared state needed.
    /// </summary>
    public class EAUtilLynDumpModeTests
    {
        [Fact]
        public void Constructor_InitializesEmpty()
        {
            var lyn = new EAUtilLynDumpMode();
            Assert.Equal(0, lyn.GetCount());
            Assert.Empty(lyn.GetDataAll());
        }

        [Fact]
        public void ParseLine_EmptyLine_ReturnsFalse()
        {
            var lyn = new EAUtilLynDumpMode();
            bool result = lyn.ParseLine("");
            Assert.False(result);
        }

        [Fact]
        public void ParseLine_SHORT_AppendsLittleEndian16()
        {
            var lyn = new EAUtilLynDumpMode();
            Assert.True(lyn.ParseLine("SHORT 0x1234 0xABCD"));

            byte[] data = lyn.GetDataAll();
            Assert.Equal(4, data.Length);
            // Little-endian: 0x1234 → 0x34, 0x12
            Assert.Equal(0x34, data[0]);
            Assert.Equal(0x12, data[1]);
            // Little-endian: 0xABCD → 0xCD, 0xAB
            Assert.Equal(0xCD, data[2]);
            Assert.Equal(0xAB, data[3]);
        }

        [Fact]
        public void ParseLine_BYTE_AppendsSingleBytes()
        {
            var lyn = new EAUtilLynDumpMode();
            Assert.True(lyn.ParseLine("BYTE 0x01 0xFF 0x42"));

            byte[] data = lyn.GetDataAll();
            Assert.Equal(3, data.Length);
            Assert.Equal(0x01, data[0]);
            Assert.Equal(0xFF, data[1]);
            Assert.Equal(0x42, data[2]);
        }

        [Fact]
        public void ParseLine_WORD_AppendsLittleEndian32()
        {
            var lyn = new EAUtilLynDumpMode();
            Assert.True(lyn.ParseLine("WORD 0x12345678"));

            byte[] data = lyn.GetDataAll();
            Assert.Equal(4, data.Length);
            Assert.Equal(0x78, data[0]);
            Assert.Equal(0x56, data[1]);
            Assert.Equal(0x34, data[2]);
            Assert.Equal(0x12, data[3]);
        }

        [Fact]
        public void ParseLine_POIN_AppendsFourZeroBytes()
        {
            var lyn = new EAUtilLynDumpMode();
            Assert.True(lyn.ParseLine("POIN SomeLabel"));

            byte[] data = lyn.GetDataAll();
            Assert.Equal(4, data.Length);
            Assert.Equal(0, data[0]);
            Assert.Equal(0, data[1]);
            Assert.Equal(0, data[2]);
            Assert.Equal(0, data[3]);
        }

        [Fact]
        public void ParseLine_UnknownInstruction_DoesNothingButReturnsTrue()
        {
            var lyn = new EAUtilLynDumpMode();
            Assert.True(lyn.ParseLine("UNKNOWN 0x01"));
            Assert.Empty(lyn.GetDataAll());
        }

        [Fact]
        public void ParseORG_CreatesListEntry_EvenStart()
        {
            var lyn = new EAUtilLynDumpMode();
            // ORG CURRENTOFFSET+0x100 ;MyLabel:
            // Since LastORG is 0 and start (0x100) > 1, should add NONAME first
            lyn.ParseLine("ORG CURRENTOFFSET+0x100 ;MyLabel:");
            Assert.Equal(2, lyn.GetCount());
            Assert.Equal("NONAME", lyn.GetName(0));
            Assert.Equal("MyLabel", lyn.GetName(1));
        }

        [Fact]
        public void ParseORG_OddStart_AdjustsToEven()
        {
            var lyn = new EAUtilLynDumpMode();
            // 0x101 is odd, so Start should be 0x100 but StartLow stays 0x101
            lyn.ParseLine("ORG CURRENTOFFSET+0x101 ;OddLabel:");

            // First entry is NONAME (since LastORG=0 and start>1)
            Assert.Equal(2, lyn.GetCount());
            Assert.Equal("OddLabel", lyn.GetName(1));
        }

        [Fact]
        public void ParseORG_StartIs1_NoNONAME()
        {
            var lyn = new EAUtilLynDumpMode();
            // start=1, LastORG=0 → start=1, NOT > 1, so no NONAME
            lyn.ParseLine("ORG CURRENTOFFSET+0x1 ;FirstLabel:");
            Assert.Equal(1, lyn.GetCount());
            Assert.Equal("FirstLabel", lyn.GetName(0));
        }

        [Fact]
        public void ParseORG_Accumulates_LastORG()
        {
            var lyn = new EAUtilLynDumpMode();
            // First ORG: LastORG=0, offset=0x100 → start=0x100
            lyn.ParseLine("ORG CURRENTOFFSET+0x100 ;First:");
            // Second ORG: LastORG=0x100, offset=0x50 → start=0x150
            lyn.ParseLine("ORG CURRENTOFFSET+0x50 ;Second:");

            Assert.Equal(3, lyn.GetCount()); // NONAME + First + Second
            Assert.Equal("NONAME", lyn.GetName(0));
            Assert.Equal("First", lyn.GetName(1));
            Assert.Equal("Second", lyn.GetName(2));
        }

        [Fact]
        public void GetData_ReturnsSliceForIndex()
        {
            var lyn = new EAUtilLynDumpMode();
            // Fill binary data first, then add ORG entries
            // Sequence: BYTE data, ORG at 4, more data
            lyn.ParseLine("BYTE 0x01 0x02 0x03 0x04");
            lyn.ParseLine("ORG CURRENTOFFSET+0x4 ;SecondBlock:");
            lyn.ParseLine("BYTE 0xAA 0xBB");

            // Should have: NONAME at 0, SecondBlock at 4
            Assert.Equal(2, lyn.GetCount());

            byte[] first = lyn.GetData(0);
            Assert.Equal(4, first.Length);
            Assert.Equal(0x01, first[0]);

            byte[] second = lyn.GetData(1);
            Assert.Equal(2, second.Length);
            Assert.Equal(0xAA, second[0]);
            Assert.Equal(0xBB, second[1]);
        }

        [Fact]
        public void GetDataAll_ReturnsFullBinary()
        {
            var lyn = new EAUtilLynDumpMode();
            lyn.ParseLine("SHORT 0x1234");
            lyn.ParseLine("BYTE 0xFF");
            byte[] all = lyn.GetDataAll();
            Assert.Equal(3, all.Length);
        }

        [Fact]
        public void FullSequence_MultipleInstructions()
        {
            var lyn = new EAUtilLynDumpMode();
            // Simulate a realistic lyn.event file
            Assert.True(lyn.ParseLine("SHORT 0x4770"));
            Assert.True(lyn.ParseLine("ORG CURRENTOFFSET+0x2 ;MyFunction:"));
            Assert.True(lyn.ParseLine("SHORT 0xB500 0x4801"));
            Assert.True(lyn.ParseLine("WORD 0x08001234"));
            Assert.True(lyn.ParseLine("POIN SomeRef"));
            // End
            Assert.False(lyn.ParseLine(""));

            // Check list entries
            Assert.Equal(2, lyn.GetCount());
            Assert.Equal("NONAME", lyn.GetName(0));
            Assert.Equal("MyFunction", lyn.GetName(1));

            // Check total binary size: 2 (SHORT) + 4 (SHORT×2) + 4 (WORD) + 4 (POIN) = 14
            Assert.Equal(14, lyn.GetDataAll().Length);
        }

        [Fact]
        public void ParseLine_MultipleWORDs()
        {
            var lyn = new EAUtilLynDumpMode();
            Assert.True(lyn.ParseLine("WORD 0x00000001 0x00000002"));

            byte[] data = lyn.GetDataAll();
            Assert.Equal(8, data.Length);
            // First WORD: 0x00000001 → 01 00 00 00
            Assert.Equal(0x01, data[0]);
            Assert.Equal(0x00, data[1]);
            Assert.Equal(0x00, data[2]);
            Assert.Equal(0x00, data[3]);
            // Second WORD: 0x00000002 → 02 00 00 00
            Assert.Equal(0x02, data[4]);
        }
    }
}
