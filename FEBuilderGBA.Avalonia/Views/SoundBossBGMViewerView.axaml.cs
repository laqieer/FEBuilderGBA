using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SoundBossBGMViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SoundBossBGMViewerViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "Boss BGM";
        public new bool IsLoaded => _vm.CanWrite;
        public EditorDescriptor Descriptor => new("Boss BGM Editor", 1392, 722, SizeToContent: true);
        public event EventHandler? CloseRequested;

        public SoundBossBGMViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
        }

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
                var items = _vm.LoadSoundBossBGMList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.ErrorF("SoundBossBGMViewerView.LoadList failed: {0}", ex.Message);
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
                Log.ErrorF("SoundBossBGMViewerView.OnSelected failed: {0}", ex.Message);
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
                Log.ErrorF("SoundBossBGMViewerView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();

        void OnUnitIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            JumpToUnit_Click(sender, new RoutedEventArgs());
        }

        void OnSongIdLinkClick(object? sender, PointerPressedEventArgs e)
        {
            JumpToSong_Click(sender, new RoutedEventArgs());
        }

        void JumpToSong_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint songId = (uint)(SongIdBox.Value ?? 0);
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

        void JumpToUnit_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                // UnitId is 1-based; UnitAddrForOneBased applies the (id-1)
                // index + FE6 dummy-entry skip so Jump lands on the right unit (#937).
                uint addr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, (uint)(UnitIdBox.Value ?? 0));
                if (addr == 0) return;
                WindowManager.Instance.Navigate<UnitEditorView>(addr);
            }
            catch (Exception ex)
            {
                Log.ErrorF("JumpToUnit failed: {0}", ex.Message);
            }
        }

        async void PickUnit_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                uint navAddr = SupportUnitNavigation.UnitAddrForOneBased(CoreState.ROM, (uint)(UnitIdBox.Value ?? 0));

                var result = await WindowManager.Instance.PickFromEditor<UnitEditorView>(navAddr);
                // PickResult.Index is 0-based; UnitId is 1-based (#937).
                if (result != null)
                    UnitIdBox.Value = SupportUnitNavigation.OneBasedIdFromPickIndex(result.Index);
            }
            catch (Exception ex)
            {
                Log.ErrorF("PickUnit failed: {0}", ex.Message);
            }
        }

        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
