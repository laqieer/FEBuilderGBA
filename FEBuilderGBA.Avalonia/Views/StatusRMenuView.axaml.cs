using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusRMenuView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly StatusRMenuViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Status R-Menu";
        public new bool IsLoaded => _vm.CanWrite;

        public EditorDescriptor Descriptor => new("Status R-Menu Editor", 1238, 604, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public StatusRMenuView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            TextIdBox.ValueChanged += OnTextIdChanged;
            FilterComboBox.SelectionChanged += FilterCombo_SelectionChanged;
        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                PopulateFilterCombo();

                // Selecting index 0 fires FilterCombo_SelectionChanged →

                // LoadList(). If the combo ended up empty (no ROM) load directly.

                if (FilterComboBox.ItemCount > 0)

                FilterComboBox.SelectedIndex = 0;

                else

                LoadList();

            }

        }

        /// <summary>
        /// Populate the RMenu table switcher from
        /// <see cref="StatusRMenuListCore.TableCount"/> (5 entries, +1 FE8-only
        /// status-screen entry). Mirrors WinForms StatusRMenuForm's
        /// version-gated FilterComboBox population (#1459).
        /// </summary>
        void PopulateFilterCombo()
        {
            FilterComboBox.Items.Clear();
            int count = _vm.GetTableCount();
            for (int i = 0; i < count && i < StatusRMenuListCore.TableLabelKeys.Length; i++)
            {
                FilterComboBox.Items.Add(R._(StatusRMenuListCore.TableLabelKeys[i]));
            }
        }

        void FilterCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            _vm.SelectedTableIndex = Math.Max(0, FilterComboBox.SelectedIndex);
            LoadList();
        }

        void OnTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(TextIdBox.Value ?? 0);
            try { TextIdPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { TextIdPreview.Text = ""; }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadStatusRMenuList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("StatusRMenuView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadStatusRMenu(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("StatusRMenuView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UpPtrBox.Text = $"0x{_vm.UpPtr:X08}";
            DownPtrBox.Text = $"0x{_vm.DownPtr:X08}";
            LeftPtrBox.Text = $"0x{_vm.LeftPtr:X08}";
            RightPtrBox.Text = $"0x{_vm.RightPtr:X08}";
            PosXBox.Value = _vm.PosX;
            PosYBox.Value = _vm.PosY;
            TextIdBox.Value = _vm.TextId;
            try { TextIdPreview.Text = _vm.TextId != 0 ? NameResolver.GetTextById(_vm.TextId) : ""; }
            catch { TextIdPreview.Text = ""; }
            LoopBox.Text = $"0x{_vm.LoopRoutine:X08}";
            GetterBox.Text = $"0x{_vm.GetterRoutine:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Status R-Menu");
            try
            {
                _vm.UpPtr = ParseHexText(UpPtrBox.Text);
                _vm.DownPtr = ParseHexText(DownPtrBox.Text);
                _vm.LeftPtr = ParseHexText(LeftPtrBox.Text);
                _vm.RightPtr = ParseHexText(RightPtrBox.Text);
                _vm.PosX = (uint)(PosXBox.Value ?? 0);
                _vm.PosY = (uint)(PosYBox.Value ?? 0);
                _vm.TextId = (uint)(TextIdBox.Value ?? 0);
                _vm.LoopRoutine = ParseHexText(LoopBox.Text);
                _vm.GetterRoutine = ParseHexText(GetterBox.Text);
                _vm.WriteStatusRMenu();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Status R-Menu data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("StatusRMenuView.Write: {0}", ex.Message); }
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
    }
}
