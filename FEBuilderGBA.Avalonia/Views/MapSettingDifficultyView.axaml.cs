using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MapSettingDifficultyView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly MapSettingDifficultyViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Difficulty Settings";
        public new bool IsLoaded => _vm.IsLoaded;

        public EditorDescriptor Descriptor => new("Difficulty Settings", 1100, 600, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public MapSettingDifficultyView()
        {
            InitializeComponent();
            DataContext = _vm;
            EntryList.SelectedAddressChanged += OnSelected;
            HardBoostInput.ValueChanged += OnNibbleChanged;
            NormalPenaltyInput.ValueChanged += OnNibbleChanged;
            EasyPenaltyInput.ValueChanged += OnNibbleChanged;
            WriteButton.Click += OnWriteClick;        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                LoadList();

            }

        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);

                // Compute banner / input-enabled state from explicit ROM state so
                // users see WHY the editor is empty on FE6 or when no ROM is loaded.
                var rom = CoreState.ROM;
                bool hasRom = rom?.RomInfo != null;
                bool supported = hasRom && DifficultyValueCore.IsSupported(rom);
                NoRomBanner.IsVisible = !hasRom;
                UnsupportedBanner.IsVisible = hasRom && !supported;
                HardBoostInput.IsEnabled = supported;
                NormalPenaltyInput.IsEnabled = supported;
                EasyPenaltyInput.IsEnabled = supported;
                WriteButton.IsEnabled = supported;
            }
            catch (Exception ex)
            {
                Log.ErrorF("MapSettingDifficultyView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("MapSettingDifficultyView.OnSelected failed: {0}", ex.Message);
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
                CoreState.Services?.ShowError(R._("Difficulty Settings: current ROM is not supported (FE6 has a different layout)."));
                return;
            }

            _undoService.Begin("Edit Difficulty Settings");
            try
            {
                if (!_vm.Write())
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(R._("Difficulty Settings: write was rejected by ROM/version guard."));
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();

                // Reload to confirm write
                _vm.LoadEntry(_vm.CurrentAddr);
                UpdateUI();
                CoreState.Services?.ShowInfo(R._("Difficulty Settings written."));
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("MapSettingDifficultyView.Write failed: {0}", ex.Message);
                CoreState.Services?.ShowError(R._("Difficulty Settings write failed: {0}", ex.Message));
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
