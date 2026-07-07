using System;
using global::Avalonia;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class CStringView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly CStringViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        bool _suppress;

        public string ViewTitle => "C-String Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("C-String Editor", 520, 320, SizeToContent: false);
        public event EventHandler? CloseRequested;

        public CStringView()
        {
            InitializeComponent();
            DataContext = _vm;

            ReloadButton.Click += OnReload;
            WriteButton.Click += OnWrite;

        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                SyncFromVm();
            }
        }

        // ------------------------------------------------------------------
        // Loading
        // ------------------------------------------------------------------

        void OnReload(object? sender, RoutedEventArgs e)
        {
            uint addr = (uint)(ReadStartBox.Value ?? 0);
            LoadAddress(addr);
        }

        void LoadAddress(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                SyncFromVm();
            }
            catch (Exception ex)
            {
                Log.Error($"CStringView.LoadAddress failed: {ex}");
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        /// <summary>Push VM values into the address box + hex label.</summary>
        void SyncFromVm()
        {
            _suppress = true;
            try
            {
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                ReadStartBox.Value = _vm.CurrentAddr;
            }
            finally
            {
                _suppress = false;
            }
        }

        // ------------------------------------------------------------------
        // Write
        // ------------------------------------------------------------------

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            _undoService.Begin("Edit C-String");
            try
            {
                bool changed = _vm.Write();
                if (changed)
                {
                    _undoService.Commit();
                    _vm.MarkClean();
                    SyncFromVm(); // a move updates CurrentAddr.
                }
                else
                {
                    _undoService.Rollback();
                }
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"CStringView.Write failed: {ex}");
            }
        }

        // ------------------------------------------------------------------
        // IEditorView
        // ------------------------------------------------------------------

        /// <summary>
        /// Navigate to the C-string at <paramref name="address"/> (the standalone /
        /// manual path; no parent pointer slot to repoint on a move).
        /// </summary>
        public void NavigateTo(uint address) => NavigateFromParent(address, 0);

        /// <summary>
        /// Navigate to a C-string from a PARENT editor (a CSTRING/TEXT field),
        /// supplying the ROM offset of the pointer slot that references it so a
        /// grow/move can repoint that slot (ports the WinForms
        /// <c>CStringForm.Init(NumericUpDown)</c> parent-bound path).
        /// </summary>
        public void NavigateFromParent(uint address, uint parentPointerSlot)
        {
            // Set the parent slot WITHIN the load's IsLoading window so its
            // assignment does not mark the freshly-loaded VM dirty (LoadAddress
            // ends with MarkClean()). ParentPointerSlot is metadata, not user input.
            _vm.IsLoading = true;
            try { _vm.ParentPointerSlot = parentPointerSlot; }
            finally { _vm.IsLoading = false; }
            LoadAddress(address);
        }

        public void SelectFirstItem() { /* no list — manual address entry */ }
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
