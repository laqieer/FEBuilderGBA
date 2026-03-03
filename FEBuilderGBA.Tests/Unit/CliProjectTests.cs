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

        private static string GetCliProgramPath()
        {
            var dir = System.AppContext.BaseDirectory;
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                dir = System.IO.Path.GetDirectoryName(dir);
            if (dir == null) throw new System.InvalidOperationException("Cannot find solution root");
            return System.IO.Path.Combine(dir, "FEBuilderGBA.CLI", "Program.cs");
        }
    }
}
