using System;
using System.Collections.Generic;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class TextViewerViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentId;
        string _decodedText = "";
        bool _isLoaded;

        public uint CurrentId { get => _currentId; set => SetField(ref _currentId, value); }
        public string DecodedText { get => _decodedText; set => SetField(ref _decodedText, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }

        public List<AddrResult> LoadTextList()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return new List<AddrResult>();

            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return new List<AddrResult>();

            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr)) return new List<AddrResult>();

            var result = new List<AddrResult>();
            for (uint i = 0; i < 0x2000; i++) // reasonable max
            {
                uint entryAddr = (uint)(baseAddr + i * 4);
                if (entryAddr + 3 >= (uint)rom.Data.Length) break;

                uint textPtr = rom.u32(entryAddr);
                if (!U.isPointerOrNULL(textPtr)) break;

                string preview;
                try
                {
                    string decoded = FETextDecode.Direct(i);
                    if (decoded != null && decoded.Length > 40)
                        decoded = decoded.Substring(0, 40) + "...";
                    preview = decoded ?? "";
                }
                catch
                {
                    preview = "";
                }

                string name = U.ToHexString(i) + " " + preview;
                result.Add(new AddrResult(entryAddr, name, i));
            }
            return result;
        }

        public void LoadText(uint id)
        {
            CurrentId = id;
            try
            {
                DecodedText = FETextDecode.Direct(id) ?? "(empty)";
            }
            catch
            {
                DecodedText = "(decode error)";
            }
            IsLoaded = true;
        }

        public int GetListCount() => LoadTextList().Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["CurrentId"] = $"0x{CurrentId:X04}",
                ["DecodedText"] = DecodedText ?? "",
            };
        }

        public Dictionary<string, string> GetRawRomReport()
        {
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null || CurrentId == 0) return new Dictionary<string, string>();
            uint textPtr = rom.RomInfo.text_pointer;
            if (textPtr == 0) return new Dictionary<string, string>();
            uint baseAddr = rom.p32(textPtr);
            if (!U.isSafetyOffset(baseAddr)) return new Dictionary<string, string>();
            uint a = (uint)(baseAddr + CurrentId * 4);
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{a:X08}",
                ["u32@0x00"] = $"0x{rom.u32(a):X08}",
            };
        }
    }
}
