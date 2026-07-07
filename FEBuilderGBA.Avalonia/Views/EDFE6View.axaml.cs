using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EDFE6View : TranslatedUserControl, IEmbeddableEditor
    {
        readonly EDFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _hasLoadedList;
        bool _loading;

        public string ViewTitle => "Ending (FE6)";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Ending (FE6)", 1204, 885, SizeToContent: global::Avalonia.Controls.SizeToContent.WidthAndHeight);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public EDFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;

            // Live text preview as the user edits each text-ID (no ROM write until "Write").
            Text0IdBox.ValueChanged += (_, _) => OnIdEdited(Text0IdBox, id => _vm.Text0Id = id, Text0Preview, () => _vm.Text0Preview);
            Text2IdBox.ValueChanged += (_, _) => OnIdEdited(Text2IdBox, id => _vm.Text2Id = id, Text2Preview, () => _vm.Text2Preview);
            Text4IdBox.ValueChanged += (_, _) => OnIdEdited(Text4IdBox, id => _vm.Text4Id = id, Text4Preview, () => _vm.Text4Preview);
            Text6IdBox.ValueChanged += (_, _) => OnIdEdited(Text6IdBox, id => _vm.Text6Id = id, Text6Preview, () => _vm.Text6Preview);

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
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitByIdLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("EDFE6View.LoadList failed: " + ex.ToString());
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
                Log.Error("EDFE6View.OnSelected failed: " + ex.ToString());
            }
        }

        void OnIdEdited(NumericUpDown box, Action<uint> setId, TextBlock preview, Func<string> getPreview)
        {
            if (_loading) return;
            setId((uint)(box.Value ?? 0));
            preview.Text = getPreview();
        }

        void UpdateUI()
        {
            _loading = true;
            try
            {
                AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
                Text0IdBox.Value = _vm.Text0Id;
                Text2IdBox.Value = _vm.Text2Id;
                Text4IdBox.Value = _vm.Text4Id;
                Text6IdBox.Value = _vm.Text6Id;
                Text0Preview.Text = _vm.Text0Preview;
                Text2Preview.Text = _vm.Text2Preview;
                Text4Preview.Text = _vm.Text4Preview;
                Text6Preview.Text = _vm.Text6Preview;
            }
            finally
            {
                _loading = false;
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _vm.Text0Id = (uint)(Text0IdBox.Value ?? 0);
            _vm.Text2Id = (uint)(Text2IdBox.Value ?? 0);
            _vm.Text4Id = (uint)(Text4IdBox.Value ?? 0);
            _vm.Text6Id = (uint)(Text6IdBox.Value ?? 0);

            _undoService.Begin(R._("Edit Ending Text"));
            try
            {
                _vm.WriteEntry();
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EDFE6View.Write_Click failed: " + ex.ToString());
            }

            UpdateUI();
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
