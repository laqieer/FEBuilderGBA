using System;
using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #365 regression tests. Verifies that CCBranchEditorViewModel's
    /// "Upstream Chain" (= classes that promote INTO the currently selected class)
    /// matches the WinForms <see cref="CCBranchForm.AddressList_SelectedIndexChanged"/>
    /// byte-scan logic for every class index in FE8U.
    ///
    /// Two pre-fix bugs we regress against:
    ///   1) Avalonia iterated up to a hardcoded 0xFF instead of the actual class
    ///      count, polluting the upstream list with bytes read past the class table.
    ///   2) Avalonia did not skip class 0; WinForms wraps the scan with
    ///      <c>if (class_id >= 1)</c>, so class 0 must show no upstream.
    /// </summary>
    [Collection("SharedState")]
    public class CCBranchEditorUpstreamChainTests
    {
        readonly ITestOutputHelper _output;

        public CCBranchEditorUpstreamChainTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Loads FE8U.gba and sets CoreState.ROM. Returns null and writes a skip
        /// message to the test output if the ROM is not available.
        /// Caller MUST restore CoreState.ROM afterwards.
        /// </summary>
        ROM? LoadFE8U()
        {
            string? path = TestRomLocator.FindRom("FE8U");
            if (path == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found in roms/ or ROMS_DIR");
                return null;
            }
            var rom = new ROM();
            if (!rom.Load(path, out _))
            {
                _output.WriteLine("SKIP: FE8U.gba failed to load");
                return null;
            }
            CoreState.ROM = rom;
            return rom;
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Replicate the WinForms CCBranchForm scan exactly:
        /// for each class index i in 0..datcount-1, mark i as upstream of
        /// <paramref name="classId"/> when <c>u8(branchBase + i*2) == classId</c>
        /// OR <c>u8(branchBase + i*2 + 1) == classId</c>.
        /// Class 0 has no upstream (matching WinForms <c>if (class_id >= 1)</c>).
        /// </summary>
        static List<uint> WinFormsExpectedUpstream(ROM rom, uint classId, int datcount)
        {
            var result = new List<uint>();
            if (classId == 0) return result;
            uint branchBase = rom.p32(rom.RomInfo.ccbranch_pointer);
            for (uint i = 0; i < (uint)datcount; i++)
            {
                uint addr = branchBase + i * 2;
                uint promo1 = rom.u8(addr + 0);
                uint promo2 = rom.u8(addr + 1);
                if (promo1 == classId || promo2 == classId)
                {
                    result.Add(i);
                }
            }
            return result;
        }

        /// <summary>
        /// Parse the comma-separated upstream chain string emitted by
        /// CCBranchEditorViewModel into a set of class-index hex values.
        /// "(none)" -> empty set.
        /// </summary>
        static List<uint> ParseUpstreamChain(string s)
        {
            var result = new List<uint>();
            if (string.IsNullOrEmpty(s) || s == "(none)") return result;
            foreach (var raw in s.Split(','))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                // Format: "0xXX Name"; take the hex prefix.
                int space = token.IndexOf(' ');
                string hex = space > 0 ? token.Substring(0, space) : token;
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    hex = hex.Substring(2);
                if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint v))
                    result.Add(v);
            }
            return result;
        }

        // -------------------------------------------------------------------
        // Authoritative regression: WinForms-parity scan for every class
        // -------------------------------------------------------------------

        [Fact]
        public void FE8U_UpstreamChain_MatchesWinFormsByteScan()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadFE8U();
                if (rom == null) return;
                if (rom.RomInfo.ccbranch_pointer == 0)
                {
                    _output.WriteLine("SKIP: ROM has no CC branch table");
                    return;
                }

                var vm = new CCBranchEditorViewModel();
                var list = vm.LoadCCBranchList();
                Assert.NotEmpty(list);
                int datcount = list.Count;

                int mismatches = 0;
                for (int idx = 1; idx < datcount; idx++) // skip class 0; covered by dedicated test
                {
                    var entry = list[idx];
                    vm.LoadCCBranch(entry.addr);
                    var actual = ParseUpstreamChain(vm.UpstreamChain).OrderBy(x => x).ToList();
                    var expected = WinFormsExpectedUpstream(rom, (uint)idx, datcount).OrderBy(x => x).ToList();
                    if (!actual.SequenceEqual(expected))
                    {
                        mismatches++;
                        _output.WriteLine($"class 0x{idx:X2}: expected=[{string.Join(",", expected.Select(v => $"0x{v:X2}"))}] actual=[{string.Join(",", actual.Select(v => $"0x{v:X2}"))}]");
                    }
                }
                Assert.Equal(0, mismatches);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // -------------------------------------------------------------------
        // Class 0 guard regression
        // -------------------------------------------------------------------

        [Fact]
        public void FE8U_UpstreamChain_Class0_ReturnsNone()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadFE8U();
                if (rom == null) return;
                if (rom.RomInfo.ccbranch_pointer == 0)
                {
                    _output.WriteLine("SKIP: ROM has no CC branch table");
                    return;
                }

                var vm = new CCBranchEditorViewModel();
                var list = vm.LoadCCBranchList();
                Assert.NotEmpty(list);

                vm.LoadCCBranch(list[0].addr);
                // Either "(none)" or empty string is acceptable; both indicate no upstream.
                Assert.True(vm.UpstreamChain == "(none)" || vm.UpstreamChain == "",
                    $"Expected class 0 upstream to be empty/(none), got '{vm.UpstreamChain}'");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // -------------------------------------------------------------------
        // Over-scan regression
        // -------------------------------------------------------------------

        [Fact]
        public void FE8U_UpstreamChain_AllResultsWithinClassCount()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadFE8U();
                if (rom == null) return;
                if (rom.RomInfo.ccbranch_pointer == 0)
                {
                    _output.WriteLine("SKIP: ROM has no CC branch table");
                    return;
                }

                var vm = new CCBranchEditorViewModel();
                var list = vm.LoadCCBranchList();
                Assert.NotEmpty(list);
                uint classCount = (uint)list.Count;

                for (int idx = 1; idx < list.Count; idx++)
                {
                    vm.LoadCCBranch(list[idx].addr);
                    var reported = ParseUpstreamChain(vm.UpstreamChain);
                    foreach (uint v in reported)
                    {
                        Assert.True(v < classCount,
                            $"class 0x{idx:X2}: upstream entry 0x{v:X2} >= classCount 0x{classCount:X2}");
                    }
                }
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // -------------------------------------------------------------------
        // Smoke test (Paladin)
        // -------------------------------------------------------------------

        [Fact]
        public void FE8U_UpstreamChain_Paladin_HasNonEmptyUpstream()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadFE8U();
                if (rom == null) return;
                if (rom.RomInfo.ccbranch_pointer == 0)
                {
                    _output.WriteLine("SKIP: ROM has no CC branch table");
                    return;
                }

                var vm = new CCBranchEditorViewModel();
                var list = vm.LoadCCBranchList();
                const int paladinIdx = 0x07;
                if (list.Count <= paladinIdx)
                {
                    _output.WriteLine("SKIP: ROM does not expose class 0x07");
                    return;
                }

                vm.LoadCCBranch(list[paladinIdx].addr);
                var reported = ParseUpstreamChain(vm.UpstreamChain);
                // Authoritative parity is covered by FE8U_UpstreamChain_MatchesWinFormsByteScan;
                // here we just smoke-test that Paladin is reported as a promoted class
                // (it must have at least one upstream entry in a stock FE8U).
                Assert.NotEmpty(reported);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        // -------------------------------------------------------------------
        // Null ROM safety
        // -------------------------------------------------------------------

        [Fact]
        public void NoRom_UpstreamChain_DoesNotThrow()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new CCBranchEditorViewModel();
                // Both calls must be safe.
                var list = vm.LoadCCBranchList();
                Assert.Empty(list);
                vm.LoadCCBranch(0); // should not throw
                Assert.True(vm.UpstreamChain == "" || vm.UpstreamChain == "(none)");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }
    }
}
