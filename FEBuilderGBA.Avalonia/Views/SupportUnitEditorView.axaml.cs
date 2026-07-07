using System;
using global::Avalonia;
using System.Collections.Generic;
using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportUnitEditorView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SupportUnitEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Support Unit Editor";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Support Unit Editor", 1100, 900);
        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public SupportUnitEditorView()
        {
            InitializeComponent();
            SupportList.SelectedAddressChanged += OnSupportSelected;
            WriteButton.Click += Write_Click;
            // #358: source-unit jump + per-partner SupportTalk jumps.
            JumpToSourceUnitButton.Click += JumpToSourceUnit_Click;
            Talk1Button.Click += (s, e) => JumpToTalk((uint)(Partner1Nud.Value ?? 0));
            Talk2Button.Click += (s, e) => JumpToTalk((uint)(Partner2Nud.Value ?? 0));
            Talk3Button.Click += (s, e) => JumpToTalk((uint)(Partner3Nud.Value ?? 0));
            Talk4Button.Click += (s, e) => JumpToTalk((uint)(Partner4Nud.Value ?? 0));
            Talk5Button.Click += (s, e) => JumpToTalk((uint)(Partner5Nud.Value ?? 0));
            Talk6Button.Click += (s, e) => JumpToTalk((uint)(Partner6Nud.Value ?? 0));
            Talk7Button.Click += (s, e) => JumpToTalk((uint)(Partner7Nud.Value ?? 0));
            // Refresh partner name labels when the user edits a partner ID.
            Partner1Nud.ValueChanged += (s, e) => Partner1NameLabel.Text = ResolvePartnerName(Partner1Nud.Value);
            Partner2Nud.ValueChanged += (s, e) => Partner2NameLabel.Text = ResolvePartnerName(Partner2Nud.Value);
            Partner3Nud.ValueChanged += (s, e) => Partner3NameLabel.Text = ResolvePartnerName(Partner3Nud.Value);
            Partner4Nud.ValueChanged += (s, e) => Partner4NameLabel.Text = ResolvePartnerName(Partner4Nud.Value);
            Partner5Nud.ValueChanged += (s, e) => Partner5NameLabel.Text = ResolvePartnerName(Partner5Nud.Value);
            Partner6Nud.ValueChanged += (s, e) => Partner6NameLabel.Text = ResolvePartnerName(Partner6Nud.Value);
            Partner7Nud.ValueChanged += (s, e) => Partner7NameLabel.Text = ResolvePartnerName(Partner7Nud.Value);
            // #1455: reciprocal mirroring is a ROM-byte mutation of OTHER rows.
            // In decomp mode Write_Click is source-backed and never reaches
            // WriteSupportUnit(), so a default-on checkbox would silently lie —
            // disable it (and force it off) there. Reciprocal source-entry
            // mirroring for support_units is out of scope (separate decomp concern).
            if (CoreState.IsDecompMode)
            {
                AutoCollectCheckBox.IsChecked = false;
                AutoCollectCheckBox.IsEnabled = false;
                ToolTip.SetTip(AutoCollectCheckBox,
                    R._("Reciprocal mirroring applies to ROM saves only. In decomp mode, edit the source and rebuild."));
            }
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

        static string ResolvePartnerName(decimal? value)
        {
            if (value == null || value.Value == 0) return "";
            // Partner ID in the support data is 1-based (matching WinForms
            // UnitForm.GetUnitName).  ResolveUnitTableName takes 0-based
            // table index, so subtract 1.
            uint id = (uint)value.Value;
            if (id == 0) return "";
            try { return SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, id - 1); }
            catch { return ""; }
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSupportUnitList();
                SupportList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("SupportUnitEditorView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSupportSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSupportUnit(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SupportUnitEditorView.OnSupportSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        public void NavigateTo(uint address)
        {
            SupportList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            Partner1Nud.Value = _vm.Partner1;
            Partner2Nud.Value = _vm.Partner2;
            Partner3Nud.Value = _vm.Partner3;
            Partner4Nud.Value = _vm.Partner4;
            Partner5Nud.Value = _vm.Partner5;
            Partner6Nud.Value = _vm.Partner6;
            Partner7Nud.Value = _vm.Partner7;

            InitialValue1Nud.Value = _vm.InitialValue1;
            InitialValue2Nud.Value = _vm.InitialValue2;
            InitialValue3Nud.Value = _vm.InitialValue3;
            InitialValue4Nud.Value = _vm.InitialValue4;
            InitialValue5Nud.Value = _vm.InitialValue5;
            InitialValue6Nud.Value = _vm.InitialValue6;
            InitialValue7Nud.Value = _vm.InitialValue7;

            GrowthRate1Nud.Value = _vm.GrowthRate1;
            GrowthRate2Nud.Value = _vm.GrowthRate2;
            GrowthRate3Nud.Value = _vm.GrowthRate3;
            GrowthRate4Nud.Value = _vm.GrowthRate4;
            GrowthRate5Nud.Value = _vm.GrowthRate5;
            GrowthRate6Nud.Value = _vm.GrowthRate6;
            GrowthRate7Nud.Value = _vm.GrowthRate7;

            PartnerCountNud.Value = _vm.PartnerCount;
            Separator1Nud.Value = _vm.Separator1;
            Separator2Nud.Value = _vm.Separator2;

            // #358: source unit and partner name labels.
            if (_vm.SourceUnitId1Based == 0)
            {
                SourceUnitIdLabel.Text = "—";
                SourceUnitNameLabel.Text = "(no unit points at this row)";
                JumpToSourceUnitButton.IsEnabled = false;
            }
            else
            {
                SourceUnitIdLabel.Text = $"0x{_vm.SourceUnitId1Based:X02}";
                SourceUnitNameLabel.Text = _vm.SourceUnitName;
                JumpToSourceUnitButton.IsEnabled = true;
            }
            Partner1NameLabel.Text = ResolvePartnerName(Partner1Nud.Value);
            Partner2NameLabel.Text = ResolvePartnerName(Partner2Nud.Value);
            Partner3NameLabel.Text = ResolvePartnerName(Partner3Nud.Value);
            Partner4NameLabel.Text = ResolvePartnerName(Partner4Nud.Value);
            Partner5NameLabel.Text = ResolvePartnerName(Partner5Nud.Value);
            Partner6NameLabel.Text = ResolvePartnerName(Partner6Nud.Value);
            Partner7NameLabel.Text = ResolvePartnerName(Partner7Nud.Value);
        }

        // #358: Navigate to the version-correct Unit Editor for the source
        // unit that owns this support row.  WinForms equivalent: clicking
        // SupportUnitForm.X_SRC_UNIT_LABEL (markup-jump via UNIT linktype).
        void JumpToSourceUnit_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.SourceUnitId1Based == 0) return;
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint unitBase = SupportUnitNavigation.GetUnitTableBase(rom);
                if (unitBase == 0) return;
                uint dataSize = rom.RomInfo.unit_datasize;
                if (dataSize == 0) return;
                // SourceUnitId1Based is 1-based; the unit-table is 0-based,
                // and FE7/8 (this view) doesn't skip the first entry.
                uint addr = unitBase + (_vm.SourceUnitId1Based - 1) * dataSize;
                // FE7 has its own unit editor view; FE8 uses the shared editor.
                int ver = rom.RomInfo.version;
                if (ver == 7)
                    WindowManager.Instance.Navigate<UnitFE7View>(addr);
                else
                    WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("SupportUnitEditorView.JumpToSourceUnit failed: {0}", ex.Message);
            }
        }

        // #358: Navigate to the version-correct Support Talk editor and
        // select the conversation between the source unit and the given
        // partner.  WinForms equivalent: SupportUnitForm.GotoSupportTalk.
        void JumpToTalk(uint partnerUid)
        {
            try
            {
                if (partnerUid == 0) return;
                uint srcUid = _vm.SourceUnitId1Based;
                if (srcUid == 0) return;
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                int ver = rom.RomInfo.version;
                if (ver == 6)
                {
                    var window = WindowManager.Instance.Open<SupportTalkFE6View>();
                    window.JumpToUnitPair(srcUid, partnerUid);
                }
                else if (ver == 7)
                {
                    var window = WindowManager.Instance.Open<SupportTalkFE7View>();
                    window.JumpToUnitPair(srcUid, partnerUid);
                }
                else
                {
                    var window = WindowManager.Instance.Open<SupportTalkView>();
                    window.JumpToUnitPair(srcUid, partnerUid);
                }
            }
            catch (Exception ex)
            {
                Log.ErrorF("SupportUnitEditorView.JumpToTalk failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #358 — navigate to the support row whose ROM address (after
        /// normalization) equals <paramref name="supportPointerOrFileOffset"/>.
        /// Accepts both raw <c>0x08xxxxxx</c> pointers and file offsets.
        /// </summary>
        public void JumpToAddr(uint supportPointerOrFileOffset)
        {
            uint normalized = U.toOffset(supportPointerOrFileOffset);
            SupportList.SelectAddress(normalized);
        }

        void ReadUIToVM()
        {
            _vm.Partner1 = (uint)(Partner1Nud.Value ?? 0);
            _vm.Partner2 = (uint)(Partner2Nud.Value ?? 0);
            _vm.Partner3 = (uint)(Partner3Nud.Value ?? 0);
            _vm.Partner4 = (uint)(Partner4Nud.Value ?? 0);
            _vm.Partner5 = (uint)(Partner5Nud.Value ?? 0);
            _vm.Partner6 = (uint)(Partner6Nud.Value ?? 0);
            _vm.Partner7 = (uint)(Partner7Nud.Value ?? 0);

            _vm.InitialValue1 = (uint)(InitialValue1Nud.Value ?? 0);
            _vm.InitialValue2 = (uint)(InitialValue2Nud.Value ?? 0);
            _vm.InitialValue3 = (uint)(InitialValue3Nud.Value ?? 0);
            _vm.InitialValue4 = (uint)(InitialValue4Nud.Value ?? 0);
            _vm.InitialValue5 = (uint)(InitialValue5Nud.Value ?? 0);
            _vm.InitialValue6 = (uint)(InitialValue6Nud.Value ?? 0);
            _vm.InitialValue7 = (uint)(InitialValue7Nud.Value ?? 0);

            _vm.GrowthRate1 = (uint)(GrowthRate1Nud.Value ?? 0);
            _vm.GrowthRate2 = (uint)(GrowthRate2Nud.Value ?? 0);
            _vm.GrowthRate3 = (uint)(GrowthRate3Nud.Value ?? 0);
            _vm.GrowthRate4 = (uint)(GrowthRate4Nud.Value ?? 0);
            _vm.GrowthRate5 = (uint)(GrowthRate5Nud.Value ?? 0);
            _vm.GrowthRate6 = (uint)(GrowthRate6Nud.Value ?? 0);
            _vm.GrowthRate7 = (uint)(GrowthRate7Nud.Value ?? 0);

            _vm.PartnerCount = (uint)(PartnerCountNud.Value ?? 0);
            _vm.Separator1 = (uint)(Separator1Nud.Value ?? 0);
            _vm.Separator2 = (uint)(Separator2Nud.Value ?? 0);

            // #1455: reciprocal mirroring toggle (disabled in decomp mode).
            _vm.AutoCollect = AutoCollectCheckBox.IsEnabled && (AutoCollectCheckBox.IsChecked == true);
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            ReadUIToVM();

            // #1149: in decomp mode, support_units edits are source-backed.
            if (CoreState.IsDecompMode)
            {
                if (TryWriteSupportUnitSource())
                    return;
                CoreState.Services?.ShowInfo(R._("This support unit entry is ROM-only in decomp mode. Edit the source manually and rebuild."));
                return;
            }

            _undoService.Begin("Edit Support Unit");
            try
            {
                _vm.WriteSupportUnit();
                _undoService.Commit();
                _vm.MarkClean();
                // #1455: AutoCollect may have recomputed B21 (partner count) —
                // reflect it back into the UI so the field shows the saved value.
                PartnerCountNud.Value = _vm.PartnerCount;
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SupportUnitEditorView.Write_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #1149: attempt a source-backed write for the support_units table.
        /// Returns true when the table has a source owner (write was attempted and message shown).
        /// Returns false only when no owner exists.
        /// </summary>
        bool TryWriteSupportUnitSource()
        {
            var project = CoreState.DecompProject;
            var owner = project?.TryGetTableOwner("support_units");
            if (owner == null)
                return false;

            uint entryId = _vm.CurrentEntryId;
            if (entryId == U.NOT_FOUND)
            {
                CoreState.Services?.ShowError(R._("Could not resolve this support unit entry id — source write skipped."));
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

            var res = DecompSourceWriterCore.WriteTableEntry(project, "support_units", (int)entryId, changed);
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
                        ? R._("Support unit source updated. Project needs rebuild.")
                        : R._("No change needed — the source already matches."));
                    break;
                case DecompSourceWriteStatus.RomOnly:
                    CoreState.Services?.ShowInfo(R._("This support unit entry is ROM-only in decomp mode."));
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

        public void SelectFirstItem()
        {
            SupportList.SelectFirst();
        }
    }
}
