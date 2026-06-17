// SPDX-License-Identifier: GPL-3.0-or-later
// Core tests for SpecialOamScanCore (#1179) — the cross-platform port of WF
// OAMSPForm's LDR-scan discovery + hex-dump inspection of special-OAM
// sprite-assembly entries.
//
// The synthetic ROM hand-lays one OAMSP entry that the scan must discover:
//   - a Thumb LDR function whose literal-pool word points at an OAMSP pointer
//     array (so MakeLDRMap surfaces the array address);
//   - the OAMSP pointer array (>= 0xDB000 borderline) holding two distinct
//     pointers to OAM12 blocks plus an 0x80000000 terminator (== 4*3 bytes);
//   - two OAM12 12-byte-record blocks, each terminated by 0x01 in byte 0.
// Tests also assert the never-throw / guard contract (null ROM, null map,
// truncated buffer) and the read-only BuildDetailDump output.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FEBuilderGBA;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SpecialOamScanCoreTests
    {
        const int ROM_SIZE = 0x1000000;    // 16 MiB — LoadLow requires this for FE8U
        const int OAMSP_ARRAY_OFF = 0xE0000; // >= compress_image_borderline_address (0xDB000)
        const int OAM12_A_OFF = 0xE1000;
        const int OAM12_B_OFF = 0xE2000;
        const int LDR_FUNC_OFF = 0x200;

        static void WriteU16(byte[] buf, int off, ushort v)
        {
            buf[off + 0] = (byte)(v & 0xFF);
            buf[off + 1] = (byte)((v >> 8) & 0xFF);
        }

        static void WritePtr(byte[] buf, int off, uint pointer)
        {
            buf[off + 0] = (byte)(pointer & 0xFF);
            buf[off + 1] = (byte)((pointer >> 8) & 0xFF);
            buf[off + 2] = (byte)((pointer >> 16) & 0xFF);
            buf[off + 3] = (byte)((pointer >> 24) & 0xFF);
        }

        // Build a synthetic FE8U ROM carrying one discoverable OAMSP entry.
        static ROM MakeOamSpRom()
        {
            var data = new byte[ROM_SIZE];

            // --- OAM12 block A: one 12-byte data record + a 0x01 terminator record.
            // record 0: byte0 == 0 => "data" (continue)
            data[OAM12_A_OFF + 0] = 0x00;
            data[OAM12_A_OFF + 1] = 0x10; // arbitrary non-zero payload
            // record 1 (at +12): byte0 == 1 => terminator
            data[OAM12_A_OFF + 12] = 0x01;

            // --- OAM12 block B: same shape.
            data[OAM12_B_OFF + 0] = 0x00;
            data[OAM12_B_OFF + 1] = 0x20;
            data[OAM12_B_OFF + 12] = 0x01;

            // --- OAMSP pointer array: 2 distinct OAM12 pointers + a terminator word.
            // Length = 3 words = 12 bytes == the 4*3 minimum the LDR pass requires.
            WritePtr(data, OAMSP_ARRAY_OFF + 0, 0x08000000u + OAM12_A_OFF);
            WritePtr(data, OAMSP_ARRAY_OFF + 4, 0x08000000u + OAM12_B_OFF);
            WritePtr(data, OAMSP_ARRAY_OFF + 8, 0x80000000u); // OAM term 0x8X0000XX

            // --- Thumb LDR function loading the OAMSP array pointer.
            // PUSH {lr}; LDR r0,[pc,#4]; BX lr; NOP; <literal = array pointer>.
            WriteU16(data, LDR_FUNC_OFF + 0, 0xB500);
            WriteU16(data, LDR_FUNC_OFF + 2, 0x4801);
            WriteU16(data, LDR_FUNC_OFF + 4, 0x4770);
            WriteU16(data, LDR_FUNC_OFF + 6, 0x46C0);
            WritePtr(data, LDR_FUNC_OFF + 8, 0x08000000u + OAMSP_ARRAY_OFF);

            var rom = new ROM();
            Assert.True(rom.LoadLow("oamsp-fe8u.gba", data, "BE8E01"));
            return rom;
        }

        [Fact]
        public void ScanSpecialOam_FindsHandLaidEntry()
        {
            var saved = CoreState.ROM;
            try
            {
                var rom = MakeOamSpRom();
                CoreState.ROM = rom; // U.isSafetyPointer (no-rom overload) reads CoreState.ROM

                var ldrMap = PointerToolAutoSearchCore.BuildLdrMap(rom.Data);
                var entries = SpecialOamScanCore.ScanSpecialOam(rom, ldrMap);

                Assert.NotEmpty(entries);
                var hit = entries.Find(e => e.Addr == (uint)OAMSP_ARRAY_OFF);
                Assert.NotNull(hit);
                Assert.StartsWith("OAMSP ", hit.Name);          // label prefix from WF
                Assert.Equal(12u, hit.Length);                  // 3 words
                Assert.Equal(2, hit.Oam12.Count);               // two OAM12 sub-blocks
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void BuildDetailDump_NonEmptyForFoundEntry()
        {
            var saved = CoreState.ROM;
            try
            {
                var rom = MakeOamSpRom();
                CoreState.ROM = rom;

                var ldrMap = PointerToolAutoSearchCore.BuildLdrMap(rom.Data);
                var entries = SpecialOamScanCore.ScanSpecialOam(rom, ldrMap);
                var hit = entries.Find(e => e.Addr == (uint)OAMSP_ARRAY_OFF);
                Assert.NotNull(hit);

                string dump = SpecialOamScanCore.BuildDetailDump(rom, hit);
                Assert.False(string.IsNullOrEmpty(dump));
                // Pointer-array word + both OAM12 block headers appear in the dump.
                Assert.Contains("80000000", dump);                 // terminator word
                Assert.Contains(U.ToHexString8((uint)OAM12_A_OFF), dump);
                Assert.Contains(U.ToHexString8((uint)OAM12_B_OFF), dump);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void ScanSpecialOam_NullRom_ReturnsEmpty()
        {
            var ldrMap = new List<DisassemblerTrumb.LDRPointer>();
            var entries = SpecialOamScanCore.ScanSpecialOam(null, ldrMap);
            Assert.Empty(entries);
        }

        [Fact]
        public void ScanSpecialOam_NullMap_ReturnsEmpty()
        {
            var saved = CoreState.ROM;
            try
            {
                var rom = MakeOamSpRom();
                CoreState.ROM = rom;
                var entries = SpecialOamScanCore.ScanSpecialOam(rom, null);
                Assert.Empty(entries);
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void BuildDetailDump_NullOrInvalidEntry_ReturnsEmptyNoThrow()
        {
            var saved = CoreState.ROM;
            try
            {
                var rom = MakeOamSpRom();
                CoreState.ROM = rom;

                Assert.Equal("", SpecialOamScanCore.BuildDetailDump(rom, null));
                Assert.Equal("", SpecialOamScanCore.BuildDetailDump(null, new SpecialOamScanCore.OamSpEntry()));

                // An entry pointing past the end of ROM must not throw — the
                // bounds guards stop the dump and return whatever was built.
                var oob = new SpecialOamScanCore.OamSpEntry
                {
                    Addr = (uint)(rom.Data.Length + 0x100),
                    Length = 64,
                    Name = "OAMSP oob",
                };
                string dump = SpecialOamScanCore.BuildDetailDump(rom, oob);
                Assert.NotNull(dump); // no throw
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }

        [Fact]
        public void RealRom_FE8U_ScanFindsLabeledEntries()
        {
            // GUARDED: requires roms/FE8U.gba next to the .sln. Skips cleanly when
            // absent (worktree / CI without ROM files). Proves the LDR scan + the
            // oam_name_ resource discover real OAMSP entries with real labels.
            string romPath = FindRom("FE8U.gba");
            if (romPath == null) return; // skip

            var savedRom = CoreState.ROM;
            var savedBaseDir = CoreState.BaseDirectory;
            var savedLang = CoreState.Language;
            try
            {
                string romsDir = Path.GetDirectoryName(romPath);
                string repoRoot = Path.GetDirectoryName(romsDir);
                CoreState.BaseDirectory = repoRoot;
                CoreState.Language = "en";

                var rom = new ROM();
                if (!rom.Load(romPath, out string _)) return; // skip on load failure
                CoreState.ROM = rom;

                var ldrMap = PointerToolAutoSearchCore.BuildLdrMap(rom.Data);
                Assert.NotEmpty(ldrMap); // the full-ROM LDR scan must find SOMETHING

                var entries = SpecialOamScanCore.ScanSpecialOam(rom, ldrMap);
                Assert.NotEmpty(entries); // the scan must discover OAMSP entries

                foreach (var e in entries)
                {
                    Assert.StartsWith("OAMSP", e.Name);  // "OAMSP " or "OAMSP_ "
                    Assert.True(e.Length > 0);
                }

                // The detail dump for the first entry must be non-empty.
                string dump = SpecialOamScanCore.BuildDetailDump(rom, entries[0]);
                Assert.False(string.IsNullOrEmpty(dump));
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.BaseDirectory = savedBaseDir;
                CoreState.Language = savedLang;
            }
        }

        static string FindRom(string romName)
        {
            string thisAssembly = Assembly.GetExecutingAssembly().Location;
            string dir = Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 12 && dir != null; i++)
            {
                if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string path = Path.Combine(dir, "roms", romName);
                    if (File.Exists(path)) return path;
                    // Keep walking up: a worktree has its own .sln but no roms/.
                }
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        [Fact]
        public void ScanSpecialOam_EmptyRom_ReturnsEmptyNoThrow()
        {
            var saved = CoreState.ROM;
            try
            {
                // A valid (zero-filled) FE8U ROM with no OAMSP data — the scan must
                // guard every read and simply find nothing (never throw).
                var data = new byte[ROM_SIZE];
                var rom = new ROM();
                Assert.True(rom.LoadLow("oamsp-empty.gba", data, "BE8E01"));
                CoreState.ROM = rom;

                var ldrMap = new List<DisassemblerTrumb.LDRPointer>();
                // Synthesize a malicious LDR entry pointing past EOF to exercise the
                // bounds guards in CalcLengthAndCheck / CalcLengthAndCheckOAM12.
                ldrMap.Add(new DisassemblerTrumb.LDRPointer
                {
                    ldr_address = 0x200,
                    ldr_data_address = 0x208,
                    ldr_data = 0x08000000u + (uint)(ROM_SIZE - 8),
                });
                var entries = SpecialOamScanCore.ScanSpecialOam(rom, ldrMap);
                Assert.Empty(entries); // nothing valid; no throw
            }
            finally
            {
                CoreState.ROM = saved;
            }
        }
    }
}
