using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class StatusOptionView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly StatusOptionViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Status Screen Options";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Status Screen Options", 1238, 806, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public StatusOptionView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            ExpandListButton.Click += OnExpandListClick;
            IdTextIdBox.ValueChanged += OnIdTextIdChanged;
            NameTextIdBox.ValueChanged += OnNameTextIdChanged;
            HelpTextIdBox.ValueChanged += OnHelpTextIdChanged;
            DefaultTextIdBox.ValueChanged += OnDefaultTextIdChanged;
            YesTextIdBox.ValueChanged += OnYesTextIdChanged;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadList();
            }
        }

        // リストの拡張 — expand the 44-byte status_game_option (game option) table
        // by a prompted count, mirroring WinForms StatusOptionForm's
        // AddressListExpandsButton + OnPreGameOptionExtendsWarningHandler (#1607).
        // The whole expand runs under one UndoService scope with a byte-identical
        // fault restore inside StatusGameOptionCore.ExpandGameOptionTable. On a
        // cancel / declined warning / malformed / zero / same count this is a no-op.
        async void OnExpandListClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (CoreState.ROM == null)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }

                uint current = StatusGameOptionCore.CountGameOptions(CoreState.ROM);
                if (current == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: the game option list is empty."));
                    return;
                }

                // Max = the Core/editor enumeration cap (StatusGameOptionCore.MaxRows
                // == 0x100). Refuse when already at the cap so the dialog never gets
                // an invalid min>max range (Copilot PR #1612 inline review).
                uint maxCount = StatusGameOptionCore.MaxRows;
                if (current >= maxCount)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: the game option list is already at the maximum of {0} entries.", maxCount));
                    return;
                }

                // Default = current + 1, clamped to the cap.
                uint defaultCount = current + 1;
                if (defaultCount > maxCount) defaultCount = maxCount;
                uint? chosen = await NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the new game option entry count (current: {0}, max: {1}).", current, maxCount),
                    R._("List Expansion"),
                    defaultCount,
                    current,
                    maxCount);
                if (chosen == null) return; // user cancelled

                uint newCount = chosen.Value;
                if (newCount <= current)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count must be greater than the current count."));
                    return;
                }
                uint addCount = newCount - current;

                // Pre-expand repoint warning — faithful port of WinForms
                // StatusOptionForm.OnPreGameOptionExtendsWarningHandler. Declining
                // aborts with ZERO mutation.
                if (CoreState.Services?.ShowYesNo(
                        R._("ゲームオプションの拡張はROMの破壊につながることがあります。\r\nゲームオプションの拡張は、新しいゲームオプションを実現する時にのみにやるべきです。\r\nゲームオプションの順番や非表示をやりたいだけであれば、ゲームオプションの順番の方で行ってください。\r\n\r\n上記を理解した上で、それでもリポイント処理を実行してもいいですか？\r\n")) != true)
                    return;

                _undoService.Begin("Expand Game Options");
                try
                {
                    var result = StatusGameOptionCore.ExpandGameOptionTable(
                        CoreState.ROM, addCount, _undoService.GetActiveUndoData(), out string err);
                    if (!result.Success)
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(string.IsNullOrEmpty(err)
                            ? R._("Game option list expansion failed.") : err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    LoadList();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded game option list to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.Error($"StatusOptionView.OnExpandListClick inner failed: {inner}");
                    CoreState.Services?.ShowError(R._("Game option list expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"StatusOptionView.OnExpandListClick failed: {ex}");
            }
        }

        void OnIdTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(IdTextIdBox.Value ?? 0);
            try { IdTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { IdTextPreview.Text = ""; }
        }

        void OnNameTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(NameTextIdBox.Value ?? 0);
            try { NameTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { NameTextPreview.Text = ""; }
        }

        void OnHelpTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(HelpTextIdBox.Value ?? 0);
            try { HelpTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { HelpTextPreview.Text = ""; }
        }

        void OnDefaultTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(DefaultTextIdBox.Value ?? 0);
            try { DefaultTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { DefaultTextPreview.Text = ""; }
        }

        void OnYesTextIdChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_vm.IsLoading) return;
            uint id = (uint)(YesTextIdBox.Value ?? 0);
            try { YesTextPreview.Text = id != 0 ? NameResolver.GetTextById(id) : ""; }
            catch { YesTextPreview.Text = ""; }
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
                Log.ErrorF("StatusOptionView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("StatusOptionView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            IdTextIdBox.Value = _vm.IdTextId;
            try { IdTextPreview.Text = _vm.IdTextId != 0 ? NameResolver.GetTextById(_vm.IdTextId) : ""; }
            catch { IdTextPreview.Text = ""; }
            NameTextIdBox.Value = _vm.NameTextId;
            try { NameTextPreview.Text = _vm.NameTextId != 0 ? NameResolver.GetTextById(_vm.NameTextId) : ""; }
            catch { NameTextPreview.Text = ""; }
            HelpTextIdBox.Value = _vm.HelpTextId;
            try { HelpTextPreview.Text = _vm.HelpTextId != 0 ? NameResolver.GetTextById(_vm.HelpTextId) : ""; }
            catch { HelpTextPreview.Text = ""; }
            PosXBox.Value = _vm.PosX;
            PosYBox.Value = _vm.PosY;
            Sel1Box.Value = _vm.SelectionText1;
            Sel2Box.Value = _vm.SelectionText2;
            ColumnsBox.Value = _vm.Columns;
            RowsBox.Value = _vm.Rows;
            DefaultTextIdBox.Value = _vm.DefaultTextId;
            try { DefaultTextPreview.Text = _vm.DefaultTextId != 0 ? NameResolver.GetTextById(_vm.DefaultTextId) : ""; }
            catch { DefaultTextPreview.Text = ""; }
            YesTextIdBox.Value = _vm.YesTextId;
            try { YesTextPreview.Text = _vm.YesTextId != 0 ? NameResolver.GetTextById(_vm.YesTextId) : ""; }
            catch { YesTextPreview.Text = ""; }
            MinValueBox.Value = _vm.MinValue;
            MaxValueBox.Value = _vm.MaxValue;
            OnOff1Box.Value = _vm.OnOffText1;
            OnOff2Box.Value = _vm.OnOffText2;
            DefaultValueBox.Value = _vm.DefaultValue;
            OptionTypeBox.Value = _vm.OptionType;
            IconIdBox.Value = _vm.IconId;
            AsmPointerBox.Text = $"0x{_vm.AsmPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            _undoService.Begin("Edit Status Option");
            try
            {
                _vm.IdTextId = (uint)(IdTextIdBox.Value ?? 0);
                _vm.NameTextId = (uint)(NameTextIdBox.Value ?? 0);
                _vm.HelpTextId = (uint)(HelpTextIdBox.Value ?? 0);
                _vm.PosX = (uint)(PosXBox.Value ?? 0);
                _vm.PosY = (uint)(PosYBox.Value ?? 0);
                _vm.SelectionText1 = (uint)(Sel1Box.Value ?? 0);
                _vm.SelectionText2 = (uint)(Sel2Box.Value ?? 0);
                _vm.Columns = (uint)(ColumnsBox.Value ?? 0);
                _vm.Rows = (uint)(RowsBox.Value ?? 0);
                _vm.DefaultTextId = (uint)(DefaultTextIdBox.Value ?? 0);
                _vm.YesTextId = (uint)(YesTextIdBox.Value ?? 0);
                _vm.MinValue = (uint)(MinValueBox.Value ?? 0);
                _vm.MaxValue = (uint)(MaxValueBox.Value ?? 0);
                _vm.OnOffText1 = (uint)(OnOff1Box.Value ?? 0);
                _vm.OnOffText2 = (uint)(OnOff2Box.Value ?? 0);
                _vm.DefaultValue = (uint)(DefaultValueBox.Value ?? 0);
                _vm.OptionType = (uint)(OptionTypeBox.Value ?? 0);
                _vm.IconId = (uint)(IconIdBox.Value ?? 0);
                _vm.AsmPointer = ParseHexText(AsmPointerBox.Text);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Status option data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("StatusOptionView.Write: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
