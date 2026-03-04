using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>
    /// OP Class Alpha Name editor (FE8J variant).
    /// Data: class_alphaname_pointer, datasize=20, each entry is a fixed 20-byte
    /// C-string (class name in alphabetic characters).
    /// </summary>
    public class OPClassAlphaNameViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _alphaName = "";
        string _unavailableMessage = "";

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
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
                IsLoaded = true;
                return new List<AddrResult>();
            }

            if (!U.isSafetyOffset(baseAddr))
            {
                UnavailableMessage = "Invalid pointer for this ROM version";
                IsLoaded = true;
                return new List<AddrResult>();
            }

            UnavailableMessage = "";
            var result = new List<AddrResult>();
            // Each entry is 20 bytes (fixed-length C-string)
            for (uint i = 0; i < 0x100; i++)
            {
                uint addr = (uint)(baseAddr + i * 20);
                if (addr + 20 > (uint)rom.Data.Length) break;

                // Stop if first byte is 0 (empty string = end of data)
                if (rom.u8(addr) == 0 && i > 0) break;

                string name;
                try
                {
                    name = rom.getString(addr, 20);
                }
                catch
                {
                    name = "";
                }
                string label = U.ToHexString(i) + " " + name;
                result.Add(new AddrResult(addr, label, i));
            }
            return result;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 20 > (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            try
            {
                AlphaName = rom.getString(addr, 20);
            }
            catch
            {
                AlphaName = "";
            }
            IsLoaded = true;
        }

        public int GetListCount() => LoadList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
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
