using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform struct data export/import for ROM tables (units, classes, items).
    /// Mirrors TranslateCore pattern: export to TSV, import from TSV, round-trip validation.
    /// </summary>
    public static class StructExportCore
    {
        /// <summary>
        /// Definition of a ROM data table (pointer, entry size, count strategy).
        /// </summary>
        public class TableDef
        {
            public string Name { get; set; }
            public string StructNameFE6 { get; set; }
            public string StructNameFE78 { get; set; }
            public string MetadataFileFE6 { get; set; }
            public string MetadataFileFE78 { get; set; }
            /// <summary>Get the base address for this table from the ROM.</summary>
            public Func<ROM, uint> GetBaseAddress { get; set; }
            /// <summary>Get the entry data size from the ROM.</summary>
            public Func<ROM, uint> GetDataSize { get; set; }
            /// <summary>Get the entry count for this table.</summary>
            public Func<ROM, uint> GetEntryCount { get; set; }
        }

        static readonly Dictionary<string, TableDef> _tables = new Dictionary<string, TableDef>(StringComparer.OrdinalIgnoreCase);

        static StructExportCore()
        {
            RegisterTable(new TableDef
            {
                Name = "units",
                StructNameFE6 = "Unit_FE6",
                StructNameFE78 = "Unit_FE78",
                MetadataFileFE6 = "struct_unit_fe6.txt",
                MetadataFileFE78 = "struct_unit_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.unit_pointer),
                GetDataSize = rom => rom.RomInfo.unit_datasize,
                GetEntryCount = rom => rom.RomInfo.unit_maxcount,
            });

            RegisterTable(new TableDef
            {
                Name = "classes",
                StructNameFE6 = "Class_FE6",
                StructNameFE78 = "Class_FE78",
                MetadataFileFE6 = "struct_class_fe6.txt",
                MetadataFileFE78 = "struct_class_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.class_pointer),
                GetDataSize = rom => rom.RomInfo.class_datasize,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.class_pointer);
                    uint dataSize = rom.RomInfo.class_datasize;
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i == 0) return true;
                        if (i > 0xFF) return false;
                        return rom.u8(addr + 4) != 0;
                    });
                },
            });

            RegisterTable(new TableDef
            {
                Name = "items",
                StructNameFE6 = "Item_FE6",
                StructNameFE78 = "Item_FE78",
                MetadataFileFE6 = "struct_item_fe6.txt",
                MetadataFileFE78 = "struct_item_fe78.txt",
                GetBaseAddress = rom => ResolvePointer(rom, rom.RomInfo.item_pointer),
                GetDataSize = rom => rom.RomInfo.item_datasize,
                GetEntryCount = rom =>
                {
                    uint baseAddr = ResolvePointer(rom, rom.RomInfo.item_pointer);
                    uint dataSize = rom.RomInfo.item_datasize;
                    return rom.getBlockDataCount(baseAddr, dataSize, (int i, uint addr) =>
                    {
                        if (i > 0xFF) return false;
                        return U.isPointerOrNULL(rom.u32(addr + 12));
                    });
                },
            });
        }

        /// <summary>
        /// Resolve a ROM pointer: dereference the pointer at the given address.
        /// ROM info properties like unit_pointer store the address OF the pointer,
        /// not the data address itself. This mirrors InputFormRef behavior.
        /// </summary>
        static uint ResolvePointer(ROM rom, uint pointerAddr)
        {
            if (pointerAddr == 0 || pointerAddr == U.NOT_FOUND) return 0;
            uint offset = U.toOffset(pointerAddr);
            if (!U.isSafetyOffset(offset, rom)) return 0;
            return rom.p32(offset);
        }

        /// <summary>Register a table definition.</summary>
        public static void RegisterTable(TableDef def)
        {
            _tables[def.Name] = def;
        }

        /// <summary>Get all registered table names.</summary>
        public static IEnumerable<string> GetTableNames() => _tables.Keys;

        /// <summary>Get a table definition by name.</summary>
        public static TableDef GetTable(string name)
        {
            _tables.TryGetValue(name, out var def);
            return def;
        }

        /// <summary>
        /// Load the appropriate StructDef for a table based on ROM version.
        /// </summary>
        public static StructMetadata.StructDef LoadStructDef(ROM rom, TableDef table)
        {
            bool isFE6 = rom.RomInfo.version == 6;
            string fileName = isFE6 ? table.MetadataFileFE6 : table.MetadataFileFE78;
            string structName = isFE6 ? table.StructNameFE6 : table.StructNameFE78;

            string dir = Path.Combine(CoreState.BaseDirectory, "config", "data");
            string path = Path.Combine(dir, fileName);

            var meta = new StructMetadata();
            meta.LoadFromFile(path);
            return meta.GetStruct(structName);
        }

        /// <summary>
        /// Export a ROM table to a list of (index, fieldName→value) entries.
        /// </summary>
        public static List<Dictionary<string, string>> ExportTable(ROM rom, TableDef table, StructMetadata.StructDef structDef)
        {
            var result = new List<Dictionary<string, string>>();
            if (rom?.RomInfo == null || table == null || structDef == null) return result;

            uint baseAddr = table.GetBaseAddress(rom);
            uint dataSize = table.GetDataSize(rom);
            uint count = table.GetEntryCount(rom);

            if (baseAddr == 0 || baseAddr == U.NOT_FOUND || count == 0) return result;

            for (uint i = 0; i < count; i++)
            {
                uint entryAddr = baseAddr + (i * dataSize);
                if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) break;

                var entry = new Dictionary<string, string>();

                // First column: index with decoded name
                string name = "";
                if (structDef.Fields.Count > 0 && structDef.Fields[0].Name == "NameTextID")
                {
                    uint textId = structDef.ReadField(rom, entryAddr, structDef.Fields[0]);
                    try { name = FETextDecode.Direct(textId) ?? ""; } catch { }
                }
                entry["_Index"] = U.To0xHexString((byte)i) + " " + name;

                // All fields as hex values
                foreach (var field in structDef.Fields)
                {
                    uint val = structDef.ReadField(rom, entryAddr, field);
                    entry[field.Name] = FormatFieldValue(val, field);
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>Format a field value as hex string appropriate for its type.</summary>
        static string FormatFieldValue(uint val, StructMetadata.FieldDef field)
        {
            return field.Type switch
            {
                StructMetadata.FieldType.Byte => "0x" + val.ToString("X02"),
                StructMetadata.FieldType.Word => "0x" + val.ToString("X04"),
                StructMetadata.FieldType.DWord => "0x" + val.ToString("X08"),
                StructMetadata.FieldType.Pointer => "0x" + val.ToString("X08"),
                _ => "0x" + val.ToString("X02"),
            };
        }

        /// <summary>
        /// Export table data to a TSV file.
        /// </summary>
        public static void ExportToTSV(List<Dictionary<string, string>> entries, StructMetadata.StructDef structDef, string outputPath)
        {
            var sb = new StringBuilder();

            // Header
            sb.Append("Index");
            foreach (var field in structDef.Fields)
            {
                sb.Append('\t');
                sb.Append(field.Name);
            }
            sb.AppendLine();

            // Data rows
            foreach (var entry in entries)
            {
                sb.Append(entry.TryGetValue("_Index", out var idx) ? idx : "");
                foreach (var field in structDef.Fields)
                {
                    sb.Append('\t');
                    sb.Append(entry.TryGetValue(field.Name, out var val) ? val : "");
                }
                sb.AppendLine();
            }

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Import table data from a TSV file.
        /// Returns list of (index, fieldName→value) entries.
        /// </summary>
        public static List<(int index, Dictionary<string, string> fields)> ImportFromTSV(string inputPath, StructMetadata.StructDef structDef)
        {
            var result = new List<(int, Dictionary<string, string>)>();
            if (!File.Exists(inputPath)) return result;

            string[] lines = File.ReadAllLines(inputPath, Encoding.UTF8);
            if (lines.Length < 2) return result;

            // Parse header to get column mapping
            string[] headers = ParseTSVLine(lines[0]);
            if (headers.Length < 2) return result;

            for (int lineIdx = 1; lineIdx < lines.Length; lineIdx++)
            {
                string line = lines[lineIdx];
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] cols = ParseTSVLine(line);
                if (cols.Length < 1) continue;

                // Parse index from first column: "0xNN name" → extract hex index
                int entryIndex = ParseIndexFromFirstColumn(cols[0]);
                if (entryIndex < 0) continue;

                var fields = new Dictionary<string, string>();
                for (int c = 1; c < headers.Length && c < cols.Length; c++)
                {
                    fields[headers[c]] = cols[c];
                }

                result.Add((entryIndex, fields));
            }

            return result;
        }

        /// <summary>Parse a TSV line into columns, handling tabs correctly.</summary>
        public static string[] ParseTSVLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return Array.Empty<string>();
            return line.Split('\t');
        }

        /// <summary>Parse entry index from first column (e.g., "0x0A Eirika" → 10).</summary>
        static int ParseIndexFromFirstColumn(string col)
        {
            if (string.IsNullOrWhiteSpace(col)) return -1;
            string trimmed = col.Trim();

            // Extract hex portion (up to first space)
            int space = trimmed.IndexOf(' ');
            string hexPart = space >= 0 ? trimmed.Substring(0, space) : trimmed;

            try
            {
                return (int)U.atoi0x(hexPart);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Write imported data back to ROM.
        /// Returns the number of entries written.
        /// </summary>
        public static int WriteTable(ROM rom, TableDef table, StructMetadata.StructDef structDef,
            List<(int index, Dictionary<string, string> fields)> entries)
        {
            if (rom?.RomInfo == null || table == null || structDef == null) return 0;

            uint baseAddr = table.GetBaseAddress(rom);
            uint dataSize = table.GetDataSize(rom);
            uint count = table.GetEntryCount(rom);

            if (baseAddr == 0 || baseAddr == U.NOT_FOUND || count == 0) return 0;

            int written = 0;

            foreach (var (index, fields) in entries)
            {
                if (index < 0 || (uint)index >= count) continue;

                uint entryAddr = baseAddr + ((uint)index * dataSize);
                if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) continue;

                foreach (var field in structDef.Fields)
                {
                    if (!fields.TryGetValue(field.Name, out string valStr)) continue;

                    uint val = U.atoi0x(valStr);
                    structDef.WriteField(rom, entryAddr, field, val);
                }

                written++;
            }

            return written;
        }

        /// <summary>
        /// Result of a data round-trip validation.
        /// </summary>
        public class DataRoundTripResult
        {
            public string TableName { get; set; }
            public int TotalEntries { get; set; }
            public int MatchCount { get; set; }
            public int MismatchCount { get; set; }
            public List<(int index, string fieldName, string before, string after)> Mismatches { get; set; }
                = new List<(int, string, string, string)>();
            public bool IsLossless => MismatchCount == 0;
        }

        /// <summary>
        /// Validate round-trip: export → import → write → re-export → diff.
        /// The ROM is modified in-place (caller should work on a copy).
        /// </summary>
        public static DataRoundTripResult ValidateRoundTrip(ROM rom, string tableName)
        {
            var table = GetTable(tableName);
            if (table == null) throw new ArgumentException($"Unknown table: {tableName}");

            var structDef = LoadStructDef(rom, table);
            if (structDef == null) throw new InvalidOperationException($"Could not load struct definition for table: {tableName}");

            // Phase 1: export
            var export1 = ExportTable(rom, table, structDef);

            // Phase 2: write same data back
            var importEntries = new List<(int index, Dictionary<string, string> fields)>();
            for (int i = 0; i < export1.Count; i++)
            {
                var fields = new Dictionary<string, string>(export1[i]);
                fields.Remove("_Index");
                importEntries.Add((i, fields));
            }
            WriteTable(rom, table, structDef, importEntries);

            // Phase 3: re-export
            var export2 = ExportTable(rom, table, structDef);

            // Phase 4: compare
            var result = new DataRoundTripResult
            {
                TableName = tableName,
                TotalEntries = export1.Count,
            };

            int minCount = Math.Min(export1.Count, export2.Count);
            for (int i = 0; i < minCount; i++)
            {
                bool allMatch = true;
                foreach (var field in structDef.Fields)
                {
                    string v1 = export1[i].TryGetValue(field.Name, out var s1) ? s1 : "";
                    string v2 = export2[i].TryGetValue(field.Name, out var s2) ? s2 : "";
                    if (v1 != v2)
                    {
                        allMatch = false;
                        result.Mismatches.Add((i, field.Name, v1, v2));
                    }
                }
                if (allMatch)
                    result.MatchCount++;
                else
                    result.MismatchCount++;
            }

            // Handle count differences
            for (int i = minCount; i < Math.Max(export1.Count, export2.Count); i++)
            {
                result.MismatchCount++;
                result.Mismatches.Add((i, "(count)", i < export1.Count ? "present" : "missing",
                    i < export2.Count ? "present" : "missing"));
            }

            return result;
        }

        /// <summary>
        /// Validate round-trip for all registered tables.
        /// </summary>
        public static List<DataRoundTripResult> ValidateRoundTripAll(ROM rom)
        {
            var results = new List<DataRoundTripResult>();
            foreach (var name in GetTableNames())
            {
                try
                {
                    results.Add(ValidateRoundTrip(rom, name));
                }
                catch (Exception ex)
                {
                    results.Add(new DataRoundTripResult
                    {
                        TableName = name,
                        MismatchCount = 1,
                        Mismatches = { (-1, "error", ex.Message, "") },
                    });
                }
            }
            return results;
        }
    }
}
