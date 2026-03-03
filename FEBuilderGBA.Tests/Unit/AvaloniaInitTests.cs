namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests that verify MainWindow.LoadRomFile() has per-subsystem init
    /// with fallbacks matching CLI RomLoader.InitFull() pattern.
    /// Source-code verification tests.
    /// </summary>
    public class AvaloniaInitTests
    {
        private static string SolutionDir
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                    dir = Path.GetDirectoryName(dir);
                return dir ?? throw new InvalidOperationException("Cannot find solution root");
            }
        }

        private string MainWindowSource => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs"));

        [Fact]
        public void LoadRomFile_HasSystemTextEncoderFallback()
        {
            Assert.Contains("HeadlessSystemTextEncoder", MainWindowSource);
            Assert.Contains("Failed to init SystemTextEncoder, using headless fallback", MainWindowSource);
        }

        [Fact]
        public void LoadRomFile_HasPerSubsystemTryCatch_FETextEncode()
        {
            Assert.Contains("Failed to init FETextEncode", MainWindowSource);
        }

        [Fact]
        public void LoadRomFile_HasPerSubsystemTryCatch_FlagCache()
        {
            Assert.Contains("Failed to init FlagCache", MainWindowSource);
        }

        [Fact]
        public void LoadRomFile_HasPerSubsystemTryCatch_EventScripts()
        {
            Assert.Contains("Failed to init EventScripts", MainWindowSource);
        }

        [Fact]
        public void LoadRomFile_WiresHeadlessCaches()
        {
            Assert.Contains("CommentCache ??= new HeadlessEtcCache()", MainWindowSource);
            Assert.Contains("LintCache ??= new HeadlessEtcCache()", MainWindowSource);
            Assert.Contains("WorkSupportCache ??= new HeadlessEtcCache()", MainWindowSource);
        }

        [Fact]
        public void LoadRomFile_MatchesCLIInitPattern()
        {
            // Verify the Avalonia init has the same structure as CLI RomLoader.InitFull()
            var cliSource = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.CLI", "RomLoader.cs"));

            // Both should have HeadlessSystemTextEncoder fallback
            Assert.Contains("HeadlessSystemTextEncoder", cliSource);
            Assert.Contains("HeadlessSystemTextEncoder", MainWindowSource);

            // Both should have per-subsystem error logging
            Assert.Contains("Failed to init SystemTextEncoder", cliSource);
            Assert.Contains("Failed to init SystemTextEncoder", MainWindowSource);
        }

        [Fact]
        public void AppAxamlCs_HasForceDetailFlag()
        {
            var appSource = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "App.axaml.cs"));
            Assert.Contains("ForceDetailMode", appSource);
            Assert.Contains("--force-detail", appSource);
        }

        [Fact]
        public void AppAxamlCs_HasLastRomSupport()
        {
            var appSource = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "App.axaml.cs"));
            Assert.Contains("--lastrom", appSource);
            Assert.Contains("Last_Rom_Filename", appSource);
        }
    }
}
