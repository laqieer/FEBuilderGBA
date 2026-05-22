using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class DecreaseColorTSAToolView : TranslatedWindow, IEditorView
    {
        readonly DecreaseColorTSAToolViewModel _vm = new();

        public string ViewTitle => "Color Reduction Tool";
        public bool IsLoaded => _vm.IsLoaded;

        public DecreaseColorTSAToolView()
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
                Log.Error("DecreaseColorTSAToolView.LoadList failed: {0}", ex.Message);
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
                Log.Error("DecreaseColorTSAToolView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        /// <summary>
        /// Pre-select the Color Reduce method index. Mirrors the WinForms
        /// `DecreaseColorTSAToolForm.InitMethod(int)` entry point used by
        /// callers like ImageBattleBGForm (mode 2 = battle BG) so the
        /// dialog opens in the correct mode for the caller.
        ///
        /// The current Avalonia view is a stub with no Method combo yet;
        /// storing the mode index on the VM keeps the calling contract
        /// honest so a future full port can pick the mode without
        /// breaking existing callers.
        /// </summary>
        /// <param name="methodIndex">WF method index — 2 = Battle BG.</param>
        public void InitMethod(int methodIndex)
        {
            _vm.Method = methodIndex;
        }
    }
}
