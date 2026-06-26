// SPDX-License-Identifier: GPL-3.0-or-later
// #1464 PR proof — render the real MonsterWMapProbabilityViewerView on a
// synthetic FE8 ROM so the screenshot shows the 3 restored editing surfaces:
// the stage-spread list + map-id field, the per-base probability list with 9
// cells + live SUM%, and the skirmish start/end event pointer fields. The
// previous stub had ONLY the base-point list + a single "Base Point ID" field.
//
// Headless RenderTargetBitmap — works on locked machines and in CI. Default
// output is a temp dir; set FEBUILDERGBA_SCREENSHOT_DIR to regenerate the
// canonical PR screenshot into the repo's pr-screenshots/.
using System;
using System.IO;
using global::Avalonia;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class MonsterWMapProbabilityScreenshotTest : IDisposable
    {
        readonly ITestOutputHelper _output;
        readonly ROM? _savedRom;

        public MonsterWMapProbabilityScreenshotTest(ITestOutputHelper output)
        {
            _output = output;
            _savedRom = CoreState.ROM;
        }

        public void Dispose() => CoreState.ROM = _savedRom;

        [AvaloniaFact]
        public void MonsterWMapProbabilityView_ShowsThreeSurfaces_SavesScreenshot()
        {
            // Synthetic FE8U ROM with stage + probability tables planted and
            // repointed so all 3 restored surfaces load real data.
            var rom = new ROM();
            rom.LoadLow("wmap-1464-shot.gba", new byte[0x1100000], "BE8E01");
            CoreState.ROM = rom;

            uint stageEirikaBase = 0x00900000u;
            uint stageEphraimBase = 0x00901000u;
            uint probEirikaBase = 0x00910000u;
            uint probEphraimBase = 0x00911000u;

            WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_stage_1_pointer, stageEirikaBase | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_stage_2_pointer, stageEphraimBase | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_probability_1_pointer, probEirikaBase | 0x08000000u);
            WriteU32(rom.Data, (int)rom.RomInfo.monster_wmap_probability_2_pointer, probEphraimBase | 0x08000000u);

            for (int i = 0; i < MonsterWMapProbabilityCore.StageCount; i++)
            {
                rom.Data[(int)(stageEirikaBase + i)] = (byte)(0x10 + i);
                rom.Data[(int)(stageEphraimBase + i)] = (byte)(0x40 + i);
            }
            // Plant a probability row whose 9 cells sum to 100.
            byte[] row = { 10, 20, 30, 5, 0, 0, 0, 0, 35 };
            for (int i = 0; i < row.Length; i++) rom.Data[(int)(probEirikaBase + i)] = row[i];

            var view = new MonsterWMapProbabilityViewerView();
            view.SelectFirstItem();

            // Data-layer render coverage (ENFORCED). The FE8-only surfaces gate.
            var vm = Assert.IsType<MonsterWMapProbabilityViewerViewModel>(view.DataViewModel);
            Assert.True(vm.IsSupported);
            Assert.Equal(MonsterWMapProbabilityCore.StageCount, vm.LoadStageList().Count);
            Assert.Equal(MonsterWMapProbabilityCore.ProbabilityCount, vm.LoadProbabilityList().Count);

            // Measure/Arrange must not throw on the real visual tree (catches XAML
            // binding/format faults). PNG Save is best-effort (UseHeadlessDrawing).
            const int VW = 1100, VH = 760;
            view.Measure(new Size(VW, VH));
            view.Arrange(new Rect(0, 0, VW, VH));
            try
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(VW, VH));
                bitmap.Render(view);

                string outDir = ResolveScreenshotOutputDir();
                Directory.CreateDirectory(outDir);
                string outPath = Path.Combine(outDir, "pr1464-wmapmonster-fe8u.png");
                bitmap.Save(outPath);
                _output.WriteLine($"Saved screenshot to: {outPath} ({new FileInfo(outPath).Length} bytes)");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Headless PNG save no-op (UseHeadlessDrawing, not the #1464 fix): {ex.Message}");
            }
        }

        static void WriteU32(byte[] data, int offset, uint value)
        {
            data[offset + 0] = (byte)(value & 0xFF);
            data[offset + 1] = (byte)((value >> 8) & 0xFF);
            data[offset + 2] = (byte)((value >> 16) & 0xFF);
            data[offset + 3] = (byte)((value >> 24) & 0xFF);
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
