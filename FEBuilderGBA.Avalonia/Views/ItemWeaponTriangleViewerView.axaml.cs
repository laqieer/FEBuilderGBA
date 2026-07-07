using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ItemWeaponTriangleViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        readonly ItemWeaponTriangleViewerViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Weapon Triangle";
        public new bool IsLoaded => _vm.CanWrite;


        public EditorDescriptor Descriptor => new("Weapon Triangle Editor", 1290, 648, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);

        public event EventHandler? CloseRequested;
        public ItemWeaponTriangleViewerView()
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
                var items = _vm.LoadItemWeaponTriangleList();
                // Show weapon-TYPE icons (attacker + defender) read from ROM,
                // NOT item icons keyed on the row text — issue #370.
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.WeaponTypePairFromAddrU8Loader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("ItemWeaponTriangleViewerView.LoadList: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadItemWeaponTriangle(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.ErrorF("ItemWeaponTriangleViewerView.OnSelected: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            WeaponType1Box.Value = _vm.WeaponType1;
            WeaponType2Box.Value = _vm.WeaponType2;
            // Bonus/Penalty are signed (sbyte). NumericUpDown.Value is decimal,
            // and Bonus/Penalty are int — implicit conversion is safe because
            // int range fits in decimal. Issue #370.
            BonusBox.Value = _vm.Bonus;
            PenaltyBox.Value = _vm.Penalty;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _vm.WeaponType1 = (uint)(WeaponType1Box.Value ?? 0);
            _vm.WeaponType2 = (uint)(WeaponType2Box.Value ?? 0);
            // Bonus/Penalty are signed (int). The XAML clamps the NumericUpDown
            // to -128..127, so the cast is safe. Issue #370.
            _vm.Bonus = (int)(BonusBox.Value ?? 0);
            _vm.Penalty = (int)(PenaltyBox.Value ?? 0);

            _undoService.Begin("Edit Weapon Triangle");
            try
            {
                _vm.WriteItemWeaponTriangle();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Weapon Triangle data written.");
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
