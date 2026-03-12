using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// Lightweight auto-binding helper for Avalonia editors.
    /// Mirrors the core pattern of WinForms InputFormRef: given a ROM address
    /// and struct size, auto-read/write fields based on naming conventions.
    ///
    /// Naming convention:
    ///   B{offset} = byte   (1 byte)   e.g. B5  -> offset 5
    ///   W{offset} = word   (2 bytes)  e.g. W8  -> offset 8
    ///   D{offset} = dword  (4 bytes)  e.g. D12 -> offset 12
    ///   P{offset} = pointer(4 bytes, GBA pointer with 0x08000000 base)
    /// </summary>
    public static class EditorFormRef
    {
        /// <summary>Field width types matching the ROM read/write primitives.</summary>
        public enum FieldType
        {
            Byte,     // 1 byte  – rom.u8 / rom.write_u8
            Word,     // 2 bytes – rom.u16 / rom.write_u16
            DWord,    // 4 bytes – rom.u32 / rom.write_u32
            Pointer   // 4 bytes – rom.p32 / rom.write_p32
        }

        /// <summary>Describes one field in a ROM struct.</summary>
        public class FieldDef
        {
            /// <summary>Original control name, e.g. "B5", "W8".</summary>
            public string Name { get; set; } = "";

            /// <summary>Byte offset within the struct.</summary>
            public uint Offset { get; set; }

            /// <summary>How wide the field is and how to read/write it.</summary>
            public FieldType Type { get; set; }

            /// <summary>Optional human-readable label for display.</summary>
            public string Label { get; set; } = "";

            /// <summary>Byte width of this field (1, 2, or 4).</summary>
            public int ByteSize => Type switch
            {
                FieldType.Byte => 1,
                FieldType.Word => 2,
                FieldType.DWord => 4,
                FieldType.Pointer => 4,
                _ => 1
            };
        }

        // Regex: leading letter (B/W/D/P) followed by decimal offset
        private static readonly Regex FieldNamePattern =
            new Regex(@"^([BWDP])(\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Parse a control name into a FieldDef.
        /// Returns null if the name does not match the B/W/D/P pattern.
        /// </summary>
        public static FieldDef? ParseFieldName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var m = FieldNamePattern.Match(name);
            if (!m.Success)
                return null;

            char prefix = char.ToUpperInvariant(m.Groups[1].Value[0]);
            uint offset = uint.Parse(m.Groups[2].Value);

            FieldType type = prefix switch
            {
                'B' => FieldType.Byte,
                'W' => FieldType.Word,
                'D' => FieldType.DWord,
                'P' => FieldType.Pointer,
                _ => FieldType.Byte
            };

            return new FieldDef
            {
                Name = name.ToUpperInvariant(),
                Offset = offset,
                Type = type
            };
        }

        /// <summary>
        /// Read all fields from ROM at the given base address.
        /// Returns a dictionary keyed by the normalized field name (e.g. "B5").
        /// </summary>
        public static Dictionary<string, uint> ReadFields(ROM rom, uint addr, IEnumerable<FieldDef> fields)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            var result = new Dictionary<string, uint>();
            foreach (var f in fields)
            {
                uint fieldAddr = addr + f.Offset;
                uint value = f.Type switch
                {
                    FieldType.Byte => rom.u8(fieldAddr),
                    FieldType.Word => rom.u16(fieldAddr),
                    FieldType.DWord => rom.u32(fieldAddr),
                    FieldType.Pointer => rom.p32(fieldAddr),
                    _ => 0
                };
                result[f.Name] = value;
            }
            return result;
        }

        /// <summary>
        /// Write field values to ROM at the given base address.
        /// Uses the ambient undo scope if one is active; callers should wrap
        /// with ROM.BeginUndoScope() for undo support.
        /// </summary>
        public static void WriteFields(ROM rom, uint addr, Dictionary<string, uint> values, IEnumerable<FieldDef> fields)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            foreach (var f in fields)
            {
                if (!values.TryGetValue(f.Name, out uint value))
                    continue;

                uint fieldAddr = addr + f.Offset;
                switch (f.Type)
                {
                    case FieldType.Byte:
                        rom.write_u8(fieldAddr, value);
                        break;
                    case FieldType.Word:
                        rom.write_u16(fieldAddr, value);
                        break;
                    case FieldType.DWord:
                        rom.write_u32(fieldAddr, value);
                        break;
                    case FieldType.Pointer:
                        rom.write_p32(fieldAddr, value);
                        break;
                }
            }
        }

        /// <summary>
        /// Write field values to ROM with explicit undo tracking.
        /// </summary>
        public static void WriteFields(ROM rom, uint addr, Dictionary<string, uint> values,
            IEnumerable<FieldDef> fields, Undo.UndoData undoData)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (fields == null) throw new ArgumentNullException(nameof(fields));

            using (ROM.BeginUndoScope(undoData))
            {
                WriteFields(rom, addr, values, fields);
            }
        }

        /// <summary>
        /// Auto-detect FieldDefs from a collection of control names.
        /// Only names matching B/W/D/P + digits are included.
        /// </summary>
        public static List<FieldDef> DetectFields(IEnumerable<string> controlNames)
        {
            if (controlNames == null) throw new ArgumentNullException(nameof(controlNames));

            var result = new List<FieldDef>();
            foreach (var name in controlNames)
            {
                var f = ParseFieldName(name);
                if (f != null)
                    result.Add(f);
            }
            // Sort by offset for deterministic ordering
            result.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            return result;
        }

        /// <summary>
        /// Count entries in a ROM table by scanning from baseAddr.
        /// Stops when isValid returns false or we exceed ROM bounds.
        /// isValid receives (index, entryAddress).
        /// </summary>
        public static int CountEntries(ROM rom, uint baseAddr, uint entrySize,
            Func<int, uint, bool> isValid)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (isValid == null) throw new ArgumentNullException(nameof(isValid));
            if (entrySize == 0) throw new ArgumentException("entrySize must be > 0", nameof(entrySize));

            int count = 0;
            uint dataLength = (uint)rom.Data.Length;
            while (true)
            {
                uint entryAddr = baseAddr + (uint)count * entrySize;
                if (entryAddr + entrySize > dataLength)
                    break;
                if (!isValid(count, entryAddr))
                    break;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Build an address list with display names.
        /// nameFunc receives (index, entryAddress) and returns a display string.
        /// </summary>
        public static List<AddrResult> BuildList(ROM rom, uint baseAddr, uint entrySize,
            int count, Func<int, uint, string> nameFunc)
        {
            if (rom == null) throw new ArgumentNullException(nameof(rom));
            if (nameFunc == null) throw new ArgumentNullException(nameof(nameFunc));

            var list = new List<AddrResult>(count);
            for (int i = 0; i < count; i++)
            {
                uint entryAddr = baseAddr + (uint)i * entrySize;
                string name = nameFunc(i, entryAddr);
                list.Add(new AddrResult(entryAddr, name));
            }
            return list;
        }

        /// <summary>
        /// Convenience: build a list by counting entries first.
        /// </summary>
        public static List<AddrResult> BuildListWithCount(ROM rom, uint baseAddr, uint entrySize,
            Func<int, uint, bool> isValid, Func<int, uint, string> nameFunc)
        {
            int count = CountEntries(rom, baseAddr, entrySize, isValid);
            return BuildList(rom, baseAddr, entrySize, count, nameFunc);
        }
    }
}
