using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SupportTalkFE6View : Window, IEditorView, IDataVerifiableView
    {
        readonly SupportTalkFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Support Talk (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public SupportTalkFE6View()
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
                Log.Error("SupportTalkFE6View.LoadList failed: {0}", ex.Message);
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
                Log.Error("SupportTalkFE6View.OnSelected failed: {0}", ex.Message);
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
            TextCNud.Value = _vm.TextC;
            TextBNud.Value = _vm.TextB;
            TextANud.Value = _vm.TextA;
            Padding1Nud.Value = _vm.Padding1;
            Padding2Nud.Value = _vm.Padding2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Support Talk (FE6)");
            try
            {
                _vm.SupportPartner1 = (uint)(SupportPartner1Nud.Value ?? 0);
                _vm.SupportPartner2 = (uint)(SupportPartner2Nud.Value ?? 0);
                _vm.TextC = (uint)(TextCNud.Value ?? 0);
                _vm.TextB = (uint)(TextBNud.Value ?? 0);
                _vm.TextA = (uint)(TextANud.Value ?? 0);
                _vm.Padding1 = (uint)(Padding1Nud.Value ?? 0);
                _vm.Padding2 = (uint)(Padding2Nud.Value ?? 0);

                _vm.WriteSupportTalk();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SupportTalkFE6View.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
