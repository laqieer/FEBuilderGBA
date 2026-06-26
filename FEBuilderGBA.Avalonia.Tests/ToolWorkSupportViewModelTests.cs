#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// VM-level tests for the single-work Work Support update flow (#1454). Verifies
    /// the VM reads the ROM hack's own <c>.updateinfo.txt</c> (CHECK_URL/UPDATE_URL)
    /// and delegates to the Core check + download/apply pipeline with injected
    /// (offline) network/ROM functions.
    /// </summary>
    [Collection("SharedState")]
    public class ToolWorkSupportViewModelTests : IDisposable
    {
        readonly string _root;
        readonly ROM _savedRom;

        public ToolWorkSupportViewModelTests()
        {
            _savedRom = CoreState.ROM;
            CoreState.ROM = null;
            _root = Path.Combine(Path.GetTempPath(), "fe_ws_vm_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        // A minimal ROM with just a Filename set, so the VM can resolve the sidecar.
        static ROM StubRom(string filename) => new ROM { Filename = filename };

        string SeedRomWithUpdateInfo(string updateinfoBody)
        {
            string rom = Path.Combine(_root, "hack.gba");
            File.WriteAllBytes(rom, new byte[16]);
            File.WriteAllText(Path.ChangeExtension(rom, ".updateinfo.txt"), updateinfoBody);
            return rom;
        }

        [Fact]
        public void Initialize_NoRom_HasNoUpdateInfo()
        {
            var vm = new ToolWorkSupportViewModel();
            vm.Initialize();
            Assert.True(vm.IsLoaded);
            Assert.False(vm.HasUpdateInfo);
            Assert.Equal("No ROM loaded.", vm.InfoText);
        }

        [Fact]
        public void Initialize_NoUpdateInfo_HasNoUpdateInfo()
        {
            string rom = Path.Combine(_root, "plain.gba");
            File.WriteAllBytes(rom, new byte[16]);
            CoreState.ROM = StubRom(rom);

            var vm = new ToolWorkSupportViewModel();
            vm.Initialize();
            Assert.True(vm.IsLoaded);
            Assert.False(vm.HasUpdateInfo);
        }

        [Fact]
        public void Initialize_WithUpdateInfo_ParsesNameAuthorCommunity()
        {
            string rom = SeedRomWithUpdateInfo(
                "NAME=My Hack\nAUTHOR=Someone\nCOMMUNITY_URL=http://discord/x\n" +
                "CHECK_URL=http://example.com/v\nCHECK_REGEX=ver=(\\d{8})\n");
            CoreState.ROM = StubRom(rom);

            var vm = new ToolWorkSupportViewModel();
            vm.Initialize();

            Assert.True(vm.HasUpdateInfo);
            Assert.Equal("My Hack", vm.Name);
            Assert.Equal("Someone", vm.Author);
            Assert.Equal("http://discord/x", vm.CommunityUrl);
            Assert.True(vm.UpdateinfoLines.ContainsKey("CHECK_URL"));
        }

        [Fact]
        public void CheckUpdate_RemoteNewer_ReturnsUpdateable()
        {
            string rom = SeedRomWithUpdateInfo(
                "CHECK_URL=http://example.com/v\nCHECK_REGEX=ver=(\\d{8})\n");
            CoreState.ROM = StubRom(rom);

            var vm = new ToolWorkSupportViewModel();
            vm.Initialize();

            var r = vm.CheckUpdate(
                httpGet: _ => "ver=20300101",
                httpHeadLastModified: _ => null,
                romDateTime: _ => new DateTime(2010, 1, 1));
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Updateable, r);
        }

        [Fact]
        public void CheckUpdate_RemoteOlder_ReturnsLatest()
        {
            string rom = SeedRomWithUpdateInfo(
                "CHECK_URL=http://example.com/v\nCHECK_REGEX=ver=(\\d{8})\n");
            CoreState.ROM = StubRom(rom);

            var vm = new ToolWorkSupportViewModel();
            vm.Initialize();

            var r = vm.CheckUpdate(
                httpGet: _ => "ver=20100101",
                httpHeadLastModified: _ => null,
                romDateTime: _ => new DateTime(2030, 1, 1));
            Assert.Equal(WorkSupportUpdateCheckCore.UpdateResult.Latest, r);
        }

        [Fact]
        public void ResolveDownloadUrl_DirectUrl_ReturnsUrl()
        {
            string rom = SeedRomWithUpdateInfo(
                "CHECK_URL=http://example.com/v\nCHECK_REGEX=@DIRECT_URL\n" +
                "UPDATE_URL=http://cdn/build.ups\nUPDATE_REGEX=@DIRECT_URL\n");
            CoreState.ROM = StubRom(rom);

            var vm = new ToolWorkSupportViewModel();
            vm.Initialize();

            var r = vm.ResolveDownloadUrl(_ => "");
            Assert.Equal(WorkSupportUpdateDownloadCore.ResolveStatus.Ok, r.Status);
            Assert.Equal("http://cdn/build.ups", r.Url);
        }

        [Fact]
        public void DownloadAndStage_RawUps_StagesIntoRomDir()
        {
            string rom = SeedRomWithUpdateInfo(
                "CHECK_URL=http://x\nCHECK_REGEX=@DIRECT_URL\n" +
                "UPDATE_URL=http://cdn/build.ups\nUPDATE_REGEX=@DIRECT_URL\n");
            CoreState.ROM = StubRom(rom);

            var vm = new ToolWorkSupportViewModel();
            vm.Initialize();

            byte[] ups = UPSUtilCore.MakeUPSData(new byte[512], new byte[512]);
            // Make src != dst so it's a real diffable UPS over the floor.
            byte[] dst = new byte[512];
            for (int i = 0; i < dst.Length; i++) dst[i] = (byte)i;
            ups = UPSUtilCore.MakeUPSData(new byte[512], dst);

            var r = vm.DownloadAndStage("http://cdn/build.ups",
                downloadFile: (u, dest) => { File.WriteAllBytes(dest, ups); return (true, ""); },
                extract: (a, d) => "should-not-extract");

            Assert.Equal(WorkSupportUpdateDownloadCore.StageStatus.Ok, r.Status);
            Assert.Single(r.UpsFiles);
            Assert.True(File.Exists(Path.Combine(_root, "build.ups")));
        }

        [Fact]
        public void ApplyUps_RoundTrip_ProducesPatchedGba()
        {
            string rom = SeedRomWithUpdateInfo("CHECK_URL=http://x\nCHECK_REGEX=@DIRECT_URL\n");
            CoreState.ROM = StubRom(rom);

            byte[] original = new byte[256];
            byte[] modified = new byte[256];
            for (int i = 0; i < modified.Length; i++) modified[i] = (byte)(i ^ 0x5A);

            string origPath = Path.Combine(_root, "vanilla.gba");
            File.WriteAllBytes(origPath, original);
            string upsPath = Path.Combine(_root, "p.ups");
            File.WriteAllBytes(upsPath, UPSUtilCore.MakeUPSData(original, modified));

            var vm = new ToolWorkSupportViewModel();
            vm.Initialize();

            var r = vm.ApplyUps(new List<string> { upsPath }, origPath,
                applyOne: (orig, ups) =>
                {
                    byte[] patch = File.ReadAllBytes(ups);
                    byte[] outb = UPSUtilCore.ApplyUPS(orig, patch, out string msg);
                    return (outb, outb == null ? msg : "", outb != null ? msg : "");
                });

            Assert.Equal(WorkSupportUpdateDownloadCore.ApplyStatus.Ok, r.Status);
            Assert.Equal(modified, File.ReadAllBytes(r.SavedRoms[0]));
        }

        [Fact]
        public void GetUpsDateTimeString_NoUps_ReturnsRomMarker()
        {
            string rom = Path.Combine(_root, "x.gba");
            File.WriteAllBytes(rom, new byte[16]);
            string s = ToolWorkSupportViewModel.GetUpsDateTimeString(rom);
            Assert.Contains("(ROM)", s);
        }
    }
}
