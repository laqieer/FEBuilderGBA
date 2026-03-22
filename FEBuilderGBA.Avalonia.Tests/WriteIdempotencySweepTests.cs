using System;
using System.Collections.Generic;
using System.Linq;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Sweep test verifying that Load+Write is a no-op for all writable ViewModels.
    /// For each VM: load an entry, call Write without modifying any property,
    /// and assert that every ROM byte in the struct region is unchanged.
    ///
    /// This catches bugs where Write methods use incorrect offsets, widths,
    /// sign-extension, or endianness.
    /// </summary>
    [Collection("SharedState")]
    public class WriteIdempotencySweepTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public WriteIdempotencySweepTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public static IEnumerable<object[]> WritableVMs() => WritableViewModelRegistry.WritableViewModels();

        [Theory]
        [MemberData(nameof(WritableVMs))]
        public void LoadThenWrite_DoesNotCorruptRomBytes(
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

            // Get list — skip if empty or throws (version mismatch)
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

            // Load entry[1] (first real entry, skipping 0 which is often null/header)
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

            uint size = WritableViewModelRegistry.GetStructSize(vm);
            // Safety: ensure we don't read past ROM
            if (addr + size > (uint)CoreState.ROM.Data.Length)
            {
                size = (uint)CoreState.ROM.Data.Length - addr;
            }

            // Snapshot bytes before write
            byte[] snapshot = new byte[size];
            Array.Copy(CoreState.ROM.Data, (int)addr, snapshot, 0, (int)size);

            try
            {
                // Write without modification — should be a no-op
                WritableViewModelRegistry.InvokeWrite(vm, writeMethod);

                // Compare every byte
                int mismatches = 0;
                for (int i = 0; i < (int)size; i++)
                {
                    byte expected = snapshot[i];
                    byte actual = CoreState.ROM.Data[(int)addr + i];
                    if (expected != actual)
                    {
                        if (mismatches < 10) // log first 10 mismatches
                        {
                            _output.WriteLine(
                                $"  MISMATCH at offset +0x{i:X2}: expected 0x{expected:X2}, got 0x{actual:X2}");
                        }
                        mismatches++;
                    }
                }

                _output.WriteLine(
                    $"OK {vmType.Name}: addr=0x{addr:X}, size={size}, mismatches={mismatches}");
                Assert.Equal(0, mismatches);
            }
            finally
            {
                // Restore ROM bytes
                Array.Copy(snapshot, 0, CoreState.ROM.Data, (int)addr, (int)size);
            }
        }
    }
}
