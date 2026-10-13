// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public sealed partial class ImageUnitMoveIconDisplayTests : IClassFixture<RealBitmapTestHost>
{
    readonly RealBitmapTestHost host;

    public ImageUnitMoveIconDisplayTests(RealBitmapTestHost host) => this.host = host;

    [Theory]
    [InlineData(0, false)]
    [InlineData(SelectedRow, false)]
    [InlineData(0, true)]
    [InlineData(SelectedRow, true)]
    public Task MoveIconLoader_UsesStableRowTag_ForActualThumbnailPixels(int row, bool reorderedAndRenamed)
    {
        return WithRealBitmapApp(() =>
        {
            using var state = new TestState();
            ROM rom = CreateRom();
            byte[] before = SHA256.HashData(rom.Data);
            var items = new ImageUnitMoveIconViewModel().LoadList();
            var original = items[row];
            int index = row;
            if (reorderedAndRenamed)
            {
                items = new List<AddrResult> { new(original.addr, "FF display-only label", original.tag) };
                index = 0;
            }

            using Bitmap? thumbnail = ListIconLoaders.MoveIconLoader(items, index);

            Assert.NotNull(thumbnail);
            Assert.Equal(new PixelSize(32, 32), thumbnail.PixelSize);
            Assert.Equal(ExpectedThumbnailPixels(rom, row), ReadBitmapRgba(thumbnail));
            Assert.Equal(MoveBase + (uint)row * 8, items[index].addr);
            Assert.Equal((uint)row, items[index].tag);
            Assert.Equal(before, SHA256.HashData(rom.Data));
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(SelectedRow)]
    public Task ViewList_UsesCorrectActualThumbnails_ForZeroBasedRows(int row)
    {
        return WithRealBitmapApp(() =>
        {
            using var state = new TestState();
            ROM rom = CreateRom();
            byte[] before = SHA256.HashData(rom.Data);
            var view = new ImageUnitMoveIconView();
            view.Show();
            try
            {
                var list = view.FindControl<AddressListControl>("EntryList")!;
                var listBox = list.FindControl<ListBox>("AddressList")!;
                var displayed = Assert.IsType<AddressListItem>(listBox.Items[row]);
                Assert.Equal(row == SelectedRow ? "0E AA" : "01 A", displayed.Text);
                Assert.NotNull(displayed.Icon);
                Assert.Equal(new PixelSize(32, 32), displayed.Icon.PixelSize);
                Assert.Equal(ExpectedThumbnailPixels(rom, row), ReadBitmapRgba(displayed.Icon));
                Assert.True(list.SelectByIndex(row));
                var vm = Assert.IsType<ImageUnitMoveIconViewModel>(view.DataViewModel);
                Assert.Equal(row, vm.CurrentIndex);
                Assert.Equal(MoveBase + (uint)row * 8, vm.CurrentAddr);
                Assert.Equal((uint)row, list.SelectedItem!.tag);
                Assert.Equal(before, SHA256.HashData(rom.Data));
            }
            finally
            {
                view.Close();
            }
        });
    }

    [Fact]
    public async Task Host_StartupFactoryFailure_PropagatesAndRejectsDispatch()
    {
        var expected = new InvalidOperationException("Bitmap app factory failed before platform setup.");
        var failedHost = new RealBitmapTestHost(() => throw expected);

        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(failedHost.InitializeAsync));
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(failedHost.DisposeAsync));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => failedHost.RunAsync(() => Assert.Fail("Dispatch after failed startup.")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_DispatchFailure_PreservesExceptionAndAllowsActualBitmapAction(bool actionTimeout)
    {
        Exception expected = actionTimeout
            ? new TimeoutException("Distinctive bitmap action timeout.")
            : new InvalidOperationException("Distinctive bitmap action failure.");
        Assert.Same(expected, await Assert.ThrowsAnyAsync<Exception>(() => host.RunAsync(() => throw expected)));

        await MoveIconLoader_UsesStableRowTag_ForActualThumbnailPixels(SelectedRow, true);
    }

    Task WithRealBitmapApp(Action action) => host.RunAsync(action);

    static byte[] ExpectedThumbnailPixels(ROM rom, int row)
    {
        uint color = rom.u16(rom.RomInfo.unit_icon_palette_address + (uint)(row % 15 + 1) * 2);
        byte[] pixels = new byte[32 * 32 * 4];
        for (int pixel = 0; pixel < 32 * 32; pixel++)
        {
            pixels[pixel * 4] = (byte)((color & 0x1F) << 3);
            pixels[pixel * 4 + 1] = (byte)(((color >> 5) & 0x1F) << 3);
            pixels[pixel * 4 + 2] = (byte)(((color >> 10) & 0x1F) << 3);
            pixels[pixel * 4 + 3] = 255;
        }

        return pixels;
    }

    static byte[] ReadBitmapRgba(Bitmap bitmap)
    {
        byte[] pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        var buffer = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height),
                buffer.AddrOfPinnedObject(), pixels.Length, bitmap.PixelSize.Width * 4);
        }
        finally
        {
            buffer.Free();
        }
        if (bitmap.Format == PixelFormat.Bgra8888)
        {
            for (int offset = 0; offset < pixels.Length; offset += 4)
                (pixels[offset], pixels[offset + 2]) = (pixels[offset + 2], pixels[offset]);
        }
        else
        {
            Assert.Equal(PixelFormat.Rgba8888, bitmap.Format);
        }
        return pixels;
    }

    public sealed class ImageUnitMoveIconThumbnailTestApp : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<ImageUnitMoveIconThumbnailTestApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .UseSkia();
    }
}
