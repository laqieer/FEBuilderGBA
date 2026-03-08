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
        public bool IsLoaded => _vm.CanWrite;

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
            UnitIdBox.Value = _vm.UnitId;
            Unknown1Box.Value = _vm.Unknown1;
            Unknown2Box.Value = _vm.Unknown2;
            Unknown3Box.Value = _vm.Unknown3;
            SongIdBox.Value = _vm.SongId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
            _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
            _vm.Unknown2 = (uint)(Unknown2Box.Value ?? 0);
            _vm.Unknown3 = (uint)(Unknown3Box.Value ?? 0);
            _vm.SongId = (uint)(SongIdBox.Value ?? 0);
            _vm.WriteSoundBossBGM();
            CoreState.Services.ShowInfo("Boss BGM data written.");
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
