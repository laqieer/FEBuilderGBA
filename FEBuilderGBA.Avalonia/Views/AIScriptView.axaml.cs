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
using FEBuilderGBA.Avalonia.Dialogs;
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
                Log.ErrorF("AIScriptView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("AIScriptView.FilterCombo_SelectionChanged failed: {0}", ex.Message);
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
                Log.ErrorF("AIScriptView.Reload_Click failed: {0}", ex.Message);
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
                // Re-disassemble for the newly-selected entry. LoadEntry cleared
                // the editable model; this repopulates it for THIS entry so the
                // list shows the right opcodes and a later New/Remove/Write
                // operates on the loaded entry (not a stale one).
                DisassemblyList.ItemsSource = _vm.DisassembleScript();
                // Auto-select the first opcode (#1600) so the detail-panel
                // parameter rows populate immediately — otherwise the 5 param
                // rows (and their POINTER_AI* jump affordance) stay blank until
                // the user clicks a disassembly row. SelectionChanged ->
                // UpdateParamRows fills them.
                if (DisassemblyList.ItemCount > 0)
                    DisassemblyList.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIScriptView.OnSelected failed: {0}", ex.Message);
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
        // Write-back (#760/#763). Serializes the in-memory disassembled model
        // (with a WF-parity EXIT terminator append) and writes it back under a
        // UndoService scope. Same-size edits write in place at CurrentAddr; a
        // length change from New/Remove reallocates to free space and repoints
        // the AI pointer slot — both signed against the active undoData so the
        // whole operation commits / rolls back as one transaction. Mirrors WF
        // AllWriteButton_Click. The Rollback-on-false path discards any orphan
        // free-space allocation.
        // -----------------------------------------------------------------

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // Guard an empty/never-loaded model BEFORE opening the undo scope so
            // we never produce a ghost undo entry or the misleading error when
            // the user hasn't pressed Re-read yet.
            if (!_vm.HasDisassembly)
            {
                CoreState.Services.ShowInfo(R._("Re-read the AI script before writing."));
                return;
            }
            _undoService.Begin("Edit AI Script");
            try
            {
                if (!_vm.WriteScript(_undoService.GetActiveUndoData()))
                {
                    _undoService.Rollback();
                    CoreState.Services.ShowError(
                        R._("Could not write (out of range or the AI pointer slot is unsafe)."));
                    return;
                }
                _undoService.Commit();
                CoreState.Services.ShowInfo(R._("AI script written."));
                // Sync the Address / byte-count boxes to the VM FIRST: a
                // realloc Write moves CurrentAddr / ReadByteCount, and
                // ReloadList_Click re-reads from those boxes — so without this
                // the re-read would disassemble the OLD (pre-relocation)
                // location. UpdateUI() pushes the VM's new address/length back
                // into the boxes before the re-read.
                UpdateUI();
                ReloadList_Click(sender, e);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("AIScriptView.Write: {0}", ex.Message);
            }
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
                Log.ErrorF("AIScriptView.ReloadList_Click failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // List expansion
        // -----------------------------------------------------------------

        /// <summary>
        /// List-expansion handler (#1020). Prompts the user for a new pointer-slot
        /// count, delegates to <see cref="AIScriptViewModel.ExpandList"/> inside an
        /// <see cref="UndoService"/> scope, then reloads the list. Mirrors WF
        /// <c>AIScriptForm.AddressListExpandsEventNoCopyPointer</c> (prompt ->
        /// ExpandTableTo -> repoint ai*[3] -> reload) and the
        /// <c>ImageMapActionAnimationView.ListExpand_Click</c> flow.
        /// </summary>
        async void ListExpand_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (!_vm.IsLoaded)
                {
                    CoreState.Services?.ShowInfo(R._("Load a ROM first."));
                    return;
                }
                // Drive the prompt + expansion from the ACTUAL loaded list size, NOT
                // _vm.ReadCount: the editor's "Read Count" can be 0 ("no cap") or exceed
                // the dialog max after a user-driven reload, which would either treat a
                // non-empty list as empty or pass an invalid min>max range to the dialog
                // (which does not validate its inputs). #1056 review.
                uint currentCount = (uint)EntryList.ItemCount;
                if (currentCount == 0)
                {
                    CoreState.Services?.ShowInfo(R._("Cannot expand: list is empty."));
                    return;
                }

                // Max mirrors the Read Count NUD's ReadCountMaximum (4096), clamped up to
                // currentCount so min never exceeds max. ExpandTableTo fails gracefully
                // when there is insufficient free space.
                uint maxCount = System.Math.Max(4096u, currentCount);
                uint defaultCount = currentCount + 1;
                if (defaultCount > maxCount) defaultCount = maxCount;
                uint? chosen = await NumberInputDialog.Show(
                    this,
                    R._("Enter the new entry count for the AI pointer table (current: {0}, max: {1}).",
                        currentCount, maxCount),
                    R._("List Expansion"),
                    defaultCount,
                    currentCount,
                    maxCount);
                if (chosen == null) return; // user cancelled
                uint newCount = chosen.Value;
                if (newCount == currentCount)
                {
                    CoreState.Services?.ShowInfo(R._("No change: new count equals current count."));
                    return;
                }

                // Re-sync the VM's current count to the actual list size so ExpandList
                // copies the FULL table (it uses _vm.ReadCount as the old row count).
                _vm.ReadCount = currentCount;

                _undoService.Begin("Expand AI Script List");
                try
                {
                    string err = _vm.ExpandList(newCount, _undoService.GetActiveUndoData());
                    if (!string.IsNullOrEmpty(err))
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    LoadList();
                    CoreState.Services?.ShowInfo(
                        R._("Expanded AI pointer table to {0} entries.", newCount));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.ErrorF("AIScriptView.ListExpand_Click inner failed: {0}", inner.Message);
                    CoreState.Services?.ShowError(R._("List expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIScriptView.ListExpand_Click failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Disassembly row selection (#760): mirror the selected instruction's
        // bytes into the Binary Code box and its mnemonic into the Description
        // label so the user can hand-edit the hex and re-decode via Update.
        // -----------------------------------------------------------------

        void DisassemblyList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            try
            {
                int idx = DisassemblyList.SelectedIndex;
                AsmBox.Text = _vm.GetRowHex(idx) ?? "";
                ScriptCodeNameLabel.Text = _vm.GetRowOpcodeName(idx) ?? "";
                UpdateParamRows(idx);
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIScriptView.DisassemblyList_SelectionChanged failed: {0}", ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Parameter rows + POINTER_AI* jump (#1600). Populate the 5 detail
        // param rows from the selected opcode's non-FIXED args, and make an
        // AI-pointer param label jump to the matching sub-editor (allocating a
        // 4-byte ASM block on null/broken for Coordinate/Range/CallTalk).
        // Mirrors WF AIScriptForm.ParamLabel_Clicked.
        // -----------------------------------------------------------------

        TextBlock[] ParamLabels => new[] { Param1Label, Param2Label, Param3Label, Param4Label, Param5Label };
        NumericUpDown[] ParamBoxes => new[] { Param1Box, Param2Box, Param3Box, Param4Box, Param5Box };
        TextBox[] ParamValues => new[] { Param1Value, Param2Value, Param3Value, Param4Value, Param5Value };

        void UpdateParamRows(int rowIdx)
        {
            int count = rowIdx < 0 ? 0 : _vm.GetParamCount(rowIdx);
            TextBlock[] labels = ParamLabels;
            NumericUpDown[] boxes = ParamBoxes;
            TextBox[] values = ParamValues;
            for (int row = 1; row <= 5; row++)
            {
                int i = row - 1;
                if (row <= count && _vm.TryGetParamArg(rowIdx, row, out _, out _, out uint value))
                {
                    AiPointerKind kind = _vm.ClassifyParam(rowIdx, row);
                    string label = _vm.GetParamLabel(rowIdx, row);
                    // Hint that an AI-pointer label is clickable.
                    labels[i].Text = kind != AiPointerKind.None ? $"→ {label}" : label;
                    labels[i].IsEnabled = true;
                    boxes[i].Value = value;
                    boxes[i].IsEnabled = true;
                    values[i].Text = _vm.GetParamValueText(rowIdx, row);
                    values[i].IsVisible = true;
                    boxes[i].IsVisible = true;
                    labels[i].IsVisible = true;
                }
                else
                {
                    labels[i].Text = $"Parameter {row}";
                    boxes[i].Value = 0;
                    values[i].Text = "";
                    // Keep the rows present (stable layout) but clear/disable unused ones.
                    boxes[i].IsEnabled = false;
                    labels[i].IsEnabled = false;
                }
            }
        }

        void ParamLabel_Click(object? sender, PointerPressedEventArgs e)
        {
            try
            {
                if (sender is not Control ctrl || ctrl.Tag is not string tagStr
                    || !int.TryParse(tagStr, out int paramRow))
                    return;

                int rowIdx = DisassemblyList.SelectedIndex;
                if (rowIdx < 0) return;

                AiPointerKind kind = _vm.ClassifyParam(rowIdx, paramRow);
                if (kind == AiPointerKind.None) return; // not an AI-pointer param — no jump

                // WF AllocIfNeed parity: prompt before allocating a fresh ASM
                // block for a null/broken Coordinate/Range/CallTalk pointer.
                if (_vm.ParamNeedsAlloc(rowIdx, paramRow))
                {
                    // Match WinForms' per-form prompt wording (both keys already
                    // translated en/zh/ja — no new translate entry):
                    //   Coordinate/Range -> "新規に座標データを作成しますか？"
                    //   (AIASMCoordinateForm/AIASMRangeForm.AllocIfNeed)
                    //   CallTalk          -> "新規にデータを作成しますか？"
                    //   (AIASMCALLTALKForm.AllocIfNeed)
                    string promptKey = kind == AiPointerKind.CallTalk
                        ? "新規にデータを作成しますか？"
                        : "新規に座標データを作成しますか？";
                    bool proceed = CoreState.Services?.ShowYesNo(R._(promptKey)) ?? false;
                    if (!proceed) return;
                }

                AiPointerKind resolvedKind;
                uint pointerValue;
                bool allocated;
                _undoService.Begin("AIScript pointer jump");
                try
                {
                    if (!_vm.ApplyPointerJump(rowIdx, paramRow,
                            _undoService.GetActiveUndoData(),
                            out resolvedKind, out pointerValue, out allocated))
                    {
                        _undoService.Rollback();
                        return;
                    }
                    _undoService.Commit();
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    Log.Error($"AIScriptView.ParamLabel_Click apply failed: {inner}");
                    return;
                }

                // Refresh the disassembly + param rows from the IN-MEMORY model so
                // the (possibly newly-allocated) pointer shows without a ROM
                // re-read (which would discard pending row edits).
                DisassemblyList.ItemsSource = _vm.GetDisplayLines();
                if (rowIdx >= 0 && rowIdx < _vm.RowCount)
                    DisassemblyList.SelectedIndex = rowIdx;
                UpdateParamRows(rowIdx);

                OpenAiSubEditor(resolvedKind, pointerValue);
            }
            catch (Exception ex)
            {
                Log.Error($"AIScriptView.ParamLabel_Click failed: {ex}");
            }
        }

        // Open the AI sub-editor matching the pointer kind, seeded at the
        // (possibly newly-allocated) pointer. Mirrors WF JumpFormLow + JumpTo.
        // Uses the Open<T>().NavigateTo(addr) pattern; each sub-View's
        // NavigateTo direct-loads the supplied non-placeholder address (#1414).
        void OpenAiSubEditor(AiPointerKind kind, uint pointerValue)
        {
            uint addr = U.toOffset(pointerValue);
            switch (kind)
            {
                case AiPointerKind.Units:
                    WindowManager.Instance.Navigate<AIUnitsView>(addr);
                    break;
                case AiPointerKind.Tiles:
                    WindowManager.Instance.Navigate<AITilesView>(addr);
                    break;
                case AiPointerKind.Coordinate:
                    WindowManager.Instance.Navigate<AIASMCoordinateView>(addr);
                    break;
                case AiPointerKind.Range:
                    WindowManager.Instance.Navigate<AIASMRangeView>(addr);
                    break;
                case AiPointerKind.CallTalk:
                    WindowManager.Instance.Navigate<AIASMCALLTALKView>(addr);
                    break;
            }
        }

        // -----------------------------------------------------------------
        // Update (#760): re-decode the hand-edited Binary Code hex for the
        // selected row back into the model and refresh the Disassembly list
        // from the in-memory model (NOT a ROM re-read, so the pending edit is
        // reflected). Mirrors WF AIScriptForm.OneLineDisassembler. New /
        // Remove stay deferred (explicit informational stubs below).
        // -----------------------------------------------------------------

        void Update_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int idx = DisassemblyList.SelectedIndex;
                if (idx < 0)
                {
                    CoreState.Services.ShowInfo(R._("Select an instruction first."));
                    return;
                }

                string? line = _vm.UpdateRow(idx, AsmBox.Text);
                if (line == null)
                {
                    CoreState.Services.ShowError(
                        R._("Invalid instruction bytes (must be one 16-byte hex instruction)."));
                    return;
                }

                // Refresh from the in-memory model so the edit is visible
                // before any Write (DisassembleScript would re-read the
                // unmodified ROM and discard the pending edit).
                DisassemblyList.ItemsSource = _vm.GetDisplayLines();
                DisassemblyList.SelectedIndex = idx;
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIScriptView.Update_Click failed: {0}", ex.Message);
            }
        }

        // New / Remove (#763): insert / delete a 16-byte AI instruction in the
        // in-memory model (mirrors WF NewButton_Click / RemoveButton_Click).
        // These change the script length, so the next Write takes the
        // realloc + pointer-repoint path. The list is refreshed from the model
        // (GetDisplayLines), NOT a ROM re-read, so the pending structural edit
        // stays visible until Write persists it.
        void New_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // Require a loaded script first (mirror the Write guard). Without
                // this, New on an empty model would create a 1-opcode script with
                // no CurrentAddr/BaseAddr loaded, and a subsequent Write would
                // serialize only that opcode + EXIT and repoint the AI slot —
                // silently dropping all existing opcodes.
                if (!_vm.HasDisassembly)
                {
                    CoreState.Services.ShowInfo(R._("Re-read the AI script before inserting an instruction."));
                    return;
                }
                if (string.IsNullOrWhiteSpace(AsmBox.Text))
                {
                    CoreState.Services.ShowInfo(R._("Enter instruction bytes in Binary Code first."));
                    return;
                }

                string? line = _vm.InsertRow(DisassemblyList.SelectedIndex, AsmBox.Text);
                if (line == null)
                {
                    CoreState.Services.ShowError(
                        R._("Invalid instruction bytes (one 16-byte hex instruction)."));
                    return;
                }

                // Refresh from the in-memory model and select the inserted row.
                int insertedAt = DisassemblyList.SelectedIndex < 0
                    ? _vm.RowCount - 1
                    : DisassemblyList.SelectedIndex + 1;
                DisassemblyList.ItemsSource = _vm.GetDisplayLines();
                if (insertedAt >= 0 && insertedAt < _vm.RowCount)
                    DisassemblyList.SelectedIndex = insertedAt;
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIScriptView.New_Click failed: {0}", ex.Message);
            }
        }

        void Remove_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                int idx = DisassemblyList.SelectedIndex;
                if (idx < 0)
                {
                    CoreState.Services.ShowInfo(R._("Select an instruction to remove."));
                    return;
                }
                if (!_vm.RemoveRow(idx))
                {
                    CoreState.Services.ShowInfo(R._("Cannot remove the last instruction."));
                    return;
                }

                DisassemblyList.ItemsSource = _vm.GetDisplayLines();
                // Re-select a sensible neighbour (the row that shifted up into idx).
                int next = idx < _vm.RowCount ? idx : _vm.RowCount - 1;
                if (next >= 0)
                    DisassemblyList.SelectedIndex = next;
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIScriptView.Remove_Click failed: {0}", ex.Message);
            }
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

        // -----------------------------------------------------------------
        // Script Change (#766): mirror WF AIScriptForm.ScriptChangeButton_Click.
        // Open the category-based AI opcode picker modally; on a confirmed
        // selection, copy the chosen command's DEFAULT bytes into the Binary
        // Code box (and its mnemonic into the Description label) so the user can
        // then Update/New it into the script. The picker
        // (ScriptCommandPickerView, backed by AIScriptCategorySelectViewModel)
        // is the FUNCTIONAL category browser; this is a read/UI-only copy — no
        // ROM write happens here (the existing Update/New/Write path persists).
        // -----------------------------------------------------------------

        /// <summary>
        /// Copy a picked command's default bytes into the Binary Code box and its
        /// name into the Description label — the WF parity for
        /// <c>ASMTextBox.Text = U.convertByteToStringDump(script.Data)</c> +
        /// <c>ScriptCodeName.Text = makeCommandComboText(script,false)</c>.
        /// Factored out (internal) so headless tests can exercise the
        /// apply-path without a live modal. The Binary Code text is the shared
        /// <see cref="AIScriptViewModel.FormatInstructionHex"/> form that
        /// Update/New parse.
        /// </summary>
        internal void ApplyPickedScript(EventScript.Script? script)
        {
            if (script == null) return;
            AsmBox.Text = AIScriptViewModel.FormatInstructionHex(script.Data);
            ScriptCodeNameLabel.Text = EventScript.makeCommandComboText(script, false);
        }

        async void ScriptChange_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new ScriptCommandPickerView(EventScript.EventScriptType.AI);

                // AIScriptView IS a Window (TranslatedWindow : Window), so it can
                // own the modal directly.
                EventScript.Script? result = await picker.ShowDialog<EventScript.Script?>(this);

                if (result != null)
                    ApplyPickedScript(result);
                // Cancel / no selection -> leave the Binary Code box unchanged.
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIScriptView.ScriptChange_Click failed: {0}", ex.Message);
            }
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
                Log.ErrorF("AIScriptView.DetailAddress_Click failed: {0}", ex.Message);
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
                if (file == null) return;

                // Full per-opcode dump (WF AIScriptForm.EventToTextAll parity):
                // one line per 16-byte opcode — hex bytes + tab + //script-name
                // + decoded args + comment. ExportToText lazily disassembles the
                // loaded script when the in-memory model is empty.
                string content = _vm.ExportToText();
                if (string.IsNullOrEmpty(content))
                {
                    CoreState.Services.ShowInfo(R._("There is no AI script to export. Re-read the script first."));
                    return;
                }
                // #1639: write via the SAF bridge so Android content:// targets
                // (no local path) are written through OpenWriteAsync.
                string? written = await FileDialogHelper.WriteViaAsync(file, p => System.IO.File.WriteAllText(p, content));
                if (written != null) CoreState.Services.ShowInfo($"{R._("AI script exported to")} {written}");
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIScriptView.Export_Click failed: {0}", ex.Message);
            }
        }

        async void Import_Click(object? sender, RoutedEventArgs e)
        {
            // Require a loaded script (same guard as Export). Without a loaded
            // pointer slot / CurrentAddr, an import would populate a model whose
            // rows show 0x000000 addresses and a later Write would have no
            // target slot to persist to (Copilot bot review).
            if (!_vm.IsLoaded)
            {
                CoreState.Services.ShowInfo(R._("Re-read the AI script before importing."));
                return;
            }
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

                // Full byte-stream import (WF AIScriptForm.FileToEvent parity):
                // parse each hex line, rebuild the in-memory opcode model, and
                // refresh the Disassembly list. NO ROM write — the user clicks
                // the Write button to persist (preserving the undo flow).
                // #1639: read via the stream API so Android content:// sources
                // (no local path) are read, not treated as cancelled.
                string text;
                await using (var stream = await files[0].OpenReadAsync())
                using (var reader = new System.IO.StreamReader(stream))
                {
                    text = await reader.ReadToEndAsync();
                }
                int count = _vm.ImportFromText(text);
                if (count <= 0)
                {
                    CoreState.Services.ShowError(R._("No valid AI opcodes were found in the selected file."));
                    return;
                }

                // Refresh from the in-memory model (NOT a ROM re-read) so the
                // imported opcodes are visible before any Write. Sync the
                // Address / byte-count boxes to the (unchanged) load location.
                DisassemblyList.ItemsSource = _vm.GetDisplayLines();
                UpdateUI();
                CoreState.Services.ShowInfo(
                    $"{R._("Imported AI opcodes:")} {count}. {R._("Press Write to save to ROM.")}");
            }
            catch (Exception ex)
            {
                Log.ErrorF("AIScriptView.Import_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
