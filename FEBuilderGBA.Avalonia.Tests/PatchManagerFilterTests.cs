// SPDX-License-Identifier: GPL-3.0-or-later
// Avalonia parity tests for PatchManagerViewModel.ApplyFilter HARDCODING_*/"!"
// token handling (#1376).
//
// The HardCoding links on the Unit/Class/Item editors seed the Patch Manager
// filter box with "HARDCODING_{UNIT|CLASS|ITEM}=NN"; ApplyFilter must filter the
// list to the patches that hard-code that id (via PatchFilterCore), not show 0.
// These tests seed the VM's private _allPatches with PatchEntry rows pointing at
// temp PATCH_*.txt files, set FilterText (which triggers ApplyFilter), and assert
// FilteredPatches reflects the token semantics.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class PatchManagerFilterTests
    {
        static ROM MakeFe8uRom(Action<byte[]> seed)
        {
            var data = new byte[0x1000000];
            seed?.Invoke(data);
            var rom = new ROM();
            rom.LoadLow("pm-filter.gba", data, "BE8E01"); // FE8U
            return rom;
        }

        static string MakeTempVerDir(out string root)
        {
            root = Path.Combine(Path.GetTempPath(), "fe_pmf_" + Guid.NewGuid().ToString("N"));
            string verDir = Path.Combine(root, "config", "patch2", "FE8U");
            Directory.CreateDirectory(verDir);
            return verDir;
        }

        static string WritePatch(string verDir, string name, params string[] lines)
        {
            string path = Path.Combine(verDir, "PATCH_" + name + ".txt");
            File.WriteAllLines(path, lines);
            return path;
        }

        // Seed the VM's private _allPatches list with entries pointing at the given files.
        static void SeedAllPatches(PatchManagerViewModel vm, IEnumerable<(string name, string file)> entries)
        {
            var field = typeof(PatchManagerViewModel)
                .GetField("_allPatches", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            var list = (List<PatchEntry>)field.GetValue(vm);
            list.Clear();
            foreach (var (name, file) in entries)
                list.Add(new PatchEntry { Name = name, PatchFilePath = file });
        }

        static void TryDelete(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        }

        [Fact]
        public void HardCodingUnitToken_FiltersToMatchingPatch()
        {
            string verDir = MakeTempVerDir(out string root);
            var savedRom = CoreState.ROM;
            var savedLang = CoreState.Language;
            try
            {
                var rom = MakeFe8uRom(d => { d[0x2000] = 0x01; d[0x2400] = 0x05; });
                CoreState.ROM = rom;
                CoreState.Language = "en";

                string unitFile = WritePatch(verDir, "EirikaUnit",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT");   // unit id 0x01
                string classFile = WritePatch(verDir, "SomeClass",
                    "TYPE=ADDR", "ADDRESS=0x2400", "ADDRESS_TYPE=CLASS");  // class id 0x05
                string plainFile = WritePatch(verDir, "Plain",
                    "TYPE=ADDR", "ADDRESS=0x2400", "ADDRESS_TYPE=ITEM");   // item id 0x05

                var vm = new PatchManagerViewModel();
                SeedAllPatches(vm, new[]
                {
                    ("EirikaUnit", unitFile),
                    ("SomeClass", classFile),
                    ("Plain", plainFile),
                });

                vm.FilterText = "HARDCODING_UNIT=01";

                Assert.Single(vm.FilteredPatches);
                Assert.Equal("EirikaUnit", vm.FilteredPatches[0].Name);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        [Fact]
        public void HardCodingClassToken_FiltersToMatchingPatch()
        {
            string verDir = MakeTempVerDir(out string root);
            var savedRom = CoreState.ROM;
            var savedLang = CoreState.Language;
            try
            {
                var rom = MakeFe8uRom(d => { d[0x2400] = 0x0C; });
                CoreState.ROM = rom;
                CoreState.Language = "en";

                string classFile = WritePatch(verDir, "MyClass",
                    "TYPE=ADDR", "ADDRESS=0x2400", "ADDRESS_TYPE=CLASS");

                var vm = new PatchManagerViewModel();
                SeedAllPatches(vm, new[] { ("MyClass", classFile) });

                vm.FilterText = "HARDCODING_CLASS=0C";

                Assert.Single(vm.FilteredPatches);
                Assert.Equal("MyClass", vm.FilteredPatches[0].Name);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        [Fact]
        public void HardCodingItemToken_FiltersToMatchingPatch()
        {
            string verDir = MakeTempVerDir(out string root);
            var savedRom = CoreState.ROM;
            var savedLang = CoreState.Language;
            try
            {
                var rom = MakeFe8uRom(d => { d[0x2800] = 0x21; });
                CoreState.ROM = rom;
                CoreState.Language = "en";

                string itemFile = WritePatch(verDir, "MyItem",
                    "TYPE=ADDR", "ADDRESS=0x2800", "ADDRESS_TYPE=ITEM");

                var vm = new PatchManagerViewModel();
                SeedAllPatches(vm, new[] { ("MyItem", itemFile) });

                vm.FilterText = "HARDCODING_ITEM=21";

                Assert.Single(vm.FilteredPatches);
                Assert.Equal("MyItem", vm.FilteredPatches[0].Name);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        [Fact]
        public void InstalledOnlyToken_FiltersToInstalledPatches()
        {
            string verDir = MakeTempVerDir(out string root);
            var savedRom = CoreState.ROM;
            var savedLang = CoreState.Language;
            try
            {
                // installed: PATCHED_IF bytes match ROM; not-installed: mismatch.
                var rom = MakeFe8uRom(d => { d[0x5000] = 0xDE; d[0x5001] = 0xAD; });
                CoreState.ROM = rom;
                CoreState.Language = "en";

                string installed = WritePatch(verDir, "Installed",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT",
                    "PATCHED_IF:0x5000=0xDE 0xAD");
                string notInstalled = WritePatch(verDir, "NotInstalled",
                    "TYPE=ADDR", "ADDRESS=0x2000", "ADDRESS_TYPE=UNIT",
                    "PATCHED_IF:0x5000=0x00 0x00");

                var vm = new PatchManagerViewModel();
                SeedAllPatches(vm, new[]
                {
                    ("Installed", installed),
                    ("NotInstalled", notInstalled),
                });

                vm.FilterText = "!";

                Assert.Single(vm.FilteredPatches);
                Assert.Equal("Installed", vm.FilteredPatches[0].Name);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Language = savedLang;
                TryDelete(root);
            }
        }

        [Fact]
        public void PlainSubstringFilter_StillWorks()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeFe8uRom(null);

                var vm = new PatchManagerViewModel();
                SeedAllPatches(vm, new[]
                {
                    ("Skill System", ""),
                    ("Eirika Campaign", ""),
                });

                vm.FilterText = "skill";

                Assert.Single(vm.FilteredPatches);
                Assert.Equal("Skill System", vm.FilteredPatches[0].Name);
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void EmptyFilter_ReturnsAll()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeFe8uRom(null);

                var vm = new PatchManagerViewModel();
                SeedAllPatches(vm, new[] { ("A", ""), ("B", ""), ("C", "") });

                // Trigger a filter pass (FilterText defaults to "" so an immediate
                // "" set would be a no-op), then clear it to assert "all".
                vm.FilterText = "A";
                Assert.Single(vm.FilteredPatches);

                vm.FilterText = "";
                Assert.Equal(3, vm.FilteredPatches.Count);
            }
            finally { CoreState.ROM = savedRom; }
        }
    }
}
