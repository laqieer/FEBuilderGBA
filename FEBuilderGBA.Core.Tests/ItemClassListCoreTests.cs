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
            byte[] data = new byte[64];
            data[0] = 0;
            var rom = MakeRom(data);
            var list = ItemClassListCore.ScanClassList(rom, baseAddr: 0);
            Assert.Empty(list);
        }

        [Fact]
        public void ScanClassList_TerminatesOnFirstZero()
        {
            byte[] data = new byte[64];
            data[0] = 0x10;
            data[1] = 0x20;
            data[2] = 0x30;
            data[3] = 0x00; // terminator
            data[4] = 0x40; // beyond terminator — must not be returned

            var rom = MakeRom(data);
            var list = ItemClassListCore.ScanClassList(rom, baseAddr: 0);
            Assert.Equal(3, list.Count);
            Assert.Equal(0x10u, list[0]);
            Assert.Equal(0x20u, list[1]);
            Assert.Equal(0x30u, list[2]);
        }

        [Fact]
        public void ScanClassList_HandlesBoundsSafely()
        {
            // Array that fills the entire ROM and never hits a terminator.
            byte[] data = new byte[8];
            for (int i = 0; i < data.Length; i++) data[i] = 0x42;
            var rom = MakeRom(data);
            var list = ItemClassListCore.ScanClassList(rom, baseAddr: 0);
            Assert.Equal(8, list.Count);
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

                // Confirm new array has count 3 (old 2 + new 0-slot) + terminator.
                Assert.Equal(0x10, rom.Data[newAddr + 0]);
                Assert.Equal(0x20, rom.Data[newAddr + 1]);
                Assert.Equal(0x00, rom.Data[newAddr + 2]); // new appended slot
                Assert.Equal(0x00, rom.Data[newAddr + 3]); // terminator
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
