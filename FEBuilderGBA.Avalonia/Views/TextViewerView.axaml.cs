using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TextViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly TextViewerViewModel _vm = new();

        public string ViewTitle => "Text Editor";
        public bool IsLoaded => _vm.CanWrite;

        public TextViewerView()
        {
            InitializeComponent();
            TextList.SelectedAddressChanged += OnTextSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadTextList();
                TextList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnTextSelected(uint addr)
        {
            try
            {
                // The addr is the pointer table entry address, but we need the text ID (index)
                // The AddrResult.tag contains the index
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;

                uint textPtr = rom.RomInfo.text_pointer;
                if (textPtr == 0) return;
                uint baseAddr = rom.p32(textPtr);
                if (baseAddr == 0 || !U.isSafetyOffset(baseAddr)) return;

                uint id = (addr - baseAddr) / 4;
                _vm.LoadText(id);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("TextViewerView.OnTextSelected failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            TextList.SelectAddress(address);
        }

        void UpdateUI()
        {
            TextIdLabel.Text = $"Text ID: 0x{_vm.CurrentId:X04}";
            DecodedTextBlock.Text = _vm.DecodedText;
        }

        public void SelectFirstItem()
        {
            TextList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
