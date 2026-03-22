using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Sweep test verifying that Undo restores ROM bytes for all writable ViewModels.
    /// For each VM: load entry, snapshot bytes, mutate a field, write via UndoService,
    /// verify bytes changed, then RunUndo and verify bytes restored.
    ///
    /// This catches bugs where Write bypasses the undo scope or where undo does not
    /// fully revert all modified bytes.
    /// </summary>
    [Collection("SharedState")]
    public class UndoSweepTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public UndoSweepTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public static IEnumerable<object[]> WritableVMs() => WritableViewModelRegistry.WritableViewModels();

        [Theory]
        [MemberData(nameof(WritableVMs))]
        public void WriteAndUndo_RestoresOriginalBytes(
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

            // Snapshot bytes for safety-net restore
            byte[] snapshot = new byte[size];
            Array.Copy(CoreState.ROM.Data, (int)addr, snapshot, 0, (int)size);

            try
            {
                uint original = (uint)prop.GetValue(vm)!;
                uint testValue = original == 42u ? 43u : 42u;

                // Begin undo scope, mutate, write, commit
                var svc = new UndoService();
                svc.Begin("sweep-" + vmType.Name);
                prop.SetValue(vm, testValue);
                WritableViewModelRegistry.InvokeWrite(vm, writeMethod);
                svc.Commit();

                // Verify ROM bytes actually changed (at least one byte should differ)
                bool anyChanged = false;
                for (int i = 0; i < (int)size; i++)
                {
                    if (snapshot[i] != CoreState.ROM.Data[(int)addr + i])
                    {
                        anyChanged = true;
                        break;
                    }
                }

                if (!anyChanged)
                {
                    _output.WriteLine(
                        $"SKIP {vmType.Name}.{prop.Name}: write did not change any bytes " +
                        $"(original=0x{original:X}, test=0x{testValue:X})");
                    return;
                }

                // Undo
                CoreState.Undo.RunUndo();

                // Verify bytes restored
                int mismatches = 0;
                for (int i = 0; i < (int)size; i++)
                {
                    if (snapshot[i] != CoreState.ROM.Data[(int)addr + i])
                    {
                        if (mismatches < 10)
                        {
                            _output.WriteLine(
                                $"  UNDO MISMATCH at offset +0x{i:X2}: " +
                                $"expected 0x{snapshot[i]:X2}, got 0x{CoreState.ROM.Data[(int)addr + i]:X2}");
                        }
                        mismatches++;
                    }
                }

                _output.WriteLine(
                    $"OK {vmType.Name}.{prop.Name}: undo restored, mismatches={mismatches}");
                Assert.Equal(0, mismatches);
            }
            finally
            {
                // Safety-net: always restore ROM bytes
                Array.Copy(snapshot, 0, CoreState.ROM.Data, (int)addr, (int)size);
            }
        }
    }
}
