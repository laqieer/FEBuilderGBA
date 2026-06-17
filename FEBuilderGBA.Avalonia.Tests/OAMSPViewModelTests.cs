// SPDX-License-Identifier: GPL-3.0-or-later
// OAMSPViewModel tests (#1179) — the Avalonia Special OAM editor VM that wraps
// the cross-platform SpecialOamScanCore LDR-scan discovery + hex-dump detail.
//
// Proves: LoadList discovers the hand-laid OAMSP entry; LoadEntry populates the
// detail labels + non-empty hex dump; the per-ROM LDR/scan cache is reused on a
// second LoadList (no re-scan); a no-ROM call returns empty without throwing.
// Marked [Collection("SharedState")] because it mutates CoreState.ROM.
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class OAMSPViewModelTests
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

        // Build a synthetic FE8U ROM carrying one discoverable OAMSP entry (the
        // same hand-laid layout as the Core tests).
        static ROM MakeOamSpRom()
        {
            var data = new byte[ROM_SIZE];

            data[OAM12_A_OFF + 0] = 0x00; data[OAM12_A_OFF + 1] = 0x10; data[OAM12_A_OFF + 12] = 0x01;
            data[OAM12_B_OFF + 0] = 0x00; data[OAM12_B_OFF + 1] = 0x20; data[OAM12_B_OFF + 12] = 0x01;

            WritePtr(data, OAMSP_ARRAY_OFF + 0, 0x08000000u + OAM12_A_OFF);
            WritePtr(data, OAMSP_ARRAY_OFF + 4, 0x08000000u + OAM12_B_OFF);
            WritePtr(data, OAMSP_ARRAY_OFF + 8, 0x80000000u);

            WriteU16(data, LDR_FUNC_OFF + 0, 0xB500);
            WriteU16(data, LDR_FUNC_OFF + 2, 0x4801);
            WriteU16(data, LDR_FUNC_OFF + 4, 0x4770);
            WriteU16(data, LDR_FUNC_OFF + 6, 0x46C0);
            WritePtr(data, LDR_FUNC_OFF + 8, 0x08000000u + OAMSP_ARRAY_OFF);

            var rom = new ROM();
            Assert.True(rom.LoadLow("oamsp-vm-fe8u.gba", data, "BE8E01"));
            return rom;
        }

        [Fact]
        public void LoadList_DiscoversHandLaidEntry()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeOamSpRom();
                var vm = new OAMSPViewModel();

                var list = vm.LoadList();
                Assert.NotEmpty(list);
                Assert.Contains(list, r => r.addr == (uint)OAMSP_ARRAY_OFF);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void LoadEntry_PopulatesDetailAndLabels()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = MakeOamSpRom();
                var vm = new OAMSPViewModel();
                vm.LoadList();

                vm.LoadEntry((uint)OAMSP_ARRAY_OFF);

                Assert.True(vm.IsLoaded);
                Assert.Equal((uint)OAMSP_ARRAY_OFF, vm.CurrentAddr);
                Assert.StartsWith("OAMSP", vm.EntryName);
                Assert.Equal("0xC", vm.EntryLength);   // 12 bytes
                Assert.Equal("2", vm.Oam12Count);
                Assert.False(string.IsNullOrEmpty(vm.DetailText));
                Assert.Contains("80000000", vm.DetailText); // terminator word
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void LoadList_CachesPerRom_NoRescan()
        {
            var saved = CoreState.ROM;
            try
            {
                ROM rom = MakeOamSpRom();
                CoreState.ROM = rom;
                var vm = new OAMSPViewModel();

                Assert.False(vm.IsCachedFor(rom)); // nothing scanned yet
                vm.LoadList();
                Assert.True(vm.IsCachedFor(rom));  // cache populated for this ROM

                // A second LoadList must reuse the cache (still cached for the
                // SAME ROM instance) and return the same content.
                var first = vm.LoadList();
                Assert.True(vm.IsCachedFor(rom));
                var second = vm.LoadList();
                Assert.Equal(first.Count, second.Count);
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void LoadList_NoRom_ReturnsEmpty()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new OAMSPViewModel();
                Assert.Empty(vm.LoadList());
            }
            finally { CoreState.ROM = saved; }
        }

        [Fact]
        public void LoadEntry_NoRom_NoThrow_NotLoaded()
        {
            var saved = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new OAMSPViewModel();
                vm.LoadEntry(0xE0000); // must not throw
                Assert.False(vm.IsLoaded);
                Assert.Equal("", vm.DetailText);
            }
            finally { CoreState.ROM = saved; }
        }
    }
}
