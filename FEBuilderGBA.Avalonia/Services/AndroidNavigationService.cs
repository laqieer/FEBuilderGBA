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
// retained (the page's owner) but never Show()n.
//
// WINDOW-LIFECYCLE PARITY (Copilot PR #1154 review): editor views do their data
// load + translation in a `Window.Opened` handler and their unsubscribe/bitmap
// cleanup in `Closed`. Since the page Window is never Show()n, this service
// drives those lifecycles explicitly:
//   - on push it propagates the Window-level DataContext onto the detached
//     content (so AXAML bindings that inherit from the Window still resolve) and
//     RAISES the Window's `Opened` event (so list-loaders / TranslatedWindow
//     translation run);
//   - when a page leaves the stack (back / pop / clear / CloseAll) it RAISES the
//     Window's `Closed` event (cleanup) and drops the singleton from `_open`, so
//     `FindOpen<T>` never returns a no-longer-visible view.
// The page-leave reconciliation runs off `NavigationStack.StackChanged`, so it
// covers every removal path uniformly.
//
// HONEST SCOPE (#1122 / carved to #1873): this covers the COMMON navigation
// paths — Open/Navigate/back, modal-as-overlay-page, and PickFromEditor
// result-await, with the Opened/Closed lifecycle + DataContext + FindOpen
// parity above. It does NOT yet make every per-editor attached-Window service
// work on Android: views that call StorageProvider / MessageBoxWindow.Show(this)
// / ShowDialog(this) / Close() directly still assume an attached top-level
// Window owner. A detached, never-shown Window is not a reliable top-level, so
// those flows are carved out (TODO(#1873)) — route them through
// TopLevel.GetTopLevel(content) / a dialog service in a follow-up. The desktop
// path is unaffected (DesktopNavigationService).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        // Every page Control currently considered "on the stack" -> its owning
        // Window, so StackChanged reconciliation can fire `Closed` on pages that
        // left and drop the matching `_open` singleton entry.
        readonly Dictionary<Control, Window> _pageWindows = new();

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
            _stack.StackChanged += OnStackChanged;
        }

        // --- INavigationHost (shell-facing) ----------------------------------

        public bool CanGoBack => _stack.CanGoBack;
        public Control? CurrentContent => _stack.CurrentTop?.Page;
        public bool GoBack() => _stack.Back();
        public event Action? StackChanged;

        /// <summary>
        /// Title of the current page, from the OWNING view Window's
        /// <see cref="IEditorView.ViewTitle"/> (IEditorView is implemented by the
        /// Window subclass, not the page content's DataContext — so the title is
        /// resolved via the page→Window map, not the content). Null for the root
        /// launcher page (no owning editor Window).
        /// </summary>
        public string? CurrentTitle
        {
            get
            {
                var content = _stack.CurrentTop?.Page;
                if (content != null && _pageWindows.TryGetValue(content, out var window)
                    && window is IEditorView ev && !string.IsNullOrEmpty(ev.ViewTitle))
                    return ev.ViewTitle;
                return null;
            }
        }

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

        /// <inheritdoc />
        // Single-view host: there are no separate editor windows to target, so the
        // in-app bug reporter falls back to the main view (#1747).
        public Window? ActiveEditorWindow => null;

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
            PushPage(window, content);
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
            _pageWindows[content] = window;
            var (entry, result) = _stack.PushForResult<object?>(content, asModal: true);
            RaiseWindowEvent(window, OpenedEventName);

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
            _pageWindows[content] = window;

            var (entry, result) = _stack.PushForResult<PickResult>(content, asModal: true);
            RaiseWindowEvent(window, OpenedEventName);

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
            // ClearToRoot raises StackChanged, so OnStackChanged fires `Closed`
            // on every removed page and clears their `_open` entries.
            _stack.ClearToRoot();
        }

        // --- helpers ---------------------------------------------------------

        // Window.OnOpened / Window.OnClosed are the protected virtual methods the
        // windowing system calls on Show()/Close(); invoking them raises the
        // public Opened/Closed events AND runs subclass overrides (e.g.
        // TranslatedWindow.OnClosed unsubscribes its language-change handler).
        // Since the page Window is never shown, we invoke them reflectively so
        // editor list-loaders (Opened) + cleanup (Closed) run.
        const string OpenedEventName = "OnOpened";
        const string ClosedEventName = "OnClosed";

        /// <summary>
        /// Instantiate a view Window and detach its <c>Content</c> control so it
        /// can be hosted as a page. The Window is kept alive (the page's owner)
        /// but never Show()n. The Window-level DataContext is propagated onto the
        /// detached content when the content has none of its own, so AXAML
        /// bindings that inherit from the Window still resolve once the content
        /// is re-parented under the shell.
        /// </summary>
        (T Window, Control Content) MakePage<T>() where T : Window, new()
        {
            var window = new T();
            Control content = window.Content as Control
                              ?? new ContentControl { Content = window.Content };

            // Capture the Window-level VM BEFORE detaching: many views set
            // DataContext = _vm on the Window while their AXAML bindings live in
            // the content tree and INHERIT it through the logical tree.
            object? windowVm = window.DataContext;

            // Detach from the Window's logical tree so it can be re-parented into
            // the nav host without an "already has a visual parent" error. After
            // this, any inherited DataContext on the content is gone, so its
            // DataContext reads null unless it was set LOCALLY.
            window.Content = null;

            // Preserve binding inheritance: if the content has no local VM of its
            // own, give it the Window's, so bindings that inherited from the
            // Window keep resolving once the content is re-parented under the shell.
            if (content.DataContext == null && windowVm != null)
                content.DataContext = windowVm;

            ViewInstantiated?.Invoke(window);
            return (window, content);
        }

        void PushPage(Window window, Control content)
        {
            _pageWindows[content] = window;
            _stack.Push(content);
            // Editor views load their lists / translate in Window.Opened.
            RaiseWindowEvent(window, OpenedEventName);
        }

        void BringToTop(Control content)
        {
            // Already on the stack — surface it WITHOUT duplicating its entry
            // (MoveToTop, not Push), so repeated Open<T> of a singleton can't grow
            // the back stack unboundedly or create inconsistent duplicate history.
            // We do NOT re-raise Opened (the view is already initialized).
            _stack.MoveToTop(content);
        }

        void PopIfTop(Control content)
        {
            if (ReferenceEquals(_stack.CurrentTop?.Page, content))
                _stack.Pop();
        }

        /// <summary>
        /// Reconcile tracked page windows against the stack after any mutation:
        /// any page that left the stack gets its Window's <c>Closed</c> raised
        /// (cleanup) and its singleton entry dropped from <c>_open</c>, so
        /// <c>FindOpen&lt;T&gt;</c> matches desktop "closed window" behavior.
        /// Then forwards <see cref="StackChanged"/> to the shell.
        /// </summary>
        void OnStackChanged()
        {
            var present = new HashSet<Control>(_stack.Entries.Select(e => e.Page));
            var removed = _pageWindows.Keys.Where(c => !present.Contains(c)).ToList();
            foreach (var content in removed)
            {
                var window = _pageWindows[content];
                _pageWindows.Remove(content);

                // Drop the singleton entry that maps to this content (if any).
                var keys = _open.Where(kv => ReferenceEquals(kv.Value.Content, content))
                                .Select(kv => kv.Key).ToList();
                foreach (var k in keys) _open.Remove(k);

                // Run the view's Closed cleanup (unsubscribe, dispose bitmaps).
                RaiseWindowEvent(window, ClosedEventName);
            }

            StackChanged?.Invoke();
        }

        /// <summary>
        /// Invoke a <see cref="Window"/>'s protected <c>OnOpened</c>/<c>OnClosed</c>
        /// method reflectively, which raises the public <c>Opened</c>/<c>Closed</c>
        /// event AND runs subclass overrides. No-op + logged if the method shape
        /// ever changes, so navigation never throws because of a lifecycle hook.
        /// </summary>
        static void RaiseWindowEvent(Window window, string methodName)
        {
            try
            {
                MethodInfo? method = typeof(Window).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    binder: null, types: new[] { typeof(EventArgs) }, modifiers: null);
                if (method == null)
                {
                    // A future Avalonia version could rename/reshape OnOpened/OnClosed;
                    // surface that loudly so the lifecycle regression is diagnosable.
                    Log.Error("AndroidNavigationService.RaiseWindowEvent: Window.", methodName,
                              "(EventArgs) not found — page lifecycle hook skipped.");
                    return;
                }
                method.Invoke(window, new object[] { EventArgs.Empty });
            }
            catch (Exception ex)
            {
                // Log the full exception text (not just Message) so navigation
                // never throws because of a lifecycle hook but stays diagnosable.
                Log.Error("AndroidNavigationService.RaiseWindowEvent ", methodName, ": ", ex.ToString());
            }
        }
    }
}
