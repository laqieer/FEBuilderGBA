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
    public partial class EventHaikuView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventHaikuViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        // Route choices mirror WinForms EventHaikuForm L_2_COMBO
        // (01=序盤 / 02=エイリーク編 / 03=エフラム編 / FF=無条件). Each item maps a
        // numeric Route byte to a label; the combo selection is kept in sync with
        // the byte value (an unknown byte selects no item, like WinForms).
        static readonly (uint Value, string Label)[] RouteChoices =
        {
            (0x01u, "0x01 Prologue / Early"),
            (0x02u, "0x02 Eirika Route"),
            (0x03u, "0x03 Ephraim Route"),
            (0xFFu, "0xFF Unconditional"),
        };

        public string ViewTitle => "Haiku Event Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Haiku Event Editor", 1252, 724, SizeToContent: true);
        public event EventHandler? CloseRequested;

        public EventHaikuView()
        {
            InitializeComponent();
            // ComboBox.Items strings are NOT picked up by the logical-tree
            // translation pass, so route each Route label through R._() so ja/zh
            // locales localize them (#948 review; same pattern as ClassOPDemoView).
            foreach (var c in RouteChoices) RouteCombo.Items.Add(R._(c.Label));
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
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error($"EventHaikuView.LoadList failed: {ex.Message}");
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
                Log.Error($"EventHaikuView.OnSelected failed: {ex.Message}");
            }
        }

        /// <summary>True when the FE8 "death quote add killer ID" patch is
        /// installed, so offset +1 is a real killer unit id (not an unknown byte).</summary>
        static bool KillerIdEnabled() => PatchDetectionService.Instance.DeathQuoteAddKillerID;

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            UnitNud.Value = _vm.Unit;
            UnitNud.NameText = _vm.Unit == 0 ? "ANY" : NameResolver.GetUnitNameAndANYByOneBasedId(_vm.Unit);

            // KillerUnit: patch-gated. Killer-id mode shows a unit field; otherwise
            // a plain "unknown byte" NumericUpDown (do NOT imply killer semantics
            // on vanilla ROMs — #947 plan).
            bool killer = KillerIdEnabled();
            KillerUnitNud.IsVisible = killer;
            KillerUnitPlainCaption.IsVisible = !killer;
            KillerUnitPlainBox.IsVisible = !killer;
            KillerUnitNud.Value = _vm.KillerUnit;
            KillerUnitNud.NameText = (_vm.KillerUnit == 0) ? "ANY" : NameResolver.GetUnitNameAndANYByOneBasedId(_vm.KillerUnit);
            KillerUnitPlainBox.Value = _vm.KillerUnit;

            SetRouteCombo(_vm.Route);

            ChapterIdBox.Value = _vm.ChapterID;
            ChapterNameLabel.Text = ResolveChapterName(_vm.ChapterID);

            AchievementFlagBox.Value = _vm.AchievementFlag;

            TextNud.Value = _vm.Text;
            TextNud.NameText = _vm.Text != 0 ? NameResolver.GetTextById(_vm.Text) : "";

            EventPointerBox.Value = _vm.EventPointer;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Haiku Event");
            try
            {
                _vm.Unit = UnitNud.Value;
                // Killer-id mode reads the unit field; otherwise the plain box.
                _vm.KillerUnit = KillerIdEnabled() ? KillerUnitNud.Value : (uint)(KillerUnitPlainBox.Value ?? 0);
                _vm.Route = ReadRouteCombo();
                _vm.ChapterID = (uint)(ChapterIdBox.Value ?? 0);
                _vm.AchievementFlag = (uint)(AchievementFlagBox.Value ?? 0);
                _vm.Text = TextNud.Value;
                _vm.EventPointer = (uint)(EventPointerBox.Value ?? 0);

                _vm.Write();
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"EventHaikuView.Write_Click failed: {ex.Message}");
            }
        }

        // -- Route combo <-> numeric value sync --------------------------------

        bool _routeSync;

        void SetRouteCombo(uint value)
        {
            _routeSync = true;
            try
            {
                int idx = -1;
                for (int i = 0; i < RouteChoices.Length; i++)
                {
                    if (RouteChoices[i].Value == value) { idx = i; break; }
                }
                RouteCombo.SelectedIndex = idx; // -1 = unknown byte, no item
            }
            finally { _routeSync = false; }
        }

        uint ReadRouteCombo()
        {
            int idx = RouteCombo.SelectedIndex;
            if (idx >= 0 && idx < RouteChoices.Length)
                return RouteChoices[idx].Value;
            // No combo selection (unknown byte) — preserve whatever the VM holds.
            return _vm.Route;
        }

        void Route_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_routeSync) return;
            // Keep the VM in sync so a Write without re-selecting still persists.
            _vm.Route = ReadRouteCombo();
        }

        // -- Chapter / map name preview (ANYFF semantics) ----------------------

        /// <summary>Resolve a chapter id to a name preview. FE8 renders 0xFF as
        /// "ANY" (WinForms MapSettingForm.GetMapNameAndANYFF).</summary>
        static string ResolveChapterName(uint chapterId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return "";
            // FE8 (version >= 8): 0xFF is the "ANY" sentinel.
            if (chapterId == 0xFF) return "ANY";
            string name = MapSettingCore.GetMapNameById(chapterId);
            return string.IsNullOrEmpty(name) ? "" : name;
        }

        void ChapterId_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            uint id = (uint)(ChapterIdBox.Value ?? 0);
            ChapterNameLabel.Text = ResolveChapterName(id);
        }

        // -- Unit IdField handlers --------------------------------------------
        // Jump-address math lives in the shared EditorJumpAddressHelper (#948).

        void Unit_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = EditorJumpAddressHelper.UnitAddrFor(CoreState.ROM, UnitNud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.Error($"EventHaikuView.Unit_Jump failed: {ex.Message}"); }
        }

        async void Unit_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = EditorJumpAddressHelper.UnitAddrFor(CoreState.ROM, UnitNud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr);
                if (result != null) UnitNud.Value = (uint)result.Index + 1; // 0-based pick -> 1-based id
            }
            catch (Exception ex) { Log.Error($"EventHaikuView.Unit_Pick failed: {ex.Message}"); }
        }

        void Unit_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            UnitNud.NameText = e.NewValue == 0 ? "ANY" : NameResolver.GetUnitNameAndANYByOneBasedId(e.NewValue);
        }

        void KillerUnit_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = EditorJumpAddressHelper.UnitAddrFor(CoreState.ROM, KillerUnitNud.Value); if (addr != 0) WindowManager.Instance.Navigate<UnitEditorView>(addr); }
            catch (Exception ex) { Log.Error($"EventHaikuView.KillerUnit_Jump failed: {ex.Message}"); }
        }

        async void KillerUnit_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = EditorJumpAddressHelper.UnitAddrFor(CoreState.ROM, KillerUnitNud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr);
                if (result != null) KillerUnitNud.Value = (uint)result.Index + 1;
            }
            catch (Exception ex) { Log.Error($"EventHaikuView.KillerUnit_Pick failed: {ex.Message}"); }
        }

        void KillerUnit_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            KillerUnitNud.NameText = e.NewValue == 0 ? "ANY" : NameResolver.GetUnitNameAndANYByOneBasedId(e.NewValue);
        }

        void Text_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = EditorJumpAddressHelper.TextRowAddrFor(CoreState.ROM, TextNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error($"EventHaikuView.Text_Jump failed: {ex.Message}"); }
        }

        void Text_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            TextNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
