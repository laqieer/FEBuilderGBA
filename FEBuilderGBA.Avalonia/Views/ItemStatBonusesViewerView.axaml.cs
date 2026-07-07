using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemStatBonusesViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        readonly ItemStatBonusesViewerViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Item Stat Bonuses";
        public new bool IsLoaded => _vm.CanWrite;


        public EditorDescriptor Descriptor => new("Item Stat Bonuses Editor", 1291, 587, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ItemStatBonusesViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;        }


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
                var items = _vm.LoadItemStatBonusesList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemStatBonusesViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItemStatBonuses(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemStatBonusesViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            HPBox.Value = _vm.HP;
            StrBox.Value = _vm.Str;
            SkillBox.Value = _vm.Skill;
            SpeedBox.Value = _vm.Speed;
            DefBox.Value = _vm.Def;
            ResBox.Value = _vm.Res;
            LuckBox.Value = _vm.Luck;
            MoveBox.Value = _vm.Move;
            ConBox.Value = _vm.Con;
            Unknown9Box.Value = _vm.Unknown9;
            Unknown10Box.Value = _vm.Unknown10;
            Unknown11Box.Value = _vm.Unknown11;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.HP = (uint)(HPBox.Value ?? 0);
            _vm.Str = (uint)(StrBox.Value ?? 0);
            _vm.Skill = (uint)(SkillBox.Value ?? 0);
            _vm.Speed = (uint)(SpeedBox.Value ?? 0);
            _vm.Def = (uint)(DefBox.Value ?? 0);
            _vm.Res = (uint)(ResBox.Value ?? 0);
            _vm.Luck = (uint)(LuckBox.Value ?? 0);
            _vm.Move = (uint)(MoveBox.Value ?? 0);
            _vm.Con = (uint)(ConBox.Value ?? 0);
            _vm.Unknown9 = (uint)(Unknown9Box.Value ?? 0);
            _vm.Unknown10 = (uint)(Unknown10Box.Value ?? 0);
            _vm.Unknown11 = (uint)(Unknown11Box.Value ?? 0);

            _undoService.Begin("Edit Item Stat Bonuses");
            try
            {
                _vm.WriteItemStatBonuses();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Item Stat Bonuses data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
