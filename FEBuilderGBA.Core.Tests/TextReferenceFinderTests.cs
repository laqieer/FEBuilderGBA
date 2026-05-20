using System;
using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="TextReferenceFinder"/>. Each test builds a tiny synthetic
    /// ROM in-memory and verifies that scanning correctly:
    ///   - dereferences ROMFEINFO pointer FIELDS via NameResolver.DerefPointer
    ///   - finds u16 text IDs at the configured offsets
    ///   - respects entry size, max count, and overflow-safe bounds checks
    ///
    /// IMPORTANT: All pointer FIELDS and table bases are placed at ROM offsets &gt;= 0x200
    /// because <see cref="U.isSafetyOffset(uint, ROM)"/> rejects anything below 0x200.
    /// </summary>
    public class TextReferenceFinderTests
    {
        /// <summary>
        /// Build a small ROM containing:
        ///   - at offset <paramref name="pointerFieldOffset"/>: a u32 pointer encoded in
        ///     GBA pointer format (data base + 0x08000000)
        ///   - at offset <paramref name="dataBaseOffset"/>: <paramref name="data"/>
        /// </summary>
        static ROM BuildRom(uint pointerFieldOffset, uint dataBaseOffset, byte[] data, int romSize = 0x1000)
        {
            var bytes = new byte[romSize];
            // Write GBA-formatted pointer (offset + 0x08000000) as little-endian u32 at the field
            uint ptrValue = dataBaseOffset + 0x08000000u;
            bytes[pointerFieldOffset + 0] = (byte)(ptrValue & 0xFF);
            bytes[pointerFieldOffset + 1] = (byte)((ptrValue >> 8) & 0xFF);
            bytes[pointerFieldOffset + 2] = (byte)((ptrValue >> 16) & 0xFF);
            bytes[pointerFieldOffset + 3] = (byte)((ptrValue >> 24) & 0xFF);
            Array.Copy(data, 0, bytes, dataBaseOffset, data.Length);
            var rom = new ROM();
            rom.SwapNewROMDataDirect(bytes);
            return rom;
        }

        static byte[] LE16(ushort v) => new byte[] { (byte)(v & 0xFF), (byte)((v >> 8) & 0xFF) };

        /// <summary>
        /// Three 4-byte entries at offsets +0 and +2 hold u16 text IDs.
        /// Entry 0: name=0x0100, desc=0x0200
        /// Entry 1: name=0x0101, desc=0x0201  ← target textId=0x0201 matches at +2
        /// Entry 2: name=0x0102, desc=0x0202
        /// </summary>
        [Fact]
        public void Find_PointerFieldDereferenced_FindsMatchAtConfiguredOffset()
        {
            uint pointerField = 0x200; // first valid offset
            uint dataBase = 0x400;
            var entries = new List<byte>();
            // entry 0
            entries.AddRange(LE16(0x0100));
            entries.AddRange(LE16(0x0200));
            // entry 1
            entries.AddRange(LE16(0x0101));
            entries.AddRange(LE16(0x0201));
            // entry 2
            entries.AddRange(LE16(0x0102));
            entries.AddRange(LE16(0x0202));

            var rom = BuildRom(pointerField, dataBase, entries.ToArray());
            var desc = new TextRefTableDescriptor
            {
                Kind = "Unit",
                PointerField = pointerField,
                EntrySize = 4,
                MaxCount = 3,
                TextIdOffsets = new uint[] { 0, 2 },
                NameResolver = id => $"U{id}",
            };

            var result = TextReferenceFinder.Find(rom, 0x0201, new[] { desc });

            Assert.Single(result);
            Assert.Equal("Unit 0x01 (U1)", result[0]);
        }

        [Fact]
        public void Find_ZeroTextId_ReturnsEmpty()
        {
            // Even with valid tables, textId 0 must short-circuit (text id 0 = no text)
            uint pointerField = 0x200;
            uint dataBase = 0x400;
            var entries = new List<byte>();
            entries.AddRange(LE16(0x0000));
            entries.AddRange(LE16(0x0000));
            var rom = BuildRom(pointerField, dataBase, entries.ToArray());
            var desc = new TextRefTableDescriptor
            {
                Kind = "Unit",
                PointerField = pointerField,
                EntrySize = 4,
                MaxCount = 1,
                TextIdOffsets = new uint[] { 0, 2 },
                NameResolver = _ => "",
            };

            var result = TextReferenceFinder.Find(rom, 0, new[] { desc });

            Assert.Empty(result);
        }

        [Fact]
        public void Find_PointerFieldIsZero_ReturnsEmpty()
        {
            // PointerField == 0 means the ROM info doesn't have this table address — skip.
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[0x1000]);
            var desc = new TextRefTableDescriptor
            {
                Kind = "Unit",
                PointerField = 0,
                EntrySize = 4,
                MaxCount = 10,
                TextIdOffsets = new uint[] { 0 },
                NameResolver = _ => "",
            };

            var result = TextReferenceFinder.Find(rom, 0x0001, new[] { desc });

            Assert.Empty(result);
        }

        /// <summary>
        /// Verifies the overflow-safe range guard. Even though baseAddr + maxCount*entrySize
        /// would overflow uint, the ulong cast in TextReferenceFinder must reject the
        /// whole-table-fits check without throwing or producing false matches.
        /// </summary>
        [Fact]
        public void Find_OverflowingTableExtent_RejectedSafely()
        {
            uint pointerField = 0x200;
            uint dataBase = 0x400;
            var rom = BuildRom(pointerField, dataBase, new byte[16]);

            var desc = new TextRefTableDescriptor
            {
                Kind = "Unit",
                PointerField = pointerField,
                EntrySize = 0x10000,
                MaxCount = 0x10000, // entrySize * maxCount overflows uint when multiplied as uint
                TextIdOffsets = new uint[] { 0 },
                NameResolver = _ => "",
            };

            // Must not throw, must not match
            var result = TextReferenceFinder.Find(rom, 0x0001, new[] { desc });
            Assert.Empty(result);
        }

        /// <summary>
        /// Matches across multiple tables/offsets and only one reference per entry
        /// even if multiple offsets in the same entry match.
        /// </summary>
        [Fact]
        public void Find_OneRefPerEntry_AcrossMultipleTables()
        {
            uint unitPointer = 0x200;
            uint classPointer = 0x204;
            uint unitDataBase = 0x400;
            uint classDataBase = 0x500;

            // Unit entry 0 has the target text ID at BOTH offset +0 AND +2 — must still yield ONE ref.
            var unitData = new List<byte>();
            unitData.AddRange(LE16(0x0555)); // matches at offset 0
            unitData.AddRange(LE16(0x0555)); // matches at offset 2 too — should be deduped within the entry

            // Class entry 1 has the target text ID at offset +2.
            var classData = new List<byte>();
            classData.AddRange(LE16(0x0000));
            classData.AddRange(LE16(0x0000));
            classData.AddRange(LE16(0x0001));
            classData.AddRange(LE16(0x0555));

            var rom = new ROM();
            var bytes = new byte[0x1000];
            // Write unit pointer field
            uint unitPtrValue = unitDataBase + 0x08000000u;
            bytes[unitPointer + 0] = (byte)(unitPtrValue & 0xFF);
            bytes[unitPointer + 1] = (byte)((unitPtrValue >> 8) & 0xFF);
            bytes[unitPointer + 2] = (byte)((unitPtrValue >> 16) & 0xFF);
            bytes[unitPointer + 3] = (byte)((unitPtrValue >> 24) & 0xFF);
            // Write class pointer field
            uint classPtrValue = classDataBase + 0x08000000u;
            bytes[classPointer + 0] = (byte)(classPtrValue & 0xFF);
            bytes[classPointer + 1] = (byte)((classPtrValue >> 8) & 0xFF);
            bytes[classPointer + 2] = (byte)((classPtrValue >> 16) & 0xFF);
            bytes[classPointer + 3] = (byte)((classPtrValue >> 24) & 0xFF);
            Array.Copy(unitData.ToArray(), 0, bytes, unitDataBase, unitData.Count);
            Array.Copy(classData.ToArray(), 0, bytes, classDataBase, classData.Count);
            rom.SwapNewROMDataDirect(bytes);

            var tables = new[]
            {
                new TextRefTableDescriptor
                {
                    Kind = "Unit",
                    PointerField = unitPointer,
                    EntrySize = 4,
                    MaxCount = 1,
                    TextIdOffsets = new uint[] { 0, 2 },
                    NameResolver = id => $"U{id}",
                },
                new TextRefTableDescriptor
                {
                    Kind = "Class",
                    PointerField = classPointer,
                    EntrySize = 4,
                    MaxCount = 2,
                    TextIdOffsets = new uint[] { 0, 2 },
                    NameResolver = id => $"C{id}",
                },
            };

            var result = TextReferenceFinder.Find(rom, 0x0555, tables);

            // One Unit ref (deduped within entry) + one Class ref
            Assert.Equal(2, result.Count);
            Assert.Contains("Unit 0x00 (U0)", result);
            Assert.Contains("Class 0x01 (C1)", result);
        }

        /// <summary>
        /// When NameResolver throws, the finder must still emit a reference with
        /// an empty name (graceful degradation).
        /// </summary>
        [Fact]
        public void Find_NameResolverThrows_StillEmitsReferenceWithoutCrash()
        {
            uint pointerField = 0x200;
            uint dataBase = 0x400;
            var entries = new List<byte>();
            entries.AddRange(LE16(0x00AA));
            var rom = BuildRom(pointerField, dataBase, entries.ToArray());

            var desc = new TextRefTableDescriptor
            {
                Kind = "Item",
                PointerField = pointerField,
                EntrySize = 2,
                MaxCount = 1,
                TextIdOffsets = new uint[] { 0 },
                NameResolver = _ => throw new InvalidOperationException("boom"),
            };

            var result = TextReferenceFinder.Find(rom, 0x00AA, new[] { desc });
            Assert.Single(result);
            Assert.Equal("Item 0x00 ()", result[0]);
        }

        [Fact]
        public void Find_NullRom_ReturnsEmpty()
        {
            var desc = new TextRefTableDescriptor
            {
                Kind = "Unit",
                PointerField = 0x200,
                EntrySize = 4,
                MaxCount = 1,
                TextIdOffsets = new uint[] { 0 },
            };
            var result = TextReferenceFinder.Find(null!, 0x0001, new[] { desc });
            Assert.Empty(result);
        }
    }
}
