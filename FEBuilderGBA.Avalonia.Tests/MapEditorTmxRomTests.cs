using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Integration (real-ROM) smoke test for the Tiled (.tmx) export→import
    /// round-trip (#1387). Loads FE8U, finds a renderable map, serializes it to
    /// a .tmx via <see cref="MapTmxCore.SerializeTmx"/>, re-parses with
    /// <see cref="MapTmxCore.ParseTmx"/>, and asserts the decoded MAR grid is
    /// identical — then applies it through the same <see cref="MapEditorViewModel.ApplyMapGrid"/>
    /// path the UI uses. SKIPPED (not green-passed) when FE8U.gba is unavailable.
    /// </summary>
    [Collection("SharedState")]
    public class MapEditorTmxRomTests
    {
        [SkippableFact]
        public void Tmx_FE8U_ExportImport_RoundTrips()
        {
            string? romPath = TestRomLocator.FindRom("FE8U");
            Skip.If(romPath == null, "FE8U.gba not available — skipping real-ROM .tmx round-trip test.");

            RomTestHelper.WithRom("FE8U", () =>
            {
                if (CoreState.ImageService == null)
                    CoreState.ImageService = new FEBuilderGBA.SkiaSharp.SkiaImageService();

                var probe = new MapEditorViewModel();
                var list = probe.LoadList();
                Assert.NotNull(list);
                Assert.NotEmpty(list);

                MapEditorViewModel loaded = null;
                foreach (var entry in list)
                {
                    var candidate = new MapEditorViewModel();
                    candidate.LoadMapImage(entry.addr, entry.tag);
                    if (candidate.MapWidth > 0 && candidate.MapHeight > 0)
                    {
                        loaded = candidate;
                        break;
                    }
                }
                Assert.True(loaded != null,
                    "No FE8U map loaded with positive dimensions — expected at least one renderable map.");
                var vm = loaded!;

                byte[] snapshot = vm.GetMapDataSnapshot();
                Assert.NotNull(snapshot);

                // Build the expected MAR grid straight from the cache.
                ushort[] expected = new ushort[vm.MapWidth * vm.MapHeight];
                for (int i = 0; i < expected.Length; i++)
                {
                    int offset = 2 + i * 2;
                    expected[i] = (ushort)(snapshot[offset] | (snapshot[offset + 1] << 8));
                }

                // Export -> .tmx, then re-parse and assert the MAR grid round-trips.
                string tmx = MapTmxCore.SerializeTmx(snapshot, "fe8u.tsx");
                Assert.False(string.IsNullOrEmpty(tmx));

                Assert.True(MapTmxCore.ParseTmx(tmx, out int w, out int h, out ushort[] back, out string err), err);
                Assert.Equal(vm.MapWidth, w);
                Assert.Equal(vm.MapHeight, h);
                Assert.Equal(expected, back);

                // And the parsed grid applies through the real write path.
                bool ok = vm.ApplyMapGrid(back, w, h, out string applyErr, out uint addr);
                Assert.True(ok, applyErr ?? "(null error)");
                Assert.NotEqual(0u, addr);
            });
        }
    }
}
