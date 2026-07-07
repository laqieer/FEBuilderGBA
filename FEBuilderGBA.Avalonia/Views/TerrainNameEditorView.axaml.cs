using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class TerrainNameEditorView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly TerrainNameEditorViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Terrain Name Editor";
        public new bool IsLoaded => _vm.CanWrite;

        public EditorDescriptor Descriptor => new("Terrain Name Editor", 1253, 790, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public ViewModelBase? DataViewModel => _vm;


        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
        public TerrainNameEditorView()
        {
            InitializeComponent();
            TerrainList.SelectedAddressChanged += OnTerrainSelected;
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
                var items = _vm.LoadTerrainNameList();
                TerrainList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.ErrorF("TerrainNameEditorView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnTerrainSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadTerrainName(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.ErrorF("TerrainNameEditorView.OnTerrainSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        public void NavigateTo(uint address)
        {
            TerrainList.SelectAddress(address);
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";

            bool multibyte = _vm.IsMultibyte;

            // Non-multibyte (US/EU): Text ID NumericUpDown + decoded preview.
            TextIdLabel.IsVisible = !multibyte;
            TextIdBox.IsVisible = !multibyte;
            NamePreviewLabel.IsVisible = !multibyte;
            NameLabel.IsVisible = !multibyte;

            // Multibyte (JP): editable raw-string TextBox.
            NameEditLabel.IsVisible = multibyte;
            NameBox.IsVisible = multibyte;

            if (multibyte)
            {
                NameBox.Text = _vm.TerrainName;
            }
            else
            {
                TextIdBox.Value = _vm.TextId;
                NameLabel.Text = _vm.TerrainName;
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Edit Terrain Name");
            try
            {
                if (_vm.IsMultibyte)
                    _vm.TerrainName = NameBox.Text ?? "";
                else
                    _vm.TextId = (uint)(TextIdBox.Value ?? 0);

                _vm.WriteTerrainName();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Terrain name written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.ErrorF("TerrainNameEditorView.Write: {0}", ex.Message); }
        }

        public void SelectFirstItem()
        {
            TerrainList.SelectFirst();
        }
    }
}
