using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for the GUI-free clipboard/structural-edit helpers backing the
    /// Avalonia AddressListControl opt-in structural-edit context menu (#1539).
    /// Verifies byte-for-byte WinForms interop of the clipboard wire format.
    /// </summary>
    public class AddressListClipboardCoreTests
    {
        // ---- Serialize (WF CopyToClipbord) -------------------------------

        [Fact]
        public void Serialize_MatchesWinFormsFormat_NoLeadingZero()
        {
            // WF uses ToString("X") — uppercase, NO leading zero (0x0F -> "F").
            byte[] block = { 0x00, 0x0F, 0xAB, 0xFF, 0x10 };
            string text = AddressListClipboardCore.Serialize("AddressList", "SoundRoomForm", block);
            Assert.Equal("AddressList@SoundRoomForm 0 F AB FF 10", text);
        }

        [Fact]
        public void Serialize_EmptyBlock_IsHeaderOnly()
        {
            string text = AddressListClipboardCore.Serialize("AddressList", "SoundRoomForm", new byte[0]);
            Assert.Equal("AddressList@SoundRoomForm", text);
        }

        [Fact]
        public void BuildHeader_JoinsWithAt()
        {
            Assert.Equal("AddressList@SoundRoomFE6Form",
                AddressListClipboardCore.BuildHeader("AddressList", "SoundRoomFE6Form"));
        }

        // ---- Round-trip (Serialize -> TryParse) --------------------------

        [Fact]
        public void RoundTrip_SerializeThenParse_RecoversBytes()
        {
            byte[] block = { 0x12, 0x34, 0x00, 0xFF, 0x9A, 0x0B, 0x07, 0x80 };
            string text = AddressListClipboardCore.Serialize("AddressList", "SoundRoomForm", block);
            bool ok = AddressListClipboardCore.TryParse(text, "AddressList", "SoundRoomForm", block.Length, out byte[] parsed);
            Assert.True(ok);
            Assert.Equal(block, parsed);
        }

        [Fact]
        public void TryParse_AcceptsExactSoundRoomFE78Header()
        {
            string text = "AddressList@SoundRoomForm 1 2 3 4";
            bool ok = AddressListClipboardCore.TryParse(text, "AddressList", "SoundRoomForm", 4, out byte[] parsed);
            Assert.True(ok);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, parsed);
        }

        [Fact]
        public void TryParse_AcceptsExactSoundRoomFE6Header()
        {
            string text = "AddressList@SoundRoomFE6Form A B C D";
            bool ok = AddressListClipboardCore.TryParse(text, "AddressList", "SoundRoomFE6Form", 4, out byte[] parsed);
            Assert.True(ok);
            Assert.Equal(new byte[] { 0x0A, 0x0B, 0x0C, 0x0D }, parsed);
        }

        // ---- Header mismatch rejection (WF parity) -----------------------

        [Fact]
        public void TryParse_WrongFormName_Rejected()
        {
            string text = "AddressList@SoundRoomForm 1 2 3 4";
            bool ok = AddressListClipboardCore.TryParse(text, "AddressList", "SoundRoomFE6Form", 4, out byte[] parsed);
            Assert.False(ok);
            Assert.Empty(parsed);
        }

        [Fact]
        public void TryParse_WrongListName_Rejected()
        {
            string text = "EntryList@SoundRoomForm 1 2 3 4";
            bool ok = AddressListClipboardCore.TryParse(text, "AddressList", "SoundRoomForm", 4, out _);
            Assert.False(ok);
        }

        // ---- Byte-count mismatch rejection (WF sp.Length != BlockSize+1) --

        [Fact]
        public void TryParse_TooFewBytes_Rejected()
        {
            string text = "AddressList@SoundRoomForm 1 2 3"; // 3 bytes, expecting 4
            bool ok = AddressListClipboardCore.TryParse(text, "AddressList", "SoundRoomForm", 4, out _);
            Assert.False(ok);
        }

        [Fact]
        public void TryParse_TooManyBytes_Rejected()
        {
            string text = "AddressList@SoundRoomForm 1 2 3 4 5"; // 5 bytes, expecting 4
            bool ok = AddressListClipboardCore.TryParse(text, "AddressList", "SoundRoomForm", 4, out _);
            Assert.False(ok);
        }

        // ---- Hardened parse: reject non-hex / overflow (Copilot review #5) -

        [Fact]
        public void TryParse_NonHexToken_Rejected()
        {
            // "ZZ" is not hex — WF U.atoh would silently yield 0; we must reject.
            string text = "AddressList@SoundRoomForm 1 ZZ 3 4";
            bool ok = AddressListClipboardCore.TryParse(text, "AddressList", "SoundRoomForm", 4, out _);
            Assert.False(ok);
        }

        [Fact]
        public void TryParse_OverflowToken_Rejected()
        {
            // "100" = 256 > 0xFF — must reject (byte.TryParse fails).
            string text = "AddressList@SoundRoomForm 1 2 3 100";
            bool ok = AddressListClipboardCore.TryParse(text, "AddressList", "SoundRoomForm", 4, out _);
            Assert.False(ok);
        }

        [Fact]
        public void TryParse_NullText_ReturnsFalse()
        {
            bool ok = AddressListClipboardCore.TryParse(null!, "AddressList", "SoundRoomForm", 4, out byte[] parsed);
            Assert.False(ok);
            Assert.Empty(parsed);
        }

        // ---- BuildCleared (WF ClearData) ---------------------------------

        [Fact]
        public void BuildCleared_ReturnsZeroFilledBlock()
        {
            byte[] cleared = AddressListClipboardCore.BuildCleared(12);
            Assert.Equal(12, cleared.Length);
            Assert.All(cleared, b => Assert.Equal(0, b));
        }

        [Fact]
        public void BuildCleared_NonPositive_ReturnsEmpty()
        {
            Assert.Empty(AddressListClipboardCore.BuildCleared(0));
            Assert.Empty(AddressListClipboardCore.BuildCleared(-1));
        }

        // ---- BuildSwap (WF SwapData crossed write) -----------------------

        [Fact]
        public void BuildSwap_CrossesBlocks()
        {
            byte[] a = { 1, 2, 3 };
            byte[] b = { 4, 5, 6 };
            bool ok = AddressListClipboardCore.BuildSwap(a, b, out byte[] newAtA, out byte[] newAtB);
            Assert.True(ok);
            // After swap: addrA holds b's old bytes, addrB holds a's old bytes.
            Assert.Equal(new byte[] { 4, 5, 6 }, newAtA);
            Assert.Equal(new byte[] { 1, 2, 3 }, newAtB);
        }

        [Fact]
        public void BuildSwap_LengthMismatch_ReturnsFalse()
        {
            bool ok = AddressListClipboardCore.BuildSwap(new byte[] { 1 }, new byte[] { 1, 2 }, out byte[] na, out byte[] nb);
            Assert.False(ok);
            Assert.Empty(na);
            Assert.Empty(nb);
        }

        [Fact]
        public void BuildSwap_DoesNotAliasInputs()
        {
            byte[] a = { 1, 2, 3 };
            byte[] b = { 4, 5, 6 };
            AddressListClipboardCore.BuildSwap(a, b, out byte[] newAtA, out byte[] newAtB);
            // Mutating the outputs must not touch the source arrays.
            newAtA[0] = 99;
            newAtB[0] = 88;
            Assert.Equal(4, b[0]);
            Assert.Equal(1, a[0]);
        }
    }
}
