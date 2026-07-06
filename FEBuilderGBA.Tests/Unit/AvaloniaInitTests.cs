namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// Tests that verify the shared post-load init (RomFileService.InitializeLoadedRom,
    /// #1870) has per-subsystem init with fallbacks matching the CLI
    /// RomLoader.InitFull() pattern, and that MainWindow delegates to it.
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

        // #1870: the runtime half of the post-load init moved out of
        // MainWindow.FinishLoadedRom into the shared RomFileService.InitializeLoadedRom
        // (so desktop + the single-view web/Android shell never drift). The
        // per-subsystem try/catch + fallback guards below now live there.
        private string InitSource => File.ReadAllText(
            Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Services", "RomFileService.cs"));

        [Fact]
        public void FinishLoadedRom_DelegatesToSharedInit()
        {
            // MainWindow must route its post-load init through the shared service
            // so the desktop and single-view shells stay in lockstep (#1870).
            Assert.Contains("RomFileService.InitializeLoadedRom", MainWindowSource);
        }

        [Fact]
        public void LoadRomFile_HasSystemTextEncoderFallback()
        {
            Assert.Contains("HeadlessSystemTextEncoder", InitSource);
            Assert.Contains("Failed to init SystemTextEncoder, using headless fallback", InitSource);
        }

        [Fact]
        public void LoadRomFile_HasPerSubsystemTryCatch_FETextEncode()
        {
            Assert.Contains("Failed to init FETextEncode", InitSource);
        }

        [Fact]
        public void LoadRomFile_HasPerSubsystemTryCatch_FlagCache()
        {
            Assert.Contains("Failed to init FlagCache", InitSource);
        }

        [Fact]
        public void LoadRomFile_HasPerSubsystemTryCatch_EventScripts()
        {
            Assert.Contains("Failed to init EventScripts", InitSource);
        }

        [Fact]
        public void LoadRomFile_WiresHeadlessCaches()
        {
            Assert.Contains("CommentCache ??= new HeadlessEtcCache()", InitSource);
            Assert.Contains("LintCache ??= new HeadlessEtcCache()", InitSource);
            Assert.Contains("WorkSupportCache ??= new HeadlessEtcCache()", InitSource);
        }

        [Fact]
        public void LoadRomFile_MatchesCLIInitPattern()
        {
            // Verify the shared Avalonia init has the same structure as CLI RomLoader.InitFull()
            var cliSource = File.ReadAllText(
                Path.Combine(SolutionDir, "FEBuilderGBA.CLI", "RomLoader.cs"));

            // Both should have HeadlessSystemTextEncoder fallback
            Assert.Contains("HeadlessSystemTextEncoder", cliSource);
            Assert.Contains("HeadlessSystemTextEncoder", InitSource);

            // Both should have per-subsystem error logging
            Assert.Contains("Failed to init SystemTextEncoder", cliSource);
            Assert.Contains("Failed to init SystemTextEncoder", InitSource);
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
