using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapSettingDifficultyView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MapSettingDifficultyViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Difficulty Settings";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public MapSettingDifficultyView()
        {
            InitializeComponent();
            DataContext = _vm;
            EntryList.SelectedAddressChanged += OnSelected;
            HardBoostInput.ValueChanged += OnNibbleChanged;
            NormalPenaltyInput.ValueChanged += OnNibbleChanged;
            EasyPenaltyInput.ValueChanged += OnNibbleChanged;
            WriteButton.Click += OnWriteClick;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingDifficultyView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MapSettingDifficultyView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            HardBoostInput.Value = _vm.HardBoost;
            NormalPenaltyInput.Value = _vm.NormalPenalty;
            EasyPenaltyInput.Value = _vm.EasyPenalty;
            DifficultyValueDisplay.Value = _vm.DifficultyValue;
            FormattedLabel.Text = _vm.FormattedText;

            bool enabled = _vm.IsSupported;
            HardBoostInput.IsEnabled = enabled;
            NormalPenaltyInput.IsEnabled = enabled;
            EasyPenaltyInput.IsEnabled = enabled;
            WriteButton.IsEnabled = enabled;
        }

        void OnNibbleChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            // Avoid recursive updates while LoadEntry / OnSelected is repopulating the UI.
            if (_vm.IsLoading) return;

            _vm.HardBoost = (int)(HardBoostInput.Value ?? 0);
            _vm.NormalPenalty = (int)(NormalPenaltyInput.Value ?? 0);
            _vm.EasyPenalty = (int)(EasyPenaltyInput.Value ?? 0);
            DifficultyValueDisplay.Value = _vm.DifficultyValue;
            FormattedLabel.Text = _vm.FormattedText;
        }

        void OnWriteClick(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsSupported)
            {
                CoreState.Services?.ShowError("Difficulty Settings: current ROM is not supported (FE6 has a different layout).");
                return;
            }

            _undoService.Begin("Edit Difficulty Settings");
            try
            {
                if (!_vm.Write())
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("Difficulty Settings: write was rejected by ROM/version guard.");
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();

                // Reload to confirm write
                _vm.LoadEntry(_vm.CurrentAddr);
                UpdateUI();
                CoreState.Services?.ShowInfo("Difficulty Settings written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("MapSettingDifficultyView.Write failed: {0}", ex.Message);
                CoreState.Services?.ShowError($"Difficulty Settings write failed: {ex.Message}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
