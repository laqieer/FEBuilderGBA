using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class MenuCommandView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly MenuCommandViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Menu Command";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public MenuCommandView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            NameTextIdBox.ValueChanged += OnNameTextIdChanged;
            HelpTextIdBox.ValueChanged += OnHelpTextIdChanged;
            Opened += (_, _) => LoadList();
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

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadMenuCommandList();
                EntryList.SetItems(items);
                UpdateUsabilityHint();
            }
            catch (Exception ex)
            {
                Log.Error("MenuCommandView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        /// <summary>
        /// Port of WinForms MenuCommandForm.Explain12 — shows the well-known usability
        /// FUNCTION addresses (always-show / hide menu routines) as READ-ONLY hint text.
        /// These addresses are ROM code, not 36-byte MenuCommand records, so they are
        /// deliberately NOT list rows (#1404).
        /// </summary>
        void UpdateUsabilityHint()
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) { UsabilityHintLabel.Text = ""; return; }

            uint always = rom.RomInfo.MenuCommand_UsabilityAlways;
            uint never = rom.RomInfo.MenuCommand_UsabilityNever;

            // FE6 has no "hide" routine — show the always-only form (matches Explain12).
            if (rom.RomInfo.version == 6 || never == 0)
            {
                UsabilityHintLabel.Text = R._(
                    "よく使われる関数のメモ\r\n{0} 常にメニューの項目を表示します。",
                    U.ToHexString8(U.toPointer(always + 1)));
            }
            else
            {
                UsabilityHintLabel.Text = R._(
                    "よく使われる関数のメモ\r\n{0} 常にメニューの項目を表示します。\r\n{1} メニューを非表示にします。",
                    U.ToHexString8(U.toPointer(always + 1)),
                    U.ToHexString8(U.toPointer(never + 1)));
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadMenuCommand(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("MenuCommandView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            JpNamePtrBox.Text = $"0x{_vm.JpNamePointer:X08}";
            NameTextIdBox.Value = _vm.NameTextId;
            try { NameTextPreview.Text = _vm.NameTextId != 0 ? NameResolver.GetTextById(_vm.NameTextId) : ""; }
            catch { NameTextPreview.Text = ""; }
            HelpTextIdBox.Value = _vm.HelpTextId;
            try { HelpTextPreview.Text = _vm.HelpTextId != 0 ? NameResolver.GetTextById(_vm.HelpTextId) : ""; }
            catch { HelpTextPreview.Text = ""; }
            ColorIdBox.Text = $"0x{_vm.ColorAndIdDword:X08}";
            UsabilityBox.Text = $"0x{_vm.UsabilityRoutine:X08}";
            DrawBox.Text = $"0x{_vm.DrawRoutine:X08}";
            EffectBox.Text = $"0x{_vm.EffectRoutine:X08}";
            PerTurnBox.Text = $"0x{_vm.PerTurnCallback:X08}";
            CursorSelBox.Text = $"0x{_vm.CursorSelectAction:X08}";
            CancelBox.Text = $"0x{_vm.CancelAction:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Menu Command");
            try
            {
                _vm.JpNamePointer = ParseHexText(JpNamePtrBox.Text);
                _vm.NameTextId = (uint)(NameTextIdBox.Value ?? 0);
                _vm.HelpTextId = (uint)(HelpTextIdBox.Value ?? 0);
                _vm.ColorAndIdDword = ParseHexText(ColorIdBox.Text);
                _vm.UsabilityRoutine = ParseHexText(UsabilityBox.Text);
                _vm.DrawRoutine = ParseHexText(DrawBox.Text);
                _vm.EffectRoutine = ParseHexText(EffectBox.Text);
                _vm.PerTurnCallback = ParseHexText(PerTurnBox.Text);
                _vm.CursorSelectAction = ParseHexText(CursorSelBox.Text);
                _vm.CancelAction = ParseHexText(CancelBox.Text);
                if (!_vm.WriteMenuCommand())
                {
                    // Refused (no valid record / usability code address) — do not commit
                    // or report a false success (#1404).
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Menu command data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("MenuCommandView.Write: {0}", ex.Message); }
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
