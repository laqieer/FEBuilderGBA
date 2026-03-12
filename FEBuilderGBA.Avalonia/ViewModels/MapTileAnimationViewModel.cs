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

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 3 >= (uint)rom.Data.Length) break;

                uint pointer = rom.u32(addr);
                // Stop if we hit clearly invalid data
                if (pointer == 0xFFFFFFFF) break;

                string ptrStr = U.isPointer(pointer)
                    ? "0x" + pointer.ToString("X08")
                    : (pointer == 0 ? "NULL" : "0x" + pointer.ToString("X08"));
                string name = U.ToHexString(i) + " TileAnim " + ptrStr;
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadMapTileAnimation(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;

            if (addr + 3 >= (uint)rom.Data.Length) return;

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
    }
}
