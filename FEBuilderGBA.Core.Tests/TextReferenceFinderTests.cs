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

        /// <summary>
        /// PR review follow-up (Copilot CLI): MaxCount is an upper bound for
        /// expansion (e.g. 0x100 for class/item). A relocated/expanded ROM may
        /// have fewer entries than the bound — the previous code bailed on the
        /// entire table if MaxCount*EntrySize exceeded ROM length, hiding valid
        /// early matches. The fix clamps to the number of entries that actually
        /// fit. This test builds a ROM whose table base + MaxCount*EntrySize
        /// extends past ROM end but whose early entries fit, places a match in
        /// an early entry, and asserts the match is found.
        /// </summary>
        [Fact]
        public void Find_PartiallyFittingTable_ScansAvailableEntries()
        {
            // ROM size 0x1000. Base at 0xF00 leaves 0x100 bytes. With
            // EntrySize=4 and MaxCount=0x80 (=0x200 bytes), only 0x100/4 = 64
            // entries actually fit. Match in entry 5 must be found.
            int romSize = 0x1000;
            uint pointerField = 0x200;
            uint dataBase = 0xF00;

            var bytes = new byte[romSize];
            uint ptrValue = dataBase + 0x08000000u;
            bytes[pointerField + 0] = (byte)(ptrValue & 0xFF);
            bytes[pointerField + 1] = (byte)((ptrValue >> 8) & 0xFF);
            bytes[pointerField + 2] = (byte)((ptrValue >> 16) & 0xFF);
            bytes[pointerField + 3] = (byte)((ptrValue >> 24) & 0xFF);

            // Lay 16 entries (well within the 64-fitting limit). Entry 5
            // (offset 0xF00 + 5*4 = 0xF14) holds text id 0x0777 at +0.
            for (int i = 0; i < 16; i++)
            {
                int entryOffset = (int)dataBase + i * 4;
                ushort id = (ushort)(0x1000 + i);
                if (i == 5) id = 0x0777;
                bytes[entryOffset + 0] = (byte)(id & 0xFF);
                bytes[entryOffset + 1] = (byte)((id >> 8) & 0xFF);
            }

            var rom = new ROM();
            rom.SwapNewROMDataDirect(bytes);

            var desc = new TextRefTableDescriptor
            {
                Kind = "Class",
                PointerField = pointerField,
                EntrySize = 4,
                MaxCount = 0x80, // upper bound; 0x80 * 4 = 0x200 bytes; actual fit is 64 entries (0x100/4)
                TextIdOffsets = new uint[] { 0, 2 },
                NameResolver = id => $"C{id}",
            };

            var result = TextReferenceFinder.Find(rom, 0x0777, new[] { desc });

            // The early entry must be found despite the table's nominal extent
            // exceeding ROM end.
            Assert.Single(result);
            Assert.Equal("Class 0x05 (C5)", result[0]);
        }

        /// <summary>
        /// PR review follow-up (Copilot CLI): even though
        /// NameResolver.DerefPointer validates the ROMFEINFO pointer-field
        /// address, it does NOT validate the dereferenced table base. A
        /// malformed pointer value (e.g. one whose offset is below the 0x200
        /// safety floor) would let the scan loop walk forward into arbitrary
        /// later safe offsets as i advanced, reintroducing false positives.
        /// The fix rejects bases that fail U.isSafetyOffset upfront.
        /// </summary>
        [Fact]
        public void Find_DereffedBaseBelowSafetyFloor_ReturnsEmpty()
        {
            // pointer field at 0x200 holds GBA-encoded pointer 0x08000100,
            // which dereferences to ROM offset 0x100 — below the 0x200 safety
            // floor. Even with valid u16 text-id-matching bytes anywhere in
            // the ROM, the scan must short-circuit.
            uint pointerField = 0x200;
            uint badDataBase = 0x100; // below 0x200 — should be rejected
            var bytes = new byte[0x1000];
            uint ptrValue = badDataBase + 0x08000000u;
            bytes[pointerField + 0] = (byte)(ptrValue & 0xFF);
            bytes[pointerField + 1] = (byte)((ptrValue >> 8) & 0xFF);
            bytes[pointerField + 2] = (byte)((ptrValue >> 16) & 0xFF);
            bytes[pointerField + 3] = (byte)((ptrValue >> 24) & 0xFF);
            // Salt the ROM with bytes that WOULD match if the scan walked.
            for (int i = 0x300; i < 0x800; i += 4)
            {
                bytes[i + 0] = 0x99;
                bytes[i + 1] = 0x09;
            }

            var rom = new ROM();
            rom.SwapNewROMDataDirect(bytes);

            var desc = new TextRefTableDescriptor
            {
                Kind = "Unit",
                PointerField = pointerField,
                EntrySize = 4,
                MaxCount = 64,
                TextIdOffsets = new uint[] { 0 },
                NameResolver = _ => "X",
            };

            var result = TextReferenceFinder.Find(rom, 0x0999, new[] { desc });

            Assert.Empty(result);
        }

        // ===========================================================
        // Issue #349 follow-up tests — DirectBase + Terminator paths.
        // ===========================================================

        /// <summary>
        /// Descriptors with DirectBase set should bypass pointer dereferencing
        /// and read the table base directly. Mirrors FE7's ed_3c_pointer which
        /// stores the base address (not a pointer to a pointer).
        /// </summary>
        [Fact]
        public void Find_DirectBase_BypassesPointerDeref()
        {
            uint dataBase = 0x400;
            var entries = new List<byte>();
            entries.AddRange(LE16(0x0100));
            entries.AddRange(LE16(0x0200));
            entries.AddRange(LE16(0x0100)); // entry 1 - target
            entries.AddRange(LE16(0xABCD));
            var bytes = new byte[0x1000];
            Array.Copy(entries.ToArray(), 0, bytes, dataBase, entries.Count);
            var rom = new ROM();
            rom.SwapNewROMDataDirect(bytes);

            // Pass the data base directly via DirectBase (in GBA pointer form).
            // The finder converts it to ROM offset via U.toOffset.
            var desc = new TextRefTableDescriptor
            {
                Kind = "ED_Lyn",
                DirectBase = dataBase + 0x08000000u, // GBA pointer form
                EntrySize = 4,
                MaxCount = 2,
                TextIdOffsets = new uint[] { 0, 2 },
                NameResolver = id => $"L{id}",
            };

            var result = TextReferenceFinder.Find(rom, 0xABCD, new[] { desc });
            Assert.Single(result);
            Assert.Equal("ED_Lyn 0x01 (L1)", result[0]);
        }

        /// <summary>
        /// A descriptor with a Terminator predicate must stop scanning at the
        /// sentinel entry, even when MaxCount is much larger. This guards
        /// against false positives past the real table end.
        /// </summary>
        [Fact]
        public void Find_TerminatorStopsAtSentinel()
        {
            uint pointerField = 0x200;
            uint dataBase = 0x400;
            // 4 real entries, then a 0xFFFF sentinel, then bytes that WOULD match
            // if the scanner walked past the terminator.
            var entries = new List<byte>();
            entries.AddRange(LE16(0x0001)); // entry 0
            entries.AddRange(LE16(0x0002)); // entry 1
            entries.AddRange(LE16(0x0003)); // entry 2
            entries.AddRange(LE16(0x0004)); // entry 3
            entries.AddRange(LE16(0xFFFF)); // entry 4 = sentinel
            entries.AddRange(LE16(0x0042)); // entry 5 = would match — must NOT scan
            entries.AddRange(LE16(0x0042)); // entry 6 = would match — must NOT scan

            var rom = BuildRom(pointerField, dataBase, entries.ToArray());

            var desc = new TextRefTableDescriptor
            {
                Kind = "Haiku",
                PointerField = pointerField,
                EntrySize = 2,
                MaxCount = 100, // way past the actual table size
                TextIdOffsets = new uint[] { 0 },
                Terminator = (r, entry) =>
                {
                    if (entry + 2 > (uint)r.Data.Length) return true;
                    return r.u16(entry) == 0xFFFF;
                },
                NameResolver = _ => "",
            };

            var result = TextReferenceFinder.Find(rom, 0x0042, new[] { desc });
            // Match should NOT be found — the terminator stops the scan before
            // reaching entries 5/6.
            Assert.Empty(result);
        }

        /// <summary>
        /// When the terminator predicate throws, the finder treats the table
        /// as terminated at that entry (defensive — bad predicate shouldn't
        /// crash the whole scan).
        /// </summary>
        [Fact]
        public void Find_TerminatorThrows_TableSkipped()
        {
            uint pointerField = 0x200;
            uint dataBase = 0x400;
            var entries = new List<byte>();
            entries.AddRange(LE16(0x0042));
            var rom = BuildRom(pointerField, dataBase, entries.ToArray());

            var desc = new TextRefTableDescriptor
            {
                Kind = "Foo",
                PointerField = pointerField,
                EntrySize = 2,
                MaxCount = 10,
                TextIdOffsets = new uint[] { 0 },
                Terminator = (r, e) => throw new InvalidOperationException("bad"),
                NameResolver = _ => "",
            };

            var result = TextReferenceFinder.Find(rom, 0x0042, new[] { desc });
            // Should not crash; treats throwing terminator as "stop now".
            Assert.Empty(result);
        }

        /// <summary>
        /// When both PointerField and DirectBase are set, DirectBase takes
        /// precedence so the descriptor behaviour is deterministic.
        /// </summary>
        [Fact]
        public void Find_BothPointerFieldAndDirectBaseSet_DirectBaseWins()
        {
            uint pointerField = 0x200;
            uint pointerFieldPointsTo = 0x800; // would-be alternative base
            uint directBase = 0x400;            // actual data location

            var bytes = new byte[0x1000];
            // Pointer field points at 0x800
            uint ptrValue = pointerFieldPointsTo + 0x08000000u;
            bytes[pointerField + 0] = (byte)(ptrValue & 0xFF);
            bytes[pointerField + 1] = (byte)((ptrValue >> 8) & 0xFF);
            bytes[pointerField + 2] = (byte)((ptrValue >> 16) & 0xFF);
            bytes[pointerField + 3] = (byte)((ptrValue >> 24) & 0xFF);
            // Match at the pointer-field-derived location
            bytes[pointerFieldPointsTo + 0] = 0x99;
            bytes[pointerFieldPointsTo + 1] = 0x09;
            // Match at directBase
            bytes[directBase + 0] = 0x99;
            bytes[directBase + 1] = 0x09;

            var rom = new ROM();
            rom.SwapNewROMDataDirect(bytes);

            var desc = new TextRefTableDescriptor
            {
                Kind = "Test",
                PointerField = pointerField,
                DirectBase = directBase + 0x08000000u,
                EntrySize = 4,
                MaxCount = 1,
                TextIdOffsets = new uint[] { 0 },
                NameResolver = id => $"T{id}",
            };

            var result = TextReferenceFinder.Find(rom, 0x0999, new[] { desc });
            // DirectBase takes precedence so we get exactly ONE match (not two).
            Assert.Single(result);
        }
    }
}
