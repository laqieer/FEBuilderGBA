using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FEBuilderGBA
{
    /// <summary>
    /// One field parsed out of a No$gba memory map (".nmm") field block (#1150).
    /// Mirrors the FormatNMM per-field grammar: Name, Offset, Size, TypeCode,
    /// dropdown-filename, blank. A field that cannot map to a fixed 1/2/4-byte source
    /// column (pointer / var-length / unusual size) is flagged
    /// <see cref="Unsupported"/> — but NEVER dropped — so a contributor sees the gap.
    /// </summary>
    public sealed class NmmField
    {
        /// <summary>Field name (from the NMM block). Never null.</summary>
        public string Name = "";

        /// <summary>Byte offset within the entry (decimal in the NMM grammar).</summary>
        public int Offset;

        /// <summary>Field byte size (1/2/4 supported; other values flag unsupported).</summary>
        public int Size;

        /// <summary>The raw NMM type-code token (e.g. "NEHU"). Never null.</summary>
        public string TypeCode = "";

        /// <summary>True when this field cannot be a fixed-width source column.</summary>
        public bool Unsupported;

        /// <summary>Human reason when <see cref="Unsupported"/> is set. Never null.</summary>
        public string UnsupportedReason = "";
    }

    /// <summary>
    /// Typed result of parsing a No$gba memory map (#1150). <see cref="Ok"/> is false
    /// when the header is unusable; per-field faults add to <see cref="Warnings"/> but
    /// never throw and never silently drop a field.
    /// </summary>
    public sealed class NmmParseResult
    {
        /// <summary>True when the header was parsed into a usable shape.</summary>
        public bool Ok;

        /// <summary>Module/struct name (title with " by FEBuilderGBA" stripped). Never null.</summary>
        public string ModuleName = "";

        /// <summary>Parsed base address (0 when the line was missing/garbage + a warning).</summary>
        public uint BaseAddress;

        /// <summary>Entry count from the header.</summary>
        public int EntryCount;

        /// <summary>Block (entry) size from the header.</summary>
        public int BlockSize;

        /// <summary>Parsed fields, in NMM order. Unsupported fields are flagged, not dropped.</summary>
        public List<NmmField> Fields = new List<NmmField>();

        /// <summary>Non-fatal diagnostics (bad base addr, odd size, etc.).</summary>
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// PURE, never-throwing bridge between FEBuilder's No$gba memory map (".nmm") format
    /// — produced by <see cref="StructExportCore.FormatNMM"/> — and the decomp manifest
    /// <c>tables[]</c> schema (#1150).
    ///
    /// This is a SCHEMA AID, not a writability path: parsing an NMM tells a contributor
    /// the field layout (names/offsets/sizes) of a ROM table so they can seed a manifest
    /// table-owner entry, but it does NOT make pointer/var-length fields source-writable.
    /// Such fields survive the round-trip FLAGGED as unsupported.
    ///
    /// Every public method is fully guarded and NEVER throws.
    /// </summary>
    public static class NmmSchemaBridgeCore
    {
        const string TitleSuffix = " by FEBuilderGBA";

        /// <summary>
        /// Parse the FormatNMM grammar in the inverse direction. Header: magic "1",
        /// title (→ ModuleName, " by FEBuilderGBA" stripped), 0x base address, entry
        /// count, block size, two NULL lines, a blank; then repeating 6-line field
        /// blocks (Name, decimal Offset, Size, TypeCode, dropdown-filename, blank).
        ///
        /// A field is flagged <see cref="NmmField.Unsupported"/> (never dropped) when
        /// its Size is not in {1,2,4} OR its TypeCode/Name signals a pointer
        /// (a 'P'/"PTR"/"POINTER" token). Header faults add Warnings and set
        /// <see cref="NmmParseResult.Ok"/> false where the header is unusable. NEVER throws.
        /// </summary>
        public static NmmParseResult ParseNmm(string nmmText)
        {
            var result = new NmmParseResult();
            try
            {
                if (string.IsNullOrEmpty(nmmText))
                {
                    result.Ok = false;
                    result.Warnings.Add("Empty NMM text.");
                    return result;
                }

                // Split into lines, keeping blank lines (they delimit field blocks).
                string[] lines = nmmText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
                int i = 0;

                // --- magic "1" ---
                if (i >= lines.Length || lines[i].Trim() != "1")
                {
                    result.Ok = false;
                    result.Warnings.Add("Missing or wrong NMM magic line (expected '1').");
                    return result;
                }
                i++;

                // --- title -> ModuleName ---
                if (i >= lines.Length)
                {
                    result.Ok = false;
                    result.Warnings.Add("Missing NMM title line.");
                    return result;
                }
                string title = lines[i].Trim();
                if (title.EndsWith(TitleSuffix, StringComparison.Ordinal))
                    title = title.Substring(0, title.Length - TitleSuffix.Length);
                result.ModuleName = title;
                i++;

                // --- base address (tolerant 0x parse) ---
                if (i >= lines.Length)
                {
                    result.Ok = false;
                    result.Warnings.Add("Missing NMM base-address line.");
                    return result;
                }
                if (!TryParseHexAddr(lines[i].Trim(), out uint baseAddr))
                {
                    result.BaseAddress = 0;
                    result.Warnings.Add($"Could not parse base address '{lines[i].Trim()}' — defaulting to 0x0.");
                }
                else
                {
                    result.BaseAddress = baseAddr;
                }
                i++;

                // --- entry count ---
                if (i >= lines.Length || !int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int entryCount))
                {
                    result.Ok = false;
                    result.Warnings.Add("Missing or non-numeric NMM entry-count line.");
                    return result;
                }
                result.EntryCount = entryCount;
                i++;

                // --- block size ---
                if (i >= lines.Length || !int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int blockSize))
                {
                    result.Ok = false;
                    result.Warnings.Add("Missing or non-numeric NMM block-size line.");
                    return result;
                }
                result.BlockSize = blockSize;
                i++;

                // --- two NULL lines + a blank (tolerant: skip up to two NULLs and one blank) ---
                // Skip exactly two header NULL lines if present.
                int nulls = 0;
                while (i < lines.Length && nulls < 2 && lines[i].Trim() == "NULL") { i++; nulls++; }
                // Skip a single blank separator if present.
                if (i < lines.Length && lines[i].Trim().Length == 0) i++;

                // Header is usable from here on.
                result.Ok = true;

                // --- repeating 6-line field blocks ---
                while (i < lines.Length)
                {
                    // Skip extra blank lines between blocks / trailing newline.
                    if (lines[i].Trim().Length == 0) { i++; continue; }

                    var f = new NmmField();
                    f.Name = lines[i].Trim();
                    i++;

                    // Offset (decimal).
                    if (i < lines.Length && int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int off))
                        f.Offset = off;
                    else
                    {
                        result.Warnings.Add($"Field '{f.Name}': missing/invalid offset line.");
                    }
                    i++;

                    // Size.
                    if (i < lines.Length && int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sz))
                        f.Size = sz;
                    else
                    {
                        result.Warnings.Add($"Field '{f.Name}': missing/invalid size line.");
                    }
                    i++;

                    // TypeCode.
                    if (i < lines.Length)
                        f.TypeCode = lines[i].Trim();
                    i++;

                    // dropdown filename (ignored).
                    if (i < lines.Length) i++;

                    // trailing blank (consumed by the top-of-loop skip if present).

                    ClassifyUnsupported(f);
                    result.Fields.Add(f);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Ok = false;
                result.Warnings.Add($"Unexpected fault: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Build a single decomp manifest <c>tables[]</c> entry JSON object from a parsed
        /// NMM (#1150). The entry carries <c>table</c>=<paramref name="tableName"/>,
        /// <c>entrySize</c>=BlockSize, <c>count</c>=EntryCount, and one <c>fields[]</c>
        /// element per parsed field (Name/Offset/Width=Size; unsupported fields add an
        /// Extra marker <c>"unsupported": true</c> + <c>"unsupportedReason"</c>). The
        /// base address is recorded in the entry's <c>Extra</c> bag as
        /// <c>"baseAddress"</c> (hex). Serialized with <see cref="System.Text.Json"/>
        /// (indented, proper escaping) via typed DTOs — never hand-concatenated.
        /// Returns "{}" on any fault. NEVER throws.
        /// </summary>
        public static string BuildManifestTablesEntry(NmmParseResult parsed, string tableName)
        {
            try
            {
                if (parsed == null)
                    return "{}";

                var entry = new DecompTableEntry
                {
                    Table = string.IsNullOrEmpty(tableName) ? "table" : tableName,
                    EntrySize = parsed.BlockSize,
                    Count = parsed.EntryCount,
                    Fields = new List<DecompTableField>(),
                    Extra = new Dictionary<string, JsonElement>(),
                };

                // baseAddress (hex) in the entry's Extra bag.
                entry.Extra["baseAddress"] = JsonElementOf("0x" + parsed.BaseAddress.ToString("X", CultureInfo.InvariantCulture));

                foreach (NmmField nf in parsed.Fields ?? new List<NmmField>())
                {
                    var tf = new DecompTableField
                    {
                        Name = nf.Name,
                        Offset = nf.Offset,
                        Width = nf.Size,
                    };
                    if (nf.Unsupported)
                    {
                        tf.Extra = new Dictionary<string, JsonElement>
                        {
                            ["unsupported"] = JsonElementOf(true),
                            ["unsupportedReason"] = JsonElementOf(nf.UnsupportedReason ?? ""),
                        };
                    }
                    entry.Fields.Add(tf);
                }

                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                };
                return JsonSerializer.Serialize(entry, opts);
            }
            catch
            {
                return "{}";
            }
        }

        /// <summary>
        /// Emit ".nmm" text for a manifest table-owner entry (#1150), reusing the
        /// FormatNMM grammar. The header's ModuleName is <paramref name="owner"/>.Table;
        /// the base address is read from <c>owner.Extra["baseAddress"]</c> when it is a
        /// hex string (else "0x0" + a warning); EntryCount=<c>owner.Count ?? 0</c>;
        /// BlockSize=<c>owner.EntrySize ?? 0</c>. Per field: Name, decimal Offset
        /// (falls back to the running field index when null), Size from Width (1/2/4;
        /// a null/odd Width emits a best-effort value + a warning), TypeCode "NEHU",
        /// "NULL", blank. A field whose Width is pointer-like or whose Extra marks it
        /// unsupported emits a warning so it is clearly flagged unsafe. Collects all
        /// diagnostics in <paramref name="warnings"/>. NEVER throws.
        /// </summary>
        public static string ExportTableToNmm(DecompTableEntry owner, out List<string> warnings)
        {
            warnings = new List<string>();
            try
            {
                if (owner == null)
                {
                    warnings.Add("Owner is null — emitting an empty NMM stub.");
                    return BuildNmmStub();
                }

                string moduleName = string.IsNullOrEmpty(owner.Table) ? "table" : owner.Table;

                // base address from Extra["baseAddress"].
                string baseHex = "0x0";
                if (owner.Extra != null && owner.Extra.TryGetValue("baseAddress", out JsonElement baseEl)
                    && baseEl.ValueKind == JsonValueKind.String)
                {
                    string raw = baseEl.GetString() ?? "";
                    if (TryParseHexAddr(raw, out uint addr))
                        baseHex = "0x" + addr.ToString("X", CultureInfo.InvariantCulture);
                    else
                    {
                        warnings.Add($"baseAddress '{raw}' is not a hex string — defaulting to 0x0.");
                    }
                }
                else
                {
                    warnings.Add("Owner has no baseAddress in Extra — defaulting to 0x0.");
                }

                int entryCount = owner.Count ?? 0;
                int blockSize = owner.EntrySize ?? 0;

                var sb = new StringBuilder();
                sb.AppendLine("1");
                sb.AppendLine(moduleName + TitleSuffix);
                sb.AppendLine(baseHex);
                sb.AppendLine(entryCount.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine(blockSize.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("NULL");
                sb.AppendLine("NULL");
                sb.AppendLine();

                int idx = 0;
                foreach (DecompTableField f in owner.Fields ?? new List<DecompTableField>())
                {
                    string name = f?.Name ?? ("field_" + idx);

                    int offset = f?.Offset ?? idx;

                    int size;
                    int? width = f?.Width;
                    if (width.HasValue && (width.Value == 1 || width.Value == 2 || width.Value == 4))
                    {
                        size = width.Value;
                    }
                    else
                    {
                        // best-effort: clamp a null/odd width to 1 and warn.
                        size = (width.HasValue && width.Value > 0) ? width.Value : 1;
                        warnings.Add($"Field '{name}': width {(width.HasValue ? width.Value.ToString() : "null")} is not 1/2/4 — emitting best-effort size {size}.");
                    }

                    // pointer-like / unsupported flags.
                    if (size == 4 && IsExtraPointerOrVar(f))
                        warnings.Add($"Field '{name}': flagged pointer/var-length — NMM column is informational only, NOT source-writable.");
                    if (IsExtraUnsupported(f))
                        warnings.Add($"Field '{name}': marked unsupported in the manifest — NMM column is informational only, NOT source-writable.");

                    sb.AppendLine(name);
                    sb.AppendLine(offset.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine(size.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("NEHU");
                    sb.AppendLine("NULL");
                    sb.AppendLine();

                    idx++;
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                warnings.Add($"Unexpected fault: {ex.Message}");
                return BuildNmmStub();
            }
        }

        // -------------------------------------------------------------- helpers

        /// <summary>Flag a parsed NMM field unsupported when its size or type signals pointer/var.</summary>
        static void ClassifyUnsupported(NmmField f)
        {
            if (f == null) return;

            if (f.Size != 1 && f.Size != 2 && f.Size != 4)
            {
                f.Unsupported = true;
                f.UnsupportedReason = $"Size {f.Size} is not a fixed 1/2/4-byte column.";
                return;
            }

            string tc = (f.TypeCode ?? "").ToUpperInvariant();
            string nm = (f.Name ?? "").ToUpperInvariant();
            // A 'P' token, "PTR"/"POINTER" in the type code or name signals a pointer field.
            if (tc.Contains("PTR") || tc.Contains("POINTER") || nm.Contains("POINTER")
                || nm.Contains("PTR") || nm.EndsWith("PTR"))
            {
                f.Unsupported = true;
                f.UnsupportedReason = "Pointer field — informational schema only, not source-writable.";
            }
        }

        /// <summary>True when the owner field's Extra bag declares a pointer/var kind.</summary>
        static bool IsExtraPointerOrVar(DecompTableField f)
        {
            try
            {
                if (f?.Extra == null) return false;
                if (f.Extra.TryGetValue("kind", out JsonElement k) && k.ValueKind == JsonValueKind.String)
                {
                    string s = (k.GetString() ?? "").ToLowerInvariant();
                    return s.Contains("pointer") || s.Contains("ptr") || s.Contains("var");
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>True when the owner field's Extra bag marks it unsupported.</summary>
        static bool IsExtraUnsupported(DecompTableField f)
        {
            try
            {
                if (f?.Extra == null) return false;
                return f.Extra.TryGetValue("unsupported", out JsonElement u)
                    && u.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }

        /// <summary>Tolerant parse of a 0x-prefixed (or bare) hex address. NEVER throws.</summary>
        static bool TryParseHexAddr(string s, out uint addr)
        {
            addr = 0;
            try
            {
                if (string.IsNullOrEmpty(s)) return false;
                string clean = s.Trim();
                if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    clean = clean.Substring(2);
                return uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
            }
            catch { return false; }
        }

        /// <summary>Wrap a value as a JsonElement (via a throwaway serialize/parse). NEVER throws.</summary>
        static JsonElement JsonElementOf(object value)
        {
            try
            {
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
                return doc.RootElement.Clone();
            }
            catch
            {
                using var doc = JsonDocument.Parse("null");
                return doc.RootElement.Clone();
            }
        }

        /// <summary>Minimal valid NMM header with no fields (used on a fault).</summary>
        static string BuildNmmStub()
        {
            var sb = new StringBuilder();
            sb.AppendLine("1");
            sb.AppendLine("table" + TitleSuffix);
            sb.AppendLine("0x0");
            sb.AppendLine("0");
            sb.AppendLine("0");
            sb.AppendLine("NULL");
            sb.AppendLine("NULL");
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
