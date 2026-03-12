using System;
using System.Collections.Generic;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventCondView : Window, IEditorView, IDataVerifiableView
    {
        readonly EventCondViewModel _vm = new();
        readonly UndoService _undoService = new();
        bool _suppressSlotChange;
        bool _suppressRecordChange;

        public string ViewTitle => "Event Condition Editor";
        public bool IsLoaded => _vm.CanWrite;
        public ViewModelBase? DataViewModel => _vm;

        public EventCondView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnMapSelected;
            SlotCombo.SelectionChanged += OnSlotChanged;
            RecordList.SelectionChanged += OnRecordSelected;
            Opened += (_, _) => LoadAll();
        }

        void LoadAll()
        {
            _vm.IsLoading = true;
            try
            {
                // Ensure slot defs are loaded
                if (EventCondViewModel.SlotDefs.Count == 0)
                    EventCondViewModel.LoadSlotDefs();

                // Populate slot combo
                _suppressSlotChange = true;
                SlotCombo.Items.Clear();
                foreach (var def in EventCondViewModel.SlotDefs)
                {
                    SlotCombo.Items.Add($"{def.Category}: {def.Name}");
                }
                if (SlotCombo.Items.Count > 0)
                    SlotCombo.SelectedIndex = 0;
                _suppressSlotChange = false;

                // Populate map list
                var maps = _vm.LoadMapList();
                EntryList.SetItems(maps);
            }
            catch (Exception ex)
            {
                Log.Error("EventCondView.LoadAll failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnMapSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                bool ok = _vm.ResolveEventDataAddr(addr);
                if (ok)
                {
                    ReloadRecordList();
                }
                else
                {
                    ClearRecordList();
                    ClearEditor();
                    SlotInfoLabel.Text = "No event data for this map.";
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventCondView.OnMapSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSlotChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressSlotChange) return;
            _vm.IsLoading = true;
            try
            {
                ReloadRecordList();
            }
            catch (Exception ex)
            {
                Log.Error("EventCondView.OnSlotChanged failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void ReloadRecordList()
        {
            int slotIndex = SlotCombo.SelectedIndex;
            if (slotIndex < 0 || _vm.EventDataAddr == 0)
            {
                ClearRecordList();
                ClearEditor();
                return;
            }

            var records = _vm.LoadConditionRecords(slotIndex);
            _suppressRecordChange = true;
            RecordList.Items.Clear();
            foreach (var r in records)
            {
                var item = new ListBoxItem { Content = r.name, Tag = r.addr };
                RecordList.Items.Add(item);
            }
            _suppressRecordChange = false;

            SlotInfoLabel.Text = _vm.SlotInfo;

            if (RecordList.Items.Count > 0)
            {
                RecordList.SelectedIndex = 0;
            }
            else
            {
                ClearEditor();
            }
        }

        void ClearRecordList()
        {
            _suppressRecordChange = true;
            RecordList.Items.Clear();
            _suppressRecordChange = false;
        }

        void OnRecordSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (_suppressRecordChange) return;
            _vm.IsLoading = true;
            try
            {
                if (RecordList.SelectedItem is ListBoxItem item && item.Tag is uint addr)
                {
                    _vm.LoadCondRecord(addr);
                    UpdateEditorUI();
                }
            }
            catch (Exception ex)
            {
                Log.Error("EventCondView.OnRecordSelected failed: {0}", ex.Message);
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateEditorUI()
        {
            AddrLabel.Text = $"0x{_vm.CondRecordAddr:X08}";
            RecordSizeLabel.Text = $"{_vm.CondRecordSize} bytes";
            CondTypeNameLabel.Text = _vm.CondTypeName;

            // Update field labels
            var labels = _vm.GetFieldLabels();
            LblB0.Text = labels.B0;
            LblB1.Text = labels.B1;
            LblB2B3.Text = labels.B2B3;
            LblB4B7.Text = labels.B4B7;
            LblB8.Text = labels.B8;
            LblB9.Text = labels.B9;
            LblB10.Text = labels.B10;
            LblB11.Text = labels.B11;

            if (_vm.IsPointerSlot)
            {
                // For pointer-only slots, only show the event pointer field
                EventPtrBox.Value = _vm.EventPtr;
                CondTypeBox.Value = 0;
                SubTypeBox.Value = 0;
                FlagIdBox.Value = 0;
                ExtraB8Box.Value = 0;
                ExtraB9Box.Value = 0;
                ExtraB10Box.Value = 0;
                ExtraB11Box.Value = 0;

                SetFieldVisibility(false, false, false, true, false, false, false, false);
                SetFE7FieldsVisible(false);
            }
            else
            {
                CondTypeBox.Value = _vm.CondType;
                SubTypeBox.Value = _vm.SubType;
                FlagIdBox.Value = _vm.FlagId;
                EventPtrBox.Value = _vm.EventPtr;
                ExtraB8Box.Value = _vm.ExtraB8;
                ExtraB9Box.Value = _vm.ExtraB9;
                ExtraB10Box.Value = _vm.ExtraB10;
                ExtraB11Box.Value = _vm.ExtraB11;

                if (_vm.CondRecordSize <= 6)
                {
                    // TRAP: 6-byte records — show type, X(B1), Y(B2/FlagId), subtype(B3/EventPtr), B4, B5
                    FlagIdBox.Maximum = 255;
                    EventPtrBox.Maximum = 255;
                    FlagIdBox.FormatString = "X2";
                    EventPtrBox.FormatString = "X2";
                    SetFieldVisibility(true, true, true, true, true, true, false, false);
                }
                else
                {
                    FlagIdBox.Maximum = 65535;
                    EventPtrBox.Maximum = 4294967295;
                    FlagIdBox.FormatString = "X4";
                    EventPtrBox.FormatString = "X8";
                    bool show12 = _vm.CondRecordSize >= 12;
                    SetFieldVisibility(true, true, true, true, show12, show12, show12, show12);
                }

                bool showFE7 = _vm.IsFE7Extended;
                SetFE7FieldsVisible(showFE7);
                if (showFE7)
                {
                    ExtraB12Box.Value = _vm.ExtraB12;
                    ExtraB13Box.Value = _vm.ExtraB13;
                    ExtraB14Box.Value = _vm.ExtraB14;
                    ExtraB15Box.Value = _vm.ExtraB15;
                }
            }

            // Name hints
            UpdateNameHints();

            // Raw hex dump
            UpdateRawHex();
        }

        void SetFieldVisibility(bool b0, bool b1, bool b2b3, bool b4b7, bool b8, bool b9, bool b10, bool b11)
        {
            CondTypeBox.IsVisible = b0;     LblB0.IsVisible = b0;
            SubTypeBox.IsVisible = b1;      LblB1.IsVisible = b1;
            FlagIdBox.IsVisible = b2b3;     LblB2B3.IsVisible = b2b3;
            EventPtrBox.IsVisible = b4b7;   LblB4B7.IsVisible = b4b7;
            ExtraB8Box.IsVisible = b8;      LblB8.IsVisible = b8;
            ExtraB9Box.IsVisible = b9;      LblB9.IsVisible = b9;
            ExtraB10Box.IsVisible = b10;    LblB10.IsVisible = b10;
            ExtraB11Box.IsVisible = b11;    LblB11.IsVisible = b11;
        }

        void SetFE7FieldsVisible(bool visible)
        {
            ExtraB12Box.IsVisible = visible; LblB12.IsVisible = visible;
            ExtraB13Box.IsVisible = visible; LblB13.IsVisible = visible;
            ExtraB14Box.IsVisible = visible; LblB14.IsVisible = visible;
            ExtraB15Box.IsVisible = visible; LblB15.IsVisible = visible;
        }

        void UpdateNameHints()
        {
            if (_vm.IsPointerSlot)
            {
                uint ptr = _vm.EventPtr;
                if (U.isPointer(ptr))
                    NameHintLabel.Text = $"Points to: 0x{U.toOffset(ptr):X06}";
                else if (ptr == 0)
                    NameHintLabel.Text = "(null pointer)";
                else
                    NameHintLabel.Text = $"Raw value: 0x{ptr:X08}";
                return;
            }

            int slotIdx = _vm.SelectedSlotIndex;
            if (slotIdx < 0 || slotIdx >= EventCondViewModel.SlotDefs.Count)
            {
                NameHintLabel.Text = "";
                return;
            }

            var cat = EventCondViewModel.SlotDefs[slotIdx].Category;
            var hints = new System.Text.StringBuilder();

            if (cat == CondCategory.TALK)
            {
                string u1 = NameResolver.GetUnitName(_vm.ExtraB8);
                string u2 = NameResolver.GetUnitName(_vm.ExtraB9);
                hints.AppendLine($"Unit 1: {u1}");
                hints.AppendLine($"Unit 2: {u2}");
            }
            else if (cat == CondCategory.TURN)
            {
                uint phase = _vm.ExtraB10;
                string phaseName = phase switch
                {
                    0x00 => "Player Phase",
                    0x40 => "Ally Phase",
                    0x80 => "Enemy Phase",
                    0xC0 => "4th Allegiance Phase",
                    _ => $"Phase 0x{phase:X02}",
                };
                hints.AppendLine($"Turn {_vm.ExtraB8} - {_vm.ExtraB9}, {phaseName}");
            }
            else if (cat == CondCategory.OBJECT)
            {
                hints.AppendLine($"Position: ({_vm.ExtraB8}, {_vm.ExtraB9})");
                hints.AppendLine($"Object sub-type: 0x{_vm.ExtraB10:X02}");
            }

            if (U.isPointer(_vm.EventPtr))
                hints.AppendLine($"Event at: 0x{U.toOffset(_vm.EventPtr):X06}");

            NameHintLabel.Text = hints.ToString().TrimEnd();
        }

        void UpdateRawHex()
        {
            ROM rom = CoreState.ROM;
            if (rom == null || _vm.CondRecordAddr == 0)
            {
                RawHexLabel.Text = "";
                return;
            }

            uint addr = _vm.CondRecordAddr;
            uint size = _vm.IsPointerSlot ? 4 : _vm.CondRecordSize;
            if (addr + size > (uint)rom.Data.Length)
            {
                RawHexLabel.Text = "(out of range)";
                return;
            }

            var sb = new System.Text.StringBuilder();
            for (uint i = 0; i < size; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(rom.u8(addr + i).ToString("X02"));
            }
            RawHexLabel.Text = sb.ToString();
        }

        void ClearEditor()
        {
            _vm.CanWrite = false;
            AddrLabel.Text = "";
            RecordSizeLabel.Text = "";
            CondTypeNameLabel.Text = "";
            NameHintLabel.Text = "";
            RawHexLabel.Text = "";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _undoService.Begin("Edit Event Condition");
            try
            {
                if (_vm.IsPointerSlot)
                {
                    _vm.EventPtr = (uint)(EventPtrBox.Value ?? 0);
                }
                else
                {
                    _vm.CondType = (uint)(CondTypeBox.Value ?? 0);
                    _vm.SubType = (uint)(SubTypeBox.Value ?? 0);
                    _vm.FlagId = (uint)(FlagIdBox.Value ?? 0);
                    _vm.EventPtr = (uint)(EventPtrBox.Value ?? 0);
                    _vm.ExtraB8 = (uint)(ExtraB8Box.Value ?? 0);
                    _vm.ExtraB9 = (uint)(ExtraB9Box.Value ?? 0);
                    _vm.ExtraB10 = (uint)(ExtraB10Box.Value ?? 0);
                    _vm.ExtraB11 = (uint)(ExtraB11Box.Value ?? 0);

                    if (_vm.IsFE7Extended)
                    {
                        _vm.ExtraB12 = (uint)(ExtraB12Box.Value ?? 0);
                        _vm.ExtraB13 = (uint)(ExtraB13Box.Value ?? 0);
                        _vm.ExtraB14 = (uint)(ExtraB14Box.Value ?? 0);
                        _vm.ExtraB15 = (uint)(ExtraB15Box.Value ?? 0);
                    }
                }

                _vm.WriteCondRecord();
                _undoService.Commit();
                _vm.MarkClean();

                // Refresh display
                UpdateRawHex();
                UpdateNameHints();
                CondTypeNameLabel.Text = EventCondViewModel.GetCondTypeName((byte)_vm.CondType);

                CoreState.Services.ShowInfo("Event condition data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventCondView.Write_Click failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address)
        {
            EntryList.SelectAddress(address);
        }

        public void SelectFirstItem()
        {
            EntryList.SelectFirst();
        }
    }
}
