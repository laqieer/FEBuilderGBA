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

        // ===========================================================
        // Issue #349 follow-up tests — Registry coverage of map/sound
        // room/world-map on a real FE8U ROM. Each test:
        //   1. resolves the relevant table base via NameResolver.DerefPointer
        //   2. picks the first entry that has a non-zero text-id field
        //   3. asserts FindCrossReferences returns a matching reference
        //      from the expected Kind.
        // ===========================================================

        /// <summary>
        /// Picks the first map-setting entry with a non-zero chapter-name text
        /// ID (offset +112 in FE8) and verifies the registry-driven
        /// FindCrossReferences returns at least one "MapSetting 0xNN" entry.
        /// </summary>
        [Fact]
        public void RealRom_FindsMapSettingReference()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            ROM rom = CoreState.ROM!;
            var info = rom.RomInfo;
            uint mapBase = NameResolver.DerefPointer(rom, info.map_setting_pointer);
            if (mapBase == 0)
            {
                _output.WriteLine("map_setting_pointer doesn't dereference; skipping.");
                return;
            }
            uint mapSize = info.map_setting_datasize;
            Assert.NotEqual(0u, mapSize);

            uint foundTextId = 0;
            uint foundMapId = 0;
            for (uint i = 0; i < 0x80; i++)
            {
                uint entry = mapBase + i * mapSize;
                if (entry + 114 > (uint)rom.Data.Length) break;
                // Try offset +112 (FE8 chapter-name text ID).
                uint tid = rom.u16(entry + 112);
                if (tid != 0 && tid < 0x7FFF)
                {
                    foundTextId = tid;
                    foundMapId = i;
                    break;
                }
            }
            Assert.NotEqual(0u, foundTextId);
            _output.WriteLine($"Using map {foundMapId} with chapter-name text ID 0x{foundTextId:X4}");

            var vm = new TextViewerViewModel();
            List<string> refs = vm.FindCrossReferences(foundTextId);

            Assert.NotEmpty(refs);
            string expected = $"MapSetting 0x{foundMapId:X02}";
            Assert.Contains(refs, r => r.StartsWith(expected));
        }

        /// <summary>
        /// Picks the first sound-room entry with a non-zero track-name text
        /// ID (offset +12 for FE7/8, +4 for FE6) and verifies the
        /// registry-driven FindCrossReferences returns a "SoundRoom" entry.
        /// </summary>
        [Fact]
        public void RealRom_FindsSoundRoomReference()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            ROM rom = CoreState.ROM!;
            var info = rom.RomInfo;
            uint srBase = NameResolver.DerefPointer(rom, info.sound_room_pointer);
            if (srBase == 0)
            {
                _output.WriteLine("sound_room_pointer doesn't dereference; skipping.");
                return;
            }
            uint srSize = info.sound_room_datasize;
            Assert.NotEqual(0u, srSize);

            // FE7/8 reads text ID at +12; FE6 at +4 (first text offset).
            uint textOffset = info.version == 6 ? 4u : 12u;

            uint foundTextId = 0;
            uint foundSrId = 0;
            for (uint i = 0; i < 0x400; i++)
            {
                uint entry = srBase + i * srSize;
                if (entry + textOffset + 2 > (uint)rom.Data.Length) break;
                // Stop on sentinel (terminator for FE7/8: u32 == 0xFFFFFFFF).
                uint terminator = rom.u32(entry);
                if (terminator == 0xFFFFFFFFu) break;
                uint tid = rom.u16(entry + textOffset);
                if (tid != 0 && tid < 0x7FFF)
                {
                    foundTextId = tid;
                    foundSrId = i;
                    break;
                }
            }
            Assert.NotEqual(0u, foundTextId);
            _output.WriteLine($"Using sound-room entry {foundSrId} with text ID 0x{foundTextId:X4}");

            var vm = new TextViewerViewModel();
            List<string> refs = vm.FindCrossReferences(foundTextId);

            Assert.NotEmpty(refs);
            string expected = $"SoundRoom 0x{foundSrId:X02}";
            Assert.Contains(refs, r => r.StartsWith(expected));
        }

        /// <summary>
        /// Picks the first world-map point entry (FE8 only) with a non-zero
        /// text ID at +28 and verifies the registry-driven
        /// FindCrossReferences returns a "WorldMapPoint" entry.
        /// </summary>
        [Fact]
        public void RealRom_FindsWorldMapPointReference_FE8Only()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            ROM rom = CoreState.ROM!;
            var info = rom.RomInfo;
            if (info.version != 8)
            {
                _output.WriteLine($"Not FE8 (version={info.version}); skipping.");
                return;
            }
            uint wmpBase = NameResolver.DerefPointer(rom, info.worldmap_point_pointer);
            if (wmpBase == 0)
            {
                _output.WriteLine("worldmap_point_pointer doesn't dereference; skipping.");
                return;
            }

            uint foundTextId = 0;
            uint foundWmpId = 0;
            for (uint i = 0; i < 0x100; i++)
            {
                uint entry = wmpBase + i * 32u;
                if (entry + 30 > (uint)rom.Data.Length) break;
                // WMP terminator: u32 at entry+0 == 0.
                if (rom.u32(entry) == 0) break;
                uint tid = rom.u16(entry + 28);
                if (tid != 0 && tid < 0x7FFF)
                {
                    foundTextId = tid;
                    foundWmpId = i;
                    break;
                }
            }
            if (foundTextId == 0)
            {
                _output.WriteLine("No world-map point with text ID found; skipping.");
                return;
            }
            _output.WriteLine($"Using WMP {foundWmpId} with text ID 0x{foundTextId:X4}");

            var vm = new TextViewerViewModel();
            List<string> refs = vm.FindCrossReferences(foundTextId);

            Assert.NotEmpty(refs);
            string expected = $"WorldMapPoint 0x{foundWmpId:X02}";
            Assert.Contains(refs, r => r.StartsWith(expected));
        }

        /// <summary>
        /// Picks the first epithet (ed_2) entry with a non-zero text ID and
        /// verifies the registry-driven FindCrossReferences returns an
        /// "ED_Epithet" entry.
        /// </summary>
        [Fact]
        public void RealRom_FindsEDEpithetReference()
        {
            if (!_fixture.IsAvailable)
            {
                _output.WriteLine("ROM not available; skipping integration test.");
                return;
            }

            ROM rom = CoreState.ROM!;
            var info = rom.RomInfo;
            if (info.ed_2_pointer == 0)
            {
                _output.WriteLine("ed_2_pointer is 0; skipping.");
                return;
            }
            uint edBase = NameResolver.DerefPointer(rom, info.ed_2_pointer);
            if (edBase == 0)
            {
                _output.WriteLine("ed_2_pointer doesn't dereference; skipping.");
                return;
            }

            uint foundTextId = 0;
            uint foundEdId = 0;
            for (uint i = 0; i < 0x200; i++)
            {
                uint entry = edBase + i * 8u;
                if (entry + 6 > (uint)rom.Data.Length) break;
                // Terminator: u32 at +0 == 0.
                if (rom.u32(entry) == 0) break;
                uint tid = rom.u16(entry + 4);
                if (tid != 0 && tid < 0x7FFF)
                {
                    foundTextId = tid;
                    foundEdId = i;
                    break;
                }
            }
            if (foundTextId == 0)
            {
                _output.WriteLine("No ED epithet with text ID found; skipping.");
                return;
            }
            _output.WriteLine($"Using ED epithet {foundEdId} with text ID 0x{foundTextId:X4}");

            var vm = new TextViewerViewModel();
            List<string> refs = vm.FindCrossReferences(foundTextId);

            Assert.NotEmpty(refs);
            string expected = $"ED_Epithet 0x{foundEdId:X02}";
            Assert.Contains(refs, r => r.StartsWith(expected));
        }
    }
}
