using global::Avalonia;
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Special OAM editor (#1179) — Avalonia port of WinForms <c>OAMSPForm</c>.
    /// A READ-ONLY discovery + hex-inspection tool: the LDR-scanned list of
    /// special-OAM sprite-assembly entries on the left, and a hex dump of the
    /// selected entry's pointer array + OAM12 sub-blocks on the right (matching
    /// WF, which renders no sprite image for these entries). Selection is
    /// read-only and must not mark the editor dirty.
    /// </summary>
    public partial class OAMSPView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly OAMSPViewModel _vm = new();
        bool _hasLoadedList;

        public string ViewTitle => "Special OAM";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Special OAM", 860, 560);
        public event EventHandler? CloseRequested;
        public object? DialogResult { get; private set; }
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public OAMSPView()
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
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("OAMSPView.LoadList failed: " + ex.ToString());
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
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("OAMSPView.OnSelected failed: " + ex.ToString());
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
            NameLabel.Text = _vm.EntryName;
            LengthLabel.Text = _vm.EntryLength;
            Oam12CountLabel.Text = _vm.Oam12Count;
            DetailBox.Text = _vm.DetailText;
        }

        void Close_Click(object? sender, RoutedEventArgs e) { DialogResult = null; RequestClose(); }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
