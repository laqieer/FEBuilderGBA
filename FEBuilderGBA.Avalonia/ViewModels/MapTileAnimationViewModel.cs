using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapTileAnimationViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "W0", "W2", "D4" });

        uint _currentAddr;
        uint _animInterval, _dataCount;
        uint _animPointer;  // P4
        string _rawBytes = "";
        bool _canWrite;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        /// <summary>Animation interval (W0).</summary>
        public uint AnimInterval { get => _animInterval; set => SetField(ref _animInterval, value); }
        /// <summary>Data count (W2).</summary>
        public uint DataCount { get => _dataCount; set => SetField(ref _dataCount, value); }
        public uint AnimPointer { get => _animPointer; set => SetField(ref _animPointer, value); }
        public string RawBytes { get => _rawBytes; set => SetField(ref _rawBytes, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }

        public List<AddrResult> LoadMapTileAnimationList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.map_tileanime1_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            // map_tileanime1_pointer is the ANIMATION PLIST table base, so each
            // 4-byte slot index IS the PLIST id. Each slot holds a u32 POINTER to
            // the real 8-byte animation struct (interval W0@0, count W2@2,
            // gfx-ptr D4@4); it is NOT the struct itself. So we DEREFERENCE every
            // slot via MapChangeCore.PlistToOffsetAddr(...ANIMATION, i...) (#1403)
            // — which returns p32(base + i*4) bounded by the version PLIST limit
            // and null/safety-checked, returning U.NOT_FOUND for a broken/empty
            // slot — and store that struct address as the row address. Without
            // this, LoadMapTileAnimation/WriteMapTileAnimation would read/write
            // the 8-byte struct AT the slot, showing garbage and overwriting two
            // adjacent PLIST pointers on Write (table corruption). Mirrors
            // MapTileAnimation1Core.BuildPlistList:137-138. The label still uses
            // the slot index i as the PLIST id, resolved to an "ANIME1/ANIME2
            // MapName" label via the shared resolver (#952, #11) instead of a raw
            // 0x… pointer. The lockstep golden builder
            // ListParityHelper.BuildMapTileAnimationList does the SAME dereference.
            var cache = MapPListResolverCore.BuildCache(rom);

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                // Dereference the PLIST slot to the actual struct address.
                // PlistToOffsetAddr already bounds by the version PLIST limit and
                // performs the ROM-aware null/safety check, returning U.NOT_FOUND
                // for a broken/empty/out-of-range slot — skip those rows.
                uint dataAddr = MapChangeCore.PlistToOffsetAddr(
                    rom, MapChangeCore.PlistType.ANIMATION, i, out uint _);
                if (dataAddr == U.NOT_FOUND) continue;

                // PlistToOffsetAddr only guarantees the struct START is a safe
                // offset, not that the full 8-byte struct (W0@0, W2@2, D4@4 — read
                // up to dataAddr+7) fits in ROM. A malformed pointer table could
                // dereference to within 8 bytes of EOF; skip those so the loader
                // never reads out of bounds (and Write never corrupts past EOF).
                if (dataAddr + 8u > (uint)rom.Data.Length) continue;

                string label = MapPListResolverCore.ResolveLabel(
                    rom, MapChangeCore.PlistType.ANIMATION, i, cache);
                string name = U.ToHexString(i) + " " + label;
                result.Add(new AddrResult(dataAddr, name, i));
            }
            return result;
        }

        public void LoadMapTileAnimation(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            // The struct is 8 bytes (W0@0, W2@2, D4@4 — read up to addr+7), so
            // require all 8 bytes in-bounds, not just the first 4 (#1403 review).
            if (addr + 8u > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            AnimInterval = values["W0"];
            DataCount = values["W2"];
            AnimPointer = values["D4"];

            // If pointer is valid, read some raw bytes at the target for display
            if (U.isPointer(AnimPointer))
            {
                uint target = AnimPointer & 0x1FFFFFF;
                uint bytesToRead = Math.Min(32u, (uint)rom.Data.Length - target);
                if (target < (uint)rom.Data.Length)
                {
                    var sb = new System.Text.StringBuilder();
                    for (uint i = 0; i < bytesToRead; i++)
                    {
                        if (i > 0 && i % 16 == 0)
                            sb.Append("\n");
                        else if (i > 0)
                            sb.Append(" ");
                        sb.Append(rom.u8(target + i).ToString("X02"));
                    }
                    RawBytes = sb.ToString();
                }
                else
                {
                    RawBytes = "";
                }
            }
            else
            {
                RawBytes = "";
            }

            CanWrite = true;
        }

        public void WriteMapTileAnimation()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            uint addr = CurrentAddr;
            var values = new Dictionary<string, uint>
            {
                ["W0"] = AnimInterval, ["W2"] = DataCount,
                ["D4"] = AnimPointer,
            };
            EditorFormRef.WriteFields(rom, addr, values, _fields);
        }

        public int GetListCount() => LoadMapTileAnimationList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["AnimInterval"] = $"0x{AnimInterval:X04}",
                ["DataCount"] = $"0x{DataCount:X04}",
                ["AnimPointer"] = $"0x{AnimPointer:X08}",
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
                ["AnimInterval@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["DataCount@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["AnimPointer@0x04"] = $"0x{rom.u32(a + 4):X08}",
            };
        }

        public Dictionary<string, string> GetFieldOffsetMap()
        {
            return new Dictionary<string, string>
            {
                ["AnimInterval"] = "AnimInterval@0x00",
                ["DataCount"] = "DataCount@0x02",
                ["AnimPointer"] = "AnimPointer@0x04",
            };
        }
    }
}
