using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Regression tests for #355 (Avalonia GUI: Invalid texts in Status Parameters Editor).
    ///
    /// The bug: <see cref="StatusParamViewModel.LoadStatusParamList"/> and
    /// <see cref="StatusParamViewModel.LoadStatusParam"/> were calling
    /// <c>rom.getString(toOffset(u32@+12), 32)</c> directly, which reads the raw bytes
    /// at the first level of indirection. The actual struct layout requires TWO steps:
    /// the u32 at +12 is a pointer to another u32 which is either a text ID (Huffman-decoded)
    /// or a string pointer.
    ///
    /// WinForms reference: <c>FEBuilderGBA/StatusParamForm.cs</c> <c>GetParamName</c>.
    /// </summary>
    [Collection("SharedState")]
    public class StatusParamNameResolutionTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public StatusParamNameResolutionTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// ROM-agnostic guard: the displayed list must not contain Unicode replacement
        /// characters or C0 control characters - both are markers of the bug where
        /// little-endian u32 text IDs are interpreted as raw bytes.
        /// </summary>
        [Fact]
        public void LoadStatusParamList_NoGarbageCharacters()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping.");
                return;
            }

            var vm = new StatusParamViewModel();
            var list = vm.LoadStatusParamList(0);
            Assert.NotEmpty(list);
            _output.WriteLine($"StatusParam (table 0) entries: {list.Count}");

            foreach (var entry in list)
            {
                // Entries are formatted as "0xNN <name>". Extract <name> portion (after first space).
                string display = entry.name ?? string.Empty;
                int sp = display.IndexOf(' ');
                string name = sp >= 0 && sp + 1 < display.Length ? display.Substring(sp + 1) : display;

                Assert.DoesNotContain('�', name);
                foreach (char c in name)
                {
                    // Allow only printable / common whitespace - reject C0 controls.
                    Assert.True(c >= 0x20 || c == '\t' || c == ' ',
                        $"Entry {entry.tag:X2} '{display}' contains control char 0x{(int)c:X2}");
                }
            }
        }

        /// <summary>
        /// ROM-agnostic check: the StringText field on a loaded entry must be either
        /// empty (if the slot is unresolvable) or human-readable (no replacement chars,
        /// no C0 controls).
        /// </summary>
        [Fact]
        public void LoadStatusParam_StringText_IsHumanReadable()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping.");
                return;
            }

            var vm = new StatusParamViewModel();
            var list = vm.LoadStatusParamList(0);
            Assert.NotEmpty(list);

            vm.LoadStatusParam(list[0].addr);
            string text = vm.StringText ?? string.Empty;
            _output.WriteLine($"Entry 0 StringText = '{text}'");

            Assert.DoesNotContain('�', text);
            foreach (char c in text)
            {
                Assert.True(c >= 0x20 || c == '\t' || c == ' ',
                    $"StringText contains control char 0x{(int)c:X2}");
            }
        }

        /// <summary>
        /// FE8U-specific assertion: entry 0 must resolve to "Skill" (per the WinForms
        /// reference output verified against FE8U.gba). Skips when the loaded ROM is
        /// a different version. The exact strings are taken from the FE8U Huffman table.
        /// </summary>
        [Fact]
        public void LoadStatusParamList_FirstEntries_MatchWinFormsReference_FE8U()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping.");
                return;
            }

            if (_fixture.Version != "FE8U")
            {
                _output.WriteLine($"Loaded ROM is {_fixture.Version}, not FE8U; skipping.");
                return;
            }

            var vm = new StatusParamViewModel();
            var list = vm.LoadStatusParamList(0);
            Assert.True(list.Count >= 6, $"Expected at least 6 entries on FE8U, got {list.Count}");

            // Expected names (verified against StatusParamForm.GetParamName output for FE8U)
            string[] expected = { "Skill", "Spd", "Luck", "Def", "Res", "Move" };
            for (int i = 0; i < expected.Length; i++)
            {
                string display = list[i].name ?? string.Empty;
                Assert.Contains(expected[i], display);
            }
        }

        /// <summary>
        /// Cross-validation: for every entry in the list, the Avalonia ViewModel's
        /// displayed name must match the WinForms-equivalent resolution we recompute
        /// inline. This works on any ROM the fixture loads and protects against the
        /// reported indirection bug.
        /// </summary>
        [Fact]
        public void LoadStatusParamList_NamesMatchWinFormsResolution()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping.");
                return;
            }

            ROM rom = CoreState.ROM!;
            var vm = new StatusParamViewModel();
            var list = vm.LoadStatusParamList(0);
            Assert.NotEmpty(list);

            int comparisons = 0;
            foreach (var entry in list)
            {
                uint structAddr = entry.addr;
                string expected = WinFormsReferenceResolve(rom, structAddr);
                string display = entry.name ?? string.Empty;
                int sp = display.IndexOf(' ');
                string actualName = sp >= 0 && sp + 1 < display.Length ? display.Substring(sp + 1) : display;

                // Both implementations return potentially empty strings for unresolved slots;
                // when both empty, that is parity. When non-empty, they must match exactly.
                if (expected.Length == 0)
                {
                    // No reference name available - skip strict comparison but ensure no garbage.
                    Assert.DoesNotContain('�', actualName);
                    continue;
                }

                Assert.Equal(expected.Trim(), actualName.Trim());
                comparisons++;
            }
            _output.WriteLine($"Verified {comparisons} list entries against WinForms reference.");
            Assert.True(comparisons > 0, "Expected at least one resolved name to cross-check");
        }

        /// <summary>
        /// Cross-validation for the detail-view StringText field: load each entry and
        /// confirm StringText equals the WinForms-equivalent resolution.
        /// </summary>
        [Fact]
        public void LoadStatusParam_StringText_MatchesWinFormsResolution()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping.");
                return;
            }

            ROM rom = CoreState.ROM!;
            var vm = new StatusParamViewModel();
            var list = vm.LoadStatusParamList(0);
            Assert.NotEmpty(list);

            int verified = 0;
            int loadCount = Math.Min(8, list.Count);
            for (int i = 0; i < loadCount; i++)
            {
                string expected = WinFormsReferenceResolve(rom, list[i].addr);
                vm.LoadStatusParam(list[i].addr);
                string actual = vm.StringText ?? string.Empty;

                if (expected.Length == 0)
                {
                    Assert.DoesNotContain('�', actual);
                    continue;
                }
                Assert.Equal(expected.Trim(), actual.Trim());
                verified++;
            }
            _output.WriteLine($"Verified {verified} StringText loads against WinForms reference.");
            Assert.True(verified > 0, "Expected at least one StringText to cross-check");
        }

        /// <summary>
        /// Inline re-implementation of the WinForms <c>StatusParamForm.GetParamName</c>
        /// logic, used as the reference oracle in cross-validation tests. Kept self-contained
        /// so the test does not depend on WinForms types.
        /// </summary>
        static string WinFormsReferenceResolve(ROM rom, uint structAddr)
        {
            if (!U.isSafetyOffset(structAddr + 15, rom)) return string.Empty;

            uint strPtr = rom.u32(structAddr + 12);
            if (!U.isPointer(strPtr)) return string.Empty;

            uint nameAddrP = U.toOffset(strPtr);
            if (!U.isSafetyOffset(nameAddrP + 3, rom)) return string.Empty;

            uint id = rom.p32(nameAddrP);
            if (id <= 0x10) return string.Empty;

            string name = string.Empty;
            if (id > 0xFFFF && U.isSafetyOffset(id, rom))
            {
                try { name = rom.getString(id) ?? string.Empty; } catch { name = string.Empty; }
            }
            if (string.IsNullOrEmpty(name))
            {
                // Use NameResolver.GetTextById to match the implementation's strip-and-trim
                // semantics exactly (strips @XXXX codes AND raw C0 control chars, then trims).
                try { name = NameResolver.GetTextById(id) ?? string.Empty; } catch { name = string.Empty; }
            }
            return name?.TrimEnd('\0').Trim() ?? string.Empty;
        }
    }
}
