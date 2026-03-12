using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class EditorFormRefTests
    {
        #region ParseFieldName

        [Theory]
        [InlineData("B0", EditorFormRef.FieldType.Byte, 0u)]
        [InlineData("B5", EditorFormRef.FieldType.Byte, 5u)]
        [InlineData("B255", EditorFormRef.FieldType.Byte, 255u)]
        [InlineData("W0", EditorFormRef.FieldType.Word, 0u)]
        [InlineData("W8", EditorFormRef.FieldType.Word, 8u)]
        [InlineData("D12", EditorFormRef.FieldType.DWord, 12u)]
        [InlineData("P16", EditorFormRef.FieldType.Pointer, 16u)]
        public void ParseFieldName_ValidPatterns(string name, EditorFormRef.FieldType expectedType, uint expectedOffset)
        {
            var field = EditorFormRef.ParseFieldName(name);
            Assert.NotNull(field);
            Assert.Equal(expectedType, field!.Type);
            Assert.Equal(expectedOffset, field.Offset);
            Assert.Equal(name.ToUpperInvariant(), field.Name);
        }

        [Theory]
        [InlineData("b5")]  // lowercase should work
        [InlineData("w8")]
        [InlineData("d12")]
        [InlineData("p16")]
        public void ParseFieldName_CaseInsensitive(string name)
        {
            var field = EditorFormRef.ParseFieldName(name);
            Assert.NotNull(field);
            Assert.Equal(name.ToUpperInvariant(), field!.Name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("X5")]       // unknown prefix
        [InlineData("B")]        // no offset
        [InlineData("5B")]       // reversed
        [InlineData("Label")]    // not a pattern
        [InlineData("BB5")]      // double prefix
        [InlineData("B5x")]      // trailing chars
        public void ParseFieldName_InvalidPatterns_ReturnsNull(string? name)
        {
            var field = EditorFormRef.ParseFieldName(name!);
            Assert.Null(field);
        }

        [Fact]
        public void ParseFieldName_ByteSize_Correct()
        {
            Assert.Equal(1, EditorFormRef.ParseFieldName("B0")!.ByteSize);
            Assert.Equal(2, EditorFormRef.ParseFieldName("W0")!.ByteSize);
            Assert.Equal(4, EditorFormRef.ParseFieldName("D0")!.ByteSize);
            Assert.Equal(4, EditorFormRef.ParseFieldName("P0")!.ByteSize);
        }

        #endregion

        #region DetectFields

        [Fact]
        public void DetectFields_FiltersAndSorts()
        {
            var names = new[] { "B5", "Label", "W2", "btnSave", "D12", "P8", "B0" };
            var fields = EditorFormRef.DetectFields(names);

            Assert.Equal(5, fields.Count);
            // Should be sorted by offset
            Assert.Equal(0u, fields[0].Offset);  // B0
            Assert.Equal(2u, fields[1].Offset);  // W2
            Assert.Equal(5u, fields[2].Offset);  // B5
            Assert.Equal(8u, fields[3].Offset);  // P8
            Assert.Equal(12u, fields[4].Offset); // D12
        }

        [Fact]
        public void DetectFields_EmptyInput_ReturnsEmpty()
        {
            var fields = EditorFormRef.DetectFields(Array.Empty<string>());
            Assert.Empty(fields);
        }

        [Fact]
        public void DetectFields_NoMatches_ReturnsEmpty()
        {
            var names = new[] { "Label1", "btnOK", "txtName" };
            var fields = EditorFormRef.DetectFields(names);
            Assert.Empty(fields);
        }

        #endregion

        #region ReadFields / WriteFields round-trip

        private ROM CreateTestRom(int size = 256)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            CoreState.ROM = rom;
            return rom;
        }

        [Fact]
        public void ReadFields_ByteWordDWord()
        {
            var rom = CreateTestRom();
            // Write known values at specific offsets
            rom.Data[5] = 0xAB;                          // B5 = 0xAB
            rom.Data[8] = 0xCD; rom.Data[9] = 0x12;      // W8 = 0x12CD
            rom.Data[12] = 0x78; rom.Data[13] = 0x56;
            rom.Data[14] = 0x34; rom.Data[15] = 0x12;    // D12 = 0x12345678

            var fields = new[]
            {
                new EditorFormRef.FieldDef { Name = "B5", Offset = 5, Type = EditorFormRef.FieldType.Byte },
                new EditorFormRef.FieldDef { Name = "W8", Offset = 8, Type = EditorFormRef.FieldType.Word },
                new EditorFormRef.FieldDef { Name = "D12", Offset = 12, Type = EditorFormRef.FieldType.DWord },
            };

            var values = EditorFormRef.ReadFields(rom, 0, fields);

            Assert.Equal(0xABu, values["B5"]);
            Assert.Equal(0x12CDu, values["W8"]);
            Assert.Equal(0x12345678u, values["D12"]);
        }

        [Fact]
        public void ReadFields_WithBaseAddress()
        {
            var rom = CreateTestRom();
            // Struct at base address 0x20, byte at offset 3
            rom.Data[0x23] = 0xFF;

            var fields = new[]
            {
                new EditorFormRef.FieldDef { Name = "B3", Offset = 3, Type = EditorFormRef.FieldType.Byte },
            };

            var values = EditorFormRef.ReadFields(rom, 0x20, fields);
            Assert.Equal(0xFFu, values["B3"]);
        }

        [Fact]
        public void WriteFields_RoundTrip()
        {
            var rom = CreateTestRom();
            var fields = new[]
            {
                new EditorFormRef.FieldDef { Name = "B0", Offset = 0, Type = EditorFormRef.FieldType.Byte },
                new EditorFormRef.FieldDef { Name = "W2", Offset = 2, Type = EditorFormRef.FieldType.Word },
                new EditorFormRef.FieldDef { Name = "D8", Offset = 8, Type = EditorFormRef.FieldType.DWord },
            };

            var writeValues = new Dictionary<string, uint>
            {
                ["B0"] = 0x42,
                ["W2"] = 0xBEEF,
                ["D8"] = 0xDEADBEEF,
            };

            EditorFormRef.WriteFields(rom, 0x10, writeValues, fields);
            var readValues = EditorFormRef.ReadFields(rom, 0x10, fields);

            Assert.Equal(0x42u, readValues["B0"]);
            Assert.Equal(0xBEEFu, readValues["W2"]);
            Assert.Equal(0xDEADBEEFu, readValues["D8"]);
        }

        [Fact]
        public void WriteFields_SkipsMissingKeys()
        {
            var rom = CreateTestRom();
            rom.Data[0] = 0xFF; // pre-existing value

            var fields = new[]
            {
                new EditorFormRef.FieldDef { Name = "B0", Offset = 0, Type = EditorFormRef.FieldType.Byte },
                new EditorFormRef.FieldDef { Name = "B1", Offset = 1, Type = EditorFormRef.FieldType.Byte },
            };

            // Only provide B1, not B0
            var values = new Dictionary<string, uint> { ["B1"] = 0x42 };
            EditorFormRef.WriteFields(rom, 0, values, fields);

            Assert.Equal(0xFFu, rom.u8(0)); // B0 untouched
            Assert.Equal(0x42u, rom.u8(1)); // B1 written
        }

        [Fact]
        public void WriteFields_WithUndo()
        {
            var rom = CreateTestRom();
            var undo = new Undo.UndoData { list = new System.Collections.Generic.List<Undo.UndoPostion>() };

            var fields = new[]
            {
                new EditorFormRef.FieldDef { Name = "B0", Offset = 0, Type = EditorFormRef.FieldType.Byte },
            };
            var values = new Dictionary<string, uint> { ["B0"] = 0x42 };

            EditorFormRef.WriteFields(rom, 0, values, fields, undo);

            Assert.Equal(0x42u, rom.u8(0));
            // Undo data should have recorded the write
            Assert.NotEmpty(undo.list);
        }

        #endregion

        #region CountEntries

        [Fact]
        public void CountEntries_StopsOnInvalid()
        {
            var rom = CreateTestRom(64);
            // 4 entries of size 8 — first 3 have non-zero byte at offset 0
            rom.Data[0] = 1;
            rom.Data[8] = 2;
            rom.Data[16] = 3;
            rom.Data[24] = 0; // terminator

            int count = EditorFormRef.CountEntries(rom, 0, 8,
                (idx, addr) => rom.u8(addr) != 0);

            Assert.Equal(3, count);
        }

        [Fact]
        public void CountEntries_StopsAtEndOfRom()
        {
            var rom = CreateTestRom(20);
            // All bytes non-zero
            for (int i = 0; i < 20; i++) rom.Data[i] = 0xFF;

            int count = EditorFormRef.CountEntries(rom, 0, 8,
                (idx, addr) => true);

            // 20 bytes / 8 = 2 complete entries (entry at 16 would need 16+8=24 > 20)
            Assert.Equal(2, count);
        }

        [Fact]
        public void CountEntries_ZeroEntrySize_Throws()
        {
            var rom = CreateTestRom();
            Assert.Throws<ArgumentException>(() =>
                EditorFormRef.CountEntries(rom, 0, 0, (i, a) => true));
        }

        [Fact]
        public void CountEntries_EmptyTable()
        {
            var rom = CreateTestRom(64);
            // First entry invalid
            int count = EditorFormRef.CountEntries(rom, 0, 8,
                (idx, addr) => rom.u8(addr) != 0);
            Assert.Equal(0, count);
        }

        #endregion

        #region BuildList

        [Fact]
        public void BuildList_CreatesCorrectEntries()
        {
            var rom = CreateTestRom(64);
            rom.Data[0] = 0x01;
            rom.Data[8] = 0x02;
            rom.Data[16] = 0x03;

            var list = EditorFormRef.BuildList(rom, 0, 8, 3,
                (idx, addr) => $"Entry {idx}: 0x{rom.u8(addr):X2}");

            Assert.Equal(3, list.Count);
            Assert.Equal(0u, list[0].addr);
            Assert.Equal("Entry 0: 0x01", list[0].name);
            Assert.Equal(8u, list[1].addr);
            Assert.Equal("Entry 1: 0x02", list[1].name);
            Assert.Equal(16u, list[2].addr);
            Assert.Equal("Entry 2: 0x03", list[2].name);
        }

        [Fact]
        public void BuildList_ZeroCount_ReturnsEmpty()
        {
            var rom = CreateTestRom();
            var list = EditorFormRef.BuildList(rom, 0, 8, 0, (i, a) => "x");
            Assert.Empty(list);
        }

        [Fact]
        public void BuildListWithCount_CombinesCountAndBuild()
        {
            var rom = CreateTestRom(64);
            rom.Data[0] = 1;
            rom.Data[4] = 2;
            rom.Data[8] = 0; // terminator

            var list = EditorFormRef.BuildListWithCount(rom, 0, 4,
                (idx, addr) => rom.u8(addr) != 0,
                (idx, addr) => $"#{idx}");

            Assert.Equal(2, list.Count);
            Assert.Equal("#0", list[0].name);
            Assert.Equal("#1", list[1].name);
        }

        #endregion

        #region Pointer field

        [Fact]
        public void ReadFields_Pointer_StripsGbaBase()
        {
            var rom = CreateTestRom(256);
            // Write a GBA pointer: 0x08000100 at offset 0
            rom.Data[0] = 0x00;
            rom.Data[1] = 0x01;
            rom.Data[2] = 0x00;
            rom.Data[3] = 0x08;

            var fields = new[]
            {
                new EditorFormRef.FieldDef { Name = "P0", Offset = 0, Type = EditorFormRef.FieldType.Pointer },
            };

            var values = EditorFormRef.ReadFields(rom, 0, fields);
            // p32 strips the 0x08000000 base
            Assert.Equal(0x100u, values["P0"]);
        }

        #endregion

        #region Null argument checks

        [Fact]
        public void ReadFields_NullRom_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                EditorFormRef.ReadFields(null!, 0, Array.Empty<EditorFormRef.FieldDef>()));
        }

        [Fact]
        public void WriteFields_NullValues_Throws()
        {
            var rom = CreateTestRom();
            Assert.Throws<ArgumentNullException>(() =>
                EditorFormRef.WriteFields(rom, 0, null!, Array.Empty<EditorFormRef.FieldDef>()));
        }

        [Fact]
        public void DetectFields_NullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                EditorFormRef.DetectFields(null!));
        }

        #endregion

        #region ExtraUnit integration patterns

        /// <summary>
        /// Validates that the field naming convention used by ExtraUnitFE8UViewModel
        /// (D0 + P4) correctly reads and writes an 8-byte struct via EditorFormRef.
        /// </summary>
        [Fact]
        public void ExtraUnitFE8U_FieldPattern_RoundTrip()
        {
            var rom = CreateTestRom(256);
            var fields = EditorFormRef.DetectFields(new[] { "D0", "P4" });

            Assert.Equal(2, fields.Count);
            Assert.Equal("D0", fields[0].Name);
            Assert.Equal(EditorFormRef.FieldType.DWord, fields[0].Type);
            Assert.Equal(0u, fields[0].Offset);
            Assert.Equal("P4", fields[1].Name);
            Assert.Equal(EditorFormRef.FieldType.Pointer, fields[1].Type);
            Assert.Equal(4u, fields[1].Offset);

            // Write a flag ID and a GBA pointer
            var writeValues = new Dictionary<string, uint>
            {
                ["D0"] = 0x0003,       // flag ID
                ["P4"] = 0x100,        // pointer (ROM offset, write_p32 adds 0x08000000)
            };
            EditorFormRef.WriteFields(rom, 0x20, writeValues, fields);

            // Read back
            var readValues = EditorFormRef.ReadFields(rom, 0x20, fields);
            Assert.Equal(0x0003u, readValues["D0"]);
            Assert.Equal(0x100u, readValues["P4"]);
        }

        /// <summary>
        /// Validates that the field naming convention used by ExtraUnitViewModel
        /// (P0) correctly reads and writes a 4-byte pointer-only struct.
        /// </summary>
        [Fact]
        public void ExtraUnit_FieldPattern_RoundTrip()
        {
            var rom = CreateTestRom(256);
            var fields = EditorFormRef.DetectFields(new[] { "P0" });

            Assert.Single(fields);
            Assert.Equal("P0", fields[0].Name);
            Assert.Equal(EditorFormRef.FieldType.Pointer, fields[0].Type);
            Assert.Equal(0u, fields[0].Offset);

            var writeValues = new Dictionary<string, uint> { ["P0"] = 0x200 };
            EditorFormRef.WriteFields(rom, 0x10, writeValues, fields);

            var readValues = EditorFormRef.ReadFields(rom, 0x10, fields);
            Assert.Equal(0x200u, readValues["P0"]);
        }

        #endregion
    }
}
