using System;
using System.Collections.Concurrent;

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

        // Characters to trim from decoded names (matches WinForms TextForm.StripAllCode)
        static readonly char[] TrimChars = { ' ', '\0', (char)0x1F, '\r', '\n', '\u3000' };

        /// <summary>Strip FE text control codes like @0501 and raw control chars from decoded text.</summary>
        internal static string StripControlCodes(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Remove @XXXX escape codes
            string result = RegexCache.Replace(text, @"@[0-9A-Fa-f]{4}", "");
            // Remove raw control characters (0x00-0x1F) that weren't encoded as @XXXX
            result = RegexCache.Replace(result, @"[\x00-\x1F]", "");
            return result.Trim(TrimChars);
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

        /// <summary>Get the name of a unit by 0-based table index.</summary>
        /// <remarks>
        /// Reads at <c>unitBase + id * dataSize</c>. Use this when iterating
        /// the unit table directly (e.g., <c>for i=0..N: GetUnitName(i)</c>).
        /// For ROM-stored unit IDs (which are 1-based per WinForms convention),
        /// use <see cref="GetUnitNameByOneBasedId(uint)"/> instead — passing a
        /// ROM byte directly to this method causes an off-by-one bug.
        /// </remarks>
        public static string GetUnitName(uint id)
        {
            return _cache.GetOrAdd(("unit", id), _ => ResolveUnitName(id));
        }

        /// <summary>
        /// Get the name of a unit by 1-based unit ID (the value stored in
        /// ROM bytes / event data / support partner fields). Mirrors WinForms
        /// <c>UnitForm.GetUnitName(uid)</c>:
        /// <list type="bullet">
        ///   <item><c>uid == 0</c> returns <c>""</c> (no unit).</item>
        ///   <item>Otherwise resolves <c>GetUnitName(uid - 1)</c>.</item>
        /// </list>
        /// Fixes the off-by-one in Avalonia editor lists (issues #652 #653) where
        /// ROM-byte uids were being passed straight to the 0-based <see cref="GetUnitName(uint)"/>.
        /// </summary>
        public static string GetUnitNameByOneBasedId(uint uid)
        {
            if (uid == 0) return "";
            return GetUnitName(uid - 1);
        }

        /// <summary>Get the name of a class by index.</summary>
        public static string GetClassName(uint id)
        {
            return _cache.GetOrAdd(("class", id), _ => ResolveClassName(id));
        }

        /// <summary>
        /// Get the unit or class name associated with a given portrait ID.
        /// Scans the unit table first (portrait at offset +6), then falls back
        /// to the class table (portrait at offset +8). Returns empty if not found.
        /// </summary>
        public static string GetPortraitName(uint portraitId)
        {
            return _cache.GetOrAdd(("portrait", portraitId), _ => ResolvePortraitName(portraitId));
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

        /// <summary>
        /// Dereference a ROMFEINFO pointer field to get the actual data base address.
        /// ROMFEINFO fields like unit_pointer/class_pointer/item_pointer store the
        /// ROM offset of a pointer, not the data address itself.
        /// </summary>
        internal static uint DerefPointer(ROM rom, uint pointerAddr)
        {
            if (pointerAddr == 0 || pointerAddr == U.NOT_FOUND) return 0;
            uint offset = U.toOffset(pointerAddr);
            if (!U.isSafetyOffset(offset, rom)) return 0;
            return rom.p32(offset);
        }

        static string ResolveUnitName(uint id)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return "???";
                uint baseAddr = DerefPointer(rom, rom.RomInfo.unit_pointer);
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
                uint baseAddr = DerefPointer(rom, rom.RomInfo.class_pointer);
                uint dataSize = rom.RomInfo.class_datasize;
                if (baseAddr == 0 || dataSize == 0) return "???";
                uint entryAddr = baseAddr + (id * dataSize);
                if (!U.isSafetyOffset(entryAddr + 1, rom)) return "???";
                uint textId = rom.u16(entryAddr);
                return textId == 0 ? $"#{id}" : GetTextById(textId);
            }
            catch { return "???"; }
        }

        static string ResolvePortraitName(uint portraitId)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return "";

                uint unitBase = DerefPointer(rom, rom.RomInfo.unit_pointer);
                uint unitSize = rom.RomInfo.unit_datasize;
                uint unitCount = rom.RomInfo.unit_maxcount;
                if (unitCount == 0) unitCount = 0x100;

                if (unitBase != 0 && unitSize != 0)
                {
                    for (uint i = 0; i < unitCount; i++)
                    {
                        uint entryAddr = unitBase + (i * unitSize);
                        if (!U.isSafetyOffset(entryAddr + 7, rom)) break;
                        if (rom.u16(entryAddr + 6) == portraitId)
                        {
                            uint textId = rom.u16(entryAddr);
                            if (textId != 0) return GetTextById(textId);
                        }
                    }
                }

                // Fallback: scan class table (portrait field at offset +8)
                uint classBase = DerefPointer(rom, rom.RomInfo.class_pointer);
                uint classSize = rom.RomInfo.class_datasize;
                uint classCount = 0x100; // reasonable upper bound

                if (classBase != 0 && classSize != 0)
                {
                    for (uint i = 0; i < classCount; i++)
                    {
                        uint entryAddr = classBase + (i * classSize);
                        if (!U.isSafetyOffset(entryAddr + 9, rom)) break;
                        if (rom.u16(entryAddr + 8) == portraitId)
                        {
                            uint textId = rom.u16(entryAddr);
                            if (textId != 0) return GetTextById(textId);
                        }
                    }
                }

                return "";
            }
            catch { return ""; }
        }

        static string ResolveItemName(uint id)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return "???";
                uint baseAddr = DerefPointer(rom, rom.RomInfo.item_pointer);
                uint dataSize = rom.RomInfo.item_datasize;
                if (baseAddr == 0 || dataSize == 0) return "???";
                uint entryAddr = baseAddr + (id * dataSize);
                if (!U.isSafetyOffset(entryAddr + 1, rom)) return "???";
                uint textId = rom.u16(entryAddr);
                return textId == 0 ? $"#{id}" : GetTextById(textId);
            }
            catch { return "???"; }
        }

        /// <summary>Get the name of a skill by index.</summary>
        public static string GetSkillName(uint id)
        {
            return _cache.GetOrAdd(("skill", id), _ => ResolveSkillName(id));
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

        static string ResolveSkillName(uint id)
        {
            if (id == 0) return "(None)";
            try
            {
                // Delegate to the UI-layer resolver if available
                var resolver = CoreState.SkillNameResolver;
                if (resolver != null)
                {
                    string name = resolver(id);
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
                // Fallback: hex representation
                return $"Skill 0x{id:X02}";
            }
            catch { return $"Skill 0x{id:X02}"; }
        }
    }
}
