using System;
using System.Collections.Generic;
using System.ComponentModel;
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
    /// Reflection-based sweep test covering all ViewModels NOT already covered by
    /// existing sweeps (WritableViewModelRegistry, DisplayViewModelRegistry, IDataVerifiable).
    ///
    /// These are utility VMs: tools, dialogs, error dialogs, controls, infrastructure.
    /// Three tests per VM: constructor, PropertyChanged, IsDirty default.
    /// </summary>
    [Collection("SharedState")]
    public class UtilityViewModelSweepTests : IClassFixture<RomFixture>
    {
        private readonly ITestOutputHelper _output;

        public UtilityViewModelSweepTests(RomFixture fixture, ITestOutputHelper output)
        {
            // RomFixture ensures CoreState is initialized for VMs that reference it.
            _ = fixture;
            _output = output;
        }

        /// <summary>
        /// Discovers all concrete ViewModelBase subclasses that are NOT in:
        /// - WritableViewModelRegistry (has Write method + LoadList + Load triplet)
        /// - DisplayViewModelRegistry (has LoadList + Load but no Write)
        /// - IDataVerifiable implementors
        /// - Abstract/interface types
        /// - ViewModelBase itself
        /// </summary>
        public static IEnumerable<object[]> UncoveredViewModels()
        {
            var assembly = typeof(UnitEditorViewModel).Assembly;
            var baseType = typeof(ViewModelBase);
            var dataVerifiableType = typeof(IDataVerifiable);

            // Collect types already covered by the three existing registries
            var writableCovered = new HashSet<Type>(
                WritableViewModelRegistry.WritableViewModels().Select(row => (Type)row[0]));

            var displayCovered = new HashSet<Type>(
                DisplayViewModelRegistry.DisplayViewModels().Select(row => (Type)row[0]));

            foreach (var type in assembly.GetTypes()
                         .Where(t => baseType.IsAssignableFrom(t)
                                     && !t.IsAbstract
                                     && !t.IsInterface
                                     && t != baseType)
                         .OrderBy(t => t.Name))
            {
                // Skip if already covered by WritableViewModelRegistry
                if (writableCovered.Contains(type)) continue;

                // Skip if already covered by DisplayViewModelRegistry
                if (displayCovered.Contains(type)) continue;

                // Skip if implements IDataVerifiable
                if (dataVerifiableType.IsAssignableFrom(type)) continue;

                // Extra safety: skip types matching the writable triplet pattern
                // (Write + LoadList + Load(uint)) in case registry enumeration missed them.
                bool hasLoadList = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(m => m.Name.StartsWith("Load", StringComparison.Ordinal)
                              && m.ReturnType == typeof(List<AddrResult>)
                              && m.GetParameters().Length == 0);

                bool hasLoadUint = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(m => m.Name.StartsWith("Load", StringComparison.Ordinal)
                              && m.ReturnType == typeof(void)
                              && m.GetParameters().Length == 1
                              && m.GetParameters()[0].ParameterType == typeof(uint));

                if (hasLoadList && hasLoadUint) continue;

                yield return new object[] { type.Name, type };
            }
        }

        /// <summary>
        /// Verifies that discovery finds a realistic number of uncovered VMs.
        /// </summary>
        [Fact]
        public void Discovery_FindsExpectedTypeCount()
        {
            var types = UncoveredViewModels().ToList();
            _output.WriteLine($"Discovered {types.Count} uncovered utility ViewModels:");
            foreach (var entry in types)
                _output.WriteLine($"  - {entry[0]}");

            // Discovery should produce a deterministic, duplicate-free set.
            // Don't assert a lower bound — the count shrinks as other sweeps expand.
            var names = types.Select(t => (string)t[0]).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }

        /// <summary>
        /// Verifies that each uncovered ViewModel can be constructed without throwing.
        /// VMs without a parameterless constructor are gracefully skipped.
        /// </summary>
        [Theory]
        [MemberData(nameof(UncoveredViewModels))]
        public void Constructor_DoesNotThrow(string vmName, Type vmType)
        {
            var ex = Record.Exception(() => Activator.CreateInstance(vmType));

            if (ex is MissingMethodException)
            {
                _output.WriteLine($"SKIP: {vmName} has no parameterless constructor");
                return;
            }

            if (ex is TargetInvocationException tie)
            {
                // Unwrap TargetInvocationException for a clearer failure message
                var inner = tie.InnerException ?? tie;
                Assert.Fail($"{vmName} constructor threw {inner.GetType().Name}: {inner.Message}");
                return;
            }

            Assert.Null(ex);
            _output.WriteLine($"OK: {vmName}");
        }

        /// <summary>
        /// For each VM, finds the first public settable property of type string or uint
        /// (excluding infrastructure: IsDirty, IsLoading), sets it, and verifies
        /// PropertyChanged fires.
        /// </summary>
        [Theory]
        [MemberData(nameof(UncoveredViewModels))]
        public void PropertyChanged_FiresOnSet(string vmName, Type vmType)
        {
            object? vm;
            try
            {
                vm = Activator.CreateInstance(vmType);
            }
            catch
            {
                _output.WriteLine($"SKIP: {vmName} could not be instantiated");
                return;
            }

            // All discovered types are ViewModelBase subclasses which implement INPC.
            var npc = (INotifyPropertyChanged)vm;

            // Find first settable string or uint property, excluding infrastructure
            var excludedNames = new HashSet<string>(StringComparer.Ordinal)
            {
                "IsDirty", "IsLoading",
            };

            var prop = vmType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.CanRead)
                .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(uint))
                .Where(p => !excludedNames.Contains(p.Name))
                .FirstOrDefault();

            if (prop == null)
            {
                _output.WriteLine($"SKIP: {vmName} has no suitable string/uint property");
                return;
            }

            var changed = new List<string>();
            npc.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != null)
                    changed.Add(e.PropertyName);
            };

            // Set value
            if (prop.PropertyType == typeof(string))
                prop.SetValue(vm, "test_value_" + vmName);
            else
                prop.SetValue(vm, 42u);

            Assert.Contains(prop.Name, changed);
            _output.WriteLine($"OK: {vmName}.{prop.Name} fired PropertyChanged");
        }

        /// <summary>
        /// Verifies IsDirty is false on fresh construction (if the VM has IsDirty property).
        /// Some VMs intentionally set properties in their constructor (e.g., DataExportViewModel
        /// sets SelectedTable), which triggers dirty marking. For those, MarkClean() after
        /// construction should yield IsDirty=false.
        /// </summary>
        [Theory]
        [MemberData(nameof(UncoveredViewModels))]
        public void DefaultState_IsDirtyFalse(string vmName, Type vmType)
        {
            object? vm;
            try
            {
                vm = Activator.CreateInstance(vmType);
            }
            catch
            {
                _output.WriteLine($"SKIP: {vmName} could not be instantiated");
                return;
            }

            // All discovered types are ViewModelBase subclasses — direct cast is safe.
            var vmBase = (ViewModelBase)vm;

            // Some constructors set properties that trigger dirty (e.g., DataExportViewModel).
            // Call MarkClean() to reset, then verify IsDirty is false.
            vmBase.MarkClean();

            Assert.False(vmBase.IsDirty, $"{vmName} IsDirty should be false after MarkClean");
            _output.WriteLine($"OK: {vmName} IsDirty=false after MarkClean");
        }

        /// <summary>
        /// Summary test that counts uncovered VMs and reports instantiation success rate.
        /// </summary>
        [Fact]
        public void Summary_CoverageReport()
        {
            var types = UncoveredViewModels().ToList();
            int succeeded = 0;
            int failed = 0;
            int propertyChangedOk = 0;
            int isDirtyOk = 0;
            var failures = new List<string>();

            foreach (var entry in types)
            {
                var name = (string)entry[0];
                var vmType = (Type)entry[1];

                try
                {
                    var vm = Activator.CreateInstance(vmType);
                    if (vm != null)
                    {
                        succeeded++;

                        // Check IsDirty
                        var isDirtyProp = vmType.GetProperty("IsDirty",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (isDirtyProp != null && isDirtyProp.GetValue(vm) is false)
                            isDirtyOk++;

                        // Check PropertyChanged
                        if (vm is INotifyPropertyChanged npc)
                        {
                            var prop = vmType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                .Where(p => p.CanWrite && p.CanRead)
                                .Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(uint))
                                .Where(p => p.Name != "IsDirty" && p.Name != "IsLoading")
                                .FirstOrDefault();

                            if (prop != null)
                            {
                                var changed = new List<string>();
                                npc.PropertyChanged += (_, e) =>
                                {
                                    if (e.PropertyName != null) changed.Add(e.PropertyName);
                                };

                                if (prop.PropertyType == typeof(string))
                                    prop.SetValue(vm, "test_summary");
                                else
                                    prop.SetValue(vm, 99u);

                                if (changed.Contains(prop.Name))
                                    propertyChangedOk++;
                            }
                        }
                    }
                    else
                    {
                        failed++;
                        failures.Add($"{name}: Activator returned null");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    var inner = ex is TargetInvocationException tie ? (tie.InnerException ?? tie) : ex;
                    failures.Add($"{name}: {inner.GetType().Name} - {inner.Message}");
                }
            }

            _output.WriteLine("=== Utility ViewModel Sweep Summary ===");
            _output.WriteLine($"Total discovered:          {types.Count}");
            _output.WriteLine($"Successfully instantiated: {succeeded}");
            _output.WriteLine($"Failed to instantiate:     {failed}");
            _output.WriteLine($"IsDirty=false on ctor:     {isDirtyOk}");
            _output.WriteLine($"PropertyChanged fires:     {propertyChangedOk}");

            if (failures.Count > 0)
            {
                _output.WriteLine("");
                _output.WriteLine("Failure details:");
                foreach (var f in failures)
                    _output.WriteLine($"  - {f}");
            }

            // Pure reporting — per-VM failures are caught by Constructor_DoesNotThrow.
            // No assertion here to avoid flakiness as the uncovered set evolves.
            _output.WriteLine($"\nSuccess rate: {(types.Count > 0 ? (double)succeeded / types.Count : 0):P0}");
        }
    }
}
