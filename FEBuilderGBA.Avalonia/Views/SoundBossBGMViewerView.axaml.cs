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
        readonly UndoService _undoService = new();

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
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSoundBossBGMList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SoundBossBGMViewerView.LoadList failed: {0}", ex.Message);
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
                _vm.LoadSoundBossBGM(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SoundBossBGMViewerView.OnSelected failed: {0}", ex.Message);
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
            UnitIdBox.Value = _vm.UnitId;
            Unknown1Box.Value = _vm.Unknown1;
            Unknown2Box.Value = _vm.Unknown2;
            Unknown3Box.Value = _vm.Unknown3;
            SongIdBox.Value = _vm.SongId;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _undoService.Begin("Edit Boss BGM");
            try
            {
                _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
                _vm.Unknown1 = (uint)(Unknown1Box.Value ?? 0);
                _vm.Unknown2 = (uint)(Unknown2Box.Value ?? 0);
                _vm.Unknown3 = (uint)(Unknown3Box.Value ?? 0);
                _vm.SongId = (uint)(SongIdBox.Value ?? 0);
                _vm.WriteSoundBossBGM();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Boss BGM data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SoundBossBGMViewerView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        void JumpToUnit_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint unitId = (uint)(UnitIdBox.Value ?? 0);
                uint baseAddr = rom.p32(rom.RomInfo.unit_pointer);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint dataSize = rom.RomInfo.unit_datasize;
                // FE6 skips entry 0
                if (rom.RomInfo.version == 6)
                    baseAddr += dataSize;
                uint addr = baseAddr + unitId * dataSize;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex)
            {
                Log.Error("JumpToUnit failed: {0}", ex.Message);
            }
        }

        async void PickUnit_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint unitId = (uint)(UnitIdBox.Value ?? 0);
                uint baseAddr = rom.p32(rom.RomInfo.unit_pointer);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint dataSize = rom.RomInfo.unit_datasize;
                if (rom.RomInfo.version == 6) baseAddr += dataSize;
                uint navAddr = baseAddr + unitId * dataSize;

                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(navAddr, this);
                if (result != null)
                    UnitIdBox.Value = result.Index;
            }
            catch (Exception ex)
            {
                Log.Error("PickUnit failed: {0}", ex.Message);
            }
        }

        public ViewModelBase? DataViewModel => _vm;
    }
}
