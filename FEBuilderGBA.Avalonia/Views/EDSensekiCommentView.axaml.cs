using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDSensekiCommentView : Window, IEditorView, IDataVerifiableView
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
                EntryList.SetItems(items);
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
            ConvText1Box.Value = _vm.ConversationText1;
            ConvText2Box.Value = _vm.ConversationText2;
            ConvText3Box.Value = _vm.ConversationText3;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
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

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
