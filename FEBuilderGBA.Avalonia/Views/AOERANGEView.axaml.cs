using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Interactivity;
using global::Avalonia.VisualTree;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AOERANGEView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly AOERANGEViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        bool _suppress;
        UniformGrid? _cellPanel; // cached ItemsControl panel (resolved once).

        public string ViewTitle => "Area of Effect Range";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Area of Effect Range", 720, 560, SizeToContent: false);
        public event EventHandler? CloseRequested;

        public AOERANGEView()
        {
            InitializeComponent();
            DataContext = _vm;

            ReloadButton.Click += OnReload;
            WriteButton.Click += OnWrite;

            // Header changes resize the grid (preserving overlap) + move the center.
            WidthBox.ValueChanged += OnSizeChanged;
            HeightBox.ValueChanged += OnSizeChanged;
            CenterXBox.ValueChanged += OnCenterChanged;
            CenterYBox.ValueChanged += OnCenterChanged;

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
                Log.Error($"AOERANGEView.LoadAddress failed: {ex}");
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        /// <summary>Push VM header values into the spinners + size the grid panel.</summary>
        void SyncFromVm()
        {
            _suppress = true;
            try
            {
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                ReadStartBox.Value = _vm.CurrentAddr;
                WidthBox.Value = _vm.Width;
                HeightBox.Value = _vm.Height;
                CenterXBox.Value = _vm.CenterX;
                CenterYBox.Value = _vm.CenterY;
                ApplyGridColumns();
            }
            finally
            {
                _suppress = false;
            }
        }

        /// <summary>Lay the UniformGrid out with exactly Width columns (WinForms rows).</summary>
        void ApplyGridColumns()
        {
            UniformGrid? panel = ResolveUniformGrid();
            if (panel != null)
            {
                panel.Columns = (int)Math.Max(1, _vm.Width);
            }
        }

        /// <summary>
        /// Resolve the grid's <see cref="UniformGrid"/> ItemsPanel ONCE and cache it.
        /// The panel is a single, stable instance for the ItemsControl's lifetime, so
        /// caching avoids re-walking the whole cell visual tree (O(cells)) on every
        /// header change — which would stall the UI for a large W×H grid.
        /// </summary>
        UniformGrid? ResolveUniformGrid()
        {
            if (_cellPanel != null) return _cellPanel;
            foreach (var d in CellGrid.GetVisualDescendants())
            {
                if (d is UniformGrid ug) { _cellPanel = ug; break; }
            }
            return _cellPanel;
        }

        // ------------------------------------------------------------------
        // Header edits
        // ------------------------------------------------------------------

        void OnSizeChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppress) return;
            int oldW = (int)_vm.Width;
            int oldH = (int)_vm.Height;
            _vm.Width = (uint)(WidthBox.Value ?? 0);
            _vm.Height = (uint)(HeightBox.Value ?? 0);
            _vm.ResizeGridPreserving(oldW, oldH);
            ApplyGridColumns();
        }

        void OnCenterChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (_suppress) return;
            _vm.CenterX = (uint)(CenterXBox.Value ?? 0);
            _vm.CenterY = (uint)(CenterYBox.Value ?? 0);
            _vm.UpdateCenterMark();
        }

        // ------------------------------------------------------------------
        // Write
        // ------------------------------------------------------------------

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit AOE Range");
            try
            {
                _vm.Width = (uint)(WidthBox.Value ?? 0);
                _vm.Height = (uint)(HeightBox.Value ?? 0);
                _vm.CenterX = (uint)(CenterXBox.Value ?? 0);
                _vm.CenterY = (uint)(CenterYBox.Value ?? 0);

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
                Log.Error($"AOERANGEView.Write failed: {ex}");
            }
        }

        // ------------------------------------------------------------------
        // IEditorView
        // ------------------------------------------------------------------

        /// <summary>
        /// Navigate to a record at <paramref name="address"/> (the standalone /
        /// manual path; no parent pointer slot to repoint on a move).
        /// </summary>
        public void NavigateTo(uint address) => NavigateFromParent(address, 0);

        /// <summary>
        /// Navigate to a record from a PARENT editor, supplying the ROM offset of the
        /// pointer slot that references it so a grow/move can repoint that slot
        /// (ports the WinForms AOERANGEPOINTER <c>JumpTo(parentNumnic, addr)</c> path).
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
