using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapStyleEditorAppendPopupViewModel : ViewModelBase
    {
        bool _isLoaded;
        bool _confirmed;
        uint _obj1Plist;
        uint _obj2Plist;
        uint _palPlist;
        uint _configPlist;
        uint _anime1Plist;
        uint _anime2Plist;
        string _plistExplanation = string.Empty;
        string _alreadyExtendsText = string.Empty;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public bool Confirmed { get => _confirmed; set => SetField(ref _confirmed, value); }
        /// <summary>OBJ1 tileset PLIST number.</summary>
        public uint Obj1Plist { get => _obj1Plist; set => SetField(ref _obj1Plist, value); }
        /// <summary>OBJ2 tileset PLIST number.</summary>
        public uint Obj2Plist { get => _obj2Plist; set => SetField(ref _obj2Plist, value); }
        /// <summary>Palette PLIST number.</summary>
        public uint PalPlist { get => _palPlist; set => SetField(ref _palPlist, value); }
        /// <summary>Config PLIST number.</summary>
        public uint ConfigPlist { get => _configPlist; set => SetField(ref _configPlist, value); }
        /// <summary>Animation 1 PLIST number.</summary>
        public uint Anime1Plist { get => _anime1Plist; set => SetField(ref _anime1Plist, value); }
        /// <summary>Animation 2 PLIST number.</summary>
        public uint Anime2Plist { get => _anime2Plist; set => SetField(ref _anime2Plist, value); }
        /// <summary>Explanation text about the PLIST system.</summary>
        public string PlistExplanation { get => _plistExplanation; set => SetField(ref _plistExplanation, value); }
        /// <summary>Text showing if PLIST is already extended.</summary>
        public string AlreadyExtendsText { get => _alreadyExtendsText; set => SetField(ref _alreadyExtendsText, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
