using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventForceSortieFE7View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventForceSortieFE7ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Force Sortie (FE7)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventForceSortieFE7View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            SubEntryList.SelectedAddressChanged += OnSubSelected;
            WriteButton.Click += OnWrite;
            Opened += (_, _) => LoadList();
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
                Log.Error($"EventForceSortieFE7View.LoadList failed: {ex}");
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                UpdateUI();
                LoadSubList();
            }
            catch (Exception ex)
            {
                Log.Error($"EventForceSortieFE7View.OnSelected failed: {ex}");
            }
        }

        // Rebuild the inner unit-list from the outer entry's D0 pointer. When
        // D0 is invalid / the list is empty we explicitly reset the sub-entry
        // (SubAddr=0 + zero fields) and clear the sub-fields UI so a stale
        // sub-entry from a previous outer selection cannot be written — we do
        // NOT rely on SetItemsWithIcons' SelectFirst() firing a fresh
        // SelectionChanged (it may not when the list was already at index 0).
        // Copilot CLI plan-review #2.
        void LoadSubList()
        {
            try
            {
                var sub = _vm.LoadSubList();
                if (sub.Count == 0)
                {
                    SubEntryList.SetItems(sub);
                    _vm.ResetSubEntry();
                    UpdateSubUI();
                    return;
                }

                SubEntryList.SetItemsWithIcons(sub, i => ListIconLoaders.UnitPortraitByIdLoader(sub, i));
                // Explicitly load the first sub-row rather than depending on the
                // selection-changed event firing.
                _vm.LoadSubEntry(sub[0].addr);
                UpdateSubUI();
            }
            catch (Exception ex)
            {
                Log.Error($"EventForceSortieFE7View.LoadSubList failed: {ex}");
            }
        }

        void OnSubSelected(uint addr)
        {
            try
            {
                _vm.LoadSubEntry(addr);
                UpdateSubUI();
            }
            catch (Exception ex)
            {
                Log.Error($"EventForceSortieFE7View.OnSubSelected failed: {ex}");
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            UnitListPointerUpDown.Value = _vm.UnitListPointer;
        }

        void UpdateSubUI()
        {
            UnitIdUpDown.Value = _vm.UnitId;
            Unknown1UpDown.Value = _vm.Unknown1;
            Unknown2UpDown.Value = _vm.Unknown2;
            Unknown3UpDown.Value = _vm.Unknown3;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            // Wrap BOTH ROM writes (outer D0 pointer + inner sub-entry) in a
            // single UndoService scope so Edit > Undo reverts them together —
            // _vm.Write()/_vm.WriteSubEntry() funnel into the bare
            // EditorFormRef.WriteFields overload, which only records undo when an
            // ambient ROM.BeginUndoScope is active (#1427 sibling fix). Both
            // calls live in the same try block so a fault rolls both back.
            // Runs on the UI thread (button Click), so the ambient undo is
            // non-null.
            _undoService.Begin("Edit Force Sortie FE7");
            try
            {
                _vm.UnitListPointer = (uint)(UnitListPointerUpDown.Value ?? 0);
                _vm.UnitId = (uint)(UnitIdUpDown.Value ?? 0);
                _vm.Unknown1 = (uint)(Unknown1UpDown.Value ?? 0);
                _vm.Unknown2 = (uint)(Unknown2UpDown.Value ?? 0);
                _vm.Unknown3 = (uint)(Unknown3UpDown.Value ?? 0);
                _vm.Write();
                _vm.WriteSubEntry();
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"EventForceSortieFE7View.OnWrite failed: {ex}");
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
