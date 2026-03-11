using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SummonsDemonKingViewerView : Window, IEditorView, IDataVerifiableView
    {
        readonly SummonsDemonKingViewerViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Demon King Summon";
        public bool IsLoaded => _vm.CanWrite;

        public SummonsDemonKingViewerView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            _vm.IsLoading = true;
            try
            {
                var items = _vm.LoadSummonsDemonKingList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("SummonsDemonKingViewerView.LoadList failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void OnSelected(uint addr)
        {
            _vm.IsLoading = true;
            try
            {
                _vm.LoadSummonsDemonKing(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("SummonsDemonKingViewerView.OnSelected failed: {0}", ex.Message);
            }
            finally { _vm.IsLoading = false; _vm.MarkClean(); }
        }

        void UpdateUI()
        {
            AddrLabel.Text = $"0x{_vm.CurrentAddr:X08}";
            UnitIdBox.Value = _vm.UnitId;
            ClassIdBox.Value = _vm.ClassId;
            Unknown1Box.Value = _vm.Commander;
            B3Box.Value = _vm.LevelGrowth;
            W4Box.Value = _vm.Coordinates;
            B6Box.Value = _vm.Special;
            B7Box.Value = _vm.Padding7;
            P8Box.Text = $"0x{_vm.AIPointer:X08}";
            B12Box.Value = _vm.Item1;
            B13Box.Value = _vm.Item2;
            B14Box.Value = _vm.Item3;
            B15Box.Value = _vm.Item4;
            B16Box.Value = _vm.PrimaryAI;
            B17Box.Value = _vm.SecondaryAI;
            B18Box.Value = _vm.TargetRecoveryAI;
            B19Box.Value = _vm.RetreatAI;
        }

        void Write_Click(object? sender, RoutedEventArgs e)
        {
            if (!_vm.CanWrite) return;
            _undoService.Begin("Edit Demon King Summon");
            try
            {
                _vm.UnitId = (uint)(UnitIdBox.Value ?? 0);
                _vm.ClassId = (uint)(ClassIdBox.Value ?? 0);
                _vm.Commander = (uint)(Unknown1Box.Value ?? 0);
                _vm.LevelGrowth = (uint)(B3Box.Value ?? 0);
                _vm.Coordinates = (uint)(W4Box.Value ?? 0);
                _vm.Special = (uint)(B6Box.Value ?? 0);
                _vm.Padding7 = (uint)(B7Box.Value ?? 0);
                _vm.AIPointer = ParseHexText(P8Box.Text);
                _vm.Item1 = (uint)(B12Box.Value ?? 0);
                _vm.Item2 = (uint)(B13Box.Value ?? 0);
                _vm.Item3 = (uint)(B14Box.Value ?? 0);
                _vm.Item4 = (uint)(B15Box.Value ?? 0);
                _vm.PrimaryAI = (uint)(B16Box.Value ?? 0);
                _vm.SecondaryAI = (uint)(B17Box.Value ?? 0);
                _vm.TargetRecoveryAI = (uint)(B18Box.Value ?? 0);
                _vm.RetreatAI = (uint)(B19Box.Value ?? 0);
                _vm.WriteSummonsDemonKing();
                _undoService.Commit();
                _vm.MarkClean();
                CoreState.Services?.ShowInfo("Demon king summon data written.");
            }
            catch (Exception ex) { _undoService.Rollback(); Log.Error("SummonsDemonKingViewerView.Write: {0}", ex.Message); }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;

        static uint ParseHexText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
            return uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : 0;
        }
    }
}
