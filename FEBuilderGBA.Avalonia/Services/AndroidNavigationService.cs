// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — Android single-activity navigation model.
//
// Single-view navigation service: hosts editor views as PAGES on a view-stack
// with a back stack, instead of separate OS windows (Android/iOS/browser have no
// multi-window model).
//
// The Open<T>() : T contract MUST return the concrete view instance because ~20
// call sites call view-specific methods on it (InitFromSkillRom, JumpTo,
// NavigateToWithCostType, ...). #1873 now supports two construction paths:
//   - converted IEmbeddableEditor controls are safe to instantiate directly, so
//     this service pushes the UserControl itself as the page (NO Window
//     construction, NO reflective Window lifecycle);
//   - legacy Window editors still use the #1122 content-factory bridge: instantiate
//     the Window, detach its Content, push that Control, and retain the Window as
//     the page owner so caller method calls still reach the live instance.
//
// WINDOW-LIFECYCLE PARITY (legacy Window path, Copilot PR #1154 review): editor
// Windows load data + translate in `Window.Opened` and clean up in `Closed`.
// Since the page Window is never Show()n, this service drives those lifecycles
// explicitly for the legacy path:
//   - on push it propagates the Window-level DataContext onto the detached
//     content and RAISES the Window's `Opened` event;
//   - when a page leaves the stack it RAISES the Window's `Closed` event and
//     drops the singleton from `_open`, so `FindOpen<T>` never returns a
//     no-longer-visible view.
// Converted embeddable editors use the normal UserControl visual lifecycle
// (`OnAttachedToVisualTree` / `OnDetachedFromVisualTree`) instead.
//
// HONEST SCOPE (#1122 / #1873): common navigation paths are covered —
// Open/Navigate/back, modal-as-overlay-page, and PickFromEditor result-await.
// Embeddable editors are the real single-view-safe path; legacy Window editors
// remain a compatibility bridge until each editor is converted. This still does
// NOT make every per-editor attached-Window service work on Android: legacy views
// that call StorageProvider / MessageBoxWindow.Show(this) / ShowDialog(this) /
// Close() directly still assume an attached top-level Window owner. A detached,
// never-shown Window is not a reliable top-level, so those flows stay carved out
// (TODO(#1873)) — route them through TopLevel.GetTopLevel(content) / a dialog
// service as the affected editors convert. The desktop path is unaffected
// (DesktopNavigationService).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Page/view-stack navigation for the Android/iOS/browser single-view model.
    /// Implements <see cref="INavigationService"/> (the WindowManager surface)
    /// and <see cref="INavigationHost"/> (the shell-facing back/content seam).
    /// </summary>
    public sealed class AndroidNavigationService : INavigationService, INavigationHost
    {
        readonly NavigationStack<Control> _stack = new();

        // Mirrors the desktop type-keyed cache so Open<T> of an already-visible
        // singleton re-activates instead of stacking duplicates. The View is the
        // concrete T returned to callers; Content is the page pushed to the stack;
        // Window is non-null only for legacy Window editors.
        readonly Dictionary<Type, (Control View, Control Content, Window? Window)> _open = new();

        // Every legacy Window-backed page Control currently considered "on the
        // stack" -> its owning Window, so StackChanged reconciliation can fire
        // `Closed` on pages that left and drop the matching `_open` singleton.
        readonly Dictionary<Control, Window> _pageWindows = new();

        // Every page Control currently considered "on the stack" -> the concrete
        // view returned to callers. Used to reconcile both embeddable and legacy
        // pages uniformly when entries leave the stack.
        readonly Dictionary<Control, Control> _pageViews = new();

        // Per-page CloseRequested cleanup for embeddable editors. Handlers must
        // be removable because retained UserControls can otherwise keep stale
        // NavigationEntry/completer state alive after the page is closed.
        readonly Dictionary<Control, Action> _pageCloseRequestedUnsubscribers = new();

        /// <summary>
        /// Test seam (#1122): raised with each freshly instantiated legacy view
        /// Window right after its content is detached and pushed. Tests use it to
        /// reach a non-cached pick/modal Window (which <see cref="FindOpen{T}"/>
        /// can't return) so they can drive <c>SelectionConfirmed</c> / close. Not
        /// used for embeddable editors and not used at runtime.
        /// </summary>
        internal event Action<Window>? ViewInstantiated;

        public AndroidNavigationService()
        {
            _stack.StackChanged += OnStackChanged;
        }

        public bool CanGoBack => _stack.CanGoBack;
        public Control? CurrentContent => _stack.CurrentTop?.Page;
        public bool GoBack() => _stack.Back();
        public event Action? StackChanged;

        /// <summary>
        /// Title of the current page. Converted embeddable editors implement
        /// <see cref="IEditorView"/> on the content itself; legacy Window editors
        /// resolve through the page→Window map. Null for the root launcher page.
        /// </summary>
        public string? CurrentTitle
        {
            get
            {
                var content = _stack.CurrentTop?.Page;
                if (content is IEditorView contentEditor && !string.IsNullOrEmpty(contentEditor.ViewTitle))
                    return contentEditor.ViewTitle;
                if (content != null && _pageWindows.TryGetValue(content, out var window)
                    && window is IEditorView windowEditor && !string.IsNullOrEmpty(windowEditor.ViewTitle))
                    return windowEditor.ViewTitle;
                return null;
            }
        }

        /// <summary>
        /// Seed the stack with the shell's initial root page (the editor launcher).
        /// Called once by <c>MainView</c> at construction.
        /// </summary>
        public void SetRoot(Control rootPage)
        {
            _stack.Reset(rootPage);
        }

        public Window? MainWindow { get; set; }

        /// <inheritdoc />
        // Single-view host: there are no separate editor windows to target, so the
        // in-app bug reporter falls back to the main view (#1747).
        public Window? ActiveEditorWindow => null;

        /// <inheritdoc />
        public T Open<T>() where T : Control, new()
        {
            // Re-activate an already-open singleton: bring its page to the top.
            if (_open.TryGetValue(typeof(T), out var existing))
            {
                BringToTop(existing.Content);
                return (T)existing.View;
            }

            var page = MakePage<T>();
            _open[typeof(T)] = page;
            PushPage(page);
            return (T)page.View;
        }

        /// <inheritdoc />
        public T Navigate<T>(uint address) where T : Control, IEditorView, new()
        {
            var view = Open<T>();
            view.NavigateTo(address);
            return view;
        }

        /// <inheritdoc />
        public async Task<T> OpenModal<T>(Window? owner = null) where T : Control, new()
        {
            var page = MakePage<T>();
            TrackPage(page);
            var (entry, result) = _stack.PushForResult<object?>(page.Content, asModal: true);
            OpenPageLifecycle(page);

            if (page.View is IEmbeddableEditor embeddable)
            {
                // A modal embeddable editor signals completion through
                // CloseRequested; the page is popped and the awaited result
                // completes with null.
                void OnCloseRequested(object? s, EventArgs e)
                {
                    UnwirePageCloseRequested(page.Content);
                    _stack.CompleteEntry(entry, embeddable.DialogResult);
                    PopIfTop(page.Content);
                }
                WirePageCloseRequested(page.Content, embeddable, OnCloseRequested);
            }
            else if (page.Window != null)
            {
                // Legacy Window modal parity: a Window closing itself completes
                // the modal page, just as ShowDialog would complete on desktop.
                void OnClosed(object? s, EventArgs e)
                {
                    page.Window.Closed -= OnClosed;
                    _stack.CompleteEntry(entry, (object?)null);
                    PopIfTop(page.Content);
                }
                page.Window.Closed += OnClosed;
            }

            await result;
            return (T)page.View;
        }

        /// <inheritdoc />
        public async Task<TResult?> OpenModal<T, TResult>(Window? owner = null, Action<T>? configure = null) where T : Control, new()
        {
            var page = MakePage<T>();
            configure?.Invoke((T)page.View);
            TrackPage(page);
            var (entry, result) = _stack.PushForResult<object?>(page.Content, asModal: true);
            OpenPageLifecycle(page);

            if (page.View is IEmbeddableEditor embeddable)
            {
                void OnCloseRequested(object? s, EventArgs e)
                {
                    UnwirePageCloseRequested(page.Content);
                    _stack.CompleteEntry(entry, embeddable.DialogResult);
                    PopIfTop(page.Content);
                }
                WirePageCloseRequested(page.Content, embeddable, OnCloseRequested);
            }
            else if (page.Window != null)
            {
                void OnClosed(object? s, EventArgs e)
                {
                    page.Window.Closed -= OnClosed;
                    _stack.CompleteEntry(entry, (object?)null);
                    PopIfTop(page.Content);
                }
                page.Window.Closed += OnClosed;
            }

            object? value = await result;
            return value is TResult typed ? typed : default;
        }

        /// <inheritdoc />
        public T? FindOpen<T>() where T : Control
        {
            if (_open.TryGetValue(typeof(T), out var existing))
                return (T)existing.View;
            return null;
        }

        /// <inheritdoc />
        public async Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Control, IPickableEditor, new()
        {
            // Pick views are always fresh (non-cached), matching desktop.
            var page = MakePage<T>();
            var editor = (T)page.View;
            editor.EnablePickMode();
            TrackPage(page);

            var (entry, result) = _stack.PushForResult<PickResult>(page.Content, asModal: true);
            OpenPageLifecycle(page);

            void OnSelectionConfirmed(PickResult picked)
            {
                editor.SelectionConfirmed -= OnSelectionConfirmed;
                UnwirePageCloseRequested(page.Content);
                _stack.CompleteEntry(entry, picked);
                PopIfTop(page.Content);
            }

            editor.SelectionConfirmed += OnSelectionConfirmed;

            if (page.View is IEmbeddableEditor embeddable)
            {
                // If the embeddable view requests close without confirming a
                // selection, cancel to null.
                void OnCloseRequested(object? s, EventArgs e)
                {
                    editor.SelectionConfirmed -= OnSelectionConfirmed;
                    UnwirePageCloseRequested(page.Content);
                    _stack.CompleteEntry(entry, (PickResult?)null);
                    PopIfTop(page.Content);
                }
                WirePageCloseRequested(page.Content, embeddable, OnCloseRequested);
            }
            else if (page.Window != null)
            {
                // If the legacy Window closes itself without a selection, cancel
                // to null.
                void OnClosed(object? s, EventArgs e)
                {
                    page.Window.Closed -= OnClosed;
                    editor.SelectionConfirmed -= OnSelectionConfirmed;
                    _stack.CompleteEntry(entry, (PickResult?)null);
                    PopIfTop(page.Content);
                }
                page.Window.Closed += OnClosed;
            }

            // Match desktop ordering: page is up, THEN navigate.
            if (navigateAddress != 0)
                editor.NavigateTo(navigateAddress);

            return await result;
        }

        /// <inheritdoc />
        public void CloseAll()
        {
            // Cancel every pending pick/modal to null and drop back to the root
            // launcher page. Mirrors desktop CloseAll (all editor windows close).
            // ClearToRoot raises StackChanged, so OnStackChanged fires `Closed`
            // on every removed legacy page and clears every `_open` entry.
            _stack.ClearToRoot();
        }

        // --- helpers ---------------------------------------------------------

        // Window.OnOpened / Window.OnClosed are the protected virtual methods the
        // windowing system calls on Show()/Close(); invoking them raises the public
        // Opened/Closed events AND runs subclass overrides (e.g.
        // TranslatedWindow.OnClosed unsubscribes its language-change handler).
        // Since legacy page Windows are never shown, we invoke them reflectively so
        // editor list-loaders (Opened) + cleanup (Closed) run. Embeddable editors
        // intentionally bypass this and use UserControl attach/detach lifecycle.
        const string OpenedEventName = "OnOpened";
        const string ClosedEventName = "OnClosed";

        /// <summary>
        /// Instantiate a view and produce the page content. Embeddable editors are
        /// returned directly as content. Legacy Windows are retained as owners but
        /// have their Content detached so that normal Control can be re-parented
        /// into the single-view shell.
        /// </summary>
        (Control View, Control Content, Window? Window) MakePage<T>() where T : Control, new()
        {
            var view = new T();
            if (view is IEmbeddableEditor)
                return (view, view, null);

            if (view is not Window window)
                throw new InvalidOperationException($"{typeof(T).Name} must be a Window or implement IEmbeddableEditor.");

            // Capture the Window-level VM BEFORE detaching: many views set
            // DataContext = _vm on the Window while their AXAML bindings live in
            // the content tree and INHERIT it through the logical tree.
            Control content = window.Content as Control
                              ?? new ContentControl { Content = window.Content };
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
            return (window, content, window);
        }

        void PushPage((Control View, Control Content, Window? Window) page)
        {
            TrackPage(page);
            if (page.View is IEmbeddableEditor embeddable)
            {
                void OnCloseRequested(object? s, EventArgs e)
                {
                    UnwirePageCloseRequested(page.Content);
                    PopIfTop(page.Content);
                }
                WirePageCloseRequested(page.Content, embeddable, OnCloseRequested);
            }
            _stack.Push(page.Content);
            OpenPageLifecycle(page);
        }

        void TrackPage((Control View, Control Content, Window? Window) page)
        {
            _pageViews[page.Content] = page.View;
            if (page.Window != null)
                _pageWindows[page.Content] = page.Window;
        }

        void OpenPageLifecycle((Control View, Control Content, Window? Window) page)
        {
            if (page.Window != null)
                RaiseWindowEvent(page.Window, OpenedEventName);
        }

        void BringToTop(Control content)
        {
            // Already on the stack — surface it WITHOUT duplicating its entry
            // (MoveToTop, not Push), so repeated Open<T> of a singleton can't grow
            // the back stack unboundedly or create inconsistent duplicate history.
            // We do NOT re-raise Window.Opened or UserControl attach lifecycle here
            // because the view is already initialized.
            _stack.MoveToTop(content);
        }

        void PopIfTop(Control content)
        {
            if (ReferenceEquals(_stack.CurrentTop?.Page, content))
                _stack.Pop();
        }

        /// <summary>
        /// Reconcile tracked pages against the stack after any mutation: any page
        /// that left the stack gets its singleton entry dropped from <c>_open</c>.
        /// Legacy Window-backed pages also get their Window's <c>Closed</c> raised
        /// for cleanup. Then forwards <see cref="StackChanged"/> to the shell.
        /// </summary>
        void OnStackChanged()
        {
            var present = new HashSet<Control>(_stack.Entries.Select(e => e.Page));
            var removed = _pageViews.Keys.Where(c => !present.Contains(c)).ToList();
            foreach (var content in removed)
            {
                UnwirePageCloseRequested(content);
                _pageViews.Remove(content);

                var keys = _open.Where(kv => ReferenceEquals(kv.Value.Content, content))
                                .Select(kv => kv.Key).ToList();
                foreach (var k in keys) _open.Remove(k);

                if (_pageWindows.TryGetValue(content, out var window))
                {
                    _pageWindows.Remove(content);
                    RaiseWindowEvent(window, ClosedEventName);
                }
            }

            StackChanged?.Invoke();
        }

        void WirePageCloseRequested(Control content, IEmbeddableEditor embeddable, EventHandler handler)
        {
            UnwirePageCloseRequested(content);
            embeddable.CloseRequested += handler;
            _pageCloseRequestedUnsubscribers[content] = () => embeddable.CloseRequested -= handler;
        }

        void UnwirePageCloseRequested(Control content)
        {
            if (_pageCloseRequestedUnsubscribers.Remove(content, out var unsubscribe))
                unsubscribe();
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
                    Log.Error("AndroidNavigationService.RaiseWindowEvent: Window.", methodName,
                              "(EventArgs) not found — page lifecycle hook skipped.");
                    return;
                }
                method.Invoke(window, new object[] { EventArgs.Empty });
            }
            catch (Exception ex)
            {
                Log.Error("AndroidNavigationService.RaiseWindowEvent ", methodName, ": ", ex.ToString());
            }
        }
    }
}
