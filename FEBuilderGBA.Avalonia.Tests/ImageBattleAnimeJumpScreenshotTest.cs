using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// #1377 screenshot proof: the Class Editor's BattleAnime Jump passes a class
    /// battle-anime SETTING pointer. The editor's left list is CLASS-centric, so
    /// that pointer IS one of the rows; <c>ImageBattleAnimeView.NavigateTo</c>
    /// SELECTS the matching class row and loads it, so the editor lands on the
    /// class's real animation (a populated detail panel) instead of entry 0's
    /// "No animation data found" message — and clicking any list row stays on a
    /// correct address. Renders the real editor to a PNG via
    /// <see cref="RenderTargetBitmap"/> (headless, no visible desktop required).
    /// </summary>
    public class ImageBattleAnimeJumpScreenshotTest : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public ImageBattleAnimeJumpScreenshotTest(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [AvaloniaFact]
        public void ClassJump_DirectLoadsAnimation_SavesScreenshot()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();

            ROM rom = CoreState.ROM;
            Assert.NotNull(rom);
            uint listBase = rom!.p32(rom.RomInfo.image_battle_animelist_pointer);

            // Find the first class whose battle-anime setting pointer is a valid,
            // in-ROM, NON-global-list pointer owned by a class (the #1377 case).
            var classVm = new ClassEditorViewModel();
            var items = classVm.LoadClassList();
            uint settingOffset = 0;
            uint expectedAnimeNo = 0;
            uint owningCid = U.NOT_FOUND;
            foreach (var it in items)
            {
                classVm.LoadClass(it.addr);
                uint raw = classVm.BattleAnimePtr;
                if (!U.isPointer(raw)) continue;
                uint off = U.toOffset(raw);
                if (!U.isSafetyOffset(off, rom) || off + 4 > (uint)rom.Data.Length) continue;
                if (off == listBase) continue;
                uint cid = ClassFormCore.GetIDWhereBattleAnimeAddr(rom, raw);
                if (cid == U.NOT_FOUND) continue;
                settingOffset = off;
                expectedAnimeNo = rom.u16(off + 2);
                owningCid = cid;
                break;
            }
            if (settingOffset == 0)
            {
                _output.WriteLine("SKIP: no class with a non-list-row setting pointer in this ROM");
                return;
            }

            var view = new ImageBattleAnimeView();
            view.Show();
            try
            {
                view.NavigateTo(settingOffset);

                var vm = (ImageBattleAnimeViewModel)view.DataViewModel!;
                _output.WriteLine($"ROM={_fixture.Version} owningCid={owningCid} settingPtr=0x{settingOffset:X08} " +
                    $"CurrentAddr=0x{vm.CurrentAddr:X08} AnimationNumber={vm.AnimationNumber} (entry-0 base would be 0x{listBase:X08})");

                // The fix (asserted OUTSIDE any try/catch so a regression fails the
                // test rather than being swallowed as an "environment" issue): the
                // jump direct-loaded the class setting pointer, NOT entry 0.
                Assert.Equal(settingOffset, vm.CurrentAddr);
                Assert.Equal(expectedAnimeNo, vm.AnimationNumber);

                // The PNG render is best-effort: the headless test app uses
                // UseHeadlessDrawing (no rasteriser) in some environments, so a
                // failed render/save is an environment limitation — NOT a #1377
                // regression. Only this rendering block is wrapped in try/catch.
                TrySaveScreenshot(view);
            }
            finally
            {
                view.Close();
            }
        }

        static void TrySaveScreenshot(ImageBattleAnimeView view)
        {
            try
            {
                const int W = 1100;
                const int H = 820;
                view.Measure(new Size(W, H));
                view.Arrange(new Rect(0, 0, W, H));

                using var bitmap = new RenderTargetBitmap(new PixelSize(W, H));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1377-battleanime-jump.png");
                bitmap.Save(outPath);
            }
            catch
            {
                // Headless render unavailable in this environment — not the #1377 fix.
            }
        }

        static string ResolveScreenshotOutputDir()
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FEBUILDERGBA_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(overrideDir))
                return overrideDir;
            return Path.Combine(Path.GetTempPath(), "FEBuilderGBA-screenshots");
        }
    }
}
