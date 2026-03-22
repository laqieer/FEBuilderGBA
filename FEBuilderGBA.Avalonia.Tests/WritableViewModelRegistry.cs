using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests
{
    /// <summary>
    /// Auto-discovery infrastructure for all writable ViewModels.
    /// Scans the Avalonia assembly for concrete ViewModelBase subclasses
    /// that have a matching triplet: list method, load method, write method.
    /// </summary>
    public static class WritableViewModelRegistry
    {
        // Property names excluded from FindFirstUintProperty — these are
        // infrastructure/identity properties, not data fields.
        private static readonly HashSet<string> ExcludedProperties = new(StringComparer.Ordinal)
        {
            "CurrentAddr", "SelectedId", "DataSize",
        };

        /// <summary>
        /// Discovers all writable VM triplets as xUnit MemberData rows.
        /// Each row: { Type vmType, string listMethodName, string loadMethodName, string writeMethodName }
        /// </summary>
        public static IEnumerable<object[]> WritableViewModels()
        {
            var assembly = typeof(UnitEditorViewModel).Assembly;
            var baseType = typeof(ViewModelBase);

            foreach (var type in assembly.GetTypes()
                         .Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                         .OrderBy(t => t.Name))
            {
                // Find write method: public void Write*() with 0 parameters
                var writeMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.StartsWith("Write", StringComparison.Ordinal)
                                && m.ReturnType == typeof(void)
                                && m.GetParameters().Length == 0)
                    .FirstOrDefault();
                if (writeMethod == null) continue;

                // Find list method: public List<AddrResult> Load*List() or LoadList() with 0 params
                var listMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.StartsWith("Load", StringComparison.Ordinal)
                                && m.ReturnType == typeof(List<AddrResult>)
                                && m.GetParameters().Length == 0)
                    .FirstOrDefault();
                if (listMethod == null) continue;

                // Find load method: public void Load*(uint) with exactly 1 uint param, NOT the list method
                var loadMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name.StartsWith("Load", StringComparison.Ordinal)
                                && m.ReturnType == typeof(void)
                                && m.GetParameters().Length == 1
                                && m.GetParameters()[0].ParameterType == typeof(uint)
                                && m.Name != listMethod.Name)
                    .FirstOrDefault();
                if (loadMethod == null) continue;

                yield return new object[] { type, listMethod.Name, loadMethod.Name, writeMethod.Name };
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

        /// <summary>Invoke the write method (void, 0 params) on the VM instance.</summary>
        public static void InvokeWrite(object vm, string methodName)
        {
            var method = vm.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);
            if (method == null)
                throw new InvalidOperationException($"Write method '{methodName}' not found on {vm.GetType().Name}");
            method.Invoke(vm, null);
        }

        /// <summary>
        /// Finds the first settable uint property that is a data field
        /// (not CurrentAddr, SelectedId, DataSize).
        /// Returns null if no suitable property is found.
        /// </summary>
        public static PropertyInfo? FindFirstUintProperty(object vm)
        {
            return vm.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == typeof(uint)
                            && p.CanRead && p.CanWrite
                            && !ExcludedProperties.Contains(p.Name))
                .FirstOrDefault();
        }

        /// <summary>
        /// Gets the struct size for the VM's data region.
        /// Reads DataSize property if available, otherwise uses ROM info defaults or 64.
        /// </summary>
        public static uint GetStructSize(object vm)
        {
            // Try DataSize property first
            var dataSizeProp = vm.GetType().GetProperty("DataSize", BindingFlags.Public | BindingFlags.Instance);
            if (dataSizeProp != null && dataSizeProp.PropertyType == typeof(uint))
            {
                uint val = (uint)dataSizeProp.GetValue(vm)!;
                if (val > 0) return val;
            }

            // Fallback: use ROM info for known VM types
            var rom = CoreState.ROM;
            if (rom?.RomInfo != null)
            {
                string name = vm.GetType().Name;
                if (name.Contains("UnitEditor")) return rom.RomInfo.unit_datasize;
                if (name.Contains("ClassEditor") || name.Contains("ClassFE6")) return rom.RomInfo.class_datasize;
                if (name.Contains("ItemEditor") || name.Contains("ItemFE6")) return rom.RomInfo.item_datasize;
            }

            // Final fallback
            return 64;
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
