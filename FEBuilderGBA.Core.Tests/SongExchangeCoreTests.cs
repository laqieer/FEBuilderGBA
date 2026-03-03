using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class SongExchangeCoreTests
    {
        [Fact]
        public void FindSongTablePointer_ReadsPointerAtAddress()
        {
            byte[] rom = new byte[256];
            // Write GBA pointer 0x08000080 at offset 0x10
            uint ptr = 0x08000080;
            rom[0x10] = (byte)(ptr & 0xFF);
            rom[0x11] = (byte)((ptr >> 8) & 0xFF);
            rom[0x12] = (byte)((ptr >> 16) & 0xFF);
            rom[0x13] = (byte)((ptr >> 24) & 0xFF);

            uint result = SongExchangeCore.FindSongTablePointer(rom, 0x10);
            Assert.Equal(0x80u, result);
        }

        [Fact]
        public void FindSongTablePointer_InvalidPointer_ReturnsZero()
        {
            byte[] rom = new byte[256];
            // Write non-GBA value
            rom[0x10] = 0x12; rom[0x11] = 0x34; rom[0x12] = 0x56; rom[0x13] = 0x00;

            uint result = SongExchangeCore.FindSongTablePointer(rom, 0x10);
            Assert.Equal(0u, result);
        }

        [Fact]
        public void FindSongTablePointer_NullRom_ReturnsZero()
        {
            uint result = SongExchangeCore.FindSongTablePointer(null, 0);
            Assert.Equal(0u, result);
        }

        [Fact]
        public void SongTableToSongList_ParsesValidEntries()
        {
            byte[] rom = new byte[512];

            // Create a song table entry at offset 0x80
            // Entry 0: pointer to header at 0x100
            uint headerGBA = 0x08000100;
            rom[0x80] = (byte)(headerGBA & 0xFF);
            rom[0x81] = (byte)((headerGBA >> 8) & 0xFF);
            rom[0x82] = (byte)((headerGBA >> 16) & 0xFF);
            rom[0x83] = (byte)((headerGBA >> 24) & 0xFF);

            // Song header at offset 0x100: trackCount=2, padding, priority, reverb
            rom[0x100] = 2; // track count
            rom[0x101] = 0; // padding
            rom[0x102] = 0; // priority
            rom[0x103] = 0; // reverb
            // Voice pointer at 0x104
            uint voiceGBA = 0x08000180;
            rom[0x104] = (byte)(voiceGBA & 0xFF);
            rom[0x105] = (byte)((voiceGBA >> 8) & 0xFF);
            rom[0x106] = (byte)((voiceGBA >> 16) & 0xFF);
            rom[0x107] = (byte)((voiceGBA >> 24) & 0xFF);

            // Track pointer at 0x108
            uint trackGBA = 0x08000190;
            rom[0x108] = (byte)(trackGBA & 0xFF);
            rom[0x109] = (byte)((trackGBA >> 8) & 0xFF);
            rom[0x10A] = (byte)((trackGBA >> 16) & 0xFF);
            rom[0x10B] = (byte)((trackGBA >> 24) & 0xFF);

            var list = SongExchangeCore.SongTableToSongList(rom, 0x80);
            Assert.Single(list);
            Assert.Equal(0u, list[0].Number);
            Assert.Equal(0x100u, list[0].Header);
            Assert.Equal(0x180u, list[0].Voices);
            Assert.Equal(0x190u, list[0].Tracks);
            Assert.Equal(2, list[0].TrackCount);
        }

        [Fact]
        public void SongTableToSongList_EmptyTable_ReturnsEmpty()
        {
            byte[] rom = new byte[256]; // All zeros = invalid pointers
            var list = SongExchangeCore.SongTableToSongList(rom, 0);
            Assert.Empty(list);
        }

        [Fact]
        public void SongTableToSongList_NullRom_ReturnsEmpty()
        {
            var list = SongExchangeCore.SongTableToSongList(null, 0);
            Assert.Empty(list);
        }

        [Fact]
        public void ConvertSong_NullArgs_ReturnsFalse()
        {
            Assert.False(SongExchangeCore.ConvertSong(null, null, null, null));
        }

        [Fact]
        public void ConvertSong_CopiesHeaderData()
        {
            byte[] src = new byte[1024];
            byte[] dest = new byte[1024];

            var srcSong = new SongExchangeCore.SongSt
            {
                Number = 0, Table = 0x80, Header = 0x100, TrackCount = 1
            };
            var destSong = new SongExchangeCore.SongSt
            {
                Number = 0, Table = 0x80, Header = 0x200, TrackCount = 1
            };

            // Write source header data
            src[0x100] = 1; // track count
            src[0x104] = 0xAA; // voice pointer byte

            bool result = SongExchangeCore.ConvertSong(src, srcSong, dest, destSong);
            Assert.True(result);
            Assert.Equal(1, dest[0x200]); // track count copied
            Assert.Equal(0xAA, dest[0x204]); // voice pointer copied
        }
    }
}
