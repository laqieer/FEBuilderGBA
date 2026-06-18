using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{

    /// <summary>
    /// Tests for <see cref="ItemClassListCore"/> — the cross-platform Core helper
    /// that backs the Avalonia Item Effectiveness / Item Promotion editors (issue
    /// #368). All ROM-mutating tests use the loaded ROM with an explicit Undo
    /// rollback in a try/finally so the disk-side ROM is never modified by tests
    /// running back-to-back.
    /// </summary>
    [Collection("SharedState")]
    public class ItemClassListCoreTests
    {
        readonly ITestOutputHelper _output;

        public ItemClassListCoreTests(ITestOutputHelper output)
        {
            _output = output;
        }

        static string? FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string? dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = Path.Combine(dir, "roms", romName);
                    if (File.Exists(path)) return path;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        // ---------------------------------------------------------------------
        // ScanClassList
        // ---------------------------------------------------------------------

        [Fact]
        public void ScanClassList_EmptyOnZeroAddress()
        {
            // baseAddr=0 is treated as a null pointer (matches the WinForms
            // convention that real ROM tables never live at offset 0). Even
            // when the ROM has non-zero data at offset 0, ScanClassList must
            // refuse to scan from there.
            byte[] data = new byte[64];
            data[0] = 0x10;
            data[1] = 0x20;
            data[2] = 0x00;
            var rom = MakeRom(data);
            var list = ItemClassListCore.ScanClassList(rom, baseAddr: 0);
            Assert.Empty(list);
        }

        [Fact]
        public void ScanClassList_TerminatesOnFirstZero()
        {
            // Use a non-zero base offset so the null-pointer guard above does
            // not short-circuit. Bytes at offsets 4..6 hold the class list,
            // offset 7 is the terminator.
            byte[] data = new byte[64];
            data[4] = 0x10;
            data[5] = 0x20;
            data[6] = 0x30;
            data[7] = 0x00; // terminator
            data[8] = 0x40; // beyond terminator — must not be returned

            var rom = MakeRom(data);
            var list = ItemClassListCore.ScanClassList(rom, baseAddr: 4);
            Assert.Equal(3, list.Count);
            Assert.Equal(0x10u, list[0]);
            Assert.Equal(0x20u, list[1]);
            Assert.Equal(0x30u, list[2]);
        }

        [Fact]
        public void ScanClassList_HandlesBoundsSafely()
        {
            // Array that fills the entire ROM and never hits a terminator.
            // Start at offset 1 so we exercise the non-null path and the
            // bounds check at the same time.
            byte[] data = new byte[8];
            for (int i = 0; i < data.Length; i++) data[i] = 0x42;
            var rom = MakeRom(data);
            var list = ItemClassListCore.ScanClassList(rom, baseAddr: 1);
            Assert.Equal(7, list.Count); // 7 bytes from offset 1 to end of ROM
        }

        // ---------------------------------------------------------------------
        // WriteClassByte
        // ---------------------------------------------------------------------

        [Fact]
        public void WriteClassByte_UpdatesByteAndRecordsUndo()
        {
            byte[] data = new byte[16];
            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom; // Undo needs CoreState.ROM
            try
            {
                var undo = new Undo.UndoData
                {
                    name = "test",
                    list = new List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };

                ItemClassListCore.WriteClassByte(rom, addr: 3, classId: 0x55, undo: undo);

                Assert.Equal(0x55, rom.Data[3]);
                Assert.Single(undo.list);
                Assert.Equal(3u, undo.list[0].addr);
                Assert.Equal(1, undo.list[0].data.Length);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        // ---------------------------------------------------------------------
        // FindItemsSharingPointer (FE8U real ROM)
        // ---------------------------------------------------------------------

        [Fact]
        public void FindItemsSharingPointer_ReturnsOwnerWhenInputMatches()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var prev = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath, out _);
                CoreState.ROM = rom;

                // Find any item whose +16 (effectiveness) is a real pointer.
                uint itemBase = rom.p32(rom.RomInfo.item_pointer);
                Assert.True(U.isSafetyOffset(itemBase));
                uint itemSize = rom.RomInfo.item_datasize;

                uint? foundEffectivenessAddr = null;
                uint foundItemId = 0;
                for (uint i = 1; i < 0x100; i++)
                {
                    uint itemAddr = itemBase + i * itemSize;
                    if (itemAddr + itemSize > (uint)rom.Data.Length) break;
                    uint critPtr = rom.u32(itemAddr + 16);
                    if (!U.isPointer(critPtr)) continue;
                    uint critOff = U.toOffset(critPtr);
                    if (!U.isSafetyOffset(critOff)) continue;
                    foundEffectivenessAddr = critOff;
                    foundItemId = i;
                    break;
                }
                if (foundEffectivenessAddr == null)
                {
                    _output.WriteLine("SKIP: FE8U has no item with effectiveness pointer");
                    return;
                }

                var owners = ItemClassListCore.FindItemsSharingPointer(rom, foundEffectivenessAddr.Value);
                _output.WriteLine($"Effectiveness 0x{foundEffectivenessAddr:X08} is owned by items: {string.Join(",", owners)}");
                Assert.Contains(foundItemId, owners);
                Assert.True(owners.Count >= 1, "At least one owner must be returned");
            }
            finally
            {
                CoreState.ROM = prev;
            }
        }

        // ---------------------------------------------------------------------
        // ExpandClassList — synthetic ROM
        // ---------------------------------------------------------------------

        [Fact]
        public void ExpandClassList_AppendsZeroSlotBeforeTerminator()
        {
            // Synthetic 1KB ROM, free space after offset 128.
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF; // free
            // The class list: 0x10, 0x20, 0x00 at offset 32
            data[32] = 0x10;
            data[33] = 0x20;
            data[34] = 0x00;
            // The "pointer" at offset 0: GBA pointer to offset 32.
            uint origPtr = 32u | 0x08000000u;
            data[0] = (byte)(origPtr & 0xFF);
            data[1] = (byte)((origPtr >> 8) & 0xFF);
            data[2] = (byte)((origPtr >> 16) & 0xFF);
            data[3] = (byte)((origPtr >> 24) & 0xFF);

            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = new Undo.UndoData
                {
                    name = "test",
                    list = new List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };

                uint newAddr = ItemClassListCore.ExpandClassList(rom, pointerAddr: 0, undo: undo);

                Assert.NotEqual(0u, newAddr);
                Assert.NotEqual(U.NOT_FOUND, newAddr);
                Assert.NotEqual(32u, newAddr); // must relocate

                // Confirm pointer was updated.
                uint newPtr = rom.u32(0);
                Assert.Equal(newAddr | 0x08000000u, newPtr);

                // Confirm new array has count 3 (old 2 + new placeholder) + terminator.
                // Placeholder MUST be non-zero so ScanClassList shows the row.
                Assert.Equal(0x10, rom.Data[newAddr + 0]);
                Assert.Equal(0x20, rom.Data[newAddr + 1]);
                Assert.Equal((byte)ItemClassListCore.NewSlotPlaceholder, rom.Data[newAddr + 2]); // new slot
                Assert.Equal(0x00, rom.Data[newAddr + 3]); // terminator

                // ScanClassList must report the new row (count = oldCount + 1).
                var scanned = ItemClassListCore.ScanClassList(rom, newAddr);
                Assert.Equal(3, scanned.Count);
                Assert.Equal(0x10u, scanned[0]);
                Assert.Equal(0x20u, scanned[1]);
                Assert.Equal(ItemClassListCore.NewSlotPlaceholder, scanned[2]);

                // PR #463 review fix: the old bytes must remain INTACT so any
                // other owners (shared effectiveness arrays) still work.
                Assert.Equal(0x10, rom.Data[32]);
                Assert.Equal(0x20, rom.Data[33]);
                Assert.Equal(0x00, rom.Data[34]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        [Fact]
        public void ExpandClassList_OnSharedArray_OtherOwnersUnchanged()
        {
            // PR #463 Copilot CLI review: when owner A expands its
            // (shared) array, owner B's scan must still see the ORIGINAL
            // class list — not the expanded one and not corrupted bytes.
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            data[96] = 0x42;
            data[97] = 0x00; // [0x42, 0]
            // Owner A at offset 0; owner B at offset 8.
            uint sharedPtr = 96u | 0x08000000u;
            for (int i = 0; i < 4; i++)
            {
                data[0 + i] = (byte)((sharedPtr >> (i * 8)) & 0xFF);
                data[8 + i] = (byte)((sharedPtr >> (i * 8)) & 0xFF);
            }

            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = new Undo.UndoData
                {
                    name = "test",
                    list = new List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };

                uint newAddrA = ItemClassListCore.ExpandClassList(rom, pointerAddr: 0, undo: undo);

                // Owner A scans the new array: [0x42, placeholder].
                var scanA = ItemClassListCore.ScanClassList(rom, newAddrA);
                Assert.Equal(2, scanA.Count);
                Assert.Equal(0x42u, scanA[0]);
                Assert.Equal(ItemClassListCore.NewSlotPlaceholder, scanA[1]);

                // Owner B's pointer is unchanged (still points to 96), and
                // its scan still shows the ORIGINAL single-class list.
                Assert.Equal(sharedPtr, rom.u32(8));
                var scanB = ItemClassListCore.ScanClassList(rom, 96);
                Assert.Single(scanB);
                Assert.Equal(0x42u, scanB[0]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        [Fact]
        public void ExpandClassList_PreservesSharedArrayForOtherOwners()
        {
            // Regression test for PR #463 review finding (Copilot bot):
            // ExpandClassList previously zeroed/0xFF-filled the old array
            // after relocating, which corrupted other items that shared the
            // same effectiveness pointer. After the fix, the old bytes stay
            // intact and the second owner continues to scan the original
            // array.
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            // Shared list at offset 64: [0x10, 0x20, 0x00]
            data[64] = 0x10;
            data[65] = 0x20;
            data[66] = 0x00;
            // Two owner pointers at offsets 0 and 8 both pointing to 64.
            uint sharedPtr = 64u | 0x08000000u;
            data[0] = (byte)(sharedPtr & 0xFF);
            data[1] = (byte)((sharedPtr >> 8) & 0xFF);
            data[2] = (byte)((sharedPtr >> 16) & 0xFF);
            data[3] = (byte)((sharedPtr >> 24) & 0xFF);
            data[8] = (byte)(sharedPtr & 0xFF);
            data[9] = (byte)((sharedPtr >> 8) & 0xFF);
            data[10] = (byte)((sharedPtr >> 16) & 0xFF);
            data[11] = (byte)((sharedPtr >> 24) & 0xFF);

            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = new Undo.UndoData
                {
                    name = "test",
                    list = new List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };

                uint newAddr = ItemClassListCore.ExpandClassList(rom, pointerAddr: 0, undo: undo);

                // Owner 1 moved to the new array.
                Assert.Equal(newAddr | 0x08000000u, rom.u32(0));
                // Owner 2 still points to the original 64.
                Assert.Equal(sharedPtr, rom.u32(8));
                // Original bytes preserved (would have been 0xFF if we cleared).
                Assert.Equal(0x10, rom.Data[64]);
                Assert.Equal(0x20, rom.Data[65]);
                Assert.Equal(0x00, rom.Data[66]);
                // ScanClassList from owner 2 still finds the original class list.
                var owner2List = ItemClassListCore.ScanClassList(rom, 64);
                Assert.Equal(2, owner2List.Count);
                Assert.Equal(0x10u, owner2List[0]);
                Assert.Equal(0x20u, owner2List[1]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        // ---------------------------------------------------------------------
        // MakeIndependentCopy — synthetic ROM
        // ---------------------------------------------------------------------

        [Fact]
        public void MakeIndependentCopy_DuplicatesArrayAndRepointsOwner()
        {
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            // Shared class list at offset 64: 0x10, 0x20, 0x30, 0x00
            data[64] = 0x10;
            data[65] = 0x20;
            data[66] = 0x30;
            data[67] = 0x00;
            // Owner pointer at offset 0
            uint sharedPtr = 64u | 0x08000000u;
            data[0] = (byte)(sharedPtr & 0xFF);
            data[1] = (byte)((sharedPtr >> 8) & 0xFF);
            data[2] = (byte)((sharedPtr >> 16) & 0xFF);
            data[3] = (byte)((sharedPtr >> 24) & 0xFF);
            // A second "owner" at offset 4 -> same shared array
            data[4] = (byte)(sharedPtr & 0xFF);
            data[5] = (byte)((sharedPtr >> 8) & 0xFF);
            data[6] = (byte)((sharedPtr >> 16) & 0xFF);
            data[7] = (byte)((sharedPtr >> 24) & 0xFF);

            var rom = MakeRom(data);
            var prevRomState = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = new Undo.UndoData
                {
                    name = "test",
                    list = new List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };

                uint newAddr = ItemClassListCore.MakeIndependentCopy(rom, sourceAddr: 64, ownerPointerAddr: 0, undo: undo);

                Assert.NotEqual(0u, newAddr);
                Assert.NotEqual(U.NOT_FOUND, newAddr);
                Assert.NotEqual(64u, newAddr);

                // Owner 1 now points to the new copy.
                uint owner1 = rom.u32(0);
                Assert.Equal(newAddr | 0x08000000u, owner1);

                // Owner 2 still points to the shared original.
                uint owner2 = rom.u32(4);
                Assert.Equal(sharedPtr, owner2);

                // New copy has the same bytes as the original.
                Assert.Equal(0x10, rom.Data[newAddr + 0]);
                Assert.Equal(0x20, rom.Data[newAddr + 1]);
                Assert.Equal(0x30, rom.Data[newAddr + 2]);
                Assert.Equal(0x00, rom.Data[newAddr + 3]);
            }
            finally
            {
                CoreState.ROM = prevRomState;
            }
        }

        // =====================================================================
        // SkillSystems "Effectiveness Rework" (4-byte entries) — issue #1175
        // =====================================================================

        // Build a 4-byte rework entry [0, coeff, classtype_lo, classtype_hi].
        static void WriteReworkEntryBytes(byte[] data, int off, byte coeff, ushort classType)
        {
            data[off + 0] = 0;
            data[off + 1] = coeff;
            data[off + 2] = (byte)(classType & 0xFF);
            data[off + 3] = (byte)((classType >> 8) & 0xFF);
        }

        [Fact]
        public void ScanReworkEntries_EmptyOnZeroAddress()
        {
            byte[] data = new byte[64];
            WriteReworkEntryBytes(data, 0, 6, 0x01);
            var rom = MakeRom(data);
            var list = ItemClassListCore.ScanReworkEntries(rom, baseAddr: 0);
            Assert.Empty(list);
        }

        [Fact]
        public void ScanReworkEntries_TerminatesOnU32Zero()
        {
            // Two valid 4-byte entries at offset 8, terminated by a u32==0.
            byte[] data = new byte[64];
            WriteReworkEntryBytes(data, 8, 6, 0x01);   // armor, coeff 6
            WriteReworkEntryBytes(data, 12, 4, 0x02);  // cavalry, coeff 4
            // data[16..19] stay 0 → terminator.
            WriteReworkEntryBytes(data, 20, 8, 0x04);  // beyond terminator — must NOT appear

            var rom = MakeRom(data);
            var list = ItemClassListCore.ScanReworkEntries(rom, baseAddr: 8);
            Assert.Equal(2, list.Count);
            Assert.Equal(8u, list[0].Addr);
            Assert.Equal(6u, list[0].Coefficient);
            Assert.Equal(0x01u, list[0].ClassType);
            Assert.Equal(12u, list[1].Addr);
            Assert.Equal(4u, list[1].Coefficient);
            Assert.Equal(0x02u, list[1].ClassType);
        }

        [Fact]
        public void WriteReworkEntry_UpdatesCoeffAndClassType_KeepsLeadingZero()
        {
            byte[] data = new byte[32];
            // Pre-seed a non-zero leading byte to prove WriteReworkEntry clears it.
            data[4] = 0x99;
            var rom = MakeRom(data);
            var prev = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = new Undo.UndoData
                {
                    name = "test",
                    list = new List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };
                ItemClassListCore.WriteReworkEntry(rom, addr: 4, coefficient: 6, classType: 0x21, undo: undo);

                Assert.Equal(0x00, rom.Data[4]);             // leading byte forced to 0
                Assert.Equal(0x06, rom.Data[5]);             // coefficient
                Assert.Equal(0x21u, rom.u16(6));             // class-type u16
                Assert.True(undo.list.Count >= 1);           // writes recorded
            }
            finally
            {
                CoreState.ROM = prev;
            }
        }

        [Fact]
        public void ExpandReworkList_AppendsEntryBeforeTerminator_PreservesOldBytes()
        {
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF; // free
            // One rework entry at offset 64 (armor coeff 6) + u32==0 terminator.
            WriteReworkEntryBytes(data, 64, 6, 0x01);
            data[68] = data[69] = data[70] = data[71] = 0; // terminator
            uint origPtr = 64u | 0x08000000u;
            for (int i = 0; i < 4; i++) data[i] = (byte)((origPtr >> (i * 8)) & 0xFF);

            var rom = MakeRom(data);
            var prev = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = new Undo.UndoData
                {
                    name = "test",
                    list = new List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };

                uint newBase = ItemClassListCore.ExpandReworkList(rom, pointerAddr: 0, undo: undo);
                Assert.NotEqual(U.NOT_FOUND, newBase);
                Assert.NotEqual(64u, newBase); // relocated

                // Pointer updated.
                Assert.Equal(newBase | 0x08000000u, rom.u32(0));

                // New array has 2 entries: the original armor + the appended one.
                var scanned = ItemClassListCore.ScanReworkEntries(rom, newBase);
                Assert.Equal(2, scanned.Count);
                Assert.Equal(0x01u, scanned[0].ClassType);
                Assert.Equal(ItemClassListCore.ReworkNewSlotClassType, scanned[1].ClassType);
                Assert.Equal(ItemClassListCore.ReworkNewSlotCoefficient, scanned[1].Coefficient);

                // Old bytes intact (shared-array safety).
                Assert.Equal(0x00, rom.Data[64]);
                Assert.Equal(0x06, rom.Data[65]);
                Assert.Equal(0x01, rom.Data[66]);
            }
            finally
            {
                CoreState.ROM = prev;
            }
        }

        [Fact]
        public void MakeIndependentReworkCopy_DuplicatesAndRepointsOwnerOnly()
        {
            byte[] data = new byte[1024];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            // Shared rework array at offset 96: armor + cavalry + terminator.
            WriteReworkEntryBytes(data, 96, 6, 0x01);
            WriteReworkEntryBytes(data, 100, 6, 0x02);
            data[104] = data[105] = data[106] = data[107] = 0; // terminator
            uint sharedPtr = 96u | 0x08000000u;
            for (int i = 0; i < 4; i++)
            {
                data[0 + i] = (byte)((sharedPtr >> (i * 8)) & 0xFF); // owner A
                data[8 + i] = (byte)((sharedPtr >> (i * 8)) & 0xFF); // owner B
            }

            var rom = MakeRom(data);
            var prev = CoreState.ROM;
            CoreState.ROM = rom;
            try
            {
                var undo = new Undo.UndoData
                {
                    name = "test",
                    list = new List<Undo.UndoPostion>(),
                    filesize = (uint)rom.Data.Length,
                };

                uint newBase = ItemClassListCore.MakeIndependentReworkCopy(rom, sourceAddr: 96, ownerPointerAddr: 0, undo: undo);
                Assert.NotEqual(U.NOT_FOUND, newBase);
                Assert.NotEqual(96u, newBase);

                // Owner A repointed; owner B still shares the original.
                Assert.Equal(newBase | 0x08000000u, rom.u32(0));
                Assert.Equal(sharedPtr, rom.u32(8));

                // New copy has the same two entries.
                var copy = ItemClassListCore.ScanReworkEntries(rom, newBase);
                Assert.Equal(2, copy.Count);
                Assert.Equal(0x01u, copy[0].ClassType);
                Assert.Equal(0x02u, copy[1].ClassType);
            }
            finally
            {
                CoreState.ROM = prev;
            }
        }

        [Theory]
        [InlineData(0x00u, "")]
        [InlineData(0x01u, "Armor")]
        [InlineData(0x02u, "Cavalry")]
        [InlineData(0x03u, "Armor,Cavalry")]
        [InlineData(0x24u, "Flying,Sword")] // 0x04 flying + 0x20 sword
        [InlineData(0xC0u, "Unknown1,Unknown2")]
        public void GetClassTypeNames_DecodesBitmask(uint classType, string expected)
        {
            Assert.Equal(expected, ItemClassListCore.GetClassTypeNames(classType));
        }

        // ---------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Create a minimal in-memory ROM wrapping a synthetic byte buffer for
        /// pointer-arithmetic tests. We bypass the regular .Load path so we can
        /// dodge Huffman / encoding / config-init costs the helper does not need.
        /// </summary>
        static ROM MakeRom(byte[] data)
        {
            var rom = new ROM();
            // ROM.Data is settable via reflection; the simpler way is to use the
            // public test surface — Data is a public property.
            typeof(ROM).GetProperty("Data")!.SetValue(rom, data);
            return rom;
        }
    }
}
