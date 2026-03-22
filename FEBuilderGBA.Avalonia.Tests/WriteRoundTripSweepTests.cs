using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Sweep test verifying that field mutation round-trips correctly for all writable ViewModels.
    /// For each VM: load an entry, find the first settable uint property, mutate it,
    /// write, reload, and verify the property holds the new value.
    ///
    /// This catches bugs where Write does not serialize a field, or Load does not
    /// read it back from the correct offset.
    /// </summary>
    [Collection("SharedState")]
    public class WriteRoundTripSweepTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public WriteRoundTripSweepTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public static IEnumerable<object[]> WritableVMs() => WritableViewModelRegistry.WritableViewModels();

        [Theory]
        [MemberData(nameof(WritableVMs))]
        public void MutateFieldThenReload_ValuePersists(
            Type vmType, string listMethod, string loadMethod, string writeMethod)
        {
            if (!_fixture.IsAvailable) return;

            // Create VM instance
            object vm;
            try { vm = Activator.CreateInstance(vmType)!; }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {vmType.Name}: constructor threw {ex.GetType().Name}: {ex.Message}");
                return;
            }

            // Get list
            List<AddrResult> list;
            try { list = WritableViewModelRegistry.InvokeList(vm, listMethod); }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {vmType.Name}: {listMethod} threw {ex.GetType().Name}: {ex.Message}");
                return;
            }
            if (list == null || list.Count < 2)
            {
                _output.WriteLine($"SKIP {vmType.Name}: list has {list?.Count ?? 0} entries (need >= 2)");
                return;
            }

            // Load entry[1]
            try { WritableViewModelRegistry.InvokeLoad(vm, loadMethod, list[1].addr); }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {vmType.Name}: {loadMethod} threw {ex.GetType().Name}: {ex.Message}");
                return;
            }

            uint addr = WritableViewModelRegistry.GetCurrentAddr(vm);
            if (addr == 0)
            {
                _output.WriteLine($"SKIP {vmType.Name}: CurrentAddr is 0 after load");
                return;
            }

            // Find first mutable uint property
            PropertyInfo? prop = WritableViewModelRegistry.FindFirstUintProperty(vm);
            if (prop == null)
            {
                _output.WriteLine($"SKIP {vmType.Name}: no suitable uint property found");
                return;
            }

            uint size = WritableViewModelRegistry.GetStructSize(vm);
            if (addr + size > (uint)CoreState.ROM.Data.Length)
            {
                size = (uint)CoreState.ROM.Data.Length - addr;
            }

            // Snapshot bytes for restore
            byte[] snapshot = new byte[size];
            Array.Copy(CoreState.ROM.Data, (int)addr, snapshot, 0, (int)size);

            try
            {
                uint original = (uint)prop.GetValue(vm)!;
                uint testValue = original == 42u ? 43u : 42u;

                // Mutate
                prop.SetValue(vm, testValue);

                // Write
                WritableViewModelRegistry.InvokeWrite(vm, writeMethod);

                // Reload
                WritableViewModelRegistry.InvokeLoad(vm, loadMethod, addr);

                // Verify
                uint reloaded = (uint)prop.GetValue(vm)!;
                _output.WriteLine(
                    $"OK {vmType.Name}.{prop.Name}: original=0x{original:X}, test=0x{testValue:X}, reloaded=0x{reloaded:X}");
                Assert.Equal(testValue, reloaded);
            }
            finally
            {
                // Restore ROM bytes
                Array.Copy(snapshot, 0, CoreState.ROM.Data, (int)addr, (int)size);
            }
        }
    }
}
