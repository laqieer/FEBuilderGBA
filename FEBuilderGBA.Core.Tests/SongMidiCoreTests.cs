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

        #region ParseMidiBytes Tests

        /// <summary>Build a minimal valid MIDI file as a byte array.</summary>
        static byte[] BuildTestMidi(int format, int trackCount, int ticksPerQuarter,
                                     params byte[][] trackDatas)
        {
            var data = new List<byte>();

            // MThd header
            data.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' });
            data.AddRange(ToBig32(6));
            data.AddRange(ToBig16((uint)format));
            data.AddRange(ToBig16((uint)trackCount));
            data.AddRange(ToBig16((uint)ticksPerQuarter));

            // MTrk chunks
            foreach (var td in trackDatas)
            {
                data.AddRange(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' });
                data.AddRange(ToBig32((uint)td.Length));
                data.AddRange(td);
            }

            return data.ToArray();
        }

        static byte[] ToBig32(uint v) => new byte[]
        {
            (byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
            (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)
        };

        static byte[] ToBig16(uint v) => new byte[]
        {
            (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)
        };

        [Fact]
        public void ParseMidiBytes_NullData_ReturnsNull()
        {
            Assert.Null(SongMidiCore.ParseMidiBytes(null));
        }

        [Fact]
        public void ParseMidiBytes_TooShort_ReturnsNull()
        {
            Assert.Null(SongMidiCore.ParseMidiBytes(new byte[10]));
        }

        [Fact]
        public void ParseMidiBytes_BadMagic_ReturnsNull()
        {
            var data = new byte[14];
            data[0] = (byte)'X'; // not 'M'
            Assert.Null(SongMidiCore.ParseMidiBytes(data));
        }

        [Fact]
        public void ParseMidiBytes_EmptyFormat0_ParsesHeader()
        {
            // Single track with just end-of-track meta event
            byte[] trackData = new byte[]
            {
                0x00,       // delta time = 0
                0xFF, 0x2F, 0x00  // end of track
            };

            byte[] midi = BuildTestMidi(0, 1, 480, trackData);
            var info = SongMidiCore.ParseMidiBytes(midi);

            Assert.NotNull(info);
            Assert.Equal(0, info.Format);
            Assert.Equal(1, info.TrackCount);
            Assert.Equal(480, info.TicksPerQuarterNote);
            Assert.Equal(120.0, info.TempoBPM); // default
            Assert.Single(info.Tracks);
            Assert.Equal(0, info.Tracks[0].NoteCount);
        }

        [Fact]
        public void ParseMidiBytes_WithTempo_ExtractsBPM()
        {
            // Track with tempo = 500000 us/beat = 120 BPM
            // Then one with 400000 us/beat = 150 BPM
            byte[] trackData = new byte[]
            {
                0x00,               // delta = 0
                0xFF, 0x51, 0x03,   // tempo meta event
                0x06, 0x1A, 0x80,   // 400000 us = 150 BPM
                0x00,               // delta = 0
                0xFF, 0x2F, 0x00    // end of track
            };

            byte[] midi = BuildTestMidi(1, 1, 96, trackData);
            var info = SongMidiCore.ParseMidiBytes(midi);

            Assert.NotNull(info);
            Assert.Equal(150.0, info.TempoBPM, 1);
        }

        [Fact]
        public void ParseMidiBytes_WithNotes_CountsCorrectly()
        {
            // Track with 3 note-on events (velocity > 0) and 3 note-off events
            byte[] trackData = new byte[]
            {
                // Note On C4, vel=100
                0x00, 0x90, 60, 100,
                // Wait 96 ticks
                0x60,
                // Note Off C4
                0x80, 60, 0,
                // Note On E4, vel=80
                0x00, 0x90, 64, 80,
                0x60, 0x80, 64, 0,
                // Note On G4, vel=90
                0x00, 0x90, 67, 90,
                0x60, 0x80, 67, 0,
                // End of track
                0x00, 0xFF, 0x2F, 0x00
            };

            byte[] midi = BuildTestMidi(0, 1, 96, trackData);
            var info = SongMidiCore.ParseMidiBytes(midi);

            Assert.NotNull(info);
            Assert.Single(info.Tracks);
            Assert.Equal(3, info.Tracks[0].NoteCount);
            Assert.Contains(0, info.Tracks[0].Channels); // channel 0
        }

        [Fact]
        public void ParseMidiBytes_NoteOnZeroVelocity_NotCounted()
        {
            // Note On with velocity 0 is treated as Note Off
            byte[] trackData = new byte[]
            {
                0x00, 0x90, 60, 100,  // Note On (counted)
                0x60, 0x90, 60, 0,    // Note On vel=0 = Note Off (not counted)
                0x00, 0xFF, 0x2F, 0x00
            };

            byte[] midi = BuildTestMidi(0, 1, 96, trackData);
            var info = SongMidiCore.ParseMidiBytes(midi);

            Assert.NotNull(info);
            Assert.Equal(1, info.Tracks[0].NoteCount);
        }

        [Fact]
        public void ParseMidiBytes_ProgramChange_Tracked()
        {
            byte[] trackData = new byte[]
            {
                0x00, 0xC0, 42,       // Program change ch0 -> 42
                0x00, 0xC1, 73,       // Program change ch1 -> 73
                0x00, 0xFF, 0x2F, 0x00
            };

            byte[] midi = BuildTestMidi(0, 1, 96, trackData);
            var info = SongMidiCore.ParseMidiBytes(midi);

            Assert.NotNull(info);
            var track = info.Tracks[0];
            Assert.Equal(2, track.InstrumentChanges.Count);
            Assert.Contains(42, track.InstrumentChanges);
            Assert.Contains(73, track.InstrumentChanges);
            Assert.Contains(0, track.Channels);
            Assert.Contains(1, track.Channels);
        }

        [Fact]
        public void ParseMidiBytes_MultipleTracks_ParsedIndependently()
        {
            byte[] track0 = new byte[]
            {
                0x00, 0x90, 60, 100,
                0x60, 0x80, 60, 0,
                0x00, 0xFF, 0x2F, 0x00
            };
            byte[] track1 = new byte[]
            {
                0x00, 0x91, 64, 80,
                0x60, 0x81, 64, 0,
                0x00, 0x91, 67, 90,
                0x60, 0x81, 67, 0,
                0x00, 0xFF, 0x2F, 0x00
            };

            byte[] midi = BuildTestMidi(1, 2, 96, track0, track1);
            var info = SongMidiCore.ParseMidiBytes(midi);

            Assert.NotNull(info);
            Assert.Equal(2, info.Tracks.Count);
            Assert.Equal(1, info.Tracks[0].NoteCount);
            Assert.Equal(2, info.Tracks[1].NoteCount);
            Assert.Contains(0, info.Tracks[0].Channels);
            Assert.Contains(1, info.Tracks[1].Channels);
        }

        [Fact]
        public void ParseMidiBytes_RunningStatus_HandledCorrectly()
        {
            // Note On, then running status (omitted status byte) for second note
            byte[] trackData = new byte[]
            {
                0x00, 0x90, 60, 100,  // Note On C4
                0x60, 60, 0,          // Running status: Note On C4 vel=0 (= note off)
                0x00, 64, 80,         // Running status: Note On E4
                0x60, 64, 0,          // Running status: Note On E4 vel=0 (= note off)
                0x00, 0xFF, 0x2F, 0x00
            };

            byte[] midi = BuildTestMidi(0, 1, 96, trackData);
            var info = SongMidiCore.ParseMidiBytes(midi);

            Assert.NotNull(info);
            // First Note On (vel 100) + running status vel=0 (not counted) + running status vel=80 + running status vel=0
            Assert.Equal(2, info.Tracks[0].NoteCount);
        }

        [Fact]
        public void ParseMidiBytes_TotalTicks_AccumulatesDeltaTimes()
        {
            byte[] trackData = new byte[]
            {
                0x60,               // delta = 96
                0x90, 60, 100,
                0x60,               // delta = 96
                0x80, 60, 0,
                0x00,               // delta = 0
                0xFF, 0x2F, 0x00
            };

            byte[] midi = BuildTestMidi(0, 1, 96, trackData);
            var info = SongMidiCore.ParseMidiBytes(midi);

            Assert.NotNull(info);
            Assert.Equal(192, info.Tracks[0].TotalTicks); // 96 + 96 + 0
        }

        [Fact]
        public void ParseMidiFile_NonexistentFile_ReturnsNull()
        {
            var result = SongMidiCore.ParseMidiFile("/nonexistent/file.mid");
            Assert.Null(result);
        }

        [Fact]
        public void ParseMidiFile_WrittenAndReadBack_RoundTrips()
        {
            // Export a simple MIDI, then parse it back
            var track = new SongMidiCore.Track();
            // Add a tempo change (BB = tempo command, value*2 = BPM -> 60)
            track.codes.Add(new SongMidiCore.Code(0, 0, 0xBB, 60)); // tempo 120 BPM
            // Add a note
            track.codes.Add(new SongMidiCore.Code(0, 0, 0xD0, 60, 100)); // note C4
            // Add a wait
            track.codes.Add(new SongMidiCore.Code(0, 0, 0x80 + 32)); // wait
            // End
            track.codes.Add(new SongMidiCore.Code(0, 96, 0xB1)); // FINE

            var tracks = new List<SongMidiCore.Track> { track };
            byte[] midiData = SongMidiCore.ExportMidi(tracks, 1, 0, 0, 0);

            var info = SongMidiCore.ParseMidiBytes(midiData);
            Assert.NotNull(info);
            Assert.Equal(1, info.Format);
            Assert.Equal(2, info.TrackCount); // conductor + 1 music track
            Assert.Equal(24, info.TicksPerQuarterNote);
            Assert.True(info.Tracks.Count >= 1);
        }

        #endregion

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
