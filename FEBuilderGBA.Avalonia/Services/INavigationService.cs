// SPDX-License-Identifier: GPL-3.0-or-later
// #1122 — Android single-activity navigation model.
//
// The navigation abstraction that WindowManager delegates to. Two impls:
//   - DesktopNavigationService : the current multi-window WindowManager body,
//     verbatim — behavior-identical to today (.Show()/.ShowDialog()/window cache).
//   - AndroidNavigationService : a single-view page/view-stack nav host with a
//     back stack (modal-as-page + pick result-await).
//
// The interface mirrors the public WindowManager surface EXACTLY (same generic
// constraints + return types), so WindowManager becomes a thin facade and the
// ~356 `WindowManager.Instance.*` call sites are untouched.
using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Platform navigation abstraction behind <see cref="WindowManager"/>.
    /// Method surface is intentionally identical to the original WindowManager
    /// so call sites don't change. The desktop impl is window-based (unchanged
    /// behavior); the Android impl is a single-view view-stack host.
    /// </summary>
    public interface INavigationService
    {
        /// <summary>The application root window (desktop) used as a modal parent. Null on Android.</summary>
        Window? MainWindow { get; set; }

        /// <summary>Open or activate a view of the specified type.</summary>
        T Open<T>() where T : Window, new();

        /// <summary>Open a view and navigate it to a specific address.</summary>
        T Navigate<T>(uint address) where T : Window, IEditorView, new();

        /// <summary>Open a view as a modal dialog (desktop) or modal overlay page (Android).</summary>
        Task<T> OpenModal<T>(Window? owner = null) where T : Window, new();

        /// <summary>
        /// Open an editor in pick mode and await the user's selection. Returns
        /// null when dismissed without a selection.
        /// </summary>
        Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Window, IPickableEditor, new();

        /// <summary>Find an already-open view of the given type, or null.</summary>
        T? FindOpen<T>() where T : Window;

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
        /// The title of the currently shown page, taken from the owning view's
        /// <see cref="IEditorView.ViewTitle"/> when it has one, else null (root /
        /// untitled page). The shell shows this in its app bar.
        /// </summary>
        string? CurrentTitle { get; }

        /// <summary>Dismiss the top modal/page. Returns true if it consumed the back.</summary>
        bool GoBack();

        /// <summary>Raised after any stack mutation so the shell re-renders the top content.</summary>
        event Action? StackChanged;
    }
}
