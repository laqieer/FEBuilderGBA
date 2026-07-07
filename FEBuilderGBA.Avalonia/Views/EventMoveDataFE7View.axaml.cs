using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventMoveDataFE7View : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventMoveDataFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        // Guard so programmatic UI population doesn't re-trigger handlers.
        bool _suppressEvents;

        public string ViewTitle => "Move Data (FE7)";
        public new bool IsLoaded => _vm.IsLoaded;


        public EditorDescriptor Descriptor => new("Move Data (FE7)", 1503, 523, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public EventMoveDataFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
            MoveDirectionUpDown.ValueChanged += OnDirectionChanged;        }


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
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("EventMoveDataFE7View.LoadList failed: " + ex.ToString());
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
                Log.Error("EventMoveDataFE7View.OnSelected failed: " + ex.ToString());
            }
        }

        void UpdateUI()
        {
            _suppressEvents = true;
            try
            {
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                MoveDirectionUpDown.Value = _vm.MoveDirection;
                TimeUpDown.Value = _vm.Time;
                UpdateTimeVisibility();
            }
            finally
            {
                _suppressEvents = false;
            }
        }

        /// <summary>
        /// Mirror of WinForms <c>B0_ValueChanged</c>: when the direction byte
        /// becomes an appended type (9/0xC) reveal the Time/Speed row and seed it
        /// from ROM offset+1; otherwise hide it.
        /// </summary>
        void OnDirectionChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppressEvents) return;
            try
            {
                _vm.MoveDirection = (uint)(MoveDirectionUpDown.Value ?? 0);

                if (_vm.HasTimeField)
                {
                    ROM rom = CoreState.ROM;
                    if (rom != null && _vm.CurrentAddr != 0 &&
                        _vm.CurrentAddr + 2 <= (uint)rom.Data.Length)
                    {
                        _vm.Time = rom.u8(_vm.CurrentAddr + 1);
                    }
                    else
                    {
                        _vm.Time = 0;
                    }
                    _suppressEvents = true;
                    try { TimeUpDown.Value = _vm.Time; }
                    finally { _suppressEvents = false; }
                }

                UpdateTimeVisibility();
            }
            catch (Exception ex)
            {
                Log.Error("EventMoveDataFE7View.OnDirectionChanged failed: " + ex.ToString());
            }
        }

        void UpdateTimeVisibility()
        {
            bool show = _vm.HasTimeField;
            TimeLabel.IsVisible = show;
            TimeUpDown.IsVisible = show;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit Move Data (FE7)"));
            try
            {
                _vm.MoveDirection = (uint)(MoveDirectionUpDown.Value ?? 0);
                _vm.Time = (uint)(TimeUpDown.Value ?? 0);
                _vm.Write();
                _undoService.Commit();

                // Refresh the list label for the edited command (type may have changed).
                RefreshListPreserving(_vm.CurrentAddr);
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventMoveDataFE7View.OnWrite failed: " + ex.ToString());
            }
        }

        void RefreshListPreserving(uint preserveAddr)
        {
            try
            {
                ROM rom = CoreState.ROM;
                if (rom?.RomInfo == null) return;
                uint baseAddr = EventSubEditorHelper.FindFirstMoveDataAddr(rom);
                if (baseAddr == 0) return;
                var items = EventMoveDataFE7Core.WalkCommands(rom, baseAddr);
                EntryList.SetItemsPreserveSelection(items, preserveAddr);
            }
            catch (Exception ex)
            {
                Log.Error("EventMoveDataFE7View.RefreshListPreserving failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
