using System;
using System.IO;
using Xunit; // Explicit (the project also has a generated global `using Xunit;` via <Using Include="Xunit"/>, so this is redundant but explicit per review #1104).
using FEBuilderGBA;

namespace FEBuilderGBA.Tests.Unit
{
    /// <summary>
    /// #1028 Slice A — cross-project wiring guards for the text-ID reference cache.
    ///
    /// These cross-project source-text + reflection assertions catch Avalonia/Core
    /// refactors that the cross-platform test projects can't see (per repo
    /// convention — FEBuilderGBA.Tests is net10.0-windows and links the WinForms
    /// assembly + Core). They verify:
    ///   1. CoreState.UseTextIDCache is typed ITextIDCache (not object).
    ///   2. WinForms EtcCacheTextID implements ITextIDCache.
    ///   3. Program.cs assigns CoreState.UseTextIDCache after building its
    ///      EtcCacheTextID instance.
    /// </summary>
    public class TextIDCacheWiringTests
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

        [Fact]
        public void CoreState_UseTextIDCache_IsTyped_ITextIDCache()
        {
            var prop = typeof(CoreState).GetProperty("UseTextIDCache");
            Assert.NotNull(prop);
            Assert.Equal(typeof(ITextIDCache), prop!.PropertyType);
        }

        [Fact]
        public void EtcCacheTextID_Implements_ITextIDCache()
        {
            // The WinForms cache class must satisfy the Core seam so Program can
            // share its instance via CoreState.
            var t = typeof(EtcCacheTextID);
            Assert.True(typeof(ITextIDCache).IsAssignableFrom(t),
                "EtcCacheTextID must implement ITextIDCache");
        }

        [Fact]
        public void ProgramCs_Assigns_CoreState_UseTextIDCache()
        {
            var programCs = Path.Combine(SolutionDir, "FEBuilderGBA", "Program.cs");
            Assert.True(File.Exists(programCs), $"Program.cs should exist at {programCs}");
            var src = File.ReadAllText(programCs);
            Assert.Contains("CoreState.UseTextIDCache = UseTextIDCache;", src);
        }

        [Fact]
        public void TextIDCacheCore_Implements_ITextIDCache()
        {
            // The cross-platform implementation used by Avalonia.
            Assert.True(typeof(ITextIDCache).IsAssignableFrom(typeof(TextIDCacheCore)),
                "TextIDCacheCore must implement ITextIDCache");
        }

        [Fact]
        public void Avalonia_MainWindow_Recreates_TextIDCache_OnRomLoad()
        {
            // The Avalonia ROM-load path must (re)create the Core cache after each
            // load (replace, not ??=), since the ctor is ROM/path/language-sensitive.
            // #1870: this init moved from MainWindow.FinishLoadedRom into the shared
            // RomFileService.InitializeLoadedRom (used by desktop + single-view shell).
            var initSource = Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Services", "RomFileService.cs");
            Assert.True(File.Exists(initSource), $"RomFileService.cs should exist at {initSource}");
            var src = File.ReadAllText(initSource);
            Assert.Contains("CoreState.UseTextIDCache = new TextIDCacheCore();", src);
            // Guard against an accidental boot-time / null-coalescing assignment.
            Assert.DoesNotContain("CoreState.UseTextIDCache ??= ", src);

            // And MainWindow must delegate to that shared init so desktop stays wired.
            var mainWindow = Path.Combine(SolutionDir, "FEBuilderGBA.Avalonia", "Views", "MainWindow.axaml.cs");
            Assert.True(File.Exists(mainWindow), $"MainWindow.axaml.cs should exist at {mainWindow}");
            Assert.Contains("RomFileService.InitializeLoadedRom", File.ReadAllText(mainWindow));
        }
    }
}
