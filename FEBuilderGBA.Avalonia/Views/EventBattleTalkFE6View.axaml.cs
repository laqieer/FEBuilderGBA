using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventBattleTalkFE6View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventBattleTalkFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Battle Dialogue (FE6)";
        public new bool IsLoaded => _vm.IsLoaded;


        public EditorDescriptor Descriptor => new("Battle Dialogue (FE6)", 1585, 940, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);

        public event EventHandler? CloseRequested;
        public EventBattleTalkFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            TableFilter.SelectionChanged += TableFilter_SelectionChanged;
            WriteButton.Click += OnWrite;

            // Live name/text previews while editing. The second field is a unit
            // only on the main table (it is a chapter id on the secondary table),
            // so its name preview is suppressed in secondary mode.
            AttackerUnitBox.ValueChanged += (_, _) => AttackerNameLabel.Text = UnitName(AttackerUnitBox);
            DefenderUnitBox.ValueChanged += (_, _) => DefenderNameLabel.Text = _vm.IsSecondaryTable ? "" : UnitName(DefenderUnitBox);
            TextIdBox.ValueChanged += (_, _) => TextPreviewLabel.Text = TextPreview(TextIdBox);        }


        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)

        {

            base.OnAttachedToVisualTree(e);

            if (!_hasLoadedList)

            {

                _hasLoadedList = true;

                LoadList();

            }

        }

        EventBattleTalkFE6ViewModel.BattleTalkTable SelectedTable =>
            TableFilter.SelectedIndex == 1
                ? EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary
                : EventBattleTalkFE6ViewModel.BattleTalkTable.Main;

        void TableFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                // Clear any loaded row from the previously-selected table BEFORE
                // repopulating, so an empty/missing target table can't leave a
                // writable stale selection behind (the Write button is gated on
                // _vm.IsLoaded). The subsequent row selection re-loads a real row.
                _vm.ClearEntry();
                var items = _vm.LoadList(SelectedTable);
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitFromAddrU8Loader(items, i));
                ApplyTableVisibility();
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleTalkFE6View.LoadList failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        // Adjust the editor chrome for the active table:
        // - The event-pointer field (offset +0x0C) exists only in the secondary
        //   16-byte boss-conversation table; hide it on the main table.
        // - In the secondary table WinForms labels B1 as 章ID (chapter/map id),
        //   not a defender unit (EventBattleTalkFE6Form.Designer N_J_1_MAP), so
        //   relabel the second field and hide its unit-name preview there.
        void ApplyTableVisibility()
        {
            bool secondary = _vm.IsSecondaryTable;
            EventPointerLabel.IsVisible = secondary;
            EventPointerBox.IsVisible = secondary;

            DefenderLabel.Text = R._(secondary ? "Chapter/Map ID:" : "Defender Unit:");
            // The second field is a unit only on the main table; on the secondary
            // table it is a chapter id, so suppress the misleading unit-name preview.
            DefenderNameLabel.IsVisible = !secondary;
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleTalkFE6View.OnSelected failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            // Route through R._() at assignment time — TranslatedUserControl.TranslateAll()
            // runs once at window open, so values assigned afterward must be
            // localized explicitly to apply in ja/zh.
            TableLabel.Text = R._(_vm.IsSecondaryTable ? "Boss conversation (16-byte)" : "Main (12-byte)");
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AttackerUnitBox.Value = _vm.AttackerUnit;
            DefenderUnitBox.Value = _vm.DefenderUnit;
            Unknown02Box.Value = _vm.Unknown02;
            Unknown03Box.Value = _vm.Unknown03;
            TextIdBox.Value = _vm.Text;
            Unknown06Box.Value = _vm.Unknown06;
            Unknown07Box.Value = _vm.Unknown07;
            AchievementFlagBox.Value = _vm.AchievementFlag;
            Unknown0ABox.Value = _vm.Unknown0A;
            Unknown0BBox.Value = _vm.Unknown0B;
            EventPointerBox.Value = _vm.EventPointer;

            AttackerNameLabel.Text = UnitName(AttackerUnitBox);
            // On the secondary table the second field is a chapter id (not a unit),
            // so don't render a unit-name preview for it.
            DefenderNameLabel.Text = _vm.IsSecondaryTable ? "" : UnitName(DefenderUnitBox);
            TextPreviewLabel.Text = TextPreview(TextIdBox);
        }

        static string UnitName(NumericUpDown box)
        {
            try { return NameResolver.GetUnitNameByOneBasedId((uint)(box.Value ?? 0)); }
            catch { return ""; }
        }

        static string TextPreview(NumericUpDown box)
        {
            uint id = (uint)(box.Value ?? 0);
            if (id == 0) return "";
            try { return NameResolver.GetTextById(id); }
            catch { return ""; }
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit Battle Dialogue (FE6)"));
            try
            {
                _vm.AttackerUnit = (uint)(AttackerUnitBox.Value ?? 0);
                _vm.DefenderUnit = (uint)(DefenderUnitBox.Value ?? 0);
                _vm.Unknown02 = (uint)(Unknown02Box.Value ?? 0);
                _vm.Unknown03 = (uint)(Unknown03Box.Value ?? 0);
                _vm.Text = (uint)(TextIdBox.Value ?? 0);
                _vm.Unknown06 = (uint)(Unknown06Box.Value ?? 0);
                _vm.Unknown07 = (uint)(Unknown07Box.Value ?? 0);
                _vm.AchievementFlag = (uint)(AchievementFlagBox.Value ?? 0);
                _vm.Unknown0A = (uint)(Unknown0ABox.Value ?? 0);
                _vm.Unknown0B = (uint)(Unknown0BBox.Value ?? 0);
                // Only the secondary 16-byte table carries the event pointer;
                // the VM ignores EventPointer for main-table writes.
                _vm.EventPointer = (uint)(EventPointerBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventBattleTalkFE6View.OnWrite failed: " + ex.ToString());
            }
        }

        /// <summary>
        /// Select the row whose address matches <paramref name="address"/>.
        /// Resolves which physical table the address belongs to — the main
        /// 12-byte table or the secondary 16-byte boss-conversation table
        /// (<c>event_ballte_talk2_pointer</c>) — switches the Table filter combo
        /// to that table (reloading the list under the correct schema), then
        /// selects the row.
        /// </summary>
        public void NavigateTo(uint address)
        {
            if (address == 0) return;

            // First try the currently-loaded table.
            EntryList.SelectAddress(address);
            if (_vm.CurrentAddr == address) return;

            int targetIndex = ResolveTableIndexFor(address);
            if (targetIndex >= 0 && targetIndex != TableFilter.SelectedIndex)
            {
                // Setting SelectedIndex fires TableFilter_SelectionChanged -> LoadList().
                TableFilter.SelectedIndex = targetIndex;
                EntryList.SelectAddress(address);
                if (_vm.CurrentAddr == address) return;
            }

            Log.Notify("EventBattleTalkFE6View.NavigateTo: address 0x" + address.ToString("X8") + " not found in any battle-talk table.");
        }

        /// <summary>
        /// Returns the Table filter combo index (0=Main, 1=Secondary) whose
        /// data region contains <paramref name="address"/>, or -1 if none.
        /// Read-only: does not mutate the VM's current-table state.
        /// FE6 main is 12-byte stride (stop on u16==0||0xFFFF); the secondary
        /// boss-conversation table is 16-byte stride (also u16 termination —
        /// NOT FE7's u8 secondary terminator).
        /// </summary>
        int ResolveTableIndexFor(uint address)
        {
            var rom = CoreState.ROM;
            if (rom == null) return -1;

            // Main: 12-byte stride, stop on u16==0 || u16==0xFFFF.
            uint mainBase = EventBattleTalkFE6ViewModel.ResolveBaseAddr(rom, EventBattleTalkFE6ViewModel.BattleTalkTable.Main);
            if (mainBase != 0 && address >= mainBase && (address - mainBase) % 12 == 0)
            {
                for (uint a = mainBase; a + 12 <= (uint)rom.Data.Length; a += 12)
                {
                    uint u = rom.u16(a);
                    if (u == 0 || u == 0xFFFF) break;
                    if (a == address) return 0;
                }
            }

            // Secondary: 16-byte stride, stop on u16==0 || u16==0xFFFF.
            uint secBase = EventBattleTalkFE6ViewModel.ResolveBaseAddr(rom, EventBattleTalkFE6ViewModel.BattleTalkTable.Secondary);
            if (secBase != 0 && address >= secBase && (address - secBase) % 16 == 0)
            {
                for (uint a = secBase; a + 16 <= (uint)rom.Data.Length; a += 16)
                {
                    uint u = rom.u16(a);
                    if (u == 0 || u == 0xFFFF) break;
                    if (a == address) return 1;
                }
            }
            return -1;
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
