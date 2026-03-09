using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventForceSortieView : Window, IEditorView
    {
        readonly EventForceSortieViewModel _vm = new();

        public string ViewTitle => "Force Sortie Editor";
        public bool IsLoaded => _vm.IsLoaded;

        public EventForceSortieView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
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
                Log.Error("EventForceSortieView.LoadList failed: {0}", ex.Message);
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
                Log.Error("EventForceSortieView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            UnitUpDown.Value = _vm.Unit;
            SquadUpDown.Value = _vm.Squad;
            ChapterIdUpDown.Value = _vm.ChapterId;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Unit = (uint)(UnitUpDown.Value ?? 0);
                _vm.Squad = (uint)(SquadUpDown.Value ?? 0);
                _vm.ChapterId = (uint)(ChapterIdUpDown.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("EventForceSortieView.OnWrite failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
