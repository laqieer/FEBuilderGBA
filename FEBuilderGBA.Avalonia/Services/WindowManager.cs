using System;
using System.Collections.Generic;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Form navigation system replacing WinForms InputFormRef.JumpForm.
    /// Caches Window instances by type, provides Open/Navigate methods.
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
