using System;
using global::Avalonia;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    // #1598: a PathType selector picks one of the 12-byte path records, and the
    // movement nodes are loaded from p32(record+8) — NOT the record table. The
    // list stays EMPTY (and Write disabled) until a path is selected, and Write
    // only ever targets a validated movement node (record/terminator-safe).
    public partial class WorldMapPathMoveEditorView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly WorldMapPathMoveEditorViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        bool _suppressPathChange;

        public string ViewTitle => "Path Movement Editor";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Path Movement Editor", 1229, 822, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;

        public WorldMapPathMoveEditorView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            PathTypeCombo.SelectionChanged += OnPathChanged;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (!_hasLoadedList)
            {
                _hasLoadedList = true;
                LoadPaths();
            }
        }

        void LoadPaths()
        {
            try
            {
                _vm.IsLoading = true;
                _suppressPathChange = true;
                PathTypeCombo.Items.Clear();
                var paths = _vm.LoadPathList();
                foreach (var p in paths)
                    PathTypeCombo.Items.Add(new ComboBoxItem { Content = p.name });
                _suppressPathChange = false;

                if (PathTypeCombo.Items.Count > 0)
                {
                    // Triggers OnPathChanged -> SelectPath(0) -> LoadList().
                    PathTypeCombo.SelectedIndex = 0;
                }
                else
                {
                    EntryList.SetItems(new List<AddrResult>());
                }
                _vm.IsLoading = false;
                _vm.MarkClean();
                UpdateWriteEnabled();
            }
            catch (Exception ex)
            {
                _suppressPathChange = false;
                _vm.IsLoading = false;
                Log.Error($"WorldMapPathMoveEditorView.LoadPaths failed: {ex.Message}");
            }
        }

        void OnPathChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressPathChange) return;
            try
            {
                _vm.IsLoading = true;
                _vm.SelectPath(PathTypeCombo.SelectedIndex);
                LoadList();
                // SelectPath dropped any loaded node; if the new list didn't auto-load one,
                // clear the editor display so no stale node values remain on screen.
                if (!_vm.IsLoaded) ClearEditorDisplay();
                _vm.IsLoading = false;
                _vm.MarkClean();
                UpdateWriteEnabled();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error($"WorldMapPathMoveEditorView.OnPathChanged failed: {ex.Message}");
            }
        }

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                _vm.IsLoading = false;
                _vm.MarkClean();
                UpdateWriteEnabled();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error($"WorldMapPathMoveEditorView.LoadList failed: {ex.Message}");
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
                _vm.IsLoading = false;
                _vm.MarkClean();
                UpdateWriteEnabled();
            }
            catch (Exception ex)
            {
                _vm.IsLoading = false;
                Log.Error($"WorldMapPathMoveEditorView.OnSelected failed: {ex.Message}");
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            ElapsedTimeBox.Value = _vm.ElapsedTime;
            CoordinateXBox.Value = _vm.CoordinateX;
            CoordinateYBox.Value = _vm.CoordinateY;
        }

        // Blank the editor fields when no node is loaded (e.g. after switching to a
        // path whose movement list is empty) so stale node values never linger.
        void ClearEditorDisplay()
        {
            AddrLabel.Text = "";
            ElapsedTimeBox.Value = 0;
            CoordinateXBox.Value = 0;
            CoordinateYBox.Value = 0;
        }

        void UpdateWriteEnabled()
        {
            WriteButton.IsEnabled = _vm.CanWrite;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _vm.ElapsedTime = (uint)(ElapsedTimeBox.Value ?? 0);
            _vm.CoordinateX = (uint)(CoordinateXBox.Value ?? 0);
            _vm.CoordinateY = (uint)(CoordinateYBox.Value ?? 0);

            _undoService.Begin("Edit Path Movement");
            try
            {
                string err = _vm.Write();
                if (!string.IsNullOrEmpty(err))
                {
                    // Validate-before-mutate Core path already wrote nothing.
                    _undoService.Rollback();
                    CoreState.Services?.ShowError(err);
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Path movement data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"WorldMapPathMoveEditorView.Write failed: {ex.Message}");
            }
        }

        // Mirrors WF JumpTo(pathid): select the path by id (record tag), then the
        // first movement node — NOT EntryList.SelectAddress(pathId).
        public void NavigateTo(uint pathId)
        {
            try
            {
                // NavigateTo may be called immediately after Window.Show(), BEFORE the
                // Opened handler runs LoadPaths() (DesktopNavigationService.Navigate). If the
                // combo isn't populated yet, load the path list first so the jump resolves.
                if (PathTypeCombo.Items.Count == 0 || _vm.PathList.Count == 0)
                    LoadPaths();

                int index = -1;
                for (int i = 0; i < _vm.PathList.Count; i++)
                {
                    if (_vm.PathList[i].tag == pathId) { index = i; break; }
                }
                if (index < 0 && pathId < (uint)PathTypeCombo.Items.Count)
                    index = (int)pathId; // fall back to positional index

                if (index >= 0 && index < PathTypeCombo.Items.Count)
                {
                    PathTypeCombo.SelectedIndex = index; // loads that path's nodes
                    EntryList.SelectFirst();
                }
            }
            catch (Exception ex)
            {
                Log.Error($"WorldMapPathMoveEditorView.NavigateTo failed: {ex.Message}");
            }
        }

        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
