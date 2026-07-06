// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    public sealed class AndroidNavigationService : INavigationService, INavigationHost
    {
        readonly NavigationStack<Control> _stack = new();
        readonly Dictionary<Type, (Control View, Control Content, Window? Window)> _open = new();
        readonly Dictionary<Control, Window> _pageWindows = new();
        readonly Dictionary<Control, Control> _pageViews = new();

        internal event Action<Window>? ViewInstantiated;

        public AndroidNavigationService()
        {
            _stack.StackChanged += OnStackChanged;
        }

        public bool CanGoBack => _stack.CanGoBack;
        public Control? CurrentContent => _stack.CurrentTop?.Page;
        public bool GoBack() => _stack.Back();
        public event Action? StackChanged;

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

        public void SetRoot(Control rootPage)
        {
            _stack.Reset(rootPage);
        }

        public Window? MainWindow { get; set; }
        public Window? ActiveEditorWindow => null;

        public T Open<T>() where T : Control, new()
        {
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

        public T Navigate<T>(uint address) where T : Control, IEditorView, new()
        {
            var view = Open<T>();
            view.NavigateTo(address);
            return view;
        }

        public async Task<T> OpenModal<T>(Window? owner = null) where T : Control, new()
        {
            var page = MakePage<T>();
            TrackPage(page);
            var (entry, result) = _stack.PushForResult<object?>(page.Content, asModal: true);
            OpenPageLifecycle(page);

            if (page.View is IEmbeddableEditor embeddable)
            {
                embeddable.CloseRequested += (_, _) =>
                {
                    _stack.CompleteEntry(entry, (object?)null);
                    PopIfTop(page.Content);
                };
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

            await result;
            return (T)page.View;
        }

        public T? FindOpen<T>() where T : Control
        {
            if (_open.TryGetValue(typeof(T), out var existing))
                return (T)existing.View;
            return null;
        }

        public async Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Control, IPickableEditor, new()
        {
            var page = MakePage<T>();
            var editor = (T)page.View;
            editor.EnablePickMode();
            TrackPage(page);

            var (entry, result) = _stack.PushForResult<PickResult>(page.Content, asModal: true);
            OpenPageLifecycle(page);

            editor.SelectionConfirmed += picked =>
            {
                _stack.CompleteEntry(entry, picked);
                PopIfTop(page.Content);
            };

            if (page.View is IEmbeddableEditor embeddable)
            {
                embeddable.CloseRequested += (_, _) =>
                {
                    _stack.CompleteEntry(entry, (PickResult?)null);
                    PopIfTop(page.Content);
                };
            }
            else if (page.Window != null)
            {
                page.Window.Closed += (_, _) =>
                {
                    _stack.CompleteEntry(entry, (PickResult?)null);
                    PopIfTop(page.Content);
                };
            }

            if (navigateAddress != 0)
                editor.NavigateTo(navigateAddress);

            return await result;
        }

        public void CloseAll()
        {
            _stack.ClearToRoot();
        }

        const string OpenedEventName = "OnOpened";
        const string ClosedEventName = "OnClosed";

        (Control View, Control Content, Window? Window) MakePage<T>() where T : Control, new()
        {
            var view = new T();
            if (view is IEmbeddableEditor)
                return (view, view, null);

            if (view is not Window window)
                throw new InvalidOperationException($"{typeof(T).Name} must be a Window or implement IEmbeddableEditor.");

            Control content = window.Content as Control
                              ?? new ContentControl { Content = window.Content };
            object? windowVm = window.DataContext;
            window.Content = null;
            if (content.DataContext == null && windowVm != null)
                content.DataContext = windowVm;

            ViewInstantiated?.Invoke(window);
            return (window, content, window);
        }

        void PushPage((Control View, Control Content, Window? Window) page)
        {
            TrackPage(page);
            if (page.View is IEmbeddableEditor embeddable)
                embeddable.CloseRequested += (_, _) => PopIfTop(page.Content);
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
            _stack.MoveToTop(content);
        }

        void PopIfTop(Control content)
        {
            if (ReferenceEquals(_stack.CurrentTop?.Page, content))
                _stack.Pop();
        }

        void OnStackChanged()
        {
            var present = new HashSet<Control>(_stack.Entries.Select(e => e.Page));
            var removed = _pageViews.Keys.Where(c => !present.Contains(c)).ToList();
            foreach (var content in removed)
            {
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
