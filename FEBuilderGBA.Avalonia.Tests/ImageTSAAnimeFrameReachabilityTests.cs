// SPDX-License-Identifier: GPL-3.0-or-later
// Reachability tests for the Avalonia TSA Animation editor (v1) — every
// FRAMECOUNT frame of each category must be listed AND individually loadable,
// not just frame 0 (#1457).
//
// Strategy: load a synthetic FE8U ROM with CoreState.BaseDirectory pointed at the
// test output dir so ImageTSAAnimeViewModel.LoadList reads the real shipped
// config/data/tsaanime_FE8.txt. We seed the first US category's pointer slot to
// a frame base we control, then assert LoadList enumerates all FRAMECOUNT frames
// of that category and that LoadEntry resolves each frame's CurrentAddr to
// base + i*12.
using System;
using System.Collections.Generic;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class ImageTSAAnimeFrameReachabilityTests
    {
        // First US category in config/data/tsaanime_FE8.txt:
        //   0807FA88  014  ... {U}   (FRAMECOUNT = 0x14 = 20 frames)
        const uint US_CATEGORY_POINTER = 0x0807FA88u;
        const uint FRAME_BASE = 0x00400000u; // we point the category here
        const uint SIZE = 12;

        sealed class Env : IDisposable
        {
            readonly ROM _prevRom;
            readonly string _prevBaseDir;
            public readonly ROM Rom;

            public Env()
            {
                _prevRom = CoreState.ROM;
                _prevBaseDir = CoreState.BaseDirectory;

                string asmDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                CoreState.BaseDirectory = asmDir;

                Rom = new ROM();
                Rom.LoadLow("synth.gba", new byte[0x1000000], "BE8E01"); // FE8U
                CoreState.ROM = Rom;

                // Point the first US category at our controlled frame base.
                Rom.write_p32(U.toOffset(US_CATEGORY_POINTER), FRAME_BASE);
            }

            public void Dispose()
            {
                CoreState.ROM = _prevRom;
                CoreState.BaseDirectory = _prevBaseDir;
            }
        }

        static bool ConfigPresent()
        {
            string asmDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            return File.Exists(Path.Combine(asmDir, "config", "data", "tsaanime_FE8.txt"));
        }

        [Fact]
        public void LoadList_EnumeratesAllFramesOfCategory_NotJustFrameZero()
        {
            if (!ConfigPresent()) return; // config not staged in this env — skip
            using var env = new Env();

            var vm = new ImageTSAAnimeViewModel();
            var rows = vm.LoadList();

            // Collect the rows belonging to our seeded US category (base + i*12).
            var mine = new List<AddrResult>();
            for (uint i = 0; i < 20; i++)
            {
                uint want = FRAME_BASE + i * SIZE;
                AddrResult found = rows.Find(r => r.addr == want);
                Assert.NotNull(found); // every frame 0..19 must be present
                mine.Add(found);
            }

            // The OLD bug surfaced only frame 0 of each category — prove >1 frame
            // of THIS category is reachable.
            Assert.True(mine.Count == 20);
            Assert.NotEqual(mine[0].addr, mine[1].addr);
            Assert.Equal(FRAME_BASE + 19u * SIZE, mine[19].addr);
        }

        [Fact]
        public void LoadEntry_ResolvesEachFrameToBasePlusIndexTimesTwelve()
        {
            if (!ConfigPresent()) return;
            using var env = new Env();

            var vm = new ImageTSAAnimeViewModel();
            vm.LoadList(); // populate config-backed categories

            // Frames 1..N-1 (the previously-unreachable ones) are individually loadable.
            for (uint i = 0; i < 20; i++)
            {
                uint frameAddr = FRAME_BASE + i * SIZE;
                vm.LoadEntry(frameAddr);
                Assert.Equal(frameAddr, vm.CurrentAddr);
                Assert.True(vm.IsLoaded);
                // P0/P4/P8 read from the per-frame slot, so each frame is independent.
                Assert.Equal(env.Rom.u32(frameAddr + 0), vm.P0);
                Assert.Equal(env.Rom.u32(frameAddr + 4), vm.P4);
                Assert.Equal(env.Rom.u32(frameAddr + 8), vm.P8);
            }
        }

        [Fact]
        public void NoTwentyEntryCap_AcrossAllCategories()
        {
            if (!ConfigPresent()) return;
            using var env = new Env();

            // Seed every US category pointer so all of them resolve, then prove the
            // overall list exceeds the old 20-row cap (the first US category alone
            // has 20 frames; more categories follow).
            var dict = U.LoadTSVResource(U.ConfigDataFilename("tsaanime_"), false);
            uint b = 0x00500000u;
            foreach (var key in new List<uint>(dict.Keys))
            {
                env.Rom.write_p32(U.toOffset(key), b);
                b += 0x10000u;
            }

            var vm = new ImageTSAAnimeViewModel();
            var rows = vm.LoadList();
            Assert.True(rows.Count > 20,
                $"expected the un-capped frame list to exceed 20 rows, got {rows.Count}");
        }
    }
}
