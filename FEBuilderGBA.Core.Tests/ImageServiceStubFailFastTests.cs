using System;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    public sealed class ImageServiceStubFailFastTests
    {
        [Fact]
        public void MinimalImageService_UnsupportedLoadImageThrowsMemberName()
        {
            var ex = Assert.Throws<NotSupportedException>(() => new MinimalImageService().LoadImage("unused.png"));
            Assert.Contains(nameof(IImageService.LoadImage), ex.Message);
        }

        [Fact]
        public void MapConvertImageService_UnsupportedCreateImageThrowsMemberName()
        {
            var ex = Assert.Throws<NotSupportedException>(() => new MapConvertImageService().CreateImage(1, 1));
            Assert.Contains(nameof(IImageService.CreateImage), ex.Message);
        }

        [Fact]
        public void StubImageServiceForDecomp_UnsupportedEncodeThrowsMemberName()
        {
            var service = new StubImageServiceForDecomp();
            using var image = service.CreateIndexedImage(1, 1, Array.Empty<byte>(), 0);

            var ex = Assert.Throws<NotSupportedException>(() => service.Encode4bppTiles(image));
            Assert.Contains(nameof(IImageService.Encode4bppTiles), ex.Message);
        }

        [Fact]
        public void FakeReduceImageService_UnsupportedLoadImageFromBytesThrowsMemberName()
        {
            var service = new FakeReduceImageService(Array.Empty<byte>(), 1, 1);

            var ex = Assert.Throws<NotSupportedException>(() => service.LoadImageFromBytes(Array.Empty<byte>()));
            Assert.Contains(nameof(IImageService.LoadImageFromBytes), ex.Message);
        }
    }
}
