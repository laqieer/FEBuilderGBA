// SPDX-License-Identifier: GPL-3.0-or-later
// AIScriptView — Avalonia parity rebuild for #410. Mirrors `AIScriptForm`
// layout (panel3 read-config + panel6 master list + panel5 write bar +
// ControlPanel detail with 5 parameter rows). Per Copilot CLI plan-review
// v2 #1, only wired `WindowManager.Navigate<>` callsites get manifest rows;
// the AI sub-editors (AIUnits / AITiles / AIASMCoordinate / AIASMRange /
// AIASMCALLTALK / AIScriptCategorySelect) stay deferred and explicitly
// absent from both manifest and click-handler wiring.
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AIScriptView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly AIScriptViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "AI Script Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public AIScriptView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            FilterCombo.SelectionChanged += FilterCombo_SelectionChanged;
            Opened += (_, _) => LoadList();
        }

        // -----------------------------------------------------------------
        // List load + filter (FilterIndex 0 = AI1, 1 = AI2) — Copilot v2 #3
        // -----------------------------------------------------------------

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                // Surface auto-detected read-config defaults into the UI.
                ROM? rom = CoreState.ROM;
                if (rom?.RomInfo != null)
                {
                    uint tablePtr = _vm.FilterIndex == 1
                        ? rom.RomInfo.ai2_pointer
                        : rom.RomInfo.ai1_pointer;
                    TopAddressBox.Value = tablePtr;
                }
                ReadCountBox.Value = (decimal)items.Count;
            }
            catch (Exception ex)
            {
                Log.Error("AIScriptView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void FilterCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Copilot CLI plan-review v2 #3: AI1 / AI2 are separate pointer
            // tables. Toggling the filter reloads from the OTHER pointer
            // (parity with WF AIScriptForm.FilterComboBox_SelectedIndexChanged).
            try
            {
                int newIndex = FilterCombo.SelectedIndex;
                if (newIndex == _vm.FilterIndex) return;
                _vm.FilterIndex = newIndex;
                LoadList();
            }
            catch (Exception ex)
            {
                Log.Error("AIScriptView.FilterCombo_SelectionChanged failed: {0}", ex.Message);
            }
        }

        void Reload_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                LoadList();
            }
            catch (Exception ex)
            {
                Log.Error("AIScriptView.Reload_Click failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Selection / detail update
        // -----------------------------------------------------------------

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
                Log.Error("AIScriptView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddressBox.Value = _vm.CurrentAddr;
            ReadByteCountBox.Value = _vm.ReadByteCount;
            DetailAddressLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ScriptByteCountLabel.Text = _vm.ReadByteCount.ToString();
            CommentBox.Text = _vm.CommentText;
            AsmBox.Text = _vm.AsmText;
            ScriptCodeNameLabel.Text = _vm.ScriptCodeName;
        }

        // -----------------------------------------------------------------
        // Write-back (mirrors WF AllWriteButton_Click)
        // Wraps every ROM write in UndoService.Begin/Commit per plan WU2.
        // -----------------------------------------------------------------

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin("Edit AI Script");
            try
            {
                _vm.CurrentAddr = (uint)(AddressBox.Value ?? 0);
                _vm.ReadByteCount = (uint)(ReadByteCountBox.Value ?? 0);
                _vm.CommentText = CommentBox.Text ?? "";
                _vm.AsmText = AsmBox.Text ?? "";
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("AI script data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("AIScriptView.Write_Click failed: {0}", ex.Message);
            }
        }

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.CurrentAddr = (uint)(AddressBox.Value ?? 0);
                _vm.ReadByteCount = (uint)(ReadByteCountBox.Value ?? 0);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("AIScriptView.ReloadList_Click failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // List expansion
        // -----------------------------------------------------------------

        void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            // Bringing the pointer table expand path live requires the same
            // P4-clearing logic the WF AddressListExpandsEventNoCopyPointer
            // handler runs. That logic is host-coupled to InputFormRef. For
            // now we surface an informational message so the parity surface
            // exists without risking a partial-expand on the live ROM.
            CoreState.Services.ShowInfo("List Expand is reserved — use the WinForms editor to expand the AI pointer table for now.");
        }

        // -----------------------------------------------------------------
        // Detail-row action buttons (no-op host parity placeholders).
        // The fully functional Update / Remove / New flows require the WF
        // EventScript.DisAssemble + EventScriptUtil.JisageReorder pipeline
        // (WinForms-coupled). We emit informational dialogs so the surface
        // matches the WF affordance set without misleading the user.
        // -----------------------------------------------------------------

        void Update_Click(object? sender, RoutedEventArgs e)
        {
            CoreState.Services.ShowInfo("AI opcode editing is not yet fully implemented in Avalonia. Use the WinForms editor for opcode-level changes.");
        }

        void New_Click(object? sender, RoutedEventArgs e)
        {
            CoreState.Services.ShowInfo("AI opcode editing is not yet fully implemented in Avalonia. Use the WinForms editor for opcode-level changes.");
        }

        void Remove_Click(object? sender, RoutedEventArgs e)
        {
            CoreState.Services.ShowInfo("AI opcode editing is not yet fully implemented in Avalonia. Use the WinForms editor for opcode-level changes.");
        }

        void Close_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WF CloseButton_Click — Close button collapses the
            // floating control panel; here we simply clear the detail
            // fields. The window itself stays open.
            CommentBox.Text = "";
            AsmBox.Text = "";
            ScriptCodeNameLabel.Text = "";
        }

        void ScriptChange_Click(object? sender, RoutedEventArgs e)
        {
            // The WF flow opens AIScriptCategorySelectForm modally and writes
            // the chosen opcode bytes back into ASMTextBox. The Avalonia
            // AIScriptCategorySelectView is a stub today; we surface the
            // affordance without navigating to avoid putting the user on a
            // dead-end screen. Per Copilot v2 #1 this jump deliberately stays
            // OUT of the manifest (MissingAvManifest in the scanner output).
            CoreState.Services.ShowInfo("Script category picker is not yet wired in Avalonia. Use the WinForms editor for category-based opcode selection.");
        }

        // -----------------------------------------------------------------
        // Address-double-click — open PointerToolCopyToView at this offset.
        // Mirrors WF AddressLabel_Click. WIRED jump (manifest row present).
        // -----------------------------------------------------------------

        void DetailAddress_Click(object? sender, PointerPressedEventArgs e)
        {
            if (!_vm.IsLoaded || _vm.CurrentAddr == 0) return;
            try
            {
                WindowManager.Instance.Navigate<PointerToolCopyToView>(_vm.CurrentAddr);
            }
            catch (Exception ex)
            {
                Log.Error("AIScriptView.DetailAddress_Click failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // File export / import (mirrors WF EventToFile / FileToEvent).
        // -----------------------------------------------------------------

        async void Export_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;
            try
            {
                var txtType = new FilePickerFileType(R._("Text Files")) { Patterns = new[] { "*.txt", "*.event" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = R._("Export AI Script"),
                    SuggestedFileName = $"aiscript_0x{_vm.CurrentAddr:X06}.txt",
                    FileTypeChoices = new[] { txtType, allType },
                });
                string? path = file?.TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                // Stub export: dump CurrentAddr + ReadByteCount header. The
                // full byte-for-byte AI script dump requires the WinForms
                // EventScript.DisAssemble pipeline; we emit a placeholder so
                // the file-write surface is at parity with the WF affordance.
                System.IO.File.WriteAllText(path,
                    $"// AI Script export\n" +
                    $"// Address: 0x{_vm.CurrentAddr:X08}\n" +
                    $"// Bytes: {_vm.ReadByteCount}\n");
                CoreState.Services.ShowInfo($"AI script header exported to {path}");
            }
            catch (Exception ex)
            {
                Log.Error("AIScriptView.Export_Click failed: {0}", ex.Message);
            }
        }

        async void Import_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var txtType = new FilePickerFileType(R._("Text Files")) { Patterns = new[] { "*.txt", "*.event" } };
                var allType = new FilePickerFileType(R._("All Files")) { Patterns = new[] { "*" } };
                var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = R._("Import AI Script"),
                    AllowMultiple = false,
                    FileTypeFilter = new[] { txtType, allType },
                });
                if (files.Count == 0) return;
                string? path = files[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(path)) return;

                // Stub import: parse header bytes only. Full byte-stream
                // import requires the WF AIScript.DisAssemble path which is
                // WinForms-coupled.
                CoreState.Services.ShowInfo(
                    "AI script import will fully populate once the Core extraction lands. " +
                    $"Selected file: {path}");
            }
            catch (Exception ex)
            {
                Log.Error("AIScriptView.Import_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
