// SPDX-License-Identifier: GPL-3.0-or-later
// #1448 Core tests for SongWaveConvertCore / SongSoxConvertCore — the ENCODE /
// quality-preview / .s-load side of the DirectSound wav-import pipeline (the
// decode side is covered by SongDirectSoundWavCoreTests).
//
// Validates (no external sox binary required — sox helper tested for the
// unconfigured / no-op paths only):
//   * WavToDPCMByte: emits a 0x01-flagged GBA DPCM sample that DECODES back via
//     SongDirectSoundWavCore.ByteToWavForDPCM with high SNR on a smooth signal
//   * WavToDPCMByte rejects non-RIFF / too-small / >8-bit (null + error, no throw)
//   * CalculateSNR: exact match => 100; known noisy input => finite dB; nulls => 0
//   * LoadWavS: .byte / .word / comment parsing; bad token => false + error
//   * HasHqMixer: false on a synthetic ROM (no patch bytes), never throws
//   * SongSoxConvertCore.IsNoOp / ConvertWaveBySox: no-op early-exit returns input
//     verbatim; unconfigured sox => null + clear error (no throw, no temp leak)
//   * Convert/Preview orchestration: DPCM gated off when hqMixerAvailable=false
using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    [Collection("SharedState")]
    public class SongWaveConvertCoreTests
    {
        // Build a minimal canonical 44-byte WAV header + the supplied 8-bit PCM
        // body. fmt_bits_per_sample at +34, data_chunk_size at +40.
        static byte[] MakeWav(int bitsPerSample, byte[] pcm, uint freq = 12000)
        {
            var fp = new List<byte>();
            void Ascii(string s) { foreach (var c in s) fp.Add((byte)c); }
            Ascii("RIFF");
            U.append_u32(fp, (uint)(36 + pcm.Length));
            Ascii("WAVE");
            Ascii("fmt ");
            U.append_u32(fp, 16);
            U.append_u16(fp, 1);                   // PCM
            U.append_u16(fp, 1);                   // channels
            U.append_u32(fp, freq);                // samples per sec (+24)
            U.append_u32(fp, freq);                // bytes per sec (+28)
            U.append_u16(fp, 1);                   // block align (+32)
            U.append_u16(fp, (uint)bitsPerSample); // bits per sample (+34)
            Ascii("data");
            U.append_u32(fp, (uint)pcm.Length);    // data chunk size (+40)
            fp.AddRange(pcm);
            return fp.ToArray();
        }

        // A smooth slowly-varying 8-bit signal (a triangle wave) — DPCM tracks it
        // accurately, so the round-trip SNR is high. Length is a multiple of 64.
        static byte[] SmoothPcm(int n)
        {
            var pcm = new byte[n];
            for (int i = 0; i < n; i++)
            {
                int t = i % 128;
                int v = (t < 64) ? t : (128 - t); // 0..63..0 triangle
                pcm[i] = (byte)(0x80 + v);        // centered around mid-scale
            }
            return pcm;
        }

        // ---- WavToDPCMByte ----------------------------------------------------

        [Fact]
        public void WavToDPCMByte_EmitsDpcmHeaderAndRoundTrips()
        {
            byte[] wav = MakeWav(8, SmoothPcm(256));
            byte[] dpcm = SongWaveConvertCore.WavToDPCMByte(wav, 3, out string err);

            Assert.Null(err);
            Assert.NotNull(dpcm);
            // header: +0 compression flag == 0x01 (DPCM)
            Assert.Equal(0x01u, U.u32(dpcm, 0));
            // +4 freq*1024
            Assert.Equal(12000u * 1024u, U.u32(dpcm, 4));
            // +12 original PCM length == 256
            Assert.Equal(256u, U.u32(dpcm, 12));

            // Decode it back and assert high SNR vs the source wave.
            byte[] decoded = SongDirectSoundWavCore.ByteToWavForDPCM(dpcm, 0);
            Assert.NotNull(decoded);
            double snr = SongWaveConvertCore.CalculateSNR(wav, decoded);
            Assert.True(snr > 20.0, $"expected high SNR on smooth signal, got {snr}");
        }

        [Fact]
        public void WavToDPCMByte_RejectsNonRiff()
        {
            byte[] bad = MakeWav(8, SmoothPcm(64));
            bad[0] = (byte)'X';
            byte[] r = SongWaveConvertCore.WavToDPCMByte(bad, 3, out string err);
            Assert.Null(r);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void WavToDPCMByte_RejectsTooSmall()
        {
            byte[] tiny = new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' };
            byte[] r = SongWaveConvertCore.WavToDPCMByte(tiny, 3, out string err);
            Assert.Null(r);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void WavToDPCMByte_Rejects16Bit()
        {
            byte[] wav = MakeWav(16, SmoothPcm(64));
            byte[] r = SongWaveConvertCore.WavToDPCMByte(wav, 3, out string err);
            Assert.Null(r);
            Assert.False(string.IsNullOrEmpty(err));
        }

        [Fact]
        public void WavToDPCMByte_ZeroLookaheadDoesNotThrowAndProducesSample()
        {
            byte[] wav = MakeWav(8, SmoothPcm(128));
            // lookahead 0 is clamped to 1 internally — must still produce a sample.
            byte[] r = SongWaveConvertCore.WavToDPCMByte(wav, 0, out string err);
            Assert.Null(err);
            Assert.NotNull(r);
            Assert.Equal(0x01u, U.u32(r, 0));
        }

        // ---- CalculateSNR -----------------------------------------------------

        [Fact]
        public void CalculateSNR_ExactMatchReturns100()
        {
            byte[] a = MakeWav(8, SmoothPcm(128));
            Assert.Equal(100.0, SongWaveConvertCore.CalculateSNR(a, (byte[])a.Clone()));
        }

        [Fact]
        public void CalculateSNR_NoisyInputIsFinite()
        {
            byte[] a = MakeWav(8, SmoothPcm(256));
            byte[] b = (byte[])a.Clone();
            for (int i = 0x44; i < b.Length; i += 3) b[i] ^= 0x10; // inject error
            double snr = SongWaveConvertCore.CalculateSNR(a, b);
            Assert.True(!double.IsNaN(snr) && !double.IsInfinity(snr));
            Assert.True(snr < 100.0);
        }

        [Fact]
        public void CalculateSNR_NullsReturnZero()
        {
            Assert.Equal(0.0, SongWaveConvertCore.CalculateSNR(null, new byte[10]));
            Assert.Equal(0.0, SongWaveConvertCore.CalculateSNR(new byte[10], null));
        }

        [Fact]
        public void CalculateSNR_ShortInputReturnsZeroNot100()
        {
            // Both shorter than the 0x44 PCM-body start: there is NO data to
            // compare, so SNR must be 0 (Copilot review #1537) — NOT the 100
            // "exact match" value the empty loop would otherwise produce.
            byte[] a = new byte[0x40];
            byte[] b = new byte[0x40];
            Assert.Equal(0.0, SongWaveConvertCore.CalculateSNR(a, b));

            // Exactly at the boundary (max == 0x44, loop body never runs) => 0.
            Assert.Equal(0.0, SongWaveConvertCore.CalculateSNR(new byte[0x44], new byte[0x44]));
        }

        // ---- LoadWavS ---------------------------------------------------------

        [Fact]
        public void LoadWavS_ParsesByteAndWordDirectives()
        {
            string[] lines =
            {
                "@ a comment line",
                ".byte 0x01, 0x02, 3, -1   # trailing comment",
                ".word 0x08000000",
            };
            bool ok = SongWaveConvertCore.LoadWavS(lines, out byte[] data, out string err);
            Assert.True(ok, err);
            Assert.Null(err);
            // 4 bytes from .byte (0x01,0x02,0x03,0xFF) + 4 bytes from the .word.
            Assert.Equal(8, data.Length);
            Assert.Equal(0x01, data[0]);
            Assert.Equal(0x02, data[1]);
            Assert.Equal(0x03, data[2]);
            Assert.Equal(0xFF, data[3]);
            Assert.Equal(0x08000000u, U.u32(data, 4));
        }

        [Fact]
        public void LoadWavS_BadTokenReturnsError()
        {
            string[] lines = { ".byte 0x01, notanumber" };
            bool ok = SongWaveConvertCore.LoadWavS(lines, out byte[] data, out string err);
            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(err));
            Assert.Empty(data);
        }

        [Fact]
        public void LoadWavS_NullLinesIsHandled()
        {
            bool ok = SongWaveConvertCore.LoadWavS((string[])null, out byte[] data, out string err);
            Assert.False(ok);
            Assert.NotNull(data);
        }

        // ---- HasHqMixer -------------------------------------------------------

        [Fact]
        public void HasHqMixer_NullRomFalse()
        {
            Assert.False(SongWaveConvertCore.HasHqMixer(null));
        }

        // ---- SongSoxConvertCore ----------------------------------------------

        [Fact]
        public void Sox_IsNoOp_AllDefaultsTrue()
        {
            Assert.True(SongSoxConvertCore.IsNoOp(0, 0, 0, 0));
            Assert.False(SongSoxConvertCore.IsNoOp(1, 0, 0, 0));
            Assert.False(SongSoxConvertCore.IsNoOp(0, 12000, 0, 0));
        }

        [Fact]
        public void Sox_NoOpReturnsInputVerbatim()
        {
            byte[] wav = MakeWav(8, SmoothPcm(64));
            byte[] r = SongSoxConvertCore.ConvertWaveBySox(wav, 0, 0, 0, 0, out string err);
            Assert.Null(err);
            Assert.NotNull(r);
            Assert.Equal(wav, r);
            Assert.NotSame(wav, r); // a clone, not the same array
        }

        [Fact]
        public void Sox_UnconfiguredReturnsClearError()
        {
            // Force an unconfigured sox path for this test, then restore.
            Config saved = CoreState.Config;
            try
            {
                CoreState.Config = new Config();
                CoreState.Config["sox"] = ""; // explicitly unset
                byte[] wav = MakeWav(8, SmoothPcm(64));
                // A non-no-op transform => sox is required => clear error.
                byte[] r = SongSoxConvertCore.ConvertWaveBySox(wav, 1, 12000, 0, 0, out string err);
                Assert.Null(r);
                Assert.False(string.IsNullOrEmpty(err));
                Assert.Contains("sox", err, StringComparison.OrdinalIgnoreCase);
                Assert.False(SongSoxConvertCore.IsSoxAvailable());
            }
            finally
            {
                CoreState.Config = saved;
            }
        }

        // ---- Convert / Preview orchestration ---------------------------------

        [Fact]
        public void Convert_RawPcmWhenDpcmOff()
        {
            byte[] wav = MakeWav(8, SmoothPcm(128));
            var r = SongWaveConvertCore.Convert(wav, 0, 0, 0, 0, useDpcm: false, lookahead: 3, hqMixerAvailable: true);
            Assert.Null(r.Error);
            Assert.NotNull(r.SampleBytes);
            Assert.False(r.IsDpcm);
            // raw 8-bit GBA sample header: +0 compression flag != 0x01.
            Assert.NotEqual(0x01u, U.u32(r.SampleBytes, 0));
        }

        [Fact]
        public void Convert_DpcmGatedOffWhenNoHqMixer()
        {
            byte[] wav = MakeWav(8, SmoothPcm(128));
            // useDpcm requested but the HQ mixer patch is absent => raw fallback.
            var r = SongWaveConvertCore.Convert(wav, 0, 0, 0, 0, useDpcm: true, lookahead: 3, hqMixerAvailable: false);
            Assert.Null(r.Error);
            Assert.NotNull(r.SampleBytes);
            Assert.False(r.IsDpcm); // NOT DPCM — gated off, never produces an unplayable sample
        }

        [Fact]
        public void Convert_DpcmWhenHqMixerAvailable()
        {
            byte[] wav = MakeWav(8, SmoothPcm(128));
            var r = SongWaveConvertCore.Convert(wav, 0, 0, 0, 0, useDpcm: true, lookahead: 3, hqMixerAvailable: true);
            Assert.Null(r.Error);
            Assert.NotNull(r.SampleBytes);
            Assert.True(r.IsDpcm);
            Assert.Equal(0x01u, U.u32(r.SampleBytes, 0));
        }

        [Fact]
        public void Preview_DpcmReportsSnrAndSizes()
        {
            byte[] wav = MakeWav(8, SmoothPcm(256));
            var p = SongWaveConvertCore.Preview(wav, 0, 0, 0, 0, useDpcm: true, lookahead: 3, hqMixerAvailable: true);
            Assert.Null(p.Error);
            Assert.True(p.IsDpcm);
            Assert.Equal(wav.Length, p.OriginalSize);
            Assert.True(p.ResultSize > 0);
            Assert.False(double.IsNaN(p.Snr));
            Assert.True(p.Snr > 20.0);
        }

        [Fact]
        public void Preview_RawHasNoSnr()
        {
            byte[] wav = MakeWav(8, SmoothPcm(128));
            var p = SongWaveConvertCore.Preview(wav, 0, 0, 0, 0, useDpcm: false, lookahead: 3, hqMixerAvailable: true);
            Assert.Null(p.Error);
            Assert.False(p.IsDpcm);
            Assert.True(double.IsNaN(p.Snr));
        }
    }
}
