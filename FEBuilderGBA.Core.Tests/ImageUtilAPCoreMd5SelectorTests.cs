// SPDX-License-Identifier: GPL-3.0-or-later
// #1226 Core tests for the AP MD5-dictionary selector helpers added to
// ImageUtilAPCore: CalcAPMD5 / MakeAPAddressDic / ResolveApOffsetByMd5.
//
// These port the WinForms ImageUnitMoveIconFrom MD5-matching subsystem
// (CalcAPMD5 / MakeAPAddressDic / SelectAPAddresssFromAPComboLow /
// SearchAPHashInVanilla). They are pure / READ-ONLY: they identify an AP region
// by content hash and resolve a chosen catalog AP to an EXISTING ROM address —
// they NEVER mutate the ROM (the View does the single P4 re-point write).
//
// The synthetic-blob tests run deterministically on CI (no ROM). One real-ROM
// test exercises the full move-icon-table round trip and SKIPS when no ROM is
// present.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class ImageUtilAPCoreMd5SelectorTests
    {
        readonly ITestOutputHelper _output;
        public ImageUtilAPCoreMd5SelectorTests(ITestOutputHelper output) => _output = output;

        // MD5 of an empty byte array — the documented fallback for a zero-length /
        // unparseable region (WF getBinaryData(addr, 0) → empty array).
        const string EmptyMd5 = "d41d8cd98f00b204e9800998ecf8427e";

        // ----------------------------------------------------------------
        // Synthetic minimal-but-valid AP blob (16 bytes), so CalcAPLength
        // returns a non-zero length and the bytes hash deterministically.
        //
        // Layout at base (matches ImageUtilAPCore.Parse):
        //   +0  u16 frameDataOffset = 4   (also the 0x0004 vanilla header marker)
        //   +2  u16 animeTableOffset = 6
        //   +4  u16 framePtr f0     = 4   (minData=4; frame data @ base+4+4=base+8)
        //   +6  u16 animePtr a0     = 6   (anime data @ base+6+6=base+12)
        //   +8  u16 frame OAM count = 0   (empty frame, ends @ base+10)
        //   +10 u16 (padding)       = 0
        //   +12 u16 anime Wait      = 0   (terminator; FrameIndex @ +14)
        //   +14 u16 anime FrameIdx  = 0
        // GetLength = Padding4(16) = 16.
        // ----------------------------------------------------------------
        static void WriteApBlob(byte[] rom, uint baseOff)
        {
            void W16(uint a, ushort v) { rom[a] = (byte)(v & 0xFF); rom[a + 1] = (byte)(v >> 8); }
            W16(baseOff + 0, 4);   // frameDataOffset (and 0x0004 header)
            W16(baseOff + 2, 6);   // animeTableOffset
            W16(baseOff + 4, 4);   // framePtr f0
            W16(baseOff + 6, 6);   // animePtr a0
            W16(baseOff + 8, 0);   // frame OAM count = 0
            W16(baseOff + 10, 0);  // padding
            W16(baseOff + 12, 0);  // anime Wait = 0 (terminator)
            W16(baseOff + 14, 0);  // anime FrameIndex
        }

        static byte[] NewRom(int size = 0x4000)
        {
            return new byte[size];
        }

        // ----------------------------------------------------------------
        // CalcAPMD5
        // ----------------------------------------------------------------

        [Fact]
        public void CalcAPMD5_NullData_ReturnsEmptyArrayMd5_NoThrow()
        {
            Assert.Equal(EmptyMd5, ImageUtilAPCore.CalcAPMD5(null, 0x1000));
        }

        [Fact]
        public void CalcAPMD5_UnparseableRegion_ReturnsEmptyArrayMd5()
        {
            // A region in the GBA header danger zone is unparseable → length 0 →
            // MD5 of the empty byte array.
            byte[] rom = NewRom();
            Assert.Equal(EmptyMd5, ImageUtilAPCore.CalcAPMD5(rom, 0x10));
        }

        [Fact]
        public void CalcAPMD5_ValidBlob_HashesExactlyTheParsedBytes()
        {
            byte[] rom = NewRom();
            uint baseOff = 0x1000;
            WriteApBlob(rom, baseOff);

            uint len = ImageUtilAPCore.CalcAPLength(rom, baseOff);
            Assert.Equal(16u, len);

            // The MD5 must equal U.md5 over exactly those `len` bytes (parity with
            // WF CalcAPMD5 = md5(getBinaryData(ap_addr, length))).
            byte[] expectedBytes = U.getBinaryData(rom, baseOff, len);
            string expected = U.md5(expectedBytes);
            Assert.Equal(expected, ImageUtilAPCore.CalcAPMD5(rom, baseOff));
        }

        [Fact]
        public void CalcAPMD5_SameBytesAtTwoAddresses_ProduceSameHash()
        {
            byte[] rom = NewRom();
            WriteApBlob(rom, 0x1000);
            WriteApBlob(rom, 0x2000);
            Assert.Equal(
                ImageUtilAPCore.CalcAPMD5(rom, 0x1000),
                ImageUtilAPCore.CalcAPMD5(rom, 0x2000));
        }

        // ----------------------------------------------------------------
        // MakeAPAddressDic
        // ----------------------------------------------------------------

        [Fact]
        public void MakeAPAddressDic_NullData_ReturnsEmpty_NoThrow()
        {
            var dic = ImageUtilAPCore.MakeAPAddressDic(null, 0x100, 4);
            Assert.Empty(dic);
        }

        [Fact]
        public void MakeAPAddressDic_MapsEachEntryApOffsetToItsMd5()
        {
            byte[] rom = NewRom();
            uint apOff = 0x1000;
            WriteApBlob(rom, apOff);

            // Build a 2-entry move-icon table at 0x800. Each entry is 8 bytes:
            // +0 image pointer (unused here), +4 AP pointer (GBA pointer to apOff).
            uint tableBase = 0x800;
            uint apGba = U.toPointer(apOff);
            void W32(uint a, uint v)
            {
                rom[a] = (byte)v; rom[a + 1] = (byte)(v >> 8);
                rom[a + 2] = (byte)(v >> 16); rom[a + 3] = (byte)(v >> 24);
            }
            W32(tableBase + 0, U.toPointer(0x3000)); // entry0 image ptr (dummy)
            W32(tableBase + 4, apGba);               // entry0 AP ptr → apOff
            W32(tableBase + 8, U.toPointer(0x3000)); // entry1 image ptr (dummy)
            W32(tableBase + 12, apGba);              // entry1 AP ptr → apOff (shared)

            var dic = ImageUtilAPCore.MakeAPAddressDic(rom, tableBase, 2);

            // Both entries share the same AP offset → one map entry.
            Assert.True(dic.ContainsKey(apOff));
            Assert.Equal(ImageUtilAPCore.CalcAPMD5(rom, apOff), dic[apOff]);
        }

        // ----------------------------------------------------------------
        // ResolveApOffsetByMd5
        // ----------------------------------------------------------------

        [Fact]
        public void ResolveApOffsetByMd5_NullOrEmptyTarget_ReturnsNotFound()
        {
            byte[] rom = NewRom();
            Assert.Equal(U.NOT_FOUND, ImageUtilAPCore.ResolveApOffsetByMd5(rom, null, null, null));
            Assert.Equal(U.NOT_FOUND, ImageUtilAPCore.ResolveApOffsetByMd5(rom, "", null, null));
        }

        [Fact]
        public void ResolveApOffsetByMd5_MatchesAnExistingEntryAp_First()
        {
            byte[] rom = NewRom();
            uint apOff = 0x1000;
            WriteApBlob(rom, apOff);
            string md5 = ImageUtilAPCore.CalcAPMD5(rom, apOff);

            var entryDic = new Dictionary<uint, string> { [apOff] = md5 };

            // Entry match takes priority — returns the entry offset, no vanilla
            // search needed.
            Assert.Equal(apOff,
                ImageUtilAPCore.ResolveApOffsetByMd5(rom, md5, entryDic, null));
        }

        [Fact]
        public void ResolveApOffsetByMd5_FallsBackToVanilla_WhenHeaderAndHashMatch()
        {
            byte[] rom = NewRom();
            uint vanillaOff = 0x1000;
            WriteApBlob(rom, vanillaOff);
            string md5 = ImageUtilAPCore.CalcAPMD5(rom, vanillaOff);

            // No entry match; the vanilla list catalogues this offset with the
            // matching MD5, the header is 0x0004, and the recomputed hash matches
            // → fall back to the vanilla offset.
            var vanillaDic = new Dictionary<uint, string> { [vanillaOff] = md5 };
            Assert.Equal(vanillaOff,
                ImageUtilAPCore.ResolveApOffsetByMd5(rom, md5,
                    new Dictionary<uint, string>(), vanillaDic));
        }

        [Fact]
        public void ResolveApOffsetByMd5_RejectsVanilla_WhenHeaderOverwritten()
        {
            byte[] rom = NewRom();
            uint vanillaOff = 0x1000;
            WriteApBlob(rom, vanillaOff);
            string md5 = ImageUtilAPCore.CalcAPMD5(rom, vanillaOff);

            // Overwrite the header u16 so it is no longer 0x0004 — the vanilla slot
            // has been replaced; the resolver must reject it.
            rom[vanillaOff] = 0xFF; rom[vanillaOff + 1] = 0xFF;

            var vanillaDic = new Dictionary<uint, string> { [vanillaOff] = md5 };
            Assert.Equal(U.NOT_FOUND,
                ImageUtilAPCore.ResolveApOffsetByMd5(rom, md5,
                    new Dictionary<uint, string>(), vanillaDic));
        }

        [Fact]
        public void ResolveApOffsetByMd5_RejectsVanilla_WhenBytesChangedButHeaderIntact()
        {
            byte[] rom = NewRom();
            uint vanillaOff = 0x1000;
            WriteApBlob(rom, vanillaOff);
            string md5 = ImageUtilAPCore.CalcAPMD5(rom, vanillaOff);

            // Keep the 0x0004 header but corrupt a later byte so the recomputed MD5
            // no longer matches the catalogued one → reject (the re-verify guard).
            rom[vanillaOff + 6] = (byte)(rom[vanillaOff + 6] ^ 0x01);

            var vanillaDic = new Dictionary<uint, string> { [vanillaOff] = md5 };
            Assert.Equal(U.NOT_FOUND,
                ImageUtilAPCore.ResolveApOffsetByMd5(rom, md5,
                    new Dictionary<uint, string>(), vanillaDic));
        }

        [Fact]
        public void ResolveApOffsetByMd5_NotPresentAnywhere_ReturnsNotFound()
        {
            byte[] rom = NewRom();
            Assert.Equal(U.NOT_FOUND,
                ImageUtilAPCore.ResolveApOffsetByMd5(rom, "deadbeefdeadbeefdeadbeefdeadbeef",
                    new Dictionary<uint, string>(), new Dictionary<uint, string>()));
        }

        // ----------------------------------------------------------------
        // Real-ROM round trip (skips on CI without a ROM)
        // ----------------------------------------------------------------

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

        [Fact]
        public void MakeAPAddressDic_RealRom_ResolvesEachEntryApBackToItself()
        {
            var savedRom = CoreState.ROM;
            try
            {
                string? path = FindRom("FE8U.gba");
                if (path == null) { _output.WriteLine("SKIP: FE8U.gba not found"); return; }
                var rom = new ROM();
                rom.Load(path, out _);
                CoreState.ROM = rom;

                uint ptr = rom.RomInfo.unit_move_icon_pointer;
                Assert.NotEqual(0u, ptr);
                uint baseAddr = rom.p32(ptr);
                Assert.True(U.isSafetyOffset(baseAddr, rom));

                // Count rows the same way the VM does.
                int count = 0;
                for (uint i = 0; i < 0x100; i++)
                {
                    uint entry = baseAddr + i * 8;
                    if (entry + 8 > (uint)rom.Data.Length) break;
                    if (!U.isPointer(rom.u32(entry + 0))) break;
                    count++;
                }
                Assert.True(count > 0);

                var dic = ImageUtilAPCore.MakeAPAddressDic(rom.Data, baseAddr, count);
                Assert.NotEmpty(dic);

                // For each catalogued AP offset, resolving its OWN MD5 against the
                // entry dictionary must return that same offset (round trip).
                foreach (var pair in dic)
                {
                    uint resolved = ImageUtilAPCore.ResolveApOffsetByMd5(
                        rom.Data, pair.Value, dic, new Dictionary<uint, string>());
                    Assert.Equal(pair.Key, resolved);
                }
            }
            finally { CoreState.ROM = savedRom; }
        }
    }
}
