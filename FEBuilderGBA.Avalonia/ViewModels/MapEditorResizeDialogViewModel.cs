using System;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    public class MapEditorResizeDialogViewModel : ViewModelBase
    {
        bool _isLoaded;
        int _positionX, _positionY;
        int _mapWidth, _mapHeight;
        int _paddingTop, _paddingLeft, _paddingRight, _paddingBottom;
        string _dialogResult = "";

        public bool IsLoaded { get => _isLoaded; set => SetField(ref _isLoaded, value); }
        /// <summary>Map origin X position.</summary>
        public int PositionX { get => _positionX; set => SetField(ref _positionX, value); }
        /// <summary>Map origin Y position.</summary>
        public int PositionY { get => _positionY; set => SetField(ref _positionY, value); }
        /// <summary>Current map width (read-only display).</summary>
        public int MapWidth { get => _mapWidth; set => SetField(ref _mapWidth, value); }
        /// <summary>Current map height (read-only display).</summary>
        public int MapHeight { get => _mapHeight; set => SetField(ref _mapHeight, value); }
        /// <summary>Top padding tiles to add/remove.</summary>
        public int PaddingTop { get => _paddingTop; set => SetField(ref _paddingTop, value); }
        /// <summary>Left padding tiles to add/remove.</summary>
        public int PaddingLeft { get => _paddingLeft; set => SetField(ref _paddingLeft, value); }
        /// <summary>Right padding tiles to add/remove.</summary>
        public int PaddingRight { get => _paddingRight; set => SetField(ref _paddingRight, value); }
        /// <summary>Bottom padding tiles to add/remove.</summary>
        public int PaddingBottom { get => _paddingBottom; set => SetField(ref _paddingBottom, value); }
        public string DialogResult { get => _dialogResult; set => SetField(ref _dialogResult, value); }

        public void Initialize()
        {
            IsLoaded = true;
        }
    }
}
