using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventForceSortieView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly EventForceSortieViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Force Sortie Editor";
        public new bool IsLoaded => _vm.IsLoaded;


        public EditorDescriptor Descriptor => new("Force Sortie Editor", 1185, 778, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);

        public event EventHandler? CloseRequested;
        public EventForceSortieView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;        }


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
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error($"EventForceSortieView.LoadList failed: {ex.Message}");
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
                Log.Error($"EventForceSortieView.OnSelected failed: {ex.Message}");
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            UnitUpDown.Value = _vm.Unit;
            SquadUpDown.Value = _vm.Squad;
            ChapterIdUpDown.Value = _vm.ChapterId;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            // Wrap the ROM write in an UndoService scope so Edit > Undo can
            // revert it — _vm.Write() funnels into the bare
            // EditorFormRef.WriteFields overload, which only records undo when
            // an ambient ROM.BeginUndoScope is active (#1427). Mirrors the
            // sibling EventHaikuView/MapExitPointView Begin/Commit/Rollback
            // pattern. Runs on the UI thread (button Click), so the ambient
            // undo is non-null.
            _undoService.Begin("Edit Force Sortie");
            try
            {
                _vm.Unit = (uint)(UnitUpDown.Value ?? 0);
                _vm.Squad = (uint)(SquadUpDown.Value ?? 0);
                _vm.ChapterId = (uint)(ChapterIdUpDown.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"EventForceSortieView.OnWrite failed: {ex.Message}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
