using System;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests
{
    [Collection("SharedState")]
    public class MapEditorWriteInPlaceRomTests : IDisposable
    {
        readonly ROM? _savedRom;

        public MapEditorWriteInPlaceRomTests()
        {
            _savedRom = CoreState.ROM;
            CoreState.ROM = CreateRom();
        }

        public void Dispose()
        {
            CoreState.ROM = _savedRom;
        }

        static ROM CreateRom(int size = 0x8000)
        {
            byte[] data = Enumerable.Repeat((byte)0xAA, size).ToArray();
            for (int i = size / 2; i < size / 2 + 0x1000; i++)
                data[i] = 0x00;

            var rom = new ROM();
            Assert.True(rom.LoadLow("synthetic.gba", data, "NAZO"));
            return rom;
        }

        static byte[] LiteralLz77(byte seed, int uncompressedSize)
        {
            Assert.True(uncompressedSize >= 3);
            byte[] compressed = new byte[4 + ((uncompressedSize + 7) / 8) + uncompressedSize];
            compressed[0] = 0x10;
            compressed[1] = (byte)(uncompressedSize & 0xFF);
            compressed[2] = (byte)((uncompressedSize >> 8) & 0xFF);
            compressed[3] = (byte)((uncompressedSize >> 16) & 0xFF);
            int dst = 4;
            for (int written = 0; written < uncompressedSize;)
            {
                compressed[dst++] = 0x00;
                int count = Math.Min(8, uncompressedSize - written);
                for (int i = 0; i < count; i++)
                    compressed[dst++] = (byte)(seed + written + i);
                written += count;
            }
            return compressed;
        }

        static byte[] BuildMap(int width, int height, ushort fill)
        {
            byte[] map = new byte[2 + width * height * 2];
            map[0] = (byte)width;
            map[1] = (byte)height;
            for (int i = 0; i < width * height; i++)
            {
                int off = 2 + i * 2;
                map[off] = (byte)(fill & 0xFF);
                map[off + 1] = (byte)(fill >> 8);
            }
            return map;
        }

        static void SetPrivateField(object target, string name, object value)
        {
            var f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(f);
            f.SetValue(target, value);
        }

        static MapEditorViewModel CreateVmWithMap(uint pointerEntryAddr, uint oldAddr, byte[] oldBlob, int width, int height)
        {
            var rom = CoreState.ROM!;
            rom.write_p32(pointerEntryAddr, oldAddr);
            rom.write_range(oldAddr, oldBlob);

            var vm = new MapEditorViewModel { MapWidth = width, MapHeight = height };
            SetPrivateField(vm, "_cachedMapPointerEntryAddr", pointerEntryAddr);
            SetPrivateField(vm, "_cachedMapData", BuildMap(width, height, 0x0001));
            return vm;
        }

        [Fact]
        public void ApplyMapEdit_ReusesCurrentCompressedBlob_WhenNewPayloadFits()
        {
            const uint ptr = 0x240;
            const uint oldAddr = 0x1000;
            var vm = CreateVmWithMap(ptr, oldAddr, LiteralLz77(0x10, 128), 2, 2);

            bool ok = vm.ApplyMapEdit(0, 0, 0x0002, out string error, out uint writeAddr);

            Assert.True(ok, error ?? "(null error)");
            Assert.Equal(oldAddr, writeAddr);
            Assert.Equal(oldAddr, CoreState.ROM!.p32(ptr));
            Assert.Equal(0x0002, vm.GetMapDataSnapshot()[2] | (vm.GetMapDataSnapshot()[3] << 8));
        }

        [Fact]
        public void ApplyMapGrid_ReusesCurrentCompressedBlob_WhenNewPayloadFits()
        {
            const uint ptr = 0x244;
            const uint oldAddr = 0x1100;
            var vm = CreateVmWithMap(ptr, oldAddr, LiteralLz77(0x20, 128), 2, 2);
            ushort[] mars = { 0x0002, 0x0003, 0x0004, 0x0005 };

            bool ok = vm.ApplyMapGrid(mars, 2, 2, out string error, out uint writeAddr);

            Assert.True(ok, error ?? "(null error)");
            Assert.Equal(oldAddr, writeAddr);
            Assert.Equal(oldAddr, CoreState.ROM!.p32(ptr));
            byte[] after = vm.GetMapDataSnapshot();
            Assert.Equal(0x0005, after[8] | (after[9] << 8));
        }

        [Fact]
        public void ApplyMapResize_GrowthRelocatesThroughSharedCoreHelperAndFreesOldPrivateBlob()
        {
            const uint ptr = 0x248;
            const uint oldAddr = 0x1200;
            byte[] oldBlob = LiteralLz77(0x30, 8);
            uint oldSize = U.Padding4(LZ77.getCompressedSize(oldBlob, 0));
            var vm = CreateVmWithMap(ptr, oldAddr, oldBlob, 15, 10);

            bool ok = vm.ApplyMapResize(0, 0, 1, 0, 0, out string error, out uint writeAddr);

            Assert.True(ok, error ?? "(null error)");
            Assert.NotEqual(oldAddr, writeAddr);
            Assert.Equal(writeAddr, CoreState.ROM!.p32(ptr));
            Assert.Equal(16, vm.MapWidth);
            Assert.Equal(10, vm.MapHeight);
            for (uint i = 0; i < oldSize; i++)
                Assert.Equal(0x00, CoreState.ROM.Data[oldAddr + i]);
        }
    }
}
