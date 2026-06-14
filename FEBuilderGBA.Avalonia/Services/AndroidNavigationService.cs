// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — Android single-activity navigation model.
//
// Single-view navigation service: hosts the ~356 editor views (top-level
// Windows on desktop) as PAGES on a view-stack with a back stack, instead of as
// separate OS windows (Android has no multi-window model).
//
// The Open<T>() : T contract MUST return the concrete view instance because ~20
// call sites call view-specific methods on it (InitFromSkillRom, JumpTo,
// NavigateToWithCostType, ...). So this service instantiates the view Window as
// a CONTENT FACTORY: it takes the Window's .Content (a normal Control), detaches
// it, and pushes THAT control onto the stack. The Window object itself is
// retained (never Show()n) so callers' NavigateTo/SelectFirstItem/view-method
// calls still operate on the live view (they touch the DataContext/named
// controls, not the act of being shown).
//
// HONEST SCOPE (#1122 / carved to #1070): this covers the COMMON navigation
// paths — Open/Navigate/back, modal-as-overlay-page, and PickFromEditor
// result-await. It does NOT yet make every per-editor attached-Window service
// work on Android: views that call StorageProvider / MessageBoxWindow.Show(this)
// / ShowDialog(this) / Close() directly still assume an attached top-level
// Window owner. A detached, never-shown Window is not a reliable top-level, so
// those flows are carved out (TODO(#1070)) — route them through
// TopLevel.GetTopLevel(content) / a dialog service in a follow-up. The desktop
// path is unaffected (DesktopNavigationService).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Page/view-stack navigation for the Android single-activity model.
    /// Implements <see cref="INavigationService"/> (the WindowManager surface)
    /// and <see cref="INavigationHost"/> (the shell-facing back/content seam).
    /// </summary>
    public sealed class AndroidNavigationService : INavigationService, INavigationHost
    {
        readonly NavigationStack<Control> _stack = new();

        // Mirrors the desktop type-keyed cache so Open<T> of an already-visible
        // singleton re-activates instead of stacking duplicates. Maps the page
        // Control back to the owning Window so view-method calls reach the live
        // instance and FindOpen<T> can return it.
        readonly Dictionary<Type, (Window Window, Control Content)> _open = new();

        /// <summary>
        /// Test seam (#1122): raised with each freshly instantiated view Window
        /// right after its content is detached and pushed. Tests use it to reach
        /// a non-cached pick/modal view (which <see cref="FindOpen{T}"/> can't
        /// return) so they can drive <c>SelectionConfirmed</c> / close. Not used
        /// at runtime.
        /// </summary>
        internal event Action<Window>? ViewInstantiated;

        public AndroidNavigationService()
        {
            _stack.StackChanged += () => StackChanged?.Invoke();
        }

        // --- INavigationHost (shell-facing) ----------------------------------

        public bool CanGoBack => _stack.CanGoBack;
        public Control? CurrentContent => _stack.CurrentTop?.Page;
        public bool GoBack() => _stack.Back();
        public event Action? StackChanged;

        /// <summary>
        /// Seed the stack with the shell's initial root page (the editor
        /// launcher). Called once by <c>MainView</c> at construction.
        /// </summary>
        public void SetRoot(Control rootPage)
        {
            _stack.Reset(rootPage);
        }

        // --- INavigationService (WindowManager surface) ----------------------

        public Window? MainWindow { get; set; }

        public T Open<T>() where T : Window, new()
        {
            // Re-activate an already-open singleton: bring its page to the top.
            if (_open.TryGetValue(typeof(T), out var existing))
            {
                BringToTop(existing.Content);
                return (T)existing.Window;
            }

            var (window, content) = MakePage<T>();
            _open[typeof(T)] = (window, content);
            _stack.Push(content);
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
            var (window, content) = MakePage<T>();
            var (entry, result) = _stack.PushForResult<object?>(content, asModal: true);

            // A modal view signals completion by closing itself. We can't Show()
            // it, so bridge Window.Closed -> pop+complete. (Desktop awaits
            // ShowDialog; here we await the page being popped.)
            void OnClosed(object? s, EventArgs e)
            {
                window.Closed -= OnClosed;
                _stack.CompleteEntry(entry, (object?)null);
                PopIfTop(content);
            }
            window.Closed += OnClosed;

            await result;
            return window;
        }

        public T? FindOpen<T>() where T : Window
        {
            if (_open.TryGetValue(typeof(T), out var existing))
                return (T)existing.Window;
            return null;
        }

        public async Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Window, IPickableEditor, new()
        {
            // Pick views are always fresh (non-cached), matching desktop.
            var (window, content) = MakePage<T>();
            window.EnablePickMode();

            var (entry, result) = _stack.PushForResult<PickResult>(content, asModal: true);

            window.SelectionConfirmed += picked =>
            {
                _stack.CompleteEntry(entry, picked);
                PopIfTop(content);
            };

            // If the view closes itself without a selection, cancel to null.
            window.Closed += (_, _) =>
            {
                _stack.CompleteEntry(entry, (PickResult?)null);
                PopIfTop(content);
            };

            // Match desktop ordering: page is up, THEN navigate.
            if (navigateAddress != 0)
                window.NavigateTo(navigateAddress);

            return await result;
        }

        public void CloseAll()
        {
            // Cancel every pending pick/modal to null and drop back to the root
            // launcher page. Mirrors desktop CloseAll (all editor windows close).
            _open.Clear();
            _stack.ClearToRoot();
        }

        // --- helpers ---------------------------------------------------------

        /// <summary>
        /// Instantiate a view Window and detach its <c>Content</c> control so it
        /// can be hosted as a page. The Window is kept alive (the page's owner)
        /// but never Show()n.
        /// </summary>
        (T Window, Control Content) MakePage<T>() where T : Window, new()
        {
            var window = new T();
            Control content = window.Content as Control
                              ?? new ContentControl { Content = window.Content };
            // Detach from the Window's logical tree so it can be re-parented
            // into the nav host without an "already has a visual parent" error.
            window.Content = null;
            ViewInstantiated?.Invoke(window);
            return (window, content);
        }

        void BringToTop(Control content)
        {
            // Already on the stack — re-push to surface it. Re-pushing a control
            // already present is harmless for the pure stack (it tracks entries,
            // not uniqueness); the host renders CurrentTop.
            _stack.Push(content);
        }

        void PopIfTop(Control content)
        {
            if (ReferenceEquals(_stack.CurrentTop?.Page, content))
                _stack.Pop();
        }
    }
}
