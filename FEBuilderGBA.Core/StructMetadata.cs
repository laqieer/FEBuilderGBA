using System;
using System.Collections.Generic;
using System.IO;

namespace FEBuilderGBA
{
    /// <summary>
    /// Config-driven struct definitions replacing AsmMapFile reflection-based Form scanning.
    /// Loads struct layout metadata from text files in config/data/.
    /// </summary>
    public class StructMetadata
    {
        /// <summary>Field type in a struct definition.</summary>
        public enum FieldType
        {
            Byte,    // B - 1 byte
            Word,    // W - 2 bytes (uint16)
            DWord,   // D - 4 bytes (uint32)
            Pointer, // P - 4 bytes (GBA pointer)
        }

        /// <summary>A single field in a struct.</summary>
        public class FieldDef
        {
            public string Name { get; set; }
            public uint Offset { get; set; }
            public FieldType Type { get; set; }
            public string Comment { get; set; }

            public int Size => Type switch
            {
                FieldType.Byte => 1,
                FieldType.Word => 2,
                FieldType.DWord => 4,
                FieldType.Pointer => 4,
                _ => 1,
            };
        }

        /// <summary>A struct definition with its fields.</summary>
        public class StructDef
        {
            public string Name { get; set; }
            public uint DataSize { get; set; }
            public List<FieldDef> Fields { get; set; } = new List<FieldDef>();

            /// <summary>Read a value from ROM at the given struct base + field offset.</summary>
            public uint ReadField(ROM rom, uint baseAddr, FieldDef field)
            {
                uint addr = baseAddr + field.Offset;
                return field.Type switch
                {
                    FieldType.Byte => rom.u8(addr),
                    FieldType.Word => rom.u16(addr),
                    FieldType.DWord => rom.u32(addr),
                    FieldType.Pointer => rom.u32(addr),
                    _ => rom.u8(addr),
                };
            }

            /// <summary>Write a value to ROM at the given struct base + field offset.</summary>
            public void WriteField(ROM rom, uint baseAddr, FieldDef field, uint value)
            {
                uint addr = baseAddr + field.Offset;
                switch (field.Type)
                {
                    case FieldType.Byte:
                        rom.write_u8(addr, value);
                        break;
                    case FieldType.Word:
                        rom.write_u16(addr, value);
                        break;
                    case FieldType.DWord:
                        rom.write_u32(addr, value);
                        break;
                    case FieldType.Pointer:
                        rom.write_u32(addr, value);
                        break;
                }
            }
        }

        readonly Dictionary<string, StructDef> _structs = new Dictionary<string, StructDef>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Load struct definitions from a text file.</summary>
        /// <remarks>
        /// Format: one line per field, blank lines separate structs.
        /// First line of each struct: @StructName SIZE
        /// Field lines: OFFSET TYPE NAME [COMMENT]
        /// TYPE: B=byte, W=word, D=dword, P=pointer
        /// Lines starting with // are comments.
        /// </remarks>
        public void LoadFromFile(string path)
        {
            if (!File.Exists(path)) return;

            string[] lines = File.ReadAllLines(path);
            StructDef current = null;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                    continue;

                if (line.StartsWith("@"))
                {
                    // New struct: @Name SIZE
                    string[] parts = line.Substring(1).Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    current = new StructDef
                    {
                        Name = parts[0],
                        DataSize = U.atoh(parts[1])
                    };
                    _structs[current.Name] = current;
                    continue;
                }

                if (current == null) continue;

                // Field: OFFSET TYPE NAME [COMMENT]
                string[] fieldParts = line.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fieldParts.Length < 3) continue;

                FieldType ft = ParseFieldType(fieldParts[1]);
                current.Fields.Add(new FieldDef
                {
                    Offset = U.atoh(fieldParts[0]),
                    Type = ft,
                    Name = fieldParts[2],
                    Comment = fieldParts.Length > 3 ? fieldParts[3] : ""
                });
            }
        }

        /// <summary>Load all struct metadata from the config/data directory.</summary>
        public void LoadFromConfigDir()
        {
            string dir = Path.Combine(CoreState.BaseDirectory, "config", "data");
            string path = Path.Combine(dir, "struct_metadata.txt");
            if (File.Exists(path))
                LoadFromFile(path);

            // Also load version-specific file
            if (CoreState.ROM?.RomInfo != null)
            {
                string versionFile = Path.Combine(dir, $"struct_metadata_{CoreState.ROM.RomInfo.TitleToFilename}.txt");
                if (File.Exists(versionFile))
                    LoadFromFile(versionFile);
            }
        }

        /// <summary>Get a struct definition by name.</summary>
        public StructDef GetStruct(string name)
        {
            _structs.TryGetValue(name, out StructDef def);
            return def;
        }

        /// <summary>Get all loaded struct definitions.</summary>
        public IReadOnlyDictionary<string, StructDef> AllStructs => _structs;

        static FieldType ParseFieldType(string s)
        {
            return s.ToUpperInvariant() switch
            {
                "B" => FieldType.Byte,
                "W" => FieldType.Word,
                "D" => FieldType.DWord,
                "P" => FieldType.Pointer,
                "BYTE" => FieldType.Byte,
                "WORD" => FieldType.Word,
                "DWORD" => FieldType.DWord,
                "POINTER" => FieldType.Pointer,
                _ => FieldType.Byte,
            };
        }
    }
}
