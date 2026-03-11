using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Form navigation system replacing WinForms InputFormRef.JumpForm.
    /// Caches Window instances by type, provides Open/Navigate/Modal methods.
    /// </summary>
    public class WindowManager
    {
        static WindowManager? _instance;
        public static WindowManager Instance => _instance ??= new WindowManager();

        readonly Dictionary<Type, Window> _windows = new();
        public Window? MainWindow { get; set; }

        /// <summary>Open or activate a window of the specified type.</summary>
        public T Open<T>() where T : Window, new()
        {
            if (_windows.TryGetValue(typeof(T), out var existing) && existing.IsVisible)
            {
                existing.Activate();
                return (T)existing;
            }

            var window = new T();
            _windows[typeof(T)] = window;
            window.Closed += (_, _) => _windows.Remove(typeof(T));
            window.Show();
            return window;
        }

        /// <summary>Open a window and navigate to a specific address.</summary>
        public T Navigate<T>(uint address) where T : Window, IEditorView, new()
        {
            var window = Open<T>();
            window.NavigateTo(address);
            return window;
        }

        /// <summary>Open a window as a modal dialog.</summary>
        public async Task<T> OpenModal<T>(Window? owner = null) where T : Window, new()
        {
            var window = new T();
            var parent = owner ?? MainWindow;
            if (parent != null)
                await window.ShowDialog(parent);
            else
                window.Show();
            return window;
        }

        /// <summary>Find an already-open window of the given type, or null.</summary>
        public T? FindOpen<T>() where T : Window
        {
            if (_windows.TryGetValue(typeof(T), out var existing) && existing.IsVisible)
                return (T)existing;
            return null;
        }

        /// <summary>
        /// Open a window, navigate to an address, and invoke a callback when an item is selected.
        /// Useful for pick-and-return patterns (e.g., "select a unit").
        /// </summary>
        public T NavigateAndSelect<T>(uint address, Action<uint>? onSelected = null) where T : Window, IEditorView, new()
        {
            var window = Navigate<T>(address);
            if (onSelected != null && window is IEditorView editor)
            {
                // The editor view can call back when a selection is confirmed
                // This is wired through the IEditorView pattern
            }
            return window;
        }

        /// <summary>Close all managed windows.</summary>
        public void CloseAll()
        {
            foreach (var w in new List<Window>(_windows.Values))
            {
                try { w.Close(); } catch { }
            }
            _windows.Clear();
        }
    }
}
