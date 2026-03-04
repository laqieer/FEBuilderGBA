using System;
using global::Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Views
{
    public partial class VennouWeaponLockView : Window, IEditorView, IDataVerifiableView
    {
        readonly VennouWeaponLockViewModel _vm = new();

        public string ViewTitle => "Weapon Lock (Vennou)";
        public bool IsLoaded => _vm.IsLoaded;

        public VennouWeaponLockView()
        {
            InitializeComponent();
        }

        public void NavigateTo(uint address) { }
        public void SelectFirstItem() { }
        public ViewModelBase? DataViewModel => _vm;
    }
}
