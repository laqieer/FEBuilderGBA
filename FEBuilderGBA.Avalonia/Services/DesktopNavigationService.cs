// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    public sealed class DesktopNavigationService : INavigationService
    {
        readonly Dictionary<Type, (Window Host, Control Content)> _windows = new();
        readonly List<Window> _mru = new();

        public Window? MainWindow { get; set; }

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

        internal void NoteActivated(Window? window)
        {
            if (window == null || ReferenceEquals(window, MainWindow) || !IsEditorHost(window))
                return;
            _mru.Remove(window);
            _mru.Add(window);
        }

        internal void NoteClosed(Window? window)
        {
            if (window != null)
                _mru.Remove(window);
        }

        public T Open<T>() where T : Control, new()
        {
            var (host, content) = OpenEntry<T>();
            return (T)content;
        }

        public Window OpenAsTopLevel<T>() where T : Control, new()
        {
            var (host, _) = OpenEntry<T>();
            return host;
        }

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

        public T Navigate<T>(uint address) where T : Control, IEditorView, new()
        {
            var content = Open<T>();
            content.NavigateTo(address);
            return content;
        }

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

        public T? FindOpen<T>() where T : Control
        {
            if (_windows.TryGetValue(typeof(T), out var existing) && existing.Host.IsVisible)
                return (T)existing.Content;
            return null;
        }

        public async Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Control, IPickableEditor, new()
        {
            var tcs = new TaskCompletionSource<PickResult?>();
            var (host, contentControl) = CreateHost<T>();
            var editor = (T)contentControl;
            editor.EnablePickMode();

            editor.SelectionConfirmed += result =>
            {
                tcs.TrySetResult(result);
                host.Close();
            };

            host.Closed += (_, _) => tcs.TrySetResult(null);
            WireCloseRequested(editor, host);

            var parent = owner ?? MainWindow;
            if (parent != null)
                _ = host.ShowDialog(parent);
            else
                host.Show();

            if (navigateAddress != 0)
                editor.NavigateTo(navigateAddress);

            return await tcs.Task;
        }

        public void CloseAll()
        {
            foreach (var entry in new List<(Window Host, Control Content)>(_windows.Values))
            {
                try { entry.Host.Close(); } catch (Exception ex) { Log.Error("DesktopNavigationService.CloseAll window close: ", ex.ToString()); }
            }
            _windows.Clear();
        }

        static (Window Host, Control Content) CreateHost<T>() where T : Control, new()
        {
            var content = new T();
            if (content is Window window)
                return (window, window);
            if (content is IEmbeddableEditor embeddable)
                return (new EditorHostWindow(embeddable), content);
            throw new InvalidOperationException($"{typeof(T).Name} must be a Window or implement IEmbeddableEditor.");
        }

        static void WireCloseRequested(Control content, Window host)
        {
            if (content is IEmbeddableEditor embeddable)
                embeddable.CloseRequested += (_, _) => host.Close();
        }

        static bool IsEditorHost(Window window)
            => window is IEditorView
               || window is EditorHostWindow { Content: IEditorView };
    }
}
