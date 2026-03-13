using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace FEBuilderGBA
{
    /// <summary>
    /// Resolves human-readable names for ROM entities (units, classes, items, etc.)
    /// by reading from ROM data + FETextDecode. Thread-safe with caching.
    /// </summary>
    public static class NameResolver
    {
        static readonly ConcurrentDictionary<(string kind, uint id), string> _cache = new();

        /// <summary>Clear the name cache (e.g., after undo or ROM reload).</summary>
        public static void ClearCache() => _cache.Clear();

        /// <summary>Strip FE text control codes like @0501 from decoded text.</summary>
        internal static string StripControlCodes(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"@[0-9A-Fa-f]{4}", "");
        }

        /// <summary>Decode a text ID to a string. Returns "???" on failure.</summary>
        public static string GetTextById(uint textId)
        {
            if (textId == 0) return "";
            try
            {
                string raw = FETextDecode.Direct(textId) ?? "???";
                return StripControlCodes(raw);
            }
            catch
            {
                return "???";
            }
        }

        /// <summary>Get the name of a unit by index.</summary>
        public static string GetUnitName(uint id)
        {
            return _cache.GetOrAdd(("unit", id), _ => ResolveUnitName(id));
        }

        /// <summary>Get the name of a class by index.</summary>
        public static string GetClassName(uint id)
        {
            return _cache.GetOrAdd(("class", id), _ => ResolveClassName(id));
        }

        /// <summary>Get the name of an item by index.</summary>
        public static string GetItemName(uint id)
        {
            return _cache.GetOrAdd(("item", id), _ => ResolveItemName(id));
        }

        /// <summary>Get a song/music name by index.</summary>
        public static string GetSongName(uint id)
        {
            return _cache.GetOrAdd(("song", id), _ => ResolveSongName(id));
        }

        static string ResolveUnitName(uint id)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return "???";
                uint baseAddr = rom.RomInfo.unit_pointer;
                uint dataSize = rom.RomInfo.unit_datasize;
                if (baseAddr == 0 || dataSize == 0) return "???";
                uint entryAddr = baseAddr + (id * dataSize);
                if (!U.isSafetyOffset(entryAddr + 1, rom)) return "???";
                uint textId = rom.u16(entryAddr);
                return textId == 0 ? $"#{id}" : GetTextById(textId);
            }
            catch { return "???"; }
        }

        static string ResolveClassName(uint id)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return "???";
                uint baseAddr = rom.RomInfo.class_pointer;
                uint dataSize = rom.RomInfo.class_datasize;
                if (baseAddr == 0 || dataSize == 0) return "???";
                uint entryAddr = baseAddr + (id * dataSize);
                if (!U.isSafetyOffset(entryAddr + 1, rom)) return "???";
                uint textId = rom.u16(entryAddr);
                return textId == 0 ? $"#{id}" : GetTextById(textId);
            }
            catch { return "???"; }
        }

        static string ResolveItemName(uint id)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return "???";
                uint baseAddr = rom.RomInfo.item_pointer;
                uint dataSize = rom.RomInfo.item_datasize;
                if (baseAddr == 0 || dataSize == 0) return "???";
                uint entryAddr = baseAddr + (id * dataSize);
                if (!U.isSafetyOffset(entryAddr + 1, rom)) return "???";
                uint textId = rom.u16(entryAddr);
                return textId == 0 ? $"#{id}" : GetTextById(textId);
            }
            catch { return "???"; }
        }

        static string ResolveSongName(uint id)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return $"Song {id}";
                // Song table doesn't have text IDs, just return index-based name
                return $"Song 0x{id:X}";
            }
            catch { return "???"; }
        }
    }
}
