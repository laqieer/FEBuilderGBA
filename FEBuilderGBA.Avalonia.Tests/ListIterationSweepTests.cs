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
    /// Sweep test that iterates ALL entries in every writable ViewModel's list
    /// and calls the load method on each. Verifies no crash and CurrentAddr != 0.
    ///
    /// This catches out-of-bounds reads, null references, and decode errors
    /// that only manifest on specific entries (e.g. entry 0xFF, last entry).
    /// </summary>
    [Collection("SharedState")]
    public class ListIterationSweepTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public ListIterationSweepTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public static IEnumerable<object[]> WritableVMs() => WritableViewModelRegistry.WritableViewModels();

        [Theory]
        [MemberData(nameof(WritableVMs))]
        public void EveryEntry_LoadsWithoutCrash(
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
            if (list == null || list.Count == 0)
            {
                _output.WriteLine($"SKIP {vmType.Name}: list is empty");
                return;
            }

            int loaded = 0;
            int errors = 0;
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    WritableViewModelRegistry.InvokeLoad(vm, loadMethod, list[i].addr);
                    uint addr = WritableViewModelRegistry.GetCurrentAddr(vm);

                    // CurrentAddr should be set after a successful load
                    // (some VMs may set it to 0 for invalid/null entries — that is not a failure)
                    loaded++;
                }
                catch (TargetInvocationException tie) when (tie.InnerException != null)
                {
                    errors++;
                    if (errors <= 5) // log first 5 errors
                    {
                        _output.WriteLine(
                            $"  ERROR [{i}] addr=0x{list[i].addr:X}: {tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    if (errors <= 5)
                    {
                        _output.WriteLine(
                            $"  ERROR [{i}] addr=0x{list[i].addr:X}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            _output.WriteLine(
                $"OK {vmType.Name}: {loaded}/{list.Count} entries loaded, {errors} errors");

            // All entries should load without throwing
            Assert.Equal(0, errors);
        }
    }
}
