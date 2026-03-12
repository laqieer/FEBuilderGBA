using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Alpha Name editor (FE6 variant).
    /// Data: class_alphaname_pointer, datasize=4 (pointer to C-string per entry).
    /// </summary>
    public class OPClassAlphaNameFE6ViewModel : ViewModelBase, IDataVerifiable
    {
        static readonly List<EditorFormRef.FieldDef> _fields =
            EditorFormRef.DetectFields(new[] { "D0" });

        uint _currentAddr;
        bool _canWrite;
        uint _namePointer;
        string _alphaName = "";
        string _unavailableMessage = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool CanWrite { get => _canWrite; set => SetField(ref _canWrite, value); }
        public uint NamePointer { get => _namePointer; set => SetField(ref _namePointer, value); }
        public string AlphaName { get => _alphaName; set => SetField(ref _alphaName, value); }
        public string UnavailableMessage { get => _unavailableMessage; set => SetField(ref _unavailableMessage, value); }

        public List<AddrResult> LoadList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint baseAddr = rom.RomInfo.class_alphaname_pointer;
            if (baseAddr == 0)
            {
                UnavailableMessage = "Not available for this ROM version";
                CanWrite = true;
                return new List<AddrResult>();
            }

            if (!U.isSafetyOffset(baseAddr))
            {
                UnavailableMessage = "Invalid pointer for this ROM version";
                CanWrite = true;
                return new List<AddrResult>();
            }

            UnavailableMessage = "";
            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x200; i++)
            {
                uint addr = (uint)(baseAddr + i * 4);
                if (addr + 4 > (uint)rom.Data.Length) break;

                uint ptr = rom.u32(addr);
                if (!U.isPointerOrNULL(ptr)) break;

                string name = U.ToHexString(i + 1);
                if (U.isPointer(ptr))
                {
                    try
                    {
                        uint strAddr = U.toOffset(ptr);
                        if (U.isSafetyOffset(strAddr))
                        {
                            string resolved = rom.getString(strAddr, 64);
                            if (!string.IsNullOrEmpty(resolved))
                                name = U.ToHexString(i + 1) + " " + resolved;
                        }
                    }
                    catch { }
                }
                result.Add(new AddrResult(addr, name, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 4 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            var values = EditorFormRef.ReadFields(rom, addr, _fields);
            NamePointer = values["D0"];
            AlphaName = "";

            if (U.isPointer(NamePointer))
            {
                try
                {
                    uint strAddr = U.toOffset(NamePointer);
                    if (U.isSafetyOffset(strAddr))
                        AlphaName = rom.getString(strAddr, 64);
                }
                catch { }
            }
            CanWrite = true;
        }

        public void WriteEntry()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            var values = new Dictionary<string, uint> { ["D0"] = NamePointer };
            EditorFormRef.WriteFields(rom, CurrentAddr, values, _fields);
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["NamePointer"] = $"0x{NamePointer:X08}",
                ["AlphaName"] = AlphaName ?? "",
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
                ["u32@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }
    }
}
