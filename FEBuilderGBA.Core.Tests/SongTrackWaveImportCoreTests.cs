// SPDX-License-Identifier: GPL-3.0-or-later
// #1001 PR1 Core tests for SongTrackWaveImportCore — the whole-song WAV import
// seam (build sample + 2-row voicegroup + one-track song + song header, then
// repoint a song-table entry, as one validate-before-mutate transaction with a
// byte-identical fault restore).
//
// Byte-level assertions against the WF SongUtil.ImportWave layout:
//   * appended sample bytes == SongDirectSoundWavCore.WavToByte(wav)
//   * voicegroup row 0: type=0x00, key=0x3c, P4->sample, ADSR FF 00 FF A5;
//     row 1: 0xFFFFFFFF terminator dwords
//   * song header: track count 1, voicegroup ptr @+4, track ptr @+8
//   * track byte sequence: VOL 127 / KEYSH 0 / VOICE 0 / TIE 60 127 / W96.. /
//     EOT / [GOTO addr when loop] / FINE
//   * loop GOTO operand == track_off + 6 (GBA pointer) when useLoop
//   * NO GOTO emitted when !useLoop (WF default, Copilot plan review pt 3/5)
//   * song-table slot repointed to the new song header
//   * rollback: BeginUndoScope + Rollback restores ROM length + bytes
//   * no-mutation-on-fault: malformed/too-short WAV -> NOT_FOUND, ROM identical
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SongTrackWaveImportCoreTests
    {
        const uint SONG_TABLE_SLOT = 0x800; // a song-table entry pointer slot
        const uint ROM_LEN = 0x20000;       // 128 KiB, ROM end is free space

        // mxlplay command bytes (mirror the Core constants).
        const byte VOL = 0xBE, KEYSH = 0xBC, VOICE = 0xBD;
        const byte EOT = 0xCE, TIE = 0xCF, GOTO = 0xB2, FINE = 0xB1;
        const byte W96 = 48 + 0x80;

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            Array.Fill(data, (byte)0xFF);
            for (uint i = 0; i < 0x1000; i++) data[i] = 0x00;
            rom.LoadLow("synth.gba", data, "BE8E01");
            return rom;
        }

        // A minimal canonical 44-byte WAV header + N ascending data bytes,
        // 8-bit mono. bytes_per_sec (+28) is set so wavToDataSec yields a small
        // playsec we can predict.
        static byte[] MakeMinimalWav(int dataBytes, uint freq = 12000)
        {
            var fp = new List<byte>();
            void Ascii(string s) { foreach (var c in s) fp.Add((byte)c); }
            uint byteRate = freq; // 8-bit mono
            Ascii("RIFF");
            U.append_u32(fp, (uint)(36 + dataBytes));
            Ascii("WAVE");
            Ascii("fmt ");
            U.append_u32(fp, 16);
            U.append_u16(fp, 1);                 // PCM
            U.append_u16(fp, 1);                 // channels
            U.append_u32(fp, freq);              // samples/sec (+24)
            U.append_u32(fp, byteRate);          // bytes/sec (+28)
            U.append_u16(fp, 1);                 // block align (+32)
            U.append_u16(fp, 8);                 // bits/sample (+34)
            Ascii("data");
            U.append_u32(fp, (uint)dataBytes);   // data chunk size (+40)
            for (int i = 0; i < dataBytes; i++)
                fp.Add((byte)(i & 0xFF));
            return fp.ToArray();
        }

        // Recompute the expected playsec the same way WF wavToDataSec does.
        static uint ExpectedPlaysec(byte[] wav)
        {
            uint bps = U.u32(wav, 28);
            uint dcs = U.u32(wav, 40);
            if (dcs > wav.Length - 45) dcs = (uint)(wav.Length - 45);
            uint ret = dcs / bps;
            if (dcs % bps != 0) ret += 1;
            return Math.Max(ret, 1);
        }

        // Expected W96 rest-run count: playsec/2 + (odd remainder) + 11% pad.
        static int ExpectedRestCount(uint playsec)
        {
            int n = (int)(playsec / 2);
            if (playsec % 2 == 1) n++;
            n += (int)(playsec * 0.11f);
            return n;
        }

        // -----------------------------------------------------------------
        // 1. Success (loop): every blob lands with the exact WF byte layout and
        //    the song-table slot is repointed.
        // -----------------------------------------------------------------
        [Fact]
        public void ImportWaveAsSong_Loop_BuildsExpectedBlobsAndRepoints()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                byte[] wav = MakeMinimalWav(dataBytes: 120);
                byte[] expectedSample = SongDirectSoundWavCore.WavToByte(wav, out string werr);
                Assert.Null(werr);
                Assert.NotNull(expectedSample);
                uint playsec = ExpectedPlaysec(wav);

                uint lenBefore = (uint)rom.Data.Length;
                uint headerPtr = SongTrackWaveImportCore.ImportWaveAsSong(
                    rom, SONG_TABLE_SLOT, wav, useLoop: true, out string err);

                Assert.Null(err);
                Assert.NotEqual(U.NOT_FOUND, headerPtr);

                // The song-table slot now points at the new header.
                Assert.Equal(headerPtr, rom.u32(SONG_TABLE_SLOT));
                uint headerOff = U.toOffset(headerPtr);
                Assert.Equal(0u, headerOff % 4); // 4-byte aligned

                // Song header: track count 1, voicegroup ptr @+4, track ptr @+8.
                Assert.Equal(1u, rom.u8(headerOff + 0));
                uint vocaPtr = rom.u32(headerOff + 4);
                uint trackPtr = rom.u32(headerOff + 8);
                Assert.True(U.isPointer(vocaPtr));
                Assert.True(U.isPointer(trackPtr));
                uint vocaOff = U.toOffset(vocaPtr);
                uint trackOff = U.toOffset(trackPtr);
                Assert.Equal(0u, vocaOff % 4);
                Assert.Equal(0u, trackOff % 4);

                // Voicegroup row 0: type=0x00, key=0x3c, P4->sample, ADSR FF 00 FF A5.
                Assert.Equal(0x00u, rom.u8(vocaOff + 0));
                Assert.Equal(0x3cu, rom.u8(vocaOff + 1));
                uint samplePtr = rom.u32(vocaOff + 4);
                Assert.True(U.isPointer(samplePtr));
                Assert.Equal(0xFFu, rom.u8(vocaOff + 8));
                Assert.Equal(0x00u, rom.u8(vocaOff + 9));
                Assert.Equal(0xFFu, rom.u8(vocaOff + 10));
                Assert.Equal(0xA5u, rom.u8(vocaOff + 11));
                // Row 1: 0xFFFFFFFF terminator dwords.
                Assert.Equal(U.NOT_FOUND, rom.u32(vocaOff + 12));
                Assert.Equal(U.NOT_FOUND, rom.u32(vocaOff + 16));
                Assert.Equal(U.NOT_FOUND, rom.u32(vocaOff + 20));

                // Appended sample bytes == WavToByte output, byte-for-byte.
                uint sampleOff = U.toOffset(samplePtr);
                byte[] gotSample = rom.getBinaryData(sampleOff, (uint)expectedSample.Length);
                Assert.Equal(expectedSample, gotSample);

                // Track byte sequence prologue.
                Assert.Equal(VOL, (byte)rom.u8(trackOff + 0));
                Assert.Equal(127u, rom.u8(trackOff + 1));
                Assert.Equal(KEYSH, (byte)rom.u8(trackOff + 2));
                Assert.Equal(0u, rom.u8(trackOff + 3));
                Assert.Equal(VOICE, (byte)rom.u8(trackOff + 4));
                Assert.Equal(0u, rom.u8(trackOff + 5));
                Assert.Equal(TIE, (byte)rom.u8(trackOff + 6));
                Assert.Equal(60u, rom.u8(trackOff + 7));
                Assert.Equal(127u, rom.u8(trackOff + 8));

                // W96 rest run, then EOT.
                int restCount = ExpectedRestCount(playsec);
                uint p = trackOff + 9;
                for (int i = 0; i < restCount; i++)
                {
                    Assert.Equal(W96, (byte)rom.u8(p));
                    p++;
                }
                Assert.Equal(EOT, (byte)rom.u8(p)); p++;

                // GOTO (loop) operand == track_off + 6 (GBA pointer), then FINE.
                Assert.Equal(GOTO, (byte)rom.u8(p)); p++;
                uint gotoPtr = rom.u32(p);
                Assert.Equal(U.toPointer(trackOff + 6), gotoPtr);
                p += 4;
                Assert.Equal(FINE, (byte)rom.u8(p));

                // ROM grew.
                Assert.True(rom.Data.Length > lenBefore);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 2. Success (no loop, WF default): NO GOTO emitted; FINE follows EOT.
        // -----------------------------------------------------------------
        [Fact]
        public void ImportWaveAsSong_NoLoop_OmitsGoto()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;

                byte[] wav = MakeMinimalWav(dataBytes: 80);
                uint playsec = ExpectedPlaysec(wav);

                uint headerPtr = SongTrackWaveImportCore.ImportWaveAsSong(
                    rom, SONG_TABLE_SLOT, wav, useLoop: false, out string err);
                Assert.Null(err);
                Assert.NotEqual(U.NOT_FOUND, headerPtr);

                uint headerOff = U.toOffset(headerPtr);
                uint trackOff = U.toOffset(rom.u32(headerOff + 8));

                int restCount = ExpectedRestCount(playsec);
                uint p = trackOff + 9 + (uint)restCount;
                Assert.Equal(EOT, (byte)rom.u8(p)); p++;
                // Immediately FINE — no GOTO byte in between.
                Assert.Equal(FINE, (byte)rom.u8(p));
                Assert.NotEqual(GOTO, (byte)rom.u8(p));
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 3. Rollback: BeginUndoScope + Rollback restores ROM length + bytes.
        // -----------------------------------------------------------------
        [Fact]
        public void ImportWaveAsSong_OuterRollback_RestoresByteIdentical()
        {
            var savedRom = CoreState.ROM;
            var savedUndo = CoreState.Undo;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                // Seed the song-table slot so we can prove it is restored.
                rom.write_p32(SONG_TABLE_SLOT, 0x1234);

                var undo = new Undo();
                CoreState.Undo = undo;

                byte[] before = (byte[])rom.Data.Clone();
                uint lenBefore = (uint)rom.Data.Length;
                uint slotBefore = rom.u32(SONG_TABLE_SLOT);

                byte[] wav = MakeMinimalWav(dataBytes: 200);

                var ud = new Undo.UndoData
                {
                    time = DateTime.Now,
                    name = "import wave song",
                    list = new List<Undo.UndoPostion>(),
                    filesize = lenBefore,
                };

                uint headerPtr;
                using (ROM.BeginUndoScope(ud))
                {
                    headerPtr = SongTrackWaveImportCore.ImportWaveAsSong(
                        rom, SONG_TABLE_SLOT, wav, useLoop: true, out string err);
                    Assert.Null(err);
                    Assert.NotEqual(U.NOT_FOUND, headerPtr);
                }

                Assert.True(rom.Data.Length > lenBefore);
                Assert.NotEqual(slotBefore, rom.u32(SONG_TABLE_SLOT));

                undo.Rollback(ud);

                Assert.Equal((int)lenBefore, rom.Data.Length);
                Assert.Equal(slotBefore, rom.u32(SONG_TABLE_SLOT));
                Assert.Equal(before, rom.Data);
            }
            finally
            {
                CoreState.ROM = savedRom;
                CoreState.Undo = savedUndo;
            }
        }

        // -----------------------------------------------------------------
        // 4. No-mutation-on-fault: malformed/too-short WAV -> NOT_FOUND, error,
        //    ROM byte-identical (zero mutation).
        // -----------------------------------------------------------------
        [Fact]
        public void ImportWaveAsSong_BadWav_NoMutation()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                byte[] bad = new byte[] { (byte)'X', (byte)'Y', (byte)'Z', 0, 1, 2, 3 };
                uint result = SongTrackWaveImportCore.ImportWaveAsSong(
                    rom, SONG_TABLE_SLOT, bad, useLoop: true, out string err);

                Assert.Equal(U.NOT_FOUND, result);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before.Length, rom.Data.Length);
                Assert.Equal(before, rom.Data); // byte-identical, ZERO mutation
            }
            finally { CoreState.ROM = savedRom; }
        }

        [Fact]
        public void ImportWaveAsSong_NullRomAndNullWav_NoThrow()
        {
            uint r1 = SongTrackWaveImportCore.ImportWaveAsSong(
                null, SONG_TABLE_SLOT, MakeMinimalWav(32), useLoop: true, out string e1);
            Assert.Equal(U.NOT_FOUND, r1);
            Assert.False(string.IsNullOrEmpty(e1));

            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();
                uint r2 = SongTrackWaveImportCore.ImportWaveAsSong(
                    rom, SONG_TABLE_SLOT, null, useLoop: true, out string e2);
                Assert.Equal(U.NOT_FOUND, r2);
                Assert.False(string.IsNullOrEmpty(e2));
                Assert.Equal(before, rom.Data); // null wav -> no mutation
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 5. Out-of-range song-table slot -> NOT_FOUND, no mutation.
        // -----------------------------------------------------------------
        [Fact]
        public void ImportWaveAsSong_SlotOutOfRange_NoMutation()
        {
            var savedRom = CoreState.ROM;
            try
            {
                ROM rom = MakeRom();
                CoreState.ROM = rom;
                byte[] before = (byte[])rom.Data.Clone();

                // Slot 1 byte past the end of a valid 4-byte read.
                uint badSlot = (uint)rom.Data.Length - 2;
                uint result = SongTrackWaveImportCore.ImportWaveAsSong(
                    rom, badSlot, MakeMinimalWav(64), useLoop: true, out string err);

                Assert.Equal(U.NOT_FOUND, result);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Equal(before, rom.Data);
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // 6. Helper unit tests: voicegroup + track builders (pure).
        // -----------------------------------------------------------------
        [Fact]
        public void BuildVoicegroupNoPointers_HasExpectedDefaults()
        {
            byte[] voca = SongTrackWaveImportCore.BuildVoicegroupNoPointers();
            Assert.Equal(24, voca.Length);
            Assert.Equal((byte)0x00, voca[0]);  // type
            Assert.Equal((byte)0x3c, voca[1]);  // base key
            Assert.Equal(0u, U.u32(voca, 4)); // P4 unresolved (caller patches)
            Assert.Equal((byte)0xFF, voca[8]);
            Assert.Equal((byte)0x00, voca[9]);
            Assert.Equal((byte)0xFF, voca[10]);
            Assert.Equal((byte)0xA5, voca[11]);
            Assert.Equal(U.NOT_FOUND, U.u32(voca, 12));
            Assert.Equal(U.NOT_FOUND, U.u32(voca, 16));
            Assert.Equal(U.NOT_FOUND, U.u32(voca, 20));
        }

        [Fact]
        public void BuildTrack_LoopVsNoLoop_GotoIndex()
        {
            byte[] loop = SongTrackWaveImportCore.BuildTrack(4, useLoop: true, out int gi);
            Assert.True(gi > 0);
            Assert.Equal(GOTO, loop[gi - 1]);   // GOTO precedes its operand
            Assert.Equal(FINE, loop[gi + 4]);   // FINE follows the operand

            byte[] noLoop = SongTrackWaveImportCore.BuildTrack(4, useLoop: false, out int gi2);
            Assert.Equal(-1, gi2);
            Assert.Equal(FINE, noLoop[noLoop.Length - 1]);
            Assert.DoesNotContain(GOTO, noLoop); // no GOTO when !useLoop
        }
    }
}
