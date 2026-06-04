using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventHaikuFE7View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventHaikuFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Haiku (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventHaikuFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += Write_Click;
            Opened += (_, _) => LoadList();
        }

        // OUT OF SCOPE (#947 / #7): the FE7 N1_ tutorial death-quote tables
        // (event_haiku_tutorial_1_pointer / event_haiku_tutorial_2_pointer) use a
        // DIFFERENT 12-byte schema and are intentionally NOT edited by this view's
        // VM/panel. They remain a documented follow-up (see NavigateTo below).

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
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
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
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
        /// If the address sits in a table that this view's <see cref="LoadList"/>
        /// did NOT load (e.g. FE7's tutorial tables at
        /// <c>event_haiku_tutorial_1_pointer</c> /
        /// <c>event_haiku_tutorial_2_pointer</c> which use 12-byte blocks),
        /// log the hit and do NOT call LoadEntry — the VM assumes the main
        /// 16-byte schema and would misparse the 12-byte tutorial rows
        /// (Copilot review #522 round 4). The Core search helper
        /// <see cref="MapEventUnitCore.FindHaikuFE7Address"/> still routes
        /// the user to the correct tutorial address; the user can manually
        /// open the address if a 12-byte-aware tutorial list is added in a
        /// follow-up.
        /// </summary>
        public void NavigateTo(uint address)
        {
            if (address == 0) return;
            EntryList.SelectAddress(address);
            if (_vm.CurrentAddr == address) return;
            // Out-of-list address (tutorial-table hit). The N1 (tutorial)
            // schema is 12-byte but this VM assumes the main 16-byte
            // schema — loading the entry would misparse fields and leave
            // Write enabled against the wrong schema (Copilot review #522
            // round 4). Log the hit; tutorial-list UI is a follow-up.
            Log.Notify("EventHaikuFE7View.NavigateTo: tutorial-table (12-byte) hit at 0x" + address.ToString("X8") + ". Tutorial list UI is tracked as a follow-up to PR #522.");
        }
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
