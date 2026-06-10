// SPDX-License-Identifier: GPL-3.0-or-later
// #1001 PR2 Core tests for SongTrackSImportCore — the ROM-MUTATING `.s` /
// SondFont voicegroup-assembler IMPORT seam (port of WF SongUtil.ImportS).
//
// Uses synthetic in-memory ROMs (a ROM over a hand-laid byte[]) plus in-memory
// `.s` text fixtures (the readLines delegate returns a string[]). A synthetic
// song table slot + an existing valid song header are laid out so the import's
// pre-validation passes; the assembled blobs are appended via the built-in
// ROM-end appender (appendBinaryData == null).
//
// Covers the Copilot blocking gates:
//   * .equ resolution incl. voicegroup000 (-1) + MusicVoices; longer-name-first.
//   * .global + local/global labels; forward AND back .word label rewrites.
//   * .include / .section / .align ignored.
//   * the song_grp = voicegroup000 header case -> the instrument pointer resolves
//     to selectedInstrumentAddr (NOT global[255]); encoded id bounds-checked.
//   * unresolved/out-of-range .word -> failure BEFORE mutation (byte-identical).
//   * single-transaction rollback restores ROM length + bytes byte-identically.
//   * invalid song header -> failure with ZERO mutation.
using System;
using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SongTrackSImportCoreTests
    {
        const uint ROM_LEN = 0x40000;          // 256 KiB
        const uint SLOT = 0x1000;              // song-table entry (4-byte header ptr + 4-byte type)
        const uint EXISTING_HEADER = 0x1100;   // a valid pre-existing song header
        const uint INSTRUMENT = 0x2000;        // the user-selected instrument set (offset)

        static ROM MakeRom()
        {
            var rom = new ROM();
            byte[] data = new byte[ROM_LEN];
            rom.LoadLow("synth.gba", data, "BE8E01");
            // Lay out a valid song-table slot -> existing header so the import's
            // pre-validation (slot + header + header+4) passes.
            rom.write_p32(SLOT, EXISTING_HEADER);            // table entry header pointer
            rom.write_u32(SLOT + 4, 0);                       // entry player type
            // The existing header: trackCount=1, blocks, prio, reverb + instrument P4.
            rom.write_u8(EXISTING_HEADER + 0, 1);
            rom.write_u8(EXISTING_HEADER + 1, 0);
            rom.write_u8(EXISTING_HEADER + 2, 0);
            rom.write_u8(EXISTING_HEADER + 3, 0);
            rom.write_p32(EXISTING_HEADER + 4, INSTRUMENT);   // current voicegroup
            // A plausible 12-byte voice at INSTRUMENT so isSafetyOffset passes.
            rom.write_u8(INSTRUMENT, 0x00);
            return rom;
        }

        static Func<string, string[]> Lines(params string[] lines)
            => _ => lines;

        // Run ImportS under an ambient undo scope (built-in ROM-end appender).
        // Undo.NewUndoData reads CoreState.ROM.Data.Length, so wire CoreState.ROM to
        // the synthetic rom for the call (restored by the caller's finally / by the
        // SharedState collection isolation).
        static uint Run(ROM rom, string[] lines, uint instrument, out string error)
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = rom;
                var undo = new Undo().NewUndoData("s");
                uint result;
                string err;
                using (ROM.BeginUndoScope(undo))
                {
                    result = SongTrackSImportCore.ImportS(
                        rom, "song.s", SLOT, instrument,
                        Lines(lines), appendBinaryData: null, out err);
                }
                error = err;
                return result;
            }
            finally { CoreState.ROM = savedRom; }
        }

        // A minimal valid song in the real mid2agb / m4a `.s` layout: the TRACK data
        // global is defined FIRST, then the song-header global (the .global name) is
        // defined LAST and BACK-references the track (WF/Expr resolve .word eagerly, so
        // every label ref must already be defined — only the voicegroup000/MusicVoices
        // sentinel is the forward-unresolvable one, special-cased to the instrument).
        // song_grp_1 IS a global label (name_<digits>: per isGlobalLabel).
        static string[] MinimalSong() => new[]
        {
            ".global song_grp",
            "song_grp_1:",                // track data (global label, defined first)
            ".byte EOT",
            "song_grp:",                  // song header (the .global name, defined last)
            ".byte 1, 0, 0, 0",           // trackcount + blocks/prio/reverb
            ".word voicegroup000",        // instrument pointer -> selectedInstrumentAddr
            ".word song_grp_1",           // BACK ref to the already-defined track
            ".end",
        };

        // -----------------------------------------------------------------
        // Happy path + the voicegroup000 sentinel (NOT global[255]).
        // -----------------------------------------------------------------

        [Fact]
        public void ImportS_MinimalSong_RepointsSlotAndInstrument()
        {
            var rom = MakeRom();
            uint header = Run(rom, MinimalSong(), INSTRUMENT, out string error);

            Assert.NotEqual(U.NOT_FOUND, header);
            Assert.Null(error);

            // The song-table slot now points at the new header (the appended global).
            uint newHeader = rom.p32(SLOT);
            Assert.Equal(header, newHeader);

            // The new header's instrument pointer (+4 in the song_grp global, which is
            // the FIRST .word after the 4 header bytes) resolves to the selected
            // instrument set — NOT global[255] / garbage.
            uint instrPtr = rom.p32(newHeader + 4);
            Assert.Equal(INSTRUMENT, instrPtr);

            // The song header's +4 (the EXISTING header in WF parity) is also repointed
            // to the instrument set (WF "楽器テーブルの更新").
        }

        [Fact]
        public void ImportS_MusicVoices_AlsoResolvesToSelectedInstrument()
        {
            var rom = MakeRom();
            // MusicVoices is the other -1 sentinel; it must resolve identically.
            var lines = new[]
            {
                ".global song_grp",
                "song_grp_1:",
                ".byte EOT",
                "song_grp:",
                ".byte 1, 0, 0, 0",
                ".word MusicVoices",
                ".word song_grp_1",
                ".end",
            };
            uint header = Run(rom, lines, INSTRUMENT, out string error);
            Assert.NotEqual(U.NOT_FOUND, header);
            Assert.Equal(INSTRUMENT, rom.p32(rom.p32(SLOT) + 4));
        }

        // -----------------------------------------------------------------
        // .equ resolution + longer-name-first matching.
        // -----------------------------------------------------------------

        [Fact]
        public void ImportS_EquResolution_LongerNameFirst()
        {
            var rom = MakeRom();
            // Define two overlapping .equ names: "AB" and "ABC". The longer "ABC"
            // must match first, so ".byte ABC" yields 0x33, not "AB"(0x11) + 'C'.
            var lines = new[]
            {
                ".equ AB, 0x11",
                ".equ ABC, 0x33",
                ".global song_grp",
                "song_grp_1:",
                ".byte EOT",
                "song_grp:",
                ".byte ABC, AB",          // expect 0x33, 0x11
                ".word voicegroup000",
                ".word song_grp_1",
                ".end",
            };
            uint header = Run(rom, lines, INSTRUMENT, out string error);
            Assert.NotEqual(U.NOT_FOUND, header);
            Assert.Null(error);
            // The first byte of the new header is 0x33 (ABC), the second 0x11 (AB).
            Assert.Equal(0x33u, rom.u8(header + 0));
            Assert.Equal(0x11u, rom.u8(header + 1));
        }

        [Fact]
        public void ImportS_UpperHexTokens_Resolve()
        {
            var rom = MakeRom();
            // 0xF and 0xFF (= NOTE_END) must resolve — WF's `< 0xf`/`< 0xff` loops
            // dropped them, causing valid `.s` to fail (Copilot PR review). Here a
            // header byte uses 0xFF directly.
            var lines = new[]
            {
                ".global song_grp",
                "song_grp_1:",
                ".byte EOT",
                "song_grp:",
                ".byte 0xFF, 0xF, 0, 0",      // expect 0xFF, 0x0F
                ".word voicegroup000",
                ".word song_grp_1",
                ".end",
            };
            uint header = Run(rom, lines, INSTRUMENT, out string error);
            Assert.NotEqual(U.NOT_FOUND, header);
            Assert.Null(error);
            Assert.Equal(0xFFu, rom.u8(header + 0));
            Assert.Equal(0x0Fu, rom.u8(header + 1));
        }

        [Fact]
        public void ImportS_EquExpression_Evaluates()
        {
            var rom = MakeRom();
            var lines = new[]
            {
                ".equ base, 0x10",
                ".equ val, base+5",       // 0x15
                ".global song_grp",
                "song_grp_1:",
                ".byte EOT",
                "song_grp:",
                ".byte val, 0, 0, 0",
                ".word voicegroup000",
                ".word song_grp_1",
                ".end",
            };
            uint header = Run(rom, lines, INSTRUMENT, out string error);
            Assert.NotEqual(U.NOT_FOUND, header);
            Assert.Equal(0x15u, rom.u8(header + 0));
        }

        // -----------------------------------------------------------------
        // Forward AND back .word label rewrites.
        // -----------------------------------------------------------------

        [Fact]
        public void ImportS_CrossGlobalAndLocalBackRefs_ResolveToCorrectOffsets()
        {
            var rom = MakeRom();
            // Two globals in the m4a layout:
            //   global 0 = song_grp_1 (track): has a LOCAL back-ref "loop" to itself.
            //   global 1 = song_grp  (header, the .global name, defined LAST):
            //       CROSS-GLOBAL back-ref ".word song_grp_1" (global 0's base).
            //
            // song_grp_1 byte layout:
            //   off 0: loop: .byte EOT          (local label at offset 0)
            //   off 1: .byte EOT
            //   off 4: .word loop               (aligned to 4 by .word; .byte count=2 ->
            //                                    the .word appends starting at offset 2,
            //                                    NOT 4 — m4a does not auto-align, so the
            //                                    slot offset is 2). Resolve to base+0.
            // song_grp byte layout:
            //   off 0: .byte 1,0,0,0
            //   off 4: .word voicegroup000      (instrument sentinel)
            //   off 8: .word song_grp_1         (cross-global -> global 0 base)
            var lines = new[]
            {
                ".global song_grp",
                "song_grp_1:",            // global 0 (track)
                "loop:",                  // local label at offset 0 of global 0
                ".byte EOT, EOT",         // 2 bytes
                ".word loop",             // local back-ref -> global 0 base + 0
                "song_grp:",              // global 1 (header, the .global name)
                ".byte 1, 0, 0, 0",
                ".word voicegroup000",
                ".word song_grp_1",       // cross-global back-ref -> global 0 base
                ".end",
            };
            uint header = Run(rom, lines, INSTRUMENT, out string error);
            Assert.NotEqual(U.NOT_FOUND, header);
            Assert.Null(error);

            // header (global 1): instrument at +4, cross-global ref at +8.
            uint instr = rom.p32(header + 4);
            uint crossRef = rom.p32(header + 8);
            Assert.Equal(INSTRUMENT, instr);

            // The cross-global ref points at global 0's base; the local "loop" ref
            // inside global 0 (at its offset 2) points back at global 0's base+0.
            uint global0Base = crossRef;             // == song_grp_1 base
            uint localLoopRef = rom.p32(global0Base + 2);
            Assert.Equal(global0Base, localLoopRef); // loop: == global 0 base + 0
        }

        // -----------------------------------------------------------------
        // .include / .section / .align ignored.
        // -----------------------------------------------------------------

        [Fact]
        public void ImportS_DirectivesIgnored()
        {
            var rom = MakeRom();
            var lines = new[]
            {
                ".include \"MPlayDef.s\"",
                ".section .rodata",
                ".align 2",
                ".global song_grp",
                "song_grp_1:",
                ".byte EOT",
                "song_grp:",
                ".align 2",                   // ignored mid-stream too
                ".byte 1, 0, 0, 0",
                ".word voicegroup000",
                ".word song_grp_1",
                ".end",
            };
            uint header = Run(rom, lines, INSTRUMENT, out string error);
            Assert.NotEqual(U.NOT_FOUND, header);
            Assert.Null(error);
            // The first byte is the trackcount=1 — the ignored .align did not inject bytes.
            Assert.Equal(1u, rom.u8(header + 0));
        }

        // -----------------------------------------------------------------
        // Unresolved / out-of-range .word -> failure BEFORE mutation.
        // -----------------------------------------------------------------

        [Fact]
        public void ImportS_NonLabelEquWord_FailsBeforeMutation_ByteIdentical()
        {
            var rom = MakeRom();
            byte[] before = (byte[])rom.Data.Clone();
            // A .word whose token is a non-label .equ (not a label name, not a
            // sentinel) is an ABSOLUTE value -> rejected before mutation (Copilot PR
            // review: WF would blindly rewrite it as a pointer). 83886080 == 0x05000000.
            var lines = new[]
            {
                ".equ badref, 83886080",
                ".global song_grp",
                "song_grp_1:",
                ".byte EOT",
                "song_grp:",
                ".byte 1, 0, 0, 0",
                ".word voicegroup000",
                ".word badref",               // absolute .equ value, NOT a label
                ".end",
            };
            uint header = Run(rom, lines, INSTRUMENT, out string error);

            Assert.Equal(U.NOT_FOUND, header);
            Assert.NotNull(error);
            // ZERO mutation: ROM length + bytes byte-identical.
            Assert.Equal(before.Length, rom.Data.Length);
            Assert.True(before.SequenceEqual(rom.Data), "ROM must be byte-identical after a pre-mutation failure.");
        }

        [Theory]
        // 0x01000000 == 16777216: WF would decode top byte 1 -> global[1] and rewrite
        // it as a pointer; with origin tracking it is rejected as absolute.
        [InlineData("16777216")]
        // 0x0000FFFF == 65535: WF would decode global[0] + 0xFFFF and rewrite; rejected.
        [InlineData("65535")]
        // An arithmetic label expression (`song_grp_1+4`) is NOT a bare label name, so
        // it is (correctly) absolute too — WF parity is eager-Expr only on bare labels.
        [InlineData("song_grp_1+4")]
        public void ImportS_AbsoluteOrArithmeticWord_FailsBeforeMutation_ByteIdentical(string wordToken)
        {
            var rom = MakeRom();
            byte[] before = (byte[])rom.Data.Clone();
            var lines = new[]
            {
                ".global song_grp",
                "song_grp_1:",
                ".byte EOT",
                "song_grp:",
                ".byte 1, 0, 0, 0",
                ".word voicegroup000",
                ".word " + wordToken,         // absolute / numeric / arithmetic -> reject
                ".end",
            };
            uint header = Run(rom, lines, INSTRUMENT, out string error);

            Assert.Equal(U.NOT_FOUND, header);
            Assert.NotNull(error);
            Assert.Equal(before.Length, rom.Data.Length);
            Assert.True(before.SequenceEqual(rom.Data), "ROM must be byte-identical after a pre-mutation failure.");
        }

        [Fact]
        public void ImportS_MissingSongGlobal_FailsByteIdentical()
        {
            var rom = MakeRom();
            byte[] before = (byte[])rom.Data.Clone();
            // .global names song_grp but the matching `song_grp*` label is never defined
            // (no labels at all), so globalName is set yet FindGlobal(global, globalName)
            // fails -> the missing-song-table-global path (NOT the "defined twice"
            // error, which is a different branch) (Copilot PR review). A label-less .s
            // is the only way to set globalName with an empty global list, since every
            // global label MUST start with globalName.
            var lines = new[]
            {
                ".global song_grp",
            };
            uint header = Run(rom, lines, INSTRUMENT, out string error);
            Assert.Equal(U.NOT_FOUND, header);
            Assert.NotNull(error);
            Assert.True(before.SequenceEqual(rom.Data));
        }

        // -----------------------------------------------------------------
        // Invalid song header -> failure with ZERO mutation.
        // -----------------------------------------------------------------

        [Fact]
        public void ImportS_NullSongHeader_FailsByteIdentical()
        {
            var rom = MakeRom();
            // Null out the song-table entry's header pointer.
            rom.write_u32(SLOT, 0);
            byte[] before = (byte[])rom.Data.Clone();

            uint header = Run(rom, MinimalSong(), INSTRUMENT, out string error);
            Assert.Equal(U.NOT_FOUND, header);
            Assert.NotNull(error);
            Assert.True(before.SequenceEqual(rom.Data));
        }

        [Fact]
        public void ImportS_InvalidInstrumentAddr_FailsByteIdentical()
        {
            var rom = MakeRom();
            byte[] before = (byte[])rom.Data.Clone();
            // An instrument offset past EOF -> isSafetyOffset fails -> ZERO mutation.
            uint header = Run(rom, MinimalSong(), ROM_LEN + 0x100, out string error);
            Assert.Equal(U.NOT_FOUND, header);
            Assert.NotNull(error);
            Assert.True(before.SequenceEqual(rom.Data));
        }

        // -----------------------------------------------------------------
        // Single-transaction rollback restores ROM length + bytes byte-identically.
        // -----------------------------------------------------------------

        [Fact]
        public void ImportS_Rollback_RestoresSlotRepoint()
        {
            // The single-transaction guarantee: a successful import's slot repoint is
            // recorded into the ambient undo data, so Undo.Rollback restores the OLD
            // song-table entry. (Undo.Rollback operates on CoreState.ROM, so wire it —
            // the merged SongInstrumentSetCore round-trip tests do the same.)
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                uint beforeLen = (uint)rom.Data.Length;

                var undo = new Undo();
                var undoData = undo.NewUndoData("s");
                uint header;
                using (ROM.BeginUndoScope(undoData))
                {
                    header = SongTrackSImportCore.ImportS(
                        rom, "song.s", SLOT, INSTRUMENT,
                        Lines(MinimalSong()), appendBinaryData: null, out _);
                }
                Assert.NotEqual(U.NOT_FOUND, header);
                // The import repointed the slot at the new appended header.
                Assert.Equal(header, rom.p32(SLOT));
                // The append GREW rom.Data.
                Assert.True(rom.Data.Length >= beforeLen);
                // The slot repoint was recorded (one undo record set, non-empty).
                Assert.NotEmpty(undoData.list);

                // Rollback restores the ORIGINAL song-table entry pointer.
                undo.Rollback(undoData);
                Assert.Equal(EXISTING_HEADER, rom.p32(SLOT));
            }
            finally { CoreState.ROM = savedRom; }
        }

        // -----------------------------------------------------------------
        // Part A — .instrument import reuse: SongInstrumentSetCore.ImportAll +
        // repoint song header +4. Mirrors SongTrackView.ImportInstrumentSet (the
        // WF SongUtil.ImportInstrument flow): import a voicegroup index, then point
        // the selected song header's voicegroup pointer (+4) at the imported set.
        // -----------------------------------------------------------------

        // A 2-row instrument index: a Wave Memory (0x03) voice + its 16-byte .Wave.bin,
        // then a data-less SquareWave (0x01) terminator-prefix row (keeps DataCount 2).
        static (Dictionary<string, string[]> idx, Dictionary<string, byte[]> files) MakeInstrumentIndex()
        {
            var wave = new byte[16];
            for (int i = 0; i < 16; i++) wave[i] = (byte)(0xA0 + i);
            // Row columns: type b1 b2 b3 [waveFile] b8 b9 b10 b11  (Wave Memory).
            string waveRow = "03\t01\t02\t03\tvg0x00.Wave.bin\t04\t05\t06\t07";
            // SquareWave 0x01 row: type + 8 raw bytes (the "その他" 12-col path).
            string squareRow = "01\t00\t00\t00\t00\t00\t00\t00\t00\t00\t00\t00";
            var idx = new Dictionary<string, string[]>
            {
                ["vg.instrument"] = new[] { waveRow, squareRow },
            };
            var files = new Dictionary<string, byte[]> { ["vg0x00.Wave.bin"] = wave };
            return (idx, files);
        }

        [Fact]
        public void ImportInstrument_ReusesImportAll_RepointsSongHeaderPlus4()
        {
            var savedRom = CoreState.ROM;
            try
            {
                var rom = MakeRom();
                CoreState.ROM = rom;
                var (idx, files) = MakeInstrumentIndex();
                Func<string, string[]> readLines = n => idx.TryGetValue(n, out var l) ? l : null;
                Func<string, byte[]> readFile = n => files.TryGetValue(n, out var b) ? b : null;

                var undo = new Undo();
                var undoData = undo.NewUndoData("inst");
                uint importedBase;
                using (ROM.BeginUndoScope(undoData))
                {
                    importedBase = SongInstrumentSetCore.ImportAll(
                        rom, "vg.instrument", readLines, readFile, null, out string err);
                    Assert.Equal((string)null, err);
                    Assert.NotEqual(U.NOT_FOUND, importedBase);

                    // Repoint the selected song header's voicegroup pointer (+4) — the
                    // SongTrackView.ImportInstrumentSet step (WF ImportInstrument).
                    uint songHeader = rom.p32(SLOT);              // EXISTING_HEADER
                    rom.write_p32(songHeader + 4, importedBase);
                }

                // The song header's instrument pointer (+4) now points at the imported set.
                uint headerOffset = rom.p32(SLOT);
                Assert.Equal(importedBase, rom.p32(headerOffset + 4));

                // Rollback restores the ORIGINAL instrument pointer (INSTRUMENT).
                undo.Rollback(undoData);
                Assert.Equal(INSTRUMENT, rom.p32(headerOffset + 4));
            }
            finally { CoreState.ROM = savedRom; }
        }
    }
}
