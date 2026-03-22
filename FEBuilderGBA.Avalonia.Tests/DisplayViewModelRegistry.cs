using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Auto-discovery infrastructure for all display-only ViewModels.
    /// Scans the Avalonia assembly for concrete ViewModelBase subclasses
    /// that have a matching pair: list method + load method, but NO write method.
    /// These are read-only / display-only ViewModels.
    /// </summary>
    public static class DisplayViewModelRegistry
    {
        /// <summary>
        /// Discovers all display-only VM pairs as xUnit MemberData rows.
        /// Each row: { Type vmType, string listMethodName, string loadMethodName }
        ///
        /// A display-only VM is one that has:
        /// 1. A LoadList method: public List&lt;AddrResult&gt; Load*() with 0 params
        /// 2. A Load method: public void Load*(uint addr) with 1 uint param (not the list method)
        /// 3. NO Write method: no public void Write*() with 0 params
        /// </summary>
        public static IEnumerable<object[]> DisplayViewModels()
        {
            var assembly = typeof(UnitEditorViewModel).Assembly;
            var baseType = typeof(ViewModelBase);

            foreach (var type in assembly.GetTypes()
                         .Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                         .OrderBy(t => t.Name))
            {
                // Check for write method: public void Write*() with 0 parameters
                var writeMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.StartsWith("Write", StringComparison.Ordinal)
                                && m.ReturnType == typeof(void)
                                && m.GetParameters().Length == 0)
                    .FirstOrDefault();

                // Display-only means NO write method
                if (writeMethod != null) continue;

                // Find list method: public List<AddrResult> Load*() with 0 params
                var listMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.StartsWith("Load", StringComparison.Ordinal)
                                && m.ReturnType == typeof(List<AddrResult>)
                                && m.GetParameters().Length == 0)
                    .FirstOrDefault();
                if (listMethod == null) continue;

                // Find load method: public void Load*(uint) with exactly 1 uint param
                // Must not be the list method, must not end with "List", must not return List<AddrResult>
                var loadMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.StartsWith("Load", StringComparison.Ordinal)
                                && m.ReturnType == typeof(void)
                                && m.GetParameters().Length == 1
                                && m.GetParameters()[0].ParameterType == typeof(uint)
                                && m.Name != listMethod.Name
                                && !m.Name.EndsWith("List", StringComparison.Ordinal))
                    .FirstOrDefault();
                if (loadMethod == null) continue;

                yield return new object[] { type, listMethod.Name, loadMethod.Name };
            }
        }

        /// <summary>Invoke the list method on the VM instance. Returns the list of AddrResult.</summary>
        public static List<AddrResult> InvokeList(object vm, string methodName)
        {
            var method = vm.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                throw new InvalidOperationException($"List method '{methodName}' not found on {vm.GetType().Name}");
            return (List<AddrResult>)method.Invoke(vm, null)!;
        }

        /// <summary>Invoke the load method (takes uint addr) on the VM instance.</summary>
        public static void InvokeLoad(object vm, string methodName, uint addr)
        {
            var method = vm.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(uint) }, null);
            if (method == null)
                throw new InvalidOperationException($"Load method '{methodName}' not found on {vm.GetType().Name}");
            method.Invoke(vm, new object[] { addr });
        }

        /// <summary>
        /// Gets the CurrentAddr value from a VM instance via reflection.
        /// Returns 0 if the property is not found or not readable.
        /// </summary>
        public static uint GetCurrentAddr(object vm)
        {
            var prop = vm.GetType().GetProperty("CurrentAddr", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || prop.PropertyType != typeof(uint)) return 0;
            return (uint)prop.GetValue(vm)!;
        }
    }
}
