using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SummonsDemonKingViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SummonsDemonKingViewerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Demon King Summon";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Demon King Summon Editor", 1237, 700, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public SummonsDemonKingViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
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

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSummonsDemonKingList();
                EntryList.SetItems(items);
                // #1606: the list-expand affordance is FE8-only (FE6/FE7 leave the
                // table pointer + count address at 0). Gate the button accordingly.
                ExpandButton.IsEnabled = SummonsDemonKingExpandCore.IsEnabled(CoreState.ROM);
            }
            catch (Exception ex)
            {
                Log.Error($"SummonsDemonKingViewerView.LoadList failed: {ex}");
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSummonsDemonKing(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SummonsDemonKingViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            // UnitId is a 1-based ROM unit ID; GetUnitNameByOneBasedId handles
            // the 0/bounds cases and the 1-based → 0-based conversion (#937).
            try { UnitIdBox.NameText = NameResolver.GetUnitNameByOneBasedId(_vm.UnitId); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
            ClassIdBox.Value = _vm.ClassId;
            try { ClassIdBox.NameText = NameResolver.GetClassName(_vm.ClassId); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
            Unknown1Box.Value = _vm.Commander;
            B3Box.Value = _vm.LevelGrowth;
            W4Box.Value = _vm.Coordinates;
            B6Box.Value = _vm.Special;
            B7Box.Value = _vm.Padding7;
            P8Box.Text = $"0x{_vm.AIPointer:X08}";
            B12Box.Value = _vm.Item1;
            B13Box.Value = _vm.Item2;
            B14Box.Value = _vm.Item3;
            B15Box.Value = _vm.Item4;
            B16Box.Value = _vm.PrimaryAI;
            B17Box.Value = _vm.SecondaryAI;
            B18Box.Value = _vm.TargetRecoveryAI;
            B19Box.Value = _vm.RetreatAI;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Demon King Summon");
            try
            {
                _vm.UnitId = UnitIdBox.Value;
                _vm.ClassId = ClassIdBox.Value;
                _vm.Commander = (uint)(Unknown1Box.Value ?? 0);
                _vm.LevelGrowth = (uint)(B3Box.Value ?? 0);
                _vm.Coordinates = (uint)(W4Box.Value ?? 0);
                _vm.Special = (uint)(B6Box.Value ?? 0);
                _vm.Padding7 = (uint)(B7Box.Value ?? 0);
                _vm.AIPointer = ParseHexText(P8Box.Text);
                _vm.Item1 = (uint)(B12Box.Value ?? 0);
                _vm.Item2 = (uint)(B13Box.Value ?? 0);
                _vm.Item3 = (uint)(B14Box.Value ?? 0);
                _vm.Item4 = (uint)(B15Box.Value ?? 0);
                _vm.PrimaryAI = (uint)(B16Box.Value ?? 0);
                _vm.SecondaryAI = (uint)(B17Box.Value ?? 0);
                _vm.TargetRecoveryAI = (uint)(B18Box.Value ?? 0);
                _vm.RetreatAI = (uint)(B19Box.Value ?? 0);
                _vm.WriteSummonsDemonKing();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Demon king summon data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error($"SummonsDemonKingViewerView.Write: {ex}"); }
        }

        // #1606 — list-expand (WF SummonsDemonKingForm.AddressListExpandsEvent
        // parity). Grows the 20-byte summons_demon_king table AND writes the new
        // entry count to summons_demon_king_count_address, all under ONE undo
        // scope with the MapSettingCore #885 byte-identical fault restore (the
        // Core helper snapshots + restores on any failure, even across a ROM
        // resize). FE8-only.
        async void Expand_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (!SummonsDemonKingExpandCore.IsEnabled(rom))
                {
                    CoreState.Services?.ShowInfo(R._("Demon King Summon table is not available for this ROM."));
                    return;
                }

                // Pre-dialog guard (Copilot plan-review finding #2): never build the
                // NumberInputDialog with min/default > max. A corrupt count (>= 100)
                // or an already-maxed count (>= 99) short-circuits with a message and
                // ZERO mutation.
                uint current = SummonsDemonKingExpandCore.ReadCountByte(rom);
                if (current >= SummonsDemonKingExpandCore.CorruptCountThreshold)
                {
                    CoreState.Services?.ShowInfo(R._("The current Demon King Summon count byte (0x{0:X}) is corrupt — cannot expand.", current));
                    return;
                }
                if (current >= SummonsDemonKingExpandCore.MaxCountByte)
                {
                    CoreState.Services?.ShowInfo(R._("Already at the maximum Demon King Summon count ({0}).", SummonsDemonKingExpandCore.MaxCountByte));
                    return;
                }

                uint defaultCount = current + 1;
                uint? chosen = await NumberInputDialog.Show(
                    TopLevel.GetTopLevel(this) as Window,
                    R._("Enter the new Demon King Summon count (current: {0}, max: {1}).", current, SummonsDemonKingExpandCore.MaxCountByte),
                    R._("List Expansion"),
                    defaultCount,
                    defaultCount,
                    SummonsDemonKingExpandCore.MaxCountByte);
                if (chosen == null) return; // user cancelled

                // Preserve the selection by ORIGINAL INDEX, not by address: the
                // expand repoints the table base, so the selected row's address
                // changes and an address match would always fall back to row 0.
                // The row index is stable across an append-only grow. (Copilot
                // bot review thread.)
                int preserveIndex = EntryList.SelectedOriginalIndex;

                _undoService.Begin("Expand Demon King Summon");
                try
                {
                    var result = SummonsDemonKingExpandCore.Expand(
                        rom, chosen.Value, _undoService.GetActiveUndoData(), out string err);
                    if (!result.Success)
                    {
                        _undoService.Rollback();
                        CoreState.Services?.ShowError(string.IsNullOrEmpty(err)
                            ? R._("Demon King Summon table expansion failed.") : err);
                        return;
                    }
                    _undoService.Commit();
                    _vm.MarkClean();
                    LoadList();
                    if (preserveIndex >= 0)
                        EntryList.SelectByIndex(preserveIndex);
                    CoreState.Services?.ShowInfo(
                        R._("Expanded Demon King Summon list to {0} entries.", result.NewCountByte + 1));
                }
                catch (Exception inner)
                {
                    _undoService.Rollback();
                    // Log.Error joins string[] args verbatim (no composite-format
                    // {0} substitution), so use interpolation + ex.ToString() for
                    // actionable detail.
                    Log.Error($"SummonsDemonKingViewerView.Expand inner failed: {inner}");
                    CoreState.Services?.ShowError(R._("Demon King Summon table expansion failed: {0}", inner.Message));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SummonsDemonKingViewerView.Expand failed: {ex}");
            }
        }

        // -- IdFieldControl handlers (#360) ----------------------------------

        static uint ClassAddrFor(uint classId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint classPtr = rom.RomInfo.class_pointer;
            if (classPtr == 0) return 0;
            uint baseAddr = rom.p32(classPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.class_datasize;
            if (dataSize == 0) return 0;
            uint entryAddr = baseAddr + classId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void UnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                // UnitId is 1-based; UnitAddrForOneBased applies the (id-1)
                // index + FE6 dummy-entry skip so Jump lands on the right unit (#937).
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("SummonsDemonKingViewerView.UnitId_Jump failed: {0}", ex.Message); }
        }

        async void UnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                // PickResult.Index is 0-based; UnitId is 1-based (#937).
                if (result != null) UnitIdBox.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
            }
            catch (Exception ex) { Log.ErrorF("SummonsDemonKingViewerView.UnitId_Pick failed: {0}", ex.Message); }
        }

        void UnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // 1-based Unit ID → name via GetUnitNameByOneBasedId (#937).
            try { UnitIdBox.NameText = NameResolver.GetUnitNameByOneBasedId(e.NewValue); }
            catch { /* GetUnitNameByOneBasedId returns a fallback and never throws; defensive only */ }
        }

        void ClassId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                if (addr == 0) return;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    WindowManager.Instance.Navigate<ClassFE6View>(addr);
                else
                    WindowManager.Instance.Navigate<ClassEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("SummonsDemonKingViewerView.ClassId_Jump failed: {0}", ex.Message); }
        }

        async void ClassId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = ClassAddrFor(ClassIdBox.Value);
                PickResult? result;
                if (CoreState.ROM?.RomInfo?.version == 6)
                    result = await WindowManager.Instance.PickFromEditor<ClassFE6View>(addr, TopLevel.GetTopLevel(this) as Window);
                else
                    result = await WindowManager.Instance.PickFromEditor<ClassEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) ClassIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.ErrorF("SummonsDemonKingViewerView.ClassId_Pick failed: {0}", ex.Message); }
        }

        void ClassId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { ClassIdBox.NameText = NameResolver.GetClassName(e.NewValue); }
            catch { /* NameResolver may fail without ROM — leave prior text */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
