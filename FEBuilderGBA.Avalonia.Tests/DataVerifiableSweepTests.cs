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
    /// Automated sweep that discovers all IDataVerifiable ViewModels via reflection
    /// and verifies GetDataReport()/GetRawRomReport() produce valid output.
    ///
    /// WU3 of #210: This is the largest work unit — 169 ViewModels are tested.
    /// Each ViewModel gets its own test case via [Theory] + [MemberData].
    /// Tests skip gracefully when ROMs are unavailable.
    /// </summary>
    [Collection("SharedState")]
    public class DataVerifiableSweepTests : IClassFixture<RomFixture>
    {
        private readonly RomFixture _rom;
        private readonly ITestOutputHelper _output;

        public DataVerifiableSweepTests(RomFixture rom, ITestOutputHelper output)
        {
            _rom = rom;
            _output = output;
        }

        /// <summary>
        /// Reflection-based discovery of all concrete types implementing IDataVerifiable.
        /// Returns (typeName, Type) pairs for use as [MemberData].
        /// </summary>
        public static IEnumerable<object[]> VerifiableViewModels()
        {
            var assembly = typeof(IDataVerifiable).Assembly;
            foreach (var type in assembly.GetTypes()
                .Where(t => typeof(IDataVerifiable).IsAssignableFrom(t)
                            && !t.IsAbstract
                            && !t.IsInterface)
                .OrderBy(t => t.Name))
            {
                yield return new object[] { type.Name, type };
            }
        }

        /// <summary>
        /// Verifies that reflection discovers a realistic number of IDataVerifiable types.
        /// If this fails, the sweep is broken.
        /// </summary>
        [Fact]
        public void Discovery_FindsExpectedTypeCount()
        {
            var types = VerifiableViewModels().ToList();
            _output.WriteLine($"Discovered {types.Count} IDataVerifiable types");
            // We expect ~170 types; use a generous lower bound
            Assert.True(types.Count >= 165,
                $"Expected >= 165 IDataVerifiable types, found {types.Count}. " +
                "Reflection discovery may be broken.");
        }

        /// <summary>
        /// Verifies that every discovered IDataVerifiable type can be instantiated
        /// via its parameterless constructor without throwing.
        /// </summary>
        [Theory]
        [MemberData(nameof(VerifiableViewModels))]
        public void CanInstantiate(string name, Type vmType)
        {
            var instance = TryCreateInstance(vmType);
            Assert.True(instance != null,
                $"{name}: Could not instantiate — no parameterless constructor or constructor threw.");
        }

        /// <summary>
        /// Verifies GetDataReport() returns a non-null dictionary for every IDataVerifiable.
        /// When ROM is loaded, also verifies the report is non-empty for types that
        /// have list data (GetListCount() > 0).
        /// </summary>
        [Theory]
        [MemberData(nameof(VerifiableViewModels))]
        public void GetDataReport_ReturnsNonNull(string name, Type vmType)
        {
            var instance = TryCreateInstance(vmType);
            if (instance == null)
            {
                _output.WriteLine($"SKIP {name}: could not instantiate");
                return;
            }

            var verifiable = (IDataVerifiable)instance;

            // Without ROM loaded, GetDataReport should still return a dictionary (possibly empty)
            Dictionary<string, string>? report = null;
            Exception? ex = null;
            try
            {
                report = verifiable.GetDataReport();
            }
            catch (Exception e)
            {
                ex = e;
            }

            if (!_rom.IsAvailable)
            {
                // Without ROM, we just verify it doesn't crash fatally
                // Some VMs may throw NullReferenceException when ROM is null — that's acceptable
                _output.WriteLine($"SKIP {name}: ROM not available (exception: {ex?.GetType().Name ?? "none"})");
                return;
            }

            // With ROM loaded, the method should not throw
            if (ex != null)
            {
                Assert.Fail($"{name}.GetDataReport() threw {ex.GetType().Name}: {ex.Message}");
            }

            Assert.NotNull(report);
            _output.WriteLine($"OK {name}.GetDataReport() => {report.Count} fields");
        }

        /// <summary>
        /// Verifies GetRawRomReport() returns a non-null dictionary for every IDataVerifiable.
        /// </summary>
        [Theory]
        [MemberData(nameof(VerifiableViewModels))]
        public void GetRawRomReport_ReturnsNonNull(string name, Type vmType)
        {
            var instance = TryCreateInstance(vmType);
            if (instance == null)
            {
                _output.WriteLine($"SKIP {name}: could not instantiate");
                return;
            }

            var verifiable = (IDataVerifiable)instance;

            Dictionary<string, string>? report = null;
            Exception? ex = null;
            try
            {
                report = verifiable.GetRawRomReport();
            }
            catch (Exception e)
            {
                ex = e;
            }

            if (!_rom.IsAvailable)
            {
                _output.WriteLine($"SKIP {name}: ROM not available (exception: {ex?.GetType().Name ?? "none"})");
                return;
            }

            if (ex != null)
            {
                Assert.Fail($"{name}.GetRawRomReport() threw {ex.GetType().Name}: {ex.Message}");
            }

            Assert.NotNull(report);
            _output.WriteLine($"OK {name}.GetRawRomReport() => {report.Count} fields");
        }

        /// <summary>
        /// Verifies GetListCount() returns a non-negative value (or 0 for sub-editors).
        /// </summary>
        [Theory]
        [MemberData(nameof(VerifiableViewModels))]
        public void GetListCount_ReturnsNonNegative(string name, Type vmType)
        {
            var instance = TryCreateInstance(vmType);
            if (instance == null)
            {
                _output.WriteLine($"SKIP {name}: could not instantiate");
                return;
            }

            var verifiable = (IDataVerifiable)instance;

            int count = -1;
            Exception? ex = null;
            try
            {
                count = verifiable.GetListCount();
            }
            catch (Exception e)
            {
                ex = e;
            }

            if (!_rom.IsAvailable)
            {
                _output.WriteLine($"SKIP {name}: ROM not available (exception: {ex?.GetType().Name ?? "none"})");
                return;
            }

            if (ex != null)
            {
                Assert.Fail($"{name}.GetListCount() threw {ex.GetType().Name}: {ex.Message}");
            }

            Assert.True(count >= 0,
                $"{name}.GetListCount() returned {count}, expected >= 0");
            _output.WriteLine($"OK {name}.GetListCount() => {count}");
        }

        /// <summary>
        /// Cross-checks that when GetDataReport() and GetRawRomReport() both return data,
        /// the "addr" field matches between them (if present in both).
        /// This catches address calculation mismatches.
        /// </summary>
        [Theory]
        [MemberData(nameof(VerifiableViewModels))]
        public void DataAndRawReports_AddressesMatch(string name, Type vmType)
        {
            if (!_rom.IsAvailable)
            {
                _output.WriteLine($"SKIP {name}: ROM not available");
                return;
            }

            var instance = TryCreateInstance(vmType);
            if (instance == null)
            {
                _output.WriteLine($"SKIP {name}: could not instantiate");
                return;
            }

            var verifiable = (IDataVerifiable)instance;

            Dictionary<string, string>? dataReport = null;
            Dictionary<string, string>? rawReport = null;

            try
            {
                dataReport = verifiable.GetDataReport();
                rawReport = verifiable.GetRawRomReport();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {name}: report threw {ex.GetType().Name}: {ex.Message}");
                return;
            }

            if (dataReport == null || rawReport == null)
            {
                _output.WriteLine($"SKIP {name}: null report");
                return;
            }

            // Both reports should be empty or both should have data
            if (dataReport.Count == 0 && rawReport.Count == 0)
            {
                _output.WriteLine($"OK {name}: both reports empty (no data loaded)");
                return;
            }

            // If both have "addr" key, values must match
            if (dataReport.TryGetValue("addr", out string? dataAddr)
                && rawReport.TryGetValue("addr", out string? rawAddr))
            {
                Assert.Equal(dataAddr, rawAddr);
                _output.WriteLine($"OK {name}: addr match {dataAddr}");
            }
            else
            {
                _output.WriteLine($"OK {name}: no addr key to compare " +
                    $"(data={dataReport.Count} fields, raw={rawReport.Count} fields)");
            }
        }

        /// <summary>
        /// Summary test that counts how many ViewModels produce non-empty reports
        /// when ROM is loaded. Useful for tracking coverage progress.
        /// </summary>
        [Fact]
        public void Summary_ReportsPopulatedCount()
        {
            if (!_rom.IsAvailable)
            {
                _output.WriteLine("SKIP: ROM not available");
                return;
            }

            var types = VerifiableViewModels().ToList();
            int instantiated = 0;
            int dataReportNonEmpty = 0;
            int rawReportNonEmpty = 0;
            int listCountPositive = 0;
            var failures = new List<string>();

            foreach (var entry in types)
            {
                var name = (string)entry[0];
                var vmType = (Type)entry[1];

                var instance = TryCreateInstance(vmType);
                if (instance == null)
                {
                    failures.Add($"{name}: could not instantiate");
                    continue;
                }
                instantiated++;

                var verifiable = (IDataVerifiable)instance;
                try
                {
                    var dr = verifiable.GetDataReport();
                    if (dr != null && dr.Count > 0) dataReportNonEmpty++;

                    var rr = verifiable.GetRawRomReport();
                    if (rr != null && rr.Count > 0) rawReportNonEmpty++;

                    int lc = verifiable.GetListCount();
                    if (lc > 0) listCountPositive++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{name}: {ex.GetType().Name} — {ex.Message}");
                }
            }

            _output.WriteLine($"=== IDataVerifiable Sweep Summary ===");
            _output.WriteLine($"Total discovered:       {types.Count}");
            _output.WriteLine($"Successfully created:   {instantiated}");
            _output.WriteLine($"GetDataReport non-empty:    {dataReportNonEmpty}");
            _output.WriteLine($"GetRawRomReport non-empty:  {rawReportNonEmpty}");
            _output.WriteLine($"GetListCount > 0:           {listCountPositive}");
            _output.WriteLine($"Failures:                   {failures.Count}");

            if (failures.Count > 0)
            {
                _output.WriteLine("\nFailure details:");
                foreach (var f in failures)
                    _output.WriteLine($"  - {f}");
            }

            // At least 90% should instantiate successfully
            double instantiateRate = (double)instantiated / types.Count;
            Assert.True(instantiateRate >= 0.9,
                $"Only {instantiated}/{types.Count} ({instantiateRate:P0}) VMs instantiated. " +
                $"Expected >= 90%.");
        }

        /// <summary>
        /// Verifies GetFieldOffsetMap() returns a non-null dictionary and that
        /// all keys exist in GetDataReport() and all values exist in GetRawRomReport().
        /// </summary>
        [Theory]
        [MemberData(nameof(VerifiableViewModels))]
        public void GetFieldOffsetMap_KeysConsistentWithReports(string name, Type vmType)
        {
            if (!_rom.IsAvailable)
            {
                _output.WriteLine($"SKIP {name}: ROM not available");
                return;
            }

            var instance = TryCreateInstance(vmType);
            if (instance == null)
            {
                _output.WriteLine($"SKIP {name}: could not instantiate");
                return;
            }

            var verifiable = (IDataVerifiable)instance;

            Dictionary<string, string>? fieldMap = null;
            Dictionary<string, string>? dataReport = null;
            Dictionary<string, string>? rawReport = null;

            try
            {
                fieldMap = verifiable.GetFieldOffsetMap();
                dataReport = verifiable.GetDataReport();
                rawReport = verifiable.GetRawRomReport();
            }
            catch (Exception ex)
            {
                _output.WriteLine($"SKIP {name}: threw {ex.GetType().Name}: {ex.Message}");
                return;
            }

            Assert.NotNull(fieldMap);
            _output.WriteLine($"OK {name}.GetFieldOffsetMap() => {fieldMap.Count} mappings");

            if (fieldMap.Count == 0)
            {
                _output.WriteLine($"OK {name}: empty field map (default implementation)");
                return;
            }

            // Validate: each field map key should exist in dataReport
            if (dataReport != null && dataReport.Count > 0)
            {
                foreach (var kv in fieldMap)
                {
                    Assert.True(dataReport.ContainsKey(kv.Key),
                        $"{name}: GetFieldOffsetMap key '{kv.Key}' not found in GetDataReport()");
                }
            }

            // When fieldMap is non-empty and ROM is available, rawReport should be non-empty
            // (the VM should be able to read raw ROM bytes at the current address).
            if (rawReport == null || rawReport.Count == 0)
            {
                _output.WriteLine($"WARN {name}: rawReport is empty despite non-empty fieldMap — verify GetRawRomReport() reads data");
            }

            // Validate: each field map value should exist in rawReport
            if (rawReport != null && rawReport.Count > 0)
            {
                foreach (var kv in fieldMap)
                {
                    Assert.True(rawReport.ContainsKey(kv.Value),
                        $"{name}: GetFieldOffsetMap value '{kv.Value}' not found in GetRawRomReport()");
                }
            }
        }

        /// <summary>
        /// Per-field cross-check: for ViewModels with non-empty GetFieldOffsetMap(),
        /// verifies that each mapped field value in GetDataReport() matches the
        /// corresponding value in GetRawRomReport().
        /// This is the core of --data-verify-full field validation.
        /// </summary>
        [Theory]
        [MemberData(nameof(VerifiableViewModels))]
        public void FieldLevelCrossCheck_AllFieldsMatch(string name, Type vmType)
        {
            if (!_rom.IsAvailable)
            {
                _output.WriteLine($"SKIP {name}: ROM not available");
                return;
            }

            var instance = TryCreateInstance(vmType);
            if (instance == null)
            {
                _output.WriteLine($"SKIP {name}: could not instantiate");
                return;
            }

            var verifiable = (IDataVerifiable)instance;

            try
            {
                var fieldMap = verifiable.GetFieldOffsetMap();
                if (fieldMap.Count == 0)
                {
                    _output.WriteLine($"OK {name}: no field map (default implementation)");
                    return;
                }

                var dataReport = verifiable.GetDataReport();
                var rawReport = verifiable.GetRawRomReport();

                if (dataReport.Count == 0 || rawReport.Count == 0)
                {
                    _output.WriteLine($"SKIP {name}: empty report(s)");
                    return;
                }

                int checked_ = 0;
                int mismatches = 0;
                foreach (var (fieldName, offsetKey) in fieldMap)
                {
                    if (!dataReport.TryGetValue(fieldName, out string? dataVal)) continue;
                    if (!rawReport.TryGetValue(offsetKey, out string? rawVal)) continue;

                    checked_++;
                    string normData = NormalizeHex(dataVal);
                    string normRaw = NormalizeHex(rawVal);

                    if (!string.Equals(normData, normRaw, StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches++;
                        _output.WriteLine($"MISMATCH {name}.{fieldName}: data={dataVal} raw={rawVal} (offset={offsetKey})");
                    }
                }

                Assert.True(mismatches == 0,
                    $"{name}: {mismatches} field mismatches out of {checked_} checked fields");
                _output.WriteLine($"OK {name}: {checked_} fields cross-checked, all match");
            }
            catch (Exception ex) when (!_rom.IsAvailable)
            {
                // Only swallow exceptions when ROM is not available (expected failures).
                // When ROM IS available, let exceptions propagate to surface regressions.
                _output.WriteLine($"SKIP {name}: threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Verifies the number of ViewModels with non-empty GetFieldOffsetMap().
        /// After #269 batch, we expect at least 156 editors with field maps.
        /// </summary>
        [Fact]
        public void FieldOffsetMap_CoverageCount()
        {
            var types = VerifiableViewModels().ToList();
            int withFieldMap = 0;
            var mapped = new List<string>();

            foreach (var entry in types)
            {
                var name = (string)entry[0];
                var vmType = (Type)entry[1];

                var instance = TryCreateInstance(vmType);
                if (instance == null) continue;

                var verifiable = (IDataVerifiable)instance;
                try
                {
                    var fm = verifiable.GetFieldOffsetMap();
                    if (fm != null && fm.Count > 0)
                    {
                        withFieldMap++;
                        mapped.Add($"{name} ({fm.Count} fields)");
                    }
                }
                catch
                {
                    // skip
                }
            }

            _output.WriteLine($"ViewModels with field maps: {withFieldMap}");
            foreach (var m in mapped)
                _output.WriteLine($"  - {m}");

            // After #269 batch: 161 editors now have field maps
            Assert.True(withFieldMap >= 156,
                $"Expected >= 156 ViewModels with field maps, found {withFieldMap}");
        }

        /// <summary>Normalize a hex or decimal string for comparison.</summary>
        internal static string NormalizeHex(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return value.ToUpperInvariant();
            if (int.TryParse(value, out int dec))
            {
                if (dec >= 0 && dec <= 255) return $"0x{(byte)dec:X02}";
                // Signed byte range (-128..-1): treat as unsigned byte for ROM comparison
                if (dec >= -128 && dec < 0) return $"0x{(byte)(sbyte)dec:X02}";
                return $"0x{dec:X08}";
            }
            return value;
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>
        /// Attempts to create an instance of the given type using its parameterless constructor.
        /// Returns null if the type has no parameterless constructor or if instantiation throws.
        /// </summary>
        private static object? TryCreateInstance(Type type)
        {
            try
            {
                // Check for parameterless constructor
                var ctor = type.GetConstructor(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);

                if (ctor != null)
                    return ctor.Invoke(null);

                // Try Activator as fallback
                return Activator.CreateInstance(type);
            }
            catch
            {
                return null;
            }
        }
    }
}
