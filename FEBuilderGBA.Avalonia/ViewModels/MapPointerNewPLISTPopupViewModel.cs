using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapPointerNewPLISTPopupViewModel : ViewModelBase
    {
        bool _isLoaded;
        uint _plistId;
        string _plistExplanation = "PLIST (Pointer List) assigns a numeric ID to each map.\nChoose an unused PLIST number to add a new map pointer entry.";
        string _alreadyExtendsText = string.Empty;
        string _linkPlistInfo = string.Empty;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>The PLIST ID number for the new map pointer entry.</summary>
        public uint PlistId { get => _plistId; set => SetField(ref _plistId, value); }
        /// <summary>Explanation text about the PLIST system.</summary>
        public string PlistExplanation { get => _plistExplanation; set => SetField(ref _plistExplanation, value); }
        /// <summary>Text indicating if the PLIST range is already extended.</summary>
        public string AlreadyExtendsText { get => _alreadyExtendsText; set => SetField(ref _alreadyExtendsText, value); }
        /// <summary>Information about what this PLIST ID links to.</summary>
        public string LinkPlistInfo { get => _linkPlistInfo; set => SetField(ref _linkPlistInfo, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
