using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class AITargetView : Window, IEditorView
    {
        readonly AITargetViewModel _vm = new();

        public string ViewTitle => "AI Targeting";
        public bool IsLoaded => _vm.IsLoaded;

        public AITargetView()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;
            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                var items = _vm.LoadList();
                EntryList.SetItems(items);
            }
            catch (Exception ex)
            {
                Log.Error("AITargetView.LoadList failed: {0}", ex.Message);
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
                Log.Error("AITargetView.OnSelected failed: {0}", ex.Message);
            }
        }

        void UpdateUI()
        {
            AddrLabel.Text = string.Format("0x{0:X08}", _vm.CurrentAddr);
            LethalDamagePriorityBox.Value = _vm.LethalDamagePriority;
            EnemyRemainingHPPriorityBox.Value = _vm.EnemyRemainingHPPriority;
            EnemyDistancePriorityBox.Value = _vm.EnemyDistancePriority;
            EnemyClassPriorityBox.Value = _vm.EnemyClassPriority;
            CurrentTurnPriorityBox.Value = _vm.CurrentTurnPriority;
            CounterDamageWarningBox.Value = _vm.CounterDamageWarning;
            SurroundWarningBox.Value = _vm.SurroundWarning;
            SelfRemainingHPWarningBox.Value = _vm.SelfRemainingHPWarning;
            Unknown8Box.Value = _vm.Unknown8;
            Unknown9Box.Value = _vm.Unknown9;
            Unknown10Box.Value = _vm.Unknown10;
            Unknown11Box.Value = _vm.Unknown11;
            Unknown12Box.Value = _vm.Unknown12;
            Unknown13Box.Value = _vm.Unknown13;
            Unknown14Box.Value = _vm.Unknown14;
            Unknown15Box.Value = _vm.Unknown15;
            Unknown16Box.Value = _vm.Unknown16;
            Unknown17Box.Value = _vm.Unknown17;
            Unknown18Box.Value = _vm.Unknown18;
            Unknown19Box.Value = _vm.Unknown19;
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            try
            {
                _vm.LethalDamagePriority = (uint)(LethalDamagePriorityBox.Value ?? 0);
                _vm.EnemyRemainingHPPriority = (uint)(EnemyRemainingHPPriorityBox.Value ?? 0);
                _vm.EnemyDistancePriority = (uint)(EnemyDistancePriorityBox.Value ?? 0);
                _vm.EnemyClassPriority = (uint)(EnemyClassPriorityBox.Value ?? 0);
                _vm.CurrentTurnPriority = (uint)(CurrentTurnPriorityBox.Value ?? 0);
                _vm.CounterDamageWarning = (uint)(CounterDamageWarningBox.Value ?? 0);
                _vm.SurroundWarning = (uint)(SurroundWarningBox.Value ?? 0);
                _vm.SelfRemainingHPWarning = (uint)(SelfRemainingHPWarningBox.Value ?? 0);
                _vm.Unknown8 = (uint)(Unknown8Box.Value ?? 0);
                _vm.Unknown9 = (uint)(Unknown9Box.Value ?? 0);
                _vm.Unknown10 = (uint)(Unknown10Box.Value ?? 0);
                _vm.Unknown11 = (uint)(Unknown11Box.Value ?? 0);
                _vm.Unknown12 = (uint)(Unknown12Box.Value ?? 0);
                _vm.Unknown13 = (uint)(Unknown13Box.Value ?? 0);
                _vm.Unknown14 = (uint)(Unknown14Box.Value ?? 0);
                _vm.Unknown15 = (uint)(Unknown15Box.Value ?? 0);
                _vm.Unknown16 = (uint)(Unknown16Box.Value ?? 0);
                _vm.Unknown17 = (uint)(Unknown17Box.Value ?? 0);
                _vm.Unknown18 = (uint)(Unknown18Box.Value ?? 0);
                _vm.Unknown19 = (uint)(Unknown19Box.Value ?? 0);
                _vm.Write();
            }
            catch (Exception ex)
            {
                Log.Error("AITargetView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
    }
}
