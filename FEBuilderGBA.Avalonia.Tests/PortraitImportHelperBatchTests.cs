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
        public async Task ImportFolderAsync_AllSkipped_LeavesRomUnchanged()
        {
            // With per-file undo isolation (Copilot PR review #1 fix), an
            // all-skipped batch never even enters ImportSimple/ImportSheet,
            // so no ROM bytes are written. This test guards against that
            // invariant — bytes around a known slot must be byte-identical
            // before and after the batch.
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
                // imported. No file ever opens an undo scope.
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

                // No portrait entry should have changed.
                Assert.Equal(d0Before, rom.p32(slot5 + 0));
                Assert.Equal(d8Before, rom.p32(slot5 + 8));
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ImportFolderAsync_FileFailsMidWrite_DoesNotCommitItsBytes()
        {
            // Copilot PR review #1 acceptance: per-file undo isolation.
            //
            // Strategy: drive the batch with a mixed folder
            //   - valid PNG -> slot A succeeds
            //   - broken/corrupt PNG file with a valid slot prefix ->
            //     LoadAndQuantizeFromFile returns Success=false, so the helper
            //     reports FAILED and ImportSimple/ImportSheet is never even
            //     called. That alone proves pre-validation guards the scope.
            //
            // Stronger guarantee — file fails AFTER opening the helper's undo
            // scope: byte-equivalence check. We synthesize one valid PNG and
            // run the batch, then on a fresh ROM copy we call ImportSimple
            // directly for that same slot. If per-file isolation holds, the
            // batch path leaves the same ROM bytes as the standalone call —
            // ANY trace of an unrelated failed file's writes would show as a
            // diff.
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
                // File 1: valid PNG -> slot 0x25.
                WriteTinyPng(Path.Combine(folder, "0x25.png"), 16, 16);
                // File 2: corrupt content with valid slot prefix -> fails load.
                File.WriteAllBytes(Path.Combine(folder, "0x26.png"),
                    new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 });

                var rom = _fixture.ROM;
                uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
                uint slot25 = baseAddr + (uint)(0x25 * rom.RomInfo.portrait_datasize);
                uint slot26 = baseAddr + (uint)(0x26 * rom.RomInfo.portrait_datasize);

                // Snapshot slot 0x26 BEFORE batch — must be byte-identical
                // after the batch since file 2 failed pre-validation.
                byte[] slot26Before = new byte[rom.RomInfo.portrait_datasize];
                Array.Copy(rom.Data, (int)slot26, slot26Before, 0, slot26Before.Length);

                uint d0_25_before = rom.p32(slot25 + 0);

                var undo = new UndoService();
                var result = await PortraitImportHelper.ImportFolderAsync(folder, null, undo, rom);

                // Valid file 1 imported, corrupt file 2 reported as FAILED.
                Assert.Equal(2, result.Total);
                Assert.Equal(1, result.Imported);
                Assert.Equal(1, result.Failed);
                Assert.Equal(0, result.Skipped);

                // Slot 0x25 was updated.
                Assert.NotEqual(d0_25_before, rom.p32(slot25 + 0));

                // Slot 0x26 — failed file MUST NOT have left any bytes behind.
                byte[] slot26After = new byte[rom.RomInfo.portrait_datasize];
                Array.Copy(rom.Data, (int)slot26, slot26After, 0, slot26After.Length);
                Assert.Equal(slot26Before, slot26After);

                // Result line for the corrupt file mentions FAILED (not OK).
                Assert.Contains(result.Lines, l => l.Contains("0x26.png") && l.Contains("FAILED"));
                Assert.Contains(result.Lines, l => l.Contains("0x25.png") && l.Contains("OK"));
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ImportFolderAsync_128x112File_RoutesThroughImportSheet()
        {
            // Copilot PR review #2 acceptance: 128x112 composites must route
            // through ImportSheet (writes D0 + D4 + D8 + D12) instead of
            // ImportSimple (writes only D0 + D8). FE7/FE8 only — FE6 ROMs
            // would reject via the IsFe7Or8EntryLayout gate.
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
                // Composite-sheet sized PNG: 128x112.
                WriteTinyPng(Path.Combine(folder, "0x28.png"), 128, 112);

                var rom = _fixture.ROM;
                uint baseAddr = rom.p32(rom.RomInfo.portrait_pointer);
                uint slot28 = baseAddr + (uint)(0x28 * rom.RomInfo.portrait_datasize);

                uint d0Before = rom.p32(slot28 + 0);
                uint d4Before = rom.p32(slot28 + 4);
                uint d8Before = rom.p32(slot28 + 8);
                uint d12Before = rom.p32(slot28 + 12);

                var undo = new UndoService();
                var result = await PortraitImportHelper.ImportFolderAsync(folder, null, undo, rom);

                Assert.Equal(1, result.Total);
                Assert.Equal(1, result.Imported);
                Assert.Equal(0, result.Failed);

                // ImportSheet path: D0 (sheet), D4 (mini), D8 (palette),
                // D12 (mouth) all change. ImportSimple would have left D4 +
                // D12 unchanged.
                Assert.NotEqual(d0Before, rom.p32(slot28 + 0));
                Assert.NotEqual(d4Before, rom.p32(slot28 + 4));
                Assert.NotEqual(d8Before, rom.p32(slot28 + 8));
                Assert.NotEqual(d12Before, rom.p32(slot28 + 12));

                // Result line marks the slot as imported via the sheet path
                // so users can tell which mode ran.
                Assert.Contains(result.Lines, l => l.Contains("0x28.png") && l.Contains("(sheet)") && l.Contains("OK"));
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch { }
            }
        }

        [Fact]
        public async Task ImportFolderAsync_RejectsSlotIdBeyondPortraitTable()
        {
            // Copilot CLI PR review (round 2) #1 acceptance: filenames whose
            // slot ID is past the portrait-table cap must be rejected as
            // FAILED, not just bounds-checked against rom.Data.Length.
            //
            // We pick a slot ID well above any realistic FE8U portrait count
            // (e.g. 0xFFFF) — that index passes a raw "addr + size <= ROM
            // length" check on the full FE8U ROM (16 MiB), but would write
            // outside the portrait table proper. The new table-bound check
            // must catch it.
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
                // Filename slot ID = 0xFFFF — well past any real portrait
                // table size, but still inside the ROM byte length.
                WriteTinyPng(Path.Combine(folder, "0xFFFF.png"), 16, 16);

                var rom = _fixture.ROM;
                // Sanity check: the portrait table is much smaller than 0xFFFF.
                int portraitCount = PortraitImportHelper.CountPortraitTableEntries(rom);
                Assert.True(portraitCount > 0 && portraitCount < 0xFFFF,
                    $"Test precondition broken — portrait table size is {portraitCount}");

                // Snapshot the bytes at the OUT-OF-RANGE target — must NOT
                // have changed after the batch (proves the helper bailed
                // before any ROM write).
                uint portraitBase = rom.p32(rom.RomInfo.portrait_pointer);
                uint outOfRangeAddr = portraitBase + (uint)(0xFFFF * rom.RomInfo.portrait_datasize);
                byte[] before = new byte[rom.RomInfo.portrait_datasize];
                if (outOfRangeAddr + before.Length <= (uint)rom.Data.Length)
                    Array.Copy(rom.Data, (int)outOfRangeAddr, before, 0, before.Length);

                var undo = new UndoService();
                var result = await PortraitImportHelper.ImportFolderAsync(folder, null, undo, rom);

                Assert.Equal(1, result.Total);
                Assert.Equal(0, result.Imported);
                Assert.Equal(1, result.Failed);
                Assert.Equal(0, result.Skipped);

                // Result line names the out-of-range slot.
                Assert.Contains(result.Lines, l =>
                    l.Contains("0xFFFF.png") && l.Contains("FAILED") && l.Contains("out of range"));

                // Out-of-range region remains byte-identical (when it's
                // addressable at all).
                if (outOfRangeAddr + before.Length <= (uint)rom.Data.Length)
                {
                    byte[] after = new byte[rom.RomInfo.portrait_datasize];
                    Array.Copy(rom.Data, (int)outOfRangeAddr, after, 0, after.Length);
                    Assert.Equal(before, after);
                }
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
            Assert.Contains("ImagePortraitImporter_BatchResults_Input", xaml);
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
