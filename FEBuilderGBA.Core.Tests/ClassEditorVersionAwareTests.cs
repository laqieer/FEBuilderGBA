using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests that ClassEditorViewModel correctly reads version-specific offsets.
    /// FE6 uses a 72-byte class struct with different field positions from FE7/8 (84 bytes).
    /// Key differences starting at offset 36:
    ///   FE6:  B36-B39 = ability flags, B40-B47 = weapon ranks, P48 = battle anim
    ///   FE7/8: b36-b39 = stat caps, B40-B43 = ability flags, B44-B51 = weapon ranks, P52 = battle anim
    /// </summary>
    [Collection("SharedState")]
    public class ClassEditorVersionAwareTests
    {
        private static ROM MakeRomFE6()
        {
            var rom = new ROM();
            var data = new byte[0x1000_0000]; // 16MB minimum for FE6 (requires >= 0x800000)
            rom.LoadLow("fake.gba", data, "AFEJ01"); // FE6 JP version string
            return rom;
        }

        private static ROM MakeRomFE8U()
        {
            var rom = new ROM();
            var data = new byte[0x1000_0000]; // 16MB minimum for FE8
            rom.LoadLow("fake.gba", data, "BE8E01"); // FE8 US version string
            return rom;
        }

        private static ROM MakeRomFE7U()
        {
            var rom = new ROM();
            var data = new byte[0x1000_0000]; // 16MB minimum for FE7
            rom.LoadLow("fake.gba", data, "AE7E01"); // FE7 US version string
            return rom;
        }

        /// <summary>
        /// Plant known bytes at the correct FE6 offsets and verify LoadClass reads them correctly.
        /// FE6 class struct layout:
        ///   W0=name, W2=desc, B4-B10=identity, B11-B18=stats, B19=extra,
        ///   B20-B26=weapon prof, B27-B33=growths, b34-b35=stat caps,
        ///   B36-B39=ability flags, B40-B47=weapon ranks, P48=battle anim,
        ///   P52/P56/P60/P64=move costs, D68=unknown
        /// </summary>
        [Fact]
        public void FE6_LoadClass_ReadsAbilityFlagsAt36()
        {
            var rom = MakeRomFE6();
            CoreState.ROM = rom;

            // Place a class entry at a known address (use something in range)
            uint classBase = 0x1000;
            // We need class_pointer to be set. FE6's class_pointer points to a location
            // that stores the class base address. Plant the pointer.
            uint classPointerAddr = rom.RomInfo.class_pointer;
            if (classPointerAddr == 0 || !U.isSafetyOffset(classPointerAddr))
            {
                // Skip test if ROM info doesn't have a usable pointer
                return;
            }

            // Write a GBA pointer at class_pointer that points to classBase
            rom.write_u32(classPointerAddr, classBase + 0x08000000);

            // Write test data at classBase:
            uint addr = classBase;

            // Name ID (W0) = 0x1234
            rom.write_u16(addr + 0, 0x1234);
            // Desc ID (W2) = 0x5678
            rom.write_u16(addr + 2, 0x5678);
            // Class number (B4) = 0x01
            rom.write_u8(addr + 4, 0x01);

            // Base stats at B11-B16
            rom.write_u8(addr + 11, 20); // HP
            rom.write_u8(addr + 12, 5);  // Str
            rom.write_u8(addr + 17, 6);  // Mov

            // FE6 ability flags at +36..+39
            rom.write_u8(addr + 36, 0xAA); // Ability1
            rom.write_u8(addr + 37, 0xBB); // Ability2
            rom.write_u8(addr + 38, 0xCC); // Ability3
            rom.write_u8(addr + 39, 0xDD); // Ability4

            // FE6 weapon ranks at +40..+47
            rom.write_u8(addr + 40, 101); // Sword rank
            rom.write_u8(addr + 41, 102); // Lance rank
            rom.write_u8(addr + 42, 103); // Axe rank

            // FE6 battle anime pointer at +48
            rom.write_u32(addr + 48, 0x08ABCDEF);

            // FE6 move cost at +52
            rom.write_u32(addr + 52, 0x08112233);

            var vm = new ClassEditorViewModel();
            vm.LoadClass(addr);

            // Verify ability flags read from FE6 offsets
            Assert.Equal(0xAAu, vm.Ability1);
            Assert.Equal(0xBBu, vm.Ability2);
            Assert.Equal(0xCCu, vm.Ability3);
            Assert.Equal(0xDDu, vm.Ability4);

            // Verify weapon ranks read from FE6 offsets
            Assert.Equal(101u, vm.WepRankSword);
            Assert.Equal(102u, vm.WepRankLance);
            Assert.Equal(103u, vm.WepRankAxe);

            // Verify battle anime pointer
            Assert.Equal(0x08ABCDEFu, vm.BattleAnimePtr);

            // Verify move cost pointer
            Assert.Equal(0x08112233u, vm.MoveCostPtr);

            // Verify base stats
            Assert.Equal(20u, vm.BaseHp);
            Assert.Equal(5u, vm.BaseStr);
            Assert.Equal(6u, vm.Mov);

            // FE6 has no stat caps for Skl/Spd/Def/Res in class struct
            Assert.Equal(0, vm.CapSkl);
            Assert.Equal(0, vm.CapSpd);
            Assert.Equal(0, vm.CapDef);
            Assert.Equal(0, vm.CapRes);
        }

        /// <summary>
        /// Plant known bytes at FE8 offsets and verify LoadClass reads them correctly.
        /// </summary>
        [Fact]
        public void FE8_LoadClass_ReadsAbilityFlagsAt40()
        {
            var rom = MakeRomFE8U();
            CoreState.ROM = rom;

            uint classPointerAddr = rom.RomInfo.class_pointer;
            if (classPointerAddr == 0 || !U.isSafetyOffset(classPointerAddr))
                return;

            uint classBase = 0x1000;
            rom.write_u32(classPointerAddr, classBase + 0x08000000);

            uint addr = classBase;
            rom.write_u8(addr + 4, 0x01); // Class number

            // FE8 stat caps at b36-b39
            rom.write_u8(addr + 36, 0x03); // CapSkl
            rom.write_u8(addr + 37, 0x04); // CapSpd
            rom.write_u8(addr + 38, 0x05); // CapDef
            rom.write_u8(addr + 39, 0x06); // CapRes

            // FE8 ability flags at +40..+43
            rom.write_u8(addr + 40, 0x11);
            rom.write_u8(addr + 41, 0x22);
            rom.write_u8(addr + 42, 0x33);
            rom.write_u8(addr + 43, 0x44);

            // FE8 weapon ranks at +44..+51
            rom.write_u8(addr + 44, 201); // Sword
            rom.write_u8(addr + 45, 202); // Lance

            // FE8 battle anime at +52
            rom.write_u32(addr + 52, 0x08FEDCBA);

            // FE8 terrain avoid at +68
            rom.write_u32(addr + 68, 0x08AABBCC);

            var vm = new ClassEditorViewModel();
            vm.LoadClass(addr);

            // FE8 stat caps at b36-b39
            Assert.Equal(3, vm.CapSkl);
            Assert.Equal(4, vm.CapSpd);
            Assert.Equal(5, vm.CapDef);
            Assert.Equal(6, vm.CapRes);

            // FE8 ability flags at +40
            Assert.Equal(0x11u, vm.Ability1);
            Assert.Equal(0x22u, vm.Ability2);
            Assert.Equal(0x33u, vm.Ability3);
            Assert.Equal(0x44u, vm.Ability4);

            // FE8 weapon ranks at +44
            Assert.Equal(201u, vm.WepRankSword);
            Assert.Equal(202u, vm.WepRankLance);

            // FE8 battle anime at +52
            Assert.Equal(0x08FEDCBAu, vm.BattleAnimePtr);

            // FE8 terrain avoid at +68
            Assert.Equal(0x08AABBCCu, vm.TerrainAvoidPtr);
        }

        /// <summary>
        /// Write class data for FE6 and verify it goes to the correct offsets.
        /// </summary>
        [Fact]
        public void FE6_WriteClass_WritesAbilityFlagsAt36()
        {
            var rom = MakeRomFE6();
            CoreState.ROM = rom;

            uint classPointerAddr = rom.RomInfo.class_pointer;
            if (classPointerAddr == 0 || !U.isSafetyOffset(classPointerAddr))
                return;

            uint classBase = 0x1000;
            rom.write_u32(classPointerAddr, classBase + 0x08000000);

            uint addr = classBase;
            rom.write_u8(addr + 4, 0x01); // Class number

            var vm = new ClassEditorViewModel();
            vm.LoadClass(addr);

            // Set values
            vm.Ability1 = 0xAA;
            vm.Ability2 = 0xBB;
            vm.Ability3 = 0xCC;
            vm.Ability4 = 0xDD;
            vm.WepRankSword = 150;
            vm.BattleAnimePtr = 0x08ABCDEF;

            vm.WriteClass();

            // Verify FE6 offsets
            Assert.Equal(0xAAu, rom.u8(addr + 36));  // Ability1 at +36
            Assert.Equal(0xBBu, rom.u8(addr + 37));  // Ability2 at +37
            Assert.Equal(0xCCu, rom.u8(addr + 38));  // Ability3 at +38
            Assert.Equal(0xDDu, rom.u8(addr + 39));  // Ability4 at +39
            Assert.Equal(150u, rom.u8(addr + 40));    // WepRankSword at +40
            Assert.Equal(0x08ABCDEFu, rom.u32(addr + 48)); // BattleAnime at +48
        }

        /// <summary>
        /// Write class data for FE8 and verify it goes to the correct offsets.
        /// </summary>
        [Fact]
        public void FE8_WriteClass_WritesAbilityFlagsAt40()
        {
            var rom = MakeRomFE8U();
            CoreState.ROM = rom;

            uint classPointerAddr = rom.RomInfo.class_pointer;
            if (classPointerAddr == 0 || !U.isSafetyOffset(classPointerAddr))
                return;

            uint classBase = 0x1000;
            rom.write_u32(classPointerAddr, classBase + 0x08000000);

            uint addr = classBase;
            rom.write_u8(addr + 4, 0x01);

            var vm = new ClassEditorViewModel();
            vm.LoadClass(addr);

            vm.Ability1 = 0x11;
            vm.Ability2 = 0x22;
            vm.WepRankSword = 200;
            vm.BattleAnimePtr = 0x08FEDCBA;
            vm.TerrainAvoidPtr = 0x08AABBCC;

            vm.WriteClass();

            // Verify FE8 offsets
            Assert.Equal(0x11u, rom.u8(addr + 40));   // Ability1 at +40
            Assert.Equal(0x22u, rom.u8(addr + 41));   // Ability2 at +41
            Assert.Equal(200u, rom.u8(addr + 44));     // WepRankSword at +44
            Assert.Equal(0x08FEDCBAu, rom.u32(addr + 52)); // BattleAnime at +52
            Assert.Equal(0x08AABBCCu, rom.u32(addr + 68)); // TerrainAvoid at +68
        }

        [Fact]
        public void FE6_IsFE6_IsTrue()
        {
            var rom = MakeRomFE6();
            CoreState.ROM = rom;
            var vm = new ClassEditorViewModel();
            Assert.True(vm.IsFE6);
        }

        [Fact]
        public void FE8_IsFE6_IsFalse()
        {
            var rom = MakeRomFE8U();
            CoreState.ROM = rom;
            var vm = new ClassEditorViewModel();
            Assert.False(vm.IsFE6);
        }

        [Fact]
        public void FE7_IsFE6_IsFalse()
        {
            var rom = MakeRomFE7U();
            CoreState.ROM = rom;
            var vm = new ClassEditorViewModel();
            Assert.False(vm.IsFE6);
        }
    }
}
