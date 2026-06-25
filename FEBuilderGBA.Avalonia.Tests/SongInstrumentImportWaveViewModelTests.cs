// SPDX-License-Identifier: GPL-3.0-or-later
// #1448 — the Avalonia DirectSound wav-import window was an empty stub (dummy
// addr-0 entry, no conversion options, no preview, no DPCM). These headless
// tests drive the real ported SongInstrumentImportWaveViewModel and prove:
//   * Seed loads the source wav + WF-default option values.
//   * DPCM gating: UseDpcm has no effect (DpcmEffective == false) when the HQ
//     mixer patch is absent; a no-op convert with all-default params produces a
//     raw 8-bit GBA sample (never a DPCM one without the patch).
//   * RunPreview formats a non-empty result line (size delta) using all-default
//     (no-op) params so no external sox binary is required.
//   * Convert returns ready GBA-sample bytes for a valid 8-bit wav (raw path).
//   * Convert surfaces a clear error when sox is required but unconfigured.
//
// All conversions here use all-default (no-op) sox params so they run offline
// (the sox no-op early-exit returns the input wave verbatim).

using System;
using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Core;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class SongInstrumentImportWaveViewModelTests : IDisposable
{
    readonly ROM? _savedRom;
    readonly Config? _savedConfig;

    public SongInstrumentImportWaveViewModelTests()
    {
        _savedRom = CoreState.ROM;
        _savedConfig = CoreState.Config;
    }

    public void Dispose()
    {
        CoreState.ROM = _savedRom;
        CoreState.Config = _savedConfig;
    }

    // Build a minimal 8-bit mono WAV with a smooth triangle body.
    static byte[] MakeWav(byte[] pcm, uint freq = 12000)
    {
        var fp = new List<byte>();
        void Ascii(string s) { foreach (var c in s) fp.Add((byte)c); }
        Ascii("RIFF");
        U.append_u32(fp, (uint)(36 + pcm.Length));
        Ascii("WAVE");
        Ascii("fmt ");
        U.append_u32(fp, 16);
        U.append_u16(fp, 1);     // PCM
        U.append_u16(fp, 1);     // channels
        U.append_u32(fp, freq);  // +24
        U.append_u32(fp, freq);  // +28
        U.append_u16(fp, 1);     // +32
        U.append_u16(fp, 8);     // +34 bits per sample
        Ascii("data");
        U.append_u32(fp, (uint)pcm.Length); // +40
        fp.AddRange(pcm);
        return fp.ToArray();
    }

    static byte[] TrianglePcm(int n)
    {
        var pcm = new byte[n];
        for (int i = 0; i < n; i++)
        {
            int t = i % 128;
            int v = (t < 64) ? t : (128 - t);
            pcm[i] = (byte)(0x80 + v);
        }
        return pcm;
    }

    [Fact]
    public void Seed_LoadsSourceAndWfDefaults()
    {
        var vm = new SongInstrumentImportWaveViewModel();
        byte[] wav = MakeWav(TrianglePcm(128));
        vm.Seed(wav, "drum.wav");

        Assert.True(vm.IsLoaded);
        Assert.Equal("drum.wav", vm.SourceFileName);
        // WF defaults
        Assert.Equal(13379u, vm.Hz);
        Assert.Equal(1u, vm.Strip);
        Assert.Equal(1u, vm.Channel);
        Assert.Equal(100u, vm.Volume);
        Assert.False(vm.UseDpcm);
        Assert.Equal(3u, vm.Lookahead);
    }

    [Fact]
    public void Dpcm_GatedOff_WhenNoHqMixer()
    {
        CoreState.ROM = null; // HasHqMixer(null) == false
        var vm = new SongInstrumentImportWaveViewModel();
        vm.Seed(MakeWav(TrianglePcm(128)), "x.wav");

        Assert.False(vm.HqMixerAvailable);
        Assert.False(vm.CanUseDpcm);

        vm.UseDpcm = true;           // user toggles it on
        Assert.False(vm.DpcmEffective); // but it is gated off — never applied

        // All-default params => sox no-op => raw 8-bit sample (not DPCM).
        vm.Channel = 0; vm.Hz = 0; vm.Strip = 0; vm.Volume = 0;
        byte[]? sample = vm.Convert(out string err);
        Assert.Null(err);
        Assert.NotNull(sample);
        Assert.NotEqual(0x01u, U.u32(sample!, 0)); // compression flag != DPCM
    }

    [Fact]
    public void RunPreview_NoOpParams_ProducesSizeLine()
    {
        var vm = new SongInstrumentImportWaveViewModel();
        vm.Seed(MakeWav(TrianglePcm(256)), "x.wav");
        vm.Channel = 0; vm.Hz = 0; vm.Strip = 0; vm.Volume = 0; // no-op (offline)

        string text = vm.RunPreview();
        Assert.False(string.IsNullOrEmpty(text));
        Assert.Equal(vm.PreviewText, text);
        // a successful preview is NOT the placeholder/error text
        Assert.DoesNotContain("requires", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_RawPath_ReturnsSampleBytes()
    {
        var vm = new SongInstrumentImportWaveViewModel();
        vm.Seed(MakeWav(TrianglePcm(128)), "x.wav");
        vm.Channel = 0; vm.Hz = 0; vm.Strip = 0; vm.Volume = 0; // no-op

        byte[]? sample = vm.Convert(out string err);
        Assert.Null(err);
        Assert.NotNull(sample);
        Assert.True(sample!.Length > 16);
    }

    [Fact]
    public void Convert_SoxRequiredButUnconfigured_SurfacesError()
    {
        // A non-no-op transform requires sox; force it unconfigured.
        CoreState.Config = new Config();
        CoreState.Config["sox"] = "";

        var vm = new SongInstrumentImportWaveViewModel();
        vm.Seed(MakeWav(TrianglePcm(128)), "x.wav");
        vm.Channel = 1; vm.Hz = 12000; vm.Strip = 0; vm.Volume = 0; // needs sox

        byte[]? sample = vm.Convert(out string err);
        Assert.Null(sample);
        Assert.False(string.IsNullOrEmpty(err));
        Assert.Contains("sox", err, StringComparison.OrdinalIgnoreCase);
    }
}
