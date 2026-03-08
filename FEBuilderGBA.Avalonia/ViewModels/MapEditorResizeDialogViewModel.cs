using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorResizeDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        int _x, _y, _t, _l, _r, _b;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        public int X { get => _x; set => SetField(ref _x, value); }
        public int Y { get => _y; set => SetField(ref _y, value); }
        public int T { get => _t; set => SetField(ref _t, value); }
        public int L { get => _l; set => SetField(ref _l, value); }
        public int R { get => _r; set => SetField(ref _r, value); }
        public int B { get => _b; set => SetField(ref _b, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
