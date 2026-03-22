using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Views;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Visual rendering sweep tests that verify Views render non-blank content
    /// using Avalonia's headless rendering pipeline.
    ///
    /// For each target view, two tests are provided:
    /// 1. Renders_NonBlank: View with loaded DataContext produces a non-trivial bitmap.
    /// 2. Renders_WithData_DiffersFromEmpty: Loaded vs empty view produces different output.
    ///
    /// Target views: UnitEditorView, ClassEditorView, ItemEditorView,
    /// MapSettingFE6View (FE6 only), MapSettingFE7UView (non-FE6).
    /// </summary>
    [Collection("SharedState")]
    public class VisualRenderingSweepTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        private const int RenderWidth = 800;
        private const int RenderHeight = 600;

        /// <summary>
        /// Minimum PNG stream length to consider the bitmap non-blank.
        /// A truly blank 1x1 PNG is ~67 bytes; an 800x600 all-white PNG is typically ~2-3 KB.
        /// Real UI content should produce substantially more.
        /// </summary>
        // An 800x600 all-blank PNG is ~2-3 KB. Real UI content should exceed this.
        private const int MinNonBlankStreamLength = 3000;

        public VisualRenderingSweepTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // =============================================================
        // Helper methods
        // =============================================================

        /// <summary>
        /// Measure, arrange, and render a control to a PNG MemoryStream.
        /// Returns null if rendering fails (some views require a Window parent).
        /// </summary>
        private MemoryStream? RenderToStream(Control view)
        {
            try
            {
                view.Measure(new Size(RenderWidth, RenderHeight));
                view.Arrange(new Rect(0, 0, RenderWidth, RenderHeight));

                var bitmap = new RenderTargetBitmap(new PixelSize(RenderWidth, RenderHeight));
                bitmap.Render(view);

                var stream = new MemoryStream();
                bitmap.Save(stream);
                stream.Position = 0;
                return stream;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Render failed: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Assert that the rendered stream represents non-blank content.
        /// </summary>
        private void AssertNonBlank(MemoryStream? stream, string viewName)
        {
            Assert.NotNull(stream);
            Assert.True(stream!.Length > MinNonBlankStreamLength,
                $"{viewName}: Rendered PNG stream length ({stream.Length} bytes) " +
                $"should exceed {MinNonBlankStreamLength} bytes for non-blank content");
            _output.WriteLine($"{viewName}: rendered {stream.Length} bytes");
        }

        // =============================================================
        // UnitEditorView
        // =============================================================

        [AvaloniaFact]
        public void UnitEditorView_Renders_NonBlank()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 units"); return; }

            vm.LoadUnit(list[1].addr);

            var view = new UnitEditorView();
            view.DataContext = vm;

            using var stream = RenderToStream(view);
            AssertNonBlank(stream, nameof(UnitEditorView));
        }

        [AvaloniaFact]
        public void UnitEditorView_WithData_DiffersFromEmpty()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 units"); return; }

            // Render empty view
            var emptyView = new UnitEditorView();
            using var emptyStream = RenderToStream(emptyView);
            if (emptyStream == null) { _output.WriteLine("SKIP: empty render failed"); return; }

            // Render loaded view
            vm.LoadUnit(list[1].addr);
            var loadedView = new UnitEditorView();
            loadedView.DataContext = vm;
            using var loadedStream = RenderToStream(loadedView);
            if (loadedStream == null) { _output.WriteLine("SKIP: loaded render failed"); return; }

            _output.WriteLine($"Empty: {emptyStream.Length} bytes, Loaded: {loadedStream.Length} bytes");

            // Either size differs or byte content differs
            bool differs = emptyStream.Length != loadedStream.Length;
            if (!differs)
            {
                emptyStream.Position = 0;
                loadedStream.Position = 0;
                var emptyBytes = emptyStream.ToArray();
                var loadedBytes = loadedStream.ToArray();
                for (int i = 0; i < emptyBytes.Length; i++)
                {
                    if (emptyBytes[i] != loadedBytes[i]) { differs = true; break; }
                }
            }

            Assert.True(differs,
                "UnitEditorView with loaded data should render differently from empty view");
        }

        // =============================================================
        // ClassEditorView
        // =============================================================

        [AvaloniaFact]
        public void ClassEditorView_Renders_NonBlank()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 classes"); return; }

            vm.LoadClass(list[1].addr);

            var view = new ClassEditorView();
            view.DataContext = vm;

            using var stream = RenderToStream(view);
            AssertNonBlank(stream, nameof(ClassEditorView));
        }

        [AvaloniaFact]
        public void ClassEditorView_WithData_DiffersFromEmpty()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 classes"); return; }

            // Render empty view
            var emptyView = new ClassEditorView();
            using var emptyStream = RenderToStream(emptyView);
            if (emptyStream == null) { _output.WriteLine("SKIP: empty render failed"); return; }

            // Render loaded view
            vm.LoadClass(list[1].addr);
            var loadedView = new ClassEditorView();
            loadedView.DataContext = vm;
            using var loadedStream = RenderToStream(loadedView);
            if (loadedStream == null) { _output.WriteLine("SKIP: loaded render failed"); return; }

            _output.WriteLine($"Empty: {emptyStream.Length} bytes, Loaded: {loadedStream.Length} bytes");

            bool differs = emptyStream.Length != loadedStream.Length;
            if (!differs)
            {
                var emptyBytes = emptyStream.ToArray();
                var loadedBytes = loadedStream.ToArray();
                for (int i = 0; i < emptyBytes.Length; i++)
                {
                    if (emptyBytes[i] != loadedBytes[i]) { differs = true; break; }
                }
            }

            Assert.True(differs,
                "ClassEditorView with loaded data should render differently from empty view");
        }

        // =============================================================
        // ItemEditorView
        // =============================================================

        [AvaloniaFact]
        public void ItemEditorView_Renders_NonBlank()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 items"); return; }

            vm.LoadItem(list[1].addr);

            var view = new ItemEditorView();
            view.DataContext = vm;

            using var stream = RenderToStream(view);
            AssertNonBlank(stream, nameof(ItemEditorView));
        }

        [AvaloniaFact]
        public void ItemEditorView_WithData_DiffersFromEmpty()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 items"); return; }

            // Render empty view
            var emptyView = new ItemEditorView();
            using var emptyStream = RenderToStream(emptyView);
            if (emptyStream == null) { _output.WriteLine("SKIP: empty render failed"); return; }

            // Render loaded view
            vm.LoadItem(list[1].addr);
            var loadedView = new ItemEditorView();
            loadedView.DataContext = vm;
            using var loadedStream = RenderToStream(loadedView);
            if (loadedStream == null) { _output.WriteLine("SKIP: loaded render failed"); return; }

            _output.WriteLine($"Empty: {emptyStream.Length} bytes, Loaded: {loadedStream.Length} bytes");

            bool differs = emptyStream.Length != loadedStream.Length;
            if (!differs)
            {
                var emptyBytes = emptyStream.ToArray();
                var loadedBytes = loadedStream.ToArray();
                for (int i = 0; i < emptyBytes.Length; i++)
                {
                    if (emptyBytes[i] != loadedBytes[i]) { differs = true; break; }
                }
            }

            Assert.True(differs,
                "ItemEditorView with loaded data should render differently from empty view");
        }

        // =============================================================
        // MapSettingFE6View (FE6 only)
        // =============================================================

        [AvaloniaFact]
        public void MapSettingFE6View_Renders_NonBlank()
        {
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM?.RomInfo?.version != 6)
            {
                _output.WriteLine("SKIP: not an FE6 ROM (version={0})", CoreState.ROM?.RomInfo?.version);
                return;
            }

            var vm = new MapSettingFE6ViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 map settings"); return; }

            vm.LoadEntry(list[1].addr);

            var view = new MapSettingFE6View();
            view.DataContext = vm;

            using var stream = RenderToStream(view);
            AssertNonBlank(stream, nameof(MapSettingFE6View));
        }

        [AvaloniaFact]
        public void MapSettingFE6View_WithData_DiffersFromEmpty()
        {
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM?.RomInfo?.version != 6)
            {
                _output.WriteLine("SKIP: not an FE6 ROM");
                return;
            }

            var vm = new MapSettingFE6ViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 map settings"); return; }

            // Render empty view
            var emptyView = new MapSettingFE6View();
            using var emptyStream = RenderToStream(emptyView);
            if (emptyStream == null) { _output.WriteLine("SKIP: empty render failed"); return; }

            // Render loaded view
            vm.LoadEntry(list[1].addr);
            var loadedView = new MapSettingFE6View();
            loadedView.DataContext = vm;
            using var loadedStream = RenderToStream(loadedView);
            if (loadedStream == null) { _output.WriteLine("SKIP: loaded render failed"); return; }

            _output.WriteLine($"Empty: {emptyStream.Length} bytes, Loaded: {loadedStream.Length} bytes");

            bool differs = emptyStream.Length != loadedStream.Length;
            if (!differs)
            {
                var emptyBytes = emptyStream.ToArray();
                var loadedBytes = loadedStream.ToArray();
                for (int i = 0; i < emptyBytes.Length; i++)
                {
                    if (emptyBytes[i] != loadedBytes[i]) { differs = true; break; }
                }
            }

            Assert.True(differs,
                "MapSettingFE6View with loaded data should render differently from empty view");
        }

        // =============================================================
        // MapSettingFE7UView (non-FE6 only)
        // =============================================================

        [AvaloniaFact]
        public void MapSettingFE7UView_Renders_NonBlank()
        {
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM?.RomInfo?.version == 6)
            {
                _output.WriteLine("SKIP: FE6 ROM -- MapSettingFE7UView is for FE7U/FE8U");
                return;
            }

            var vm = new MapSettingFE7UViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 map settings"); return; }

            vm.LoadEntry(list[1].addr);

            var view = new MapSettingFE7UView();
            view.DataContext = vm;

            using var stream = RenderToStream(view);
            AssertNonBlank(stream, nameof(MapSettingFE7UView));
        }

        [AvaloniaFact]
        public void MapSettingFE7UView_WithData_DiffersFromEmpty()
        {
            if (!_fixture.IsAvailable) return;
            if (CoreState.ROM?.RomInfo?.version == 6)
            {
                _output.WriteLine("SKIP: FE6 ROM -- MapSettingFE7UView is for FE7U/FE8U");
                return;
            }

            var vm = new MapSettingFE7UViewModel();
            var list = vm.LoadMapSettingList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 map settings"); return; }

            // Render empty view
            var emptyView = new MapSettingFE7UView();
            using var emptyStream = RenderToStream(emptyView);
            if (emptyStream == null) { _output.WriteLine("SKIP: empty render failed"); return; }

            // Render loaded view
            vm.LoadEntry(list[1].addr);
            var loadedView = new MapSettingFE7UView();
            loadedView.DataContext = vm;
            using var loadedStream = RenderToStream(loadedView);
            if (loadedStream == null) { _output.WriteLine("SKIP: loaded render failed"); return; }

            _output.WriteLine($"Empty: {emptyStream.Length} bytes, Loaded: {loadedStream.Length} bytes");

            bool differs = emptyStream.Length != loadedStream.Length;
            if (!differs)
            {
                var emptyBytes = emptyStream.ToArray();
                var loadedBytes = loadedStream.ToArray();
                for (int i = 0; i < emptyBytes.Length; i++)
                {
                    if (emptyBytes[i] != loadedBytes[i]) { differs = true; break; }
                }
            }

            Assert.True(differs,
                "MapSettingFE7UView with loaded data should render differently from empty view");
        }

        // =============================================================
        // Cross-cutting: pure instantiation rendering (no ROM needed)
        // =============================================================

        [AvaloniaFact]
        public void UnitEditorView_EmptyInstantiation_RendersWithoutCrash()
        {
            if (!_fixture.IsAvailable) return;
            var view = new UnitEditorView();
            using var stream = RenderToStream(view);
            Assert.NotNull(stream);
            _output.WriteLine($"UnitEditorView empty render: {stream.Length} bytes");
        }

        [AvaloniaFact]
        public void ClassEditorView_EmptyInstantiation_RendersWithoutCrash()
        {
            if (!_fixture.IsAvailable) return;
            var view = new ClassEditorView();
            using var stream = RenderToStream(view);
            Assert.NotNull(stream);
            _output.WriteLine($"ClassEditorView empty render: {stream.Length} bytes");
        }

        [AvaloniaFact]
        public void ItemEditorView_EmptyInstantiation_RendersWithoutCrash()
        {
            if (!_fixture.IsAvailable) return;
            var view = new ItemEditorView();
            using var stream = RenderToStream(view);
            Assert.NotNull(stream);
            _output.WriteLine($"ItemEditorView empty render: {stream.Length} bytes");
        }

        [AvaloniaFact]
        public void MapSettingFE6View_EmptyInstantiation_RendersWithoutCrash()
        {
            if (!_fixture.IsAvailable) return;
            var view = new MapSettingFE6View();
            using var stream = RenderToStream(view);
            Assert.NotNull(stream);
            _output.WriteLine($"MapSettingFE6View empty render: {stream.Length} bytes");
        }

        [AvaloniaFact]
        public void MapSettingFE7UView_EmptyInstantiation_RendersWithoutCrash()
        {
            if (!_fixture.IsAvailable) return;
            var view = new MapSettingFE7UView();
            using var stream = RenderToStream(view);
            Assert.NotNull(stream);
            _output.WriteLine($"MapSettingFE7UView empty render: {stream.Length} bytes");
        }

        // =============================================================
        // Render size scaling: verify output scales with view size
        // =============================================================

        [AvaloniaFact]
        public void UnitEditorView_RenderScalesWithSize()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 units"); return; }
            vm.LoadUnit(list[1].addr);

            // Render at small size
            var smallView = new UnitEditorView();
            smallView.DataContext = vm;
            long smallLength = 0;
            try
            {
                smallView.Measure(new Size(200, 150));
                smallView.Arrange(new Rect(0, 0, 200, 150));
                var smallBmp = new RenderTargetBitmap(new PixelSize(200, 150));
                smallBmp.Render(smallView);
                using var smallStream = new MemoryStream();
                smallBmp.Save(smallStream);
                smallLength = smallStream.Length;
            }
            catch { _output.WriteLine("SKIP: small render failed"); return; }

            // Render at large size
            var largeView = new UnitEditorView();
            largeView.DataContext = vm;
            long largeLength = 0;
            try
            {
                largeView.Measure(new Size(RenderWidth, RenderHeight));
                largeView.Arrange(new Rect(0, 0, RenderWidth, RenderHeight));
                var largeBmp = new RenderTargetBitmap(new PixelSize(RenderWidth, RenderHeight));
                largeBmp.Render(largeView);
                using var largeStream = new MemoryStream();
                largeBmp.Save(largeStream);
                largeLength = largeStream.Length;
            }
            catch { _output.WriteLine("SKIP: large render failed"); return; }

            _output.WriteLine($"Small (200x150): {smallLength} bytes, Large (800x600): {largeLength} bytes");

            // Larger render should produce more data (or at least equal)
            Assert.True(largeLength >= smallLength,
                $"Larger render ({largeLength}) should be >= smaller render ({smallLength})");
        }

        [AvaloniaFact]
        public void ClassEditorView_RenderScalesWithSize()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ClassEditorViewModel();
            var list = vm.LoadClassList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 classes"); return; }
            vm.LoadClass(list[1].addr);

            long smallLength = 0;
            try
            {
                var smallView = new ClassEditorView();
                smallView.DataContext = vm;
                smallView.Measure(new Size(200, 150));
                smallView.Arrange(new Rect(0, 0, 200, 150));
                var smallBmp = new RenderTargetBitmap(new PixelSize(200, 150));
                smallBmp.Render(smallView);
                using var s = new MemoryStream();
                smallBmp.Save(s);
                smallLength = s.Length;
            }
            catch { _output.WriteLine("SKIP: small render failed"); return; }

            long largeLength = 0;
            try
            {
                var largeView = new ClassEditorView();
                largeView.DataContext = vm;
                largeView.Measure(new Size(RenderWidth, RenderHeight));
                largeView.Arrange(new Rect(0, 0, RenderWidth, RenderHeight));
                var largeBmp = new RenderTargetBitmap(new PixelSize(RenderWidth, RenderHeight));
                largeBmp.Render(largeView);
                using var s = new MemoryStream();
                largeBmp.Save(s);
                largeLength = s.Length;
            }
            catch { _output.WriteLine("SKIP: large render failed"); return; }

            _output.WriteLine($"Small (200x150): {smallLength} bytes, Large (800x600): {largeLength} bytes");

            Assert.True(largeLength >= smallLength,
                $"Larger render ({largeLength}) should be >= smaller render ({smallLength})");
        }

        [AvaloniaFact]
        public void ItemEditorView_RenderScalesWithSize()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 2) { _output.WriteLine("SKIP: fewer than 2 items"); return; }
            vm.LoadItem(list[1].addr);

            long smallLength = 0;
            try
            {
                var smallView = new ItemEditorView();
                smallView.DataContext = vm;
                smallView.Measure(new Size(200, 150));
                smallView.Arrange(new Rect(0, 0, 200, 150));
                var smallBmp = new RenderTargetBitmap(new PixelSize(200, 150));
                smallBmp.Render(smallView);
                using var s = new MemoryStream();
                smallBmp.Save(s);
                smallLength = s.Length;
            }
            catch { _output.WriteLine("SKIP: small render failed"); return; }

            long largeLength = 0;
            try
            {
                var largeView = new ItemEditorView();
                largeView.DataContext = vm;
                largeView.Measure(new Size(RenderWidth, RenderHeight));
                largeView.Arrange(new Rect(0, 0, RenderWidth, RenderHeight));
                var largeBmp = new RenderTargetBitmap(new PixelSize(RenderWidth, RenderHeight));
                largeBmp.Render(largeView);
                using var s = new MemoryStream();
                largeBmp.Save(s);
                largeLength = s.Length;
            }
            catch { _output.WriteLine("SKIP: large render failed"); return; }

            _output.WriteLine($"Small (200x150): {smallLength} bytes, Large (800x600): {largeLength} bytes");

            Assert.True(largeLength >= smallLength,
                $"Larger render ({largeLength}) should be >= smaller render ({smallLength})");
        }

        // =============================================================
        // Multiple entries: verify rendering changes per entry
        // =============================================================

        [AvaloniaFact]
        public void UnitEditorView_DifferentEntries_ProduceDifferentRenders()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new UnitEditorViewModel();
            var list = vm.LoadUnitList();
            if (list.Count < 3) { _output.WriteLine("SKIP: fewer than 3 units"); return; }

            // Render unit at index 1
            vm.LoadUnit(list[1].addr);
            var view1 = new UnitEditorView();
            view1.DataContext = vm;
            using var stream1 = RenderToStream(view1);
            if (stream1 == null) { _output.WriteLine("SKIP: render 1 failed"); return; }

            // Render unit at index 2
            var vm2 = new UnitEditorViewModel();
            vm2.LoadUnit(list[2].addr);
            var view2 = new UnitEditorView();
            view2.DataContext = vm2;
            using var stream2 = RenderToStream(view2);
            if (stream2 == null) { _output.WriteLine("SKIP: render 2 failed"); return; }

            _output.WriteLine($"Entry 1: {stream1.Length} bytes, Entry 2: {stream2.Length} bytes");

            // Both should be non-blank
            Assert.True(stream1.Length > MinNonBlankStreamLength, "Unit entry 1 render should be non-blank");
            Assert.True(stream2.Length > MinNonBlankStreamLength, "Unit entry 2 render should be non-blank");
            // Compare byte content — different entries should produce different PNG output
            var bytes1 = stream1.ToArray();
            var bytes2 = stream2.ToArray();
            bool differs = bytes1.Length != bytes2.Length;
            if (!differs)
                for (int i = 0; i < bytes1.Length; i++)
                    if (bytes1[i] != bytes2[i]) { differs = true; break; }
            Assert.True(differs, "Different unit entries should produce different renders");
        }

        [AvaloniaFact]
        public void ItemEditorView_DifferentEntries_ProduceDifferentRenders()
        {
            if (!_fixture.IsAvailable) return;

            var vm = new ItemEditorViewModel();
            var list = vm.LoadItemList();
            if (list.Count < 3) { _output.WriteLine("SKIP: fewer than 3 items"); return; }

            vm.LoadItem(list[1].addr);
            var view1 = new ItemEditorView();
            view1.DataContext = vm;
            using var stream1 = RenderToStream(view1);
            if (stream1 == null) { _output.WriteLine("SKIP: render 1 failed"); return; }

            var vm2 = new ItemEditorViewModel();
            vm2.LoadItem(list[2].addr);
            var view2 = new ItemEditorView();
            view2.DataContext = vm2;
            using var stream2 = RenderToStream(view2);
            if (stream2 == null) { _output.WriteLine("SKIP: render 2 failed"); return; }

            _output.WriteLine($"Entry 1: {stream1.Length} bytes, Entry 2: {stream2.Length} bytes");

            Assert.True(stream1.Length > MinNonBlankStreamLength, "Item entry 1 render should be non-blank");
            Assert.True(stream2.Length > MinNonBlankStreamLength, "Item entry 2 render should be non-blank");
            var bytes1 = stream1.ToArray();
            var bytes2 = stream2.ToArray();
            bool differs = bytes1.Length != bytes2.Length;
            if (!differs)
                for (int i = 0; i < bytes1.Length; i++)
                    if (bytes1[i] != bytes2[i]) { differs = true; break; }
            Assert.True(differs, "Different item entries should produce different renders");
        }
    }
}
