using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SoundBossBGMViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SoundBossBGMViewerViewModel _vm = new();

        public string ViewTitle => "Boss BGM";
        public bool IsLoaded => _vm.IsLoaded;

        public SoundBossBGMViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadSoundBossBGMList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SoundBossBGMViewerView.LoadList failed: {0}", ex.Message);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadSoundBossBGM(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SoundBossBGMViewerView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdLabel.Text = $"0x{_vm.UnitId:X02} ({_vm.UnitId})";
            Unknown1Label.Text = $"0x{_vm.Unknown1:X02}";
            Unknown2Label.Text = $"0x{_vm.Unknown2:X02}";
            Unknown3Label.Text = $"0x{_vm.Unknown3:X02}";
            SongIdLabel.Text = $"0x{_vm.SongId:X08}";
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
