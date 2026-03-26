using System;
using System.Collections.Generic;
using System.Linq;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Helper for comparing Avalonia editor lists against reference lists
    /// generated directly from ROM data using the same pointer/size info
    /// that WinForms InputFormRef uses.
    /// </summary>
    public static class ListParityHelper
    {
        /// <summary>
        /// Delegate for building a reference list from ROM data.
        /// </summary>
        public delegate List<AddrResult> ReferenceListBuilder(ROM rom);

        /// <summary>
        /// Maps Avalonia editor view name to a reference list builder function and
        /// the WinForms form name (for reporting).
        /// </summary>
        static readonly Dictionary<string, (string WinFormsName, ReferenceListBuilder Builder)> EditorMap
            = new(StringComparer.Ordinal);

        static ListParityHelper()
        {
            // Data Editors - these use well-known ROM pointers
            Register("UnitEditorView", "UnitForm", BuildUnitList);
            Register("ItemEditorView", "ItemForm", BuildItemList);
            Register("ClassEditorView", "ClassForm", BuildClassList);
            Register("PortraitViewerView", "ImagePortraitForm", BuildPortraitList);
            Register("ImagePortraitView", "ImagePortraitForm", BuildPortraitList);
            Register("ImageGenericEnemyPortraitView", "ImageGenericEnemyPortraitForm", BuildGenericEnemyPortraitList);
            Register("SoundRoomViewerView", "SoundRoomForm", BuildSoundRoomList);
        }

        static void Register(string avaloniaName, string winFormsName, ReferenceListBuilder builder)
        {
            EditorMap[avaloniaName] = (winFormsName, builder);
        }

        /// <summary>Check if a given Avalonia editor name has a known reference list builder.</summary>
        public static bool HasMapping(string avaloniaEditorName) => EditorMap.ContainsKey(avaloniaEditorName);

        /// <summary>Get the WinForms form name for reporting.</summary>
        public static (string FormType, string MethodName)? GetMapping(string avaloniaEditorName)
        {
            if (EditorMap.TryGetValue(avaloniaEditorName, out var entry))
                return (entry.WinFormsName, "MakeList");
            return null;
        }

        /// <summary>Get all mapped editor names.</summary>
        public static IReadOnlyCollection<string> GetAllMappedEditors() => EditorMap.Keys;

        /// <summary>
        /// Build a reference list for the given editor using Core ROM data.
        /// Returns null if the editor has no mapping.
        /// </summary>
        public static List<AddrResult> BuildReferenceList(string avaloniaEditorName)
        {
            if (!EditorMap.TryGetValue(avaloniaEditorName, out var entry))
                return null;

            var rom = CoreState.ROM;
            if (rom?.RomInfo == null)
                return null;

            try
            {
                return entry.Builder(rom);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LISTPARITY: Error building reference list for {avaloniaEditorName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Compare two lists of AddrResult row-by-row.
        /// Returns a comparison result.
        /// </summary>
        public static ListParityResult CompareLists(string editorName, List<AddrResult> avaloniaList, List<AddrResult> referenceList)
        {
            var result = new ListParityResult
            {
                EditorName = editorName,
                AvaloniaCount = avaloniaList.Count,
                WinFormsCount = referenceList.Count,
            };

            int minCount = Math.Min(avaloniaList.Count, referenceList.Count);
            int textMatches = 0;

            for (int i = 0; i < minCount; i++)
            {
                var av = avaloniaList[i];
                var rf = referenceList[i];

                bool addrMatch = av.addr == rf.addr;
                // Normalize whitespace and compare text (trim leading/trailing)
                string avText = (av.name ?? "").Trim();
                string rfText = (rf.name ?? "").Trim();
                bool textMatch = string.Equals(avText, rfText, StringComparison.Ordinal);

                if (textMatch)
                    textMatches++;

                if (!addrMatch && result.FirstAddrDiffIndex < 0)
                {
                    result.FirstAddrDiffIndex = i;
                    result.FirstAddrDiffAvalonia = av.addr;
                    result.FirstAddrDiffWinForms = rf.addr;
                }

                if (!textMatch && result.FirstTextDiffIndex < 0)
                {
                    result.FirstTextDiffIndex = i;
                    result.FirstTextDiffAvalonia = avText;
                    result.FirstTextDiffWinForms = rfText;
                }
            }

            result.TextMatches = textMatches;
            result.IsMatch = avaloniaList.Count == referenceList.Count
                          && result.FirstAddrDiffIndex < 0
                          && result.FirstTextDiffIndex < 0;

            return result;
        }

        // ------------------------------------------------------------------
        // Reference list builders — replicate InputFormRef.MakeList() logic
        // using Core ROM data directly, matching the Avalonia VM patterns
        // ------------------------------------------------------------------

        static string GetTextById(uint id)
        {
            try { return NameResolver.GetTextById(id); }
            catch { return "???"; }
        }

        /// <summary>Build unit list matching UnitEditorViewModel.LoadUnitList().</summary>
        static List<AddrResult> BuildUnitList(ROM rom)
        {
            uint ptr = rom.RomInfo.unit_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.unit_datasize;
            uint maxCount = rom.RomInfo.unit_maxcount;
            if (maxCount == 0) maxCount = 0x100;

            // FE6: skip first entry
            if (rom.RomInfo.version == 6)
                baseAddr += dataSize;

            var result = new List<AddrResult>();
            for (uint i = 0; i < maxCount; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                uint nameId = rom.u16(addr);
                string name = U.ToHexString(i + 1) + " " + GetTextById(nameId);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build item list matching ItemEditorViewModel.LoadItemList().</summary>
        static List<AddrResult> BuildItemList(ROM rom)
        {
            uint ptr = rom.RomInfo.item_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.item_datasize;
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                uint nameId = rom.u16(addr);
                string name = U.ToHexString(i) + " " + GetTextById(nameId);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build class list matching ClassEditorViewModel.LoadClassList().</summary>
        static List<AddrResult> BuildClassList(ROM rom)
        {
            uint ptr = rom.RomInfo.class_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.class_datasize;
            // Class count is typically 0x100
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                uint nameId = rom.u16(addr);
                string name = U.ToHexString(i) + " " + GetTextById(nameId);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build portrait list matching PortraitViewerViewModel.</summary>
        static List<AddrResult> BuildPortraitList(ROM rom)
        {
            uint ptr = rom.RomInfo.portrait_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.portrait_datasize;
            if (dataSize == 0) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                string name = U.ToHexString(i) + " Portrait";
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build generic enemy portrait list.</summary>
        static List<AddrResult> BuildGenericEnemyPortraitList(ROM rom)
        {
            uint ptr = rom.RomInfo.generic_enemy_portrait_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint count = rom.RomInfo.generic_enemy_portrait_count;
            if (count == 0) return new List<AddrResult>();

            // Each entry is a pointer (4 bytes)
            uint dataSize = 4;
            var result = new List<AddrResult>();
            for (uint i = 0; i < count; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;
                string name = U.ToHexString(i);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        /// <summary>Build sound room list.</summary>
        static List<AddrResult> BuildSoundRoomList(ROM rom)
        {
            uint ptr = rom.RomInfo.sound_room_pointer;
            if (ptr == 0) return new List<AddrResult>();
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            uint dataSize = rom.RomInfo.sound_room_datasize;
            if (dataSize == 0) dataSize = 16;

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = baseAddr + i * dataSize;
                if (addr + dataSize > (uint)rom.Data.Length) break;

                // Check for end-of-list sentinel (all zeros or invalid)
                uint songId = rom.u16(addr + 0);
                if (songId == 0 && i > 0) break; // End of list

                uint textId = rom.u16(addr + 2);
                string name = U.ToHexString(i) + " " + GetTextById(textId);
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }
    }

    /// <summary>Result of a list parity comparison for one editor.</summary>
    public class ListParityResult
    {
        public string EditorName { get; set; }
        public int AvaloniaCount { get; set; }
        public int WinFormsCount { get; set; }
        public int TextMatches { get; set; }
        public bool IsMatch { get; set; }

        /// <summary>Index of first address difference (-1 = none).</summary>
        public int FirstAddrDiffIndex { get; set; } = -1;
        public uint FirstAddrDiffAvalonia { get; set; }
        public uint FirstAddrDiffWinForms { get; set; }

        /// <summary>Index of first text difference (-1 = none).</summary>
        public int FirstTextDiffIndex { get; set; } = -1;
        public string FirstTextDiffAvalonia { get; set; }
        public string FirstTextDiffWinForms { get; set; }

        public string FormatResult()
        {
            string status = IsMatch ? "MATCH" : "MISMATCH";
            string line = $"LISTPARITY: {EditorName} | avalonia_count={AvaloniaCount} | winforms_count={WinFormsCount} | text_match={TextMatches}/{Math.Max(AvaloniaCount, WinFormsCount)} | {status}";

            if (!IsMatch)
            {
                if (AvaloniaCount != WinFormsCount)
                    line += $" (count differs: {AvaloniaCount} vs {WinFormsCount})";
                if (FirstAddrDiffIndex >= 0)
                    line += $" (first addr diff at [{FirstAddrDiffIndex}]: 0x{FirstAddrDiffAvalonia:X} vs 0x{FirstAddrDiffWinForms:X})";
                if (FirstTextDiffIndex >= 0)
                    line += $" (first text diff at [{FirstTextDiffIndex}]: \"{Truncate(FirstTextDiffAvalonia, 40)}\" vs \"{Truncate(FirstTextDiffWinForms, 40)}\")";
            }

            return line;
        }

        static string Truncate(string s, int max) =>
            s != null && s.Length > max ? s.Substring(0, max) + "..." : s ?? "";
    }
}
