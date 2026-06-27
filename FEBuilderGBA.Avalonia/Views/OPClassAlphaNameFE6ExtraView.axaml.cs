using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class OPClassAlphaNameFE6ExtraView : TranslatedWindow, IEditorView
    {
        readonly OPClassAlphaNameFE6ExtraViewModel _vm = new();

        public string ViewTitle => "OP Class Alpha Name (FE6 Extra)";
        public bool IsLoaded => _vm.IsLoaded;

        public OPClassAlphaNameFE6ExtraView()
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
                // #939: the single row is a synthetic section label, NOT a
                // class — the prefix is not a class id, so the old class-icon
                // loader showed a spurious icon. Drop the icon column entirely.
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("OPClassAlphaNameFE6ExtraView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("OPClassAlphaNameFE6ExtraView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
