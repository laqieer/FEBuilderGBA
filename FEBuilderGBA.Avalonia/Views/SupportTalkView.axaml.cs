using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportTalkView : Window, IEditorView, IDataVerifiableView
    {
        readonly SupportTalkViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Support Talk";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportTalkView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += Write_Click;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSupportTalkList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkView.LoadList failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSupportTalk(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SupportTalkView.OnSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            SupportPartner1Nud.Value = _vm.SupportPartner1;
            SupportPartner2Nud.Value = _vm.SupportPartner2;
            TextIdCNud.Value = _vm.TextIdC;
            TextIdBNud.Value = _vm.TextIdB;
            TextIdANud.Value = _vm.TextIdA;
            SongCNud.Value = _vm.SongC;
            SongBNud.Value = _vm.SongB;
            SongANud.Value = _vm.SongA;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Support Talk");
            try
            {
                _vm.SupportPartner1 = (uint)(SupportPartner1Nud.Value ?? 0);
                _vm.SupportPartner2 = (uint)(SupportPartner2Nud.Value ?? 0);
                _vm.TextIdC = (uint)(TextIdCNud.Value ?? 0);
                _vm.TextIdB = (uint)(TextIdBNud.Value ?? 0);
                _vm.TextIdA = (uint)(TextIdANud.Value ?? 0);
                _vm.SongC = (uint)(SongCNud.Value ?? 0);
                _vm.SongB = (uint)(SongBNud.Value ?? 0);
                _vm.SongA = (uint)(SongANud.Value ?? 0);

                _vm.WriteSupportTalk();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SupportTalkView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
