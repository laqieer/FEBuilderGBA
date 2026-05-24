using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Reflection-based headless test sweep that discovers and instantiates all
    /// concrete View classes (UserControl/Window subclasses) in the Avalonia assembly.
    /// Verifies no constructor throws and basic content structure is valid.
    ///
    /// This complements DataVerifiableSweepTests (which covers 169 ViewModels via
    /// IDataVerifiable) by exercising the 338+ View/.axaml.cs classes that have
    /// no headless instantiation tests otherwise.
    ///
    /// Closes #211.
    /// Ref #404: serialize against `SharedState` so a parallel test class
    /// holding a live `CoreState.ROM` via `RomFixture` doesn't race with the
    /// View constructors that read `CoreState.ROM` / `SystemTextEncoder`
    /// during their initialization paths.
    /// </summary>
    [Collection("SharedState")]
    public class ViewInstantiationSweepTests
    {
        private readonly ITestOutputHelper _output;

        public ViewInstantiationSweepTests(ITestOutputHelper output) => _output = output;

        /// <summary>
        /// Discover all concrete View classes (UserControl/Window subclasses) in
        /// the FEBuilderGBA.Avalonia assembly that have a public parameterless constructor.
        /// Returns (typeName, Type) pairs for use as [MemberData].
        /// </summary>
        public static IEnumerable<object[]> AllViewTypes()
        {
            var asm = typeof(FEBuilderGBA.Avalonia.App).Assembly;
            foreach (var type in asm.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => typeof(UserControl).IsAssignableFrom(t) || typeof(Window).IsAssignableFrom(t))
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName))
            {
                yield return new object[] { type.Name, type };
            }
        }

        /// <summary>
        /// Verifies that every discovered View type can be instantiated via its
        /// parameterless constructor without throwing.
        /// </summary>
        [AvaloniaTheory]
        [MemberData(nameof(AllViewTypes))]
        public void View_CanInstantiate(string name, Type viewType)
        {
            Exception? caught = null;
            object? view = null;
            try
            {
                view = Activator.CreateInstance(viewType);
            }
            catch (TargetInvocationException tie)
            {
                caught = tie.InnerException ?? tie;
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            if (caught != null)
            {
                _output.WriteLine($"FAIL: {name} threw {caught.GetType().Name}: {caught.Message}");
            }

            Assert.True(view != null,
                $"{name}: Constructor threw {caught?.GetType().Name}: {caught?.Message}");
            _output.WriteLine($"OK: {name}");
        }

        /// <summary>
        /// For ContentControl-derived views, verifies accessing Content does not throw.
        /// For Panel-derived views, verifies accessing Children does not throw.
        /// This catches XAML binding or resource errors that surface only when
        /// the visual tree is first accessed.
        /// </summary>
        [AvaloniaTheory]
        [MemberData(nameof(AllViewTypes))]
        public void View_HasContent(string name, Type viewType)
        {
            object? view = null;
            try
            {
                view = Activator.CreateInstance(viewType);
            }
            catch
            {
                _output.WriteLine($"SKIP: {name} could not be instantiated");
                return;
            }

            if (view is ContentControl cc)
            {
                // Content may be null for some views before data loading -- just verify no throw
                _output.WriteLine($"Content check: {name} content={(cc.Content != null ? "set" : "null")}");
            }
            else if (view is Panel p)
            {
                _output.WriteLine($"Panel check: {name} children={p.Children.Count}");
            }
            else
            {
                _output.WriteLine($"Type check: {name} is {view.GetType().BaseType?.Name}");
            }
        }

        /// <summary>
        /// Validates that the discovery mechanism finds a realistic number of View types.
        /// We expect at least 300 (338 known .axaml.cs files plus Controls and Dialogs).
        /// If this fails, something is wrong with the reflection discovery.
        /// </summary>
        [AvaloniaFact]
        public void Discovery_FindsAllExpectedViewTypes()
        {
            var types = AllViewTypes().ToList();
            _output.WriteLine($"Discovered {types.Count} View types (UserControl + Window):");
            _output.WriteLine("");

            // Group by namespace for reporting
            var grouped = types
                .Select(t => (Type)t[1])
                .GroupBy(t => t.Namespace ?? "(no namespace)")
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                _output.WriteLine($"  {group.Key}: {group.Count()} types");
                foreach (var t in group.OrderBy(t => t.Name))
                {
                    _output.WriteLine($"    - {t.Name}");
                }
            }

            // We expect at least 300 view types (338 known .axaml.cs + Controls + Dialogs)
            Assert.True(types.Count >= 300,
                $"Expected >= 300 view types, found {types.Count}. " +
                "Some views may be missing parameterless constructors.");
        }

        /// <summary>
        /// Summary test that attempts instantiation of all discovered View types
        /// and reports success/failure counts. Useful for tracking coverage progress.
        /// </summary>
        [AvaloniaFact]
        public void Summary_InstantiationReport()
        {
            var types = AllViewTypes().ToList();
            int succeeded = 0;
            int failed = 0;
            var failures = new List<string>();

            foreach (var entry in types)
            {
                var name = (string)entry[0];
                var viewType = (Type)entry[1];

                try
                {
                    var view = Activator.CreateInstance(viewType);
                    if (view != null)
                    {
                        succeeded++;
                    }
                    else
                    {
                        failed++;
                        failures.Add($"{name}: Activator returned null");
                    }
                }
                catch (TargetInvocationException tie)
                {
                    failed++;
                    var inner = tie.InnerException ?? tie;
                    failures.Add($"{name}: {inner.GetType().Name} - {inner.Message}");
                }
                catch (Exception ex)
                {
                    failed++;
                    failures.Add($"{name}: {ex.GetType().Name} - {ex.Message}");
                }
            }

            _output.WriteLine("=== View Instantiation Sweep Summary ===");
            _output.WriteLine($"Total discovered:     {types.Count}");
            _output.WriteLine($"Successfully created: {succeeded}");
            _output.WriteLine($"Failed:               {failed}");

            if (failures.Count > 0)
            {
                _output.WriteLine("");
                _output.WriteLine("Failure details:");
                foreach (var f in failures)
                    _output.WriteLine($"  - {f}");
            }

            // At least 90% should instantiate successfully
            double successRate = types.Count > 0 ? (double)succeeded / types.Count : 0;
            Assert.True(successRate >= 0.90,
                $"Only {succeeded}/{types.Count} ({successRate:P0}) Views instantiated. " +
                $"Expected >= 90%. Failures: {string.Join("; ", failures.Take(10))}");
        }
    }
}
