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
    }
}
