using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Unit tests for the Bulk Track Change voice-reassignment seam (#1015).
    /// Builds a SYNTHETIC GBA song (song header + track pointers + a small
    /// 0xBD-voice track stream that SongMidiCore.ParseTracks actually parses)
    /// so the tests run hermetic with no real ROM.
    /// </summary>
    [Collection("SharedState")]
    public class SongVoiceChangeCoreTests
    {
        // Layout for the synthetic ROM. isSafetyOffset requires addr >= 0x200,
        // so place the song header at 0x300.
        const uint SongAddr = 0x300;
        const uint Track0Data = 0x400;
        const uint Track1Data = 0x440;

        static void WriteGbaPtr(byte[] data, uint at, uint romOffset)
        {
            uint ptr = romOffset + 0x08000000;
            data[at + 0] = (byte)(ptr & 0xFF);
            data[at + 1] = (byte)((ptr >> 8) & 0xFF);
            data[at + 2] = (byte)((ptr >> 16) & 0xFF);
            data[at + 3] = (byte)((ptr >> 24) & 0xFF);
        }

        /// <summary>
        /// Build a 2-track song:
        ///   Track0 @0x400: BD 05, BD 09, BD 05, B1   (voices 5, 9, 5 -> dedup {5,9})
        ///   Track1 @0x440: BD 05, BD 11, B1          (voices 5, 17  -> dedup adds {17})
        /// Distinct voices across both tracks: {5, 9, 17}.
        /// </summary>
        static ROM BuildSong(out List<uint> voiceByteAddrs)
        {
            byte[] data = new byte[0x1000];

            // Song header: trackCount=2.
            data[SongAddr + 0] = 2;
            data[SongAddr + 1] = 0;
            data[SongAddr + 2] = 0;
            data[SongAddr + 3] = 0;
            // Instrument pointer (+4) -> some GBA pointer (unused by the seam).
            WriteGbaPtr(data, SongAddr + 4, 0x800);
            // Track pointers (+8, +12).
            WriteGbaPtr(data, SongAddr + 8, Track0Data);
            WriteGbaPtr(data, SongAddr + 12, Track1Data);

            voiceByteAddrs = new List<uint>();

            // Track0: BD 05, BD 09, BD 05, B1
            uint a = Track0Data;
            data[a++] = 0xBD; voiceByteAddrs.Add(a); data[a++] = 5;
            data[a++] = 0xBD; voiceByteAddrs.Add(a); data[a++] = 9;
            data[a++] = 0xBD; voiceByteAddrs.Add(a); data[a++] = 5;
            data[a++] = 0xB1;

            // Track1: BD 05, BD 11(=17), B1
            a = Track1Data;
            data[a++] = 0xBD; voiceByteAddrs.Add(a); data[a++] = 5;
            data[a++] = 0xBD; voiceByteAddrs.Add(a); data[a++] = 17;
            data[a++] = 0xB1;

            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        // ------------------------------------------------------------------
        // GetDistinctVoices
        // ------------------------------------------------------------------

        [Fact]
        public void GetDistinctVoices_DedupesAcrossTracks()
        {
            var rom = BuildSong(out _);
            var voices = SongVoiceChangeCore.GetDistinctVoices(rom, SongAddr);

            // Distinct {5, 9, 17}; duplicates (the two extra 5s) collapsed.
            var froms = voices.Select(v => v.From).OrderBy(x => x).ToList();
            Assert.Equal(new List<int> { 5, 9, 17 }, froms);

            // Each row seeded with To == From (no-op until edited).
            Assert.All(voices, v => Assert.Equal(v.From, v.To));
        }

        [Fact]
        public void GetDistinctVoices_NullRom_ReturnsEmpty()
        {
            Assert.Empty(SongVoiceChangeCore.GetDistinctVoices(null, SongAddr));
        }

        [Fact]
        public void GetDistinctVoices_UnsafeSongAddr_ReturnsEmpty()
        {
            var rom = BuildSong(out _);
            // 0x10 is below the 0x200 safety floor.
            Assert.Empty(SongVoiceChangeCore.GetDistinctVoices(rom, 0x10));
        }

        // ------------------------------------------------------------------
        // ApplyVoiceChanges
        // ------------------------------------------------------------------

        [Fact]
        public void ApplyVoiceChanges_RewritesOnlyMatchingVoiceBytes()
        {
            var rom = BuildSong(out var voiceAddrs);
            byte[] before = (byte[])rom.Data.Clone();

            // 5 -> 42. Three codes use voice 5 (two in track0, one in track1).
            var map = new Dictionary<int, int> { { 5, 42 } };
            string err = SongVoiceChangeCore.ApplyVoiceChanges(rom, SongAddr, map);
            Assert.Equal("", err);

            // Every voice-5 byte became 42; the voice-9 / voice-17 bytes stay.
            for (int i = 0; i < voiceAddrs.Count; i++)
            {
                uint vAddr = voiceAddrs[i];
                byte original = before[vAddr];
                if (original == 5)
                    Assert.Equal(42, rom.Data[vAddr]);
                else
                    Assert.Equal(original, rom.Data[vAddr]);
            }

            // Only the three voice bytes changed — every OTHER byte is untouched.
            for (uint addr = 0; addr < before.Length; addr++)
            {
                if (voiceAddrs.Contains(addr) && before[addr] == 5) continue;
                Assert.Equal(before[addr], rom.Data[addr]);
            }
        }

        [Fact]
        public void ApplyVoiceChanges_Identity_IsNoOp()
        {
            var rom = BuildSong(out _);
            byte[] before = (byte[])rom.Data.Clone();

            // from == to for every row -> zero writes.
            var map = new Dictionary<int, int> { { 5, 5 }, { 9, 9 }, { 17, 17 } };
            string err = SongVoiceChangeCore.ApplyVoiceChanges(rom, SongAddr, map);
            Assert.Equal("", err);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ApplyVoiceChanges_EmptyMap_IsNoOp()
        {
            var rom = BuildSong(out _);
            byte[] before = (byte[])rom.Data.Clone();

            string err = SongVoiceChangeCore.ApplyVoiceChanges(rom, SongAddr, new Dictionary<int, int>());
            Assert.Equal("", err);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ApplyVoiceChanges_OutOfRangeTarget_NoMutation()
        {
            var rom = BuildSong(out _);
            byte[] before = (byte[])rom.Data.Clone();

            // 5 -> 200 is out of the 0..127 voice range. VALIDATE-ALL-BEFORE-
            // MUTATE means ZERO bytes change.
            var map = new Dictionary<int, int> { { 5, 200 } };
            string err = SongVoiceChangeCore.ApplyVoiceChanges(rom, SongAddr, map);
            Assert.NotEqual("", err);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ApplyVoiceChanges_RoundTrips_WhenTargetVoiceDidNotPreexist()
        {
            // Map 9 -> 99 (99 does NOT pre-exist), then 99 -> 9 restores the
            // original bytes exactly. (A target that already existed would
            // wrongly also move the pre-existing copies — see the chained test.)
            var rom = BuildSong(out _);
            byte[] before = (byte[])rom.Data.Clone();

            Assert.Equal("", SongVoiceChangeCore.ApplyVoiceChanges(
                rom, SongAddr, new Dictionary<int, int> { { 9, 99 } }));
            Assert.NotEqual(before, rom.Data); // mutated

            Assert.Equal("", SongVoiceChangeCore.ApplyVoiceChanges(
                rom, SongAddr, new Dictionary<int, int> { { 99, 9 } }));
            Assert.Equal(before, rom.Data); // restored byte-identical
        }

        [Fact]
        public void ApplyVoiceChanges_ChainedMappings_SinglePassNoCascade()
        {
            // Given {9 -> 5} and {5 -> 42} applied in ONE pass against the
            // ORIGINAL codes: the original 9 becomes 5, the original 5s become
            // 42 — the freshly-written 5 is NOT re-mapped to 42 (no cascade).
            var rom = BuildSong(out var voiceAddrs);
            byte[] before = (byte[])rom.Data.Clone();

            var map = new Dictionary<int, int> { { 9, 5 }, { 5, 42 } };
            Assert.Equal("", SongVoiceChangeCore.ApplyVoiceChanges(rom, SongAddr, map));

            foreach (uint vAddr in voiceAddrs)
            {
                byte original = before[vAddr];
                if (original == 5)
                    Assert.Equal(42, rom.Data[vAddr]);      // 5 -> 42
                else if (original == 9)
                    Assert.Equal(5, rom.Data[vAddr]);       // 9 -> 5 (NOT cascaded to 42)
                else
                    Assert.Equal(original, rom.Data[vAddr]); // 17 untouched
            }
        }

        [Fact]
        public void ApplyVoiceChanges_TargetAlreadyExists_OnePassSemantics()
        {
            // 5 -> 9 where 9 ALREADY exists in the song. One-pass semantics:
            // the original 5s become 9, and the pre-existing 9 stays 9 (it is
            // NOT in the map as a `from`). So after apply there are no 5s left.
            var rom = BuildSong(out var voiceAddrs);
            byte[] before = (byte[])rom.Data.Clone();

            Assert.Equal("", SongVoiceChangeCore.ApplyVoiceChanges(
                rom, SongAddr, new Dictionary<int, int> { { 5, 9 } }));

            foreach (uint vAddr in voiceAddrs)
            {
                byte original = before[vAddr];
                if (original == 5)
                    Assert.Equal(9, rom.Data[vAddr]);
                else
                    Assert.Equal(original, rom.Data[vAddr]);
            }
            // No voice byte remains 5.
            Assert.DoesNotContain(voiceAddrs, vAddr => rom.Data[vAddr] == 5);
        }

        [Fact]
        public void ApplyVoiceChanges_NoMatchingVoice_IsNoOp()
        {
            var rom = BuildSong(out _);
            byte[] before = (byte[])rom.Data.Clone();

            // Voice 100 is not used by the song -> nothing to rewrite.
            var map = new Dictionary<int, int> { { 100, 42 } };
            Assert.Equal("", SongVoiceChangeCore.ApplyVoiceChanges(rom, SongAddr, map));
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ApplyVoiceChanges_NullRom_NoThrowReturnsError()
        {
            string err = SongVoiceChangeCore.ApplyVoiceChanges(
                null, SongAddr, new Dictionary<int, int> { { 5, 9 } });
            Assert.NotEqual("", err);
        }

        [Fact]
        public void ApplyVoiceChanges_UnsafeSongAddr_NoMutation()
        {
            var rom = BuildSong(out _);
            byte[] before = (byte[])rom.Data.Clone();

            string err = SongVoiceChangeCore.ApplyVoiceChanges(
                rom, 0x10, new Dictionary<int, int> { { 5, 9 } });
            Assert.NotEqual("", err);
            Assert.Equal(before, rom.Data);
        }

        [Fact]
        public void ApplyVoiceChanges_TruncatedVoiceAtTrackEnd_NoCrash()
        {
            // A 0xBD at the very last byte (no voice byte follows) is dropped by
            // ParseTrackOne (it breaks on addr+1 >= limitter), so it never
            // appears as a code -> ApplyVoiceChanges simply has nothing to write.
            byte[] data = new byte[0x500];
            data[SongAddr + 0] = 1; // 1 track
            WriteGbaPtr(data, SongAddr + 4, 0x800);
            WriteGbaPtr(data, SongAddr + 8, Track0Data);
            // Track data: a single 0xBD as the LAST byte of the ROM region.
            data[data.Length - 1] = 0xBD;
            // Repoint the track to that last byte.
            WriteGbaPtr(data, SongAddr + 8, (uint)(data.Length - 1));

            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            byte[] before = (byte[])rom.Data.Clone();

            string err = SongVoiceChangeCore.ApplyVoiceChanges(
                rom, SongAddr, new Dictionary<int, int> { { 0, 5 } });
            Assert.Equal("", err);          // no writable code -> success no-op
            Assert.Equal(before, rom.Data); // byte-identical
        }

        [Fact]
        public void ApplyVoiceChanges_AcceptsGbaPointerSongAddr()
        {
            // The seam normalizes a GBA pointer (0x08000000 | offset) via
            // U.toOffset, so passing the pointer form works the same as the
            // offset form.
            var rom = BuildSong(out var voiceAddrs);
            byte[] before = (byte[])rom.Data.Clone();

            string err = SongVoiceChangeCore.ApplyVoiceChanges(
                rom, SongAddr + 0x08000000, new Dictionary<int, int> { { 9, 42 } });
            Assert.Equal("", err);

            foreach (uint vAddr in voiceAddrs)
            {
                if (before[vAddr] == 9)
                    Assert.Equal(42, rom.Data[vAddr]);
            }
        }

        // ------------------------------------------------------------------
        // Truncated track-pointer TABLE (the header itself runs the +8+ti*4
        // pointer slots past EOF) — must NOT throw and must NOT mutate (#1088).
        // ------------------------------------------------------------------

        /// <summary>Build a ROM whose song header at 0x300 is safe to read, but
        /// trackCount=16 makes the +8+16*4 pointer table overrun the buffer end.</summary>
        static ROM BuildTruncatedTableRom()
        {
            // 0x308 (header end) fits; 0x300+8+16*4 = 0x348 overruns 0x320.
            byte[] data = new byte[0x320];
            data[SongAddr] = 16; // trackCount; the pointer table runs past EOF
            var rom = new ROM();
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        [Fact]
        public void GetDistinctVoices_TruncatedTrackPointerTable_ReturnsEmptyNoThrow()
        {
            var rom = BuildTruncatedTableRom();
            var list = SongVoiceChangeCore.GetDistinctVoices(rom, SongAddr);
            Assert.Empty(list); // no IndexOutOfRange; honors "never throws"
        }

        [Fact]
        public void ApplyVoiceChanges_TruncatedTrackPointerTable_ErrorAndByteIdentical()
        {
            // Failure path: returns a localized error with ZERO surviving
            // mutation (byte-identical) and no committed write — never throws.
            var rom = BuildTruncatedTableRom();
            byte[] before = (byte[])rom.Data.Clone();
            string err = SongVoiceChangeCore.ApplyVoiceChanges(
                rom, SongAddr, new Dictionary<int, int> { { 5, 9 } });
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Equal(before, rom.Data);
        }
    }
}
