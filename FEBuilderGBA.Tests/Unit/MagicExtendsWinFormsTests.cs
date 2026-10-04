using System;
using System.Reflection;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Tests.Unit
{
    [Collection("SharedState")]
    public class MagicExtendsWinFormsTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DetectorCache_UnpatchedThenInstalledThenAllocatedThenOtherRom(bool csaCreator)
        {
            var romProperty = typeof(Program).GetProperty("ROM", BindingFlags.Public | BindingFlags.Static)!;
            ROM previousProgramRom = Program.ROM;
            ROM previousCoreRom = CoreState.ROM;
            try
            {
                var blank = MakeRom(false, csaCreator);
                SetRom(blank);
                Assert.Equal(ImageUtilMagic.magic_system_enum.NO, ImageUtilMagic.SearchMagicSystem());
                Assert.Equal(U.NOT_FOUND, ImageUtilMagic.GetCSASpellTablePointer());

                var installed = MakeRom(true, csaCreator);
                byte[] before = (byte[])installed.Data.Clone();
                SetRom(installed);
                AssertEngine(csaCreator, U.NOT_FOUND);
                Assert.Equal(before, installed.Data);

                BitConverter.GetBytes(0x08100000u).CopyTo(installed.Data, 0x200010);
                ImageUtilMagic.ClearCache();
                AssertEngine(csaCreator, 0x100000u);

                SetRom(MakeRom(false, csaCreator));
                Assert.Equal(ImageUtilMagic.magic_system_enum.NO, ImageUtilMagic.SearchMagicSystem());
                Assert.Equal(U.NOT_FOUND, ImageUtilMagic.GetCSASpellTableAddr());
                Assert.Equal(U.NOT_FOUND, ImageUtilMagic.GetCSASpellTablePointer());
            }
            finally
            {
                romProperty.SetValue(null, previousProgramRom);
                CoreState.ROM = previousCoreRom;
                ImageUtilMagic.ClearCache();
            }
        }

        static void AssertEngine(bool csaCreator, uint expectedTable)
        {
            Assert.Equal(csaCreator
                ? ImageUtilMagic.magic_system_enum.CSA_CREATOR
                : ImageUtilMagic.magic_system_enum.FEDITOR_ADV,
                ImageUtilMagic.SearchMagicSystem(out uint b, out uint d, out uint n));
            Assert.Equal(0x95d780u, b);
            Assert.Equal(0x95d7edu, d);
            Assert.Equal(csaCreator ? 0x95d899u : 0x95d8efu, n);
            Assert.Equal(expectedTable, ImageUtilMagic.GetCSASpellTableAddr());
            Assert.Equal(0x200010u, ImageUtilMagic.GetCSASpellTablePointer());
        }

        static void SetRom(ROM rom)
        {
            typeof(Program).GetProperty("ROM", BindingFlags.Public | BindingFlags.Static)!
                .SetValue(null, rom);
            CoreState.ROM = rom;
            ImageUtilMagic.ClearCache();
        }

        static ROM MakeRom(bool installed, bool csaCreator)
        {
            var data = new byte[0x1000000];
            if (installed)
            {
                byte[] engine = { 0x01,0,0,0,0x90,0xD7,0x95,0x08,
                    0x03,0,0,0,0x39,0xD9,0x95,0x08 };
                if (csaCreator) { engine[12] = 0xD9; engine[13] = 0xD8; }
                Array.Copy(engine, 0, data, 0x95d780, engine.Length);
                byte[] signature = csaCreator
                    ? new byte[] { 0x1C,0x58,0x05,0x08,0,0x01,0,0x80,
                        0xED,0xD7,0x95,0x08,0x99,0xD8,0x95,0x08 }
                    : new byte[] { 0x01,0xB4,0x7D,0xE7,0x34,0xFF,0x03,0x02,
                        0x80,0xD7,0x95,0x08,0x1A,0xE1,0x03,0x02 };
                Array.Copy(signature, 0, data, 0x200000, signature.Length);
            }
            var rom = new ROM();
            rom.LoadLow("synthetic-fe8u.gba", data, "BE8E01");
            return rom;
        }
    }
}
