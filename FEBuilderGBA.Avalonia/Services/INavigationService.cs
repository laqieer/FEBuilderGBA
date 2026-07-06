// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — Android single-activity navigation model.
using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Platform navigation abstraction behind <see cref="WindowManager"/>.
    /// The generic editor type is a <see cref="Control"/> so legacy Window
    /// editors and converted embeddable UserControl editors can coexist.
    /// </summary>
    public interface INavigationService
    {
        Window? MainWindow { get; set; }
        Window? ActiveEditorWindow { get; }
        T Open<T>() where T : Control, new();
        T Navigate<T>(uint address) where T : Control, IEditorView, new();
        Task<T> OpenModal<T>(Window? owner = null) where T : Control, new();
        Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Control, IPickableEditor, new();
        T? FindOpen<T>() where T : Control;
        void CloseAll();
    }

    public interface INavigationHost
    {
        bool CanGoBack { get; }
        Control? CurrentContent { get; }
        string? CurrentTitle { get; }
        bool GoBack();
        event Action? StackChanged;
    }
}
