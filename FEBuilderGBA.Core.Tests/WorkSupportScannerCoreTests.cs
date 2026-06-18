using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the All-Work-Support scanner (#1196). Each test builds a
    /// throwaway temp dir tree mirroring <c>config/etc/.../worksupport_.txt</c>
    /// plus the project ROM + <c>.updateinfo.txt</c> sidecar.
    /// </summary>
    [Collection("SharedState")]
    public class WorkSupportScannerCoreTests : IDisposable
    {
        readonly string _root;
        readonly string _etcDir;
        readonly string _projDir;
        readonly ROM _savedRom;

        public WorkSupportScannerCoreTests()
        {
            // OtherLangLine (used by the update-info parser) reads CoreState.ROM;
            // null is a safe no-op for the parser, so isolate it.
            _savedRom = CoreState.ROM;
            CoreState.ROM = null;

            _root = Path.Combine(Path.GetTempPath(), "fe_worksupport_" + Guid.NewGuid().ToString("N"));
            _etcDir = Path.Combine(_root, "config", "etc", "FE8");
            _projDir = Path.Combine(_root, "projects");
            Directory.CreateDirectory(_etcDir);
            Directory.CreateDirectory(_projDir);
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch { /* best effort */ }
        }

        string WriteRom(string name)
        {
            string path = Path.Combine(_projDir, name);
            File.WriteAllBytes(path, new byte[16]);
            return path;
        }

        void WriteWorksupport(string romPath, string subdir = "FE8")
        {
            string dir = Path.Combine(_root, "config", "etc", subdir);
            Directory.CreateDirectory(dir);
            // worksupport_.txt is index<TAB>value TSV; field 0 = rom filename.
            File.WriteAllText(Path.Combine(dir, "worksupport_.txt"),
                "0\t" + romPath + "\n1\t0\n");
        }

        void WriteUpdateInfo(string romPath, string body)
        {
            string info = Path.ChangeExtension(romPath, ".updateinfo.txt");
            File.WriteAllText(info, body);
        }

        [Fact]
        public void Scan_MissingDir_ReturnsEmpty()
        {
            var list = WorkSupportScannerCore.Scan(Path.Combine(_root, "does-not-exist"));
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Fact]
        public void Scan_EmptyDir_ReturnsEmpty()
        {
            var list = WorkSupportScannerCore.Scan(Path.Combine(_root, "config", "etc"));
            Assert.NotNull(list);
            Assert.Empty(list);
        }

        [Fact]
        public void Scan_ValidProject_ReturnsOneWithNameAndLogo()
        {
            string rom = WriteRom("game.gba");
            WriteWorksupport(rom);
            WriteUpdateInfo(rom, "NAME=My Hack\nLOGO_FILENAME=logo.png\n");
            File.WriteAllBytes(Path.Combine(_projDir, "logo.png"), new byte[8]);

            var list = WorkSupportScannerCore.Scan(Path.Combine(_root, "config", "etc"));

            Assert.Single(list);
            Assert.Equal("My Hack", list[0].Name);
            Assert.Equal(rom, list[0].RomFilename);
            Assert.Equal(Path.Combine(_projDir, "logo.png"), list[0].LogoFilename);
            Assert.False(list[0].IsUpdateMark);
        }

        [Fact]
        public void Scan_NameAbsent_FallsBackToRomStem()
        {
            string rom = WriteRom("fallback.gba");
            WriteWorksupport(rom);
            WriteUpdateInfo(rom, "AUTHOR=someone\n");

            var list = WorkSupportScannerCore.Scan(Path.Combine(_root, "config", "etc"));

            Assert.Single(list);
            Assert.Equal("fallback", list[0].Name);
            Assert.Equal("", list[0].LogoFilename);
        }

        [Fact]
        public void Scan_RomMissing_Skips()
        {
            string rom = Path.Combine(_projDir, "ghost.gba"); // not created
            WriteWorksupport(rom);
            WriteUpdateInfo(rom, "NAME=Ghost\n");

            var list = WorkSupportScannerCore.Scan(Path.Combine(_root, "config", "etc"));
            Assert.Empty(list);
        }

        [Fact]
        public void Scan_UpdateInfoMissing_Skips()
        {
            string rom = WriteRom("noinfo.gba");
            WriteWorksupport(rom);
            // no .updateinfo.txt

            var list = WorkSupportScannerCore.Scan(Path.Combine(_root, "config", "etc"));
            Assert.Empty(list);
        }

        [Fact]
        public void Scan_MalformedWorksupport_DoesNotThrow_Skips()
        {
            string dir = Path.Combine(_root, "config", "etc", "BAD");
            Directory.CreateDirectory(dir);
            // No tab-separated field 0 / garbage content.
            File.WriteAllText(Path.Combine(dir, "worksupport_.txt"), "this is not a tsv row\n\x00\x01garbage");

            var ex = Record.Exception(() =>
            {
                var list = WorkSupportScannerCore.Scan(Path.Combine(_root, "config", "etc"));
                Assert.Empty(list);
            });
            Assert.Null(ex);
        }

        [Fact]
        public void GetUpdateInfo_ProgressiveTrim_FindsTrimmedSidecar()
        {
            // BSFE_1.0.gba -> BSFE.updateinfo.txt (trimmed at the underscore).
            string rom = WriteRom("BSFE_1.0.gba");
            string trimmedInfo = Path.Combine(_projDir, "BSFE.updateinfo.txt");
            File.WriteAllText(trimmedInfo, "NAME=BSFE\n");

            string found = WorkSupportScannerCore.GetUpdateInfo(rom);
            Assert.Equal(trimmedInfo, found);
        }

        [Fact]
        public void LoadUpdateInfo_ParsesKeyValue_SkipsComments()
        {
            string info = Path.Combine(_projDir, "p.updateinfo.txt");
            File.WriteAllText(info, "#comment\nNAME=Hello\n\nBADLINE\nLOGO_FILENAME=a.png\n");

            Dictionary<string, string> d = WorkSupportScannerCore.LoadUpdateInfo(info);
            Assert.Equal("Hello", d["NAME"]);
            Assert.Equal("a.png", d["LOGO_FILENAME"]);
            Assert.False(d.ContainsKey("BADLINE"));
        }
    }
}
