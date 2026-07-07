using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemStatBonusesSkillSystemsView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        readonly ItemStatBonusesSkillSystemsViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Stat Bonuses (Skill Systems)";
        public new bool IsLoaded => _vm.IsLoaded;


        public EditorDescriptor Descriptor => new("Stat Bonuses (Skill Systems)", 1291, 587, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ItemStatBonusesSkillSystemsView()
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
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemStatBonusesSkillSystemsView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemStatBonusesSkillSystemsView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            // Stat bonuses
            HPBox.Value = _vm.HP;
            StrBox.Value = _vm.Str;
            SkillBox.Value = _vm.Skill;
            SpeedBox.Value = _vm.Speed;
            DefBox.Value = _vm.Def;
            ResBox.Value = _vm.Res;
            LuckBox.Value = _vm.Luck;
            MoveBox.Value = _vm.Move;
            ConBox.Value = _vm.Con;
            MagicOrUnknownBox.Value = _vm.MagicOrUnknown;

            // Growth rate bonuses
            GrowHPBox.Value = _vm.GrowHP;
            GrowStrBox.Value = _vm.GrowStr;
            GrowSkillBox.Value = _vm.GrowSkill;
            GrowSpeedBox.Value = _vm.GrowSpeed;
            GrowDefBox.Value = _vm.GrowDef;
            GrowResBox.Value = _vm.GrowRes;
            GrowLuckBox.Value = _vm.GrowLuck;
            GrowUnknownBox.Value = _vm.GrowUnknown;

            // Padding
            Padding1Box.Value = _vm.Padding1;
            Padding2Box.Value = _vm.Padding2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.HP = (int)(HPBox.Value ?? 0);
            _vm.Str = (int)(StrBox.Value ?? 0);
            _vm.Skill = (int)(SkillBox.Value ?? 0);
            _vm.Speed = (int)(SpeedBox.Value ?? 0);
            _vm.Def = (int)(DefBox.Value ?? 0);
            _vm.Res = (int)(ResBox.Value ?? 0);
            _vm.Luck = (int)(LuckBox.Value ?? 0);
            _vm.Move = (int)(MoveBox.Value ?? 0);
            _vm.Con = (int)(ConBox.Value ?? 0);
            _vm.MagicOrUnknown = (int)(MagicOrUnknownBox.Value ?? 0);

            _vm.GrowHP = (int)(GrowHPBox.Value ?? 0);
            _vm.GrowStr = (int)(GrowStrBox.Value ?? 0);
            _vm.GrowSkill = (int)(GrowSkillBox.Value ?? 0);
            _vm.GrowSpeed = (int)(GrowSpeedBox.Value ?? 0);
            _vm.GrowDef = (int)(GrowDefBox.Value ?? 0);
            _vm.GrowRes = (int)(GrowResBox.Value ?? 0);
            _vm.GrowLuck = (int)(GrowLuckBox.Value ?? 0);
            _vm.GrowUnknown = (int)(GrowUnknownBox.Value ?? 0);

            _vm.Padding1 = (int)(Padding1Box.Value ?? 0);
            _vm.Padding2 = (int)(Padding2Box.Value ?? 0);

            _undoService.Begin("Edit Stat Bonuses (Skill Systems)");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Stat Bonuses (Skill Systems) data written.");
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
