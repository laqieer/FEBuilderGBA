using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Core;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDSensekiCommentView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EDSensekiCommentViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "ED Senseki Comment";
        public bool IsLoaded => _vm.IsLoaded;
        public ViewModelBase? DataViewModel => _vm;

        public EDSensekiCommentView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
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
                Log.ErrorF("EDSensekiCommentView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("EDSensekiCommentView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            // UnitId is a 1-based ROM unit ID; GetUnitNameByOneBasedId handles
            // the 0/bounds cases and the 1-based → 0-based conversion (#937).
            UnitIdBox.NameText = NameResolver.GetUnitNameByOneBasedId(_vm.UnitId);
            ConvText1Box.Value = _vm.ConversationText1;
            ConvText2Box.Value = _vm.ConversationText2;
            ConvText3Box.Value = _vm.ConversationText3;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.UnitId = UnitIdBox.Value;
            _vm.ConversationText1 = (uint)(ConvText1Box.Value ?? 0);
            _vm.ConversationText2 = (uint)(ConvText2Box.Value ?? 0);
            _vm.ConversationText3 = (uint)(ConvText3Box.Value ?? 0);
            _undoService.Begin("Edit ED Senseki Comment");
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("ED Senseki Comment data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("EDSensekiCommentView.Write failed: {0}", ex.Message);
            }
        }

        // -- IdFieldControl handlers (#360 final) ---------------------------

        void UnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                // UnitId is 1-based; UnitAddrForOneBased applies the (id-1)
                // index + FE6 dummy-entry skip so Jump lands on the right unit (#937).
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.ErrorF("EDSensekiCommentView.UnitId_Jump failed: {0}", ex.Message); }
        }

        async void UnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                // PickResult.Index is 0-based; UnitId is 1-based (#937).
                if (result != null) UnitIdBox.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
            }
            catch (Exception ex) { Log.ErrorF("EDSensekiCommentView.UnitId_Pick failed: {0}", ex.Message); }
        }

        void UnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            // 1-based Unit ID → name via GetUnitNameByOneBasedId (#937).
            UnitIdBox.NameText = NameResolver.GetUnitNameByOneBasedId(e.NewValue);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
