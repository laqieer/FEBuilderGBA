using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Tests for the Color Reduction Tool VM (#998 PR 2). The VM is a thin
    /// driver over the merged Core engine <c>DecreaseColorConvertCore</c>:
    /// <c>ApplyPreset</c> mirrors WF <c>Method_SelectedIndexChanged</c>,
    /// <c>Reduce</c> calls <c>ReduceColorFile</c>. No ROM mutation, no Undo.
    /// </summary>
    [Collection("SharedState")]
    public class DecreaseColorTSAToolViewModelTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public DecreaseColorTSAToolViewModelTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        static int RomVersion(RomFixture fixture) =>
            fixture.IsAvailable ? (CoreState.ROM?.RomInfo?.version ?? 8) : 8;

        // -----------------------------------------------------------------
        // ApplyPreset — VM fields must equal the Core preset for every method
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(0xA)]
        public void ApplyPreset_MatchesCorePreset(int method)
        {
            int ver = RomVersion(_fixture);
            var expected = DecreaseColorConvertCore.GetMethodPreset(method, ver);

            var vm = new DecreaseColorTSAToolViewModel();
            vm.ApplyPreset(method);

            Assert.Equal(expected.Width, vm.Width);
            Assert.Equal(expected.Height, vm.Height);
            Assert.Equal(expected.Yohaku, vm.Yohaku);
            Assert.Equal(expected.PaletteNo, vm.PaletteNo);
            Assert.Equal(expected.Scalable, vm.Scalable);
            Assert.Equal(expected.Scalable ? 1 : 0, vm.SizeMethodIndex);
            Assert.Equal(expected.Reserve1st, vm.Reserve1st);
            Assert.Equal(expected.Reserve1st ? 1 : 0, vm.ReserveIndex);
            Assert.Equal(expected.IgnoreTSA, vm.IgnoreTSA);

            _output.WriteLine(
                $"method {method} (v{ver}): {vm.Width}x{vm.Height} y={vm.Yohaku} " +
                $"pal={vm.PaletteNo} scale={vm.Scalable} reserve={vm.Reserve1st} tsa={vm.IgnoreTSA}");
        }

        [Fact]
        public void ApplyPreset_Method2_BattleBG_ConcreteValues()
        {
            // Method 2 (Battle BG) is version-independent: 30*8 x 20*8, no yohaku,
            // 8 banks, reserve on, scale on, TSA off.
            var vm = new DecreaseColorTSAToolViewModel();
            vm.ApplyPreset(2);

            Assert.Equal(30 * 8, vm.Width);
            Assert.Equal(20 * 8, vm.Height);
            Assert.Equal(0, vm.Yohaku);
            Assert.Equal(8, vm.PaletteNo);
            Assert.Equal(1, vm.SizeMethodIndex);
            Assert.Equal(1, vm.ReserveIndex);
            Assert.False(vm.IgnoreTSA);
        }

        // -----------------------------------------------------------------
        // Method 0 = manual no-op (WF combo item 00=自分で決める has no branch)
        // -----------------------------------------------------------------

        [Fact]
        public void ApplyPreset_Method0_IsNoOp_PreservesValues()
        {
            var vm = new DecreaseColorTSAToolViewModel();

            // Start from method 7 (single-image map chips: 512x512, 5 banks, no scale).
            vm.ApplyPreset(7);
            int w = vm.Width, h = vm.Height, y = vm.Yohaku, p = vm.PaletteNo;
            int sm = vm.SizeMethodIndex, rv = vm.ReserveIndex;
            bool tsa = vm.IgnoreTSA;

            // Selecting method 0 must NOT overwrite anything.
            vm.ApplyPreset(0);

            Assert.Equal(w, vm.Width);
            Assert.Equal(h, vm.Height);
            Assert.Equal(y, vm.Yohaku);
            Assert.Equal(p, vm.PaletteNo);
            Assert.Equal(sm, vm.SizeMethodIndex);
            Assert.Equal(rv, vm.ReserveIndex);
            Assert.Equal(tsa, vm.IgnoreTSA);
        }

        [Fact]
        public void ApplyPreset_NegativeMethod_IsNoOp()
        {
            var vm = new DecreaseColorTSAToolViewModel();
            vm.ApplyPreset(2);
            int w = vm.Width;
            vm.ApplyPreset(-1);
            Assert.Equal(w, vm.Width);
        }

        // -----------------------------------------------------------------
        // ShowIgnoreTSA toggles with PaletteNo and raises PropertyChanged
        // -----------------------------------------------------------------

        [Fact]
        public void ShowIgnoreTSA_TogglesWithPaletteNo()
        {
            var vm = new DecreaseColorTSAToolViewModel();

            vm.PaletteNo = 3;
            Assert.False(vm.ShowIgnoreTSA);

            vm.PaletteNo = 4;
            Assert.True(vm.ShowIgnoreTSA);

            vm.PaletteNo = 16;
            Assert.True(vm.ShowIgnoreTSA);

            vm.PaletteNo = 1;
            Assert.False(vm.ShowIgnoreTSA);
        }

        [Fact]
        public void PaletteNo_Set_RaisesShowIgnoreTSAPropertyChanged()
        {
            var vm = new DecreaseColorTSAToolViewModel();
            vm.PaletteNo = 1; // ensure a change below

            var changed = new List<string>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != null) changed.Add(e.PropertyName);
            };

            vm.PaletteNo = 8;

            Assert.Contains(nameof(vm.PaletteNo), changed);
            Assert.Contains(nameof(vm.ShowIgnoreTSA), changed);
        }

        // -----------------------------------------------------------------
        // PropertyChanged for int/bool bound fields (the sweep only checks the
        // first string/uint property, so cover these explicitly)
        // -----------------------------------------------------------------

        [Theory]
        [InlineData(nameof(DecreaseColorTSAToolViewModel.Width), 100)]
        [InlineData(nameof(DecreaseColorTSAToolViewModel.Height), 200)]
        [InlineData(nameof(DecreaseColorTSAToolViewModel.Yohaku), 16)]
        [InlineData(nameof(DecreaseColorTSAToolViewModel.Method), 5)]
        [InlineData(nameof(DecreaseColorTSAToolViewModel.SizeMethodIndex), 0)]
        [InlineData(nameof(DecreaseColorTSAToolViewModel.ReserveIndex), 0)]
        public void IntProperty_Set_RaisesPropertyChanged(string propName, int value)
        {
            var vm = new DecreaseColorTSAToolViewModel();
            var prop = typeof(DecreaseColorTSAToolViewModel).GetProperty(propName)!;

            // Make sure we actually change the value.
            if ((int)prop.GetValue(vm)! == value)
                value += 1;

            var changed = new List<string>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != null) changed.Add(e.PropertyName);
            };

            prop.SetValue(vm, value);
            Assert.Contains(propName, changed);
        }

        [Fact]
        public void IgnoreTSA_Set_RaisesPropertyChanged()
        {
            var vm = new DecreaseColorTSAToolViewModel { IgnoreTSA = false };

            var changed = new List<string>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != null) changed.Add(e.PropertyName);
            };

            vm.IgnoreTSA = true;
            Assert.Contains(nameof(vm.IgnoreTSA), changed);
        }

        [Fact]
        public void StringProperties_Set_RaisePropertyChanged()
        {
            var vm = new DecreaseColorTSAToolViewModel();
            var changed = new List<string>();
            ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != null) changed.Add(e.PropertyName);
            };

            vm.InputPath = "a.png";
            vm.OutputPath = "b.png";
            vm.StatusMessage = "hi";

            Assert.Contains(nameof(vm.InputPath), changed);
            Assert.Contains(nameof(vm.OutputPath), changed);
            Assert.Contains(nameof(vm.StatusMessage), changed);
        }

        // -----------------------------------------------------------------
        // Scalable / Reserve1st combo-index → Core-bool mapping
        // -----------------------------------------------------------------

        [Fact]
        public void SizeMethodIndex_MapsTo_Scalable()
        {
            var vm = new DecreaseColorTSAToolViewModel { SizeMethodIndex = 1 };
            Assert.True(vm.Scalable);
            vm.SizeMethodIndex = 0;
            Assert.False(vm.Scalable);
        }

        [Fact]
        public void ReserveIndex_MapsTo_Reserve1st()
        {
            var vm = new DecreaseColorTSAToolViewModel { ReserveIndex = 1 };
            Assert.True(vm.Reserve1st);
            vm.ReserveIndex = 0;
            Assert.False(vm.Reserve1st);
        }

        // -----------------------------------------------------------------
        // Reduce — Core error codes
        // -----------------------------------------------------------------

        [Fact]
        public void Reduce_MissingInput_ReturnsMinus2()
        {
            EnsureImageService();
            var vm = new DecreaseColorTSAToolViewModel
            {
                InputPath = Path.Combine(Path.GetTempPath(), "no_such_file_" + Guid.NewGuid() + ".png"),
                OutputPath = Path.Combine(Path.GetTempPath(), "out_" + Guid.NewGuid() + ".png"),
            };

            int code = vm.Reduce();
            Assert.Equal(-2, code);
            Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
        }

        [Fact]
        public void Reduce_NoImageService_ReturnsMinus1()
        {
            // Save & null the image service so ReduceColorFile hits its -1 guard.
            var prev = CoreState.ImageService;
            string input = WriteTempTestPng();
            string output = Path.Combine(Path.GetTempPath(), "out_" + Guid.NewGuid() + ".png");
            try
            {
                CoreState.ImageService = null;
                var vm = new DecreaseColorTSAToolViewModel
                {
                    InputPath = input,
                    OutputPath = output,
                    Width = 16,
                    Height = 16,
                    PaletteNo = 4,
                };
                int code = vm.Reduce();
                Assert.Equal(-1, code);
            }
            finally
            {
                CoreState.ImageService = prev;
                TryDelete(input);
                TryDelete(output);
            }
        }

        [Fact]
        public void Reduce_HappyPath_ReturnsZero_AndWritesOutput()
        {
            EnsureImageService();
            string input = WriteTempTestPng();
            string output = Path.Combine(Path.GetTempPath(), "out_" + Guid.NewGuid() + ".png");
            try
            {
                var vm = new DecreaseColorTSAToolViewModel
                {
                    InputPath = input,
                    OutputPath = output,
                };
                vm.ApplyPreset(2); // Battle BG: 240x160, 8 banks, scale, reserve

                int code = vm.Reduce();

                Assert.Equal(0, code);
                Assert.True(File.Exists(output), "Reduce must write the output PNG.");

                using var saved = CoreState.ImageService!.LoadImage(output);
                Assert.NotNull(saved);
                // Output dimensions = Padding8(width) + yohaku  x  Padding8(height).
                Assert.Equal(240, saved.Width);
                Assert.Equal(160, saved.Height);

                Assert.Contains(output, vm.StatusMessage);
            }
            finally
            {
                TryDelete(input);
                TryDelete(output);
            }
        }

        // -----------------------------------------------------------------
        // helpers
        // -----------------------------------------------------------------

        static void EnsureImageService()
        {
            if (CoreState.ImageService == null)
                CoreState.ImageService = new SkiaImageService();
        }

        /// <summary>Write a small multi-color RGBA test PNG to a temp file.</summary>
        static string WriteTempTestPng()
        {
            EnsureImageService();
            const int w = 32, h = 32;
            var rgba = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (x + y * w) * 4;
                    rgba[i + 0] = (byte)((x * 8) & 0xFF);
                    rgba[i + 1] = (byte)((y * 8) & 0xFF);
                    rgba[i + 2] = (byte)(((x + y) * 4) & 0xFF);
                    rgba[i + 3] = 255;
                }
            }
            string path = Path.Combine(Path.GetTempPath(), "in_" + Guid.NewGuid() + ".png");
            using var img = CoreState.ImageService!.CreateImage(w, h);
            img.SetPixelData(rgba);
            img.Save(path);
            return path;
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort */ }
        }
    }
}
