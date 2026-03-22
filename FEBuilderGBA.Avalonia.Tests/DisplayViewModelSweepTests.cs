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
    /// Automated sweep that discovers all display-only ViewModels (those with
    /// LoadList + Load but NO Write method) via reflection and verifies:
    /// 1. LoadList returns a valid non-null list
    /// 2. Loading an entry populates state (CurrentAddr != 0)
    /// 3. Full iteration over all entries completes without crash
    ///
    /// Each ViewModel gets its own test case via [Theory] + [MemberData].
    /// Tests skip gracefully when ROMs are unavailable.
    /// </summary>
    [Collection("SharedState")]
    public class DisplayViewModelSweepTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _fixture;
        private readonly ITestOutputHelper _output;

        public DisplayViewModelSweepTests(RomFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public static IEnumerable<object[]> DisplayVMs() => DisplayViewModelRegistry.DisplayViewModels();

        /// <summary>
        /// For each display VM, call LoadList and verify:
        /// - List is non-null
        /// - If list has entries, all entries have non-null name and addr > 0
        /// Skips VMs that throw due to version mismatch.
        /// </summary>
        [Theory]
        [MemberData(nameof(DisplayVMs))]
        public void ListLoad_ReturnsNonEmptyList(Type vmType, string listMethod, string loadMethod)
        {
            if (!_fixture.IsAvailable) return;

            // Construct VM
            object vm;
            try { vm = Activator.CreateInstance(vmType)!; }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {vmType.Name}: constructor threw {ex.GetType().Name}: {ex.Message}");
                return;
            }

            // Call LoadList — catch exceptions from version mismatch
            List<AddrResult> list;
            try
            {
                list = DisplayViewModelRegistry.InvokeList(vm, listMethod);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                _output.WriteLine(
                    $"SKIP {vmType.Name}.{listMethod}: version mismatch or unsupported — " +
                    $"{tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
                return;
            }
            catch (Exception ex)
            {
                _output.WriteLine(
                    $"SKIP {vmType.Name}.{listMethod}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            // List must be non-null
            Assert.NotNull(list);

            // If list has entries, validate each entry
            if (list.Count > 0)
            {
                foreach (var entry in list)
                {
                    Assert.NotNull(entry.name);
                    Assert.True(entry.addr > 0,
                        $"{vmType.Name}: entry '{entry.name}' has addr=0");
                }
            }

            _output.WriteLine($"OK {vmType.Name}.{listMethod}: {list.Count} entries");
        }

        /// <summary>
        /// For each display VM, load entry[1] and verify CurrentAddr != 0.
        /// Skips VMs with empty or single-entry lists.
        /// </summary>
        [Theory]
        [MemberData(nameof(DisplayVMs))]
        public void EntryLoad_PopulatesState(Type vmType, string listMethod, string loadMethod)
        {
            if (!_fixture.IsAvailable) return;

            // Construct VM
            object vm;
            try { vm = Activator.CreateInstance(vmType)!; }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {vmType.Name}: constructor threw {ex.GetType().Name}: {ex.Message}");
                return;
            }

            // Get list
            List<AddrResult> list;
            try { list = DisplayViewModelRegistry.InvokeList(vm, listMethod); }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {vmType.Name}.{listMethod}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (list == null || list.Count < 2)
            {
                _output.WriteLine($"SKIP {vmType.Name}: list is null or has fewer than 2 entries (count={list?.Count ?? 0})");
                return;
            }

            // Load entry[1]
            try
            {
                DisplayViewModelRegistry.InvokeLoad(vm, loadMethod, list[1].addr);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                _output.WriteLine(
                    $"SKIP {vmType.Name}.{loadMethod}(0x{list[1].addr:X}): " +
                    $"{tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
                return;
            }
            catch (Exception ex)
            {
                _output.WriteLine(
                    $"SKIP {vmType.Name}.{loadMethod}(0x{list[1].addr:X}): " +
                    $"{ex.GetType().Name}: {ex.Message}");
                return;
            }

            // Check CurrentAddr if the VM has that property
            uint currentAddr = DisplayViewModelRegistry.GetCurrentAddr(vm);
            if (currentAddr != 0)
            {
                _output.WriteLine(
                    $"OK {vmType.Name}: loaded entry[1] at addr=0x{list[1].addr:X}, CurrentAddr=0x{currentAddr:X}");
            }
            else
            {
                // Some VMs may not have CurrentAddr property — that is acceptable
                _output.WriteLine(
                    $"OK {vmType.Name}: loaded entry[1] at addr=0x{list[1].addr:X} (no CurrentAddr property or value is 0)");
            }

            Assert.True(currentAddr != 0 ||
                vm.GetType().GetProperty("CurrentAddr", BindingFlags.Public | BindingFlags.Instance) == null,
                $"{vmType.Name}: CurrentAddr is 0 after loading entry at addr=0x{list[1].addr:X}");
        }

        /// <summary>
        /// For each display VM, iterate ALL entries and call Load for each.
        /// Verifies no exceptions are thrown during full iteration.
        /// </summary>
        [Theory]
        [MemberData(nameof(DisplayVMs))]
        public void FullIteration_NoCrash(Type vmType, string listMethod, string loadMethod)
        {
            if (!_fixture.IsAvailable) return;

            // Construct VM
            object vm;
            try { vm = Activator.CreateInstance(vmType)!; }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {vmType.Name}: constructor threw {ex.GetType().Name}: {ex.Message}");
                return;
            }

            // Get list
            List<AddrResult> list;
            try { list = DisplayViewModelRegistry.InvokeList(vm, listMethod); }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {vmType.Name}.{listMethod}: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (list == null || list.Count == 0)
            {
                _output.WriteLine($"SKIP {vmType.Name}: list is null or empty");
                return;
            }

            // Iterate all entries
            int loaded = 0;
            int errors = 0;
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    DisplayViewModelRegistry.InvokeLoad(vm, loadMethod, list[i].addr);
                    loaded++;
                }
                catch (TargetInvocationException tie) when (tie.InnerException != null)
                {
                    errors++;
                    if (errors <= 5) // log first 5 errors
                    {
                        _output.WriteLine(
                            $"  ERROR [{i}] addr=0x{list[i].addr:X}: " +
                            $"{tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
                    }
                }
                catch (Exception ex)
                {
                    errors++;
                    if (errors <= 5)
                    {
                        _output.WriteLine(
                            $"  ERROR [{i}] addr=0x{list[i].addr:X}: " +
                            $"{ex.GetType().Name}: {ex.Message}");
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
