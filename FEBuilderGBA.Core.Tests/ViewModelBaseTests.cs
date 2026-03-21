using System.ComponentModel;
using System.Runtime.CompilerServices;
using Xunit;

namespace FEBuilderGBA.Core.Tests
{
    /// <summary>
    /// Tests for the dirty-tracking pattern used by ViewModelBase.
    /// Uses a local copy of the pattern to avoid requiring Avalonia dependency.
    /// </summary>
    public class ViewModelBaseTests
    {
        /// <summary>Minimal reproduction of ViewModelBase dirty-tracking logic.</summary>
        class TestViewModelBase : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            bool _isDirty;
            bool _isLoading;

            public bool IsDirty
            {
                get => _isDirty;
                private set => SetField(ref _isDirty, value);
            }

            public bool IsLoading
            {
                get => _isLoading;
                set => _isLoading = value;
            }

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

        class TestViewModel : TestViewModelBase
        {
            string _name = "";
            public string Name
            {
                get => _name;
                set => SetField(ref _name, value);
            }
        }

        [Fact]
        public void SetField_MarksDirty()
        {
            var vm = new TestViewModel();
            Assert.False(vm.IsDirty);
            vm.Name = "test";
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void MarkClean_ResetsDirty()
        {
            var vm = new TestViewModel();
            vm.Name = "test";
            Assert.True(vm.IsDirty);
            vm.MarkClean();
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void IsLoading_SuppressesDirty()
        {
            var vm = new TestViewModel();
            vm.IsLoading = true;
            vm.Name = "loaded value";
            Assert.False(vm.IsDirty);
            vm.IsLoading = false;
        }

        [Fact]
        public void SetField_SameValue_NoDirty()
        {
            var vm = new TestViewModel();
            vm.Name = ""; // same as default
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void PropertyChanged_Fires()
        {
            var vm = new TestViewModel();
            string? changedProp = null;
            vm.PropertyChanged += (_, e) => changedProp = e.PropertyName;
            vm.Name = "new";
            Assert.Equal("Name", changedProp);
        }

        /// <summary>
        /// Regression test for #198: after write, reload with IsLoading guard + MarkClean
        /// must leave IsDirty=false. Without the guard, the reload's SetField calls re-dirty.
        /// </summary>
        [Fact]
        public void WriteReloadPattern_WithIsLoadingGuard_StaysClean()
        {
            var vm = new TestViewModel();

            // Simulate: user edits, write succeeds, then reload with IsLoading guard
            vm.Name = "edited";
            Assert.True(vm.IsDirty);

            // Simulate write success, then guarded reload (the fix from #198)
            vm.IsLoading = true;
            try
            {
                vm.Name = "reloaded from ROM"; // SetField during reload
            }
            finally
            {
                vm.IsLoading = false;
                vm.MarkClean();
            }

            Assert.False(vm.IsDirty, "After guarded reload + MarkClean, IsDirty should be false");
        }

        /// <summary>
        /// Shows the bug from #198: without IsLoading guard, reload after write re-dirties.
        /// </summary>
        [Fact]
        public void WriteReloadPattern_WithoutIsLoadingGuard_ReDirties()
        {
            var vm = new TestViewModel();
            vm.Name = "edited";
            vm.MarkClean(); // simulate write success

            // Reload WITHOUT IsLoading guard (the bug)
            vm.Name = "reloaded from ROM";

            Assert.True(vm.IsDirty, "Without IsLoading guard, reload re-dirties the VM");
        }

        [Fact]
        public void MarkClean_RaisesPropertyChanged()
        {
            var vm = new TestViewModel();
            vm.Name = "dirty";
            bool dirtyChanged = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "IsDirty") dirtyChanged = true;
            };
            vm.MarkClean();
            Assert.True(dirtyChanged);
        }
    }
}
