// SPDX-License-Identifier: GPL-3.0-or-later
// #1171 — ROM Rebuild analysis tool (Avalonia port of WinForms ToolROMRebuildForm).
// #1261 — full produce→apply→write-rebuilt-ROM flow (RebuildRom).
// Tests the VM's address validation + validate-then-make report over Core RebuildCore,
// and the end-to-end VM rebuild flow over RebuildProducerCore.MakeWithProducer →
// RebuildApplyCore.Apply on a real FE8U ROM.
using System;
using System.IO;
using System.Threading;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ToolROMRebuildViewModelTests
    {
        [Fact]
        public void ValidateRebuildAddress_NotAligned_ReturnsNotAligned()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;   // no RomInfo -> only alignment/safety apply
                var vm = new ToolROMRebuildViewModel();
                Assert.Equal(ToolROMRebuildViewModel.AddressCheck.NotAligned, vm.ValidateRebuildAddress(0x09000001));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ValidateRebuildAddress_NullRom_ReturnsUnsafe()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;   // no loaded ROM -> cannot judge safety
                var vm = new ToolROMRebuildViewModel();
                Assert.Equal(ToolROMRebuildViewModel.AddressCheck.Unsafe, vm.ValidateRebuildAddress(0x00000400));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void ValidateRebuildAddress_Aligned_ReturnsOk_WhenNoRomInfo()
        {
            var prevRom = CoreState.ROM;
            try
            {
                // In-memory ROM (no RomInfo). isSafetyOffset judges against ITS length, so
                // pick an aligned offset that is >= 0x200 and < the ROM size.
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x4000]);
                CoreState.ROM = rom;
                var vm = new ToolROMRebuildViewModel();
                Assert.Equal(ToolROMRebuildViewModel.AddressCheck.Ok, vm.ValidateRebuildAddress(0x00001000));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void MakeRebuild_NoRom_ReturnsNoRom()
        {
            var prevRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new ToolROMRebuildViewModel();
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.NoRom, vm.MakeRebuild("x", 0x09000000, "y"));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void MakeRebuild_MissingOriginal_ReturnsOriginalMissing()
        {
            var prevRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x100]);
                CoreState.ROM = rom;
                var vm = new ToolROMRebuildViewModel();
                string missing = Path.Combine(Path.GetTempPath(), "feb_rb_missing_" + Guid.NewGuid().ToString("N") + ".gba");
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.OriginalMissing, vm.MakeRebuild(missing, 0x00000000, "out.rebuild"));
            }
            finally { CoreState.ROM = prevRom; }
        }

        [Fact]
        public void MakeRebuild_BadAddress_ReturnsBadAddress()
        {
            var prevRom = CoreState.ROM;
            string origPath = Path.Combine(Path.GetTempPath(), "feb_rb_orig_" + Guid.NewGuid().ToString("N") + ".gba");
            try
            {
                var rom = new ROM();
                rom.SwapNewROMDataDirect(new byte[0x400]);
                CoreState.ROM = rom;
                File.WriteAllBytes(origPath, new byte[0x400]);

                var vm = new ToolROMRebuildViewModel();
                // Misaligned address is a hard failure, checked before reading the original ROM.
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.BadAddress, vm.MakeRebuild(origPath, 0x09000001, "out.rebuild"));
            }
            finally
            {
                CoreState.ROM = prevRom;
                try { File.Delete(origPath); } catch { }
            }
        }

        [Fact]
        public void MakeRebuild_WritesReport_WhenOriginalCrcSkipped()
        {
            // No RomInfo -> orignal_crc32 == 0 -> CRC check skipped (headless path).
            var prevRom = CoreState.ROM;
            string origPath = Path.Combine(Path.GetTempPath(), "feb_rb_orig2_" + Guid.NewGuid().ToString("N") + ".gba");
            string outPath = Path.Combine(Path.GetTempPath(), "feb_rb_out_" + Guid.NewGuid().ToString("N") + ".rebuild");
            try
            {
                byte[] cur = new byte[0x4000];
                for (int i = 0; i < cur.Length; i++) cur[i] = (byte)(i & 0xFF);
                var rom = new ROM();
                rom.SwapNewROMDataDirect(cur);
                CoreState.ROM = rom;

                byte[] orig = (byte[])cur.Clone();
                orig[0x40] ^= 0xFF;
                File.WriteAllBytes(origPath, orig);

                var vm = new ToolROMRebuildViewModel();
                // Aligned, >= 0x200, < ROM length -> a safe offset for this in-memory ROM.
                var r = vm.MakeRebuild(origPath, 0x00001000, outPath);
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.Ok, r);
                Assert.True(File.Exists(outPath));
                Assert.Contains("@_CRC32 ", File.ReadAllText(outPath));
            }
            finally
            {
                CoreState.ROM = prevRom;
                try { File.Delete(origPath); } catch { }
                try { File.Delete(outPath); } catch { }
            }
        }

        [Fact]
        public void SuggestedName_MirrorsWinFormsPrefix()
        {
            Assert.Equal("R.20260616123000.rebuild", ToolROMRebuildViewModel.SuggestedName("20260616123000"));
        }
    }

    // Needs a real ROM so RomInfo.extends_address / orignal_crc32 are set; skips without ROMs.
    [Collection("SharedState")]
    public class ToolROMRebuildRomTests : IClassFixture<RomFixture>
    {
        readonly RomFixture _fixture;
        readonly ITestOutputHelper _output;
        public ToolROMRebuildRomTests(RomFixture fixture, ITestOutputHelper output)
        { _fixture = fixture; _output = output; }

        [Fact]
        public void Load_DefaultsAddressToExtends_ForRealRom()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }
            var vm = new ToolROMRebuildViewModel();
            bool ok = vm.Load();
            Assert.True(ok);
            uint expected = U.toOffset(CoreState.ROM.RomInfo.extends_address);
            Assert.Equal(expected, vm.RebuildAddress);
            Assert.Equal(expected, vm.DefaultRebuildAddress());
        }

        [Fact]
        public void MakeRebuild_WrongOriginal_ReturnsOriginalNotMatching()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }
            string wrong = Path.Combine(Path.GetTempPath(), "feb_rb_wrong_" + Guid.NewGuid().ToString("N") + ".gba");
            try
            {
                // Zeros -> CRC32 won't match the loaded game's known-original CRC32.
                File.WriteAllBytes(wrong, new byte[0x1000]);
                var vm = new ToolROMRebuildViewModel();
                uint addr = U.toOffset(CoreState.ROM.RomInfo.extends_address);
                Assert.Equal(ToolROMRebuildViewModel.MakeResult.OriginalNotMatching, vm.MakeRebuild(wrong, addr, "out.rebuild"));
            }
            finally { try { File.Delete(wrong); } catch { } }
        }

        // ---- #1261 RebuildRom: the FULL produce→apply→write-rebuilt-ROM VM flow ----

        [Fact]
        public void RebuildRom_RealFE8U_ProducesValidRebuiltRom()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }
            // This proof is calibrated on FE8U (the gate is open + faithful there per #1261 e2e).
            if (_fixture.Version != "FE8U") { _output.WriteLine("SKIP: not FE8U (" + _fixture.Version + ")"); return; }

            // MakeWithProducer's data path refuses (incomplete) when the event-script disassembler
            // is unwired — EventCondForm + the ScanScript-family forms are re-reported at runtime.
            // The running Avalonia app wires these on ROM load (MainWindow); replicate that here so
            // this functional test exercises the GATE-OPEN success path rather than a refusal.
            var savedEvent = CoreState.EventScript;
            var savedProcs = CoreState.ProcsScript;
            var savedAi = CoreState.AIScript;
            string outPath = Path.Combine(Path.GetTempPath(), "feb_rb_rebuilt_" + Guid.NewGuid().ToString("N") + ".gba");
            try
            {
                if (!TryWireEventScripts())
                {
                    _output.WriteLine("SKIP: event-script definitions unavailable in this env (disasm cannot be wired).");
                    return;
                }

                var vm = new ToolROMRebuildViewModel();
                // NOTE: vm.Load() returns false on a VANILLA FE8U (extends_address == end-of-ROM,
                // so "no extended region") — the WF-parity gate for the analysis tool. In the real
                // GUI the loaded ROM is the user's MODIFIED ROM (larger than 16MB), where Load()
                // returns true; here the only available ROM is the vanilla one. RebuildRom reads
                // CoreState.ROM directly and does NOT require Load(), so the full pipeline still runs
                // (exactly as the #1261 e2e round-trip proves a vanilla FE8U rebuilds faithfully).

                // The #1261-proven faithful relocate address (0 Missing! per the e2e round-trip).
                uint rebuildAddress = ToolROMRebuildViewModel.DefaultFullRebuildAddress;
                Assert.True(U.isPadding4(rebuildAddress));

                string original = _fixture.RomPath;
                Assert.NotNull(original);

                var progress = new CollectingProgress();
                var result = vm.RebuildRom(original, rebuildAddress, outPath, progress, CancellationToken.None);

                // On a vanilla FE8U the gate is OPEN and the apply leaves 0 Missing! at 0x00B00000,
                // so the full flow must succeed and write a valid rebuilt ROM.
                Assert.True(result == ToolROMRebuildViewModel.RebuildResult.Ok,
                    "RebuildRom did not succeed (result=" + result + "). LastMessage: " + vm.LastMessage);

                // The output ROM was written and is non-empty.
                Assert.True(File.Exists(outPath), "the rebuilt ROM file must be written");
                long fileLen = new FileInfo(outPath).Length;
                Assert.True(fileLen > 0, "the rebuilt ROM must be non-empty");
                Assert.Equal(vm.LastRebuiltSize, (int)fileLen);

                // The rebuilt bytes re-load as a structurally valid FE8U (RELOCATE moved real
                // structs above 0x00B00000 while keeping the ROM a valid, re-detectable FE8U).
                var reload = new ROM();
                Assert.True(reload.Load(outPath, out string _), "the rebuilt ROM must re-load");
                Assert.Equal(8, reload.RomInfo.version);
                Assert.False(reload.RomInfo.is_multibyte);

                // The non-rebuild base region [0, rebuildAddress) is preserved byte-for-byte from
                // the clean original (Apply seeds the output from the vanilla base).
                var vanilla = new ROM();
                Assert.True(vanilla.Load(original, out string _));
                byte[] van = vanilla.Data;
                byte[] reb = reload.Data;
                Assert.True(van.Length >= rebuildAddress && reb.Length >= rebuildAddress);
                for (uint i = 0; i < rebuildAddress; i++)
                {
                    if (reb[i] != van[i])
                    {
                        Assert.Fail($"rebuilt base region diverges from vanilla at 0x{i:X}: "
                            + $"expected 0x{van[i]:X2} got 0x{reb[i]:X2}");
                    }
                }

                _output.WriteLine($"[#1261 VM e2e] FE8U RebuildRom OK: wrote {fileLen:X} bytes to {outPath}; "
                    + $"rebuilt re-detects version=8; base [0,0x{rebuildAddress:X}) preserved; "
                    + $"progress lines={progress.Count}.");
            }
            finally
            {
                CoreState.EventScript = savedEvent;
                CoreState.ProcsScript = savedProcs;
                CoreState.AIScript = savedAi;
                try { File.Delete(outPath); } catch { }
            }
        }

        [Fact]
        public void RebuildRom_WrongOriginal_ReturnsOriginalNotMatching_NoFileWritten()
        {
            if (!_fixture.IsAvailable) { _output.WriteLine("SKIP: no ROM available"); return; }
            if (_fixture.RomPath == null) { _output.WriteLine("SKIP: no ROM path"); return; }
            string wrong = Path.Combine(Path.GetTempPath(), "feb_rb_wrong2_" + Guid.NewGuid().ToString("N") + ".gba");
            string outPath = Path.Combine(Path.GetTempPath(), "feb_rb_out2_" + Guid.NewGuid().ToString("N") + ".gba");
            try
            {
                // A LOADABLE-but-modified ROM: copy the real ROM (so ROM.Load succeeds and it is
                // recognized as the same game) then flip a body byte so its CRC32 no longer matches
                // the loaded game's known-original CRC32 -> OriginalNotMatching (NOT Unreadable).
                byte[] copy = File.ReadAllBytes(_fixture.RomPath);
                copy[0x1000] ^= 0xFF;   // well past the header; keeps the ROM loadable, breaks the CRC
                File.WriteAllBytes(wrong, copy);

                var vm = new ToolROMRebuildViewModel();
                uint addr = ToolROMRebuildViewModel.DefaultFullRebuildAddress;
                var result = vm.RebuildRom(wrong, addr, outPath);
                Assert.Equal(ToolROMRebuildViewModel.RebuildResult.OriginalNotMatching, result);
                // A rejected rebuild must NOT have written an output ROM.
                Assert.False(File.Exists(outPath));
            }
            finally
            {
                try { File.Delete(wrong); } catch { }
                try { File.Delete(outPath); } catch { }
            }
        }

        /// <summary>
        /// Wire CoreState.EventScript/ProcsScript/AIScript exactly as the running Avalonia app does
        /// on ROM load (MainWindow.axaml.cs). Returns false if the event-script definitions cannot
        /// be loaded in this environment (so the dependent test can cleanly skip rather than fail on
        /// an env gap unrelated to the rebuild flow).
        /// </summary>
        static bool TryWireEventScripts()
        {
            try
            {
                var ev = new EventScript();
                ev.Load(EventScript.EventScriptType.Event);
                CoreState.EventScript = ev;

                var pr = new EventScript();
                pr.Load(EventScript.EventScriptType.Procs);
                CoreState.ProcsScript = pr;

                var ai = new EventScript(16);
                ai.Load(EventScript.EventScriptType.AI);
                CoreState.AIScript = ai;
                return true;
            }
            catch
            {
                return false;
            }
        }

        sealed class CollectingProgress : IProgress<string>
        {
            public int Count { get; private set; }
            public void Report(string value) { Count++; }
        }
    }
}
