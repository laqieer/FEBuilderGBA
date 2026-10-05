using System;
using System.IO;
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
                if (!csaCreator)
                {
                    Array.Copy(installed.Data, 0x200000, installed.Data, 0x95D8F4, 16);
                    Array.Clear(installed.Data, 0x200000, 20);
                }
                byte[] before = (byte[])installed.Data.Clone();
                SetRom(installed);
                AssertEngine(csaCreator, U.NOT_FOUND);
                Assert.Equal(before, installed.Data);

                BitConverter.GetBytes(0x08100000u).CopyTo(installed.Data,
                    csaCreator ? 0x200010 : 0x95D904);
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

        [Fact]
        public void DetectorCache_FEditorInstallerSentinel_TransitionsToAllocated()
        {
            var romProperty = typeof(Program).GetProperty("ROM", BindingFlags.Public | BindingFlags.Static)!;
            ROM previousProgramRom = Program.ROM;
            ROM previousCoreRom = CoreState.ROM;
            try
            {
                ROM installed = MakeRom(true, false);
                Array.Copy(installed.Data, 0x200000, installed.Data, 0x95D8F4, 16);
                Array.Clear(installed.Data, 0x200000, 20);
                BitConverter.GetBytes(0x08000000u).CopyTo(installed.Data, 0x95D904);
                byte[] before = (byte[])installed.Data.Clone();
                SetRom(installed);

                Assert.Equal(ImageUtilMagic.magic_system_enum.FEDITOR_ADV,
                    ImageUtilMagic.SearchMagicSystem());
                Assert.Equal(0x95D904u, ImageUtilMagic.GetCSASpellTablePointer());
                Assert.Equal(U.NOT_FOUND, ImageUtilMagic.GetCSASpellTableAddr());
                Assert.Equal(before, installed.Data);

                BitConverter.GetBytes(0x08100000u).CopyTo(installed.Data, 0x95D904);
                ImageUtilMagic.ClearCache();
                Assert.Equal(ImageUtilMagic.magic_system_enum.FEDITOR_ADV,
                    ImageUtilMagic.SearchMagicSystem());
                Assert.Equal(0x95D904u, ImageUtilMagic.GetCSASpellTablePointer());
                Assert.Equal(0x100000u, ImageUtilMagic.GetCSASpellTableAddr());
            }
            finally
            {
                romProperty.SetValue(null, previousProgramRom);
                CoreState.ROM = previousCoreRom;
                ImageUtilMagic.ClearCache();
            }
        }

        [Fact]
        public void MaintainedFEditor_FirstRealAllocationRetainsSlotBeforeEarlierDecoy()
        {
            var programType = typeof(Program);
            var flags = BindingFlags.Public | BindingFlags.Static;
            var romProperty = programType.GetProperty("ROM", flags)!;
            var configProperty = programType.GetProperty("Config", flags)!;
            var undoProperty = programType.GetProperty("Undo", flags)!;
            var lintProperty = programType.GetProperty("LintCache", flags)!;
            var commentProperty = programType.GetProperty("CommentCache", flags)!;
            ROM previousRom = Program.ROM;
            ROM previousCoreRom = CoreState.ROM;
            string previousBaseDirectory = CoreState.BaseDirectory;
            var previousConfig = Program.Config;
            var previousUndo = Program.Undo;
            var previousLint = Program.LintCache;
            var previousComment = Program.CommentCache;
            try
            {
                ROM rom = MakeRom(true, false);
                byte[] blob = File.ReadAllBytes(Path.Combine(FindRepoRoot(),
                    "config", "patch2", "FE8U", "FEEditor", "Magic", "CSA System.dmp"));
                Assert.Equal(1088, blob.Length);
                Array.Copy(blob, 0, rom.Data, 0x95D780, blob.Length);
                Assert.Equal(0x08000000u, rom.u32(0x95D904));
                Assert.Equal(0u, rom.u32(0x200010));
                BitConverter.GetBytes(0x08700000u).CopyTo(rom.Data, 0x3000C);
                BitConverter.GetBytes(0x08030000u).CopyTo(rom.Data, 0x4000);
                for (int i = 0; i < 32; i++)
                    rom.Data[0x700000 + i] = (byte)(i + 1);
                CoreState.BaseDirectory = FindRepoRoot();
                SetRom(rom);
                configProperty.SetValue(null, new ConfigWinForms());
                undoProperty.SetValue(null, new Undo());
                lintProperty.SetValue(null, new EtcCache("lint_"));
                commentProperty.SetValue(null, new EtcCache("comment_"));

                Assert.Equal(ImageUtilMagic.magic_system_enum.FEDITOR_ADV,
                    ImageUtilMagic.SearchMagicSystem());
                Assert.Equal(0x95D904u, ImageUtilMagic.GetCSASpellTablePointer());
                Assert.Equal(U.NOT_FOUND, ImageUtilMagic.GetCSASpellTableAddr());
                byte[] beforeAllocation = (byte[])rom.Data.Clone();
                Undo.UndoData undo = Program.Undo.NewUndoData("test first CSA allocation");
                uint allocated = InputFormRef.ExpandsArea(null!, 254, 0x95D904, 0,
                    InputFormRef.ExpandsFillOption.NO, 20, undo);
                Assert.NotEqual(U.NOT_FOUND, allocated);
                Assert.Equal(0u, allocated % 4);
                Assert.True(U.isSafetyOffset(allocated + 254u * 20u, rom));
                Assert.Equal(0u, rom.u32(allocated + 253u * 20u));
                Assert.Equal(0xFFFFFFFFu, rom.u32(allocated + 254u * 20u));
                Assert.Equal(U.toPointer(allocated), rom.u32(0x95D904));
                Assert.Equal(0u, rom.u32(0x200010));
                Assert.Equal(0x08700000u, rom.u32(0x3000C));
                Assert.Equal(0x08030000u, rom.u32(0x4000));
                for (int i = 0; i < 32; i++)
                    Assert.Equal((byte)(i + 1), rom.Data[0x700000 + i]);
                Assert.NotEmpty(undo.list);
                Program.Undo.UndoBuffer.Add(undo);
                Assert.Equal(beforeAllocation,
                    Undo.RollbackMemoryData(Program.Undo, 0, rom.Data));

                ImageUtilMagic.ClearCache();
                Assert.Equal(ImageUtilMagic.magic_system_enum.FEDITOR_ADV,
                    ImageUtilMagic.SearchMagicSystem());
                Assert.Equal(0x95D904u, ImageUtilMagic.GetCSASpellTablePointer());
                Assert.Equal(allocated, ImageUtilMagic.GetCSASpellTableAddr());
                ImageUtilMagic.ClearCache();
                Assert.Equal(ImageUtilMagic.magic_system_enum.FEDITOR_ADV,
                    ImageUtilMagic.SearchMagicSystem());
                Assert.Equal(allocated, ImageUtilMagic.GetCSASpellTableAddr());
                Assert.Equal(0x95D904u, ImageUtilMagic.GetCSASpellTablePointer());
            }
            finally
            {
                romProperty.SetValue(null, previousRom);
                CoreState.ROM = previousCoreRom;
                CoreState.BaseDirectory = previousBaseDirectory;
                configProperty.SetValue(null, previousConfig);
                undoProperty.SetValue(null, previousUndo);
                lintProperty.SetValue(null, previousLint);
                commentProperty.SetValue(null, previousComment);
                ImageUtilMagic.ClearCache();
            }
        }

        static string FindRepoRoot()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory);
                 dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "FEBuilderGBA.sln")))
                    return dir.FullName;
            throw new DirectoryNotFoundException("FEBuilderGBA.sln not found");
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
            Assert.Equal(csaCreator ? 0x200010u : 0x95D904u,
                ImageUtilMagic.GetCSASpellTablePointer());
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
