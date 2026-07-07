using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapBGMView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly WorldMapBGMViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "World Map BGM";
        public new bool IsLoaded => _vm.CanWrite;


        public EditorDescriptor Descriptor => new("World Map BGM Editor", 1163, 778, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public WorldMapBGMView()
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
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadWorldMapBGMList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapBGMView.LoadList failed: {0}", ex.Message);
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
                _vm.LoadWorldMapBGM(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("WorldMapBGMView.OnSelected failed: {0}", ex.Message);
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
            SongId1Box.Value = _vm.SongId1;
            SongId2Box.Value = _vm.SongId2;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _undoService.Begin("Edit World Map BGM");
            try
            {
                _vm.SongId1 = (uint)(SongId1Box.Value ?? 0);
                _vm.SongId2 = (uint)(SongId2Box.Value ?? 0);
                _vm.WriteWorldMapBGM();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("World map BGM data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.ErrorF("WorldMapBGMView.Write_Click failed: {0}", ex.Message);
            }
        }

        void JumpToSong1_Click(object? sender, RoutedEventArgs e) => JumpToSong((uint)(SongId1Box.Value ?? 0));
        void JumpToSong2_Click(object? sender, RoutedEventArgs e) => JumpToSong((uint)(SongId2Box.Value ?? 0));

        void JumpToSong(uint songId)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint ptr = rom.RomInfo.sound_table_pointer;
                if (ptr == 0) return;
                uint baseAddr = rom.p32(ptr);
                if (!U.isSafetyOffset(baseAddr)) return;
                uint addr = baseAddr + songId * 8;
                WindowManager.Instance.Navigate<SongTableView>(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("JumpToSong failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
