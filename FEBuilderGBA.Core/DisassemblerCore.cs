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
                writer.WriteLine($"; Disassembly of {Path.GetFileName(rom.Filename)}");
                writer.WriteLine($"; ROM version: {rom.RomInfo.VersionToFilename}");
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
