using System.IO;
using System.Reflection;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Diagnostic tests that load a real FE8U ROM and compare ViewModel values
    /// against raw ROM bytes for multiple class entries.
    /// If these tests PASS, the bug is in the UI/presentation layer.
    /// If these tests FAIL, the bug is in LoadClass().
    /// </summary>
    [Collection("SharedState")]
    public class ClassEditorDiagnosticTests
    {
        private readonly ITestOutputHelper _output;

        public ClassEditorDiagnosticTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Find ROM file by walking up from test assembly directory.
        /// Returns null if ROM is not found.
        /// </summary>
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
        /// Load a real FE8U ROM and compare ViewModel values against raw ROM bytes
        /// for class index 1 (Lord - Eirika).
        /// </summary>
        [Fact]
        public void FE8U_RealRom_ClassIndex1_ViewModelMatchesRawBytes()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found in roms/ directory — test requires real ROM");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                // Load ROM
                var rom = new ROM();
                rom.Load(romPath!, out string version);
                CoreState.ROM = rom;

                Assert.NotNull(rom.RomInfo);
                Assert.Equal(84u, rom.RomInfo.class_datasize); // FE8 uses 84 bytes per class
                _output.WriteLine($"ROM version: {rom.RomInfo.version}, versionStr: {version}");

                // Get class base address
                uint classPtr = rom.RomInfo.class_pointer;
                Assert.NotEqual(0u, classPtr);
                uint classBase = rom.p32(classPtr);
                Assert.True(U.isSafetyOffset(classBase), $"Invalid class base addr: 0x{classBase:X08}");

                // Class index 1 address
                uint dataSize = rom.RomInfo.class_datasize;
                uint addr = classBase + 1 * dataSize;
                _output.WriteLine($"Class[1] addr: 0x{addr:X08}");

                // Load via ViewModel
                var vm = new ClassEditorViewModel();
                vm.LoadClass(addr);

                // Now compare every field against raw ROM bytes
                // Common fields (same offset for all versions)
                Assert.Equal(rom.u16(addr + 0), vm.NameId);    // W0: Name ID
                Assert.Equal(rom.u16(addr + 2), vm.DescId);    // W2: Desc ID
                Assert.Equal(rom.u8(addr + 4), vm.ClassNumber); // B4: Class Number
                Assert.Equal(rom.u8(addr + 5), vm.PromotionLevel); // B5: Promotion Level
                Assert.Equal(rom.u8(addr + 6), vm.WaitIcon);   // B6: Wait Icon
                Assert.Equal(rom.u8(addr + 7), vm.WalkSpeed);  // B7: Walk Speed
                Assert.Equal(rom.u16(addr + 8), vm.PortraitId); // W8: Portrait ID
                Assert.Equal(rom.u8(addr + 10), vm.SortOrder); // B10: Sort Order

                // Base Stats
                Assert.Equal(rom.u8(addr + 11), vm.BaseHp);    // B11: HP
                Assert.Equal(rom.u8(addr + 12), vm.BaseStr);   // B12: Str
                Assert.Equal(rom.u8(addr + 13), vm.BaseSkl);   // B13: Skl
                Assert.Equal(rom.u8(addr + 14), vm.BaseSpd);   // B14: Spd
                Assert.Equal(rom.u8(addr + 15), vm.BaseDef);   // B15: Def
                Assert.Equal(rom.u8(addr + 16), vm.BaseRes);   // B16: Res
                Assert.Equal(rom.u8(addr + 17), vm.BaseCon);   // B17: Con
                Assert.Equal(rom.u8(addr + 18), vm.BaseMov);   // B18: Mov

                // Stat caps (max values)
                Assert.Equal(rom.u8(addr + 19), vm.MaxHp);     // B19: Max HP
                Assert.Equal(rom.u8(addr + 20), vm.MaxStr);    // B20: Max Str
                Assert.Equal(rom.u8(addr + 21), vm.MaxSkl);    // B21: Max Skl
                Assert.Equal(rom.u8(addr + 22), vm.MaxSpd);    // B22: Max Spd
                Assert.Equal(rom.u8(addr + 23), vm.MaxDef);    // B23: Max Def
                Assert.Equal(rom.u8(addr + 24), vm.MaxRes);    // B24: Max Res
                Assert.Equal(rom.u8(addr + 25), vm.MaxCon);    // B25: Max Con
                Assert.Equal(rom.u8(addr + 26), vm.ClassPower); // B26: Class Power

                // Growth rates
                Assert.Equal(rom.u8(addr + 27), vm.GrowHp);    // B27
                Assert.Equal(rom.u8(addr + 28), vm.GrowStr);   // B28
                Assert.Equal(rom.u8(addr + 29), vm.GrowSkl);   // B29
                Assert.Equal(rom.u8(addr + 30), vm.GrowSpd);   // B30
                Assert.Equal(rom.u8(addr + 31), vm.GrowDef);   // B31
                Assert.Equal(rom.u8(addr + 32), vm.GrowRes);   // B32
                Assert.Equal(rom.u8(addr + 33), vm.GrowLck);   // B33

                // Promotion gains (signed bytes) -- FE8 uses b34-b39
                Assert.Equal((int)(sbyte)rom.u8(addr + 34), vm.PromoHp);  // b34
                Assert.Equal((int)(sbyte)rom.u8(addr + 35), vm.PromoStr); // b35
                Assert.Equal((int)(sbyte)rom.u8(addr + 36), vm.PromoSkl); // b36
                Assert.Equal((int)(sbyte)rom.u8(addr + 37), vm.PromoSpd); // b37
                Assert.Equal((int)(sbyte)rom.u8(addr + 38), vm.PromoDef); // b38
                Assert.Equal((int)(sbyte)rom.u8(addr + 39), vm.PromoRes); // b39

                // Ability flags -- FE8 at +40..+43
                Assert.Equal(rom.u8(addr + 40), vm.Ability1);   // B40
                Assert.Equal(rom.u8(addr + 41), vm.Ability2);   // B41
                Assert.Equal(rom.u8(addr + 42), vm.Ability3);   // B42
                Assert.Equal(rom.u8(addr + 43), vm.Ability4);   // B43

                // Weapon rank levels -- FE8 at +44..+51
                Assert.Equal(rom.u8(addr + 44), vm.WepRankSword); // B44
                Assert.Equal(rom.u8(addr + 45), vm.WepRankLance); // B45
                Assert.Equal(rom.u8(addr + 46), vm.WepRankAxe);   // B46
                Assert.Equal(rom.u8(addr + 47), vm.WepRankBow);   // B47
                Assert.Equal(rom.u8(addr + 48), vm.WepRankStaff); // B48
                Assert.Equal(rom.u8(addr + 49), vm.WepRankAnima); // B49
                Assert.Equal(rom.u8(addr + 50), vm.WepRankLight); // B50
                Assert.Equal(rom.u8(addr + 51), vm.WepRankDark);  // B51

                // Pointers -- FE8 offsets
                Assert.Equal(rom.u32(addr + 52), vm.BattleAnimePtr);    // P52
                Assert.Equal(rom.u32(addr + 56), vm.MoveCostPtr);       // P56
                Assert.Equal(rom.u32(addr + 60), vm.MoveCostRainPtr);   // P60
                Assert.Equal(rom.u32(addr + 64), vm.MoveCostSnowPtr);   // P64
                Assert.Equal(rom.u32(addr + 68), vm.TerrainAvoidPtr);   // P68
                Assert.Equal(rom.u32(addr + 72), vm.TerrainDefPtr);     // P72
                Assert.Equal(rom.u32(addr + 76), vm.TerrainResPtr);     // P76
                Assert.Equal(rom.u32(addr + 80), vm.UnknownD80);        // D80

                // Print all values for diagnostic
                _output.WriteLine($"NameId=0x{vm.NameId:X04}  DescId=0x{vm.DescId:X04}");
                _output.WriteLine($"ClassNumber={vm.ClassNumber}  PromoLevel={vm.PromotionLevel}");
                _output.WriteLine($"HP={vm.BaseHp} Str={vm.BaseStr} Skl={vm.BaseSkl} Spd={vm.BaseSpd} Def={vm.BaseDef} Res={vm.BaseRes}");
                _output.WriteLine($"Con={vm.BaseCon} Mov={vm.BaseMov} MaxHp={vm.MaxHp}");
                _output.WriteLine($"PromoHp={vm.PromoHp} PromoStr={vm.PromoStr} PromoSkl={vm.PromoSkl} PromoSpd={vm.PromoSpd} PromoDef={vm.PromoDef} PromoRes={vm.PromoRes}");
                _output.WriteLine($"Ability: {vm.Ability1:X02} {vm.Ability2:X02} {vm.Ability3:X02} {vm.Ability4:X02}");
                _output.WriteLine($"WepRank: Sword={vm.WepRankSword} Lance={vm.WepRankLance} Axe={vm.WepRankAxe} Bow={vm.WepRankBow}");
                _output.WriteLine($"BattleAnimePtr=0x{vm.BattleAnimePtr:X08}  MoveCost=0x{vm.MoveCostPtr:X08}");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Test multiple class indices to ensure consistency across entries.
        /// </summary>
        [Theory]
        [InlineData(0)]  // Index 0 (null/unused class)
        [InlineData(1)]  // Lord (Eirika)
        [InlineData(2)]  // Lord (Ephraim)
        [InlineData(10)] // Higher class
        [InlineData(50)] // Mid-range class
        public void FE8U_RealRom_MultipleClasses_ViewModelMatchesRawBytes(int classIndex)
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found in roms/ directory — test requires real ROM");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                uint classPtr = rom.RomInfo.class_pointer;
                uint classBase = rom.p32(classPtr);
                uint dataSize = rom.RomInfo.class_datasize;
                uint addr = (uint)(classBase + classIndex * dataSize);

                if (addr + dataSize > (uint)rom.Data.Length)
                {
                    _output.WriteLine($"SKIP: Class index {classIndex} out of range");
                    return;
                }

                _output.WriteLine($"Testing class index {classIndex} at addr 0x{addr:X08}");

                var vm = new ClassEditorViewModel();
                vm.LoadClass(addr);

                // Verify all fields match raw ROM bytes
                Assert.Equal(rom.u16(addr + 0), vm.NameId);
                Assert.Equal(rom.u16(addr + 2), vm.DescId);
                Assert.Equal(rom.u8(addr + 4), vm.ClassNumber);
                Assert.Equal(rom.u8(addr + 5), vm.PromotionLevel);
                Assert.Equal(rom.u8(addr + 6), vm.WaitIcon);
                Assert.Equal(rom.u8(addr + 7), vm.WalkSpeed);
                Assert.Equal(rom.u16(addr + 8), vm.PortraitId);
                Assert.Equal(rom.u8(addr + 10), vm.SortOrder);
                Assert.Equal(rom.u8(addr + 11), vm.BaseHp);
                Assert.Equal(rom.u8(addr + 12), vm.BaseStr);
                Assert.Equal(rom.u8(addr + 13), vm.BaseSkl);
                Assert.Equal(rom.u8(addr + 14), vm.BaseSpd);
                Assert.Equal(rom.u8(addr + 15), vm.BaseDef);
                Assert.Equal(rom.u8(addr + 16), vm.BaseRes);
                Assert.Equal(rom.u8(addr + 17), vm.BaseCon);
                Assert.Equal(rom.u8(addr + 18), vm.BaseMov);
                Assert.Equal(rom.u8(addr + 19), vm.MaxHp);
                Assert.Equal(rom.u8(addr + 20), vm.MaxStr);
                Assert.Equal(rom.u8(addr + 21), vm.MaxSkl);
                Assert.Equal(rom.u8(addr + 22), vm.MaxSpd);
                Assert.Equal(rom.u8(addr + 23), vm.MaxDef);
                Assert.Equal(rom.u8(addr + 24), vm.MaxRes);
                Assert.Equal(rom.u8(addr + 25), vm.MaxCon);
                Assert.Equal(rom.u8(addr + 26), vm.ClassPower);
                Assert.Equal(rom.u8(addr + 27), vm.GrowHp);
                Assert.Equal(rom.u8(addr + 28), vm.GrowStr);
                Assert.Equal(rom.u8(addr + 29), vm.GrowSkl);
                Assert.Equal(rom.u8(addr + 30), vm.GrowSpd);
                Assert.Equal(rom.u8(addr + 31), vm.GrowDef);
                Assert.Equal(rom.u8(addr + 32), vm.GrowRes);
                Assert.Equal(rom.u8(addr + 33), vm.GrowLck);
                Assert.Equal((int)(sbyte)rom.u8(addr + 34), vm.PromoHp);
                Assert.Equal((int)(sbyte)rom.u8(addr + 35), vm.PromoStr);
                Assert.Equal((int)(sbyte)rom.u8(addr + 36), vm.PromoSkl);
                Assert.Equal((int)(sbyte)rom.u8(addr + 37), vm.PromoSpd);
                Assert.Equal((int)(sbyte)rom.u8(addr + 38), vm.PromoDef);
                Assert.Equal((int)(sbyte)rom.u8(addr + 39), vm.PromoRes);
                Assert.Equal(rom.u8(addr + 40), vm.Ability1);
                Assert.Equal(rom.u8(addr + 41), vm.Ability2);
                Assert.Equal(rom.u8(addr + 42), vm.Ability3);
                Assert.Equal(rom.u8(addr + 43), vm.Ability4);
                Assert.Equal(rom.u8(addr + 44), vm.WepRankSword);
                Assert.Equal(rom.u8(addr + 45), vm.WepRankLance);
                Assert.Equal(rom.u8(addr + 46), vm.WepRankAxe);
                Assert.Equal(rom.u8(addr + 47), vm.WepRankBow);
                Assert.Equal(rom.u8(addr + 48), vm.WepRankStaff);
                Assert.Equal(rom.u8(addr + 49), vm.WepRankAnima);
                Assert.Equal(rom.u8(addr + 50), vm.WepRankLight);
                Assert.Equal(rom.u8(addr + 51), vm.WepRankDark);
                Assert.Equal(rom.u32(addr + 52), vm.BattleAnimePtr);
                Assert.Equal(rom.u32(addr + 56), vm.MoveCostPtr);
                Assert.Equal(rom.u32(addr + 60), vm.MoveCostRainPtr);
                Assert.Equal(rom.u32(addr + 64), vm.MoveCostSnowPtr);
                Assert.Equal(rom.u32(addr + 68), vm.TerrainAvoidPtr);
                Assert.Equal(rom.u32(addr + 72), vm.TerrainDefPtr);
                Assert.Equal(rom.u32(addr + 76), vm.TerrainResPtr);
                Assert.Equal(rom.u32(addr + 80), vm.UnknownD80);

                _output.WriteLine($"  NameId=0x{vm.NameId:X04} HP={vm.BaseHp} Str={vm.BaseStr} Skl={vm.BaseSkl} Spd={vm.BaseSpd}");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>
        /// Compare LoadClassList() addresses against manual calculation from class_pointer.
        /// Ensures ViewModel and WinForms InputFormRef would use the same base addresses.
        /// </summary>
        [Fact]
        public void FE8U_RealRom_LoadClassList_AddressesMatchManualCalculation()
        {
            string? romPath = FindRom("FE8U.gba");
            if (romPath == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found in roms/ directory — test requires real ROM");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.Load(romPath!, out _);
                CoreState.ROM = rom;

                var vm = new ClassEditorViewModel();
                var list = vm.LoadClassList();

                uint classPtr = rom.RomInfo.class_pointer;
                uint classBase = rom.p32(classPtr);
                uint dataSize = rom.RomInfo.class_datasize;

                _output.WriteLine($"ClassPointer=0x{classPtr:X08}, ClassBase=0x{classBase:X08}, DataSize={dataSize}");
                _output.WriteLine($"List count: {list.Count}");

                // Verify list is non-empty
                Assert.NotEmpty(list);

                // Verify each entry's address matches manual calculation
                for (int i = 0; i < list.Count && i < 10; i++)
                {
                    uint expected = (uint)(classBase + i * dataSize);
                    uint actual = list[i].addr;
                    _output.WriteLine($"  [{i}] expected=0x{expected:X08}  actual=0x{actual:X08}  name={list[i].name}");
                    Assert.Equal(expected, actual);
                }
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }
    }
}
