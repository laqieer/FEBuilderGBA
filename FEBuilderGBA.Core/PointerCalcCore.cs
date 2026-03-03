using System;
using System.Collections.Generic;
using System.Text;

namespace FEBuilderGBA
{
    /// <summary>
    /// Cross-platform pointer search logic extracted from WinForms PointerToolForm.
    /// Searches a target ROM for addresses that match data patterns from a source ROM.
    /// </summary>
    public static class PointerCalcCore
    {
        /// <summary>Result of a pointer search.</summary>
        public class SearchResult
        {
            public uint SourceAddress { get; set; }
            public uint TargetAddress { get; set; }
            public string MatchType { get; set; } // "Pointer", "LDR", "Data"
        }

        /// <summary>
        /// Build skip data set from pointer references in ROM.
        /// Pointers are 4-byte aligned GBA addresses (0x08xxxxxx).
        /// </summary>
        public static HashSet<uint> MakeSkipDataByPointer(byte[] romData)
        {
            var skip = new HashSet<uint>();
            if (romData == null || romData.Length < 4) return skip;

            for (uint i = 0; i + 3 < romData.Length; i += 4)
            {
                uint val = (uint)(romData[i] | (romData[i + 1] << 8) |
                                  (romData[i + 2] << 16) | (romData[i + 3] << 24));
                if (val >= 0x08000000 && val < 0x08000000 + (uint)romData.Length)
                {
                    uint offset = val - 0x08000000;
                    skip.Add(offset);
                }
            }
            return skip;
        }

        /// <summary>
        /// Build skip data set from LDR (load register) code references.
        /// Scans Thumb code for LDR Rn, [PC, #imm] patterns.
        /// </summary>
        public static HashSet<uint> MakeSkipDataByCode(byte[] romData)
        {
            var skip = new HashSet<uint>();
            if (romData == null || romData.Length < 4) return skip;

            // Scan for Thumb LDR Rn, [PC, #imm] instructions (opcode 0x48xx-0x4Fxx)
            for (uint i = 0; i + 1 < romData.Length; i += 2)
            {
                ushort inst = (ushort)(romData[i] | (romData[i + 1] << 8));
                if ((inst & 0xF800) == 0x4800) // LDR Rn, [PC, #imm]
                {
                    uint offset = (uint)((inst & 0xFF) * 4);
                    uint pcAddr = (i + 4) & ~3u; // PC is aligned
                    uint targetOffset = pcAddr + offset;
                    if (targetOffset + 3 < romData.Length)
                    {
                        skip.Add(targetOffset);
                    }
                }
            }
            return skip;
        }

        /// <summary>
        /// Search for matching data between source and target ROMs.
        /// </summary>
        /// <param name="sourceData">Source ROM data</param>
        /// <param name="targetData">Target ROM data to search in</param>
        /// <param name="searchAddresses">Addresses to search for in source ROM</param>
        /// <param name="searchLength">Number of bytes to compare per address</param>
        /// <returns>List of search results</returns>
        public static List<SearchResult> SearchAddresses(byte[] sourceData, byte[] targetData,
            List<uint> searchAddresses, int searchLength = 16)
        {
            var results = new List<SearchResult>();
            if (sourceData == null || targetData == null) return results;
            if (searchLength < 4) searchLength = 4;

            var targetPointers = MakeSkipDataByPointer(targetData);

            foreach (uint srcAddr in searchAddresses)
            {
                if (srcAddr + searchLength > sourceData.Length) continue;

                // Extract pattern from source ROM
                byte[] pattern = new byte[searchLength];
                Array.Copy(sourceData, srcAddr, pattern, 0, searchLength);

                // Search in target ROM by pointer reference
                uint gbaPointer = srcAddr + 0x08000000;
                byte[] ptrBytes = new byte[4];
                ptrBytes[0] = (byte)(gbaPointer & 0xFF);
                ptrBytes[1] = (byte)((gbaPointer >> 8) & 0xFF);
                ptrBytes[2] = (byte)((gbaPointer >> 16) & 0xFF);
                ptrBytes[3] = (byte)((gbaPointer >> 24) & 0xFF);

                // Search target for same GBA pointer
                for (uint t = 0; t + 3 < targetData.Length; t += 4)
                {
                    if (targetData[t] == ptrBytes[0] && targetData[t + 1] == ptrBytes[1] &&
                        targetData[t + 2] == ptrBytes[2] && targetData[t + 3] == ptrBytes[3])
                    {
                        results.Add(new SearchResult
                        {
                            SourceAddress = srcAddr,
                            TargetAddress = t,
                            MatchType = "Pointer"
                        });
                    }
                }

                // Search target for matching data content
                for (uint t = 0; t + searchLength <= targetData.Length; t += 4)
                {
                    bool match = true;
                    for (int b = 0; b < searchLength; b++)
                    {
                        if (targetData[t + b] != pattern[b]) { match = false; break; }
                    }
                    if (match)
                    {
                        results.Add(new SearchResult
                        {
                            SourceAddress = srcAddr,
                            TargetAddress = t,
                            MatchType = "Data"
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Parse address list from string (comma-separated hex addresses or file path).
        /// </summary>
        public static List<uint> ParseAddressList(string input)
        {
            var result = new List<uint>();
            if (string.IsNullOrEmpty(input)) return result;

            // Try to read as file
            if (System.IO.File.Exists(input))
            {
                string content = System.IO.File.ReadAllText(input);
                input = content;
            }

            // Parse comma/newline separated hex addresses
            string[] parts = input.Split(new[] { ',', '\n', '\r', ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(2);
                if (uint.TryParse(trimmed, System.Globalization.NumberStyles.HexNumber, null, out uint addr))
                    result.Add(addr);
            }
            return result;
        }
    }
}
