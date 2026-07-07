using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventBattleTalkView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventBattleTalkViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Battle Dialogue Editor";
        public new bool IsLoaded => _vm.IsLoaded;


        public EditorDescriptor Descriptor => new("Battle Dialogue Editor", 1445, 815, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public EventBattleTalkView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;

            // Live name/text previews while editing.
            AttackerUnitBox.ValueChanged += (_, _) => AttackerNameLabel.Text = UnitName(AttackerUnitBox);
            DefenderUnitBox.ValueChanged += (_, _) => DefenderNameLabel.Text = UnitName(DefenderUnitBox);
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

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitFromAddrU16Loader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleTalkView.LoadList failed: " + ex.ToString());
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
                Log.Error("EventBattleTalkView.OnSelected failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            AttackerUnitBox.Value = _vm.AttackerUnit;
            DefenderUnitBox.Value = _vm.DefenderUnit;
            MapBox.Value = _vm.Map;
            Unknown05Box.Value = _vm.Unknown05;
            AchievementFlagBox.Value = _vm.AchievementFlag;
            TextIdBox.Value = _vm.Text;
            Unknown0ABox.Value = _vm.Unknown0A;
            Unknown0BBox.Value = _vm.Unknown0B;
            EventPointerBox.Value = _vm.EventPointer;

            AttackerNameLabel.Text = UnitName(AttackerUnitBox);
            DefenderNameLabel.Text = UnitName(DefenderUnitBox);
            TextPreviewLabel.Text = TextPreview(TextIdBox);
        }

        static string UnitName(NumericUpDown box)
        {
            try { return NameResolver.GetUnitNameAndANYByOneBasedId((uint)(box.Value ?? 0)); }
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

            _undoService.Begin(R._("Edit Battle Dialogue"));
            try
            {
                _vm.AttackerUnit = (uint)(AttackerUnitBox.Value ?? 0);
                _vm.DefenderUnit = (uint)(DefenderUnitBox.Value ?? 0);
                _vm.Map = (uint)(MapBox.Value ?? 0);
                _vm.Unknown05 = (uint)(Unknown05Box.Value ?? 0);
                _vm.AchievementFlag = (uint)(AchievementFlagBox.Value ?? 0);
                _vm.Text = (uint)(TextIdBox.Value ?? 0);
                _vm.Unknown0A = (uint)(Unknown0ABox.Value ?? 0);
                _vm.Unknown0B = (uint)(Unknown0BBox.Value ?? 0);
                _vm.EventPointer = (uint)(EventPointerBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventBattleTalkView.OnWrite failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
