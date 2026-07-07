// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — Android single-activity navigation model.
//
// Desktop navigation service: the ORIGINAL WindowManager body moved here
// as the behavior-identical, regression-safe desktop path — WindowManager now
// just delegates to it. Every legacy Window-editor quirk is preserved on purpose:
//   - type-keyed cache of non-modal singletons (Open<T> reuses a visible one);
//   - fresh (non-cached) instance for each modal / pick;
//   - OpenModal(owner:null) falling back to non-modal Show();
//   - pick ordering: ShowDialog started, THEN optional NavigateTo.
//
// #1873 adds a second, additive mode: converted IEmbeddableEditor UserControls
// are hosted inside a generic EditorHostWindow on desktop while Open<T> still
// returns the editor content instance. The cache therefore stores both the
// desktop top-level host and the content object.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Multi-window desktop navigation. Behavior-identical to the pre-#1122
    /// <see cref="WindowManager"/> implementation for legacy <see cref="Window"/>
    /// editors, with an additive #1873 wrapper path for embeddable editor content.
    /// </summary>
    public sealed class DesktopNavigationService : INavigationService
    {
        readonly Dictionary<Type, (Window Host, Control Content)> _windows = new();

        // #1747: most-recently-activated managed editor top-levels (MRU). For
        // legacy Window editors the top-level is the editor Window itself; for
        // embeddable editors it is the EditorHostWindow whose Content is the
        // IEditorView. The last still-visible entry is the editor the user is
        // currently working in — used by the in-app bug reporter to target that
        // window instead of MainWindow.
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
                        return _mru[i];
                }
                return null;
            }
        }

        // Record a managed top-level becoming active (moves it to the MRU tail).
        // Only IEditorView windows or EditorHostWindow instances whose Content is
        // IEditorView are tracked, and never MainWindow, so the shell and any
        // transient/tool/dialog windows can never become the bug-report target
        // (#1747). internal so the headless test can drive tracking
        // deterministically without depending on Window.Activated firing under
        // Avalonia.Headless.
        internal void NoteActivated(Window? window)
        {
            if (window == null || ReferenceEquals(window, MainWindow) || !IsEditorHost(window))
                return;
            _mru.Remove(window);
            _mru.Add(window);
        }

        // Record a managed top-level closing (drops it from the MRU so
        // ActiveEditorWindow falls back to the next most-recent still-open editor).
        // internal for the test seam.
        internal void NoteClosed(Window? window)
        {
            if (window != null)
                _mru.Remove(window);
        }

        /// <summary>
        /// Open or activate a singleton editor. Legacy Window editors return the
        /// Window; embeddable editors return their UserControl content while the
        /// host Window stays in the desktop cache.
        /// </summary>
        public T Open<T>() where T : Control, new()
        {
            var (host, content) = OpenEntry<T>();
            return (T)content;
        }

        /// <summary>
        /// Desktop harness helper: open the editor and return the top-level that
        /// should be rendered/closed. Legacy Window editors return themselves;
        /// embeddable editors return their <see cref="EditorHostWindow"/>.
        /// </summary>
        public Window OpenAsTopLevel<T>() where T : Control, new()
        {
            var (host, _) = OpenEntry<T>();
            return host;
        }

        // Shared non-modal singleton path. Subscribe once, on the creation path
        // only (Navigate<T> calls Open<T>, so hooking here avoids double-subscription).
        (Window Host, Control Content) OpenEntry<T>() where T : Control, new()
        {
            if (_windows.TryGetValue(typeof(T), out var existing) && existing.Host.IsVisible)
            {
                existing.Host.Activate();
                NoteActivated(existing.Host);
                return existing;
            }

            var (host, content) = CreateHost<T>();
            _windows[typeof(T)] = (host, content);
            NoteActivated(host);
            host.Activated += (_, _) => NoteActivated(host);
            host.Closed += (_, _) => { _windows.Remove(typeof(T)); NoteClosed(host); };
            WireCloseRequested(content, host);
            host.Show();
            return (host, content);
        }

        /// <inheritdoc />
        public T Navigate<T>(uint address) where T : Control, IEditorView, new()
        {
            var content = Open<T>();
            content.NavigateTo(address);
            return content;
        }

        /// <inheritdoc />
        public async Task<T> OpenModal<T>(Window? owner = null) where T : Control, new()
        {
            var (host, content) = CreateHost<T>();
            WireCloseRequested(content, host);
            var parent = owner ?? MainWindow;
            if (parent != null)
                await host.ShowDialog(parent);
            else
                host.Show();
            return (T)content;
        }

        /// <inheritdoc />
        public async Task<TResult?> OpenModal<T, TResult>(Window? owner = null, Action<T>? configure = null) where T : Control, new()
        {
            var (host, content) = CreateHost<T>();
            configure?.Invoke((T)content);
            WireCloseRequested(content, host);
            var parent = owner ?? MainWindow;
            if (parent != null)
                return await host.ShowDialog<TResult?>(parent);

            var tcs = new TaskCompletionSource<TResult?>();
            host.Closed += (_, _) =>
            {
                object? result = content is IEmbeddableEditor embeddable ? embeddable.DialogResult : null;
                tcs.TrySetResult(result is TResult typed ? typed : default);
            };
            host.Show();
            return await tcs.Task;
        }

        /// <inheritdoc />
        public T? FindOpen<T>() where T : Control
        {
            if (_windows.TryGetValue(typeof(T), out var existing) && existing.Host.IsVisible)
                return (T)existing.Content;
            return null;
        }

        /// <inheritdoc />
        public async Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Control, IPickableEditor, new()
        {
            var tcs = new TaskCompletionSource<PickResult?>();

            // Create a fresh (non-cached) instance for modal pick, matching the
            // original WindowManager behavior. The host may be the editor Window
            // itself or an EditorHostWindow around embeddable content.
            var (host, contentControl) = CreateHost<T>();
            var editor = (T)contentControl;
            editor.EnablePickMode();

            void OnSelectionConfirmed(PickResult result)
            {
                editor.SelectionConfirmed -= OnSelectionConfirmed;
                tcs.TrySetResult(result);
                host.Close();
            }

            editor.SelectionConfirmed += OnSelectionConfirmed;

            host.Closed += (_, _) =>
            {
                editor.SelectionConfirmed -= OnSelectionConfirmed;
                tcs.TrySetResult(null);
            };
            WireCloseRequested(editor, host);

            var parent = owner ?? MainWindow;
            if (parent != null)
                _ = host.ShowDialog(parent);
            else
                host.Show();

            // Navigate to the requested address once the window/page is showing.
            if (navigateAddress != 0)
                editor.NavigateTo(navigateAddress);

            return await tcs.Task;
        }

        /// <inheritdoc />
        public void CloseAll()
        {
            foreach (var entry in new List<(Window Host, Control Content)>(_windows.Values))
            {
                // Log.Error takes params string[] joined with spaces (no composite
                // formatting), so pass the full exception text as its own arg.
                try { entry.Host.Close(); } catch (Exception ex) { Log.Error("DesktopNavigationService.CloseAll window close: ", ex.ToString()); }
            }
            _windows.Clear();
        }

        // Dual-mode host creation: legacy Window editors keep their exact
        // top-level path; converted embeddable editors are wrapped in a generic
        // host that applies the descriptor once.
        static (Window Host, Control Content) CreateHost<T>() where T : Control, new()
        {
            var content = new T();
            if (content is Window window)
                return (window, window);
            if (content is IEmbeddableEditor embeddable)
                return (new EditorHostWindow(embeddable), content);
            throw new InvalidOperationException($"{typeof(T).Name} must be a Window or implement IEmbeddableEditor.");
        }

        // Embeddable editors request close without knowing whether they are hosted
        // in a desktop Window or single-view page. On desktop, that maps to closing
        // the generic host.
        static void WireCloseRequested(Control content, Window host)
        {
            if (content is not IEmbeddableEditor embeddable)
                return;

            void OnCloseRequested(object? s, EventArgs e)
            {
                embeddable.CloseRequested -= OnCloseRequested;
                host.Closed -= OnHostClosed;
                host.Close(embeddable.DialogResult);
            }

            void OnHostClosed(object? s, EventArgs e)
            {
                embeddable.CloseRequested -= OnCloseRequested;
                host.Closed -= OnHostClosed;
            }

            embeddable.CloseRequested += OnCloseRequested;
            host.Closed += OnHostClosed;
        }

        // #1747 tracking predicate. In the legacy path the host is the editor
        // Window; in the embeddable path the host is EditorHostWindow and the
        // editor is its Content.
        static bool IsEditorHost(Window window)
            => window is IEditorView
               || window is EditorHostWindow { Content: IEditorView };
    }
}
