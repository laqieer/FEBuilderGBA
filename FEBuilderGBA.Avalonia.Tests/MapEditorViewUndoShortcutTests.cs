using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class MapEditorViewUndoShortcutTests : System.IDisposable
{
    const uint EditedAddr = 0x300;

    readonly ROM? _prevRom = CoreState.ROM;
    readonly Undo? _prevUndo = CoreState.Undo;
    readonly IAppServices? _prevServices = CoreState.Services;
    readonly Window? _prevMainWindow = WindowManager.Instance.MainWindow;

    public void Dispose()
    {
        CoreState.ROM = _prevRom;
        CoreState.Undo = _prevUndo;
        CoreState.Services = _prevServices;
        WindowManager.Instance.MainWindow = _prevMainWindow;
    }

    sealed class SilentServices : IAppServices
    {
        public string? LastError;
        public void ShowError(string message) => LastError = message;
        public void ShowInfo(string message) { }
        public bool ShowQuestion(string message) => true;
        public bool ShowYesNo(string message) => true;
        public void RunOnUIThread(System.Action action) => action();
        public bool IsMainThread() => true;
    }

    sealed class TestableMapEditorView : MapEditorView
    {
        public int AppliedCount { get; private set; }
        public bool CallBaseAfterApplied { get; set; }

        protected override void OnEditorUndoApplied()
        {
            AppliedCount++;
            if (CallBaseAfterApplied)
                base.OnEditorUndoApplied();
        }
    }

    static ROM MakeUndoableRom()
    {
        byte[] data = new byte[0x1000];
        data[EditedAddr] = 0x11;
        data[EditedAddr + 1] = 0x00;

        var rom = new ROM();
        rom.SwapNewROMDataDirect(data);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();

        var undoData = CoreState.Undo.NewUndoData("MapEditor.PaintTile");
        rom.write_u16(EditedAddr, 0x2233, undoData);
        CoreState.Undo.Push(undoData);
        return rom;
    }

    [AvaloniaFact]
    public void UndoKeyGestures_AreRecognizedForCtrlAndMetaZOnly()
    {
        Assert.True(MapEditorView.IsUndoGesture(Key.Z, KeyModifiers.Control));
        Assert.True(MapEditorView.IsUndoGesture(Key.Z, KeyModifiers.Meta));
        Assert.True(MapEditorView.IsUndoGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Meta));

        Assert.False(MapEditorView.IsUndoGesture(Key.Z, KeyModifiers.None));
        Assert.False(MapEditorView.IsUndoGesture(Key.Z, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.False(MapEditorView.IsUndoGesture(Key.Y, KeyModifiers.Control));
    }

    [AvaloniaFact]
    public void UndoShortcut_EmptyStack_DoesNotRefreshOrHandle()
    {
        CoreState.Undo = new Undo();
        CoreState.Services = new SilentServices();
        var view = new TestableMapEditorView();

        Assert.False(view.HandleEditorKeyDown(Key.Z, KeyModifiers.Control));
        Assert.Equal(0, view.AppliedCount);
        Assert.Equal(0, CoreState.Undo.Postion);
    }

    [AvaloniaFact]
    public void UndoShortcut_TextBoxSource_DoesNotConsumeLocalTextUndo()
    {
        CoreState.Services = new SilentServices();
        ROM rom = MakeUndoableRom();
        var view = new TestableMapEditorView();
        var textBox = new TextBox();

        Assert.False(view.HandleEditorKeyDown(Key.Z, KeyModifiers.Control, textBox));

        Assert.Equal<uint>(0x33, rom.u8(EditedAddr));
        Assert.Equal<uint>(0x22, rom.u8(EditedAddr + 1));
        Assert.Equal(1, CoreState.Undo!.Postion);
        Assert.Equal(0, view.AppliedCount);
    }

    [AvaloniaFact]
    public void UndoShortcut_CtrlZ_RevertsCommittedRomBytesAndRefreshes()
    {
        CoreState.Services = new SilentServices();
        ROM rom = MakeUndoableRom();
        var view = new TestableMapEditorView();

        Assert.Equal<uint>(0x33, rom.u8(EditedAddr));
        Assert.Equal<uint>(0x22, rom.u8(EditedAddr + 1));
        Assert.Equal(1, CoreState.Undo!.Postion);

        Assert.True(view.HandleEditorKeyDown(Key.Z, KeyModifiers.Control));

        Assert.Equal<uint>(0x11, rom.u8(EditedAddr));
        Assert.Equal<uint>(0x00, rom.u8(EditedAddr + 1));
        Assert.Equal(0, CoreState.Undo.Postion);
        Assert.Equal(1, view.AppliedCount);
    }

    [AvaloniaFact]
    public void UndoShortcut_MetaZ_UsesSameEditorLocalUndoPath()
    {
        CoreState.Services = new SilentServices();
        ROM rom = MakeUndoableRom();
        var view = new TestableMapEditorView();

        Assert.True(view.HandleEditorKeyDown(Key.Z, KeyModifiers.Meta));

        Assert.Equal<uint>(0x11, rom.u8(EditedAddr));
        Assert.Equal(0, CoreState.Undo!.Postion);
        Assert.Equal(1, view.AppliedCount);
    }

    [AvaloniaFact]
    public void UndoShortcut_UpdatesMainWindowDirtyStateThroughUndoService()
    {
        CoreState.Services = new SilentServices();
        MakeUndoableRom();
        var mainVm = new MainWindowViewModel { HasUnsavedChanges = true };
        WindowManager.Instance.MainWindow = new Window { DataContext = mainVm };
        var view = new TestableMapEditorView { CallBaseAfterApplied = true };

        Assert.True(CoreState.Undo!.IsModified);
        Assert.True(mainVm.HasUnsavedChanges);

        Assert.True(view.HandleEditorKeyDown(Key.Z, KeyModifiers.Control));

        Assert.False(CoreState.Undo.IsModified);
        Assert.False(mainVm.HasUnsavedChanges);
        Assert.Equal(1, view.AppliedCount);
    }
}
