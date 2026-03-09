using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ImageMapActionAnimationView : Window, IEditorView
    {
        readonly ImageMapActionAnimationViewModel _vm = new();

        public string ViewTitle => "Map Action Animation";
        public bool IsLoaded => _vm.IsLoaded;

        public ImageMapActionAnimationView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("ImageMapActionAnimationView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ImageMapActionAnimationView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            AnimationPointerBox.Text = $"0x{_vm.AnimationPointer:X08}";
            Padding1Box.Value = _vm.Padding1;
            Padding2Box.Value = _vm.Padding2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.AnimationPointer = ParseHexText(AnimationPointerBox.Text);
            _vm.Padding1 = (uint)(Padding1Box.Value ?? 0);
            _vm.Padding2 = (uint)(Padding2Box.Value ?? 0);
            _vm.Write();
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            if (uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint val))
                return val;
            return 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
