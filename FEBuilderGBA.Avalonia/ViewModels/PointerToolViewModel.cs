using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PointerToolViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _addressInput = string.Empty;
        string _pointerValue = string.Empty;
        string _littleEndianValue = string.Empty;
        string _firstReference = string.Empty;
        string _dataAddress = string.Empty;
        string _otherRomAddress = string.Empty;
        string _otherRomRefPointer = string.Empty;
        string _otherRomLdrAddress = string.Empty;
        string _otherRomLdrRefPointer = string.Empty;
        string _otherRomName = string.Empty;
        string _searchResults = string.Empty;
        bool _useAsmMap = true;
        int _testMatchDataSize;
        int _dataType;
        int _grepType;
        int _slideSize;
        int _autoTrackingLevel;
        int _warningLevel;
        bool _hasZeroWarning;
        bool _hasVeryFarWarning;

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Input ROM address to analyze.</summary>
        public string AddressInput { get => _addressInput; set => SetField(ref _addressInput, value); }
        /// <summary>Address value as a GBA pointer (+ 0x08000000).</summary>
        public string PointerValue { get => _pointerValue; set => SetField(ref _pointerValue, value); }
        /// <summary>Little-endian representation of the 4 bytes at the address.</summary>
        public string LittleEndianValue { get => _littleEndianValue; set => SetField(ref _littleEndianValue, value); }
        /// <summary>First pointer reference to this address found in ROM.</summary>
        public string FirstReference { get => _firstReference; set => SetField(ref _firstReference, value); }
        /// <summary>Data address pointed to if the value is a pointer.</summary>
        public string DataAddress { get => _dataAddress; set => SetField(ref _dataAddress, value); }
        /// <summary>Matching address in the other loaded ROM.</summary>
        public string OtherRomAddress { get => _otherRomAddress; set => SetField(ref _otherRomAddress, value); }
        /// <summary>Pointer reference to the other ROM address.</summary>
        public string OtherRomRefPointer { get => _otherRomRefPointer; set => SetField(ref _otherRomRefPointer, value); }
        /// <summary>LDR-tracked address in the other ROM.</summary>
        public string OtherRomLdrAddress { get => _otherRomLdrAddress; set => SetField(ref _otherRomLdrAddress, value); }
        /// <summary>LDR-tracked reference pointer in the other ROM.</summary>
        public string OtherRomLdrRefPointer { get => _otherRomLdrRefPointer; set => SetField(ref _otherRomLdrRefPointer, value); }
        /// <summary>Filename of the other loaded ROM.</summary>
        public string OtherRomName { get => _otherRomName; set => SetField(ref _otherRomName, value); }
        public string SearchResults { get => _searchResults; set => SetField(ref _searchResults, value); }
        /// <summary>Use ASM MAP file for enhanced search.</summary>
        public bool UseAsmMap { get => _useAsmMap; set => SetField(ref _useAsmMap, value); }
        /// <summary>Comparison data size index (512, 256, 128 bytes, etc.).</summary>
        public int TestMatchDataSize { get => _testMatchDataSize; set => SetField(ref _testMatchDataSize, value); }
        /// <summary>Content type: 0=DATA, 1=ASM.</summary>
        public int DataType { get => _dataType; set => SetField(ref _dataType, value); }
        /// <summary>Search method: 0=Exact, 1=Pattern.</summary>
        public int GrepType { get => _grepType; set => SetField(ref _grepType, value); }
        /// <summary>Slide search offset size.</summary>
        public int SlideSize { get => _slideSize; set => SetField(ref _slideSize, value); }
        /// <summary>Automatic tracking level.</summary>
        public int AutoTrackingLevel { get => _autoTrackingLevel; set => SetField(ref _autoTrackingLevel, value); }
        /// <summary>Warning level: 0=Error, 1=Ignore if referenced, 2=Ignore.</summary>
        public int WarningLevel { get => _warningLevel; set => SetField(ref _warningLevel, value); }
        /// <summary>True if the address points to a zero-filled region.</summary>
        public bool HasZeroWarning { get => _hasZeroWarning; set => SetField(ref _hasZeroWarning, value); }
        /// <summary>True if the address is very far from the original data.</summary>
        public bool HasVeryFarWarning { get => _hasVeryFarWarning; set => SetField(ref _hasVeryFarWarning, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
