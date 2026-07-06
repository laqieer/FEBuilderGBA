using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Form navigation system replacing WinForms InputFormRef.JumpForm.
    ///
    /// #1122: WindowManager is now a thin FACADE over <see cref="INavigationService"/>.
    /// The public API (Open/Navigate/OpenModal/PickFromEditor/FindOpen/CloseAll/
    /// MainWindow) is UNCHANGED — its ~356 call sites are untouched — but the
    /// behavior is supplied by a platform service:
    ///   - desktop : <see cref="DesktopNavigationService"/> (multi-window,
    ///     behavior-identical to the pre-#1122 WindowManager — .Show()/.ShowDialog());
    ///   - Android : <see cref="AndroidNavigationService"/> (single-view page/
    ///     view-stack host with a back stack).
    /// The service is selected once at construction via <see cref="OperatingSystem.IsAndroid"/>
    /// / <see cref="OperatingSystem.IsIOS"/> (both single-view mobile hosts),
    /// and can be overridden (tests / the Android shell) via <see cref="SetService"/>.
    /// </summary>
    public class WindowManager
    {
        static WindowManager? _instance;
        public static WindowManager Instance => _instance ??= new WindowManager();

        INavigationService _service;

        WindowManager()
        {
            _service = CreateDefaultService();
        }

        static INavigationService CreateDefaultService()
            => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()
                ? new AndroidNavigationService()
                : new DesktopNavigationService();

        /// <summary>
        /// The active navigation service. Exposed so the Android shell
        /// (<c>MainView</c>) can reach the <see cref="INavigationHost"/> seam,
        /// and so tests can inspect/assert delegation.
        /// </summary>
        public INavigationService Service => _service;

        /// <summary>
        /// Replace the active navigation service. Used by the Android shell to
        /// install the host-backed service after the single-view boots, and by
        /// tests to inject a fake. The existing <see cref="MainWindow"/> is
        /// carried over so call sites that set it earlier keep working.
        /// </summary>
        public void SetService(INavigationService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            service.MainWindow = _service.MainWindow;
            _service = service;
        }

        /// <summary>The application root window (desktop) used as a modal parent. Null on Android.</summary>
        public Window? MainWindow
        {
            get => _service.MainWindow;
            set => _service.MainWindow = value;
        }

        /// <summary>
        /// The editor window the user is currently working in (desktop), or null when none is
        /// open / on Android. Used by the in-app bug reporter (#1747) to target the real editor.
        /// </summary>
        public Window? ActiveEditorWindow => _service.ActiveEditorWindow;

        /// <summary>Open or activate a window of the specified type.</summary>
        public T Open<T>() where T : Window, new() => _service.Open<T>();

        /// <summary>Open a window and navigate to a specific address.</summary>
        public T Navigate<T>(uint address) where T : Window, IEditorView, new()
            => _service.Navigate<T>(address);

        /// <summary>Open a window as a modal dialog.</summary>
        public Task<T> OpenModal<T>(Window? owner = null) where T : Window, new()
            => _service.OpenModal<T>(owner);

        /// <summary>Find an already-open window of the given type, or null.</summary>
        public T? FindOpen<T>() where T : Window => _service.FindOpen<T>();

        /// <summary>
        /// Open an editor as a modal pick dialog. The user selects an item via
        /// double-click or Enter, and the result is returned. Returns null if the
        /// user closes the window without selecting.
        /// </summary>
        public Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Window, IPickableEditor, new()
            => _service.PickFromEditor<T>(navigateAddress, owner);

        /// <summary>Close all managed windows.</summary>
        public void CloseAll() => _service.CloseAll();
    }
}
