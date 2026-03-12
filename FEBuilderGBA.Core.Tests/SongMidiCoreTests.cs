using System.Collections.Generic;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public class SongMidiCoreTests
    {
        [Fact]
        public void ExportMidi_EmptyTracks_ProducesValidMidiHeader()
        {
            var tracks = new List<SongMidiCore.Track>();
            byte[] midi = SongMidiCore.ExportMidi(tracks, 0, 0, 0, 0);

            // Verify MIDI header "MThd"
            Assert.True(midi.Length >= 14, "MIDI data too short");
            Assert.Equal((byte)'M', midi[0]);
            Assert.Equal((byte)'T', midi[1]);
            Assert.Equal((byte)'h', midi[2]);
            Assert.Equal((byte)'d', midi[3]);

            // Header size = 6 (big-endian)
            Assert.Equal(0, midi[4]);
            Assert.Equal(0, midi[5]);
            Assert.Equal(0, midi[6]);
            Assert.Equal(6, midi[7]);

            // Format = 1
            Assert.Equal(0, midi[8]);
            Assert.Equal(1, midi[9]);

            // Track count = 1 (conductor only, 0 music tracks)
            Assert.Equal(0, midi[10]);
            Assert.Equal(1, midi[11]);

            // Ticks per quarter note = 24
            Assert.Equal(0, midi[12]);
            Assert.Equal(24, midi[13]);
        }

        [Fact]
        public void ExportMidi_SingleTrack_ProducesTwoMidiTracks()
        {
            // A track with just a FINE command (0xB1)
            var track = new SongMidiCore.Track();
            track.codes.Add(new SongMidiCore.Code(0, 0, 0xB1));

            var tracks = new List<SongMidiCore.Track> { track };
            byte[] midi = SongMidiCore.ExportMidi(tracks, 1, 0, 0, 0);

            // Track count in header should be 2 (1 conductor + 1 music)
            Assert.Equal(0, midi[10]);
            Assert.Equal(2, midi[11]);

            // Find second MTrk header after the first one
            int mtrk1 = FindMTrk(midi, 14);
            Assert.True(mtrk1 >= 14, "First MTrk not found");

            int mtrk2 = FindMTrk(midi, mtrk1 + 8);
            Assert.True(mtrk2 > mtrk1, "Second MTrk not found");
        }

        [Fact]
        public void ExportMidiFile_WritesValidFile()
        {
            var track = new SongMidiCore.Track();
            track.codes.Add(new SongMidiCore.Code(0, 0, 0xB1));

            var tracks = new List<SongMidiCore.Track> { track };
            string tempFile = System.IO.Path.GetTempFileName() + ".mid";

            try
            {
                SongMidiCore.ExportMidiFile(tempFile, tracks, 1, 0, 0, 0);
                Assert.True(System.IO.File.Exists(tempFile));

                byte[] data = System.IO.File.ReadAllBytes(tempFile);
                Assert.True(data.Length > 14);
                Assert.Equal((byte)'M', data[0]);
                Assert.Equal((byte)'T', data[1]);
            }
            finally
            {
                if (System.IO.File.Exists(tempFile))
                    System.IO.File.Delete(tempFile);
            }
        }

        [Fact]
        public void ParseTracks_NullRom_ReturnsEmptyList()
        {
            var result = SongMidiCore.ParseTracks(null, 0, 1);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseTracks_ValidRom_ParsesTrackData()
        {
            // Create a minimal ROM with a song header
            // isSafetyOffset requires addr >= 0x200, so place header at 0x300
            byte[] romData = new byte[0x1000];

            uint songAddr = 0x300;

            // Song header: trackCount=1, numBlks=0, priority=0, reverb=0
            romData[songAddr + 0] = 1;
            romData[songAddr + 1] = 0;
            romData[songAddr + 2] = 0;
            romData[songAddr + 3] = 0;

            // Instrument pointer at songAddr+4 (GBA pointer to 0x800)
            uint instPtr = 0x08000800;
            romData[songAddr + 4] = (byte)(instPtr & 0xFF);
            romData[songAddr + 5] = (byte)((instPtr >> 8) & 0xFF);
            romData[songAddr + 6] = (byte)((instPtr >> 16) & 0xFF);
            romData[songAddr + 7] = (byte)((instPtr >> 24) & 0xFF);

            // Track pointer at songAddr+8 -> points to track data at 0x400
            uint trackDataAddr = 0x400;
            uint trackPtr = 0x08000000 + trackDataAddr;
            romData[songAddr + 8] = (byte)(trackPtr & 0xFF);
            romData[songAddr + 9] = (byte)((trackPtr >> 8) & 0xFF);
            romData[songAddr + 10] = (byte)((trackPtr >> 16) & 0xFF);
            romData[songAddr + 11] = (byte)((trackPtr >> 24) & 0xFF);

            // Track data at 0x400: just FINE (0xB1)
            romData[trackDataAddr] = 0xB1;

            var rom = new ROM();
            rom.SwapNewROMDataDirect(romData);

            var tracks = SongMidiCore.ParseTracks(rom, songAddr, 1);
            Assert.Single(tracks);
            Assert.True(tracks[0].codes.Count >= 1);
            Assert.Equal(0xB1u, tracks[0].codes[0].type);
        }

        [Fact]
        public void ImportMidiFile_ReturnsNotImplementedMessage()
        {
            string result = SongMidiCore.ImportMidiFile("dummy.mid", 0, 0);
            Assert.Contains("not yet available", result);
        }

        static int FindMTrk(byte[] data, int start)
        {
            for (int i = start; i <= data.Length - 4; i++)
            {
                if (data[i] == 'M' && data[i + 1] == 'T'
                    && data[i + 2] == 'r' && data[i + 3] == 'k')
                    return i;
            }
            return -1;
        }
    }
}
