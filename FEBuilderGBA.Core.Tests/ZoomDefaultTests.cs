using Xunit;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests that verify default zoom levels are 1x across Avalonia ViewModels.
    /// Relates to issue #183: default zoom should be 1x, not 2x.
    /// </summary>
    public class ZoomDefaultTests
    {
        [Fact]
        public void ImageViewerViewModel_DefaultZoom_Is1()
        {
            var vm = new ImageViewerViewModel();
            Assert.Equal(1, vm.Zoom);
        }

        [Fact]
        public void ImageViewerViewModel_ZoomCanBeChanged()
        {
            var vm = new ImageViewerViewModel();
            vm.Zoom = 4;
            Assert.Equal(4, vm.Zoom);
        }

        [Fact]
        public void ImageViewerViewModel_ZoomChangeRaisesPropertyChanged()
        {
            var vm = new ImageViewerViewModel();
            string? changedProp = null;
            vm.PropertyChanged += (_, e) => changedProp = e.PropertyName;

            vm.Zoom = 3;
            Assert.Equal("Zoom", changedProp);
        }

        [Fact]
        public void ImageViewerViewModel_SameZoomDoesNotRaisePropertyChanged()
        {
            var vm = new ImageViewerViewModel();
            // Default is 1, setting to 1 again should not fire
            string? changedProp = null;
            vm.PropertyChanged += (_, e) => changedProp = e.PropertyName;

            vm.Zoom = 1;
            Assert.Null(changedProp);
        }

        [Fact]
        public void GraphicsToolViewViewModel_DefaultZoom_Is1()
        {
            var vm = new GraphicsToolViewViewModel();
            Assert.Equal(1, vm.Zoom);
        }

        [Fact]
        public void GraphicsToolViewViewModel_ZoomCanBeChanged()
        {
            var vm = new GraphicsToolViewViewModel();
            vm.Zoom = 8;
            Assert.Equal(8, vm.Zoom);
        }

        [Fact]
        public void GraphicsToolViewViewModel_ZoomChangeRaisesPropertyChanged()
        {
            var vm = new GraphicsToolViewViewModel();
            string? changedProp = null;
            vm.PropertyChanged += (_, e) => changedProp = e.PropertyName;

            vm.Zoom = 5;
            Assert.Equal("Zoom", changedProp);
        }
    }
}
