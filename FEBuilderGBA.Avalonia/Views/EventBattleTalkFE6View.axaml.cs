using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class EventBattleTalkFE6View : TranslatedWindow, IEditorView, IDataVerifiableView
    {
        readonly EventBattleTalkFE6ViewModel _vm = new();
        readonly UndoService _undoService = new();

        public string ViewTitle => "Battle Dialogue (FE6)";
        public bool IsLoaded => _vm.IsLoaded;

        public EventBattleTalkFE6View()
        {
            InitializeComponent();
            EntryList.SelectedAddressChanged += OnSelected;
            WriteButton.Click += OnWrite;

            // Live name/text previews while editing.
            AttackerUnitBox.ValueChanged += (_, _) => AttackerNameLabel.Text = UnitName(AttackerUnitBox);
            DefenderUnitBox.ValueChanged += (_, _) => DefenderNameLabel.Text = UnitName(DefenderUnitBox);
            TextIdBox.ValueChanged += (_, _) => TextPreviewLabel.Text = TextPreview(TextIdBox);

            Opened += (_, _) => LoadList();
        }

        void LoadList()
        {
            try
            {
                _vm.IsLoading = true;
                var items = _vm.LoadList();
                EntryList.SetItemsWithIcons(items, i => ListIconLoaders.UnitPortraitFromAddrU8Loader(items, i));
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleTalkFE6View.LoadList failed: " + ex.ToString());
            }
            finally
            {
                _vm.IsLoading = false;
                _vm.MarkClean();
            }
        }

        void OnSelected(uint addr)
        {
            try
            {
                _vm.IsLoading = true;
                _vm.LoadEntry(addr);
                UpdateUI();
            }
            catch (Exception ex)
            {
                Log.Error("EventBattleTalkFE6View.OnSelected failed: " + ex.ToString());
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
            AttackerUnitBox.Value = _vm.AttackerUnit;
            DefenderUnitBox.Value = _vm.DefenderUnit;
            Unknown02Box.Value = _vm.Unknown02;
            Unknown03Box.Value = _vm.Unknown03;
            TextIdBox.Value = _vm.Text;
            Unknown06Box.Value = _vm.Unknown06;
            Unknown07Box.Value = _vm.Unknown07;
            AchievementFlagBox.Value = _vm.AchievementFlag;
            Unknown0ABox.Value = _vm.Unknown0A;
            Unknown0BBox.Value = _vm.Unknown0B;

            AttackerNameLabel.Text = UnitName(AttackerUnitBox);
            DefenderNameLabel.Text = UnitName(DefenderUnitBox);
            TextPreviewLabel.Text = TextPreview(TextIdBox);
        }

        static string UnitName(NumericUpDown box)
        {
            try { return NameResolver.GetUnitNameByOneBasedId((uint)(box.Value ?? 0)); }
            catch { return ""; }
        }

        static string TextPreview(NumericUpDown box)
        {
            uint id = (uint)(box.Value ?? 0);
            if (id == 0) return "";
            try { return NameResolver.GetTextById(id); }
            catch { return ""; }
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            if (!_vm.IsLoaded) return;

            _undoService.Begin(R._("Edit Battle Dialogue (FE6)"));
            try
            {
                _vm.AttackerUnit = (uint)(AttackerUnitBox.Value ?? 0);
                _vm.DefenderUnit = (uint)(DefenderUnitBox.Value ?? 0);
                _vm.Unknown02 = (uint)(Unknown02Box.Value ?? 0);
                _vm.Unknown03 = (uint)(Unknown03Box.Value ?? 0);
                _vm.Text = (uint)(TextIdBox.Value ?? 0);
                _vm.Unknown06 = (uint)(Unknown06Box.Value ?? 0);
                _vm.Unknown07 = (uint)(Unknown07Box.Value ?? 0);
                _vm.AchievementFlag = (uint)(AchievementFlagBox.Value ?? 0);
                _vm.Unknown0A = (uint)(Unknown0ABox.Value ?? 0);
                _vm.Unknown0B = (uint)(Unknown0BBox.Value ?? 0);
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("EventBattleTalkFE6View.OnWrite failed: " + ex.ToString());
            }
        }

        public void NavigateTo(uint address) => EntryList.SelectAddress(address);
        public void SelectFirstItem() => EntryList.SelectFirst();
        public ViewModelBase? DataViewModel => _vm;
    }
}
