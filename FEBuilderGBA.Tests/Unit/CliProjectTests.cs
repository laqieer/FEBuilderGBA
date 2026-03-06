using Xunit;
using CliProgram = FEBuilderGBA.CLI.Program;
using FEBuilderGBA.CLI;

namespace FEBuilderGBA.Tests.Unit
{
    public class CliProjectTests
    {
        [Fact]
        public void ParseArgs_VersionFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--version" });
            Assert.True(dic.ContainsKey("--version"));
        }

        [Fact]
        public void ParseArgs_HelpShortFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "-h" });
            Assert.True(dic.ContainsKey("--help"));
        }

        [Fact]
        public void ParseArgs_KeyValue()
        {
            var dic = CliProgram.ParseArgs(new[] { "--rom=/path/to/rom.gba" });
            Assert.Equal("/path/to/rom.gba", dic["--rom"]);
        }

        [Fact]
        public void ParseArgs_MultipleArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--rom=/path/rom.gba", "--makeups=/out.ups", "--fromrom=/clean.gba" });
            Assert.Equal("/path/rom.gba", dic["--rom"]);
            Assert.Equal("/out.ups", dic["--makeups"]);
            Assert.Equal("/clean.gba", dic["--fromrom"]);
        }

        [Fact]
        public void ParseArgs_EmptyArgs()
        {
            var dic = CliProgram.ParseArgs(System.Array.Empty<string>());
            Assert.Empty(dic);
        }

        [Fact]
        public void CliAppServices_ShowError_DoesNotThrow()
        {
            var svc = new CliAppServices();
            svc.ShowError("test error");
        }

        [Fact]
        public void CliAppServices_ShowInfo_DoesNotThrow()
        {
            var svc = new CliAppServices();
            svc.ShowInfo("test info");
        }

        [Fact]
        public void CliAppServices_IsMainThread_ReturnsTrue()
        {
            var svc = new CliAppServices();
            Assert.True(svc.IsMainThread());
        }

        [Fact]
        public void CliAppServices_RunOnUIThread_ExecutesAction()
        {
            var svc = new CliAppServices();
            bool executed = false;
            svc.RunOnUIThread(() => executed = true);
            Assert.True(executed);
        }

        [Fact]
        public void RomLoader_InitEnvironment_RequiresBaseDirectory()
        {
            var origBase = FEBuilderGBA.CoreState.BaseDirectory;
            try
            {
                FEBuilderGBA.CoreState.BaseDirectory = null;
                Assert.Throws<System.InvalidOperationException>(() => RomLoader.InitEnvironment());
            }
            finally
            {
                FEBuilderGBA.CoreState.BaseDirectory = origBase;
            }
        }

        [Fact]
        public void ParseArgs_MakeUpsWithAllRequiredArgs()
        {
            var dic = CliProgram.ParseArgs(new[] { "--makeups=out.ups", "--rom=modified.gba", "--fromrom=original.gba" });
            Assert.Equal("out.ups", dic["--makeups"]);
            Assert.Equal("modified.gba", dic["--rom"]);
            Assert.Equal("original.gba", dic["--fromrom"]);
        }

        // ------------------------------------------------------------------ CLI command source verification

        [Fact]
        public void CliProgram_HasDecreaseColorCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--decreasecolor", src);
            Assert.Contains("RunDecreaseColor", src);
        }

        [Fact]
        public void CliProgram_HasPointerCalcCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--pointercalc", src);
            Assert.Contains("RunPointerCalc", src);
        }

        [Fact]
        public void CliProgram_HasRebuildCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--rebuild", src);
            Assert.Contains("RunRebuild", src);
        }

        [Fact]
        public void CliProgram_HasSongExchangeCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--songexchange", src);
            Assert.Contains("RunSongExchange", src);
        }

        [Fact]
        public void CliProgram_HasConvertMap1PictureCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--convertmap1picture", src);
            Assert.Contains("RunConvertMap1Picture", src);
        }

        [Fact]
        public void CliProgram_HasTranslateCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--translate", src);
            Assert.Contains("RunTranslate", src);
        }

        [Fact]
        public void ParseArgs_ForceVersionFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--force-version=FE8U", "--rom=test.gba" });
            Assert.Equal("FE8U", dic["--force-version"]);
            Assert.Equal("test.gba", dic["--rom"]);
        }

        [Fact]
        public void ParseArgs_DecreaseColorSubFlags()
        {
            var dic = CliProgram.ParseArgs(new[] {
                "--decreasecolor", "--in=test.png", "--out=out.png",
                "--noScale", "--noReserve1stColor", "--ignoreTSA"
            });
            Assert.True(dic.ContainsKey("--noScale"));
            Assert.True(dic.ContainsKey("--noReserve1stColor"));
            Assert.True(dic.ContainsKey("--ignoreTSA"));
        }

        [Fact]
        public void ParseArgs_TranslateWithOutFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--translate", "--rom=test.gba", "--out=texts.tsv" });
            Assert.True(dic.ContainsKey("--translate"));
            Assert.Equal("texts.tsv", dic["--out"]);
        }

        [Fact]
        public void ParseArgs_TranslateWithInFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--translate", "--rom=test.gba", "--in=texts.tsv" });
            Assert.True(dic.ContainsKey("--translate"));
            Assert.Equal("texts.tsv", dic["--in"]);
        }

        [Fact]
        public void CliProgram_HasForceVersionInHelp()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--force-version", src);
        }

        [Fact]
        public void CliProgram_HasDecreaseColorSubFlagsInHelp()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--noScale", src);
            Assert.Contains("--noReserve1stColor", src);
            Assert.Contains("--ignoreTSA", src);
        }

        [Fact]
        public void CliProgram_TranslateCommandUsesTranslateCore()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("TranslateCore.DumpTexts", src);
            Assert.Contains("TranslateCore.ExportToTSV", src);
            Assert.Contains("TranslateCore.ImportFromTSV", src);
        }

        [Fact]
        public void RomLoader_LoadRom_HasForceVersionOverload()
        {
            var src = System.IO.File.ReadAllText(GetRomLoaderPath());
            Assert.Contains("LoadForceVersion", src);
            Assert.Contains("forceVersion", src);
        }

        // ------------------------------------------------------------------ New CLI args (cross-platform migration)

        [Fact]
        public void ParseArgs_LastRomFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--lastrom" });
            Assert.True(dic.ContainsKey("--lastrom"));
        }

        [Fact]
        public void ParseArgs_ForceDetailFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--force-detail" });
            Assert.True(dic.ContainsKey("--force-detail"));
        }

        [Fact]
        public void ParseArgs_TranslateBatchFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--translate_batch", "--rom=test.gba", "--out=texts.tsv" });
            Assert.True(dic.ContainsKey("--translate_batch"));
            Assert.Equal("test.gba", dic["--rom"]);
        }

        [Fact]
        public void ParseArgs_TestFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--test", "--rom=test.gba" });
            Assert.True(dic.ContainsKey("--test"));
        }

        [Fact]
        public void ParseArgs_TestOnlyFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--testonly", "--rom=test.gba" });
            Assert.True(dic.ContainsKey("--testonly"));
        }

        [Fact]
        public void CliProgram_HasLastRomCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--lastrom", src);
            Assert.Contains("RunLastRom", src);
        }

        [Fact]
        public void CliProgram_HasForceDetailCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--force-detail", src);
        }

        [Fact]
        public void CliProgram_HasTranslateBatchCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--translate_batch", src);
            Assert.Contains("RunTranslateBatch", src);
        }

        [Fact]
        public void CliProgram_HasTestCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--test", src);
            Assert.Contains("RunSelfTest", src);
        }

        [Fact]
        public void CliProgram_HasTestOnlyCommand()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--testonly", src);
        }

        [Fact]
        public void CliProgram_LastRomReadsConfig()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("Last_Rom_Filename", src);
        }

        [Fact]
        public void CliProgram_TranslateBatchExportsAndImports()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("TranslateCore.DumpTexts", src);
            Assert.Contains("TranslateCore.WriteTexts", src);
        }

        [Fact]
        public void CliProgram_HelpShowsNewCommands()
        {
            var src = System.IO.File.ReadAllText(GetCliProgramPath());
            Assert.Contains("--lastrom", src);
            Assert.Contains("--force-detail", src);
            Assert.Contains("--translate_batch", src);
            Assert.Contains("--test", src);
            Assert.Contains("--testonly", src);
        }

        private static string GetCliProgramPath()
        {
            var dir = System.AppContext.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = System.IO.Path.GetDirectoryName(dir);
            if (dir == null) throw new System.InvalidOperationException("Cannot find solution root");
            return System.IO.Path.Combine(dir, "FEBuilderGBA.CLI", "Program.cs");
        }

        private static string GetRomLoaderPath()
        {
            var dir = System.AppContext.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = System.IO.Path.GetDirectoryName(dir);
            if (dir == null) throw new System.InvalidOperationException("Cannot find solution root");
            return System.IO.Path.Combine(dir, "FEBuilderGBA.CLI", "RomLoader.cs");
        }

        [Fact]
        public void ParseArgs_SpaceSeparatedValue()
        {
            var dic = CliProgram.ParseArgs(new[] { "--rom", "/path/to/rom.gba", "--makeups=/out.ups" });
            Assert.Equal("/path/to/rom.gba", dic["--rom"]);
            Assert.Equal("/out.ups", dic["--makeups"]);
        }

        [Fact]
        public void ParseArgs_SpaceSeparated_DoesNotConsumeNextFlag()
        {
            var dic = CliProgram.ParseArgs(new[] { "--translate", "--rom=test.gba" });
            Assert.Equal("", dic["--translate"]);
            Assert.Equal("test.gba", dic["--rom"]);
        }
    }
}
