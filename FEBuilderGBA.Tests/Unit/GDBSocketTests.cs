using System;
using System.Text;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests for GDBSocket (Core migration batch 12).
    /// Uses GDBSocketTestHelper since GDBSocket is internal in Core.
    /// </summary>
    public class GDBSocketTests
    {
        [Fact]
        public void MakePacket_SingleChar_ProducesCorrectGDBFormat()
        {
            // From the inline TEST_GDB_CHECKSUM: "g" -> "$g#67"
            byte[] packet = GDBSocketTestHelper.MakePacket("g");
            Assert.Equal(5, packet.Length);
            Assert.Equal((byte)'$', packet[0]);
            Assert.Equal((byte)'g', packet[1]);
            Assert.Equal((byte)'#', packet[2]);
            Assert.Equal((byte)'6', packet[3]);
            Assert.Equal((byte)'7', packet[4]);
        }

        [Fact]
        public void MakePacket_EmptyString_ProducesValidPacket()
        {
            byte[] packet = GDBSocketTestHelper.MakePacket("");
            // "$#00" — empty payload, checksum 0
            Assert.Equal(4, packet.Length);
            Assert.Equal((byte)'$', packet[0]);
            Assert.Equal((byte)'#', packet[1]);
            Assert.Equal((byte)'0', packet[2]);
            Assert.Equal((byte)'0', packet[3]);
        }

        [Fact]
        public void MakePacket_MultiChar_HasCorrectStructure()
        {
            byte[] packet = GDBSocketTestHelper.MakePacket("qSupported");
            // Structure: $qSupported#xx
            Assert.Equal((byte)'$', packet[0]);
            string payload = Encoding.ASCII.GetString(packet, 1, "qSupported".Length);
            Assert.Equal("qSupported", payload);
            Assert.Equal((byte)'#', packet[1 + "qSupported".Length]);
            Assert.Equal(1 + "qSupported".Length + 1 + 2, packet.Length);
        }

        [Fact]
        public void UnPacket_RoundTripsWithMakePacket()
        {
            string original = "OK";
            byte[] packet = GDBSocketTestHelper.MakePacket(original);
            string result = GDBSocketTestHelper.UnPacket(packet, packet.Length);
            Assert.Equal(original, result);
        }

        [Fact]
        public void UnPacket_WithAckPrefix_RoundTrips()
        {
            string original = "OK";
            byte[] innerPacket = GDBSocketTestHelper.MakePacket(original);
            // Prepend '+' ack byte
            byte[] packet = new byte[1 + innerPacket.Length];
            packet[0] = (byte)'+';
            Array.Copy(innerPacket, 0, packet, 1, innerPacket.Length);
            string result = GDBSocketTestHelper.UnPacket(packet, packet.Length);
            Assert.Equal(original, result);
        }

        [Fact]
        public void UnPacket_TooShort_ReturnsEmpty()
        {
            byte[] packet = new byte[] { (byte)'$', (byte)'#', (byte)'0', (byte)'0' };
            string result = GDBSocketTestHelper.UnPacket(packet, packet.Length);
            Assert.Equal("", result);
        }

        [Fact]
        public void MakePacket_ChecksumIsCorrect()
        {
            // Manually verify checksum for "g" (ASCII 0x67)
            // Sum = 0x67 = 103, high nibble = 6, low nibble = 7
            byte[] packet = GDBSocketTestHelper.MakePacket("g");
            char high = (char)packet[packet.Length - 2];
            char low = (char)packet[packet.Length - 1];
            Assert.Equal('6', high);
            Assert.Equal('7', low);
        }

        [Fact]
        public void Dispose_OnUnconnectedSocket_DoesNotThrow()
        {
            var ex = Record.Exception(() => GDBSocketTestHelper.DisposeUnconnected());
            Assert.Null(ex);
        }

        [Fact]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var ex = Record.Exception(() => GDBSocketTestHelper.DisposeDouble());
            Assert.Null(ex);
        }

        [Fact]
        public void TEST_GDB_CHECKSUM_DoesNotThrow()
        {
            var ex = Record.Exception(() => GDBSocketTestHelper.RunInlineTest());
            Assert.Null(ex);
        }
    }
}
