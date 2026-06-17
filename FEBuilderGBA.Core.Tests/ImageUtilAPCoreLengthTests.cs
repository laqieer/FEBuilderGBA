// SPDX-License-Identifier: GPL-3.0-or-later
// #1177 Core tests for ImageUtilAPCore.GetLength / CalcAPLength — the AP-region
// padded-length computation that feeds the Unit Move Icon editor's AP export
// (and any AP MD5 matching). WF parity: GetLength() = U.Padding4(Length).
//
// Real-ROM tests skip when the ROM is unavailable (CI runners without a ROM).
using System;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageUtilAPCoreLengthTests
    {
        readonly ITestOutputHelper _output;
        public ImageUtilAPCoreLengthTests(ITestOutputHelper output) => _output = output;

        static string? FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string? dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = Path.Combine(dir, "roms", romName);
                    if (File.Exists(path)) return path;
                    break;
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        ROM? LoadRom(string romName)
        {
            string? path = FindRom(romName);
            if (path == null) { _output.WriteLine($"SKIP: {romName} not found"); return null; }
            var rom = new ROM();
            rom.Load(path, out _);
            return rom;
        }

        [Fact]
        public void CalcAPLength_RealMoveIconAP_IsPositiveAnd4Aligned()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;

                uint ptr = rom.RomInfo.unit_move_icon_pointer;
                Assert.NotEqual(0u, ptr);
                uint baseAddr = rom.p32(ptr);
                Assert.True(U.isSafetyOffset(baseAddr, rom));

                // Scan the move-icon table for the first entry whose +4 AP
                // pointer resolves to a parseable AP region.
                bool found = false;
                for (uint i = 0; i < 0x100; i++)
                {
                    uint entry = baseAddr + i * 8;
                    if (entry + 8 > (uint)rom.Data.Length) break;
                    uint p0 = rom.u32(entry + 0);
                    if (!U.isPointer(p0)) break;

                    uint apGba = rom.u32(entry + 4);
                    if (!U.isPointer(apGba)) continue;
                    uint apOff = U.toOffset(apGba);
                    if (!U.isSafetyOffset(apOff, rom)) continue;

                    uint len = ImageUtilAPCore.CalcAPLength(rom.Data, apOff);
                    if (len == 0) continue;

                    found = true;
                    // WF parity: GetLength() = U.Padding4(Length), so the result
                    // is always a multiple of 4.
                    Assert.Equal(0u, len % 4u);
                    Assert.True(len > 0);
                    // The region must fit inside the ROM.
                    Assert.True((ulong)apOff + len <= (ulong)rom.Data.Length);
                    break;
                }

                if (!found) _output.WriteLine("SKIP: no parseable AP region in the move-icon table");
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void CalcAPLength_GarbageAddress_ReturnsZero()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM? rom = LoadRom("FE8U.gba");
                if (rom == null) return;
                CoreState.ROM = rom;

                // An address in the GBA header danger zone is rejected by Parse's
                // IsSafetyOffset guard -> 0.
                Assert.Equal(0u, ImageUtilAPCore.CalcAPLength(rom.Data, 0x10));
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void CalcAPLength_NullData_ReturnsZero_NoThrow()
        {
            Assert.Equal(0u, ImageUtilAPCore.CalcAPLength(null, 0x1000));
        }
    }
}
