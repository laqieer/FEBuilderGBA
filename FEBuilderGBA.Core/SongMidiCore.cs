using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform MIDI export/import for GBA song data.
    /// Ported from WinForms SongUtil.cs — uses CoreState.ROM instead of Program.ROM.
    /// </summary>
    public static class SongMidiCore
    {
        #region Constants

        static readonly int[] WaitCode = new int[]{
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
            16, 17, 18, 19, 20, 21, 22, 23, 24, 28, 30, 32, 36, 40, 42, 44,
            48, 52, 54, 56, 60, 64, 66, 68, 72, 76, 78, 80, 84, 88, 90, 92, 96,
        };

        const uint WAIT_START = 0x80;
        const uint WAIT_END = 0x80 + 48;
        const uint EOT = 0xCE;
        const uint TIE = 0xCF;
        const uint NOTE_START = 0xD0;
        const uint NOTE_END = 0xFF;
        const uint LOOP_LABEL_CODE = 0xFEFEFEFE;
        const uint WAIT_TIE = U.NOT_FOUND;
        const uint WAIT_LOOPEND = U.NOT_FOUND - 1;

        #endregion

        #region Data Types

        /// <summary>A single command within a GBA song track.</summary>
        public class Code
        {
            public uint addr;
            public uint waitCount;
            public uint type;
            public uint value;
            public uint value2;
            public uint value3;
            public bool isAbbreviation;

            public Code(uint addr, uint waitCount, uint type,
                        uint value = 0, uint value2 = 0, uint value3 = 0,
                        bool isAbbreviation = false)
            {
                this.addr = addr;
                this.waitCount = waitCount;
                this.type = type;
                this.value = value;
                this.value2 = value2;
                this.value3 = value3;
                this.isAbbreviation = isAbbreviation;
            }
        }

        /// <summary>A parsed song track (list of codes).</summary>
        public class Track
        {
            public List<Code> codes = new List<Code>();
            public uint basepointer;
        }

        class PlayKeys
        {
            public byte key;
            public uint stopTime;
            public PlayKeys(byte key, uint stopTime)
            {
                this.key = key;
                this.stopTime = stopTime;
            }
        }

        /// <summary>A raw MIDI event extracted during parsing.</summary>
        public class MidiEvent
        {
            /// <summary>Absolute time in MIDI ticks from start of track.</summary>
            public int AbsoluteTick;
            /// <summary>MIDI status type (0x80=NoteOff, 0x90=NoteOn, 0xB0=CC, 0xC0=ProgramChange, 0xFF=Meta).</summary>
            public int StatusType;
            /// <summary>MIDI channel (0-15).</summary>
            public int Channel;
            /// <summary>First data byte (key, controller number, program, meta type).</summary>
            public int Data1;
            /// <summary>Second data byte (velocity, controller value).</summary>
            public int Data2;
            /// <summary>For tempo meta events: microseconds per beat.</summary>
            public int MetaTempo;
        }

        #endregion

        #region Byte Helpers (inlined from WinForms U.cs)

        static uint byteToWait(uint b)
        {
            if (b < WAIT_START) return 0;
            int idx = (int)b - (int)WAIT_START;
            if (idx < 0 || idx >= WaitCode.Length) return 0;
            return (uint)WaitCode[idx];
        }

        static uint byteToNote(uint b)
        {
            int idx = (int)b + 1 - (int)NOTE_START;
            if (idx < 0 || idx >= WaitCode.Length) return 0;
            return (uint)WaitCode[idx];
        }

        static bool CheckGTPRange(uint gtp)
        {
            return gtp >= 1 && gtp <= 3;
        }

        static void AppendBig32(List<byte> data, uint a)
        {
            data.Add((byte)((a >> 24) & 0xFF));
            data.Add((byte)((a >> 16) & 0xFF));
            data.Add((byte)((a >> 8) & 0xFF));
            data.Add((byte)(a & 0xFF));
        }

        static void AppendBig24(List<byte> data, uint a)
        {
            data.Add((byte)((a >> 16) & 0xFF));
            data.Add((byte)((a >> 8) & 0xFF));
            data.Add((byte)(a & 0xFF));
        }

        static void AppendBig16(List<byte> data, uint a)
        {
            data.Add((byte)((a >> 8) & 0xFF));
            data.Add((byte)(a & 0xFF));
        }

        static void AppendAscii(List<byte> data, string str)
        {
            byte[] b = Encoding.ASCII.GetBytes(str);
            data.AddRange(b);
        }

        static void AppendVLengthCode(List<byte> data, int time)
        {
            char word1 = (char)(time & 0x7f);
            char word2 = (char)((time >> 7) & 0x7f);
            char word3 = (char)((time >> 14) & 0x7f);
            char word4 = (char)((time >> 21) & 0x7f);

            if (word4 != 0)
            {
                data.Add((byte)(word4 | 0x80));
                data.Add((byte)(word3 | 0x80));
                data.Add((byte)(word2 | 0x80));
            }
            else if (word3 != 0)
            {
                data.Add((byte)(word3 | 0x80));
                data.Add((byte)(word2 | 0x80));
            }
            else if (word2 != 0)
            {
                data.Add((byte)(word2 | 0x80));
            }
            data.Add((byte)(word1));
        }

        static uint ReadBig32(byte[] data, uint addr)
        {
            if (addr + 3 >= data.Length) return 0;
            return (uint)data[addr + 3] + ((uint)data[addr + 2] << 8)
                 + ((uint)data[addr + 1] << 16) + ((uint)data[addr] << 24);
        }

        static uint ReadBig16(byte[] data, uint addr)
        {
            if (addr + 1 >= data.Length) return 0;
            return (uint)data[addr + 1] + ((uint)data[addr] << 8);
        }

        static uint ReadVLengthCode(byte[] data, uint pos, out uint outPos)
        {
            uint ret = (uint)(data[pos] & 0x7F);
            while ((data[pos] & 0x80) == 0x80)
            {
                pos++;
                if (pos >= data.Length) break;
                ret = (ret << 7) | (uint)(data[pos] & 0x7F);
            }
            outPos = pos + 1;
            return ret;
        }

        static uint GrepBytes(byte[] data, byte[] needle, uint start)
        {
            if (needle.Length == 0 || start >= data.Length) return U.NOT_FOUND;
            int end = data.Length - needle.Length;
            for (int i = (int)start; i <= end; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return (uint)i;
            }
            return U.NOT_FOUND;
        }

        /// <summary>Reverse volume curve (linear -> exponential for MIDI).</summary>
        static uint ExpVolRev(uint volume)
        {
            double v = volume;
            if (v == 0) return 0;
            v /= 127;
            v = Math.Pow(v, 1.0 / (10.0 / 6.0));
            v *= 127;
            if (v < 0) return 0;
            if (v >= 127) return 127;
            return (uint)v;
        }

        static void CalcPitchBendGBAToMidi(uint arg, out uint outArg1, out uint outArg2)
        {
            outArg1 = 0;
            outArg2 = arg;
        }

        static uint CalcPitchBendMidiToGBA(uint arg1, uint arg2)
        {
            return arg2;
        }

        #endregion

        #region Track Parsing

        /// <summary>Parse all tracks from a song header in ROM.</summary>
        public static List<Track> ParseTracks(ROM rom, uint songAddr, uint trackCount)
        {
            var tracks = new List<Track>();
            if (rom == null || !U.isSafetyOffset(songAddr, rom)) return tracks;

            uint limitter = (uint)rom.Data.Length;

            for (int ti = 0; ti < trackCount; ti++)
            {
                uint ptrOff = songAddr + 8 + (uint)(ti * 4);
                var track = ParseTrackOne(rom, ptrOff, limitter);
                tracks.Add(track);
            }
            InsertLoopLabels(tracks);
            return tracks;
        }

        static Track ParseTrackOne(ROM rom, uint trackpointer, uint limitter)
        {
            var track = new Track();
            if (trackpointer >= limitter) return track;

            uint trackaddr = rom.u32(trackpointer);
            if (!U.isSafetyPointer(trackaddr, rom)) return track;
            trackaddr = U.toOffset(trackaddr);
            track.basepointer = trackpointer;

            uint lastCommand = 0;
            uint waitCount = 0;

            for (uint addr = trackaddr; ; )
            {
                if (addr >= limitter) break;

                uint b = rom.u8(addr);

                if (b == 0xB1)
                {
                    track.codes.Add(new Code(addr, waitCount, b));
                    break;
                }
                else if (b == 0xB2 || b == 0xB3)
                {
                    uint loopaddr = rom.p32(addr + 1);
                    track.codes.Add(new Code(addr, waitCount, b, loopaddr));
                    addr += 5;
                    waitCount += 96;
                    lastCommand = 0;
                }
                else if (b == EOT)
                {
                    track.codes.Add(new Code(addr, waitCount, b));
                    addr++;
                    lastCommand = 0;
                }
                else if (b == 0xBD || b == 0xBB || b == 0xBC || b == 0xBE || b == 0xBF
                      || b == 0xC0 || b == 0xC1 || b == 0xC2 || b == 0xC3
                      || b == 0xC4 || b == 0xC5 || b == 0xC8)
                {
                    if (addr + 1 >= limitter) break;
                    uint next = rom.u8(addr + 1);
                    track.codes.Add(new Code(addr, waitCount, b, next));
                    addr += 2;
                    if (b >= 0xBE && b <= 0xC8) lastCommand = b;
                }
                else if (b == 0xB9)
                {
                    if (addr + 3 >= limitter) break;
                    uint b1 = rom.u8(addr + 1);
                    uint b2 = rom.u8(addr + 2);
                    uint b3 = rom.u8(addr + 3);
                    track.codes.Add(new Code(addr, waitCount, b, b1, b2, b3));
                    addr += 4;
                    lastCommand = 0;
                }
                else if (b >= WAIT_START && b <= WAIT_END)
                {
                    track.codes.Add(new Code(addr, waitCount, b));
                    waitCount += byteToWait(b);
                    addr++;
                }
                else if (b >= TIE && b <= NOTE_END)
                {
                    if (addr + 1 >= limitter) break;
                    lastCommand = 0;
                    uint key = rom.u8(addr + 1);
                    if (key <= 127)
                    {
                        if (addr + 2 >= limitter) break;
                        uint velocity = rom.u8(addr + 2);
                        if (velocity <= 127)
                        {
                            uint gtp = rom.u8(addr + 3);
                            if (CheckGTPRange(gtp))
                            {
                                track.codes.Add(new Code(addr, waitCount, b, key, velocity, gtp));
                                addr += 4;
                            }
                            else
                            {
                                track.codes.Add(new Code(addr, waitCount, b, key, velocity));
                                addr += 3;
                            }
                        }
                        else
                        {
                            track.codes.Add(new Code(addr, waitCount, b, key, U.NOT_FOUND));
                            addr += 2;
                        }
                    }
                    else
                    {
                        track.codes.Add(new Code(addr, waitCount, b, U.NOT_FOUND, U.NOT_FOUND));
                        addr += 1;
                    }
                }
                else if (b <= 127)
                {
                    if (addr + 1 >= limitter) break;
                    uint key = b;
                    uint velocity = rom.u8(addr + 1);
                    if (velocity <= 127)
                    {
                        track.codes.Add(new Code(addr, waitCount, key, velocity));
                        addr += 2;
                    }
                    else
                    {
                        if (lastCommand != 0)
                        {
                            track.codes.Add(new Code(addr, waitCount, lastCommand, b,
                                U.NOT_FOUND, U.NOT_FOUND, isAbbreviation: true));
                            addr += 1;
                        }
                        else
                        {
                            track.codes.Add(new Code(addr, waitCount, key, U.NOT_FOUND));
                            addr += 1;
                        }
                    }
                }
                else
                {
                    track.codes.Add(new Code(addr, waitCount, b));
                    addr++;
                    lastCommand = 0;
                }
            }
            return track;
        }

        static void InsertLoopLabels(List<Track> tracks)
        {
            var labels = new List<uint>();
            foreach (var track in tracks)
            {
                foreach (var code in track.codes)
                {
                    if (code.type == 0xB2 || code.type == 0xB3)
                    {
                        if (!labels.Contains(code.value))
                            labels.Add(code.value);
                    }
                }
            }

            foreach (uint addr in labels)
            {
                InsertLoopReferLabel(tracks, addr);
            }
        }

        static void InsertLoopReferLabel(List<Track> tracks, uint findaddr)
        {
            foreach (var track in tracks)
            {
                for (int i = 0; i < track.codes.Count; i++)
                {
                    if (track.codes[i].addr == findaddr)
                    {
                        track.codes.Insert(i, new Code(
                            track.codes[i].addr,
                            track.codes[i].waitCount,
                            LOOP_LABEL_CODE));
                        return;
                    }
                }
            }
        }

        static int FindLabel(uint label, List<Code> codes)
        {
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].type == LOOP_LABEL_CODE && codes[i].addr == label)
                    return i;
            }
            return 0xFFFFFF;
        }

        static uint SearchMinimumWait(uint currentPos, uint wait, List<PlayKeys> playKeys)
        {
            uint min = wait;
            for (uint w = 0; w < wait; w++)
            {
                for (int n = 0; n < playKeys.Count; n++)
                {
                    if (playKeys[n].stopTime > currentPos + w) continue;
                    if (min > w) min = w;
                }
            }
            if (min == 0) min = 1; // avoid infinite loop
            return min;
        }

        #endregion

        #region MIDI Export

        /// <summary>
        /// Export a song to MIDI format.
        /// Returns the MIDI data as a byte array, or writes to file if filename is provided.
        /// </summary>
        public static byte[] ExportMidi(List<Track> tracks, int numBlks, int priority,
                                         int reverb, uint instrumentAddr)
        {
            var midi = new List<byte>();

            // MIDI header: MThd
            midi.Add((byte)'M'); midi.Add((byte)'T');
            midi.Add((byte)'h'); midi.Add((byte)'d');
            AppendBig32(midi, 6);                      // header size
            AppendBig16(midi, 1);                      // format 1
            AppendBig16(midi, 1 + (uint)tracks.Count); // track count
            AppendBig16(midi, 24);                     // ticks per quarter note

            // Conductor track (tempo, loop markers from track 0)
            {
                var data = new List<byte>();
                if (tracks.Count > 0)
                {
                    var t0 = tracks[0];
                    uint loopStartWait = U.NOT_FOUND;
                    uint loopEndWait = U.NOT_FOUND;
                    for (int i = 0; i < t0.codes.Count; i++)
                    {
                        if (t0.codes[i].type == 0xB2)
                        {
                            int idx = FindLabel(t0.codes[i].value, t0.codes);
                            if (idx < t0.codes.Count)
                            {
                                loopStartWait = t0.codes[idx].waitCount;
                                loopEndWait = t0.codes[i].waitCount;
                            }
                            break;
                        }
                    }

                    uint totalDelta = 0;
                    for (int i = 0; i < t0.codes.Count; i++)
                    {
                        var code = t0.codes[i];

                        if (code.type >= WAIT_START && code.type <= WAIT_END)
                        {
                            totalDelta += byteToWait(code.type);
                            continue;
                        }
                        if (code.type == 0xBB) // TEMPO
                        {
                            uint tempo = code.value * 2;
                            tempo = (uint)(60000000.0 / tempo);
                            AppendVLengthCode(data, (int)totalDelta);
                            data.Add(0xFF); data.Add(0x51); data.Add(0x03);
                            AppendBig24(data, tempo);
                            totalDelta = 0;
                            continue;
                        }
                        if (code.type == LOOP_LABEL_CODE)
                        {
                            if (code.waitCount == loopStartWait)
                            {
                                string marker = "loopStart";
                                AppendVLengthCode(data, (int)totalDelta);
                                data.Add(0xFF); data.Add(0x06);
                                data.Add((byte)marker.Length);
                                AppendAscii(data, marker);
                            }
                            totalDelta = 0;
                            continue;
                        }
                        if (code.type == 0xB2)
                        {
                            if (code.waitCount == loopEndWait)
                            {
                                string marker = "loopEnd";
                                AppendVLengthCode(data, (int)totalDelta);
                                data.Add(0xFF); data.Add(0x06);
                                data.Add((byte)marker.Length);
                                AppendAscii(data, marker);
                            }
                            totalDelta = 0;
                            continue;
                        }
                    }
                }

                // Track end
                data.Add(0x00); data.Add(0xFF); data.Add(0x2F); data.Add(0x00);

                // Write conductor track chunk
                midi.Add((byte)'M'); midi.Add((byte)'T');
                midi.Add((byte)'r'); midi.Add((byte)'k');
                AppendBig32(midi, (uint)data.Count);
                midi.AddRange(data);
            }

            // Music tracks
            for (int t = 0; t < tracks.Count; t++)
            {
                var data = new List<byte>();
                var callstack = new List<int>();

                byte lastKeyshift = 0;
                byte lastKey = 0;
                byte lastVelocity = 0;
                var playKeys = new List<PlayKeys>();

                // Reverb control
                data.Add(0x0);
                data.Add(0xB0);
                data.Add(0x5B);
                data.Add((byte)(((uint)reverb) & 0x7F));

                for (int i = 0; i < tracks[t].codes.Count; i++)
                {
                    var code = tracks[t].codes[i];

                    if (code.type >= WAIT_START && code.type <= WAIT_END)
                    {
                        uint wait = byteToWait(code.type);
                        uint currentWait = code.waitCount;
                        uint endWait = code.waitCount + wait;
                        while (currentWait < endWait)
                        {
                            uint minWait = SearchMinimumWait(currentWait, wait, playKeys);
                            currentWait += minWait;
                            wait -= minWait;

                            data.Add((byte)minWait);

                            bool isFirst = true;
                            for (int n = playKeys.Count - 1; n >= 0; )
                            {
                                if (playKeys[n].stopTime == WAIT_LOOPEND) { }
                                else if (playKeys[n].stopTime > currentWait) { n--; continue; }

                                if (!isFirst) data.Add(0);
                                data.Add(0x80);
                                data.Add(playKeys[n].key);
                                data.Add(0);
                                isFirst = false;
                                playKeys.RemoveAt(n);
                                n = playKeys.Count - 1;
                            }

                            if (isFirst)
                            {
                                data.Add(0x80);
                                data.Add(0);
                                data.Add(0);
                            }
                        }
                        continue;
                    }

                    if (code.type >= TIE && code.type <= NOTE_END)
                    {
                        uint stopTime;
                        if (code.type == TIE) stopTime = WAIT_TIE;
                        else stopTime = byteToNote(code.type) + code.waitCount;

                        if (code.value != U.NOT_FOUND)
                        {
                            lastKey = (byte)code.value;
                            if (code.value2 != U.NOT_FOUND)
                                lastVelocity = (byte)code.value2;
                        }
                        lastKey += lastKeyshift;

                        data.Add(0);
                        data.Add(0x90);
                        data.Add(lastKey);
                        data.Add(lastVelocity);

                        playKeys.Add(new PlayKeys(lastKey, stopTime));
                        continue;
                    }

                    if (code.type <= 127)
                    {
                        lastKey = (byte)code.type;
                        if (code.value != U.NOT_FOUND)
                            lastVelocity = (byte)code.value;
                        lastKey += lastKeyshift;

                        data.Add(0);
                        data.Add(0x90);
                        data.Add(lastKey);
                        data.Add(lastVelocity);
                        playKeys.Add(new PlayKeys(lastKey, WAIT_LOOPEND));
                        continue;
                    }

                    if (code.type == 0xB3)
                    {
                        callstack.Add(i);
                        i = FindLabel(code.value, tracks[t].codes);
                        continue;
                    }
                    if (code.type == 0xB4)
                    {
                        if (callstack.Count > 0)
                        {
                            int last = callstack.Count - 1;
                            i = callstack[last];
                            callstack.RemoveAt(last);
                        }
                        continue;
                    }
                    if (code.type == 0xBC)
                    {
                        lastKeyshift = (byte)code.value;
                        continue;
                    }
                    if (code.type == 0xBD)
                    {
                        data.Add(0x0); data.Add(0xC0); data.Add((byte)code.value);
                        continue;
                    }
                    if (code.type == 0xBE)
                    {
                        data.Add(0x0); data.Add(0xB0); data.Add(0x07);
                        data.Add((byte)ExpVolRev(code.value));
                        continue;
                    }
                    if (code.type == 0xBF)
                    {
                        data.Add(0x0); data.Add(0xB0); data.Add(0x0A);
                        data.Add((byte)code.value);
                        continue;
                    }
                    if (code.type == 0xC0)
                    {
                        CalcPitchBendGBAToMidi(code.value, out uint a1, out uint a2);
                        data.Add(0x0); data.Add(0xE0);
                        data.Add((byte)a1); data.Add((byte)a2);
                        continue;
                    }
                    if (code.type == 0xC1)
                    {
                        data.Add(0x0); data.Add(0xB0); data.Add(0x14);
                        data.Add((byte)code.value);
                        continue;
                    }
                    if (code.type == 0xC2)
                    {
                        data.Add(0x0); data.Add(0xB0); data.Add(0x15);
                        data.Add((byte)code.value);
                        continue;
                    }
                    if (code.type == 0xC3)
                    {
                        data.Add(0x0); data.Add(0xB0); data.Add(0x1A);
                        data.Add((byte)code.value);
                        continue;
                    }
                    if (code.type == 0xC4)
                    {
                        data.Add(0x0); data.Add(0xB0); data.Add(0x01);
                        data.Add((byte)code.value);
                        continue;
                    }
                    if (code.type == 0xC5)
                    {
                        data.Add(0x0); data.Add(0xB0); data.Add(0x16);
                        data.Add((byte)code.value);
                        continue;
                    }
                    if (code.type == 0xC8)
                    {
                        data.Add(0x0); data.Add(0xB0); data.Add(0x18);
                        data.Add((byte)code.value);
                        continue;
                    }
                    if (code.type == EOT)
                    {
                        for (int n = 0; n < playKeys.Count; )
                        {
                            if (playKeys[n].stopTime == WAIT_TIE)
                            {
                                data.Add(0); data.Add(0x80);
                                data.Add(playKeys[n].key); data.Add(0);
                                playKeys.RemoveAt(n);
                                continue;
                            }
                            else n++;
                        }
                        continue;
                    }
                }

                // Track end
                data.Add(0x00); data.Add(0xFF); data.Add(0x2F); data.Add(0x00);

                // Write track chunk
                midi.Add((byte)'M'); midi.Add((byte)'T');
                midi.Add((byte)'r'); midi.Add((byte)'k');
                AppendBig32(midi, (uint)data.Count);
                midi.AddRange(data);
            }

            return midi.ToArray();
        }

        /// <summary>Export a song to a MIDI file.</summary>
        public static void ExportMidiFile(string filename, List<Track> tracks,
                                           int numBlks, int priority, int reverb,
                                           uint instrumentAddr)
        {
            byte[] data = ExportMidi(tracks, numBlks, priority, reverb, instrumentAddr);
            File.WriteAllBytes(filename, data);
        }

        #endregion

        #region MIDI File Parser

        /// <summary>Parsed information about a MIDI file.</summary>
        public class MidiFileInfo
        {
            /// <summary>MIDI format: 0, 1, or 2.</summary>
            public int Format;
            /// <summary>Number of tracks in the file.</summary>
            public int TrackCount;
            /// <summary>Ticks per quarter note (from header division field).</summary>
            public int TicksPerQuarterNote;
            /// <summary>Tempo in BPM from the first tempo meta-event (default 120).</summary>
            public double TempoBPM = 120.0;
            /// <summary>Per-track information.</summary>
            public List<MidiTrackInfo> Tracks = new List<MidiTrackInfo>();
        }

        /// <summary>Parsed information about a single MIDI track.</summary>
        public class MidiTrackInfo
        {
            /// <summary>Zero-based track index.</summary>
            public int Index;
            /// <summary>Number of Note-On events (velocity > 0).</summary>
            public int NoteCount;
            /// <summary>Total number of MIDI events parsed.</summary>
            public int EventCount;
            /// <summary>Set of MIDI channels used (0-15).</summary>
            public HashSet<int> Channels = new HashSet<int>();
            /// <summary>Program Change values encountered.</summary>
            public List<int> InstrumentChanges = new List<int>();
            /// <summary>Total delta-time ticks in this track.</summary>
            public int TotalTicks;
            /// <summary>Internal: first tempo BPM found in this track (0 if none).</summary>
            internal double _tempoBPM;
        }

        /// <summary>
        /// Parse a MIDI file and extract header + per-track information.
        /// Does not convert to GBA format — just reads and reports the contents.
        /// </summary>
        /// <param name="filename">Path to the .mid/.midi file.</param>
        /// <returns>Parsed MIDI info, or null if the file is not a valid MIDI.</returns>
        public static MidiFileInfo ParseMidiFile(string filename)
        {
            if (!File.Exists(filename))
                return null;

            byte[] data = File.ReadAllBytes(filename);
            return ParseMidiBytes(data);
        }

        /// <summary>
        /// Parse MIDI data from a byte array.
        /// </summary>
        public static MidiFileInfo ParseMidiBytes(byte[] data)
        {
            if (data == null || data.Length < 14)
                return null;

            // Verify MThd header
            if (data[0] != 'M' || data[1] != 'T' || data[2] != 'h' || data[3] != 'd')
                return null;

            uint headerLen = ReadBig32(data, 4);
            if (headerLen < 6)
                return null;

            var info = new MidiFileInfo();
            info.Format = (int)ReadBig16(data, 8);
            info.TrackCount = (int)ReadBig16(data, 10);

            uint division = ReadBig16(data, 12);
            if ((division & 0x8000) == 0)
            {
                // Ticks per quarter note
                info.TicksPerQuarterNote = (int)division;
            }
            else
            {
                // SMPTE-based — store raw value, set a reasonable default
                info.TicksPerQuarterNote = (int)(division & 0x7FFF);
            }

            // Parse each track chunk
            uint pos = 8 + headerLen;
            for (int t = 0; t < info.TrackCount; t++)
            {
                if (pos + 8 > data.Length)
                    break;

                // Verify MTrk header
                if (data[pos] != 'M' || data[pos + 1] != 'T'
                    || data[pos + 2] != 'r' || data[pos + 3] != 'k')
                {
                    // Skip unknown chunk
                    if (pos + 4 < data.Length)
                    {
                        uint skipLen = ReadBig32(data, pos + 4);
                        pos += 8 + skipLen;
                    }
                    else break;
                    t--; // Don't count non-MTrk chunks
                    continue;
                }

                uint trackLen = ReadBig32(data, pos + 4);
                uint trackStart = pos + 8;
                uint trackEnd = trackStart + trackLen;
                if (trackEnd > data.Length)
                    trackEnd = (uint)data.Length;

                var trackInfo = ParseMidiTrackChunk(data, trackStart, trackEnd, t);
                info.Tracks.Add(trackInfo);

                // Extract tempo from first tempo event found in any track
                if (info.TempoBPM == 120.0 && trackInfo._tempoBPM > 0)
                    info.TempoBPM = trackInfo._tempoBPM;

                pos = trackEnd;
            }

            return info;
        }

        /// <summary>
        /// Parse a single MIDI track chunk and extract stats.
        /// </summary>
        static MidiTrackInfo ParseMidiTrackChunk(byte[] data, uint start, uint end, int index)
        {
            var track = new MidiTrackInfo { Index = index };
            uint pos = start;
            byte runningStatus = 0;
            double tempoBPM = 0;
            int totalTicks = 0;

            while (pos < end)
            {
                // Read delta time (variable-length)
                uint deltaTime = ReadVLengthCode(data, pos, out uint nextPos);
                if (nextPos > end) break;
                pos = nextPos;
                totalTicks += (int)deltaTime;
                track.EventCount++;

                if (pos >= end) break;
                byte statusByte = data[pos];

                // Handle meta events (0xFF)
                if (statusByte == 0xFF)
                {
                    pos++;
                    if (pos >= end) break;
                    byte metaType = data[pos++];
                    if (pos >= end) break;

                    uint metaLen = ReadVLengthCode(data, pos, out nextPos);
                    if (nextPos > end) break;
                    pos = nextPos;

                    if (metaType == 0x51 && metaLen == 3 && pos + 3 <= end)
                    {
                        // Tempo: microseconds per quarter note
                        uint usPerBeat = ((uint)data[pos] << 16)
                                       | ((uint)data[pos + 1] << 8)
                                       | data[pos + 2];
                        if (usPerBeat > 0 && tempoBPM == 0)
                            tempoBPM = 60000000.0 / usPerBeat;
                    }

                    pos += metaLen;
                    continue;
                }

                // Handle SysEx events (0xF0, 0xF7)
                if (statusByte == 0xF0 || statusByte == 0xF7)
                {
                    pos++;
                    uint sysLen = ReadVLengthCode(data, pos, out nextPos);
                    if (nextPos > end) break;
                    pos = nextPos + sysLen;
                    continue;
                }

                // Handle channel messages
                bool isRunning = false;
                if (statusByte >= 0x80)
                {
                    runningStatus = statusByte;
                    pos++;
                }
                else
                {
                    // Running status — statusByte is the first data byte
                    isRunning = true;
                }

                byte status = runningStatus;
                int channel = status & 0x0F;
                int msgType = status & 0xF0;

                // Read first data byte: for running status it's statusByte (already read, advance pos);
                // for new status it's the next byte after the status byte.
                byte ReadDataByte()
                {
                    if (isRunning)
                    {
                        isRunning = false;  // only use statusByte once
                        pos++;              // advance past the data byte we read as statusByte
                        return statusByte;
                    }
                    return (pos < end) ? data[pos++] : (byte)0;
                }

                switch (msgType)
                {
                    case 0x80: // Note Off (2 data bytes)
                    {
                        ReadDataByte(); // key
                        if (pos < end) pos++; // velocity
                        track.Channels.Add(channel);
                        break;
                    }
                    case 0x90: // Note On (2 data bytes)
                    {
                        ReadDataByte(); // key
                        byte velocity = (pos < end) ? data[pos++] : (byte)0;
                        track.Channels.Add(channel);
                        if (velocity > 0)
                            track.NoteCount++;
                        break;
                    }
                    case 0xA0: // Polyphonic Key Pressure (2 data bytes)
                    {
                        ReadDataByte();
                        if (pos < end) pos++;
                        track.Channels.Add(channel);
                        break;
                    }
                    case 0xB0: // Control Change (2 data bytes)
                    {
                        ReadDataByte();
                        if (pos < end) pos++;
                        track.Channels.Add(channel);
                        break;
                    }
                    case 0xC0: // Program Change (1 data byte)
                    {
                        byte program = ReadDataByte();
                        track.Channels.Add(channel);
                        track.InstrumentChanges.Add(program);
                        break;
                    }
                    case 0xD0: // Channel Pressure (1 data byte)
                    {
                        ReadDataByte();
                        track.Channels.Add(channel);
                        break;
                    }
                    case 0xE0: // Pitch Bend (2 data bytes)
                    {
                        ReadDataByte();
                        if (pos < end) pos++;
                        track.Channels.Add(channel);
                        break;
                    }
                    default:
                        // Unknown status — skip
                        break;
                }
            }

            track.TotalTicks = totalTicks;
            track._tempoBPM = tempoBPM;
            return track;
        }

        #endregion

        #region MIDI Raw Event Extraction

        /// <summary>
        /// Extract raw MIDI events from a parsed MIDI file's byte data.
        /// Returns a list of lists — one list of events per MIDI track.
        /// </summary>
        public static List<List<MidiEvent>> ExtractMidiEvents(byte[] data)
        {
            var result = new List<List<MidiEvent>>();
            if (data == null || data.Length < 14) return result;

            // Verify MThd
            if (data[0] != 'M' || data[1] != 'T' || data[2] != 'h' || data[3] != 'd')
                return result;

            uint headerLen = ReadBig32(data, 4);
            int trackCount = (int)ReadBig16(data, 10);

            uint pos = 8 + headerLen;
            for (int t = 0; t < trackCount; t++)
            {
                if (pos + 8 > data.Length) break;

                if (data[pos] != 'M' || data[pos + 1] != 'T'
                    || data[pos + 2] != 'r' || data[pos + 3] != 'k')
                {
                    if (pos + 4 < data.Length)
                    {
                        uint skipLen = ReadBig32(data, pos + 4);
                        pos += 8 + skipLen;
                    }
                    else break;
                    t--;
                    continue;
                }

                uint trackLen = ReadBig32(data, pos + 4);
                uint trackStart = pos + 8;
                uint trackEnd = trackStart + trackLen;
                if (trackEnd > data.Length) trackEnd = (uint)data.Length;

                result.Add(ExtractTrackEvents(data, trackStart, trackEnd));
                pos = trackEnd;
            }

            return result;
        }

        static List<MidiEvent> ExtractTrackEvents(byte[] data, uint start, uint end)
        {
            var events = new List<MidiEvent>();
            uint pos = start;
            byte runningStatus = 0;
            int absoluteTick = 0;

            while (pos < end)
            {
                uint deltaTime = ReadVLengthCode(data, pos, out uint nextPos);
                if (nextPos > end) break;
                pos = nextPos;
                absoluteTick += (int)deltaTime;

                if (pos >= end) break;
                byte statusByte = data[pos];

                // Meta events
                if (statusByte == 0xFF)
                {
                    pos++;
                    if (pos >= end) break;
                    byte metaType = data[pos++];
                    if (pos >= end) break;
                    uint metaLen = ReadVLengthCode(data, pos, out nextPos);
                    if (nextPos > end) break;
                    pos = nextPos;

                    if (metaType == 0x51 && metaLen == 3 && pos + 3 <= end)
                    {
                        int usPerBeat = (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];
                        events.Add(new MidiEvent
                        {
                            AbsoluteTick = absoluteTick,
                            StatusType = 0xFF,
                            Data1 = metaType,
                            MetaTempo = usPerBeat
                        });
                    }
                    else if (metaType == 0x2F)
                    {
                        // End of track — don't add as event, just stop
                    }

                    pos += metaLen;
                    continue;
                }

                // SysEx
                if (statusByte == 0xF0 || statusByte == 0xF7)
                {
                    pos++;
                    uint sysLen = ReadVLengthCode(data, pos, out nextPos);
                    if (nextPos > end) break;
                    pos = nextPos + sysLen;
                    continue;
                }

                // Channel messages
                bool isRunning = false;
                if (statusByte >= 0x80)
                {
                    runningStatus = statusByte;
                    pos++;
                }
                else
                {
                    isRunning = true;
                }

                byte status = runningStatus;
                int channel = status & 0x0F;
                int msgType = status & 0xF0;

                byte ReadData()
                {
                    if (isRunning)
                    {
                        isRunning = false;
                        pos++;
                        return statusByte;
                    }
                    return (pos < end) ? data[pos++] : (byte)0;
                }

                switch (msgType)
                {
                    case 0x80: // Note Off
                    {
                        byte key = ReadData();
                        byte vel = (pos < end) ? data[pos++] : (byte)0;
                        events.Add(new MidiEvent
                        {
                            AbsoluteTick = absoluteTick,
                            StatusType = 0x80,
                            Channel = channel,
                            Data1 = key,
                            Data2 = vel
                        });
                        break;
                    }
                    case 0x90: // Note On
                    {
                        byte key = ReadData();
                        byte vel = (pos < end) ? data[pos++] : (byte)0;
                        events.Add(new MidiEvent
                        {
                            AbsoluteTick = absoluteTick,
                            StatusType = vel > 0 ? 0x90 : 0x80, // vel=0 -> note off
                            Channel = channel,
                            Data1 = key,
                            Data2 = vel
                        });
                        break;
                    }
                    case 0xA0: // Poly aftertouch
                        ReadData();
                        if (pos < end) pos++;
                        break;
                    case 0xB0: // Control Change
                    {
                        byte cc = ReadData();
                        byte val = (pos < end) ? data[pos++] : (byte)0;
                        events.Add(new MidiEvent
                        {
                            AbsoluteTick = absoluteTick,
                            StatusType = 0xB0,
                            Channel = channel,
                            Data1 = cc,
                            Data2 = val
                        });
                        break;
                    }
                    case 0xC0: // Program Change
                    {
                        byte prog = ReadData();
                        events.Add(new MidiEvent
                        {
                            AbsoluteTick = absoluteTick,
                            StatusType = 0xC0,
                            Channel = channel,
                            Data1 = prog
                        });
                        break;
                    }
                    case 0xD0: // Channel Pressure
                        ReadData();
                        break;
                    case 0xE0: // Pitch Bend
                    {
                        byte lsb = ReadData();
                        byte msb = (pos < end) ? data[pos++] : (byte)0;
                        events.Add(new MidiEvent
                        {
                            AbsoluteTick = absoluteTick,
                            StatusType = 0xE0,
                            Channel = channel,
                            Data1 = lsb,
                            Data2 = msb
                        });
                        break;
                    }
                    default:
                        break;
                }
            }

            return events;
        }

        #endregion

        #region MIDI to GBA Conversion

        /// <summary>GBA ticks per quarter note (standard).</summary>
        const int GBA_TPQN = 24;

        /// <summary>
        /// Convert a parsed MIDI file to GBA song binary data.
        /// </summary>
        /// <param name="midi">Parsed MIDI file info.</param>
        /// <param name="midiBytes">Raw MIDI file bytes (needed for event extraction).</param>
        /// <param name="instrumentAddr">GBA pointer to the instrument set.</param>
        /// <returns>Complete GBA song binary, or null on error.</returns>
        public static byte[] ConvertMidiToGBA(MidiFileInfo midi, byte[] midiBytes, uint instrumentAddr)
        {
            if (midi == null || midiBytes == null) return null;

            var allEvents = ExtractMidiEvents(midiBytes);
            if (allEvents.Count == 0) return null;

            int tpqn = midi.TicksPerQuarterNote;
            if (tpqn <= 0) tpqn = 96;

            // For Format 1 MIDI: track 0 is conductor (tempo/meta only), tracks 1+ are music.
            // For Format 0: single track contains everything — split by channel.
            // We handle both by merging all events into per-channel lists.
            var channelEvents = SplitByChannel(allEvents, midi.Format);

            // Filter out channels with no notes
            var musicChannels = new List<List<MidiEvent>>();
            var tempoEvents = ExtractTempoEvents(allEvents);
            foreach (var ch in channelEvents)
            {
                bool hasNotes = false;
                foreach (var ev in ch)
                {
                    if (ev.StatusType == 0x90) { hasNotes = true; break; }
                }
                if (hasNotes) musicChannels.Add(ch);
            }

            if (musicChannels.Count == 0) return null;

            // Cap at 16 tracks (GBA limit)
            int trackCount = musicChannels.Count;
            if (trackCount > 16) trackCount = 16;

            // Build track data for each channel
            var trackDatas = new List<byte[]>();
            for (int i = 0; i < trackCount; i++)
            {
                byte[] td = BuildGBATrackData(musicChannels[i], tempoEvents, tpqn, i == 0);
                trackDatas.Add(td);
            }

            // Assemble final binary: header + track pointers + track data
            return AssembleGBASong(trackDatas, instrumentAddr);
        }

        /// <summary>Split all MIDI events into per-channel lists. Tempo events go to all channels.</summary>
        static List<List<MidiEvent>> SplitByChannel(List<List<MidiEvent>> allTrackEvents, int format)
        {
            // Collect all channel events, grouped by channel (0-15)
            var channels = new Dictionary<int, List<MidiEvent>>();

            foreach (var trackEvents in allTrackEvents)
            {
                foreach (var ev in trackEvents)
                {
                    if (ev.StatusType == 0xFF) continue; // handled separately
                    int ch = ev.Channel;
                    if (!channels.ContainsKey(ch))
                        channels[ch] = new List<MidiEvent>();
                    channels[ch].Add(ev);
                }
            }

            // Sort each channel's events by absolute tick
            var result = new List<List<MidiEvent>>();
            var sortedKeys = new List<int>(channels.Keys);
            sortedKeys.Sort();
            foreach (int ch in sortedKeys)
            {
                channels[ch].Sort((a, b) => a.AbsoluteTick.CompareTo(b.AbsoluteTick));
                result.Add(channels[ch]);
            }

            return result;
        }

        /// <summary>Extract all tempo meta events from all tracks.</summary>
        static List<MidiEvent> ExtractTempoEvents(List<List<MidiEvent>> allTrackEvents)
        {
            var tempoEvents = new List<MidiEvent>();
            foreach (var trackEvents in allTrackEvents)
            {
                foreach (var ev in trackEvents)
                {
                    if (ev.StatusType == 0xFF && ev.Data1 == 0x51)
                        tempoEvents.Add(ev);
                }
            }
            tempoEvents.Sort((a, b) => a.AbsoluteTick.CompareTo(b.AbsoluteTick));
            return tempoEvents;
        }

        /// <summary>Scale a MIDI tick value to GBA ticks.</summary>
        static int ScaleTick(int midiTick, int midiTpqn)
        {
            if (midiTpqn <= 0) return midiTick;
            return (int)((long)midiTick * GBA_TPQN / midiTpqn);
        }

        /// <summary>
        /// Encode a GBA wait duration using the wait table.
        /// Emits one or more wait commands (0x80+index) into the output list.
        /// </summary>
        static void EncodeWait(List<byte> output, int gbaTicks)
        {
            while (gbaTicks > 0)
            {
                // Find largest wait table entry <= remaining ticks
                int bestIdx = 0;
                for (int i = WaitCode.Length - 1; i >= 0; i--)
                {
                    if (WaitCode[i] <= gbaTicks && WaitCode[i] > 0)
                    {
                        bestIdx = i;
                        break;
                    }
                }

                if (WaitCode[bestIdx] == 0)
                {
                    // Smallest nonzero wait is index 1 (= 1 tick)
                    bestIdx = 1;
                }

                output.Add((byte)(0x80 + bestIdx));
                gbaTicks -= WaitCode[bestIdx];
            }
        }

        /// <summary>
        /// Find the wait table index closest to the given GBA tick duration.
        /// Returns the index (0-48).
        /// </summary>
        static int FindDurationIndex(int gbaTicks)
        {
            if (gbaTicks <= 0) return 0;
            int bestIdx = 0;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < WaitCode.Length; i++)
            {
                int diff = Math.Abs(WaitCode[i] - gbaTicks);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        /// <summary>
        /// Build GBA track data bytes for a single channel's events.
        /// </summary>
        /// <param name="events">Events for this channel, sorted by AbsoluteTick.</param>
        /// <param name="tempoEvents">Global tempo events.</param>
        /// <param name="midiTpqn">MIDI ticks per quarter note.</param>
        /// <param name="isFirstTrack">If true, emit tempo commands.</param>
        static byte[] BuildGBATrackData(List<MidiEvent> events, List<MidiEvent> tempoEvents,
                                         int midiTpqn, bool isFirstTrack)
        {
            var output = new List<byte>();

            // Merge tempo events into this track's timeline if first track
            var merged = new List<MidiEvent>(events);
            if (isFirstTrack)
            {
                foreach (var te in tempoEvents)
                    merged.Add(te);
                merged.Sort((a, b) => a.AbsoluteTick.CompareTo(b.AbsoluteTick));
            }

            // Build a map of NoteOn -> NoteOff for duration calculation
            // Key: (channel, key, noteOnTick) -> noteOffTick
            var noteOffMap = BuildNoteOffMap(merged);

            int currentGbaTick = 0;

            for (int i = 0; i < merged.Count; i++)
            {
                var ev = merged[i];
                int evGbaTick = ScaleTick(ev.AbsoluteTick, midiTpqn);

                // Emit wait if needed
                int delta = evGbaTick - currentGbaTick;
                if (delta > 0)
                {
                    EncodeWait(output, delta);
                    currentGbaTick = evGbaTick;
                }

                switch (ev.StatusType)
                {
                    case 0xFF: // Meta (tempo)
                        if (ev.Data1 == 0x51 && ev.MetaTempo > 0)
                        {
                            int bpm = (int)(60000000.0 / ev.MetaTempo);
                            if (bpm < 2) bpm = 2;
                            if (bpm > 510) bpm = 510;
                            output.Add(0xBB); // TEMPO command
                            output.Add((byte)(bpm / 2));
                        }
                        break;

                    case 0x90: // Note On
                    {
                        int key = ev.Data1;
                        int velocity = ev.Data2;
                        if (key < 0) key = 0;
                        if (key > 127) key = 127;
                        if (velocity < 0) velocity = 0;
                        if (velocity > 127) velocity = 127;

                        // Find duration from note-off map (keyed by index in merged list)
                        int durationMidiTicks = FindNoteDurationByIndex(noteOffMap, i);
                        int durationGbaTicks = ScaleTick(durationMidiTicks, midiTpqn);
                        if (durationGbaTicks < 1) durationGbaTicks = 1;

                        // Map duration to note command byte (0xD0-0xFF range)
                        // Note commands: 0xCF = tie, 0xD0..0xFF encode duration via wait table
                        // The note byte D0+idx corresponds to WaitCode[idx+1]
                        int durIdx = FindDurationIndex(durationGbaTicks);

                        int noteCmd = (int)NOTE_START + durIdx - 1;
                        if (noteCmd < (int)TIE) noteCmd = (int)TIE;
                        if (noteCmd > (int)NOTE_END) noteCmd = (int)NOTE_END;

                        output.Add((byte)noteCmd);
                        output.Add((byte)key);
                        output.Add((byte)velocity);
                        break;
                    }

                    case 0x80: // Note Off — handled by duration encoding, skip
                        break;

                    case 0xC0: // Program Change
                        output.Add(0xBD); // VOICE command
                        output.Add((byte)(ev.Data1 & 0x7F));
                        break;

                    case 0xB0: // Control Change
                        if (ev.Data1 == 7) // Volume
                        {
                            output.Add(0xBE);
                            output.Add((byte)(ev.Data2 & 0x7F));
                        }
                        else if (ev.Data1 == 10) // Pan
                        {
                            output.Add(0xBF);
                            output.Add((byte)(ev.Data2 & 0x7F));
                        }
                        // Other CCs are ignored in simplified conversion
                        break;

                    case 0xE0: // Pitch Bend
                    {
                        byte bend = (byte)(CalcPitchBendMidiToGBA((uint)ev.Data1, (uint)ev.Data2) & 0x7F);
                        output.Add(0xC0);
                        output.Add(bend);
                        break;
                    }
                }
            }

            // End track
            output.Add(0xB1); // FINE

            return output.ToArray();
        }

        /// <summary>
        /// Build a mapping from NoteOn events to their duration in MIDI ticks.
        /// </summary>
        static Dictionary<int, int> BuildNoteOffMap(List<MidiEvent> events)
        {
            // Map: event index of NoteOn -> duration in MIDI ticks
            var result = new Dictionary<int, int>();

            // For each note-on, find the matching note-off
            // Use a stack per (channel, key)
            var pending = new Dictionary<int, List<int>>(); // key = channel*128+key, value = list of noteOn indices

            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev.StatusType == 0x90)
                {
                    int k = ev.Channel * 128 + ev.Data1;
                    if (!pending.ContainsKey(k))
                        pending[k] = new List<int>();
                    pending[k].Add(i);
                }
                else if (ev.StatusType == 0x80)
                {
                    int k = ev.Channel * 128 + ev.Data1;
                    if (pending.ContainsKey(k) && pending[k].Count > 0)
                    {
                        int noteOnIdx = pending[k][0];
                        pending[k].RemoveAt(0);
                        int duration = ev.AbsoluteTick - events[noteOnIdx].AbsoluteTick;
                        if (duration < 0) duration = 0;
                        result[noteOnIdx] = duration;
                    }
                }
            }

            // Any remaining notes without note-off get a default duration of 1 quarter note
            foreach (var kvp in pending)
            {
                foreach (int idx in kvp.Value)
                {
                    if (!result.ContainsKey(idx))
                        result[idx] = 96; // default: 1 quarter note in MIDI ticks
                }
            }

            return result;
        }

        /// <summary>Find note duration by event index from the note-off map.</summary>
        static int FindNoteDurationByIndex(Dictionary<int, int> noteOffMap, int eventIndex)
        {
            if (noteOffMap.TryGetValue(eventIndex, out int dur))
                return dur;
            return 96; // default quarter note
        }

        /// <summary>
        /// Assemble the complete GBA song binary from track data arrays.
        /// Track pointers use placeholder offsets that will be patched as actual GBA addresses
        /// when written to ROM. The returned binary uses offset 0 as the song header base.
        /// </summary>
        /// <param name="trackDatas">Array of GBA track data byte arrays.</param>
        /// <param name="instrumentAddr">GBA pointer to instrument set.</param>
        /// <returns>Complete song binary.</returns>
        static byte[] AssembleGBASong(List<byte[]> trackDatas, uint instrumentAddr)
        {
            int trackCount = trackDatas.Count;
            // Header: 8 bytes (trackCount, numBlks, priority, reverb, instrumentAddr)
            // Track pointers: trackCount * 4 bytes
            int headerSize = 8 + trackCount * 4;

            // Calculate total size
            int totalSize = headerSize;
            foreach (var td in trackDatas)
                totalSize += td.Length;

            // Pad to 4-byte alignment
            totalSize = (totalSize + 3) & ~3;

            byte[] result = new byte[totalSize];

            // Song header
            result[0] = (byte)trackCount;
            result[1] = 0; // numBlks (default)
            result[2] = 0; // priority (default)
            result[3] = 0; // reverb (default)
            // Instrument pointer (little-endian)
            result[4] = (byte)(instrumentAddr & 0xFF);
            result[5] = (byte)((instrumentAddr >> 8) & 0xFF);
            result[6] = (byte)((instrumentAddr >> 16) & 0xFF);
            result[7] = (byte)((instrumentAddr >> 24) & 0xFF);

            // Track pointers and data — pointers are relative offsets from start,
            // they'll be converted to GBA pointers when written to ROM
            int dataOffset = headerSize;
            for (int i = 0; i < trackCount; i++)
            {
                // Store the offset within the binary (will be patched to GBA pointer later)
                uint offset = (uint)dataOffset;
                int ptrPos = 8 + i * 4;
                result[ptrPos] = (byte)(offset & 0xFF);
                result[ptrPos + 1] = (byte)((offset >> 8) & 0xFF);
                result[ptrPos + 2] = (byte)((offset >> 16) & 0xFF);
                result[ptrPos + 3] = (byte)((offset >> 24) & 0xFF);

                Array.Copy(trackDatas[i], 0, result, dataOffset, trackDatas[i].Length);
                dataOffset += trackDatas[i].Length;
            }

            return result;
        }

        /// <summary>
        /// Patch the track pointers in a GBA song binary to use actual ROM addresses.
        /// Call this after deciding where in ROM the song will be written.
        /// </summary>
        /// <param name="songData">The song binary from ConvertMidiToGBA.</param>
        /// <param name="romOffset">The ROM offset where songData will be written.</param>
        public static void PatchTrackPointers(byte[] songData, uint romOffset)
        {
            if (songData == null || songData.Length < 8) return;

            int trackCount = songData[0];
            if (trackCount > 16) trackCount = 16;

            for (int i = 0; i < trackCount; i++)
            {
                int ptrPos = 8 + i * 4;
                if (ptrPos + 4 > songData.Length) break;

                // Read the relative offset
                uint relOffset = (uint)(songData[ptrPos]
                    | (songData[ptrPos + 1] << 8)
                    | (songData[ptrPos + 2] << 16)
                    | (songData[ptrPos + 3] << 24));

                // Convert to GBA pointer
                uint gbaPtr = relOffset + romOffset + 0x08000000;
                songData[ptrPos] = (byte)(gbaPtr & 0xFF);
                songData[ptrPos + 1] = (byte)((gbaPtr >> 8) & 0xFF);
                songData[ptrPos + 2] = (byte)((gbaPtr >> 16) & 0xFF);
                songData[ptrPos + 3] = (byte)((gbaPtr >> 24) & 0xFF);
            }
        }

        #endregion

        #region MIDI Import

        /// <summary>
        /// Import a MIDI file and write the song data to ROM.
        /// Uses simplified direct conversion (no S-file assembly pipeline).
        /// Returns an error message on failure, or empty string on success.
        /// </summary>
        /// <param name="filename">Path to the .mid/.midi file.</param>
        /// <param name="songHeaderAddr">Address of the song header in ROM.</param>
        /// <param name="instrumentAddr">Instrument set pointer.</param>
        /// <param name="ignoreMOD">Strip MOD/MODT events (unused in simplified conversion).</param>
        /// <param name="ignoreBEND">Strip BEND/BENDR events (unused in simplified conversion).</param>
        /// <param name="ignoreLFOS">Strip LFOS/LFODL events (unused in simplified conversion).</param>
        /// <param name="ignoreHEAD">Strip leading silence (unused in simplified conversion).</param>
        /// <param name="ignoreBACK">Strip trailing silence (unused in simplified conversion).</param>
        public static string ImportMidiFile(string filename, uint songHeaderAddr,
                                             uint instrumentAddr,
                                             bool ignoreMOD = false,
                                             bool ignoreBEND = false,
                                             bool ignoreLFOS = false,
                                             bool ignoreHEAD = false,
                                             bool ignoreBACK = false)
        {
            if (!File.Exists(filename))
                return $"File not found: {filename}";

            byte[] midiBytes = File.ReadAllBytes(filename);
            var midi = ParseMidiBytes(midiBytes);
            if (midi == null)
                return "Failed to parse MIDI file — invalid format.";

            byte[] songData = ConvertMidiToGBA(midi, midiBytes, instrumentAddr);
            if (songData == null)
                return "MIDI conversion failed — no valid music tracks found.";

            ROM rom = CoreState.ROM;
            if (rom == null)
                return "No ROM loaded.";

            // Find free space for the song data
            uint freeAddr = rom.FindFreeSpace(0x100, (uint)songData.Length);
            if (freeAddr == U.NOT_FOUND)
                return $"Cannot find {songData.Length} bytes of free space in ROM.";

            // Patch track pointers to real GBA addresses
            PatchTrackPointers(songData, freeAddr);

            // Write the song data to ROM
            rom.write_range(freeAddr, songData);

            // Update the song table pointer to point to our new song header
            // The songHeaderAddr is the pointer in the song table that points to the song header
            uint gbaPtr = freeAddr + 0x08000000;
            rom.write_u32(songHeaderAddr, gbaPtr);

            return string.Empty; // success
        }

        #endregion
    }
}
