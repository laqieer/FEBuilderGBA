using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Text dictionary browser ViewModel.</summary>
    public class TextDicViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _searchTerm = "";
        string _selectedEntry = "";
        ObservableCollection<string> _entries = new();
        uint _b0, _b1, _w2, _w4, _w6, _w8, _b10, _b11;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SearchTerm { get => _searchTerm; set => SetField(ref _searchTerm, value); }
        public string SelectedEntry { get => _selectedEntry; set => SetField(ref _selectedEntry, value); }
        public ObservableCollection<string> Entries { get => _entries; set => SetField(ref _entries, value); }

        public uint B0 { get => _b0; set => SetField(ref _b0, value); }
        public uint B1 { get => _b1; set => SetField(ref _b1, value); }
        public uint W2 { get => _w2; set => SetField(ref _w2, value); }
        public uint W4 { get => _w4; set => SetField(ref _w4, value); }
        public uint W6 { get => _w6; set => SetField(ref _w6, value); }
        public uint W8 { get => _w8; set => SetField(ref _w8, value); }
        public uint B10 { get => _b10; set => SetField(ref _b10, value); }
        public uint B11 { get => _b11; set => SetField(ref _b11, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 11 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            B0 = rom.u8(addr + 0);
            B1 = rom.u8(addr + 1);
            W2 = rom.u16(addr + 2);
            W4 = rom.u16(addr + 4);
            W6 = rom.u16(addr + 6);
            W8 = rom.u16(addr + 8);
            B10 = rom.u8(addr + 10);
            B11 = rom.u8(addr + 11);

            IsLoaded = true;
        }

        public int GetListCount() => 0;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["B0"] = $"0x{B0:X02}",
                ["B1"] = $"0x{B1:X02}",
                ["W2"] = $"0x{W2:X04}",
                ["W4"] = $"0x{W4:X04}",
                ["W6"] = $"0x{W6:X04}",
                ["W8"] = $"0x{W8:X04}",
                ["B10"] = $"0x{B10:X02}",
                ["B11"] = $"0x{B11:X02}",
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
                ["u8@0x00"] = $"0x{rom.u8(a + 0):X02}",
                ["u8@0x01"] = $"0x{rom.u8(a + 1):X02}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
                ["u16@0x04"] = $"0x{rom.u16(a + 4):X04}",
                ["u16@0x06"] = $"0x{rom.u16(a + 6):X04}",
                ["u16@0x08"] = $"0x{rom.u16(a + 8):X04}",
                ["u8@0x0A"] = $"0x{rom.u8(a + 10):X02}",
                ["u8@0x0B"] = $"0x{rom.u8(a + 11):X02}",
            };
        }
    }
}
