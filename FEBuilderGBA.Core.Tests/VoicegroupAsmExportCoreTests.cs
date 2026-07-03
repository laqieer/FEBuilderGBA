// SPDX-License-Identifier: GPL-3.0-or-later
// Tests for VoicegroupAsmExportCore (#1362) — export a FEBuilder voicegroup
// (M4A instrument set) as reviewable decomp source macro asm.
//
// Coverage:
//   * PURE formatter (ROM-free, hand-built VoiceRecords): each supported macro
//     line is emitted with the correct args (directsound, square1, square2,
//     programmable_wave, noise); keysplit/keysplit_all -> macro + unresolved
//     diagnostic; unsupported type -> commented placeholder + diagnostic, NO wrong
//     macro; DirectSound/Noise pan decode (incl. nonzero + non-canonical).
//   * ReadVoicegroup on a synthetic ROM (>= 0x200): decodes a hand-laid voicegroup.
//   * Export never mutates the ROM; null/guard never throws; out-of-range diagnosed.

using System;
using System.Collections.Generic;
using Xunit;
using FEBuilderGBA;

namespace FEBuilderGBA.Core.Tests
{
    public class VoicegroupAsmExportCoreTests
    {
        // ---- helpers -------------------------------------------------------

        static ROM MakeRom(int size = 0x10000)
        {
            var rom = new ROM();
            rom.SwapNewROMDataDirect(new byte[size]);
            return rom;
        }

        static VoicegroupAsmExportCore.VoiceRecord Rec(byte type, VoicegroupAsmExportCore.VoiceKind kind)
        {
            return new VoicegroupAsmExportCore.VoiceRecord
            {
                Index = 0,
                Type = type,
                Kind = kind,
                Raw = new byte[12],
            };
        }

        // ---- PURE formatter ------------------------------------------------

        [Fact]
        public void Format_DirectSound_EmitsMacroWithArgs()
        {
            var v = Rec(0x00, VoicegroupAsmExportCore.VoiceKind.DirectSound);
            v.BaseMidiKey = 60; v.Pan = 0;
            v.Pointer4 = 0x08123456; v.Pointer4Valid = true;
            v.Attack = 255; v.Decay = 0; v.Sustain = 255; v.Release = 0;

            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 7, 0x500, out var diags);

            Assert.Contains(".include \"asm/macros/music_voice.inc\"", s);
            Assert.Contains("voicegroup007:", s);
            Assert.Contains(".global voicegroup007", s);
            Assert.Contains("voice_directsound 60, 0, 0x08123456, 255, 0, 255, 0", s);
            // The sample pointer is unresolved -> a diagnostic exists, but NO inline comment in args.
            Assert.Contains(diags, d => d.Contains("0x08123456"));
            Assert.DoesNotContain("0x08123456 @", s); // no inline comment after the arg
        }

        // #1775: FE8J voicegroup section directives ---------------------------

        [Fact]
        public void Format_FE8J_Multibyte_EmitsNamedSectionAndWordAlign()
        {
            var v = Rec(0x01, VoicegroupAsmExportCore.VoiceKind.Square1);
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 0, 0x08207470, isMultibyte: true, out _);

            // FE8J: named .rodata.voicegroupNNN sub-section with SHF_ALLOC + word align.
            Assert.Contains(".section .rodata.voicegroup000, \"a\", %progbits", s);
            Assert.Contains("\t.align 4\n", s);
            Assert.Contains("voicegroup000:", s);
            // Must NOT emit the generic fe8u directives.
            Assert.DoesNotContain("\t.section .rodata\n", s);
            Assert.DoesNotContain("\t.align 2\n", s);
        }

        [Fact]
        public void Format_FE8U_Default_KeepsGenericSectionAndHalfwordAlign()
        {
            var v = Rec(0x01, VoicegroupAsmExportCore.VoiceKind.Square1);
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 43, 0x500, isMultibyte: false, out _);

            Assert.Contains("\t.section .rodata\n", s);
            Assert.Contains("\t.align 2\n", s);
            Assert.DoesNotContain(".rodata.voicegroup", s);   // no FE8J named section
        }

        [Fact]
        public void Format_FourArgOverload_DefaultsToFe8u()
        {
            var v = Rec(0x01, VoicegroupAsmExportCore.VoiceKind.Square1);
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 0, 0x500, out _);
            Assert.Contains("\t.section .rodata\n", s);
            Assert.DoesNotContain(".rodata.voicegroup", s);
        }

        [Fact]
        public void Format_DirectSound_NoResample_And_Alt_UseCorrectMacroNames()
        {
            var v8 = Rec(0x08, VoicegroupAsmExportCore.VoiceKind.DirectSound);
            v8.Pointer4 = 0x08000200; v8.Pointer4Valid = true;
            var v10 = Rec(0x10, VoicegroupAsmExportCore.VoiceKind.DirectSound);
            v10.Pointer4 = 0x08000200; v10.Pointer4Valid = true;

            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v8, v10 }, 0, 0x500, out _);

            Assert.Contains("voice_directsound_no_resample ", s);
            Assert.Contains("voice_directsound_alt ", s);
        }

        [Fact]
        public void Format_Square1_EmitsSweepDutyAdsr()
        {
            var v = Rec(0x01, VoicegroupAsmExportCore.VoiceKind.Square1);
            v.Sweep = 5; v.Duty = 2; v.Attack = 1; v.Decay = 2; v.Sustain = 3; v.Release = 4;
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 0, 0x500, out _);
            Assert.Contains("voice_square_1 5, 2, 1, 2, 3, 4", s);
        }

        [Fact]
        public void Format_Square2_EmitsDutyAdsr()
        {
            var v = Rec(0x02, VoicegroupAsmExportCore.VoiceKind.Square2);
            v.Duty = 1; v.Attack = 7; v.Decay = 6; v.Sustain = 5; v.Release = 4;
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 0, 0x500, out _);
            Assert.Contains("voice_square_2 1, 7, 6, 5, 4", s);
        }

        [Fact]
        public void Format_ProgrammableWave_EmitsPointerAndAdsr()
        {
            var v = Rec(0x03, VoicegroupAsmExportCore.VoiceKind.ProgrammableWave);
            v.Pointer4 = 0x08009000; v.Pointer4Valid = true;
            v.Attack = 1; v.Decay = 1; v.Sustain = 1; v.Release = 1;
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 0, 0x500, out var diags);
            Assert.Contains("voice_programmable_wave 0x08009000, 1, 1, 1, 1", s);
            Assert.Contains(diags, d => d.Contains("0x08009000"));
        }

        [Fact]
        public void Format_Noise_EmitsAllFields()
        {
            var v = Rec(0x04, VoicegroupAsmExportCore.VoiceKind.Noise);
            v.BaseMidiKey = 60; v.Pan = 0; v.NoiseUnk = 0; v.NoisePeriod = 1;
            v.Attack = 0; v.Decay = 0; v.Sustain = 15; v.Release = 7;
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 0, 0x500, out _);
            Assert.Contains("voice_noise 60, 0, 0, 1, 0, 0, 15, 7", s);
        }

        [Fact]
        public void Format_KeySplit_EmitsMacroAndUnresolvedDiagnostic()
        {
            var v = Rec(0x40, VoicegroupAsmExportCore.VoiceKind.KeySplit);
            v.Pointer4 = 0x08001000; v.Pointer4Valid = true;
            v.Pointer8 = 0x08002000; v.Pointer8Valid = true;
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 0, 0x500, out var diags);
            Assert.Contains("voice_keysplit 0x08001000, 0x08002000", s);
            // No inline comment between the two pointer args.
            Assert.DoesNotContain("0x08001000 @", s);
            Assert.Contains(diags, d => d.Contains("NOT inlined") || d.Contains("manually"));
        }

        [Fact]
        public void Format_KeySplitAll_EmitsMacroAndDiagnostic()
        {
            var v = Rec(0x80, VoicegroupAsmExportCore.VoiceKind.KeySplitAll);
            v.Pointer4 = 0x08003000; v.Pointer4Valid = true;
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 0, 0x500, out var diags);
            Assert.Contains("voice_keysplit_all 0x08003000", s);
            Assert.Contains(diags, d => d.Contains("drum") || d.Contains("manually"));
        }

        [Fact]
        public void Format_UnsupportedType_EmitsCommentedPlaceholder_NoWrongMacro()
        {
            // 0x18 is NOT a music_voice.inc macro (Copilot plan review pt 1).
            var v = Rec(0x18, VoicegroupAsmExportCore.VoiceKind.Unsupported);
            v.Raw = new byte[] { 0x18, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            string s = VoicegroupAsmExportCore.FormatVoicegroup(
                new List<VoicegroupAsmExportCore.VoiceRecord> { v }, 0, 0x500, out var diags);
            Assert.Contains("@ UNSUPPORTED voice type 0x18", s);
            Assert.DoesNotContain("voice_directsound", s); // never a guessed macro
            Assert.Contains(diags, d => d.Contains("unsupported voice type"));
        }

        [Fact]
        public void Format_NullVoices_DoesNotThrow()
        {
            string s = VoicegroupAsmExportCore.FormatVoicegroup(null, 0, 0x500, out var diags);
            Assert.Contains(".include", s);
            Assert.NotNull(diags);
        }

        // ---- pan decode ----------------------------------------------------

        [Fact]
        public void Decode_DirectSoundPan_Nonzero_StripsHighBit()
        {
            var rom = MakeRom();
            uint a = 0x1000;
            // type 0x00, key 60, +2=0, +3 = 0x80|0x10 = 0x90 -> pan arg 0x10
            WriteDirectSound(rom, a, key: 60, panByte: 0x90, samplePtr: 0x08004000, adsr: new byte[] { 1, 2, 3, 4 });
            // terminator
            WriteTerminator(rom, a + 12);

            var voices = VoicegroupAsmExportCore.ReadVoicegroup(rom, a);
            Assert.Single(voices);
            Assert.Equal(0x10, voices[0].Pan);
            Assert.False(voices[0].PanNonCanonical);
        }

        [Fact]
        public void Decode_NoisePan_NonCanonical_Flagged()
        {
            var rom = MakeRom();
            uint a = 0x1000;
            // noise type 0x04, +1 key, +2 pan = 0x10 (nonzero, bit7 clear -> non-canonical), +3 unk, +4 period
            byte[] e = new byte[12];
            e[0] = 0x04; e[1] = 60; e[2] = 0x10; e[3] = 0; e[4] = 1;
            e[8] = 0; e[9] = 0; e[10] = 15; e[11] = 7;
            WriteBytes(rom, a, e);
            WriteTerminator(rom, a + 12);

            var voices = VoicegroupAsmExportCore.ReadVoicegroup(rom, a);
            Assert.Single(voices);
            Assert.Equal(VoicegroupAsmExportCore.VoiceKind.Noise, voices[0].Kind);
            Assert.Equal(0x10, voices[0].Pan);
            Assert.True(voices[0].PanNonCanonical);
            // formatter should add a diagnostic
            VoicegroupAsmExportCore.FormatVoicegroup(voices, 0, a, out var diags);
            Assert.Contains(diags, d => d.Contains("non-canonical pan"));
        }

        // ---- ReadVoicegroup ------------------------------------------------

        [Fact]
        public void ReadVoicegroup_DecodesMixedTypes()
        {
            var rom = MakeRom();
            uint a = 0x2000;
            // voice 0: directsound
            WriteDirectSound(rom, a, key: 48, panByte: 0, samplePtr: 0x08005000, adsr: new byte[] { 9, 8, 7, 6 });
            // voice 1: square1 (type 0x01, +1=0x3C, +3 sweep, +4 duty)
            byte[] sq = new byte[12];
            sq[0] = 0x01; sq[1] = 0x3C; sq[3] = 7; sq[4] = 2; sq[8] = 1; sq[9] = 2; sq[10] = 3; sq[11] = 4;
            WriteBytes(rom, a + 12, sq);
            // terminator
            WriteTerminator(rom, a + 24);

            var voices = VoicegroupAsmExportCore.ReadVoicegroup(rom, a);
            Assert.Equal(2, voices.Count);
            Assert.Equal(VoicegroupAsmExportCore.VoiceKind.DirectSound, voices[0].Kind);
            Assert.Equal((byte)48, voices[0].BaseMidiKey);
            Assert.Equal(VoicegroupAsmExportCore.VoiceKind.Square1, voices[1].Kind);
            Assert.Equal((byte)7, voices[1].Sweep);
            Assert.Equal((byte)2, voices[1].Duty);
        }

        // ---- Export (read-only / guards) -----------------------------------

        [Fact]
        public void Export_NullRom_DoesNotThrow_ReturnsDiagnostic()
        {
            var r = VoicegroupAsmExportCore.Export(null, 0x1000, 0);
            Assert.False(r.Ok);
            Assert.NotEmpty(r.Diagnostics);
        }

        [Fact]
        public void Export_OutOfRangeAddr_Diagnosed_NoThrow()
        {
            var rom = MakeRom();
            var r = VoicegroupAsmExportCore.Export(rom, 0x7FFFFFFF, 0);
            Assert.False(r.Ok);
            Assert.NotEmpty(r.Diagnostics);
        }

        [Fact]
        public void Export_DoesNotMutateRom()
        {
            var rom = MakeRom();
            uint a = 0x3000;
            WriteDirectSound(rom, a, key: 60, panByte: 0, samplePtr: 0x08005000, adsr: new byte[] { 1, 2, 3, 4 });
            WriteTerminator(rom, a + 12);

            byte[] before = (byte[])rom.Data.Clone();
            var r = VoicegroupAsmExportCore.Export(rom, a, 3);
            Assert.True(r.Ok);
            Assert.Equal(before.Length, rom.Data.Length);
            Assert.Equal(before, rom.Data);
            Assert.Contains("voicegroup003:", r.Text);
        }

        // ---- raw ROM writers ----------------------------------------------

        static void WriteBytes(ROM rom, uint addr, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++) rom.Data[addr + i] = bytes[i];
        }

        static void WriteDirectSound(ROM rom, uint addr, byte key, byte panByte, uint samplePtr, byte[] adsr)
        {
            byte[] e = new byte[12];
            e[0] = 0x00; e[1] = key; e[2] = 0; e[3] = panByte;
            e[4] = (byte)(samplePtr & 0xFF);
            e[5] = (byte)((samplePtr >> 8) & 0xFF);
            e[6] = (byte)((samplePtr >> 16) & 0xFF);
            e[7] = (byte)((samplePtr >> 24) & 0xFF);
            e[8] = adsr[0]; e[9] = adsr[1]; e[10] = adsr[2]; e[11] = adsr[3];
            WriteBytes(rom, addr, e);
        }

        static void WriteTerminator(ROM rom, uint addr)
        {
            // 12 zero bytes — an all-zero type byte fails IsValidVoice, stopping the scan.
            for (int i = 0; i < 12; i++) rom.Data[addr + i] = 0;
        }
    }
}
