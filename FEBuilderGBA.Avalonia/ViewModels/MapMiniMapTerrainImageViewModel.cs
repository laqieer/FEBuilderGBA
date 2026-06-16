using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// Mini-Map Terrain image editor — port of WinForms <c>MapMiniMapTerrainImageForm</c>.
    /// The table at <c>p32(RomInfo.map_minimap_tile_array_pointer)</c> holds one 4-byte pointer per
    /// terrain type (count = <c>map_terrain_type_count</c>); each points at the minimap tile-array
    /// graphic for that terrain. The pointer is editable directly, and a combo offers the known
    /// named tile arrays from the version-specific <c>map_minimap_tile_array_</c> resource
    /// (lines of <c>&lt;pointer&gt;=&lt;name&gt;</c>).
    /// </summary>
    public class MapMiniMapTerrainImageViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        public const uint EntrySize = 4;

        uint _currentAddr;
        bool _isLoaded;
        uint _p0;
        string _tileArrayName = string.Empty;

        List<uint> _optionValues;
        List<string> _optionLabels;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The 4-byte tile-array pointer (raw GBA pointer, matching the resource keys).</summary>
        public uint P0 { get => _p0; set { if (SetField(ref _p0, value)) TileArrayName = ResolveName(value); } }
        /// <summary>Resolved name of the current pointer from the tile-array resource (empty if custom).</summary>
        public string TileArrayName { get => _tileArrayName; set => SetField(ref _tileArrayName, value); }

        /// <summary>Combo labels ("0xPOINTER Name") for the known named tile arrays.</summary>
        public List<string> OptionLabels { get { EnsureOptions(); return _optionLabels; } }

        /// <summary>
        /// Load the list of minimap terrain entries from map_minimap_tile_array_pointer.
        /// Each entry is 4 bytes (a pointer), total count = map_terrain_type_count.
        /// </summary>
        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_minimap_tile_array_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return new List<AddrResult>();

            int count = (int)rom.RomInfo.map_terrain_type_count;
            var result = new List<AddrResult>();
            for (int i = 0; i < count; i++)
            {
                uint addr = (uint)(baseAddr + i * EntrySize);
                if (addr + EntrySize > (uint)rom.Data.Length) break;
                result.Add(new AddrResult(addr, $"0x{i:X02} Terrain {i}", (uint)i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + EntrySize > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            P0 = values["D0"];
            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + EntrySize > (uint)rom.Data.Length) return;
            var values = new Dictionary<string, uint> { ["D0"] = P0 };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        /// <summary>Combo index (-1 if none) whose pointer matches <paramref name="value"/>.</summary>
        public int GetOptionIndex(uint value)
        {
            EnsureOptions();
            return _optionValues.IndexOf(value);
        }

        /// <summary>Pointer for combo <paramref name="index"/>, or 0 when out of range.</summary>
        public uint GetOptionValue(int index)
        {
            EnsureOptions();
            return (index >= 0 && index < _optionValues.Count) ? _optionValues[index] : 0;
        }

        public int GetListCount()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            return (int)rom.RomInfo.map_terrain_type_count;
        }

        void EnsureOptions()
        {
            if (_optionLabels != null) return;
            _optionValues = new List<uint>();
            _optionLabels = new List<string>();

            Dictionary<uint, string> dic;
            try { dic = U.LoadDicResource(U.ConfigDataFilename("map_minimap_tile_array_")); }
            catch { dic = new Dictionary<uint, string>(); }

            foreach (var kv in dic)
            {
                _optionValues.Add(kv.Key);
                _optionLabels.Add(U.ToHexString(kv.Key) + " " + CleanName(kv.Value));
            }
        }

        string ResolveName(uint value)
        {
            EnsureOptions();
            int i = _optionValues.IndexOf(value);
            if (i < 0) return "";
            // OptionLabels[i] is "0xVALUE Name" — return the name portion.
            int sp = _optionLabels[i].IndexOf(' ');
            return sp >= 0 ? _optionLabels[i].Substring(sp + 1) : _optionLabels[i];
        }

        /// <summary>Trim the resource's trailing language/comment annotations (e.g. "Plain\t{J}").</summary>
        static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var parts = s.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : s.Trim();
        }

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["P0"] = $"0x{P0:X08}",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return new Dictionary<string, string>();
            uint a = CurrentAddr;
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a + 0):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap() => new()
        {
            ["P0"] = "u32@0x00",
        };
    }
}
