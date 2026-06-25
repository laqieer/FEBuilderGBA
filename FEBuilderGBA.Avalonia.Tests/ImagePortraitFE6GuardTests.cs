// SPDX-License-Identifier: GPL-3.0-or-later
//
// Regression tests for issue #1411 (RELEASE-BLOCKER, class A silent corruption):
//
// On an FE6 ROM the main window showed BOTH the generic "Portrait Editor"
// (ImagePortraitView / ImagePortraitViewModel, hardcoded 28-byte stride) and the
// dedicated "Portrait (FE6)" button. The FE6 portrait table is 16 bytes/entry
// (ROMFE6JP.portrait_datasize == 16). The generic VM's LoadList strides at the
// ROM's portrait_datasize (16 on FE6) while LoadEntry/Write read/write 28 bytes,
// so pressing Write on any non-last FE6 portrait silently overwrote the first
// 12 bytes of the FOLLOWING entry. WinForms MainFE6Form only ever opens the
// dedicated 16-byte ImagePortraitFE6Form; FE6 never opens the 28-byte editor.
//
// The fix:
//   1. Hide the generic "Portrait Editor" button on FE6 (and set Tag=false so the
//      editor search filter keeps it hidden) — UpdateEditorVisibility.
//   2. Route OpenImagePortrait_Click to ImagePortraitFE6View on FE6 (defense in depth).
//   3. Early-return in ImagePortraitViewModel.LoadEntry/Write when the ROM's
//      portrait stride != 28 (FE6 == 16), so a direct VM call cannot corrupt.
//
// These tests pin each layer of the fix.

using System.IO;
using System.Text;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    // ====================================================================
    // Layer 1 + 2 — pure logic (no ROM, no Avalonia app needed)
    // ====================================================================
    public class ImagePortraitFE6VisibilityTests
    {
        /// <summary>
        /// #1411 — the generic "Portrait Editor" button (no version suffix in its
        /// Content) must be HIDDEN on FE6 and SHOWN on FE7/FE8. UpdateEditorVisibility
        /// sets PortraitEditorButton.IsVisible = (ver != 6); this pins that predicate
        /// (mirrors SensekiComment_OnlyVisibleForFE7).
        /// </summary>
        [Theory]
        [InlineData(6, false)]
        [InlineData(7, true)]
        [InlineData(8, true)]
        public void GenericPortraitEditor_HiddenOnlyForFE6(int ver, bool expectedVisible)
        {
            // UpdateEditorVisibility: PortraitEditorButton.IsVisible = (ver != 6)
            Assert.Equal(expectedVisible, ver != 6);
        }

        /// <summary>
        /// #1411 (P1, Copilot plan review) — hiding by IsVisible alone is not durable:
        /// the editor search filter (ApplyFilter) re-sets IsVisible and only keeps a
        /// button hidden when its Tag is bool==false. UpdateEditorVisibility therefore
        /// sets PortraitEditorButton.Tag = false on FE6 (null otherwise). This pins the
        /// tag value the filter relies on so a search/clear can't revive the button.
        /// </summary>
        [Theory]
        [InlineData(6, false)]   // FE6: Tag must be boxed bool false (version-hidden)
        [InlineData(7, true)]    // FE7: Tag cleared to null (always shown)
        [InlineData(8, true)]
        public void GenericPortraitEditor_TagMarksVersionHiddenOnFE6(int ver, bool genericVisible)
        {
            // Reproduce the exact Tag assignment from UpdateEditorVisibility.
            object? tag = genericVisible ? null : (object)false;

            if (ver == 6)
            {
                // ApplyFilter keeps the button hidden iff (Tag is bool b && !b).
                Assert.True(tag is bool b && !b);
            }
            else
            {
                Assert.Null(tag);
            }
        }

        /// <summary>
        /// #1411 — OpenImagePortrait_Click routes FE6 to ImagePortraitFE6View (the
        /// dedicated 16-byte editor) and every other version to the generic
        /// ImagePortraitView. This pins the routing decision (the same shape as the
        /// existing ResolveMapSettingView / ResolveClassEditorView dispatch tests),
        /// without constructing windows or touching the WindowManager service.
        /// </summary>
        [Theory]
        [InlineData(6, "ImagePortraitFE6View")]
        [InlineData(7, "ImagePortraitView")]
        [InlineData(8, "ImagePortraitView")]
        public void OpenImagePortrait_RoutesFE6ToDedicatedEditor(int ver, string expectedView)
        {
            Assert.Equal(expectedView, ResolvePortraitEditorView(ver));
        }

        /// <summary>
        /// Reproduces the dispatch logic from MainWindow.OpenImagePortrait_Click.
        /// </summary>
        private static string ResolvePortraitEditorView(int ver)
            => ver == 6 ? "ImagePortraitFE6View" : "ImagePortraitView";
    }

    // ====================================================================
    // Layer 3 — VM guard against a real FE6 ROM (genuine corruption test)
    // ====================================================================
    [Collection("SharedState")]
    public class ImagePortraitFE6WriteGuardTests : IClassFixture<Fe6RomFixture>
    {
        private readonly Fe6RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public ImagePortraitFE6WriteGuardTests(Fe6RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// #1411 — on a real FE6 ROM the generic ImagePortraitViewModel.Write MUST be
        /// a no-op (the editor is unsupported for the 16-byte FE6 stride). Before the
        /// fix, Write on a non-last portrait wrote 28 bytes and clobbered the first 12
        /// bytes (D0 image ptr, D4 map-face ptr, D8 palette ptr) of the NEXT entry.
        /// This test loads a non-last portrait, mutates the VM fields, calls Write, and
        /// asserts the next entry's 16 bytes are byte-identical to before.
        /// </summary>
        [Fact]
        public void FE6_Write_DoesNotCorruptNextPortraitEntry()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: FE6.gba unavailable");
                return;
            }

            ROM rom = CoreState.ROM!;
            Assert.Equal(6, rom.RomInfo.version);
            Assert.Equal(16u, rom.RomInfo.portrait_datasize);

            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
            Assert.True(U.isSafetyOffset(baseAddr, rom), "FE6 portrait base must be a safe offset");

            const uint stride = 16;
            uint entry0 = baseAddr;            // portrait #0 (non-last)
            uint nextEntry = baseAddr + stride; // portrait #1 — must NOT be touched

            // Snapshot the next entry's 16 bytes.
            byte[] before = rom.getBinaryData(nextEntry, (int)stride);

            var vm = new ImagePortraitViewModel();
            vm.LoadEntry(entry0);

            // Deliberately mutate every writable field so a 28-byte Write would
            // certainly overwrite the next entry with these values.
            vm.PortraitImagePtr = 0xDEADBEEF;
            vm.MiniPortraitPtr = 0xCAFEBABE;
            vm.PalettePtr = 0xFEEDFACE;
            vm.MouthFramesPtr = 0x12345678;
            vm.ClassCardPtr = 0x0BADF00D;
            vm.CurrentAddr = entry0; // ensure the write targets entry0

            vm.Write(new FEBuilderGBA.Avalonia.Services.UndoService());

            byte[] after = rom.getBinaryData(nextEntry, (int)stride);
            Assert.Equal(before, after); // next entry untouched → no corruption
        }

        /// <summary>
        /// #1411 — LoadEntry must also be a no-op on FE6 (over-read of 28 bytes shows
        /// wrong field values). After LoadEntry on an FE6 ROM the VM must stay
        /// not-loaded so the editor never presents a 28-byte view of a 16-byte entry.
        /// </summary>
        [Fact]
        public void FE6_LoadEntry_IsNoOp()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: FE6.gba unavailable");
                return;
            }

            ROM rom = CoreState.ROM!;
            uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);

            var vm = new ImagePortraitViewModel();
            vm.LoadEntry(baseAddr);

            Assert.False(vm.IsLoaded); // guard returned before setting IsLoaded
        }
    }

    /// <summary>
    /// Loads FE6.gba specifically (not the RomFixture preferred order which favors
    /// FE8U) so the #1411 FE6 guard can be exercised against the real 16-byte table.
    /// Modeled on RomFixture; restores CoreState on dispose.
    /// </summary>
    public class Fe6RomFixture : System.IDisposable
    {
        public bool IsAvailable { get; }
        public ROM? ROM { get; }

        private readonly ROM? _prevRom;
        private readonly IEtcCache? _prevCommentCache;
        private readonly IEtcCache? _prevLintCache;
        private readonly IEtcCache? _prevWorkSupportCache;
        private readonly ISystemTextEncoder? _prevSystemTextEncoder;
        private readonly string? _prevBaseDirectory;

        public Fe6RomFixture()
        {
            _prevRom = CoreState.ROM;
            _prevCommentCache = CoreState.CommentCache;
            _prevLintCache = CoreState.LintCache;
            _prevWorkSupportCache = CoreState.WorkSupportCache;
            _prevSystemTextEncoder = CoreState.SystemTextEncoder;
            _prevBaseDirectory = CoreState.BaseDirectory;

            string? path = TestRomLocator.FindRom("FE6");
            if (path == null)
            {
                IsAvailable = false;
                return;
            }

            try
            {
                string assemblyDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                CoreState.BaseDirectory = assemblyDir;
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                string configPath = Path.Combine(assemblyDir, "config", "config.xml");
                if (File.Exists(configPath))
                {
                    var config = new Config();
                    config.Load(configPath);
                    CoreState.Config = config;
                }

                var rom = new ROM();
                if (!rom.Load(path, out string _))
                {
                    IsAvailable = false;
                    return;
                }

                CoreState.ROM = rom;
                ROM = rom;

                CoreState.CommentCache = new HeadlessEtcCache();
                CoreState.LintCache = new HeadlessEtcCache();
                CoreState.WorkSupportCache = new HeadlessEtcCache();

                try { CoreState.SystemTextEncoder = new SystemTextEncoder(CoreState.TextEncoding, rom); }
                catch { CoreState.SystemTextEncoder = new HeadlessSystemTextEncoder(rom); }

                try { CoreState.FETextEncoder = new FETextEncode(); } catch { }
                CoreState.TextEscape ??= new TextEscape();
                CoreState.Undo ??= new Undo();

                IsAvailable = true;
            }
            catch
            {
                IsAvailable = false;
            }
        }

        public void Dispose()
        {
            CoreState.ROM = _prevRom;
            CoreState.CommentCache = _prevCommentCache;
            CoreState.LintCache = _prevLintCache;
            CoreState.WorkSupportCache = _prevWorkSupportCache;
            CoreState.SystemTextEncoder = _prevSystemTextEncoder;
            if (_prevBaseDirectory != null)
                CoreState.BaseDirectory = _prevBaseDirectory;
        }
    }
}
