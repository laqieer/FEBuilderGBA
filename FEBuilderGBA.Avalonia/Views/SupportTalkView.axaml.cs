using System;
using global::Avalonia;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportTalkView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SupportTalkViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Support Talk";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Support Talk", 1279, 720, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public SupportTalkView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += Write_Click;
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
                var items = _vm.LoadSupportTalkList();
                // Issue #361: show BOTH unit portraits per row. FE8 stores
                // partner 2 at addr+2 (partner 1 at addr+0).
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitPairFromAddrU8Loader(items, i, unit2Offset: 2));
            }
            catch (Exception ex)
            {
                Log.ErrorF("SupportTalkView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSupportTalk(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SupportTalkView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SupportPartner1Nud.Value = _vm.SupportPartner1;
            SupportPartner2Nud.Value = _vm.SupportPartner2;
            TextIdCNud.Value = _vm.TextIdC;
            TextIdBNud.Value = _vm.TextIdB;
            TextIdANud.Value = _vm.TextIdA;
            // Refresh name previews on selection change. ResolveUnitTableName /
            // GetTextById both return fallback strings on failure rather than
            // throwing, so no try/catch is needed (Copilot review #638).
            // SupportPartner1/2 are 1-based unit IDs (WinForms convention);
            // ResolveUnitTableName takes a 0-based table index, so subtract 1.
            // Pre-#725 we passed the 1-based ID directly, which offset the
            // displayed name (and the Jump target) by one row.
            SupportPartner1Nud.NameText = _vm.SupportPartner1 == 0 ? "" : SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, _vm.SupportPartner1 - 1);
            SupportPartner2Nud.NameText = _vm.SupportPartner2 == 0 ? "" : SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, _vm.SupportPartner2 - 1);
            TextIdCNud.NameText = _vm.TextIdC != 0 ? NameResolver.GetTextById(_vm.TextIdC) : "";
            TextIdBNud.NameText = _vm.TextIdB != 0 ? NameResolver.GetTextById(_vm.TextIdB) : "";
            TextIdANud.NameText = _vm.TextIdA != 0 ? NameResolver.GetTextById(_vm.TextIdA) : "";
            SongCNud.Value = _vm.SongC;
            SongBNud.Value = _vm.SongB;
            SongANud.Value = _vm.SongA;
        }

        void ReadUIToVM()
        {
            _vm.SupportPartner1 = SupportPartner1Nud.Value;
            _vm.SupportPartner2 = SupportPartner2Nud.Value;
            _vm.TextIdC = TextIdCNud.Value;
            _vm.TextIdB = TextIdBNud.Value;
            _vm.TextIdA = TextIdANud.Value;
            _vm.SongC = (uint)(SongCNud.Value ?? 0);
            _vm.SongB = (uint)(SongBNud.Value ?? 0);
            _vm.SongA = (uint)(SongANud.Value ?? 0);
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadUIToVM();

            // #1149: in decomp mode, support_talks edits are source-backed.
            if (CoreState.IsDecompMode)
            {
                if (TryWriteSupportTalkSource())
                    return;
                CoreState.Services?.ShowInfo(R._("This support talk entry is ROM-only in decomp mode. Edit the source manually and rebuild."));
                return;
            }

            _undoService.Begin("Edit Support Talk");
            try
            {
                _vm.WriteSupportTalk();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SupportTalkView.Write_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #1149: attempt a source-backed write for the support_talks table (FE8).
        /// Returns true when the table has a source owner. Returns false when no owner exists.
        /// </summary>
        bool TryWriteSupportTalkSource()
        {
            var project = CoreState.DecompProject;
            var owner = project?.TryGetTableOwner("support_talks");
            if (owner == null)
                return false;

            uint entryId = _vm.CurrentEntryId;
            if (entryId == U.NOT_FOUND)
            {
                CoreState.Services?.ShowError(R._("Could not resolve this support talk entry id — source write skipped."));
                return true;
            }

            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (owner.Fields != null)
                foreach (var f in owner.Fields)
                    if (f != null && !string.IsNullOrEmpty(f.Name))
                        declared.Add(f.Name);

            var changed = _vm.BuildSourceFieldDict();

            // ALL-OR-NOTHING (Copilot CLI review, HIGH severity): if the user changed ANY
            // field the manifest owner's fields[] does not declare, block the WHOLE save —
            // never write only the declared subset and then MarkClean (which would silently
            // drop the undeclared edit and mark it saved).
            foreach (var kv in changed)
            {
                if (!declared.Contains(kv.Key))
                {
                    CoreState.Services?.ShowInfo(R._("This support edit targets a field the manifest's fields[] does not declare — edit the source manually and rebuild."));
                    return true;
                }
            }

            var res = DecompSourceWriterCore.WriteTableEntry(project, "support_talks", (int)entryId, changed);
            switch (res.Status)
            {
                case DecompSourceWriteStatus.Ok:
                    // ALL-OR-NOTHING (Copilot CLI re-review): if the writer SKIPPED any
                    // requested field (its source token is a macro/expression the writer
                    // cannot rewrite), the save is PARTIAL — do NOT mark clean / refresh the
                    // snapshot (which would silently treat the skipped edit as saved). Leave
                    // the VM dirty and tell the user to edit those fields manually.
                    if (res.SkippedFields != null && res.SkippedFields.Count > 0)
                    {
                        CoreState.Services?.ShowInfo(R._("Some edited support fields map to a macro/expression and were skipped (edit those manually and rebuild). Any other fields were written to source — this entry was NOT fully saved. Skipped:") + " " + string.Join(", ", res.SkippedFields));
                        break;   // do NOT MarkClean / RefreshSourceFieldSnapshot
                    }
                    _vm.MarkClean();
                    _vm.RefreshSourceFieldSnapshot();
                    CoreState.Services?.ShowInfo(res.ChangedFields != null && res.ChangedFields.Count > 0
                        ? R._("Support talk source updated. Project needs rebuild.")
                        : R._("No change needed — the source already matches."));
                    break;
                case DecompSourceWriteStatus.RomOnly:
                    CoreState.Services?.ShowInfo(R._("This support talk entry is ROM-only in decomp mode."));
                    break;
                case DecompSourceWriteStatus.Manual:
                    CoreState.Services?.ShowInfo(res.Message);
                    break;
                default:
                    CoreState.Services?.ShowError(res.Message);
                    break;
            }
            return true;
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        /// <summary>
        /// #358 — select the support-talk row that pairs <paramref name="uid1"/>
        /// and <paramref name="uid2"/> (in either order).  No-op when no row
        /// matches.  Mirrors WinForms <c>SupportTalkForm.JumpTo(unit1, unit2)</c>.
        /// </summary>
        public void JumpToUnitPair(uint uid1, uint uid2)
        {
            uint? addr = _vm.FindAddrForUnitPair(uid1, uid2);
            if (addr != null)
            {
                EntryList.SelectAddress(addr.Value);
            }
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }

        // -- IdFieldControl handlers (#360 final) ---------------------------

        static uint UnitAddrFor(uint unitId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            if (unitId == 0) return 0; // 1-based: 0 is "no unit"
            uint unitPtr = rom.RomInfo.unit_pointer;
            if (unitPtr == 0) return 0;
            uint baseAddr = rom.p32(unitPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return 0;
            if (rom.RomInfo.version == 6) baseAddr += dataSize;
            // unitId is 1-based (WinForms convention — see UnitForm.SetSimUnit:
            // IDToAddr(uid - 1)). The pre-#725 code passed unitId directly,
            // which jumped to the next character (e.g. ID 1 → 2nd character).
            uint entryAddr = baseAddr + (unitId - 1) * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        static uint TextAddrFor(uint textId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return 0;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            // Compute in ulong to detect wrap-around; AXAML Maximum allows
            // 65535 here but the helper is defensively bounds-safe for any
            // 32-bit value to align with Copilot review #638 (SoundRoom /
            // SupportTalkFE7).
            ulong addr64 = (ulong)baseAddr + (ulong)textId * 4UL;
            if (addr64 > uint.MaxValue) return 0;
            uint addr = (uint)addr64;
            if (!U.isSafetyOffset(addr, rom)) return 0;
            if (!U.isSafetyOffset(addr + 3, rom)) return 0;
            return addr;
        }

        void SupportPartner1_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = UnitAddrFor(SupportPartner1Nud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.ErrorF("SupportTalkView.SupportPartner1_Jump failed: {0}", ex.Message); }
        }

        async void SupportPartner1_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(SupportPartner1Nud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                // PickResult.Index is 0-based; SupportPartner is 1-based (#725).
                if (result != null) SupportPartner1Nud.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("SupportTalkView.SupportPartner1_Pick failed: {0}", ex.Message); }
        }

        void SupportPartner1_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // 1-based Unit ID → 0-based table index (#725).
            SupportPartner1Nud.NameText = e.NewValue == 0 ? "" : SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, e.NewValue - 1);
        }

        void SupportPartner2_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = UnitAddrFor(SupportPartner2Nud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.ErrorF("SupportTalkView.SupportPartner2_Jump failed: {0}", ex.Message); }
        }

        async void SupportPartner2_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(SupportPartner2Nud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                // PickResult.Index is 0-based; SupportPartner is 1-based (#725).
                if (result != null) SupportPartner2Nud.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.ErrorF("SupportTalkView.SupportPartner2_Pick failed: {0}", ex.Message); }
        }

        void SupportPartner2_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // 1-based Unit ID → 0-based table index (#725).
            SupportPartner2Nud.NameText = e.NewValue == 0 ? "" : SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, e.NewValue - 1);
        }

        void TextIdC_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextIdCNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.ErrorF("SupportTalkView.TextIdC_Jump failed: {0}", ex.Message); }
        }

        void TextIdC_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            TextIdCNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        void TextIdB_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextIdBNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.ErrorF("SupportTalkView.TextIdB_Jump failed: {0}", ex.Message); }
        }

        void TextIdB_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            TextIdBNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        void TextIdA_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextIdANud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.ErrorF("SupportTalkView.TextIdA_Jump failed: {0}", ex.Message); }
        }

        void TextIdA_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            TextIdANud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
