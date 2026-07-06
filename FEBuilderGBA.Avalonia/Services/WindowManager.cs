using System;
using System.Threading.Tasks;
using global::Avalonia.Controls;

namespace FEBuilderGBA.Avalonia.Services
{
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

        public INavigationService Service => _service;

        public void SetService(INavigationService service)
        {
            ArgumentNullException.ThrowIfNull(service);
            service.MainWindow = _service.MainWindow;
            _service = service;
        }

        public Window? MainWindow
        {
            get => _service.MainWindow;
            set => _service.MainWindow = value;
        }

        public Window? ActiveEditorWindow => _service.ActiveEditorWindow;

        public T Open<T>() where T : Control, new() => _service.Open<T>();

        public T Navigate<T>(uint address) where T : Control, IEditorView, new()
            => _service.Navigate<T>(address);

        public Task<T> OpenModal<T>(Window? owner = null) where T : Control, new()
            => _service.OpenModal<T>(owner);

        public T? FindOpen<T>() where T : Control => _service.FindOpen<T>();

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

        public void CloseAll() => _service.CloseAll();
    }
}
