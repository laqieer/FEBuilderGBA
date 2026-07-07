using System;
using global::Avalonia;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportAttributeView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SupportAttributeViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Support Attribute";
        public new bool IsLoaded => _vm.CanWrite;


        public EditorDescriptor Descriptor => new("Support Attribute Editor", 1169, 497, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public SupportAttributeView()
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
                var items = _vm.LoadSupportAttributeList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.AttributeIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("SupportAttributeView.LoadList failed: {0}", ex.Message);
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
                _vm.LoadSupportAttribute(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("SupportAttributeView.OnSelected failed: {0}", ex.Message);
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
            AffinityTypeBox.Value = _vm.AffinityType;
            AttackBonusBox.Value = _vm.AttackBonus;
            DefenseBonusBox.Value = _vm.DefenseBonus;
            HitBonusBox.Value = _vm.HitBonus;
            AvoidBonusBox.Value = _vm.AvoidBonus;
            CritBonusBox.Value = _vm.CritBonus;
            CritAvoidBonusBox.Value = _vm.CritAvoidBonus;
            Unknown7Box.Value = _vm.Unknown7;
        }

        void ReadUIToVM()
        {
            _vm.AffinityType = (uint)(AffinityTypeBox.Value ?? 0);
            _vm.AttackBonus = (uint)(AttackBonusBox.Value ?? 0);
            _vm.DefenseBonus = (uint)(DefenseBonusBox.Value ?? 0);
            _vm.HitBonus = (uint)(HitBonusBox.Value ?? 0);
            _vm.AvoidBonus = (uint)(AvoidBonusBox.Value ?? 0);
            _vm.CritBonus = (uint)(CritBonusBox.Value ?? 0);
            _vm.CritAvoidBonus = (uint)(CritAvoidBonusBox.Value ?? 0);
            _vm.Unknown7 = (uint)(Unknown7Box.Value ?? 0);
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            // #1149: decomp guard comes before the CanWrite check so that in
            // decomp mode the user sees the source-write feedback (or the
            // ROM-only notice) rather than a silent no-op.
            if (CoreState.IsDecompMode)
            {
                if (!_vm.CanWrite) return;
                ReadUIToVM();
                if (TryWriteSupportAttributeSource())
                    return;
                CoreState.Services?.ShowInfo(R._("This support attribute entry is ROM-only in decomp mode. Edit the source manually and rebuild."));
                return;
            }

            if (!_vm.CanWrite) return;

            ReadUIToVM();
            _undoService.Begin("Edit Support Attribute");
            try
            {
                _vm.WriteSupportAttribute();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Support attribute data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("SupportAttributeView.Write_Click failed: {0}", ex.Message);
            }
        }

        /// <summary>
        /// #1149: attempt a source-backed write for the support_attributes table.
        /// Returns true when the table has a source owner (write was attempted and message shown).
        /// Returns false only when no owner exists.
        /// </summary>
        bool TryWriteSupportAttributeSource()
        {
            var project = CoreState.DecompProject;
            var owner = project?.TryGetTableOwner("support_attributes");
            if (owner == null)
                return false;

            uint entryId = _vm.CurrentEntryId;
            if (entryId == U.NOT_FOUND)
            {
                CoreState.Services?.ShowError(R._("Could not resolve this support attribute entry id — source write skipped."));
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

            var res = DecompSourceWriterCore.WriteTableEntry(project, "support_attributes", (int)entryId, changed);
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
                        ? R._("Support attribute source updated. Project needs rebuild.")
                        : R._("No change needed — the source already matches."));
                    break;
                case DecompSourceWriteStatus.RomOnly:
                    CoreState.Services?.ShowInfo(R._("This support attribute entry is ROM-only in decomp mode."));
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
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
