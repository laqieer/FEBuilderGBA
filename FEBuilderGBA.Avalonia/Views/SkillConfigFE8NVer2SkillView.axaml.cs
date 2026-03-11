using System;
using global::Avalonia.Controls;
using global::Avalonia.Interactivity;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class SkillConfigFE8NVer2SkillView : Window, IEditorView, IDataVerifiableView
    {
        readonly SkillConfigFE8NVer2SkillViewViewModel _vm = new();
        readonly UndoService _undoService = new();
        public string ViewTitle => "Skill Configuration (FE8N v2)";
        public bool IsLoaded => _vm.IsLoaded;

        public SkillConfigFE8NVer2SkillView()
        {
            InitializeComponent();
            WriteButton.Click += OnWrite;
            Opened += (_, _) => _vm.Initialize();
        }

        void OnWrite(object? sender, RoutedEventArgs e)
        {
            _vm.TextDetail = (uint)(TextDetailBox.Value ?? 0);
            _vm.Palette = (uint)(PaletteBox.Value ?? 0);
            _vm.UnitSkillPointer = (uint)(UnitSkillPointerBox.Value ?? 0);
            _vm.ClassSkillPointer = (uint)(ClassSkillPointerBox.Value ?? 0);
            _vm.WeaponItemSkillPointer = (uint)(WeaponItemSkillPointerBox.Value ?? 0);
            _vm.HeldItemSkillPointer = (uint)(HeldItemSkillPointerBox.Value ?? 0);

            _undoService.Begin("Edit Skill Config FE8N v2");
            try
            {
                _vm.Write();
                _undoService.Commit();
                _vm.MarkClean();
            }
            catch (Exception ex)
            {
                _undoService.Rollback();
                Log.Error("SkillConfigFE8NVer2SkillView.Write failed: {0}", ex.Message);
            }
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
