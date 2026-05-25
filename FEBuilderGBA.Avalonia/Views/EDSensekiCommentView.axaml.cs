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
                Log.Error("EDSensekiCommentView.LoadList failed: {0}", ex.Message);
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
                Log.Error("EDSensekiCommentView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            try { UnitIdBox.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, _vm.UnitId); }
            catch { /* SupportUnitNavigation may fail without ROM — leave prior text */ }
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
                Log.Error("EDSensekiCommentView.Write failed: {0}", ex.Message);
            }
        }

        // -- IdFieldControl handlers (#360 final) ---------------------------

        static uint UnitAddrFor(uint unitId)
        {
            var rom = CoreState.ROM;
            if (rom?.RomInfo == null) return 0;
            uint unitPtr = rom.RomInfo.unit_pointer;
            if (unitPtr == 0) return 0;
            uint baseAddr = rom.p32(unitPtr);
            if (!U.isSafetyOffset(baseAddr, rom)) return 0;
            uint dataSize = rom.RomInfo.unit_datasize;
            if (dataSize == 0) return 0;
            if (rom.RomInfo.version == 6) baseAddr += dataSize;
            uint entryAddr = baseAddr + unitId * dataSize;
            if (!U.isSafetyOffset(entryAddr, rom)) return 0;
            if (!U.isSafetyOffset(entryAddr + dataSize - 1, rom)) return 0;
            return entryAddr;
        }

        void UnitId_Jump(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(UnitIdBox.Value);
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex) { Log.Error("EDSensekiCommentView.UnitId_Jump failed: {0}", ex.Message); }
        }

        async void UnitId_Pick(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint addr = UnitAddrFor(UnitIdBox.Value);
                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(addr, this);
                if (result != null) UnitIdBox.Value = (uint)result.Index;
            }
            catch (Exception ex) { Log.Error("EDSensekiCommentView.UnitId_Pick failed: {0}", ex.Message); }
        }

        void UnitId_ValueChanged(object? sender, IdFieldValueChangedEventArgs e)
        {
            try { UnitIdBox.NameText = SupportUnitNavigation.ResolveUnitTableName(CoreState.ROM, e.NewValue); }
            catch { /* SupportUnitNavigation may fail without ROM — leave prior text */ }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
