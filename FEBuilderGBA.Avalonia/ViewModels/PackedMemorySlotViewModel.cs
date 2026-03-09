using System;
using System.Collections.ObjectModel;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class PackedMemorySlotViewModel : ViewModelBase
    {
        bool _isLoaded;
        string _messageText = "Packed Memory Slot";
        int _selectedA;
        int _selectedB;
        int _selectedC;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Message displayed at the top of the dialog.</summary>
        public string MessageText { get => _messageText; set => SetField(ref _messageText, value); }
        /// <summary>Selected index for slot A (result).</summary>
        public int SelectedA { get => _selectedA; set => SetField(ref _selectedA, value); }
        /// <summary>Selected index for slot B (operand 1).</summary>
        public int SelectedB { get => _selectedB; set => SetField(ref _selectedB, value); }
        /// <summary>Selected index for slot C (operand 2).</summary>
        public int SelectedC { get => _selectedC; set => SetField(ref _selectedC, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }
        /// <summary>Items for slot A combo box.</summary>
        public ObservableCollection<string> SlotAItems { get; } = new();
        /// <summary>Items for slot B combo box.</summary>
        public ObservableCollection<string> SlotBItems { get; } = new();
        /// <summary>Items for slot C combo box.</summary>
        public ObservableCollection<string> SlotCItems { get; } = new();

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
