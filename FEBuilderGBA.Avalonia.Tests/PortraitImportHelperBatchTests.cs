// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for the batch folder-import path added in #661 — see
// PortraitImportHelper.ImportFolderAsync + ParseSlotIdFromFilename.
//
// Covers:
//   - ParseSlotIdFromFilename: hex prefix, decimal prefix, no prefix.
//   - ImportFolderAsync: happy path (synthesizes PNGs in a temp folder).
//   - ImportFolderAsync: mixed valid + invalid filenames (skipped counter).
//   - ImportFolderAsync: rollback when every file is invalid (ROM unchanged).
using System;
using System.IO;
using System.Threading.Tasks;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class PortraitImportHelperBatchTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public PortraitImportHelperBatchTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // ------------------------------------------------------------------
        // ParseSlotIdFromFilename — pure parsing tests, no ROM/Image services
        // needed. Uses reflection to reach the internal helper.
        // ------------------------------------------------------------------

        static int InvokeParseSlotId(string fileName)
        {
            var method = typeof(PortraitImportHelper).GetMethod(
                "ParseSlotIdFromFilename",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);
            return (int)method!.Invoke(null, new object[] { fileName })!;
        }

        [Fact]
        public void ParseSlotIdFromFilename_HexPrefix_Returns0x1F()
        {
            Assert.Equal(31, InvokeParseSlotId("0x1F.png"));
            Assert.Equal(31, InvokeParseSlotId("0x1f.bmp"));
            Assert.Equal(0xAB, InvokeParseSlotId("0xAB_eirika.png"));
        }

        [Fact]
        public void ParseSlotIdFromFilename_DecimalPrefix_Returns31()
        {
            Assert.Equal(31, InvokeParseSlotId("31.png"));
            Assert.Equal(7, InvokeParseSlotId("7_seth.bmp"));
            Assert.Equal(0, InvokeParseSlotId("0.png"));
        }

        [Fact]
        public void ParseSlotIdFromFilename_NoPrefix_ReturnsMinusOne()
        {
            Assert.Equal(-1, InvokeParseSlotId("random.png"));
            Assert.Equal(-1, InvokeParseSlotId("eirika.bmp"));
            Assert.Equal(-1, InvokeParseSlotId(""));
            Assert.Equal(-1, InvokeParseSlotId((string?)null!));
        }

        // ------------------------------------------------------------------
        // ImportFolderAsync — uses real PNGs synthesized to temp + real ROM.
        // ------------------------------------------------------------------

        static IDisposable EnsureImageService()
        {
            var prev = CoreState.ImageService;
            if (prev == null) CoreState.ImageService = new SkiaImageService();
            return new RestoreImageService(prev);
        }

        sealed class RestoreImageService : IDisposable
        {
            readonly IImageService _prev;
            public RestoreImageService(IImageService prev) { _prev = prev; }
            public void Dispose() { CoreState.ImageService = _prev; }
        }

        static void WriteTinyPng(string path, int width, int height)
        {
            var svc = CoreState.ImageService;
            using IImage img = svc.CreateImage(width, height);
            byte[] rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                rgba[i * 4 + 0] = (byte)((i * 7) & 0xFF);
                rgba[i * 4 + 1] = (byte)((i * 13) & 0xFF);
                rgba[i * 4 + 2] = (byte)((i * 23) & 0xFF);
                rgba[i * 4 + 3] = 255;
            }
            img.SetPixelData(rgba);
            img.Save(path);
        }

        [Fact]
        public async Task ImportFolderAsync_ParsesAllValidFiles()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            string folder = Path.Combine(Path.GetTempPath(), $"portrait_batch_{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                // Synthesize two valid PNGs targeting different slots.
                WriteTinyPng(Path.Combine(folder, "0x20.png"), 16, 16);
                WriteTinyPng(Path.Combine(folder, "33.png"), 16, 16);

                var rom = _fixture.ROM;
                uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
                uint slot32 = baseAddr + (uint)(0x20 * rom.RomInfo.portrait_datasize);
                uint slot33 = baseAddr + (uint)(33 * rom.RomInfo.portrait_datasize);
                uint slot32_d0_before = rom.p32(slot32 + 0);
                uint slot33_d0_before = rom.p32(slot33 + 0);

                var undo = new UndoService();
                var result = await PortraitImportHelper.ImportFolderAsync(folder, null, undo, rom);

                Assert.Equal(2, result.Total);
                Assert.Equal(2, result.Imported);
                Assert.Equal(0, result.Failed);
                Assert.Equal(0, result.Skipped);
                Assert.NotEqual(slot32_d0_before, rom.p32(slot32 + 0));
                Assert.NotEqual(slot33_d0_before, rom.p32(slot33 + 0));
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ImportFolderAsync_SkipsInvalidNames()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            string folder = Path.Combine(Path.GetTempPath(), $"portrait_batch_{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                // 1 valid + 2 invalid (no numeric prefix).
                WriteTinyPng(Path.Combine(folder, "0x22.png"), 16, 16);
                WriteTinyPng(Path.Combine(folder, "eirika.png"), 16, 16);
                WriteTinyPng(Path.Combine(folder, "random_name.bmp"), 16, 16);

                var rom = _fixture.ROM;
                var undo = new UndoService();
                var result = await PortraitImportHelper.ImportFolderAsync(folder, null, undo, rom);

                Assert.Equal(3, result.Total);
                Assert.Equal(1, result.Imported);
                Assert.Equal(0, result.Failed);
                Assert.Equal(2, result.Skipped);
                // Both invalid-name lines should be in the report.
                Assert.Contains(result.Lines, l => l.Contains("eirika.png") && l.Contains("SKIPPED"));
                Assert.Contains(result.Lines, l => l.Contains("random_name.bmp") && l.Contains("SKIPPED"));
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ImportFolderAsync_RollbackWhenAllFail()
        {
            if (!_fixture.IsAvailable || _fixture.Version != "FE8U")
            {
                _output.WriteLine($"SKIP: needs FE8U ROM (have {_fixture.Version})");
                return;
            }

            using var _ = EnsureImageService();
            string folder = Path.Combine(Path.GetTempPath(), $"portrait_batch_{Guid.NewGuid():N}");
            Directory.CreateDirectory(folder);
            try
            {
                // All filenames have no numeric prefix -> all skipped, none
                // imported. Imported == 0 must trigger Rollback path.
                WriteTinyPng(Path.Combine(folder, "no_prefix_one.png"), 16, 16);
                WriteTinyPng(Path.Combine(folder, "no_prefix_two.bmp"), 16, 16);

                var rom = _fixture.ROM;
                // Snapshot a few well-known portrait entry headers to prove
                // nothing was written.
                uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
                uint slot5 = baseAddr + (uint)(5 * rom.RomInfo.portrait_datasize);
                uint d0Before = rom.p32(slot5 + 0);
                uint d8Before = rom.p32(slot5 + 8);

                var undo = new UndoService();
                var result = await PortraitImportHelper.ImportFolderAsync(folder, null, undo, rom);

                Assert.Equal(2, result.Total);
                Assert.Equal(0, result.Imported);
                Assert.Equal(2, result.Skipped);

                // No portrait entry should have changed — rollback path.
                Assert.Equal(d0Before, rom.p32(slot5 + 0));
                Assert.Equal(d8Before, rom.p32(slot5 + 8));
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ImportFolderAsync_ReportsErrorOnMissingFolder()
        {
            // No ROM / image service needed — this short-circuits on folder
            // existence check before any I/O.
            string nonExistent = Path.Combine(Path.GetTempPath(), $"does_not_exist_{Guid.NewGuid():N}");
            var rom = _fixture.IsAvailable ? _fixture.ROM : null;
            // Skip when no ROM — early-return paths require a valid ROM.
            if (rom == null)
            {
                _output.WriteLine("SKIP: no ROM available");
                return;
            }
            var undo = new UndoService();
            var result = await PortraitImportHelper.ImportFolderAsync(nonExistent, null, undo, rom);
            Assert.Equal(0, result.Total);
            Assert.Equal(0, result.Imported);
            Assert.Contains(result.Lines, l => l.Contains("Folder not found"));
        }

        // ------------------------------------------------------------------
        // AXAML wiring smoke checks — proves the new button + progress UI
        // were added and Notes was updated.
        // ------------------------------------------------------------------

        static string FindRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            if (dir == null)
                throw new InvalidOperationException("Could not find FEBuilderGBA.sln from test base directory");
            return dir;
        }

        [Fact]
        public void Wizard_AxamlContainsBatchButtonAndProgressUI()
        {
            string repoRoot = FindRepoRoot();
            string axamlPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml");
            string xaml = File.ReadAllText(axamlPath);

            Assert.Contains("ImagePortraitImporter_PickFolder_Button", xaml);
            Assert.Contains("ImagePortraitImporter_BatchProgress_Bar", xaml);
            Assert.Contains("ImagePortraitImporter_BatchResults_TextBox", xaml);
            Assert.Contains("Click=\"PickFolder_Click\"", xaml);
            // Notes blurb must no longer list "batch import" as a follow-up.
            // It SHOULD mention "Pick Folder (batch)" as a working feature.
            Assert.Contains("Pick Folder (batch)", xaml);
            // The follow-up list mentions only sliders + palette.
            int notesStart = xaml.IndexOf("Notes:", StringComparison.Ordinal);
            Assert.True(notesStart >= 0, "Notes section missing");
            int notesEnd = xaml.IndexOf("\" />", notesStart, StringComparison.Ordinal);
            string notesText = xaml.Substring(notesStart, notesEnd - notesStart);
            // The follow-up list should not advertise batch import as a TODO.
            Assert.DoesNotContain("batch import,", notesText);
            Assert.DoesNotContain("batch import)", notesText);
        }

        [Fact]
        public void Wizard_CodeBehindCallsImportFolderAsync()
        {
            string repoRoot = FindRepoRoot();
            string viewCsPath = Path.Combine(repoRoot, "FEBuilderGBA.Avalonia", "Views",
                "ImagePortraitImporterView.axaml.cs");
            string source = File.ReadAllText(viewCsPath);

            Assert.Contains("void PickFolder_Click", source);
            Assert.Contains("PortraitImportHelper.ImportFolderAsync", source);
            // The UI must disable competing buttons during a batch run.
            Assert.Contains("PickFolderButton.IsEnabled = false", source);
            Assert.Contains("PickFolderButton.IsEnabled = true", source);
        }
    }
}
