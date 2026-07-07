using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventBattleTalkFE7View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventBattleTalkFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Battle Dialogue (FE7)";
        public new bool IsLoaded => _vm.IsLoaded;


        public EditorDescriptor Descriptor => new("Battle Dialogue (FE7)", 1328, 1077, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);

        public event EventHandler? CloseRequested;
        public EventBattleTalkFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
            TableFilter.SelectionChanged += TableFilter_SelectionChanged;

            // Live name/text previews while editing.
            AttackerUnitBox.ValueChanged += (_, _) => AttackerNameLabel.Text = UnitName(AttackerUnitBox);
            DefenderUnitBox.ValueChanged += (_, _) => UpdateDefenderPreview();
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

        EventBattleTalkFE7ViewModel.BattleTalkTable SelectedTable =>
            TableFilter.SelectedIndex == 1
                ? EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary
                : EventBattleTalkFE7ViewModel.BattleTalkTable.Main;

        void TableFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadList(SelectedTable);
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitFromAddrU8Loader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleTalkFE7View.LoadList failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
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
                Log.Error("EventBattleTalkFE7View.OnSelected failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            bool secondary = _vm.IsSecondaryTable;

            // Route through R._() at assignment time — TranslatedUserControl.TranslateAll()
            // runs once at window open, so values assigned afterward must be
            // localized explicitly to apply in ja/zh (#958 review).
            TableLabel.Text = R._(secondary ? "Secondary (12-byte)" : "Main (16-byte)");
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);

            AttackerUnitBox.Value = _vm.AttackerUnit;
            DefenderUnitBox.Value = _vm.DefenderUnit;
            Unknown02Box.Value = _vm.Unknown02;
            Unknown03Box.Value = _vm.Unknown03;
            TextIdBox.Value = _vm.Text;
            Unknown06Box.Value = _vm.Unknown06;
            Unknown07Box.Value = _vm.Unknown07;
            EventPointerBox.Value = _vm.EventPointer;
            AchievementFlagBox.Value = _vm.AchievementFlag;
            Unknown0EBox.Value = _vm.Unknown0E;
            Unknown0FBox.Value = _vm.Unknown0F;

            // Offset 0x01 is the Defender Unit in the 16-byte MAIN schema but the
            // chapter/map id in the 12-byte SECONDARY schema (WinForms N1_J_1_MAP /
            // N1_L_1_MAP_ANYFF). Relabel the row + swap the inline preview so the
            // map id isn't shown with unit-name semantics. Route through R._() at
            // assignment time (TranslateAll() ran once at open).
            DefenderOrMapLabel.Text = R._(secondary ? "Chapter/Map ID:" : "Defender Unit:");

            AttackerNameLabel.Text = UnitName(AttackerUnitBox);
            UpdateDefenderPreview();
            TextPreviewLabel.Text = TextPreview(TextIdBox);

            // The secondary 12-byte schema carries no event pointer (flag lives at
            // 0x08, not 0x0C). Disable the input so the user can't edit a byte that
            // won't be written back (mirrors EventHaikuFE7View's per-schema gating).
            EventPointerBox.IsEnabled = !secondary;

            // The two trailing Unknown bytes sit at 0x0E/0x0F in the 16-byte MAIN
            // schema but at 0x0A/0x0B in the 12-byte SECONDARY schema — relabel them
            // per active table so the offsets aren't misleading. The 0x0A/0x0B/
            // 0x0E/0x0F label strings already have ja/zh entries (#958 review).
            UnknownTrailing0Label.Text = R._(secondary ? "Unknown (0x0A):" : "Unknown (0x0E):");
            UnknownTrailing1Label.Text = R._(secondary ? "Unknown (0x0B):" : "Unknown (0x0F):");
        }

        // Offset 0x01 preview: unit name (Main) vs FE7 ANYFF chapter/map name
        // (Secondary). Mirrors EventHaikuFE7View.ResolveChapterName for the map id.
        void UpdateDefenderPreview()
        {
            DefenderNameLabel.Text = _vm.IsSecondaryTable
                ? ResolveChapterName((uint)(DefenderUnitBox.Value ?? 0))
                : UnitName(DefenderUnitBox);
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

        // FE7 ANYFF semantics: 0x45 is the "ANY" map/chapter sentinel
        // (WinForms MapSettingForm.GetMapNameAndANYFF), matching
        // EventHaikuFE7View.ResolveChapterName.
        static string ResolveChapterName(uint chapterId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return "";
            if (chapterId == 0x45) return "ANY";
            try
            {
                string name = MapSettingCore.GetMapNameById(chapterId);
                return string.IsNullOrEmpty(name) ? "" : name;
            }
            catch { return ""; }
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit Battle Dialogue (FE7)"));
            try
            {
                _vm.AttackerUnit = (uint)(AttackerUnitBox.Value ?? 0);
                _vm.DefenderUnit = (uint)(DefenderUnitBox.Value ?? 0);
                _vm.Unknown02 = (uint)(Unknown02Box.Value ?? 0);
                _vm.Unknown03 = (uint)(Unknown03Box.Value ?? 0);
                _vm.Text = (uint)(TextIdBox.Value ?? 0);
                _vm.Unknown06 = (uint)(Unknown06Box.Value ?? 0);
                _vm.Unknown07 = (uint)(Unknown07Box.Value ?? 0);
                _vm.EventPointer = (uint)(EventPointerBox.Value ?? 0);
                _vm.AchievementFlag = (uint)(AchievementFlagBox.Value ?? 0);
                _vm.Unknown0E = (uint)(Unknown0EBox.Value ?? 0);
                _vm.Unknown0F = (uint)(Unknown0FBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventBattleTalkFE7View.OnWrite failed: " + ex.ToString());
            }
        }

        /// <summary>
        /// Select the row whose address matches <paramref name="address"/>.
        /// Resolves which physical table the address belongs to — the main
        /// 16-byte table or the secondary 12-byte table
        /// (<c>event_ballte_talk2_pointer</c>) — switches the Table filter combo
        /// to that table (reloading the list under the correct schema), then
        /// selects the row (#957 W1b). Previously out-of-list (secondary) hits
        /// were only logged because the VM assumed the 16-byte schema.
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

            Log.Notify("EventBattleTalkFE7View.NavigateTo: address 0x" + address.ToString("X8") + " not found in any battle-talk table.");
        }

        /// <summary>
        /// Returns the Table filter combo index (0=Main, 1=Secondary) whose
        /// data region contains <paramref name="address"/>, or -1 if none.
        /// Read-only: does not mutate the VM's current-table state.
        /// </summary>
        int ResolveTableIndexFor(uint address)
        {
            var rom = CoreState.ROM;
            if (rom == null) return -1;

            // Main: 16-byte stride, stop on u16==0 || u16==0xFFFF.
            uint mainBase = EventBattleTalkFE7ViewModel.ResolveBaseAddr(rom, EventBattleTalkFE7ViewModel.BattleTalkTable.Main);
            if (mainBase != 0 && address >= mainBase && (address - mainBase) % 16 == 0)
            {
                for (uint a = mainBase; a + 16 <= (uint)rom.Data.Length; a += 16)
                {
                    uint u = rom.u16(a);
                    if (u == 0 || u == 0xFFFF) break;
                    if (a == address) return 0;
                }
            }

            // Secondary: 12-byte stride, stop on u8==0 || u8==0xFF.
            uint secBase = EventBattleTalkFE7ViewModel.ResolveBaseAddr(rom, EventBattleTalkFE7ViewModel.BattleTalkTable.Secondary);
            if (secBase != 0 && address >= secBase && (address - secBase) % 12 == 0)
            {
                for (uint a = secBase; a + 12 <= (uint)rom.Data.Length; a += 12)
                {
                    uint u = rom.u8(a);
                    if (u == 0 || u == 0xFF) break;
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
