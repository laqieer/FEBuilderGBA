using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SystemHoverColorViewerView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SystemHoverColorViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "System Area Color Viewer";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public SystemHoverColorViewerView()
        {
            InitializeComponent();
            DataContext = _vm;
            FilterCombo.ItemsSource = _vm.FilterNames;
            FilterCombo.SelectedIndex = 0;
            FilterCombo.SelectionChanged += FilterCombo_SelectionChanged;
            EntryList.SelectedAddressChanged += OnSelected;
            GBAColorBox.ValueChanged += GBAColorBox_ValueChanged;
            Opened += (_, _) => LoadList();
        }

        void FilterCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _vm.SelectedFilterIndex = FilterCombo.SelectedIndex;
            LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadColorList(_vm.SelectedFilterIndex);
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ColorSwatchLoader(items, i));
            }
            catch (Exception ex) { Log.Error($"SystemHoverColorViewerView.LoadList: {ex.Message}"); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadHoverColor(addr);
                StatusLabel.Text = _vm.StatusMessage;
                GBAColorBox.Value = _vm.GBAColor;
                RefreshSwatch();
            }
            catch (Exception ex) { Log.ErrorF("SystemHoverColorViewerView.OnSelected: {0}", ex.Message); }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void GBAColorBox_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            // Live-preview: push the edited GBA Color into the VM so the bound
            // read-only R/G/B NumericUpDowns re-decode and update, then refresh
            // the swatch. The VM's GBAColor setter re-decodes ColorR/G/B; no ROM
            // write happens here (that's deferred to Write_Click).
            _vm.GBAColor = (uint)(GBAColorBox.Value ?? 0);
            RefreshSwatch();
        }

        void RefreshSwatch()
        {
            uint c = _vm.GBAColor;
            uint r5 = c & 0x1F;
            uint g5 = (c >> 5) & 0x1F;
            uint b5 = (c >> 10) & 0x1F;
            ColorPreview.Background = new SolidColorBrush(
                Color.FromRgb((byte)(r5 * 8), (byte)(g5 * 8), (byte)(b5 * 8)));
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit System Area Color");
            try
            {
                _vm.GBAColor = (uint)(GBAColorBox.Value ?? 0);
                uint res = _vm.Write();
                if (res == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    CoreState.Services?.ShowError("Write failed: address not valid or ROM not loaded.");
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                // Reload list label + swatch to reflect the written value.
                StatusLabel.Text = _vm.StatusMessage;
                GBAColorBox.Value = _vm.GBAColor;
                RefreshSwatch();
                LoadList();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SystemHoverColorViewerView.Write_Click: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
