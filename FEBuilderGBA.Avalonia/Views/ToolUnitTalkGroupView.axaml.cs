using System;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolUnitTalkGroupView : TranslatedUserControl, IEmbeddableEditor
    {
        readonly ToolUnitTalkGroupViewModel _vm = new();
        bool _hasLoadedList;

        public string ViewTitle => "Unit Talk Group";
        public new bool IsLoaded => _vm.IsLoaded;
        public EditorDescriptor Descriptor => new("Unit Talk Group", 820, 540, SizeToContent: false);
        public event EventHandler? CloseRequested;
        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);

        public ToolUnitTalkGroupView()
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
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
                if (items.Count == 0)
                {
                    _vm.IsLoaded = true;
                    // Distinguish "no ROM" (missing data) from FE6/unsupported (version limit).
                    DetailText.Text = CoreState.ROM?.RomInfo == null
                        ? R._("No ROM is loaded.")
                        : R._("Talk groups are available on FE7 / FE8 only.");
                }
            }
            catch (Exception ex)
            {
                Log.Error("ToolUnitTalkGroupView.LoadList failed: " + ex);
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                DetailText.Text = string.IsNullOrEmpty(_vm.Detail)
                    ? R._("(no units in this talk group)")
                    : _vm.Detail;
            }
            catch (Exception ex)
            {
                Log.Error("ToolUnitTalkGroupView.OnSelected failed: " + ex);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
