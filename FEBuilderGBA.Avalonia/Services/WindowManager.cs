using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
    /// <summary>
    /// Form navigation system replacing WinForms InputFormRef.JumpForm.
    ///
    /// #1122: WindowManager is a thin FACADE over <see cref="INavigationService"/>.
    /// The public API (Open/Navigate/OpenModal/PickFromEditor/FindOpen/CloseAll/
    /// MainWindow) remains the call-site contract — its ~356 call sites are
    /// untouched — but behavior is supplied by a platform service:
    ///   - desktop : <see cref="DesktopNavigationService"/> (multi-window,
    ///     behavior-identical for legacy Window editors and, as of #1873,
    ///     wraps embeddable editor content in <see cref="EditorHostWindow"/>);
    ///   - Android/iOS : <see cref="AndroidNavigationService"/> (single-view
    ///     page/view-stack host with a back stack; converted embeddable editors
    ///     are pushed directly as controls).
    /// #1873 relaxes the generic constraint from Window to Control so converted
    /// <see cref="IEmbeddableEditor"/> UserControls and legacy Window editors
    /// can coexist during rollout.
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
        /// For embeddable editors this is the hosting <see cref="EditorHostWindow"/>.
        /// </summary>
        public Window? ActiveEditorWindow => _service.ActiveEditorWindow;

        /// <summary>
        /// Open or activate a view of the specified type. The generic constraint is
        /// <see cref="Control"/> so legacy Window editors and converted embeddable
        /// UserControls share the same facade.
        /// </summary>
        public T Open<T>() where T : Control, new() => _service.Open<T>();

        /// <summary>Open a view and navigate to a specific address.</summary>
        public T Navigate<T>(uint address) where T : Control, IEditorView, new()
            => _service.Navigate<T>(address);

        /// <summary>Open a view as a modal dialog or modal page.</summary>
        public Task<T> OpenModal<T>(Window? owner = null) where T : Control, new()
            => _service.OpenModal<T>(owner);

        /// <summary>Find an already-open view of the given type, or null.</summary>
        public T? FindOpen<T>() where T : Control => _service.FindOpen<T>();

        /// <summary>
        /// Open an editor as a modal pick dialog/page. The user selects an item via
        /// double-click or Enter, and the result is returned. Returns null if the
        /// user closes the editor without selecting.
        /// </summary>
        public Task<PickResult?> PickFromEditor<T>(uint navigateAddress = 0, Window? owner = null)
            where T : Control, IPickableEditor, new()
            => _service.PickFromEditor<T>(navigateAddress, owner);

        /// <summary>
        /// Open an editor and return the desktop top-level that hosts it. Legacy
        /// Window editors return themselves; embeddable editors return their
        /// generic <see cref="EditorHostWindow"/> wrapper.
        /// </summary>
        public Window OpenAsTopLevel<T>() where T : Control, new()
        {
            if (_service is DesktopNavigationService desktop)
                return desktop.OpenAsTopLevel<T>();
            var opened = _service.Open<T>();
            return opened as Window
                   ?? throw new NotSupportedException("OpenAsTopLevel is only supported by the desktop navigation service.");
        }

        /// <summary>Close all managed views.</summary>
        public void CloseAll() => _service.CloseAll();
    }
}
