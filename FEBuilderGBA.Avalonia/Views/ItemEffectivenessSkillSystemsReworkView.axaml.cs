using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemEffectivenessSkillSystemsReworkView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ItemEffectivenessSkillSystemsReworkViewModel _vm = new();

        public string ViewTitle => "Effectiveness (Skill Systems Rework)";
        public bool IsLoaded => _vm.IsLoaded;

        /// <summary>
        /// Exposes the backing view-model for headless test access (issue #362
        /// regression tests assert <c>vm.CurrentAddr</c> matches the navigated
        /// address). Mirrors the <c>DataViewModel</c> pattern used by
        /// <see cref="ItemStatBonusesViewerView"/> and the other editor views.
        /// </summary>
        public ViewModelBase? DataViewModel => _vm;

        public ItemEffectivenessSkillSystemsReworkView()
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
                // Item-keyed list — render item icons, not class icons
                // (the list rows are item names + IDs, mirroring the WinForms
                // ItemEffectivenessSkillSystemsReworkForm outer list).
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessSkillSystemsReworkView.LoadList failed: {0}", ex.Message);
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
                Log.Error("ItemEffectivenessSkillSystemsReworkView.OnSelected failed: {0}", ex.Message);
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
