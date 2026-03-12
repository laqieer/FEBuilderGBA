using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Dialogs;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class ToolAutomaticRecoveryROMHeaderView : Window, IEditorView
    {
        readonly ToolAutomaticRecoveryROMHeaderViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Automatic Recovery ROM Header";
        public bool IsLoaded => _vm.IsLoaded;

        public ToolAutomaticRecoveryROMHeaderView()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.IsLoading = true;
            _vm.Initialize();
            _vm.IsLoading = false;
            _vm.MarkClean();
        }

        async void SelectFile_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                var path = await FileDialogHelper.OpenRomFile(this);
                if (!string.IsNullOrEmpty(path))
                    _vm.OriginalFilename = path;
            }
            catch (Exception ex)
            {
                Log.Error("ToolAutomaticRecoveryROMHeaderView", ex.ToString());
            }
        }

        void Recover_Click(object? sender, RoutedEventArgs e)
        {
            var currentRom = CoreState.ROM;
            if (currentRom == null)
            {
                _vm.RecoveryStatus = "No ROM loaded.";
                return;
            }

            string origPath = _vm.OriginalFilename;
            if (string.IsNullOrEmpty(origPath) || !System.IO.File.Exists(origPath))
            {
                _vm.RecoveryStatus = "Please select a valid unmodified (vanilla) ROM file first.";
                return;
            }

            _undoService.Begin("Recover ROM Header");
            try
            {
                // Load the original ROM to get the clean header
                ROM origRom = new ROM();
                string version;
                bool loaded = origRom.Load(origPath, out version);
                if (!loaded)
                {
                    _undoService.Rollback();
                    _vm.RecoveryStatus = $"Could not load original ROM. Unsupported version: {version}";
                    return;
                }

                // Compare headers (first 0x100 bytes)
                byte[] origHeader = origRom.getBinaryData(0, 0x100);
                byte[] currentHeader = currentRom.getBinaryData(0, 0x100);

                // Check if headers already match
                if (U.memcmp(origHeader, currentHeader) == 0)
                {
                    _undoService.Rollback();
                    _vm.RecoveryStatus = "ROM header is already correct. No recovery needed.";
                    return;
                }

                // Count differing bytes for the status message
                int diffCount = 0;
                for (int i = 0; i < 0x100; i++)
                {
                    if (origHeader[i] != currentHeader[i])
                        diffCount++;
                }

                // Write the original header over the current ROM's header
                currentRom.write_range(0, origHeader);
                _undoService.Commit();

                _vm.RecoveryStatus = $"Recovery complete. Restored {diffCount} byte(s) in ROM header (0x0 - 0xFF).";
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("ToolAutomaticRecoveryROMHeaderView.Recover", ex.ToString());
                _vm.RecoveryStatus = $"Recovery failed: {ex.Message}";
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
    }
}
