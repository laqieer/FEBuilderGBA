using System;
using global::Avalonia;
using System.Collections.Generic;
using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SoundFootStepsViewerView : TranslatedUserControl, IEmbeddableEditor, IDataVerifiableView
    {
        readonly SoundFootStepsViewerViewModel _vm = new();
        readonly UndoService _undoService = new();


        bool _hasLoadedList;
        public string ViewTitle => "Footstep Sounds";
        public new bool IsLoaded => _vm.CanWrite;


        public EditorDescriptor Descriptor => new("Footstep Sounds Editor", 1245, 636, SizeToContent: true);

        public event EventHandler? CloseRequested;
        public SoundFootStepsViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;        }


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
                _vm.RefreshEnableState();

                var items = _vm.LoadSoundFootStepsList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.ClassIconLoader(items, i));

                // Gate Write + List Expansion on the Switch2 signature. When the
                // jump-table is absent the editor is read-only — mirrors WinForms
                // SoundFootStepsForm_Load (hides Write, shows the not-found error);
                // hiding Expand too is sibling ItemUsagePointer parity
                // (Switch2Expands cannot proceed without a valid signature).
                bool enabled = _vm.IsSwitch2Enabled;
                WriteButton.IsVisible = enabled;
                SwitchListExpandsButton.IsVisible = enabled;
                NotFoundLabel.IsVisible = !enabled;
            }
            catch (Exception ex)
            {
                Log.Error($"SoundFootStepsViewerView.LoadList failed: {ex}");
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
                _vm.LoadSoundFootSteps(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error($"SoundFootStepsViewerView.OnSelected failed: {ex}");
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            DataPointerBox.Text = $"0x{_vm.DataPointer:X08}";
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;

            _undoService.Begin("Edit Footstep Sounds");
            try
            {
                _vm.DataPointer = ParseHexText(DataPointerBox.Text);
                _vm.WriteSoundFootSteps();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services.ShowInfo("Footstep sounds data written.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error($"SoundFootStepsViewerView.Write_Click failed: {ex}");
            }
        }

        void SwitchListExpands_Click(object? sender, RoutedEventArgs e)
        {
            // Mirror WinForms SoundFootStepsForm.SwitchListExpandsButton_Click:
            //   defAddr  = U.atoh(L_0_COMBO.Items[0])   (first config entry)
            //   newCount = ClassForm.DataCount()        (cover all classes)
            //   Switch2Expands(...) + FE8 PlaySoundStepByClass hardcode fix,
            //   all under ONE undo scope; then refresh the list.
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return;

            // WF aborts when L_0_COMBO.Items.Count <= 0 — without a default fill
            // pointer we would write a table full of NULLs. Mirror that guard.
            var comboLines = LoadDefaultPointerCombo();
            if (comboLines.Count == 0)
            {
                CoreState.Services?.ShowError(
                    "No footstep-sound function definitions loaded. " +
                    "Cannot expand the list without a default fill pointer.");
                return;
            }

            uint defAddr = U.atoh(comboLines[0]);
            uint newCount = _vm.GetNewCount();

            _undoService.Begin("SoundFootStep SwitchExpands");
            try
            {
                var undoData = _undoService.GetActiveUndoData();
                uint newAddr = undoData != null
                    ? _vm.ExpandList(newCount, defAddr, undoData)
                    : U.NOT_FOUND;
                if (newAddr == U.NOT_FOUND)
                {
                    _undoService.Rollback();
                    return;
                }
                _undoService.Commit();
                _vm.MarkClean();
                LoadList();
                CoreState.Services?.ShowInfo("Footstep-sound table expanded.");
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                // Log.Error(params string[]) joins args with spaces (no composite
                // formatting) — pass ONE interpolated string with the full exception
                // so the stack trace survives. (#1449 Copilot CLI review fix.)
                Log.Error($"SoundFootStepsViewerView.SwitchListExpands_Click failed: {ex}");
            }
        }

        /// <summary>
        /// Load the `sound_foot_steps_` config combo entries (the WinForms
        /// L_0_COMBO source). Returns each entry's leading hex token — the
        /// first is used as the default fill pointer for new table slots.
        /// Empty when the config data file is missing (older ROM / no config).
        /// </summary>
        static List<string> LoadDefaultPointerCombo()
        {
            var result = new List<string>();
            ROM rom = CoreState.ROM;
            if (rom?.RomInfo == null) return result;

            // File I/O can throw (missing/locked file, permission/IO error). Keep
            // it inside this helper's own try/catch and return an empty list so the
            // caller's empty-combo guard shows the friendly error instead of the
            // exception crashing the UI event. (#1449 review.)
            try
            {
                string filename = U.ConfigDataFilename("sound_foot_steps_", rom);
                if (!File.Exists(filename)) return result;

                foreach (string raw in File.ReadAllLines(filename))
                {
                    string line = raw;
                    if (U.IsComment(line) || U.OtherLangLine(line)) continue;
                    line = U.ClipComment(line).Trim();
                    if (line.Length == 0) continue;
                    // Each line is `0xHEX[=Name]` — keep the leading hex token.
                    int eq = line.IndexOf('=');
                    string hexToken = eq >= 0 ? line.Substring(0, eq).Trim() : line.Trim();
                    if (hexToken.Length == 0) continue;
                    result.Add(hexToken);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"SoundFootStepsViewerView.LoadDefaultPointerCombo failed: {ex}");
                return new List<string>();
            }
            return result;
        }

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        public void RequestClose() => CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
