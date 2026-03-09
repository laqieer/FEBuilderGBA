using System.Collections.Generic;
using System.Collections.ObjectModel;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Character code table viewer ViewModel.
    /// WinForms: TextCharCodeForm — record size 4 bytes.
    /// W0 / J_0 = ASCII/character code (u16@0).
    /// W2 / J_2 = Terminator value FFFF (u16@2).
    /// L_0_WSPLITSTRING_0 = character display string.
    /// J_0_FONT_ITEM / ItemFontPictureBox = item font preview.
    /// J_0_FONT_SERIF / SerifFontPictureBox = serif font preview.
    /// Search panel: SEARCH_CHAR / SEARCH_CHAR_BUTTON = character search.
    /// Frequency panel: SEARCH_COUNT / SEARCH_COUNT_BUTTON / SEARCH_COUNT_LIST.
    /// </summary>
    public class TextCharCodeViewModel : ViewModelBase, IDataVerifiable
    {
        uint _currentAddr;
        bool _isLoaded;
        string _selectedCode = "";
        ObservableCollection<string> _charCodes = new();
        uint _charCode, _terminatorValue;
        string _characterDisplay = "";
        string _searchChar = "";
        uint _searchFrequencyThreshold;

        public uint CurrentAddr { get => _currentAddr; set => SetField(ref _currentAddr, value); }
        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public string SelectedCode { get => _selectedCode; set => SetField(ref _selectedCode, value); }
        public ObservableCollection<string> CharCodes { get => _charCodes; set => SetField(ref _charCodes, value); }

        /// <summary>Character code value (u16@0). WinForms: W0 / J_0 "ASCII".</summary>
        public uint CharCode { get => _charCode; set => SetField(ref _charCode, value); }

        /// <summary>Terminator/secondary value (u16@2). WinForms: W2 / J_2 "FFFF".</summary>
        public uint TerminatorValue { get => _terminatorValue; set => SetField(ref _terminatorValue, value); }

        /// <summary>Display string for the character. WinForms: L_0_WSPLITSTRING_0.</summary>
        public string CharacterDisplay { get => _characterDisplay; set => SetField(ref _characterDisplay, value); }

        /// <summary>Character to search for. WinForms: SEARCH_CHAR.</summary>
        public string SearchChar { get => _searchChar; set => SetField(ref _searchChar, value); }

        /// <summary>Frequency threshold for search. WinForms: SEARCH_COUNT.</summary>
        public uint SearchFrequencyThreshold { get => _searchFrequencyThreshold; set => SetField(ref _searchFrequencyThreshold, value); }

        // Legacy aliases
        public uint W0 { get => CharCode; set => CharCode = value; }
        public uint W2 { get => TerminatorValue; set => TerminatorValue = value; }

        public void Initialize()
        {
            IsLoaded = true;
        }

        public void LoadEntry(uint addr)
        {
            ROM rom = CoreState.ROM;
            if (rom == null) return;
            if (addr + 3 >= (uint)rom.Data.Length) return;

            CurrentAddr = addr;
            CharCode = rom.u16(addr + 0);
            TerminatorValue = rom.u16(addr + 2);

            IsLoaded = true;
        }

        public void Write()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || CurrentAddr == 0) return;
            if (CurrentAddr + 3 >= (uint)rom.Data.Length) return;

            rom.write_u16(CurrentAddr + 0, (ushort)CharCode);
            rom.write_u16(CurrentAddr + 2, (ushort)TerminatorValue);
        }

        public int GetListCount() => CharCodes.Count;

        public Dictionary<string, string> GetDataReport()
        {
            return new Dictionary<string, string>
            {
                ["addr"] = $"0x{CurrentAddr:X08}",
                ["CharCode"] = $"0x{CharCode:X04}",
                ["TerminatorValue"] = $"0x{TerminatorValue:X04}",
                ["CharacterDisplay"] = CharacterDisplay,
                ["CharCodeCount"] = CharCodes.Count.ToString(),
                ["SelectedCode"] = SelectedCode,
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
                ["u16@0x00"] = $"0x{rom.u16(a + 0):X04}",
                ["u16@0x02"] = $"0x{rom.u16(a + 2):X04}",
            };
        }
    }
}
