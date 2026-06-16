using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolFlagNameView : TranslatedWindow, IEditorView
    {
        readonly ToolFlagNameViewModel _vm = new();

        public string ViewTitle => "Flag Name Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolFlagNameView()
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
                EntryList.SetItems(items);   // selects the first item -> OnSelected
                if (items.Count == 0)
                {
                    // No flag cache (e.g. no ROM): reset the panel + actions, no selection fires.
                    _vm.HasSelection = false;
                    _vm.FlagName = "";
                    _vm.IsCustom = false;
                    UpdateUI();
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolFlagNameView.LoadList failed: " + ex);
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
                Log.Error("ToolFlagNameView.OnSelected failed: " + ex);
            }
        }

        void UpdateUI()
        {
            FlagLabel.Text = _vm.HasSelection ? string.Format("0x{0:X}", _vm.SelectedFlag) : "";
            FlagNameTextBox.Text = _vm.FlagName;
            FlagNameTextBox.IsEnabled = _vm.HasSelection;
            WriteButton.IsEnabled = _vm.HasSelection;
            // Reset-to-default only makes sense when the current name is a customization.
            DeleteButton.IsEnabled = _vm.HasSelection && _vm.IsCustom;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasSelection) return;
                uint flag = _vm.SelectedFlag;
                if (_vm.Write(flag, FlagNameTextBox.Text ?? ""))
                {
                    _vm.Save();
                    ReloadKeepingSelection(flag);
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolFlagNameView.Write_Click failed: " + ex);
            }
        }

        void Delete_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.HasSelection) return;
                uint flag = _vm.SelectedFlag;
                _vm.Delete(flag);
                _vm.Save();
                ReloadKeepingSelection(flag);
            }
            catch (Exception ex)
            {
                Log.Error("ToolFlagNameView.Delete_Click failed: " + ex);
            }
        }

        // The flag's display name changed: rebuild the list and re-select the same flag.
        void ReloadKeepingSelection(uint flag)
        {
            var items = _vm.LoadList();
            EntryList.SetItems(items);
            EntryList.SelectAddress(ToolFlagNameViewModel.AddrFromFlag(flag));
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
