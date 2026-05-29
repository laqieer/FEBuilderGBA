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
            // Populate FilterCombo items via R._() so they pick up ja/zh
            // translations at runtime — ViewTranslationHelper does not touch
            // ComboBoxItem.Content (PR #571 Copilot bot review #6).
            FilterCombo.Items.Add(R._("0=AI1"));
            FilterCombo.Items.Add(R._("1=AI2"));
            FilterCombo.SelectedIndex = 0;

            EntryList.SelectedAddressChanged += OnSelected;
            FilterCombo.SelectionChanged += FilterCombo_SelectionChanged;
            Opened += (_, _) => LoadList();
        }

        // -----------------------------------------------------------------
        // List load + filter (FilterIndex 0 = AI1, 1 = AI2) — Copilot v2 #3
        // -----------------------------------------------------------------

        /// <summary>
        /// Load the address list. On initial load (initial=true) the VM
        /// scan window is RESET (TopAddress = 0, ReadCount = 0) so
        /// LoadList() reseeds from the AI{1,2}_pointer table base; the UI
        /// Top Address box is then set to the RESOLVED base
        /// (`rom.p32(pointer)`), matching EDView's seeding pattern. On
        /// user-driven Reload (initial=false) the UI values drive the
        /// VM scan window so the user can adjust the read range,
        /// mirroring WF panel3 / panel1 behavior.
        /// (PR #571 Copilot bot review #1+#2 follow-up.)
        /// </summary>
        void LoadList(bool initial = true)
        {
            _vm.IsLoading = true;
            try
            {
                if (initial)
                {
                    // Reset VM scan window so LoadList() reseeds from
                    // ai{1,2}_pointer instead of the previous TopAddress
                    // (which is wrong after a Switch combo change).
                    _vm.TopAddress = 0;
                    _vm.ReadCount = 0;
                }
                else
                {
                    // Honor the user-edited Top Address / Read Count.
                    // #649: editable inputs unified via EditorTopBarWithInputs.
                    _vm.TopAddress = TopBar.ReadStartAddress;
                    _vm.ReadCount = (uint)TopBar.ReadCount;
                }

                var items = _vm.LoadList();
                EntryList.SetItems(items);

                if (initial)
                {
                    // Surface RESOLVED defaults: TopAddress shows the
                    // table base (rom.p32(pointer)), not the pointer
                    // location itself. Matches EDView's seeding.
                    ROM? rom = CoreState.ROM;
                    if (rom?.RomInfo != null)
                    {
                        uint tablePtr = _vm.FilterIndex == 1
                            ? rom.RomInfo.ai2_pointer
                            : rom.RomInfo.ai1_pointer;
                        uint resolvedBase = rom.p32(tablePtr);
                        TopBar.ReadStartAddress = resolvedBase;
                        _vm.TopAddress = resolvedBase;
                    }
                    TopBar.ReadCount = items.Count;
                    _vm.ReadCount = (uint)items.Count;
                }
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
                LoadList(initial: true); // reseed defaults from the new pointer
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
                // User-driven reload — honor edited Top Address / Read Count.
                LoadList(initial: false);
            }
            catch (Exception ex)
            {
                Log.Error("AIScriptView.Reload_Click failed: {0}", ex.Message);
            }
        }

        // #649: routed event from the unified EditorTopBarWithInputs Reload
        // button.
        void OnTopBarReloadRequested(object? sender, RoutedEventArgs e)
        {
            Reload_Click(sender, e);
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
        // Write-back. WF AllWriteButton_Click writes the full disassembled
        // opcode list back; that path is WinForms-coupled via EventScript.
        // DisAssemble + EventScriptUtil.JisageReorder. Without that, an
        // Avalonia "Write" can't honestly mutate the ROM, so we tell the
        // user and short-circuit BEFORE allocating an undo scope (per
        // PR #571 Copilot bot review #1 — no ghost undo entry, no
        // misleading "success" toast).
        // -----------------------------------------------------------------

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            CoreState.Services.ShowInfo(
                "AI script Write is not yet implemented in Avalonia. The full opcode write-back requires the WinForms EventScript.DisAssemble pipeline, which is still WinForms-coupled. Use the WinForms editor for AI script writes.");
        }

        // -----------------------------------------------------------------
        // Re-read mirrors WF N_ReloadListButton_Click: read `bytecount`
        // bytes starting at `Address` and disassemble them into the
        // Disassembly list. The VM's DisassembleScript() walks the FIXED
        // 16-byte AI instruction grid and renders each opcode as a real
        // mnemonic + decoded args + comment (parity with the WinForms
        // AIScriptForm opcode list), replacing the previous raw byte dump
        // (#757). The byte range comes from the VM's CurrentAddr /
        // ReadByteCount; the ROM-bounds / safety guard lives inside
        // DisassembleScript().
        // -----------------------------------------------------------------

        void ReloadList_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.CurrentAddr = (uint)(AddressBox.Value ?? 0);
                _vm.ReadByteCount = (uint)(ReadByteCountBox.Value ?? 0);

                DisassemblyList.ItemsSource = _vm.DisassembleScript();
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
