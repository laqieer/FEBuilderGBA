// SPDX-License-Identifier: GPL-3.0-or-later
// WorldMapImageView code-behind — gap-sweep #395 parity raise.
//
// Wires the six Avalonia tabs (Main / Event / Mini / PointIcon / Border /
// IconData) to the WorldMapImageViewModel.  Three distinct UndoService
// scopes:
//   * "Write World Map Pointers" — the top WriteAll button persisting all
//     13 canonical pointer slots in one transaction (Copilot CLI plan
//     review v1->v2 finding C1).
//   * "Write World Map Border" — per-record Border write.
//   * "Write World Map Icon"   — per-record Icon write.
//
// A single top-level Undo button is present (Copilot CLI plan review C3 —
// WinForms has no per-tab Undo; we don't introduce them either).
using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class WorldMapImageView : TranslatedWindow, IEditorView
    {
        readonly WorldMapImageViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "World Map Image";
        public bool IsLoaded => _vm.IsLoaded;

        public WorldMapImageView()
        {
            InitializeComponent();
            Border_EntryList.SelectedAddressChanged += OnBorderSelected;
            IconData_EntryList.SelectedAddressChanged += OnIconSelected;
            Opened += (_, _) => LoadAll();
        }

        // ===================================================================
        // Common load entry-point
        // ===================================================================

        void LoadAll()
        {
            try
            {
                _vm.LoadAll();
                RefreshTopRowNuds();
                LoadBorderList();
                LoadIconList();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageView.LoadAll failed: {0}", ex.Message);
            }
        }

        void RefreshTopRowNuds()
        {
            MainImageBox.Value = _vm.MainImagePtr;
            MainPaletteBox.Value = _vm.MainPalettePtr;
            MainDarkPaletteBox.Value = _vm.MainDarkPalettePtr;
            MainPaletteMapBox.Value = _vm.MainPaletteMapPtr;
            EventImageBox.Value = _vm.EventImagePtr;
            EventPaletteBox.Value = _vm.EventPalettePtr;
            EventTsaBox.Value = _vm.EventTsaPtr;
            MiniImageBox.Value = _vm.MiniImagePtr;
            MiniPaletteBox.Value = _vm.MiniPalettePtr;
            Point1ImageBox.Value = _vm.Point1ImagePtr;
            Point2ImageBox.Value = _vm.Point2ImagePtr;
            RoadImageBox.Value = _vm.RoadImagePtr;
            IconPaletteBox.Value = _vm.IconPalettePtr;
        }

        void ReadNudsIntoVm()
        {
            _vm.MainImagePtr = NudU32(MainImageBox);
            _vm.MainPalettePtr = NudU32(MainPaletteBox);
            _vm.MainDarkPalettePtr = NudU32(MainDarkPaletteBox);
            _vm.MainPaletteMapPtr = NudU32(MainPaletteMapBox);
            _vm.EventImagePtr = NudU32(EventImageBox);
            _vm.EventPalettePtr = NudU32(EventPaletteBox);
            _vm.EventTsaPtr = NudU32(EventTsaBox);
            _vm.MiniImagePtr = NudU32(MiniImageBox);
            _vm.MiniPalettePtr = NudU32(MiniPaletteBox);
            _vm.Point1ImagePtr = NudU32(Point1ImageBox);
            _vm.Point2ImagePtr = NudU32(Point2ImageBox);
            _vm.RoadImagePtr = NudU32(RoadImageBox);
            _vm.IconPalettePtr = NudU32(IconPaletteBox);
        }

        // ===================================================================
        // Top WriteAll button — all 13 canonical pointer slots
        // ===================================================================

        void WriteAll_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Write World Map Pointers");
            try
            {
                ReadNudsIntoVm();
                bool ok = _vm.WriteAllPointers();
                if (!ok)
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapImageView.WriteAll failed: {0}", ex.Message);
            }
        }

        // ===================================================================
        // Border tab
        // ===================================================================

        void LoadBorderList()
        {
            var items = _vm.LoadBorderList();
            Border_EntryList.SetItems(items);
        }

        void OnBorderSelected(uint addr)
        {
            try
            {
                _vm.LoadBorderEntry(addr);
                Border_AddressBox.Value = addr;
                Border_SelectAddressLabel.Content = $"0x{addr:X08}";
                Border_P0Box.Value = _vm.BorderP0;
                Border_P4Box.Value = _vm.BorderP4;
                Border_W8Box.Value = _vm.BorderW8;
                Border_W10Box.Value = _vm.BorderW10;
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageView.OnBorderSelected failed: {0}", ex.Message);
            }
        }

        void BorderWrite_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Write World Map Border");
            try
            {
                _vm.BorderP0 = NudU32(Border_P0Box);
                _vm.BorderP4 = NudU32(Border_P4Box);
                _vm.BorderW8 = NudU32(Border_W8Box);
                _vm.BorderW10 = NudU32(Border_W10Box);
                bool ok = _vm.WriteBorder();
                if (!ok)
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapImageView.BorderWrite failed: {0}", ex.Message);
            }
        }

        void BorderReload_Click(object? sender, RoutedEventArgs e)
        {
            try { LoadBorderList(); }
            catch (Exception ex) { Log.Error("BorderReload failed: {0}", ex.Message); }
        }

        // ===================================================================
        // Icon-data tab
        // ===================================================================

        void LoadIconList()
        {
            var items = _vm.LoadIconList();
            IconData_EntryList.SetItems(items);
        }

        void OnIconSelected(uint addr)
        {
            try
            {
                _vm.LoadIconEntry(addr);
                IconData_AddressBox.Value = addr;
                IconData_SelectAddressLabel.Content = $"0x{addr:X08}";
                IconData_B0Box.Value = _vm.IconB0;
                IconData_B1Box.Value = _vm.IconB1;
                IconData_B2Box.Value = _vm.IconB2;
                IconData_B3Box.Value = _vm.IconB3;
                IconData_P4Box.Value = _vm.IconP4;
                IconData_B8Box.Value = _vm.IconB8;
                IconData_B9Box.Value = _vm.IconB9;
                IconData_B10Box.Value = _vm.IconB10;
                IconData_B11Box.Value = _vm.IconB11;
                IconData_B12Box.Value = _vm.IconB12;
                IconData_B13Box.Value = _vm.IconB13;
                IconData_W14Box.Value = _vm.IconW14;
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageView.OnIconSelected failed: {0}", ex.Message);
            }
        }

        void IconWrite_Click(object? sender, RoutedEventArgs e)
        {
            _undoService.Begin("Write World Map Icon");
            try
            {
                _vm.IconB0 = NudByte(IconData_B0Box);
                _vm.IconB1 = NudByte(IconData_B1Box);
                _vm.IconB2 = NudByte(IconData_B2Box);
                _vm.IconB3 = NudByte(IconData_B3Box);
                _vm.IconP4 = NudU32(IconData_P4Box);
                _vm.IconB8 = NudByte(IconData_B8Box);
                _vm.IconB9 = NudByte(IconData_B9Box);
                _vm.IconB10 = NudByte(IconData_B10Box);
                _vm.IconB11 = NudByte(IconData_B11Box);
                _vm.IconB12 = NudByte(IconData_B12Box);
                _vm.IconB13 = NudByte(IconData_B13Box);
                _vm.IconW14 = NudU16(IconData_W14Box);
                bool ok = _vm.WriteIcon();
                if (!ok)
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("WorldMapImageView.IconWrite failed: {0}", ex.Message);
            }
        }

        void IconDataReload_Click(object? sender, RoutedEventArgs e)
        {
            try { LoadIconList(); }
            catch (Exception ex) { Log.Error("IconDataReload failed: {0}", ex.Message); }
        }

        // ===================================================================
        // Undo (single top-level button)
        // ===================================================================

        void Undo_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                CoreState.Undo?.RunUndo();
                LoadAll();
            }
            catch (Exception ex)
            {
                Log.Error("WorldMapImageView.Undo failed: {0}", ex.Message);
            }
        }

        // ===================================================================
        // Legacy surface (kept so existing ListParityHelper / navigation
        // callers continue to compile; defaults to the Border tab).
        // ===================================================================

        public void NavigateTo(uint address) => Border_EntryList.SelectAddress(address);
        public void SelectFirstItem() => Border_EntryList.SelectFirst();

        // ===================================================================
        // Helpers
        // ===================================================================

        static uint NudU32(NumericUpDown nud)
        {
            if (nud.Value is decimal d) return (uint)d;
            return 0u;
        }

        static byte NudByte(NumericUpDown nud)
        {
            if (nud.Value is decimal d) return (byte)((uint)d & 0xFFu);
            return 0;
        }

        static ushort NudU16(NumericUpDown nud)
        {
            if (nud.Value is decimal d) return (ushort)((uint)d & 0xFFFFu);
            return 0;
        }
    }
}
