using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// High-level disassembly orchestrator wrapping DisassemblerTrumb.
    /// Loads asmmap symbol files from config/data/ and produces full ROM disassembly.
    /// Does NOT depend on AsmMapFile reflection.
    /// </summary>
    public class DisassemblerCore
    {
        /// <summary>
        /// Disassemble the loaded ROM to a file.
        /// Returns the number of instructions disassembled.
        /// </summary>
        public int DisassembleToFile(string outputPath)
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
                throw new InvalidOperationException("No ROM loaded.");

            // Load symbol map
            var symbolMap = LoadSymbolMap(rom);
            IAsmMapFile asmMap = symbolMap.Count > 0 ? new DictionaryAsmMapFile(symbolMap) : null;

            var disasm = asmMap != null ? new DisassemblerTrumb(asmMap) : new DisassemblerTrumb();
            var vm = new DisassemblerTrumb.VM();

            int instructionCount = 0;
            using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                writer.WriteLine($"; Disassembly of {Path.GetFileName(rom.Filename ?? "unknown")}");
                writer.WriteLine($"; ROM version: {rom.RomInfo?.VersionToFilename ?? "unknown"}");
                writer.WriteLine($"; Size: 0x{rom.Data.Length:X} bytes");
                writer.WriteLine();

                uint pos = 0;
                uint length = (uint)rom.Data.Length;

                while (pos < length)
                {
                    // Check for symbol at this address
                    uint romAddr = pos + 0x08000000;
                    if (symbolMap.TryGetValue(romAddr, out AsmMapSt symbol))
                    {
                        writer.WriteLine();
                        if (!string.IsNullOrEmpty(symbol.Name))
                            writer.WriteLine($"; === {symbol.Name} {symbol.ResultAndArgs} ===");
                    }

                    // Disassemble one instruction
                    var code = disasm.Disassembler(rom.Data, pos, length - pos, vm);
                    if (code == null || string.IsNullOrEmpty(code.ASM))
                    {
                        // Raw data
                        if (pos + 2 <= length)
                        {
                            ushort raw = (ushort)(rom.Data[pos] | (rom.Data[pos + 1] << 8));
                            writer.WriteLine($"  0x{pos + 0x08000000:X08}:  .short 0x{raw:X04}");
                        }
                        pos += 2;
                        instructionCount++;
                        continue;
                    }

                    string comment = string.IsNullOrEmpty(code.Comment) ? "" : $" ; {code.Comment}";
                    writer.WriteLine($"  0x{pos + 0x08000000:X08}:  {code.ASM}{comment}");

                    uint step = code.GetLength();
                    if (step == 0) step = 2;
                    pos += step;
                    instructionCount++;
                }
            }

            return instructionCount;
        }

        /// <summary>
        /// Load symbol map from config/data/asmmap_*.txt files.
        /// </summary>
        Dictionary<uint, AsmMapSt> LoadSymbolMap(ROM rom)
        {
            var result = new Dictionary<uint, AsmMapSt>();

            string baseDir = CoreState.BaseDirectory;
            if (string.IsNullOrEmpty(baseDir))
                return result;

            string dataDir = Path.Combine(baseDir, "config", "data");
            if (!Directory.Exists(dataDir))
                return result;

            if (rom.RomInfo == null) return result;

            // Determine version string for file lookup
            string versionStr = rom.RomInfo.version switch
            {
                6 => "FE6",
                7 => "FE7",
                8 => "FE8",
                _ => null
            };

            if (versionStr == null) return result;

            // Load version-specific map first, then language-specific
            string[] filesToTry = new[]
            {
                Path.Combine(dataDir, $"asmmap_{versionStr}.{CoreState.Language}.txt"),
                Path.Combine(dataDir, $"asmmap_{versionStr}.txt"),
                Path.Combine(dataDir, "asmmap_gba_ALL.txt"),
            };

            foreach (string file in filesToTry)
            {
                if (File.Exists(file))
                    LoadAsmMapFile(file, rom, result);
            }

            return result;
        }

        void LoadAsmMapFile(string path, ROM rom, Dictionary<uint, AsmMapSt> result)
        {
            string versionTag = rom.RomInfo.is_multibyte ? "{J}" : "{U}";

            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//"))
                    continue;

                // Skip struct definitions (start with @)
                if (line.StartsWith("@"))
                    continue;

                string[] parts = line.Split('\t');
                if (parts.Length < 2) continue;

                // Check version tag filtering
                string lastPart = parts[parts.Length - 1].Trim();
                if (lastPart == "{J}" || lastPart == "{U}")
                {
                    if (lastPart != versionTag)
                        continue;
                }

                // Parse address
                string addrStr = parts[0].Trim();
                uint addr = U.atoh(addrStr);
                if (addr == 0) continue;

                var st = new AsmMapSt { Name = parts.Length > 1 ? parts[1].Trim() : "" };

                // Parse remaining fields for return type and args
                var sb = new StringBuilder();
                for (int i = 2; i < parts.Length; i++)
                {
                    string p = parts[i].Trim();
                    if (p.StartsWith("{")) continue; // version tag
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(p);
                }
                st.ResultAndArgs = sb.ToString();

                result[addr] = st;
            }
        }

        /// <summary>
        /// Disassemble the loaded ROM and return lines as a list of strings.
        /// Suitable for in-memory display without writing to a file.
        /// </summary>
        public List<string> DisassembleToLines()
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
                throw new InvalidOperationException("No ROM loaded.");

            var symbolMap = LoadSymbolMap(rom);
            IAsmMapFile asmMap = symbolMap.Count > 0 ? new DictionaryAsmMapFile(symbolMap) : null;

            var disasm = asmMap != null ? new DisassemblerTrumb(asmMap) : new DisassemblerTrumb();
            var vm = new DisassemblerTrumb.VM();

            var lines = new List<string>();
            lines.Add($"; Disassembly of {Path.GetFileName(rom.Filename ?? "unknown")}");
            lines.Add($"; ROM version: {rom.RomInfo?.VersionToFilename ?? "unknown"}");
            lines.Add($"; Size: 0x{rom.Data.Length:X} bytes");
            lines.Add("");

            uint pos = 0;
            uint length = (uint)rom.Data.Length;

            while (pos < length)
            {
                uint romAddr = pos + 0x08000000;
                if (symbolMap.TryGetValue(romAddr, out AsmMapSt symbol))
                {
                    lines.Add("");
                    if (!string.IsNullOrEmpty(symbol.Name))
                        lines.Add($"; === {symbol.Name} {symbol.ResultAndArgs} ===");
                }

                var code = disasm.Disassembler(rom.Data, pos, length - pos, vm);
                if (code == null || string.IsNullOrEmpty(code.ASM))
                {
                    if (pos + 2 <= length)
                    {
                        ushort raw = (ushort)(rom.Data[pos] | (rom.Data[pos + 1] << 8));
                        lines.Add($"  0x{romAddr:X08}:  .short 0x{raw:X04}");
                    }
                    pos += 2;
                    continue;
                }

                string comment = string.IsNullOrEmpty(code.Comment) ? "" : $" ; {code.Comment}";
                lines.Add($"  0x{romAddr:X08}:  {code.ASM}{comment}");

                uint step = code.GetLength();
                if (step == 0) step = 2;
                pos += step;
            }

            return lines;
        }

        /// <summary>
        /// Export IDA MAP format from loaded ROM symbol map.
        /// Returns lines suitable for an IDA .map file.
        /// </summary>
        public List<string> ExportIDAMapLines()
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
                throw new InvalidOperationException("No ROM loaded.");

            var symbolMap = LoadSymbolMap(rom);

            var lines = new List<string>();
            lines.Add(" Start         Length     Name                   Class");

            foreach (var pair in symbolMap)
            {
                if (pair.Key == 0x0 || pair.Key == U.NOT_FOUND)
                    continue;
                if (pair.Key >= 0x08000000 && pair.Key <= 0x08000200)
                    continue;

                string name = pair.Value.Name ?? "";
                int tabIdx = name.IndexOf('\t');
                if (tabIdx >= 0) name = name.Substring(0, tabIdx);

                if (pair.Value.Length > 0)
                    lines.Add($" 0000:{pair.Key:X08} 0{pair.Value.Length:X08}H {name}  DATA");
                else
                    lines.Add($" 0000:{pair.Key:X08}       {name}");
            }

            return lines;
        }

        /// <summary>
        /// Export No$GBA SYM format from loaded ROM symbol map.
        /// Returns lines suitable for a .sym file.
        /// </summary>
        public List<string> ExportNoCashSymLines()
        {
            ROM rom = CoreState.ROM;
            if (rom == null)
                throw new InvalidOperationException("No ROM loaded.");

            var symbolMap = LoadSymbolMap(rom);

            var lines = new List<string>();

            foreach (var pair in symbolMap)
            {
                if (pair.Key == 0x0 || pair.Key == U.NOT_FOUND)
                    continue;
                if (pair.Key >= 0x08000000 && pair.Key <= 0x08000200)
                    continue;

                string name = pair.Value.Name ?? "";
                int tabIdx = name.IndexOf('\t');
                if (tabIdx >= 0) name = name.Substring(0, tabIdx);
                name = SanitizeNoCashName(name);
                if (string.IsNullOrEmpty(name))
                    continue;

                // No$GBA SYM format: address .type\naddress name
                string typeTag = pair.Value.TypeName == "ARM" ? ".arm" : ".thumb";
                uint compressBorder = rom.RomInfo.compress_image_borderline_address;
                if (pair.Value.TypeName == "ARM")
                    lines.Add($"{pair.Key:X08} {typeTag}");
                else if (pair.Value.TypeName == "ASM" || U.toOffset(pair.Key) < compressBorder)
                    lines.Add($"{pair.Key:X08} {typeTag}");

                lines.Add($"{pair.Key:X08} {name}");
            }

            return lines;
        }

        /// <summary>
        /// Sanitize a symbol name for No$GBA debugger (replace control chars, spaces).
        /// </summary>
        static string SanitizeNoCashName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c < 0x20) continue;       // strip control chars
                if (c == 0x20) c = '_';         // space -> underscore
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Simple IAsmMapFile backed by a dictionary.
        /// </summary>
        class DictionaryAsmMapFile : IAsmMapFile
        {
            readonly Dictionary<uint, AsmMapSt> _map;

            public DictionaryAsmMapFile(Dictionary<uint, AsmMapSt> map)
            {
                _map = map;
            }

            public bool TryGetValue(uint pointer, out AsmMapSt out_p)
            {
                return _map.TryGetValue(pointer, out out_p);
            }
        }
    }
}
