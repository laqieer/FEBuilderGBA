using System;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Headless tests for the Item Shop ViewModel's decomp source-routing vector builders
    /// (#1347 Slice 5a): <see cref="ItemShopViewerViewModel.BuildVectorForWrite"/>,
    /// <see cref="ItemShopViewerViewModel.BuildVectorForAppend"/>, and
    /// <see cref="ItemShopViewerViewModel.BuildVectorForRemoveLast"/>. The VM is a plain
    /// (Avalonia-UI-free) class linked into Core.Tests, so it instantiates headlessly with
    /// a synthetic <see cref="CoreState.ROM"/>.
    ///
    /// The vector packs each entry as <c>(qty&lt;&lt;8)|id</c> u16. The shop list at ROM
    /// offset 0x1000 is laid out as consecutive (id,qty) byte pairs, terminated by id==0.
    /// </summary>
    [Collection("SharedState")]
    public class ItemShopViewerViewModelTests : IDisposable
    {
        readonly ROM _savedRom;
        const uint ShopAddr = 0x1000;

        public ItemShopViewerViewModelTests()
        {
            _savedRom = CoreState.ROM;
            CoreState.ROM = MakeRomWithShop();
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        // Shop at 0x1000: (id=0x05,qty=0x01), (id=0x02,qty=0x03), terminator id=0x00.
        static ROM MakeRomWithShop()
        {
            var rom = new ROM();
            byte[] data = new byte[0x2000];
            data[0x1000] = 0x05; data[0x1001] = 0x01;
            data[0x1002] = 0x02; data[0x1003] = 0x03;
            data[0x1004] = 0x00; // terminator
            rom.SwapNewROMDataDirect(data);
            return rom;
        }

        static ItemShopViewerViewModel VmWithShopLoaded()
        {
            var vm = new ItemShopViewerViewModel();
            vm.LoadShopItems(ShopAddr, 0, "Test Shop");
            return vm;
        }

        [Fact]
        public void BuildVectorForWrite_ReplacesSelectedSlot()
        {
            var vm = VmWithShopLoaded();
            // Select slot 1 (the 0x02/0x03 entry at addr 0x1002) and edit it.
            vm.LoadItemShop(0x1002);
            vm.ItemId = 0x09;
            vm.Quantity = 0x07;

            ushort[] vec = vm.BuildVectorForWrite();
            Assert.NotNull(vec);
            Assert.Equal(2, vec.Length);
            Assert.Equal((ushort)((0x01 << 8) | 0x05), vec[0]);   // slot 0 unchanged
            Assert.Equal((ushort)((0x07 << 8) | 0x09), vec[1]);   // slot 1 replaced
        }

        [Fact]
        public void BuildVectorForWrite_NoSlotSelected_ReturnsNull()
        {
            var vm = VmWithShopLoaded();
            // CurrentAddr is 0 (no slot loaded) → null.
            Assert.Null(vm.BuildVectorForWrite());
        }

        [Fact]
        public void BuildVectorForAppend_AddsDefaultEntry()
        {
            var vm = VmWithShopLoaded();
            ushort[] vec = vm.BuildVectorForAppend();
            Assert.NotNull(vec);
            Assert.Equal(3, vec.Length);
            Assert.Equal((ushort)((0x01 << 8) | 0x05), vec[0]);
            Assert.Equal((ushort)((0x03 << 8) | 0x02), vec[1]);
            Assert.Equal((ushort)((1 << 8) | 1), vec[2]);   // appended default id=1,qty=1
        }

        [Fact]
        public void BuildVectorForRemoveLast_DropsLastEntry()
        {
            var vm = VmWithShopLoaded();
            ushort[] vec = vm.BuildVectorForRemoveLast();
            Assert.NotNull(vec);
            Assert.Single(vec);
            Assert.Equal((ushort)((0x01 << 8) | 0x05), vec[0]);   // only slot 0 remains
        }

        [Fact]
        public void BuildVectorForRemoveLast_EmptyShop_ReturnsNull()
        {
            // Point the VM at an empty shop (immediate terminator).
            var rom = CoreState.ROM;
            rom.SwapNewROMDataDirect(new byte[0x2000]);   // all zero → 0x1000 is a terminator
            var vm = new ItemShopViewerViewModel();
            vm.LoadShopItems(ShopAddr, 0, "Empty");
            Assert.Null(vm.BuildVectorForRemoveLast());
        }

        [Fact]
        public void BuildVectors_NoShopSelected_ReturnNull()
        {
            var vm = new ItemShopViewerViewModel();   // CurrentShopAddr == 0
            Assert.Null(vm.BuildVectorForWrite());
            Assert.Null(vm.BuildVectorForAppend());
            Assert.Null(vm.BuildVectorForRemoveLast());
        }
    }
}
