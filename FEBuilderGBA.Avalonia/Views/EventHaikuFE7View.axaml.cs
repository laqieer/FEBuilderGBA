using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventHaikuFE7View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventHaikuFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Haiku (FE7)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Haiku (FE7)", 1264, 1055, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public EventHaikuFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += Write_Click;
            TableFilter.SelectionChanged += TableFilter_SelectionChanged;
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

        // The FE7 N1_ tutorial death-quote tables
        // (event_haiku_tutorial_1_pointer / event_haiku_tutorial_2_pointer) use a
        // DIFFERENT 12-byte schema. They are now browsable + editable via the
        // Table filter combo, which switches the VM to the 12-byte schema
        // (#957 W1b). The shared detail panel re-labels itself per table.

        EventHaikuFE7ViewModel.HaikuTable SelectedTable => TableFilter.SelectedIndex switch
        {
            1 => EventHaikuFE7ViewModel.HaikuTable.Tutorial1,
            2 => EventHaikuFE7ViewModel.HaikuTable.Tutorial2,
            _ => EventHaikuFE7ViewModel.HaikuTable.Main,
        };

        void TableFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList(SelectedTable);
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error($"EventHaikuFE7View.LoadList failed: {ex.Message}");
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error($"EventHaikuFE7View.OnSelected failed: {ex.Message}");
            }
        }

        void UpdateUI()
        {
            bool tutorial = _vm.IsTutorialTable;
            // Route through R._() at assignment time — TranslatedUserControl.TranslateAll()
            // runs once at window open, so values assigned afterward must be
            // localized explicitly to apply in ja/zh (#958 review).
            TableLabel.Text = R._(_vm.Table switch
            {
                EventHaikuFE7ViewModel.HaikuTable.Tutorial1 => "Tutorial 1 - Lyn (12-byte)",
                EventHaikuFE7ViewModel.HaikuTable.Tutorial2 => "Tutorial 2 - Eliwood (12-byte)",
                _ => "Main (16-byte)",
            });

            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            UnitNud.Value = _vm.Unit;
            UnitNud.NameText = _vm.Unit == 0 ? "ANY" : NameResolver.GetUnitNameAndANYByOneBasedId(_vm.Unit);

            ChapterIdBox.Value = _vm.ChapterID;
            ChapterNameLabel.Text = ResolveChapterName(_vm.ChapterID);

            Unknown02Box.Value = _vm.Unknown02;
            Unknown03Box.Value = _vm.Unknown03;

            TextNud.Value = _vm.Text;
            TextNud.NameText = _vm.Text != 0 ? NameResolver.GetTextById(_vm.Text) : "";

            Unknown06Box.Value = _vm.Unknown06;
            Unknown07Box.Value = _vm.Unknown07;
            AchievementFlagBox.Value = _vm.AchievementFlag;
            Unknown0EBox.Value = _vm.Unknown0E;
            Unknown0FBox.Value = _vm.Unknown0F;

            EventPointerBox.Value = _vm.EventPointer;

            // The 12-byte tutorial schema has no Text / 0x06 / 0x07 fields
            // (event pointer lives at offset 0x04, flag at 0x08). Disable the
            // controls that don't exist in that schema so the user can't edit
            // bytes that won't be written back.
            TextNud.IsEnabled = !tutorial;
            Unknown06Box.IsEnabled = !tutorial;
            Unknown07Box.IsEnabled = !tutorial;

            // The two trailing Unknown bytes sit at 0x0E/0x0F in the 16-byte
            // MAIN schema but at 0x0A/0x0B in the 12-byte tutorial schema —
            // relabel them per active table so the offsets aren't misleading.
            // Route through R._() at assignment time (TranslateAll() ran once at
            // open). The 0x0A/0x0B/0x0E/0x0F label strings already have ja/zh
            // entries (#958 review).
            UnknownTrailing0Label.Text = R._(tutorial ? "Unknown (0x0A):" : "Unknown (0x0E):");
            UnknownTrailing1Label.Text = R._(tutorial ? "Unknown (0x0B):" : "Unknown (0x0F):");
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Haiku (FE7)");
            try
            {
                _vm.Unit = UnitNud.Value;
                _vm.ChapterID = (uint)(ChapterIdBox.Value ?? 0);
                _vm.Unknown02 = (uint)(Unknown02Box.Value ?? 0);
                _vm.Unknown03 = (uint)(Unknown03Box.Value ?? 0);
                _vm.Text = TextNud.Value;
                _vm.Unknown06 = (uint)(Unknown06Box.Value ?? 0);
                _vm.Unknown07 = (uint)(Unknown07Box.Value ?? 0);
                _vm.EventPointer = (uint)(EventPointerBox.Value ?? 0);
                _vm.AchievementFlag = (uint)(AchievementFlagBox.Value ?? 0);
                _vm.Unknown0E = (uint)(Unknown0EBox.Value ?? 0);
                _vm.Unknown0F = (uint)(Unknown0FBox.Value ?? 0);

                _vm.Write();
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"EventHaikuFE7View.Write_Click failed: {ex.Message}");
            }
        }

        // -- Chapter / map name preview (FE7 ANYFF semantics: 0x45 => ANY) ------

        static string ResolveChapterName(uint chapterId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return "";
            // FE7 (version < 8): 0x45 is the "ANY" sentinel
            // (WinForms MapSettingForm.GetMapNameAndANYFF).
            if (chapterId == 0x45) return "ANY";
            string name = MapSettingCore.GetMapNameById(chapterId);
            return string.IsNullOrEmpty(name) ? "" : name;
        }

        void ChapterId_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint id = (uint)(ChapterIdBox.Value ?? 0);
            ChapterNameLabel.Text = ResolveChapterName(id);
        }

        // -- Unit / Text handlers ---------------------------------------------
        // Jump-address math lives in the shared EditorJumpAddressHelper (#948).

        void Unit_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = EditorJumpAddressHelper.UnitAddrFor(CoreState.ROM, UnitNud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.Error($"EventHaikuFE7View.Unit_Jump failed: {ex.Message}"); }
        }

        async void Unit_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = EditorJumpAddressHelper.UnitAddrFor(CoreState.ROM, UnitNud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) UnitNud.Value = (uint)result.Index + 1; // 0-based pick -> 1-based id
            }
            catch (Exception ex) { Log.Error($"EventHaikuFE7View.Unit_Pick failed: {ex.Message}"); }
        }

        void Unit_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            UnitNud.NameText = e.NewValue == 0 ? "ANY" : NameResolver.GetUnitNameAndANYByOneBasedId(e.NewValue);
        }

        void Text_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = EditorJumpAddressHelper.TextRowAddrFor(CoreState.ROM, TextNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error($"EventHaikuFE7View.Text_Jump failed: {ex.Message}"); }
        }

        void Text_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            TextNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        /// <summary>
        /// Select the row whose address matches <paramref name="address"/>.
        /// Resolves which physical table the address belongs to — the main
        /// 16-byte table or one of the two 12-byte tutorial tables
        /// (<c>event_haiku_tutorial_1_pointer</c> /
        /// <c>event_haiku_tutorial_2_pointer</c>) — switches the Table filter
        /// combo to that table (reloading the list under the correct schema),
        /// then selects the row (#957 W1b). Previously out-of-list (tutorial)
        /// hits were only logged because the VM assumed the 16-byte schema.
        /// </summary>
        public void NavigateTo(uint address)
        {
            if (address == 0) return;

            // First try the currently-loaded table.
            EntryList.SelectAddress(address);
            if (_vm.CurrentAddr == address) return;

            // Resolve which table the address belongs to and switch to it.
            int targetIndex = ResolveTableIndexFor(address);
            if (targetIndex >= 0 && targetIndex != TableFilter.SelectedIndex)
            {
                // Setting SelectedIndex fires TableFilter_SelectionChanged -> LoadList().
                TableFilter.SelectedIndex = targetIndex;
                EntryList.SelectAddress(address);
                if (_vm.CurrentAddr == address) return;
            }

            Log.Notify("EventHaikuFE7View.NavigateTo: address 0x" + address.ToString("X8") + " not found in any haiku table.");
        }

        /// <summary>
        /// Returns the Table filter combo index (0=Main, 1=Tutorial1,
        /// 2=Tutorial2) whose data region contains <paramref name="address"/>,
        /// or -1 if none. Read-only: derives each table's base via the VM's
        /// static resolver and checks block alignment within the loaded range,
        /// without mutating the VM's current-table state.
        /// </summary>
        int ResolveTableIndexFor(uint address)
        {
            var rom = CoreState.ROM;
            if (rom == null) return -1;
            foreach (var (index, table) in new[]
                     {
                         (0, EventHaikuFE7ViewModel.HaikuTable.Main),
                         (1, EventHaikuFE7ViewModel.HaikuTable.Tutorial1),
                         (2, EventHaikuFE7ViewModel.HaikuTable.Tutorial2),
                     })
            {
                uint baseAddr = EventHaikuFE7ViewModel.ResolveBaseAddr(rom, table);
                if (baseAddr == 0 || address < baseAddr) continue;
                uint blockSize = table == EventHaikuFE7ViewModel.HaikuTable.Main ? 16u : 12u;
                if ((address - baseAddr) % blockSize != 0) continue;

                // Walk to the same terminator LoadList uses so we only match
                // addresses that the list would actually surface.
                for (uint a = baseAddr; a + blockSize <= (uint)rom.Data.Length; a += blockSize)
                {
                    if (rom.u8(a) == 0x00) break;
                    if (a == address) return index;
                }
            }
            return -1;
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
