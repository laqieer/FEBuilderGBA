// SPDX-License-Identifier: GPL-3.0-or-later
// #1870 — RomFileService: shared ROM open/save + post-load core init for the
// single-view (WebAssembly / Android) shell. These tests lock the two pieces the
// web shell depends on:
//   * InitializeLoadedRom(rom) — the CORE post-load init (the runtime half of
//     MainWindow.FinishLoadedRom) wires CoreState so editors/exports work. The
//     web Open ROM path loads through the STREAM API (browser picks have no
//     local path), so the test loads a ROM from a MemoryStream exactly like the
//     browser does, then asserts CoreState is wired.
//   * SaveRomAsync(owner) short-circuits to null when there is no ROM (so the
//     single-view Save button is a no-op rather than throwing) — hermetic, needs
//     no ROM file.
using System;
using System.IO;
using System.Threading.Tasks;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class RomFileServiceTests
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;

        public RomFileServiceTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        [Fact]
        public async Task SaveRomAsync_WhenNoRom_ReturnsNull_WithoutPicker()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                // ROM-null short-circuits BEFORE the owner/TopLevel is touched, so
                // a null owner is never dereferenced here (documents that the web
                // Save button is a safe no-op with nothing loaded).
                string? saved = await RomFileService.SaveRomAsync(null!);
                Assert.Null(saved);
            }
            finally
            {
                CoreState.ROM = prevRom;
            }
        }

        [Fact]
        public async Task InitializeLoadedRom_FromStream_WiresCoreState()
        {
            if (!_fixture.IsAvailable || _fixture.RomPath == null)
            {
                _output.WriteLine("SKIP: no ROM available (set ROMS_DIR or place roms/FE8U.gba).");
                return;
            }

            // Snapshot the replace-semantics CoreState we mutate so we don't leak
            // a different ROM instance into the shared fixture used by sibling
            // tests in the SharedState collection.
            var prevRom = CoreState.ROM;
            var prevAsm = CoreState.AsmMapFileAsmCache;
            var prevEnc = CoreState.SystemTextEncoder;
            var prevTid = CoreState.UseTextIDCache;
            var prevSkill = CoreState.SkillNameResolver;
            try
            {
                // Load through the STREAM API — this is exactly the web Open ROM
                // path (browser picks are read-only Blobs with no local path).
                byte[] bytes = File.ReadAllBytes(_fixture.RomPath);
                var rom = new ROM();
                using (var ms = new MemoryStream(bytes))
                {
                    var (ok, version) = await rom.LoadFromStreamAsync(ms, Path.GetFileName(_fixture.RomPath));
                    Assert.True(ok, "LoadFromStreamAsync should parse the ROM");
                    Assert.False(string.IsNullOrEmpty(version), "a version should be detected");
                }

                // Null the two replace-semantics fields first so the asserts below
                // prove InitializeLoadedRom actually wires them (not the fixture).
                CoreState.ROM = null;
                CoreState.AsmMapFileAsmCache = null;

                RomFileService.InitializeLoadedRom(rom);

                // Core wiring the task requires: ROM + text encoder + asm/hardcode cache.
                Assert.Same(rom, CoreState.ROM);
                Assert.NotNull(CoreState.SystemTextEncoder);
                Assert.NotNull(CoreState.AsmMapFileAsmCache);
                Assert.IsType<CoreAsmMapCache>(CoreState.AsmMapFileAsmCache);

                // The rest of the shared init that editors/exports depend on.
                Assert.NotNull(CoreState.UseTextIDCache);
                Assert.NotNull(CoreState.ExportFunction);
                Assert.NotNull(CoreState.Undo);
                Assert.NotNull(CoreState.EventScript);
                Assert.NotNull(CoreState.SkillNameResolver);
            }
            finally
            {
                CoreState.ROM = prevRom;
                CoreState.AsmMapFileAsmCache = prevAsm;
                CoreState.SystemTextEncoder = prevEnc;
                CoreState.UseTextIDCache = prevTid;
                CoreState.SkillNameResolver = prevSkill;
            }
        }
    }
}
