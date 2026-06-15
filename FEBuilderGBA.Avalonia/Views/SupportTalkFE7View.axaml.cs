using System;
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
    public partial class SupportTalkFE7View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly SupportTalkFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Support Talk (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportTalkFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += Write_Click;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSupportTalkList();
                // Issue #361: show BOTH unit portraits per row. FE7 stores
                // partner 2 at addr+1 (partner 1 at addr+0).
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitPairFromAddrU8Loader(items, i, unit2Offset: 1));
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkFE7View.LoadList failed: {0}", ex.Message);
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
                Log.Error("SupportTalkFE7View.OnSelected failed: {0}", ex.Message);
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
            TextCNud.Value = _vm.TextC;
            TextBNud.Value = _vm.TextB;
            TextANud.Value = _vm.TextA;
            // GetUnitNameByOneBasedId / GetTextById return fallback strings on
            // failure rather than throwing, so no try/catch needed (Copilot
            // review #638). SupportPartner1/2 are 1-based unit IDs;
            // GetUnitNameByOneBasedId handles the 0/bounds cases and the
            // 1-based → 0-based conversion (#937).
            SupportPartner1Nud.NameText = NameResolver.GetUnitNameByOneBasedId(_vm.SupportPartner1);
            SupportPartner2Nud.NameText = NameResolver.GetUnitNameByOneBasedId(_vm.SupportPartner2);
            TextCNud.NameText = _vm.TextC != 0 ? NameResolver.GetTextById(_vm.TextC) : "";
            TextBNud.NameText = _vm.TextB != 0 ? NameResolver.GetTextById(_vm.TextB) : "";
            TextANud.NameText = _vm.TextA != 0 ? NameResolver.GetTextById(_vm.TextA) : "";
            SongCNud.Value = _vm.SongC;
            SongBNud.Value = _vm.SongB;
            SongANud.Value = _vm.SongA;
            PaddingNud.Value = _vm.Padding;
        }

        void ReadUIToVM()
        {
            _vm.SupportPartner1 = SupportPartner1Nud.Value;
            _vm.SupportPartner2 = SupportPartner2Nud.Value;
            _vm.TextC = TextCNud.Value;
            _vm.TextB = TextBNud.Value;
            _vm.TextA = TextANud.Value;
            _vm.SongC = (uint)(SongCNud.Value ?? 0);
            _vm.SongB = (uint)(SongBNud.Value ?? 0);
            _vm.SongA = (uint)(SongANud.Value ?? 0);
            _vm.Padding = (uint)(PaddingNud.Value ?? 0);
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

            _undoService.Begin("Edit Support Talk (FE7)");
            try
            {
                _vm.WriteSupportTalk();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SupportTalkFE7View.Write_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #1149: attempt a source-backed write for the support_talks table (FE7).
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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        /// <summary>
        /// #358 — select the FE7 support-talk row that pairs the two units
        /// in either order.  Mirrors WinForms <c>SupportTalkFE7Form.JumpTo</c>.
        /// </summary>
        public void JumpToUnitPair(uint uid1, uint uid2)
        {
            uint? addr = _vm.FindAddrForUnitPair(uid1, uid2);
            if (addr != null)
            {
                EntryList.SelectAddress(addr.Value);
            }
        }

        // -- IdFieldControl handlers (#360 final) ---------------------------

        static uint TextAddrFor(uint textId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint ptr = rom.RomInfo.text_pointer;
            if (ptr == 0) return 0;
            uint baseAddr = rom.p32(ptr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            // Compute in ulong to detect wrap-around; AXAML Maximum allows
            // int.MaxValue so textId * 4 in uint arithmetic could overflow
            // and still satisfy isSafetyOffset (Copilot review #638).
            ulong addr64 = (ulong)baseAddr + (ulong)textId * 4UL;
            if (addr64 > uint.MaxValue) return 0;
            uint addr = (uint)addr64;
            if (!U.isSafetyOffset(addr, rom)) return 0;
            // Validate the full 4-byte text-table entry range.
            if (!U.isSafetyOffset(addr + 3, rom)) return 0;
            return addr;
        }

        void SupportPartner1_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, SupportPartner1Nud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.SupportPartner1_Jump failed: {0}", ex.Message); }
        }

        async void SupportPartner1_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, SupportPartner1Nud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                // PickResult.Index is 0-based; SupportPartner is 1-based (#937).
                if (result != null) SupportPartner1Nud.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
            }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.SupportPartner1_Pick failed: {0}", ex.Message); }
        }

        void SupportPartner1_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // 1-based Unit ID → name via GetUnitNameByOneBasedId (#937).
            SupportPartner1Nud.NameText = NameResolver.GetUnitNameByOneBasedId(e.NewValue);
        }

        void SupportPartner2_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, SupportPartner2Nud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.SupportPartner2_Jump failed: {0}", ex.Message); }
        }

        async void SupportPartner2_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, SupportPartner2Nud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                // PickResult.Index is 0-based; SupportPartner is 1-based (#937).
                if (result != null) SupportPartner2Nud.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
            }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.SupportPartner2_Pick failed: {0}", ex.Message); }
        }

        void SupportPartner2_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // 1-based Unit ID → name via GetUnitNameByOneBasedId (#937).
            SupportPartner2Nud.NameText = NameResolver.GetUnitNameByOneBasedId(e.NewValue);
        }

        void TextC_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextCNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.TextC_Jump failed: {0}", ex.Message); }
        }

        void TextC_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            TextCNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        void TextB_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextBNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.TextB_Jump failed: {0}", ex.Message); }
        }

        void TextB_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            TextBNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        void TextA_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = TextAddrFor(TextANud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error("SupportTalkFE7View.TextA_Jump failed: {0}", ex.Message); }
        }

        void TextA_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            TextANud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
