// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Templates;
using global::Avalonia.Data;
using global::Avalonia.Interactivity;
using global::Avalonia.Markup.Xaml;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Reusable editable sub-list editor for the FE8N Ver2/Ver3 per-skill
    /// null-terminated 1-byte-ID arrays (issue #930, #769 bucket 2 slice 2).
    /// Embedded once per tab (Unit/Class/Item/Item2[/Composite]); the host view
    /// injects a shared <see cref="UndoService"/> and calls <see cref="Load"/>
    /// with the per-skill pointer SLOT + a name resolver + a CanEdit flag.
    ///
    /// Every mutation routes through the VM's single
    /// <c>WriteByteList(slot, ids, undo)</c> path (fork-on-write, slot-repoint).
    /// The handlers mirror <see cref="MapExitPointView"/>: wrap each op in
    /// <c>UndoService.Begin/Commit/Rollback</c>, re-<see cref="Load"/> afterward,
    /// and raise <see cref="Changed"/> so the host can re-sync its cached Px
    /// offsets (C1 — the host's main-row Write would otherwise revert the
    /// repoint and orphan the new array).
    /// </summary>
    public partial class SkillSubListEditorView : UserControl
    {
        readonly SkillSubListEditorViewModel _vm = new();

        // Captured so the post-op re-Load uses the same slot + resolver + flag.
        uint _slot;
        Func<uint, string>? _resolver;
        bool _canEdit;

        /// <summary>
        /// The shared host-owned undo service. The host injects its own
        /// <c>UndoService</c> so a sub-list op composes into the same undo
        /// buffer as the rest of the editor.
        /// </summary>
        public UndoService? UndoService { get; set; }

        /// <summary>
        /// Raised after a successful sub-list mutation so the host can re-sync
        /// (re-run its <c>LoadEntry</c> to re-read the now-repointed Px offsets)
        /// and reload its embedded editors.
        /// </summary>
        public event Action? Changed;

        /// <summary>The backing ViewModel (exposed for tests + host wiring).</summary>
        public SkillSubListEditorViewModel ViewModel => _vm;

        public SkillSubListEditorView()
        {
            InitializeComponent();

            // Bind the ListBox to the VM entries (Display string) and two-way
            // the selection index.
            EntryList.ItemsSource = _vm.Entries;
            EntryList.ItemTemplate = new FuncDataTemplate<SubListEntryVM>(
                (entry, _) => new TextBlock { Text = entry?.Display ?? "" },
                supportsRecycling: true);
            EntryList.SelectionChanged += (_, _) => _vm.SelectedIndex = EntryList.SelectedIndex;

            EditIdBox.ValueChanged += (_, e) =>
                _vm.EditId = e.NewValue.HasValue ? (uint)e.NewValue.Value : 0u;
        }

        void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        /// <summary>
        /// Set the tab title (e.g. "Unit Skill Sub-list").
        /// </summary>
        public void SetTitle(string title)
        {
            TitleLabel.Content = title;
        }

        /// <summary>
        /// Load the editor for a per-skill pointer slot. <paramref name="canEdit"/>
        /// gates all mutation (the Ver2 Item2 tab passes <c>HasItem2</c>).
        /// </summary>
        public void Load(uint pointerSlotAddr, Func<uint, string> nameResolver, bool canEdit)
        {
            _slot = pointerSlotAddr;
            _resolver = nameResolver;
            _canEdit = canEdit;
            _vm.Load(pointerSlotAddr, nameResolver, canEdit);
            RefreshLabels();
        }

        void RefreshLabels()
        {
            BaseAddrLabel.Content = _vm.BaseDisplay;
            CountLabel.Content = _vm.CountDisplay;
        }

        // -----------------------------------------------------------------
        // Op handlers — every ROM write wrapped in the host UndoService.
        // -----------------------------------------------------------------

        void Add_Click(object? sender, RoutedEventArgs e) => RunOp("Add Sub-list Entry", undo => _vm.AddEntry(undo));

        void Remove_Click(object? sender, RoutedEventArgs e) => RunOp("Remove Sub-list Entry", undo => _vm.RemoveSelected(undo));

        void SetId_Click(object? sender, RoutedEventArgs e) => RunOp("Set Sub-list Entry ID", undo => _vm.SetSelectedId(undo));

        void RunOp(string label, Action<Undo.UndoData> op)
        {
            if (!_vm.CanEdit) return;
            UndoService? undoService = UndoService;
            if (undoService == null) return;
            if (_resolver == null) return;

            undoService.Begin(label);
            try
            {
                var undoData = undoService.GetActiveUndoData();
                if (undoData == null) { undoService.Rollback(); return; }
                op(undoData);
                undoService.Commit();

                // Re-deref the slot after the repoint, then notify the host.
                _vm.Load(_slot, _resolver, _canEdit);
                RefreshLabels();
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                undoService.Rollback();
                Log.Error("SkillSubListEditorView.{0} failed: {1}", label, ex.Message);
                CoreState.Services?.ShowError(ex.Message);
            }
        }
    }
}
