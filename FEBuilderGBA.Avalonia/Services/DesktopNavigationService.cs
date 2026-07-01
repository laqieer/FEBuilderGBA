// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — Android single-activity navigation model.
//
// Desktop navigation service: the ORIGINAL WindowManager body moved here
// verbatim (window cache, .Show(), .ShowDialog(), window-based pick). This is
// the behavior-identical, regression-safe desktop path — WindowManager now just
// delegates to it. Every quirk is preserved on purpose:
//   - type-keyed cache of non-modal singletons (Open<T> reuses a visible one);
//   - fresh (non-cached) instance for each modal / pick;
//   - OpenModal(owner:null) falling back to non-modal Show();
//   - pick ordering: ShowDialog started, THEN optional NavigateTo.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Multi-window desktop navigation. Behavior-identical to the pre-#1122
    /// <see cref="WindowManager"/> implementation.
    /// </summary>
    public sealed class DesktopNavigationService : INavigationService
    {
        readonly Dictionary<Type, Window> _windows = new();

        // #1747: most-recently-activated managed editor windows (MRU). The last
        // still-visible entry is the editor the user is currently working in — used
        // by the in-app bug reporter to target that window instead of MainWindow.
        readonly List<Window> _mru = new();

        public Window? MainWindow { get; set; }

        /// <inheritdoc />
        public Window? ActiveEditorWindow
        {
            get
            {
                for (int i = _mru.Count - 1; i >= 0; i--)
                {
                    if (_mru[i].IsVisible)
                    {
                        return _mru[i];
                    }
                }
                return null;
            }
        }

        // Record a managed window becoming active (moves it to the MRU tail). Only
        // IEditorView windows are tracked, and never MainWindow, so the shell and any
        // transient/tool/dialog windows can never become the bug-report target (#1747).
        // internal so the headless test can drive tracking deterministically without
        // depending on Window.Activated firing under Avalonia.Headless.
        internal void NoteActivated(Window? window)
        {
            if (window == null || ReferenceEquals(window, MainWindow) || window is not IEditorView)
            {
                return;
            }
            _mru.Remove(window);
            _mru.Add(window);
        }

        // Record a managed window closing (drops it from the MRU so ActiveEditorWindow
        // falls back to the next most-recent still-open editor). internal for the test seam.
        internal void NoteClosed(Window? window)
        {
            if (window != null)
            {
                _mru.Remove(window);
            }
        }

        public T Open<T>() where T : Window, new()
        {
            if (_windows.TryGetValue(typeof(T), out var existing) && existing.IsVisible)
            {
                existing.Activate();
                NoteActivated(existing); // re-surface in the MRU on re-activation (#1747)
                return (T)existing;
            }

            var window = new T();
            _windows[typeof(T)] = window;
            NoteActivated(window); // a freshly-opened editor is the active one (#1747)
            // Subscribe once, on the creation path only (Navigate<T> calls Open<T>, so
            // hooking here avoids double-subscription).
            window.Activated += (_, _) => NoteActivated(window);
            window.Closed += (_, _) => { _windows.Remove(typeof(T)); NoteClosed(window); };
            window.Show();
            return window;
        }

        public T Navigate<T>(uint address) where T : Window, IEditorView, new()
        {
            var window = Open<T>();
            window.NavigateTo(address);
            return window;
        }

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

        public T? FindOpen<T>() where T : Window
        {
            if (_windows.TryGetValue(typeof(T), out var existing) && existing.IsVisible)
                return (T)existing;
            return null;
        }

        public async Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Window, IPickableEditor, new()
        {
            var tcs = new TaskCompletionSource<PickResult?>();

            // Create a fresh (non-cached) instance for modal pick
            var window = new T();
            window.EnablePickMode();

            window.SelectionConfirmed += result =>
            {
                tcs.TrySetResult(result);
                window.Close();
            };

            window.Closed += (_, _) =>
            {
                tcs.TrySetResult(null);
            };

            var parent = owner ?? MainWindow;
            if (parent != null)
            {
                // Show as modal dialog. The Closed event handler above already
                // sets tcs to null when the window closes without a selection.
                _ = window.ShowDialog(parent);
            }
            else
            {
                window.Show();
            }

            // Navigate to the requested address once the window is showing
            if (navigateAddress != 0)
                window.NavigateTo(navigateAddress);

            return await tcs.Task;
        }

        public void CloseAll()
        {
            foreach (var w in new List<Window>(_windows.Values))
            {
                // Log.Error takes params string[] joined with spaces (no composite
                // formatting), so pass the full exception text as its own arg.
                try { w.Close(); } catch (Exception ex) { Log.Error("DesktopNavigationService.CloseAll window close: ", ex.ToString()); }
            }
            _windows.Clear();
        }
    }
}
