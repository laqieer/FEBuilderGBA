using System.Collections.Generic;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Issue #364 regression tests. Verifies that ItemEditorViewModel.LoadItemList()
    /// returns the WinForms-equivalent count for every ROM variant, NOT the buggy
    /// hardcoded 256 that produced dummy/weird entries at the end of the list.
    ///
    /// Expected counts (verified against legacy WinForms ItemForm and StructExportCore):
    ///   FE6   = 128
    ///   FE7J  = 159
    ///   FE7U  = 159
    ///   FE8J  = 206
    ///   FE8U  = 206
    /// </summary>
    [Collection("SharedState")]
    public class ItemEditorListCountTests
    {
        readonly ITestOutputHelper _output;

        public ItemEditorListCountTests(ITestOutputHelper output)
        {
            _output = output;
        }

        static int CountFor(string version)
        {
            string? path = TestRomLocator.FindRom(version);
            if (path == null) return -1; // skip sentinel

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                if (!rom.Load(path, out _)) return -1;
                CoreState.ROM = rom;

                if (version == "FE6")
                {
                    var vm = new ItemFE6ViewModel();
                    return vm.LoadItemList().Count;
                }
                else
                {
                    var vm = new ItemEditorViewModel();
                    return vm.LoadItemList().Count;
                }
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void FE8U_LoadItemList_Returns206_NotDummyPadded()
        {
            int count = CountFor("FE8U");
            if (count < 0)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }
            Assert.Equal(206, count);
            Assert.NotEqual(256, count); // explicit regression guard
        }

        [Fact]
        public void FE8J_LoadItemList_Returns206()
        {
            int count = CountFor("FE8J");
            if (count < 0)
            {
                _output.WriteLine("SKIP: FE8J.gba not found");
                return;
            }
            Assert.Equal(206, count);
        }

        [Fact]
        public void FE7U_LoadItemList_Returns159()
        {
            int count = CountFor("FE7U");
            if (count < 0)
            {
                _output.WriteLine("SKIP: FE7U.gba not found");
                return;
            }
            Assert.Equal(159, count);
        }

        [Fact]
        public void FE7J_LoadItemList_Returns159()
        {
            int count = CountFor("FE7J");
            if (count < 0)
            {
                _output.WriteLine("SKIP: FE7J.gba not found");
                return;
            }
            Assert.Equal(159, count);
        }

        [Fact]
        public void FE6_LoadItemList_Returns128_ViaItemFE6ViewModel()
        {
            int count = CountFor("FE6");
            if (count < 0)
            {
                _output.WriteLine("SKIP: FE6.gba not found");
                return;
            }
            Assert.Equal(128, count);
        }

        [Fact]
        public void NoRom_LoadItemList_ReturnsEmpty()
        {
            var savedRom = CoreState.ROM;
            try
            {
                CoreState.ROM = null;
                var vm = new ItemEditorViewModel();
                List<AddrResult> list = vm.LoadItemList();
                Assert.Empty(list);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }

        [Fact]
        public void ListParityHelper_BuildItemList_MatchesViewModelCount_FE8U()
        {
            string? path = TestRomLocator.FindRom("FE8U");
            if (path == null)
            {
                _output.WriteLine("SKIP: FE8U.gba not found");
                return;
            }

            var savedRom = CoreState.ROM;
            try
            {
                var rom = new ROM();
                if (!rom.Load(path, out _))
                {
                    _output.WriteLine("SKIP: FE8U.gba failed to load");
                    return;
                }
                CoreState.ROM = rom;

                // Reflect into ListParityHelper.BuildItemList(ROM rom) (private static).
                var method = typeof(ListParityHelper).GetMethod(
                    "BuildItemList",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Assert.NotNull(method);
                var parityList = (List<AddrResult>)method!.Invoke(null, new object?[] { rom })!;

                var vm = new ItemEditorViewModel();
                int vmCount = vm.LoadItemList().Count;

                Assert.Equal(vmCount, parityList.Count);
                Assert.Equal(206, parityList.Count);
            }
            finally
            {
                CoreState.ROM = savedRom;
            }
        }
    }
}
