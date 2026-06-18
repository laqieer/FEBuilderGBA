using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    /// <summary>
    /// Issue #1175 — item-driven Avalonia Item Effectiveness editor for the FE8U
    /// SkillSystems "特効リワーク" variant. Mirrors the WinForms
    /// <c>ItemEffectivenessSkillSystemsReworkForm</c> right pane: outer EntryList
    /// of items, an InnerList of the 4-byte Rework class-type entries the item
    /// targets, a per-entry editor (coefficient + class-type bitmask), an
    /// ItemListBox of items sharing the same effectiveness array, plus the
    /// IndependencePanel for forking shared arrays. The Rework on-ROM format
    /// (4-byte stride, u16 class-type) differs from the classic single-byte
    /// <c>ItemEffectivenessForm</c>, so writes go through the
    /// <c>ItemClassListCore.*Rework*</c> helpers with an explicit
    /// <see cref="Undo.UndoData"/> committed via
    /// <see cref="UndoService.CommitExternal"/>.
    /// </summary>
    public partial class ItemEffectivenessSkillSystemsReworkView : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly ItemEffectivenessSkillSystemsReworkViewModel _vm = new();
        readonly UndoService _undoService = new();

        readonly ObservableCollection<AddressListItem> _innerDisplay = new();
        List<AddrResult> _innerData = new();

        readonly ObservableCollection<AddressListItem> _sharedDisplay = new();
        List<AddrResult> _sharedData = new();

        bool _suppressFieldEvents;

        public string ViewTitle => "Effectiveness (Skill Systems Rework)";
        public bool IsLoaded => _vm.IsLoaded;

        /// <summary>
        /// Exposes the backing view-model for headless test access (issue #362
        /// regression tests assert <c>vm.CurrentAddr</c> matches the navigated
        /// address). Mirrors the <c>DataViewModel</c> pattern used by
        /// <see cref="ItemStatBonusesViewerView"/> and the other editor views.
        /// </summary>
        public ViewModelBase? DataViewModel => _vm;

        public ItemEffectivenessSkillSystemsReworkView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            InnerList.ItemsSource = _innerDisplay;
            ItemListBox.ItemsSource = _sharedDisplay;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                // Item-keyed list — render item icons, not class icons
                // (the list rows are item names + IDs, mirroring the WinForms
                // ItemEffectivenessSkillSystemsReworkForm outer list).
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ItemIconLoader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessSkillSystemsReworkView.LoadList failed: " + ex.ToString());
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.LoadEntry(addr);
                ReloadInnerAndShared();
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessSkillSystemsReworkView.OnSelected failed: " + ex.ToString());
            }
        }

        void ReloadInnerAndShared()
        {
            // Effective-against class-type list.
            _innerData = _vm.LoadInnerEntries();
            _innerDisplay.Clear();
            foreach (var e in _innerData)
                _innerDisplay.Add(new AddressListItem { Text = e.name });

            // Items sharing this effectiveness array.
            _sharedData = _vm.LoadSharedOwners();
            _sharedDisplay.Clear();
            for (int i = 0; i < _sharedData.Count; i++)
            {
                var bmp = ListIconLoaders.ItemIconLoader(_sharedData, i);
                _sharedDisplay.Add(new AddressListItem { Text = _sharedData[i].name, Icon = bmp });
            }
            IndependencePanel.IsVisible = _vm.HasSharedOwners;

            if (_innerDisplay.Count > 0)
            {
                InnerList.SelectedIndex = 0;
            }
            else
            {
                _vm.CurrentEntryAddr = 0;
                _vm.Coefficient = 0;
                _vm.ClassType = 0;
                _vm.ClassTypeNames = "";
                UpdateRightPanel();
            }
        }

        void InnerList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            int idx = InnerList.SelectedIndex;
            if (idx < 0 || idx >= _innerData.Count) return;
            _vm.LoadEntryFields(_innerData[idx].addr);
            UpdateRightPanel();
        }

        void ItemListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            int idx = ItemListBox.SelectedIndex;
            if (idx < 0 || idx >= _sharedData.Count) return;
            // Jump the outer list to the picked owner's effectiveness array.
            EntryList.SelectAddress(_vm.CurrentAddr);
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            SelectAddressLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
        }

        void UpdateRightPanel()
        {
            _suppressFieldEvents = true;
            try
            {
                EntryAddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentEntryAddr);
                CoefficientBox.Value = _vm.Coefficient;
                ClassTypeBox.Text = string.Format("0x{0:X04}", _vm.ClassType);
                ClassTypeNamesLabel.Text = _vm.ClassTypeNames;
            }
            finally
            {
                _suppressFieldEvents = false;
            }
        }

        void ClassType_TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (_suppressFieldEvents) return;
            if (TryParseHex(ClassTypeBox.Text, out uint v))
            {
                _vm.ClassType = v;
                // Live-decode the bitmask names so the user sees the effect.
                ClassTypeNamesLabel.Text = ClassTypeDisplay(v);
            }
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.CurrentEntryAddr == 0)
                {
                    CoreState.Services?.ShowError(R._("No effectiveness entry selected."));
                    return;
                }
                if (!TryParseHex(ClassTypeBox.Text, out uint classType))
                {
                    CoreState.Services?.ShowError(R._("Class Type must be a valid hex value."));
                    return;
                }
                _vm.Coefficient = (uint)(CoefficientBox.Value ?? 0);
                _vm.ClassType = classType;

                var undo = CoreState.Undo?.NewUndoData(this, "Edit Item Effectiveness Rework");
                if (undo == null)
                {
                    CoreState.Services?.ShowError(R._("Undo manager unavailable."));
                    return;
                }
                _vm.WriteCurrentEntry(undo);
                _undoService.CommitExternal(undo);

                _vm.MarkClean();
                ReloadInnerAndShared();
                CoreState.Services?.ShowInfo(R._("Item Effectiveness data written."));
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessSkillSystemsReworkView.Write_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Write failed: ") + ex.Message);
            }
        }

        void ListExpands_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.CurrentItemAddr == 0)
                {
                    CoreState.Services?.ShowError(R._("No item owns this effectiveness list."));
                    return;
                }
                var undo = CoreState.Undo?.NewUndoData(this, "Expand Item Effectiveness Rework");
                if (undo == null)
                {
                    CoreState.Services?.ShowError(R._("Undo manager unavailable."));
                    return;
                }
                _vm.ExpandCurrentList(undo);
                _undoService.CommitExternal(undo);
                CoreState.Services?.ShowInfo(R._("Added a new class slot."));
                // The pointer moved; re-resolve and reload from the new array.
                _vm.LoadEntry(_vm.CurrentAddr);
                ReloadInnerAndShared();
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessSkillSystemsReworkView.ListExpands_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Expand failed: ") + ex.Message);
            }
        }

        void Independence_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_vm.CurrentItemAddr == 0)
                {
                    CoreState.Services?.ShowError(R._("No item owns this effectiveness list."));
                    return;
                }
                var undo = CoreState.Undo?.NewUndoData(this, "Independence Item Effectiveness Rework");
                if (undo == null)
                {
                    CoreState.Services?.ShowError(R._("Undo manager unavailable."));
                    return;
                }
                _vm.MakeCurrentItemIndependent(undo);
                _undoService.CommitExternal(undo);
                CoreState.Services?.ShowInfo(R._("Item effectiveness array forked."));
                _vm.LoadEntry(_vm.CurrentAddr);
                ReloadInnerAndShared();
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("ItemEffectivenessSkillSystemsReworkView.Independence_Click failed: " + ex.ToString());
                CoreState.Services?.ShowError(R._("Independence failed: ") + ex.Message);
            }
        }

        static string ClassTypeDisplay(uint classType)
        {
            string raw = global::FEBuilderGBA.ItemClassListCore.GetClassTypeNames(classType);
            if (string.IsNullOrEmpty(raw)) return "";
            string[] keys = raw.Split(',');
            for (int i = 0; i < keys.Length; i++)
                keys[i] = R._(keys[i]);
            return string.Join(",", keys);
        }

        static bool TryParseHex(string? text, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("0X", StringComparison.Ordinal))
                s = s.Substring(2);
            return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
