using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8NVer3SkillView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8NVer3SkillViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Skill Configuration (FE8N v3)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillConfigFE8NVer3SkillView()
        {
            InitializeComponent();
            WriteButton.Click += OnWrite;
            Opened += (_, _) => _vm.Initialize();
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _vm.TextDetail = (uint)(TextDetailBox.Value ?? 0);
            _vm.Palette = (uint)(PaletteBox.Value ?? 0);
            _vm.UnitClassPointer = (uint)(UnitClassPointerBox.Value ?? 0);
            _vm.ClassSkillPointer = (uint)(ClassSkillPointerBox.Value ?? 0);
            _vm.WeaponItemSkillPointer = (uint)(WeaponItemSkillPointerBox.Value ?? 0);
            _vm.HeldItemSkillPointer = (uint)(HeldItemSkillPointerBox.Value ?? 0);
            _vm.CompositeSkillPointer = (uint)(CompositeSkillPointerBox.Value ?? 0);

            _undoService.Begin("Edit Skill Config FE8N v3");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillConfigFE8NVer3SkillView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
