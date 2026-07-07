// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — Android single-activity navigation model.
//
// The navigation abstraction that WindowManager delegates to. Two impls:
//   - DesktopNavigationService : the current multi-window WindowManager body,
//     behavior-identical for legacy Window editors (.Show()/.ShowDialog()/
//     window cache), with an additive dual-mode wrapper for IEmbeddableEditor
//     UserControls.
//   - AndroidNavigationService : a single-view page/view-stack nav host with a
//     back stack (modal-as-page + pick result-await). Converted embeddable
//     editors are pushed directly as pages; legacy Window editors still use the
//     content-factory bridge until they are converted.
//
// #1873 relaxes the generic constraint from Window to Control so the public
// WindowManager surface can host both unconverted Window editors and converted
// embeddable UserControl editors during rollout. Return types and call-site
// contracts remain source-compatible for the ~356 WindowManager call sites.
using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Platform navigation abstraction behind <see cref="WindowManager"/>.
    /// Method surface intentionally mirrors the original WindowManager surface;
    /// the generic editor type is now <see cref="Control"/> so legacy
    /// <see cref="Window"/> editors and converted <see cref="IEmbeddableEditor"/>
    /// UserControls can coexist on the same facade.
    /// </summary>
    public interface INavigationService
    {
        /// <summary>The application root window (desktop) used as a modal parent. Null on Android.</summary>
        Window? MainWindow { get; set; }

        /// <summary>
        /// The editor window the user is currently working in — the most-recently-activated
        /// managed <see cref="IEditorView"/> top-level that is still visible — or null when none
        /// is open. Used by the in-app bug reporter (#1747) to screenshot/label the actual
        /// editor rather than the main window. Always null on the Android single-view host.
        /// </summary>
        Window? ActiveEditorWindow { get; }

        /// <summary>
        /// Open or activate a view of the specified type. <typeparamref name="T"/>
        /// may be a legacy <see cref="Window"/> editor or a converted embeddable
        /// <see cref="Control"/>.
        /// </summary>
        T Open<T>() where T : Control, new();

        /// <summary>Open a view and navigate it to a specific address.</summary>
        T Navigate<T>(uint address) where T : Control, IEditorView, new();

        /// <summary>Open a view as a modal dialog (desktop) or modal overlay page (Android).</summary>
        Task<T> OpenModal<T>(Window? owner = null) where T : Control, new();

        /// <summary>Open a modal dialog/page and return its dialog result.</summary>
        Task<TResult?> OpenModal<T, TResult>(Window? owner = null, Action<T>? configure = null) where T : Control, new();

        /// <summary>
        /// Open an editor in pick mode and await the user's selection. Returns
        /// null when dismissed without a selection.
        /// </summary>
        Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Control, IPickableEditor, new();

        /// <summary>Find an already-open view of the given type, or null.</summary>
        T? FindOpen<T>() where T : Control;

        /// <summary>Close all managed views.</summary>
        void CloseAll();
    }

    /// <summary>
    /// Internal host-facing seam (Copilot plan-review point #1). The Android
    /// single-view shell (<c>MainView</c>) binds to THIS — back/forward + the
    /// rendered top content + a change notification — without casting to the
    /// concrete <see cref="AndroidNavigationService"/> or reaching into
    /// <see cref="NavigationStack{TPage}"/>. The desktop service does not
    /// implement it (desktop has no single-view host).
    /// </summary>
    public interface INavigationHost
    {
        /// <summary>True when a hardware/back-button press would dismiss a modal or pop a page.</summary>
        bool CanGoBack { get; }

        /// <summary>The control currently shown at the top of the view stack (or null).</summary>
        Control? CurrentContent { get; }

        /// <summary>
        /// The title of the currently shown page, taken from the current content's
        /// <see cref="IEditorView.ViewTitle"/> for embeddable editors, or from the
        /// owning legacy Window's title metadata via the page→Window map. Null for
        /// the root / untitled page.
        /// </summary>
        string? CurrentTitle { get; }

        /// <summary>Dismiss the top modal/page. Returns true if it consumed the back.</summary>
        bool GoBack();

        /// <summary>Raised after any stack mutation so the shell re-renders the top content.</summary>
        event Action? StackChanged;
    }
}
