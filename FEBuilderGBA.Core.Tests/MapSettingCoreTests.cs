using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class MapSettingCoreTests
    {
        [Fact]
        public void MakeMapIDList_WithNoRom_ReturnsEmpty()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var list = MapSettingCore.MakeMapIDList();
                Assert.NotNull(list);
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void GetMapAddr_WithNoRom_ReturnsNotFound()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                uint addr = MapSettingCore.GetMapAddr(0);
                Assert.Equal(U.NOT_FOUND, addr);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void MakeMapIDList_WithPointerBackedMapEntry_ReturnsEntry()
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;

                WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08000200);
                WriteU32(rom.Data, 0x200, 0x08000300); // WinForms treats a pointer here as valid.
                WriteU32(rom.Data, 0x204, 1);
                WriteU32(rom.Data, 0x208, 1);
                rom.Data[0x20C] = 0x00;

                var list = MapSettingCore.MakeMapIDList();

                Assert.Single(list);
                Assert.Equal((uint)0x200, list[0].addr);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        // ---- Version data size tests ----

        [Fact]
        public void FE6_MapSettingDataSize_Is68Or72()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(6, rom.RomInfo.version);
            Assert.True(rom.RomInfo.map_setting_datasize == 68 || rom.RomInfo.map_setting_datasize == 72,
                $"FE6 map_setting_datasize should be 68 or 72 but was {rom.RomInfo.map_setting_datasize}");
        }

        [Fact]
        public void FE8U_MapSettingDataSize_Is148()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(8, rom.RomInfo.version);
            Assert.Equal(148u, rom.RomInfo.map_setting_datasize);
        }

        [Fact]
        public void FE7JP_MapSettingDataSize_Is148()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AE7J01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(7, rom.RomInfo.version);
            Assert.Equal(148u, rom.RomInfo.map_setting_datasize);
        }

        [Fact]
        public void FE7U_MapSettingDataSize_Is152()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01");
            Assert.NotNull(rom.RomInfo);
            Assert.Equal(7, rom.RomInfo.version);
            Assert.Equal(152u, rom.RomInfo.map_setting_datasize);
        }

        [Fact]
        public void FE6_IsMultibyte_True()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AFEJ01");
            Assert.True(rom.RomInfo.is_multibyte);
        }

        [Fact]
        public void FE7U_IsMultibyte_False()
        {
            var rom = new ROM();
            rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01");
            Assert.False(rom.RomInfo.is_multibyte);
        }

        [Fact]
        public void MakeMapIDList_FE7U_ValidatesWithCorrectClearConditionOffsets()
        {
            // FE7U has clear conditions at offsets 0x8C/0x8E (140/142),
            // not 0x88/0x8A (136/138) like FE7JP/FE8.
            // D0 must be a non-pointer so the full validation path is exercised.
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01");
                CoreState.ROM = rom;

                uint mapPtr = rom.RomInfo.map_setting_pointer;
                uint baseAddr = 0x200;
                WriteU32(rom.Data, (int)mapPtr, 0x08000000 + baseAddr);

                // D0: non-pointer value to force the full validation path
                WriteU32(rom.Data, (int)baseAddr, 0x00000001);
                // W4, B6: non-zero PLISTs
                WriteU16(rom.Data, (int)(baseAddr + 4), 1);
                rom.Data[baseAddr + 6] = 1;
                // B12: weather (must be < 0xE)
                rom.Data[baseAddr + 12] = 0x01;
                // Map name texts at 0x70/0x72 - valid text IDs
                WriteU16(rom.Data, (int)(baseAddr + 0x70), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x72), 1);
                // FE7U clear conditions at 0x8C/0x8E (140/142) - valid text IDs
                WriteU16(rom.Data, (int)(baseAddr + 0x8C), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x8E), 1);

                // Set up text pointer table so textmax > 0
                WriteU32(rom.Data, (int)rom.RomInfo.text_pointer, 0x08000400);
                WriteU32(rom.Data, 0x400, 0x08000500); // text entry 0
                WriteU32(rom.Data, 0x404, 0x08000600); // text entry 1

                var list = MapSettingCore.MakeMapIDList();
                Assert.NotEmpty(list);
                Assert.Equal(baseAddr, list[0].addr);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void MakeMapIDList_FE7U_RejectsInvalidClearConditionAt0x8C()
        {
            // If FE7U clear condition at offset 0x8C (140) is invalid, the entry should be rejected
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01");
                CoreState.ROM = rom;

                uint mapPtr = rom.RomInfo.map_setting_pointer;
                uint baseAddr = 0x200;
                WriteU32(rom.Data, (int)mapPtr, 0x08000000 + baseAddr);

                // Non-pointer D0 (forces validation path)
                WriteU32(rom.Data, (int)baseAddr, 0x00000001);
                // W4: valid PLIST
                WriteU16(rom.Data, (int)(baseAddr + 4), 1);
                rom.Data[baseAddr + 6] = 1;
                // B12: valid weather
                rom.Data[baseAddr + 12] = 0x01;
                // Map name texts at 0x70/0x72 - valid
                WriteU16(rom.Data, (int)(baseAddr + 0x70), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x72), 1);
                // FE7U clear conditions at 0x8C - INVALID (huge text ID)
                WriteU16(rom.Data, (int)(baseAddr + 0x8C), 0xFFFF);
                WriteU16(rom.Data, (int)(baseAddr + 0x8E), 1);

                // Set up text pointer table
                WriteU32(rom.Data, (int)rom.RomInfo.text_pointer, 0x08000400);
                WriteU32(rom.Data, 0x400, 0x08000500);
                WriteU32(rom.Data, 0x404, 0x08000600);

                var list = MapSettingCore.MakeMapIDList();
                // Entry should be rejected because clear condition at offset 0x8C is invalid
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void MakeMapIDList_FE7U_ChapterNameUsesOffset132()
        {
            // FE7U (152-byte struct) reads the chapter number byte at offset 132.
            // GetMapName uses it to build a prefix like "Ch5" (value 10 => 10/2 = 5).
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AE7E01");
                CoreState.ROM = rom;

                uint mapPtr = rom.RomInfo.map_setting_pointer;
                uint baseAddr = 0x200;
                WriteU32(rom.Data, (int)mapPtr, 0x08000000 + baseAddr);

                // D0: non-pointer value to force the full validation path
                WriteU32(rom.Data, (int)baseAddr, 0x00000001);
                // W4, B6: non-zero PLISTs
                WriteU16(rom.Data, (int)(baseAddr + 4), 1);
                rom.Data[baseAddr + 6] = 1;
                // B12: weather (must be < 0xE)
                rom.Data[baseAddr + 12] = 0x01;
                // Map name text IDs at 0x70/0x72
                WriteU16(rom.Data, (int)(baseAddr + 0x70), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x72), 1);
                // FE7U clear conditions at 0x8C/0x8E
                WriteU16(rom.Data, (int)(baseAddr + 0x8C), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x8E), 1);

                // Chapter number byte at FE7U-specific offset 132 (0x84)
                // Value 10 => Ch5 (10/2=5, even so no 'x' suffix)
                rom.Data[baseAddr + 132] = 10;

                // Ensure offset 128 has a DIFFERENT value to prove offset 132 is used
                rom.Data[baseAddr + 128] = 20; // Would give "Ch10" if offset 128 were used

                // Set up text pointer table so textmax > 0
                WriteU32(rom.Data, (int)rom.RomInfo.text_pointer, 0x08000400);
                WriteU32(rom.Data, 0x400, 0x08000500);
                WriteU32(rom.Data, 0x404, 0x08000600);

                var list = MapSettingCore.MakeMapIDList();
                Assert.NotEmpty(list);
                // The display name should contain "Ch5" (from offset 132, value 10)
                // and NOT "Ch10" (which would come from offset 128, value 20)
                Assert.Contains("Ch5", list[0].name);
                Assert.DoesNotContain("Ch10", list[0].name);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void MakeMapIDList_FE7JP_ChapterNameUsesOffset128()
        {
            // FE7JP (148-byte struct) reads the chapter number byte at offset 128.
            // GetMapName uses it to build a prefix like "Ch5" (value 10 => 10/2 = 5).
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "AE7J01");
                CoreState.ROM = rom;

                uint mapPtr = rom.RomInfo.map_setting_pointer;
                uint baseAddr = 0x200;
                WriteU32(rom.Data, (int)mapPtr, 0x08000000 + baseAddr);

                // D0: non-pointer value to force the full validation path
                WriteU32(rom.Data, (int)baseAddr, 0x00000001);
                // W4, B6: non-zero PLISTs
                WriteU16(rom.Data, (int)(baseAddr + 4), 1);
                rom.Data[baseAddr + 6] = 1;
                // B12: weather (must be < 0xE)
                rom.Data[baseAddr + 12] = 0x01;
                // Map name text IDs at 0x70/0x72
                WriteU16(rom.Data, (int)(baseAddr + 0x70), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x72), 1);
                // FE7JP clear conditions at 0x88/0x8A (148-byte layout)
                WriteU16(rom.Data, (int)(baseAddr + 0x88), 1);
                WriteU16(rom.Data, (int)(baseAddr + 0x8A), 1);

                // Chapter number byte at FE7JP-specific offset 128 (0x80)
                // Value 10 => Ch5 (10/2=5, even so no 'x' suffix)
                rom.Data[baseAddr + 128] = 10;

                // Ensure offset 132 has a DIFFERENT value to prove offset 128 is used
                rom.Data[baseAddr + 132] = 20; // Would give "Ch10" if offset 132 were used

                // Set up text pointer table so textmax > 0
                WriteU32(rom.Data, (int)rom.RomInfo.text_pointer, 0x08000400);
                WriteU32(rom.Data, 0x400, 0x08000500);
                WriteU32(rom.Data, 0x404, 0x08000600);

                var list = MapSettingCore.MakeMapIDList();
                Assert.NotEmpty(list);
                // The display name should contain "Ch5" (from offset 128, value 10)
                // and NOT "Ch10" (which would come from offset 132, value 20)
                Assert.Contains("Ch5", list[0].name);
                Assert.DoesNotContain("Ch10", list[0].name);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        // ---- GetMapNameById (#948 review: reject out-of-range / garbage) ----

        [Fact]
        public void GetMapNameById_WithNoRom_ReturnsEmpty()
        {
            var origRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                Assert.Equal("", MapSettingCore.GetMapNameById(0));
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void GetMapNameById_ValidPointerBackedEntry_ReturnsName()
        {
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;

                // Map 0 at 0x200 — pointer in the first dword makes it a valid entry.
                WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08000200);
                WriteU32(rom.Data, 0x200, 0x08000300);

                // A valid entry must resolve without throwing; name may be "" if the
                // text id is 0, but the call itself must succeed (no exception, no garbage).
                string name = MapSettingCore.GetMapNameById(0);
                Assert.NotNull(name);
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void GetMapNameById_OutOfRangeId_ReturnsEmpty_NotGarbage()
        {
            // Regression for #948: an out-of-range mapId lands on trailing data.
            // The entry fails IsMapSettingValid, so GetMapNameById must return ""
            // rather than reading a garbage chapter name (or throwing).
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;

                uint baseAddr = 0x200;
                WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08000000 + baseAddr);
                // Exactly ONE valid map (entry 0): pointer-backed first dword.
                WriteU32(rom.Data, (int)baseAddr, 0x08000300);
                // Entry 1 onward stays all-zero → IsMapSettingValid returns false
                // (no pointer, zero PLISTs). A far out-of-range id resolves the same.
                Assert.Equal("", MapSettingCore.GetMapNameById(1));
                Assert.Equal("", MapSettingCore.GetMapNameById(50));
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        [Fact]
        public void GetMapNameById_IdWhoseRecordRunsPastEof_ReturnsEmpty()
        {
            // The record START can be in-bounds while start+datasize runs past EOF.
            // GetMapNameById must reject it (return "") instead of letting
            // GetMapName's u8/u16 reads throw IndexOutOfRangeException. Use a
            // full-size ROM (so the fixed ROM-info pointers are writable) but place
            // the map table so the entry record overruns the buffer end.
            var origRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.LoadLow("test.gba", new byte[0x1000000], "BE8E01");
                CoreState.ROM = rom;

                // datasize 148 -> base + 148 must exceed Length (0x1000000).
                uint baseAddr = 0xFFFFF0;
                WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08000000 + baseAddr);
                WriteU32(rom.Data, (int)baseAddr, 0x08000100); // looks pointer-backed

                // Must not throw; must return "" (record overruns EOF).
                Assert.Equal("", MapSettingCore.GetMapNameById(0));
            }
            finally
            {
                CoreState.ROM = origRom;
            }
        }

        // ---- GetMapIdFromAddr (#1003 — FE6 Map Settings Jump-to-MapEditor) ----

        /// <summary>
        /// Build a synthetic FE6 ROM whose map-setting table at
        /// <paramref name="baseAddr"/> holds <paramref name="mapCount"/> valid
        /// pointer-backed rows (each <c>map_setting_datasize</c> bytes) followed
        /// by an all-zero terminator row (fails IsMapSettingValid). Returns the
        /// loaded ROM and reports the base offset + per-row size via out params.
        /// </summary>
        static ROM MakeFe6WithMapTable(out uint baseAddr, out uint dataSize, int mapCount = 4)
        {
            var rom = new ROM();
            rom.LoadLow("synthetic-fe6.gba", new byte[0x1000000], "AFEJ01");
            baseAddr = 0x200u;
            dataSize = rom.RomInfo.map_setting_datasize; // FE6 = 68 or 72

            WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08000000 + baseAddr);
            // Each valid row's first dword is a pointer → IsMapSettingValid true.
            for (int i = 0; i < mapCount; i++)
            {
                uint rowAddr = baseAddr + (uint)i * dataSize;
                WriteU32(rom.Data, (int)rowAddr, 0x08000300); // pointer-backed → valid
            }
            // Terminator row (all-zero) at index mapCount → IsMapSettingValid false.
            // The buffer is already zero there; nothing to write.
            return rom;
        }

        [Fact]
        public void GetMapIdFromAddr_NullRom_ReturnsNotFound()
        {
            Assert.Equal(U.NOT_FOUND, MapSettingCore.GetMapIdFromAddr(null!, 0x200u));
        }

        [Fact]
        public void GetMapIdFromAddr_ValidAddresses_ReturnsCorrectId()
        {
            ROM rom = MakeFe6WithMapTable(out uint baseAddr, out uint dataSize, mapCount: 4);
            Assert.Equal(0u, MapSettingCore.GetMapIdFromAddr(rom, baseAddr));
            Assert.Equal(1u, MapSettingCore.GetMapIdFromAddr(rom, baseAddr + dataSize));
            Assert.Equal(3u, MapSettingCore.GetMapIdFromAddr(rom, baseAddr + 3u * dataSize));
        }

        [Fact]
        public void GetMapIdFromAddr_PointerFormAndOffsetForm_ResolveSame()
        {
            ROM rom = MakeFe6WithMapTable(out uint baseAddr, out uint dataSize, mapCount: 4);
            uint offsetAddr = baseAddr + 2u * dataSize;
            uint pointerAddr = offsetAddr + 0x08000000u;
            Assert.Equal(2u, MapSettingCore.GetMapIdFromAddr(rom, offsetAddr));
            Assert.Equal(MapSettingCore.GetMapIdFromAddr(rom, offsetAddr),
                         MapSettingCore.GetMapIdFromAddr(rom, pointerAddr));
        }

        [Fact]
        public void GetMapIdFromAddr_BelowBase_ReturnsNotFound()
        {
            ROM rom = MakeFe6WithMapTable(out uint baseAddr, out _, mapCount: 4);
            Assert.Equal(U.NOT_FOUND, MapSettingCore.GetMapIdFromAddr(rom, baseAddr - 4u));
        }

        [Fact]
        public void GetMapIdFromAddr_MisalignedAddr_ReturnsNotFound()
        {
            ROM rom = MakeFe6WithMapTable(out uint baseAddr, out _, mapCount: 4);
            Assert.Equal(U.NOT_FOUND, MapSettingCore.GetMapIdFromAddr(rom, baseAddr + 1u));
        }

        [Fact]
        public void GetMapIdFromAddr_TerminatorRow_ReturnsNotFound()
        {
            // base + mapCount*dataSize is the all-zero terminator row → must be
            // rejected by the IsMapSettingValid gate (not reported as a map id).
            ROM rom = MakeFe6WithMapTable(out uint baseAddr, out uint dataSize, mapCount: 4);
            Assert.Equal(U.NOT_FOUND, MapSettingCore.GetMapIdFromAddr(rom, baseAddr + 4u * dataSize));
            // One row past the terminator (also aligned, also invalid).
            Assert.Equal(U.NOT_FOUND, MapSettingCore.GetMapIdFromAddr(rom, baseAddr + 5u * dataSize));
        }

        [Fact]
        public void GetMapIdFromAddr_ValidLookingRowAfterTerminator_ReturnsNotFound()
        {
            // Rows 0,1 valid; row 2 all-zero terminator; row 3 valid-looking
            // (pointer-backed). MakeMapIDList stops at row 2 (count == 2), so the
            // post-terminator row 3 is NOT an enumerated entry even though it
            // passes IsMapSettingValid in isolation — must resolve to NOT_FOUND.
            // (Regression for Copilot CLI review #1086 blocking finding #2.)
            var rom = new ROM();
            rom.LoadLow("synthetic-fe6.gba", new byte[0x1000000], "AFEJ01");
            uint baseAddr = 0x200u;
            uint dataSize = rom.RomInfo.map_setting_datasize;
            WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08000000 + baseAddr);
            WriteU32(rom.Data, (int)(baseAddr + 0u * dataSize), 0x08000300); // row 0 valid
            WriteU32(rom.Data, (int)(baseAddr + 1u * dataSize), 0x08000300); // row 1 valid
            // row 2 left all-zero → terminator (IsMapSettingValid false)
            WriteU32(rom.Data, (int)(baseAddr + 3u * dataSize), 0x08000300); // row 3 valid-looking

            Assert.Equal(2, MapSettingCore.MakeMapIDList(rom).Count);
            Assert.Equal(0u, MapSettingCore.GetMapIdFromAddr(rom, baseAddr));
            Assert.Equal(1u, MapSettingCore.GetMapIdFromAddr(rom, baseAddr + 1u * dataSize));
            Assert.Equal(U.NOT_FOUND, MapSettingCore.GetMapIdFromAddr(rom, baseAddr + 3u * dataSize));
        }

        [Fact]
        public void GetMapIdFromAddr_RecordPastEof_ReturnsNotFound()
        {
            // The record START is in-bounds but start+datasize runs past EOF.
            var rom = new ROM();
            rom.LoadLow("synthetic-fe6.gba", new byte[0x1000000], "AFEJ01");
            // Place the table base 4 bytes before EOF so an aligned row (delta 0)
            // is in-bounds at the start but baseAddr + dataSize overruns the buffer.
            uint baseAddr = (uint)rom.Data.Length - 4u;
            WriteU32(rom.Data, (int)rom.RomInfo.map_setting_pointer, 0x08000000 + baseAddr);
            Assert.Equal(U.NOT_FOUND, MapSettingCore.GetMapIdFromAddr(rom, baseAddr));
        }

        [Fact]
        public void GetMapIdFromAddr_ZeroBasePointer_ReturnsNotFound()
        {
            // map_setting_pointer slot reads 0 (zero-filled) → unsafe base.
            var rom = new ROM();
            rom.LoadLow("synthetic-fe6.gba", new byte[0x1000000], "AFEJ01");
            // Do NOT plant a base pointer — p32(slot) == 0 → isSafetyOffset false.
            Assert.Equal(U.NOT_FOUND, MapSettingCore.GetMapIdFromAddr(rom, 0x200u));
        }

        // ---- StripControlChars (#1705 — EventCond chapter-name U+001F box) ----

        [Fact]
        public void StripControlChars_NullOrEmpty_ReturnsInput()
        {
            Assert.Null(MapSettingCore.StripControlChars(null!));
            Assert.Equal("", MapSettingCore.StripControlChars(""));
        }

        [Fact]
        public void StripControlChars_NormalText_IsUnchanged()
        {
            const string normal = "Chapter 5 - The Journey";
            Assert.Equal(normal, MapSettingCore.StripControlChars(normal));
        }

        [Fact]
        public void StripControlChars_RemovesU001F()
        {
            // FE7/FE8 ROM names can embed U+001F (unit-separator) as an internal
            // formatting marker — it renders as a tofu box on macOS in Avalonia.
            string input = "Ch5Belyth";
            Assert.Equal("Ch5Belyth", MapSettingCore.StripControlChars(input));
        }

        [Fact]
        public void StripControlChars_RemovesAllControlChars_KeepsTab()
        {
            // All chars < 0x20 (except tab \t) are stripped.
            // Use explicit \u escapes to avoid C# greedy \x hex parsing.
            string input = "A BCC\t D";
            Assert.Equal("ABCC\t D", MapSettingCore.StripControlChars(input));
        }

        [Fact]
        public void StripControlChars_CJKText_Unchanged()
        {
            const string cjk = "第5章　試練";
            Assert.Equal(cjk, MapSettingCore.StripControlChars(cjk));
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        static void WriteU16(byte[] data, int offset, ushort value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
