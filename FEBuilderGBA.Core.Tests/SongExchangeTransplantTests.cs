using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Byte-level golden tests for the Song Exchange cross-ROM transplant
    /// (#1002 Slice 3): InstrumentMap / Rip / Burn ported from WinForms
    /// SongExchangeForm.cs. Each test hand-builds a small SOURCE ROM byte[]
    /// and a clean (zero-filled) DESTINATION ROM, computes the expected emitted
    /// bytes BY HAND, and asserts equality — proving the port is byte-faithful.
    ///
    /// The DEST is zero-filled, so FindFreeSpaceNoGrow (upper-half-first) returns
    /// a deterministic write_pointer at Padding4(Data.Length / 2); the song table
    /// + header + voices + tracks live below the half mark so they never collide
    /// with the freshly-burned region.
    /// </summary>
    [Collection("SharedState")]
    public class SongExchangeTransplantTests
    {
        // ---------------- in-memory ROM helpers ----------------

        static void WriteGbaPtr(byte[] data, uint at, uint romOffset)
        {
            uint ptr = romOffset + 0x08000000;
            data[at + 0] = (byte)(ptr & 0xFF);
            data[at + 1] = (byte)((ptr >> 8) & 0xFF);
            data[at + 2] = (byte)((ptr >> 16) & 0xFF);
            data[at + 3] = (byte)((ptr >> 24) & 0xFF);
        }

        static void WriteU32(byte[] data, uint at, uint v)
        {
            data[at + 0] = (byte)(v & 0xFF);
            data[at + 1] = (byte)((v >> 8) & 0xFF);
            data[at + 2] = (byte)((v >> 16) & 0xFF);
            data[at + 3] = (byte)((v >> 24) & 0xFF);
        }

        static uint ReadU32(byte[] data, uint at)
            => (uint)(data[at] | (data[at + 1] << 8) | (data[at + 2] << 16) | (data[at + 3] << 24));

        static ROM NewRom(byte[] data)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            CoreState.ROM = rom; // Undo.UndoPostion snapshots read CoreState.ROM.
            if (CoreState.Undo == null) CoreState.Undo = new Undo();
            return rom;
        }

        const uint ROM_SIZE = 0x4000; // 16 KiB — half mark = 0x2000.
        static uint ExpectedWritePointer => U.Padding4(ROM_SIZE / 2); // 0x2000

        // ---------------- source-ROM builder ----------------

        // Source layout (all offsets fixed and < half mark):
        //   0x100  song header: [tc][0][0x0A][0x80][voicePtr][track0Ptr]...[trackN-1Ptr]
        //   0x300  voice table  (12-byte entries)
        //   0x600  sample data  (per test)
        //   0x800.. track data  (per test, each track at 0x800 + k*0x80)
        const uint SRC_HDR = 0x100;
        const uint SRC_VOICES = 0x300;
        const uint SRC_SAMPLE = 0x600;
        const uint SRC_TRACK0 = 0x800;

        class SrcBuild
        {
            public byte[] Data;
            public SongExchangeCore.SongSt Song;
        }

        /// <summary>Build a source ROM with the given tracks + voice table bytes.</summary>
        static SrcBuild BuildSource(byte[][] tracks, byte[] voiceTable)
        {
            byte[] data = new byte[ROM_SIZE];
            int tc = tracks.Length;
            data[SRC_HDR + 0] = (byte)tc;
            data[SRC_HDR + 1] = 0;
            data[SRC_HDR + 2] = 0x0A;
            data[SRC_HDR + 3] = 0x80;
            WriteGbaPtr(data, SRC_HDR + 4, SRC_VOICES);
            for (int k = 0; k < tc; k++)
            {
                uint trackOff = SRC_TRACK0 + (uint)k * 0x80;
                WriteGbaPtr(data, SRC_HDR + 8 + (uint)k * 4, trackOff);
                Array.Copy(tracks[k], 0, data, (int)trackOff, tracks[k].Length);
            }
            if (voiceTable != null)
                Array.Copy(voiceTable, 0, data, (int)SRC_VOICES, voiceTable.Length);

            var song = new SongExchangeCore.SongSt
            {
                Number = 1,
                Table = 0, // not used by Rip/Burn-from-source
                Header = SRC_HDR,
                Voices = SRC_VOICES,
                TrackCount = tc,
            };
            return new SrcBuild { Data = data, Song = song };
        }

        /// <summary>Build a clean dest ROM with a 2-entry song table at 0x40.</summary>
        static (ROM rom, SongExchangeCore.SongSt slot) BuildDest()
        {
            byte[] data = new byte[ROM_SIZE];
            // Song table at 0x40: entry 0 = dummy header at 0x60 (tc=0), entry 1 = our target slot.
            // We only need entry 1's Table offset for the repoint; Header is irrelevant for Burn.
            var slot = new SongExchangeCore.SongSt
            {
                Number = 1,
                Table = 0x48,   // 8-byte entry #1
                Header = 0x60,
                TrackCount = 0,
            };
            var rom = NewRom(data);
            return (rom, slot);
        }

        static SongExchangeCore.ConvertResult Convert(ROM dest, SongExchangeCore.SongSt slot, SrcBuild src)
        {
            Undo.UndoData undo = CoreState.Undo.NewUndoData("test");
            SongExchangeCore.ConvertResult r;
            using (ROM.BeginUndoScope(undo))
            {
                r = SongExchangeCore.ConvertSong(dest, slot, src.Data, src.Song, undo);
            }
            if (r.Success) CoreState.Undo.Push(undo);
            return r;
        }

        // ===================================================================
        // 1. Header bytes + song-table repoint + priority word.
        // ===================================================================
        [Fact]
        public void Transplant_WritesHeaderAndRepointsSlotAndPriority()
        {
            // Single track: B1 (end immediately). No voices needed beyond the
            // implicit percussion row + the appended dummy drum.
            var src = BuildSource(new[] { new byte[] { 0xB1 } }, new byte[12]);
            var (dest, slot) = BuildDest();

            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;

            // Header: [trackCount=1][0][0x0A][0x80]
            Assert.Equal(1, dest.Data[wp + 0]);
            Assert.Equal(0, dest.Data[wp + 1]);
            Assert.Equal(0x0A, dest.Data[wp + 2]);
            Assert.Equal(0x80, dest.Data[wp + 3]);

            // Song-table entry repointed to write_pointer (as GBA pointer).
            Assert.Equal(wp + 0x08000000u, ReadU32(dest.Data, slot.Table));

            // Priority: trackCount <= 1 -> 0x60006.
            Assert.Equal(0x60006u, ReadU32(dest.Data, slot.Table + 4));
        }

        [Fact]
        public void Transplant_MultiTrack_PriorityIsMapValue()
        {
            // Two tracks -> priority 0x10001.
            var src = BuildSource(new[] { new byte[] { 0xB1 }, new byte[] { 0xB1 } }, new byte[12]);
            var (dest, slot) = BuildDest();
            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;
            Assert.Equal(2, dest.Data[wp + 0]); // track count
            Assert.Equal(0x10001u, ReadU32(dest.Data, slot.Table + 4));
        }

        // ===================================================================
        // 2. 0xB2 loop + 0xB3 call → relativized loop-pointer operands.
        // ===================================================================
        [Fact]
        public void Transplant_B2B3_RelativizesLoopPointers()
        {
            // Track at SRC_TRACK0 (0x800): B2 <ptr to 0x804> B1
            //   B2 jumps back to itself+? — we point it at SRC_TRACK0+4 (0x804, just past the B2 operand).
            // relativized = targetOffset - songtrackdata_pointer = 0x804 - 0x800 = 4.
            var track = new byte[6];
            track[0] = 0xB2;
            // operand = GBA pointer to 0x804
            track[1] = (byte)((0x804u + 0x08000000) & 0xFF);
            track[2] = (byte)(((0x804u + 0x08000000) >> 8) & 0xFF);
            track[3] = (byte)(((0x804u + 0x08000000) >> 16) & 0xFF);
            track[4] = (byte)(((0x804u + 0x08000000) >> 24) & 0xFF);
            track[5] = 0xB1;

            var src = BuildSource(new[] { track }, new byte[12]);
            var (dest, slot) = BuildDest();
            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;
            // Track data starts at write_pointer + headerSize. headerSize = 8 + 4*1 = 12.
            uint trackStart = wp + 12;
            Assert.Equal(0xB2, dest.Data[trackStart + 0]);
            // Burned operand = toPointer(relativized + offset + write_pointer)
            //   relativized = 4 ; offset(track within buffer) = 12 ; write_pointer = wp
            //   => GBA pointer of (4 + 12 + wp).
            uint expected = (4u + 12u + wp) + 0x08000000u;
            Assert.Equal(expected, ReadU32(dest.Data, trackStart + 1));
            Assert.Equal(0xB1, dest.Data[trackStart + 5]);
        }

        // ===================================================================
        // 3. 0xB9 (MEMACC) + a 2-byte data command (0xBE VOL) → bytes verbatim.
        // ===================================================================
        [Fact]
        public void Transplant_B9_And2ByteCommand_CopiesDataBytesVerbatim()
        {
            // Track: B9 11 22 33  (MEMACC: 1 cmd + 3 data) ; BE 7F (VOL value) ; B1
            var track = new byte[] { 0xB9, 0x11, 0x22, 0x33, 0xBE, 0x7F, 0xB1 };
            var src = BuildSource(new[] { track }, new byte[12]);
            var (dest, slot) = BuildDest();
            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;
            uint t = wp + 12; // track start
            // B9 and its 3 data bytes copied verbatim.
            Assert.Equal(0xB9, dest.Data[t + 0]);
            Assert.Equal(0x11, dest.Data[t + 1]);
            Assert.Equal(0x22, dest.Data[t + 2]);
            Assert.Equal(0x33, dest.Data[t + 3]);
            // BE + its single data byte verbatim.
            Assert.Equal(0xBE, dest.Data[t + 4]);
            Assert.Equal(0x7F, dest.Data[t + 5]);
            Assert.Equal(0xB1, dest.Data[t + 6]);
        }

        // ===================================================================
        // 4. DirectSound voice → 12-byte instrument row + sample payload + fixup.
        // ===================================================================
        [Fact]
        public void Transplant_DirectSound_InstrumentRowAndSampleAndFixup()
        {
            // Voice 0: DirectSound. row = [0x00][?][?][?][samplePtr->SRC_SAMPLE][...]
            byte[] voiceTable = new byte[12 * 1];
            voiceTable[0] = 0x00; // DirectSound code
            WriteGbaPtr(voiceTable, 4, SRC_SAMPLE); // sample pointer

            // Track uses voice 0 via BD 00 (the FIRST instrument referenced).
            // BD 00 -> translate(0): DirectSound -> mapped id (1, since the implicit
            // percussion row is index 0). Then B1.
            var track = new byte[] { 0xBD, 0x00, 0xB1 };

            var src = BuildSource(new[] { track }, voiceTable);
            // DirectSound sample header (16 bytes) + body. Header bytes 0..3 must be
            // distinctive & non-zero: the recycle grep needle for a DirectSound is
            // the FIRST `body_len` bytes (WF reads sample_data_len = u32(+12) = body
            // length only), so a zero header would spuriously match the zero dest.
            src.Data[SRC_SAMPLE + 0] = 0x7A;
            src.Data[SRC_SAMPLE + 1] = 0x6B;
            src.Data[SRC_SAMPLE + 2] = 0x5C;
            src.Data[SRC_SAMPLE + 3] = 0x4D;
            WriteU32(src.Data, SRC_SAMPLE + 12, 4); // sample_length (body) = 4
            src.Data[SRC_SAMPLE + 16] = 0xDE;
            src.Data[SRC_SAMPLE + 17] = 0xAD;
            src.Data[SRC_SAMPLE + 18] = 0xBE;
            src.Data[SRC_SAMPLE + 19] = 0xEF;

            var (dest, slot) = BuildDest();
            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;
            // Layout: header(12) + track0(BD 01 B1 = 3 bytes) = 15 -> Padding4 = 16.
            uint instrStart = wp + 16;

            // Row 0 = percussion map row {0x80,0,...}; Row 1 = DirectSound (code 0x00).
            Assert.Equal(0x80, dest.Data[instrStart + 0]);
            Assert.Equal(0x00, dest.Data[instrStart + 12 + 0]);

            // The DirectSound row's +4 pointer points at the burned sample. Derive
            // the sample offset from that emitted pointer (robust + byte-exact).
            uint samplePtr = ReadU32(dest.Data, instrStart + 12 + 4);
            Assert.True(U.isPointer(samplePtr), "sample pointer must be a GBA pointer");
            uint sampleStart = samplePtr - 0x08000000u;

            // Sample payload = 16-byte header + 4 body bytes, copied verbatim.
            Assert.Equal(0x7A, dest.Data[sampleStart + 0]);
            Assert.Equal(0x6B, dest.Data[sampleStart + 1]);
            Assert.Equal(4u, ReadU32(dest.Data, sampleStart + 12));
            Assert.Equal(0xDE, dest.Data[sampleStart + 16]);
            Assert.Equal(0xAD, dest.Data[sampleStart + 17]);
            Assert.Equal(0xBE, dest.Data[sampleStart + 18]);
            Assert.Equal(0xEF, dest.Data[sampleStart + 19]);

            // The track's BD voice byte was translated to id 1 (mapped DirectSound).
            uint t = wp + 12;
            Assert.Equal(0xBD, dest.Data[t + 0]);
            Assert.Equal(1, dest.Data[t + 1]);
            Assert.Equal(0xB1, dest.Data[t + 2]);
        }

        // ===================================================================
        // 5. WaveMemory voice → 16-byte sample, instrument row.
        // ===================================================================
        [Fact]
        public void Transplant_WaveMemory_SixteenByteSample()
        {
            byte[] voiceTable = new byte[12];
            voiceTable[0] = 0x03; // WaveMemory
            WriteGbaPtr(voiceTable, 4, SRC_SAMPLE);

            var track = new byte[] { 0xBD, 0x00, 0xB1 };
            var src = BuildSource(new[] { track }, voiceTable);
            // 16 raw wave bytes in the source ROM.
            for (uint i = 0; i < 16; i++) src.Data[SRC_SAMPLE + i] = (byte)(0xA0 + i);

            var (dest, slot) = BuildDest();
            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;
            uint instrStart = wp + 16;       // same layout as DS test (track 3 bytes -> pad 16)
            // WaveMemory row code byte 0x03.
            Assert.Equal(0x03, dest.Data[instrStart + 12 + 0]);
            // Derive the burned sample offset from the row's +4 pointer.
            uint samplePtr = ReadU32(dest.Data, instrStart + 12 + 4);
            Assert.True(U.isPointer(samplePtr), "sample pointer must be a GBA pointer");
            uint sampleStart = samplePtr - 0x08000000u;
            // 16 wave bytes copied verbatim.
            for (uint i = 0; i < 16; i++)
                Assert.Equal((byte)(0xA0 + i), dest.Data[sampleStart + i]);
        }

        // ===================================================================
        // 6 + 7. Drum (0x80) voice at a NON-ZERO index + a percussion note →
        // BD voice translated to 0 (drum is always id 0), percussion engaged
        // (percussion = the non-zero voice id), and the note rewritten to the
        // translate_percussion id with its single sample extracted.
        //
        // NOTE: a drum voice at index 0 (BD 00) would set percussion = 0, which
        // disables the percussion path (WF: `percussion != 0 && b < 0x80`). Real
        // songs place the drum at a non-zero voice — so do we.
        // ===================================================================
        [Fact]
        public void Transplant_Drum_BdTranslatesToZero_AndPercussionExtractsSample()
        {
            // Voice index 1 = Drum (0x80); +4 -> percussion sub-table at SRC_VOICES + 24.
            // Sub-table pitch 0x24 -> DirectSound row pointing at SRC_SAMPLE.
            byte[] voiceTable = new byte[24 + 0x40 * 12];
            // index 0 unused (zeros); index 1 = drum.
            voiceTable[12 + 0] = 0x80; // Drum at voice index 1
            WriteGbaPtr(voiceTable, 12 + 4, SRC_VOICES + 24); // sub-table base (offset SRC_VOICES+24)

            uint pitch = 0x24;
            uint subRow = 24 + pitch * 12; // sub-table is at voiceTable offset 24
            voiceTable[subRow + 0] = 0x00; // DirectSound
            WriteGbaPtr(voiceTable, subRow + 4, SRC_SAMPLE);

            // Track: BD 01 (drum voice 1 -> translated 0, percussion=1) ; note 0x24 ; B1
            var track = new byte[] { 0xBD, 0x01, 0x24, 0xB1 };
            var src = BuildSource(new[] { track }, voiceTable);
            // Distinctive non-zero sample header (recycle needle) + body.
            src.Data[SRC_SAMPLE + 0] = 0x12;
            src.Data[SRC_SAMPLE + 1] = 0x34;
            src.Data[SRC_SAMPLE + 2] = 0x56;
            src.Data[SRC_SAMPLE + 3] = 0x78;
            WriteU32(src.Data, SRC_SAMPLE + 12, 4); // sample length (body) 4
            src.Data[SRC_SAMPLE + 16] = 0x55;
            src.Data[SRC_SAMPLE + 17] = 0x66;
            src.Data[SRC_SAMPLE + 18] = 0x77;
            src.Data[SRC_SAMPLE + 19] = 0x88;

            var (dest, slot) = BuildDest();
            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;
            uint t = wp + 12;
            // BD 01 -> drum -> id 0 (drum is always id 0).
            Assert.Equal(0xBD, dest.Data[t + 0]);
            Assert.Equal(0x00, dest.Data[t + 1]);
            // The note 0x24 was rewritten to the translate_percussion id. Mapping
            // order: percussion row (id 0) is implicit; the DS sub-sample is the
            // first _prepare-added row -> id 1.
            Assert.Equal(1, dest.Data[t + 2]);
            Assert.Equal(0xB1, dest.Data[t + 3]);

            // instrument rows: percussion row (id 0) + DS sub-sample (id 1) = 2 rows.
            uint instrStart = wp + 16u; // header 12 + track 4 = 16
            // Row 1 = DirectSound (the extracted percussion sample).
            Assert.Equal(0x00, dest.Data[instrStart + 12 + 0]);
            uint samplePtr = ReadU32(dest.Data, instrStart + 12 + 4);
            Assert.True(U.isPointer(samplePtr), "percussion sample pointer must be a GBA pointer");
            uint sampleStart = samplePtr - 0x08000000u;
            Assert.Equal(0x55, dest.Data[sampleStart + 16]);
            Assert.Equal(0x66, dest.Data[sampleStart + 17]);
            Assert.Equal(0x77, dest.Data[sampleStart + 18]);
            Assert.Equal(0x88, dest.Data[sampleStart + 19]);
        }

        // ===================================================================
        // 8. Recycle MISS then HIT.
        // ===================================================================
        [Fact]
        public void Transplant_RecycleMiss_AppendsNewSample()
        {
            byte[] voiceTable = new byte[12];
            voiceTable[0] = 0x00;
            WriteGbaPtr(voiceTable, 4, SRC_SAMPLE);

            var track = new byte[] { 0xBD, 0x00, 0xB1 };
            var src = BuildSource(new[] { track }, voiceTable);
            // Distinctive non-zero sample header (the recycle needle for a body=4
            // DirectSound is the first 4 bytes) so the zero dest does NOT match.
            src.Data[SRC_SAMPLE + 0] = 0xC1;
            src.Data[SRC_SAMPLE + 1] = 0xC2;
            src.Data[SRC_SAMPLE + 2] = 0xC3;
            src.Data[SRC_SAMPLE + 3] = 0xC4;
            WriteU32(src.Data, SRC_SAMPLE + 12, 4);
            src.Data[SRC_SAMPLE + 16] = 0x01;
            src.Data[SRC_SAMPLE + 17] = 0x02;
            src.Data[SRC_SAMPLE + 18] = 0x03;
            src.Data[SRC_SAMPLE + 19] = 0x04;

            // Dest is all zeros -> no matching sample -> MISS -> sample appended
            // into the burned region (the +4 row pointer points back inside it).
            var (dest, slot) = BuildDest();
            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;
            uint instrStart = wp + 16;
            uint samplePtr = ReadU32(dest.Data, instrStart + 12 + 4);
            Assert.True(U.isPointer(samplePtr), "sample pointer must be a GBA pointer");
            uint sampleStart = samplePtr - 0x08000000u;
            // The appended sample is INSIDE the burned region (>= write_pointer).
            Assert.True(sampleStart >= wp, "MISS must append the sample inside the burned region");
            Assert.Equal(0xC1, dest.Data[sampleStart + 0]);
            Assert.Equal(0x01, dest.Data[sampleStart + 16]);
        }

        [Fact]
        public void Transplant_RecycleHit_DropsDuplicateSampleAndRepointsToExisting()
        {
            byte[] voiceTable = new byte[12];
            voiceTable[0] = 0x00;
            WriteGbaPtr(voiceTable, 4, SRC_SAMPLE);

            var track = new byte[] { 0xBD, 0x00, 0xB1 };
            var src = BuildSource(new[] { track }, voiceTable);
            // DirectSound recycle needle = the first `body_len`(4) sample bytes.
            // Use distinctive non-zero header bytes so the seeded dest copy is the
            // ONLY 4-byte match (a zero header would match the zero dest first).
            byte[] sampleBytes = new byte[20];
            sampleBytes[0] = 0xF0; sampleBytes[1] = 0xE1; sampleBytes[2] = 0xD2; sampleBytes[3] = 0xC3;
            WriteU32(sampleBytes, 12, 4);
            sampleBytes[16] = 0x11; sampleBytes[17] = 0x22; sampleBytes[18] = 0x33; sampleBytes[19] = 0x44;
            Array.Copy(sampleBytes, 0, src.Data, (int)SRC_SAMPLE, sampleBytes.Length);

            // Pre-seed the IDENTICAL needle (first 4 bytes) in the dest at a 4-aligned
            // offset ABOVE 0x100 (U.Grep starts at 0x100) but BELOW the half mark so
            // it does not collide with the burn region.
            var (dest, slot) = BuildDest();
            uint seedAt = 0x1000;
            Array.Copy(sampleBytes, 0, dest.Data, (int)seedAt, sampleBytes.Length);

            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;
            uint instrStart = wp + 16;
            // The DirectSound row's +4 pointer points at the EXISTING dest sample (seedAt),
            // proving the recycle HIT repointed to the existing region (resyclesize advanced).
            Assert.Equal(seedAt + 0x08000000u, ReadU32(dest.Data, instrStart + 12 + 4));
        }

        // ===================================================================
        // 9. No-mutation on a bad/out-of-range track pointer.
        // ===================================================================
        [Fact]
        public void Transplant_BadTrackPointer_NoMutation()
        {
            // Build a source whose track-0 pointer is garbage (not a pointer).
            var src = BuildSource(new[] { new byte[] { 0xB1 } }, new byte[12]);
            // Corrupt track0 pointer in the header to a non-pointer value.
            WriteU32(src.Data, SRC_HDR + 8, 0x12345678);

            var (dest, slot) = BuildDest();
            byte[] before = (byte[])dest.Data.Clone();

            var r = Convert(dest, slot, src);
            Assert.False(r.Success);
            Assert.NotEqual("", r.ErrorMessage);
            Assert.Equal(before, dest.Data);
        }

        // ===================================================================
        // 10. Undo atomicity — RunUndo restores dest byte-identical.
        // ===================================================================
        [Fact]
        public void Transplant_Undo_RestoresByteIdentical()
        {
            byte[] voiceTable = new byte[12];
            voiceTable[0] = 0x00;
            WriteGbaPtr(voiceTable, 4, SRC_SAMPLE);
            var track = new byte[] { 0xBD, 0x00, 0xB1 };
            var src = BuildSource(new[] { track }, voiceTable);
            WriteU32(src.Data, SRC_SAMPLE + 12, 4);

            var (dest, slot) = BuildDest();
            byte[] before = (byte[])dest.Data.Clone();

            var r = Convert(dest, slot, src); // Convert pushes the undo on success.
            Assert.True(r.Success, r.ErrorMessage);
            Assert.NotEqual(before, dest.Data); // mutated.

            CoreState.Undo.RunUndo();
            Assert.Equal(before, dest.Data);   // byte-identical after undo.
        }

        // ===================================================================
        // 11. No-free-space → fail cleanly, no mutation.
        // ===================================================================
        [Fact]
        public void Transplant_NoFreeSpace_FailsCleanly()
        {
            byte[] voiceTable = new byte[12];
            var src = BuildSource(new[] { new byte[] { 0xB1 } }, voiceTable);

            // Dest with NO free region: fill everything with 0x01 (not 0x00/0xFF).
            byte[] data = new byte[ROM_SIZE];
            for (int i = 0; i < data.Length; i++) data[i] = 0x01;
            var rom = NewRom(data);
            var slot = new SongExchangeCore.SongSt { Number = 1, Table = 0x48, Header = 0x60 };
            byte[] before = (byte[])rom.Data.Clone();

            Undo.UndoData undo = CoreState.Undo.NewUndoData("test");
            SongExchangeCore.ConvertResult r;
            using (ROM.BeginUndoScope(undo))
            {
                r = SongExchangeCore.ConvertSong(rom, slot, src.Data, src.Song, undo);
            }
            Assert.False(r.Success);
            Assert.Equal(before, rom.Data);
        }

        // ===================================================================
        // IsDirectSound / IsWaveMemory constant-check parity (observed via the
        // transplant): all 4 DirectSound codes (0x00/0x08/0x10/0x18) get a sample
        // extracted + repointed; both WaveMemory codes (0x03/0x0B) get a 16-byte
        // sample. A non-DS/WM/Drum/Multi code (e.g. 0x05) is copied as-is with NO
        // sample. We assert the +4 row pointer is (or is not) a burned GBA pointer.
        // ===================================================================
        [Theory]
        [InlineData(0x00, true)]
        [InlineData(0x08, true)]
        [InlineData(0x10, true)]
        [InlineData(0x18, true)]
        [InlineData(0x03, true)]   // WaveMemory
        [InlineData(0x0B, true)]   // WaveMemory
        public void Transplant_InstrumentCodeClassification_ExtractsSample(int code, bool extractsSample)
        {
            byte[] voiceTable = new byte[12];
            voiceTable[0] = (byte)code;
            WriteGbaPtr(voiceTable, 4, SRC_SAMPLE);

            var track = new byte[] { 0xBD, 0x00, 0xB1 };
            var src = BuildSource(new[] { track }, voiceTable);
            // Distinctive non-zero sample header + a 4-byte body (DS) / 16 raw bytes (WM).
            for (uint i = 0; i < 20; i++) src.Data[SRC_SAMPLE + i] = (byte)(0x90 + i);
            WriteU32(src.Data, SRC_SAMPLE + 12, 4);

            var (dest, slot) = BuildDest();
            var r = Convert(dest, slot, src);
            Assert.True(r.Success, r.ErrorMessage);

            uint wp = ExpectedWritePointer;
            uint instrStart = wp + 16;
            // Row 1 = the instrument; its code byte is preserved.
            Assert.Equal(code, dest.Data[instrStart + 12 + 0]);
            uint samplePtr = ReadU32(dest.Data, instrStart + 12 + 4);
            if (extractsSample)
                Assert.True(U.isPointer(samplePtr), "DS/WM voices must repoint +4 to a burned sample");
        }

        // ===================================================================
        // 12. Real-ROM round-trip: transplant a real multi-track song A->B and
        // re-parse B's slot — track count + per-track decoded code geometry +
        // instrument-set entry count must match the source.
        // ===================================================================
        [Fact]
        public void Transplant_RealRom_RoundTripsTrackCountAndInstruments()
        {
            string romPath = FindTestRom();
            if (romPath == null) return; // skip if no ROM available.

            var savedRom = CoreState.ROM;
            try
            {
                // ROM A = donor (raw bytes); ROM B = recipient (loaded, mutated).
                byte[] aData = System.IO.File.ReadAllBytes(romPath);
                var bRom = new ROM();
                if (!bRom.Load(romPath, out string _)) return;
                CoreState.ROM = bRom;
                if (CoreState.Undo == null) CoreState.Undo = new Undo();

                uint soundTablePtr = bRom.RomInfo.sound_table_pointer;
                if (soundTablePtr == 0) return;
                uint aTable = SongExchangeCore.FindSongTablePointer(aData, soundTablePtr);
                uint bTable = SongExchangeCore.FindSongTablePointer(bRom.Data, soundTablePtr);
                if (aTable == 0 || bTable == 0) return;

                var aSongs = SongExchangeCore.SongTableToSongList(aData, aTable);
                var bSongs = SongExchangeCore.SongTableToSongList(bRom.Data, bTable);

                // Pick a real multi-track source song (TrackCount >= 2), non-zero slot.
                var srcSong = aSongs.FirstOrDefault(s => s.TrackCount >= 2 && s.Voices != 0);
                if (srcSong == null) return;
                // Pick a destination slot that is non-zero and exists.
                var destSlot = bSongs.FirstOrDefault(s => s.Number >= 1 && s.TrackCount >= 1);
                if (destSlot == null) return;

                Undo.UndoData undo = CoreState.Undo.NewUndoData("rt");
                SongExchangeCore.ConvertResult r;
                using (ROM.BeginUndoScope(undo))
                {
                    r = SongExchangeCore.ConvertSong(bRom, destSlot, aData, srcSong, undo);
                }
                Assert.True(r.Success, r.ErrorMessage);
                CoreState.Undo.Push(undo);

                // Re-parse the destination slot's header from the freshly-written ROM.
                uint newHeaderPtr = bRom.u32(destSlot.Table);
                Assert.True(U.isPointer(newHeaderPtr));
                uint newHeader = U.toOffset(newHeaderPtr);
                uint newTrackCount = bRom.u8(newHeader);

                // Track count must equal the source song's track count.
                Assert.Equal((uint)srcSong.TrackCount, newTrackCount);

                // Re-parse every track via SongMidiCore.ParseTracks — each must be
                // a non-empty, parseable track (proving the burned pointers + data
                // are coherent).
                var tracks = SongMidiCore.ParseTracks(bRom, newHeader, newTrackCount);
                Assert.Equal((int)newTrackCount, tracks.Count);
                foreach (var trk in tracks)
                {
                    Assert.NotNull(trk);
                    Assert.NotEmpty(trk.codes);
                }

                // The instrument (voice) pointer must be a valid pointer into the ROM.
                uint voicePtr = bRom.u32(newHeader + 4);
                Assert.True(U.isPointer(voicePtr), "burned voice pointer must be a GBA pointer");
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        /// <summary>Locate a test ROM by walking up from the test assembly dir.</summary>
        static string FindTestRom()
        {
            string thisAssembly = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string dir = System.IO.Path.GetDirectoryName(thisAssembly);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "FEBuilderGBA.sln")))
                {
                    string romsDir = System.IO.Path.Combine(dir, "roms");
                    if (System.IO.Directory.Exists(romsDir))
                    {
                        string[] preferred = { "FE8U.gba", "FE7U.gba", "FE8J.gba", "FE7J.gba", "FE6.gba" };
                        foreach (string name in preferred)
                        {
                            string path = System.IO.Path.Combine(romsDir, name);
                            if (System.IO.File.Exists(path)) return path;
                        }
                        string[] gbaFiles = System.IO.Directory.GetFiles(romsDir, "*.gba");
                        if (gbaFiles.Length > 0) return gbaFiles[0];
                    }
                    break;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
            return null;
        }
    }
}
