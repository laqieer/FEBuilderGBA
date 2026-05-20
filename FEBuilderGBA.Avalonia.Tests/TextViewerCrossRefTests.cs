using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Integration tests for <see cref="TextViewerViewModel.FindCrossReferences"/>.
    /// Requires a real ROM (via RomFixture) — skips gracefully when none is available,
    /// per the existing skippable-ROM pattern used across this test project.
    /// </summary>
    [Collection("SharedState")]
    public class TextViewerCrossRefTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public TextViewerCrossRefTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        /// Scan a real ROM's unit table and confirm that at least one unit's
        /// name text ID produces a Unit cross-reference. This protects against
        /// regressions of the original bug where the pointer FIELD address was
        /// scanned instead of the table base, producing zero references.
        /// </summary>
        [Fact]
        public void RealRom_FindsKnownUnitReference()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            ROM rom = CoreState.ROM!;
            var info = rom.RomInfo;
            uint unitBase = NameResolver.DerefPointer(rom, info.unit_pointer);
            Assert.NotEqual(0u, unitBase);
            Assert.True(U.isSafetyOffset(unitBase, rom), "unit table base must be a safe ROM offset");

            // Find the first non-empty unit name text ID (skip slot 0 which is often 0/null)
            uint foundTextId = 0;
            uint foundUnitId = 0;
            uint unitSize = info.unit_datasize;
            uint scanLimit = info.unit_maxcount != 0 ? info.unit_maxcount : 0x100u;
            for (uint i = 1; i < scanLimit; i++)
            {
                uint entryAddr = unitBase + i * unitSize;
                if (entryAddr + 2 > (uint)rom.Data.Length) break;
                uint tid = rom.u16(entryAddr);
                if (tid != 0)
                {
                    foundTextId = tid;
                    foundUnitId = i;
                    break;
                }
            }
            Assert.NotEqual(0u, foundTextId);
            _output.WriteLine($"Using unit {foundUnitId} with name text ID 0x{foundTextId:X4}");

            var vm = new TextViewerViewModel();
            List<string> refs = vm.FindCrossReferences(foundTextId);

            Assert.NotEmpty(refs);
            // At minimum we should see the originating unit
            string expected = $"Unit 0x{foundUnitId:X02}";
            Assert.Contains(refs, r => r.StartsWith(expected));
        }

        /// <summary>
        /// A text ID that no unit/class/item uses must yield an empty reference list,
        /// not a crash or false positives.
        /// </summary>
        [Fact]
        public void UnusedTextId_ReturnsEmpty()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            // Pick a text ID well beyond any plausible name/description ID. The ROM's
            // text pointer table is typically a few thousand entries, but any unit/class/item
            // text IDs are well under 0xFFFE. Choosing 0xFFFE makes a collision astronomically
            // unlikely while staying in u16 range.
            uint unusedId = 0xFFFE;

            var vm = new TextViewerViewModel();
            List<string> refs = vm.FindCrossReferences(unusedId);

            Assert.Empty(refs);
        }
    }
}
