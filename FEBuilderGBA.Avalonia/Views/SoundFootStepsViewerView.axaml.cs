using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SoundFootStepsViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SoundFootStepsViewerViewModel _vm = new();

        public string ViewTitle => "Footstep Sounds";
        public bool IsLoaded => _vm.CanWrite;

        public SoundFootStepsViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSoundFootStepsList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SoundFootStepsViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadSoundFootSteps(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SoundFootStepsViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            DataPointerBox.Text = $"0x{_vm.DataPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.DataPointer = ParseHexText(DataPointerBox.Text);
            _vm.WriteSoundFootSteps();
            CoreState.Services.ShowInfo("Footstep sounds data written.");
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
