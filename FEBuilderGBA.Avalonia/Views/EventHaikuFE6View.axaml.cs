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
    public partial class EventHaikuFE6View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventHaikuFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Haiku (FE6)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Haiku (FE6)", 1259, 826, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public EventHaikuFE6View()
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
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error($"EventHaikuFE6View.LoadList failed: {ex.Message}");
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
                Log.Error($"EventHaikuFE6View.OnSelected failed: {ex.Message}");
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            UnitNud.Value = _vm.Unit;
            UnitNud.NameText = _vm.Unit == 0 ? "" : NameResolver.GetUnitNameByOneBasedId(_vm.Unit);

            ChapterIdBox.Value = _vm.ChapterID;
            ChapterNameLabel.Text = ResolveChapterName(_vm.ChapterID);

            Unknown02Box.Value = _vm.Unknown02;
            Unknown03Box.Value = _vm.Unknown03;

            DeathTextNud.Value = _vm.DeathText;
            DeathTextNud.NameText = _vm.DeathText != 0 ? NameResolver.GetTextById(_vm.DeathText) : "";

            Unknown06Box.Value = _vm.Unknown06;
            Unknown07Box.Value = _vm.Unknown07;
            AchievementFlagBox.Value = _vm.AchievementFlag;
            Unknown0ABox.Value = _vm.Unknown0A;
            Unknown0BBox.Value = _vm.Unknown0B;

            FinalChapterTextNud.Value = _vm.FinalChapterText;
            FinalChapterTextNud.NameText = _vm.FinalChapterText != 0 ? NameResolver.GetTextById(_vm.FinalChapterText) : "";

            Unknown0EBox.Value = _vm.Unknown0E;
            Unknown0FBox.Value = _vm.Unknown0F;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Haiku (FE6)");
            try
            {
                _vm.Unit = UnitNud.Value;
                _vm.ChapterID = (uint)(ChapterIdBox.Value ?? 0);
                _vm.Unknown02 = (uint)(Unknown02Box.Value ?? 0);
                _vm.Unknown03 = (uint)(Unknown03Box.Value ?? 0);
                _vm.DeathText = DeathTextNud.Value;
                _vm.Unknown06 = (uint)(Unknown06Box.Value ?? 0);
                _vm.Unknown07 = (uint)(Unknown07Box.Value ?? 0);
                _vm.AchievementFlag = (uint)(AchievementFlagBox.Value ?? 0);
                _vm.Unknown0A = (uint)(Unknown0ABox.Value ?? 0);
                _vm.Unknown0B = (uint)(Unknown0BBox.Value ?? 0);
                _vm.FinalChapterText = FinalChapterTextNud.Value;
                _vm.Unknown0E = (uint)(Unknown0EBox.Value ?? 0);
                _vm.Unknown0F = (uint)(Unknown0FBox.Value ?? 0);

                _vm.Write();
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"EventHaikuFE6View.Write_Click failed: {ex.Message}");
            }
        }

        // -- Chapter / map name preview (FE6 ANYIFOVER semantics) --------------

        /// <summary>Resolve a chapter id to a name preview. FE6 renders an
        /// over-the-map-count id as "ANY" (WinForms
        /// MapSettingForm.GetMapNameAndANYIFOVER).</summary>
        static string ResolveChapterName(uint chapterId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return "";
            int count = MapSettingCore.GetMapCount();
            if (count > 0 && chapterId >= (uint)count) return "ANY";
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
            catch (Exception ex) { Log.Error($"EventHaikuFE6View.Unit_Jump failed: {ex.Message}"); }
        }

        async void Unit_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = EditorJumpAddressHelper.UnitAddrFor(CoreState.ROM, UnitNud.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, TopLevel.GetTopLevel(this) as Window);
                if (result != null) UnitNud.Value = (uint)result.Index + 1; // 0-based pick -> 1-based id
            }
            catch (Exception ex) { Log.Error($"EventHaikuFE6View.Unit_Pick failed: {ex.Message}"); }
        }

        void Unit_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            UnitNud.NameText = e.NewValue == 0 ? "" : NameResolver.GetUnitNameByOneBasedId(e.NewValue);
        }

        void DeathText_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = EditorJumpAddressHelper.TextRowAddrFor(CoreState.ROM, DeathTextNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error($"EventHaikuFE6View.DeathText_Jump failed: {ex.Message}"); }
        }

        void DeathText_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            DeathTextNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        void FinalChapterText_Jump(object? sender, RoutedEventArgs e)
        {
            try { uint addr = EditorJumpAddressHelper.TextRowAddrFor(CoreState.ROM, FinalChapterTextNud.Value); if (addr != 0) WindowManager.Instance.Navigate<TextViewerView>(addr); }
            catch (Exception ex) { Log.Error($"EventHaikuFE6View.FinalChapterText_Jump failed: {ex.Message}"); }
        }

        void FinalChapterText_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            FinalChapterTextNud.NameText = e.NewValue != 0 ? NameResolver.GetTextById(e.NewValue) : "";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
