using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FEBuilderGBA.Avalonia;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Media;
using global::Avalonia.Platform.Storage;
using global::Avalonia.Threading;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("WindowManagerSerial")]
public sealed class MainWindowRomPathTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedPickedOpen_PreservesStorageRomPathAndHandle(bool localFile)
    {
        var previousRom = CoreState.ROM;
        var previousWindow = WindowManager.Instance.MainWindow;
        bool previousSmoke = App.SmokeTestMode;
        string? previousStartup = App.StartupRomPath;
        string? previousProject = App.StartupProjectDir;
        MainWindow? window = null;
        try
        {
            CoreState.ROM = null;
            App.SmokeTestMode = true;
            App.StartupRomPath = null;
            App.StartupProjectDir = null;
            window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var loadedRom = new ROM { Filename = "provider-only.gba" };
            CoreState.ROM = loadedRom;
            var storageFile = CreateStorageFile(new Uri("content://roms/provider-only.gba"));
            var storageField = typeof(MainWindow).GetField("_currentRomStorageFile",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            storageField.SetValue(window, storageFile);
            var vm = Assert.IsType<MainWindowViewModel>(window.DataContext);
            vm.UpdateFromRom(hasLocalPath: false);

            var failedUri = localFile
                ? new Uri(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.gba"))
                : new Uri("content://roms/invalid.gba");
            Assert.False(await window.LoadPickedRomAsync(CreateStorageFile(failedUri)));

            Assert.Same(loadedRom, CoreState.ROM);
            Assert.Same(storageFile, storageField.GetValue(window));
            typeof(MainWindow).GetMethod("OnLanguageChanged",
                BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("provider-only.gba", vm.RomFilePath);
            Assert.Equal("FEBuilderGBA - provider-only.gba", vm.WindowTitle);
        }
        finally
        {
            CoreState.ROM = null;
            window?.Close();
            WindowManager.Instance.MainWindow = previousWindow;
            App.SmokeTestMode = previousSmoke;
            App.StartupRomPath = previousStartup;
            App.StartupProjectDir = previousProject;
            CoreState.ROM = previousRom;
        }
    }

    static IStorageFile CreateStorageFile(Uri path)
    {
        var file = DispatchProxy.Create<IStorageFile, StorageFileProxy>();
        ((StorageFileProxy)(object)file).Path = path;
        return file;
    }

    public class StorageFileProxy : DispatchProxy
    {
        public Uri Path { get; set; } = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name switch
            {
                "get_Path" => Path,
                "get_Name" => "invalid.gba",
                "OpenReadAsync" => Task.FromResult<Stream>(Stream.Null),
                "Dispose" => null,
                _ => throw new NotSupportedException(method?.Name)
            };
    }

    [Fact]
    public void UpdateFromRom_DisplaysAbsoluteLocalPathAndClearsItWithoutRom()
    {
        var previousRom = CoreState.ROM;
        try
        {
            CoreState.ROM = new ROM { Filename = Path.Combine("fixtures", "same-name.gba") };
            var vm = new MainWindowViewModel();
            vm.UpdateFromRom();

            Assert.Equal(Path.GetFullPath(CoreState.ROM.Filename), vm.RomFilePath);
            Assert.Equal("FEBuilderGBA - same-name.gba", vm.WindowTitle);
            vm.HasUnsavedChanges = true;
            Assert.Equal("FEBuilderGBA - same-name.gba *", vm.WindowTitle);

            CoreState.ROM = null;
            vm.UpdateFromRom();
            Assert.Equal("", vm.RomFilePath);
            Assert.False(vm.IsRomLoaded);
            Assert.Equal("FEBuilderGBA", vm.WindowTitle);
        }
        finally { CoreState.ROM = previousRom; }
    }

    [Fact]
    public void UpdateRomFilename_TracksNewSaveAsLocationWithoutChangingDirtyState()
    {
        var previousRom = CoreState.ROM;
        try
        {
            CoreState.ROM = new ROM { Filename = Path.Combine("first", "same-name.gba") };
            var vm = new MainWindowViewModel();
            vm.UpdateFromRom();
            vm.HasUnsavedChanges = true;

            CoreState.ROM.Filename = Path.Combine("second", "same-name.gba");
            vm.UpdateRomFilename();

            Assert.Equal(Path.GetFullPath(CoreState.ROM.Filename), vm.RomFilePath);
            Assert.Equal("FEBuilderGBA - same-name.gba *", vm.WindowTitle);

            CoreState.ROM.Filename = "provider-only.gba";
            vm.UpdateFromRom(hasLocalPath: false);
            Assert.Equal("provider-only.gba", vm.RomFilePath);
            Assert.Equal("provider-only.gba", vm.RomFilename);
        }
        finally { CoreState.ROM = previousRom; }
    }

    [AvaloniaFact]
    public void PathField_IsHiddenWithoutRomAndAllowsLongPathSelectionWithoutWideningWindow()
    {
        var previousRom = CoreState.ROM;
        var previousWindow = WindowManager.Instance.MainWindow;
        bool previousSmoke = App.SmokeTestMode;
        string? previousStartup = App.StartupRomPath;
        string? previousProject = App.StartupProjectDir;
        MainWindow? window = null;
        try
        {
            CoreState.ROM = null;
            App.SmokeTestMode = true;
            App.StartupRomPath = null;
            App.StartupProjectDir = null;
            window = new MainWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var field = window.FindControl<TextBox>("RomFilePathTextBox");
            Assert.NotNull(field);
            Assert.False(field!.IsEffectivelyVisible);

            string path = Path.GetFullPath(Path.Combine(new string('x', 180), new string('y', 180), "same-name.gba"));
            CoreState.ROM = new ROM { Filename = path };
            var vm = Assert.IsType<MainWindowViewModel>(window.DataContext);
            vm.UpdateFromRom();
            Dispatcher.UIThread.RunJobs();

            Assert.True(field.IsEffectivelyVisible);
            Assert.Equal(path, field.Text);
            Assert.True(field.IsReadOnly);
            Assert.Equal(TextWrapping.NoWrap, field.TextWrapping);
            Assert.Equal("Main_RomFilePath_Input", AutomationProperties.GetAutomationId(field));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(field)));
            Assert.InRange(field.Bounds.Width, 1, 900);
            Assert.Equal(900, window.ClientSize.Width);
            Assert.True(field.Focus());
            window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
            window.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control);
            Assert.Equal(path, field.SelectedText);
            window.KeyTextInput("replacement");
            Assert.Equal(path, field.Text);
        }
        finally
        {
            CoreState.ROM = null;
            window?.Close();
            WindowManager.Instance.MainWindow = previousWindow;
            App.SmokeTestMode = previousSmoke;
            App.StartupRomPath = previousStartup;
            App.StartupProjectDir = previousProject;
            CoreState.ROM = previousRom;
        }
    }
}
