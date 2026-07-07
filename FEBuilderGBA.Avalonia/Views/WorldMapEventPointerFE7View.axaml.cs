using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapEventPointerFE7View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly WorldMapEventPointerFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;

        public string ViewTitle => "World Map Event (FE7)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Event Pointer (FE7)", 1288, 770, SizeToContent: true);
        public event EventHandler? CloseRequested;

        public WorldMapEventPointerFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWriteEntry;
            EventWriteButton.Click += OnWriteGlobalEvents;
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
            try
            {
                _vm.IsLoading = true;
                _vm.LoadGlobalEvents();
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                UpdateGlobalUI();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapEventPointerFE7View.LoadList failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapEventPointerFE7View.OnSelected failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            EventPointerBox.Value = _vm.EventPointer;
        }

        void UpdateGlobalUI()
        {
            Ending1Box.Value = _vm.Ending1Event;
            Ending2Box.Value = _vm.Ending2Event;
        }

        void OnWriteEntry(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit World Map Event (FE7)"));
            try
            {
                _vm.EventPointer = (uint)(EventPointerBox.Value ?? 0);
                if (_vm.WriteEntry())
                    _undoService.Commit();
                else
                    _undoService.Rollback();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapEventPointerFE7View.OnWriteEntry failed: " + ex.ToString());
            }
        }

        void OnWriteGlobalEvents(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin(R._("Edit World Map Ending Events (FE7)"));
            try
            {
                _vm.Ending1Event = (uint)(Ending1Box.Value ?? 0);
                _vm.Ending2Event = (uint)(Ending2Box.Value ?? 0);
                if (_vm.WriteGlobalEvents())
                    _undoService.Commit();
                else
                    _undoService.Rollback();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapEventPointerFE7View.OnWriteGlobalEvents failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
