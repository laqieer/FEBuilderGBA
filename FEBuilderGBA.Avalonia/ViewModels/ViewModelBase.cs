using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FEBuilderGBA.Avalonia.ViewModels
{
    /// <summary>Base ViewModel with INotifyPropertyChanged and dirty tracking.</summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        bool _isDirty;
        bool _isLoading;

        /// <summary>Whether any property has been modified since last MarkClean().</summary>
        public bool IsDirty
        {
            get => _isDirty;
            private set => SetField(ref _isDirty, value);
        }

        /// <summary>
        /// Set to true during data loading to suppress dirty marking.
        /// Typically set in LoadItem/LoadEntry methods.
        /// </summary>
        public bool IsLoading
        {
            get => _isLoading;
            set => _isLoading = value;
        }

        /// <summary>Reset dirty state (call after save or initial load).</summary>
        public void MarkClean() => IsDirty = false;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            if (!_isLoading && name != nameof(IsDirty) && name != nameof(IsLoading))
                _isDirty = true;
            return true;
        }
    }
}
