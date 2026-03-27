using System.IO;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for ROM icon-index resolution patterns used by PreviewIconHelper.
    /// Verifies that class wait icon indices (offset +6) and item icon indices (offset +29)
    /// can be correctly read from ROM data. Real-ROM tests skip when FE8U.gba is unavailable.
    /// </summary>
    [Collection("SharedState")]
    public class IconIndexResolutionTests
    {
        private readonly ITestOutputHelper _output;

        public IconIndexResolutionTests(ITestOutputHelper output)
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

        /// <summary>
        /// Verify that class ID → wait icon index resolution works for FE8U class 1 (Lord Eirika).
        /// This tests the same ROM data access pattern used by PreviewIconHelper.LoadClassWaitIconByClassId.
        /// </summary>
        [Fact]
        public void FE8U_ClassId1_HasNonZeroWaitIconIndex()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint classPtr = rom.RomInfo.class_pointer;
                Assert.NotEqual(0u, classPtr);

                uint classBase = rom.p32(classPtr);
                Assert.True(U.isSafetyOffset(classBase));

                uint classSize = rom.RomInfo.class_datasize;
                Assert.True(classSize > 6, "class_datasize must be > 6 to read wait icon at offset +6");

                uint classAddr = classBase + 1 * classSize;
                Assert.True(classAddr + classSize <= (uint)rom.Data.Length);

                uint waitIconIndex = rom.u8(classAddr + 6);
                _output.WriteLine($"Class 1 (Lord Eirika) wait icon index: {waitIconIndex}");
                Assert.True(waitIconIndex > 0, "Class 1 should have a non-zero wait icon index");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Verify that item ID → icon index resolution works for FE8U item 1 (Iron Sword).
        /// This tests the same ROM data access pattern used by PreviewIconHelper.LoadItemIconByItemId.
        /// </summary>
        [Fact]
        public void FE8U_ItemId1_HasIconIndex()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint itemPtr = rom.RomInfo.item_pointer;
                Assert.NotEqual(0u, itemPtr);

                uint itemBase = rom.p32(itemPtr);
                Assert.True(U.isSafetyOffset(itemBase));

                uint itemSize = rom.RomInfo.item_datasize;
                Assert.True(itemSize > 29, "item_datasize must be > 29 to read icon at offset +29");

                uint itemAddr = itemBase + 1 * itemSize;
                Assert.True(itemAddr + itemSize <= (uint)rom.Data.Length);

                uint iconIndex = rom.u8(itemAddr + 29);
                _output.WriteLine($"Item 1 (Iron Sword) icon index: {iconIndex}");
                // Iron Sword should have a non-zero icon index
                Assert.True(iconIndex > 0, "Item 1 (Iron Sword) should have a non-zero icon index");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Verify class 0 has wait icon index 0 (null class entry).
        /// </summary>
        [Fact]
        public void FE8U_ClassId0_HasZeroWaitIconIndex()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint classBase = rom.p32(rom.RomInfo.class_pointer);
                uint classAddr = classBase + 0 * rom.RomInfo.class_datasize;
                uint waitIconIndex = rom.u8(classAddr + 6);

                _output.WriteLine($"Class 0 wait icon index: {waitIconIndex}");
                Assert.Equal(0u, waitIconIndex);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Verify out-of-range class ID would be caught by bounds check.
        /// </summary>
        [Fact]
        public void FE8U_ClassIdOutOfRange_BoundsCheckWorks()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint classBase = rom.p32(rom.RomInfo.class_pointer);
                uint classSize = rom.RomInfo.class_datasize;
                uint outOfRangeAddr = classBase + 0xFFFF * classSize;

                // Should exceed ROM data length
                Assert.True(outOfRangeAddr + classSize > (uint)rom.Data.Length,
                    "Out-of-range class address should exceed ROM bounds");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Verify that multiple class entries have valid wait icon indices.
        /// Tests the first 10 non-zero classes.
        /// </summary>
        [Fact]
        public void FE8U_MultipleClasses_HaveValidWaitIconIndices()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint classBase = rom.p32(rom.RomInfo.class_pointer);
                uint classSize = rom.RomInfo.class_datasize;

                int validCount = 0;
                for (uint classId = 1; classId <= 100 && classId * classSize + classBase < (uint)rom.Data.Length; classId++)
                {
                    uint addr = classBase + classId * classSize;
                    uint waitIconIndex = rom.u8(addr + 6);
                    if (waitIconIndex > 0)
                    {
                        validCount++;
                        _output.WriteLine($"Class {classId}: wait icon index = {waitIconIndex}");
                    }
                }

                _output.WriteLine($"Total classes with wait icons (of first 100): {validCount}");
                Assert.True(validCount > 10, "At least 10 classes should have non-zero wait icon indices");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Verify that multiple item entries have valid icon indices.
        /// </summary>
        [Fact]
        public void FE8U_MultipleItems_HaveValidIconIndices()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint itemBase = rom.p32(rom.RomInfo.item_pointer);
                uint itemSize = rom.RomInfo.item_datasize;

                int validCount = 0;
                for (uint itemId = 1; itemId <= 100 && itemId * itemSize + itemBase < (uint)rom.Data.Length; itemId++)
                {
                    uint addr = itemBase + itemId * itemSize;
                    uint iconIndex = rom.u8(addr + 29);
                    if (iconIndex > 0)
                    {
                        validCount++;
                    }
                }

                _output.WriteLine($"Total items with icons (of first 100): {validCount}");
                Assert.True(validCount > 10, "At least 10 items should have non-zero icon indices");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Verify the wait icon table pointer is valid and entries can be read.
        /// </summary>
        [Fact]
        public void FE8U_WaitIconTable_HasValidEntries()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint ptr = rom.RomInfo.unit_wait_icon_pointer;
                Assert.NotEqual(0u, ptr);

                uint baseAddr = rom.p32(ptr);
                Assert.True(U.isSafetyOffset(baseAddr), $"Invalid wait icon base: 0x{baseAddr:X}");

                // Check first few entries (each is 8 bytes)
                int validSprites = 0;
                for (uint i = 1; i < 50; i++)
                {
                    uint entryAddr = baseAddr + i * 8;
                    if (entryAddr + 8 > (uint)rom.Data.Length) break;

                    uint spriteGba = rom.u32(entryAddr + 4);
                    if (U.isPointer(spriteGba))
                        validSprites++;
                }

                _output.WriteLine($"Wait icon entries with valid sprite pointers: {validSprites}/49");
                Assert.True(validSprites > 5, "Should have multiple valid wait icon sprites");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Verify the move icon table pointer is valid and entries have valid data pointers.
        /// </summary>
        [Fact]
        public void FE8U_MoveIconTable_HasValidEntries()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint ptr = rom.RomInfo.unit_move_icon_pointer;
                Assert.NotEqual(0u, ptr);

                uint baseAddr = rom.p32(ptr);
                Assert.True(U.isSafetyOffset(baseAddr), $"Invalid move icon base: 0x{baseAddr:X}");

                int validSprites = 0;
                for (uint i = 0; i < 30; i++)
                {
                    uint entryAddr = baseAddr + i * 8;
                    if (entryAddr + 8 > (uint)rom.Data.Length) break;

                    uint picGba = rom.u32(entryAddr + 0);
                    if (U.isPointer(picGba))
                    {
                        validSprites++;
                        _output.WriteLine($"Move icon entry {i}: pic ptr 0x{picGba:X08}");
                    }
                }

                _output.WriteLine($"Move icon entries with valid pic pointers: {validSprites}/30");
                Assert.True(validSprites > 3, "Should have multiple valid move icon pic pointers");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Verify the battle animation table pointer is valid and records exist.
        /// </summary>
        [Fact]
        public void FE8U_BattleAnimeTable_HasValidEntries()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint pointer = rom.RomInfo.image_battle_animelist_pointer;
                Assert.NotEqual(0u, pointer);

                uint tableBase = rom.p32(pointer);
                Assert.True(U.isSafetyOffset(tableBase, rom), $"Invalid battle anime table base: 0x{tableBase:X}");

                // Check first 10 records (each is 32 bytes)
                int validRecords = 0;
                for (uint i = 0; i < 10; i++)
                {
                    uint addr = tableBase + i * 32;
                    if (addr + 32 > (uint)rom.Data.Length) break;

                    uint sectionRaw = rom.u32(addr + 12);
                    if (U.isPointer(sectionRaw))
                    {
                        validRecords++;
                        _output.WriteLine($"Anime record {i}: section ptr 0x{sectionRaw:X08}");
                    }
                }

                _output.WriteLine($"Battle anime records with valid section pointers: {validRecords}/10");
                Assert.True(validRecords > 2, "Should have multiple valid battle anime records");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Verify GBA BGR555 color decomposition matches expected values.
        /// Tests the bit-shifting pattern used by CreateColorSwatch.
        /// </summary>
        [Fact]
        public void GBA_BGR555_ColorDecomposition_IsCorrect()
        {
            // Pure red: r=31, g=0, b=0 -> 0x001F
            uint red = 0x001F;
            Assert.Equal(248, (int)((red & 0x1F) << 3));
            Assert.Equal(0, (int)(((red >> 5) & 0x1F) << 3));
            Assert.Equal(0, (int)(((red >> 10) & 0x1F) << 3));

            // Pure green: r=0, g=31, b=0 -> 0x03E0
            uint green = 0x03E0;
            Assert.Equal(0, (int)((green & 0x1F) << 3));
            Assert.Equal(248, (int)(((green >> 5) & 0x1F) << 3));
            Assert.Equal(0, (int)(((green >> 10) & 0x1F) << 3));

            // Pure blue: r=0, g=0, b=31 -> 0x7C00
            uint blue = 0x7C00;
            Assert.Equal(0, (int)((blue & 0x1F) << 3));
            Assert.Equal(0, (int)(((blue >> 5) & 0x1F) << 3));
            Assert.Equal(248, (int)(((blue >> 10) & 0x1F) << 3));

            // White: r=31, g=31, b=31 -> 0x7FFF
            uint white = 0x7FFF;
            Assert.Equal(248, (int)((white & 0x1F) << 3));
            Assert.Equal(248, (int)(((white >> 5) & 0x1F) << 3));
            Assert.Equal(248, (int)(((white >> 10) & 0x1F) << 3));
        }
    }
}
