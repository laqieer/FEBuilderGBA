using System.Reflection;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FEBuilderGBA.Avalonia.ViewModels;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public sealed class UnitPaletteViewTests : IDisposable
{
    const uint EntryAddress = 0x300;

    readonly ROM? _savedRom = CoreState.ROM;
    readonly Undo? _savedUndo = CoreState.Undo;
    readonly IAppServices? _savedServices = CoreState.Services;

    public void Dispose()
    {
        CoreStateTestState.RestoreRom(_savedRom);
        CoreStateTestState.RestoreUndo(_savedUndo);
        CoreStateTestState.RestoreServices(_savedServices);
    }

    sealed class RecordingServices : IAppServices
    {
        public string? LastError { get; private set; }
        public string? LastInfo { get; private set; }

        public void ShowError(string message) => LastError = message;
        public void ShowInfo(string message) => LastInfo = message;
        public bool ShowQuestion(string message) => false;
        public bool ShowYesNo(string message) => false;
        public void RunOnUIThread(Action action) => action();
        public bool IsMainThread() => true;
    }

    [AvaloniaFact]
    public void Labels_UsePromotedClassTerminology()
    {
        var view = new UnitPaletteView();

        Assert.Equal("Input (decimal or 0x hex)", view.FindControl<TextBlock>("InputFormatHeader")?.Text);
        Assert.Equal("Hex value", view.FindControl<TextBlock>("HexValueHeader")?.Text);
        Assert.Equal("Promoted Class 1:", view.FindControl<TextBlock>("PromotedClass1Label")?.Text);
        Assert.Equal("Promoted Class 2:", view.FindControl<TextBlock>("PromotedClass2Label")?.Text);
        Assert.Equal("Promoted Class 3:", view.FindControl<TextBlock>("PromotedClass3Label")?.Text);
        Assert.Equal("Promoted Class 4:", view.FindControl<TextBlock>("PromotedClass4Label")?.Text);
    }

    [AvaloniaFact]
    public void NamedLabels_HaveStableAutomationIds()
    {
        var view = new UnitPaletteView();
        var expected = new Dictionary<string, string>
        {
            ["InputFormatHeader"] = "UnitPalette_InputFormat_Label",
            ["HexValueHeader"] = "UnitPalette_HexValue_Label",
            ["PromotedClass1Label"] = "UnitPalette_PromotedClass1_Label",
            ["PromotedClass2Label"] = "UnitPalette_PromotedClass2_Label",
            ["PromotedClass3Label"] = "UnitPalette_PromotedClass3_Label",
            ["PromotedClass4Label"] = "UnitPalette_PromotedClass4_Label",
        };

        foreach (var pair in expected)
        {
            TextBlock? label = view.FindControl<TextBlock>(pair.Key);
            Assert.NotNull(label);
            Assert.Equal(pair.Value, AutomationProperties.GetAutomationId(label));
        }
    }

    [AvaloniaFact]
    public void DecimalAndHexInput_UpdateCanonicalHexCompanion()
    {
        var view = new UnitPaletteView();
        var input = Assert.IsType<TextBox>(view.FindControl<Control>("BaseClass1Box"));
        var hex = view.FindControl<TextBlock>("BaseClass1HexLabel");
        Assert.NotNull(hex);

        input.Text = "66";
        Assert.Equal("0x42", hex!.Text);

        input.Text = "0X2a";
        Assert.Equal("0x2A", hex.Text);

        input.Text = "FF";
        Assert.Equal("Invalid", hex.Text);
    }

    [AvaloniaFact]
    public async Task LanguageChange_AfterInvalidReattach_PreservesCorrectedHexValue()
    {
        var view = new UnitPaletteView();
        var input = Assert.IsType<TextBox>(view.FindControl<Control>("BaseClass1Box"));
        var hex = view.FindControl<TextBlock>("BaseClass1HexLabel");
        Assert.NotNull(hex);

        var firstWindow = new Window { Content = view };
        firstWindow.Show();
        input.Text = "FF";
        Assert.Equal("Invalid", hex!.Text);
        firstWindow.Content = null;
        firstWindow.Close();

        var secondWindow = new Window { Content = view };
        secondWindow.Show();
        try
        {
            input.Text = "66";
            Assert.Equal("0x42", hex.Text);

            CoreState.RaiseLanguageChanged();
            await Dispatcher.UIThread.InvokeAsync(() => { });

            Assert.Equal("0x42", hex.Text);
        }
        finally
        {
            secondWindow.Close();
        }
    }

    [AvaloniaFact]
    public void Write_AcceptsMixedDecimalAndHexInput()
    {
        var view = CreateLoadedView(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, out ROM rom, out RecordingServices services);

        SetText(view, "TraineeClassBox", "0x42");
        SetText(view, "BaseClass1Box", "67");
        SetText(view, "BaseClass2Box", "0x44");
        SetText(view, "AdvancedClass1Box", "69");
        SetText(view, "AdvancedClass2Box", "0x46");
        SetText(view, "AdvancedClass3Box", "71");
        SetText(view, "AdvancedClass4Box", "0x48");

        InvokeWrite(view);

        Assert.Equal(new byte[] { 66, 67, 68, 69, 70, 71, 72 },
            rom.Data.Skip((int)EntryAddress).Take(7).ToArray());
        Assert.Null(services.LastError);
        Assert.NotNull(services.LastInfo);
    }

    [AvaloniaFact]
    public void Write_InvalidInputReportsErrorWithoutMutation()
    {
        byte[] original = { 1, 2, 3, 4, 5, 6, 7 };
        var view = CreateLoadedView(original, out ROM rom, out RecordingServices services);
        UnitPaletteViewModel vm = GetViewModel(view);

        SetText(view, "BaseClass1Box", "0x100");
        InvokeWrite(view);

        Assert.Equal(original, rom.Data.Skip((int)EntryAddress).Take(7).ToArray());
        Assert.Equal(1u, vm.TraineeClass);
        Assert.Equal(2u, vm.BaseClass1);
        Assert.Equal(3u, vm.BaseClass2);
        Assert.NotNull(services.LastError);
        Assert.False(CoreState.Undo!.IsModified);
    }

    static UnitPaletteView CreateLoadedView(
        byte[] values,
        out ROM rom,
        out RecordingServices services)
    {
        byte[] data = new byte[0x1000];
        values.CopyTo(data, (int)EntryAddress);

        rom = new ROM();
        rom.SwapNewROMDataDirect(data);
        CoreState.ROM = rom;
        CoreState.Undo = new Undo();
        services = new RecordingServices();
        CoreState.Services = services;

        var view = new UnitPaletteView();
        UnitPaletteViewModel vm = GetViewModel(view);
        vm.LoadEntry(EntryAddress);
        InvokePrivate(view, "UpdateUI");
        return view;
    }

    static UnitPaletteViewModel GetViewModel(UnitPaletteView view)
    {
        FieldInfo? field = typeof(UnitPaletteView).GetField(
            "_vm",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<UnitPaletteViewModel>(field?.GetValue(view));
    }

    static void SetText(UnitPaletteView view, string name, string value)
    {
        var input = Assert.IsType<TextBox>(view.FindControl<Control>(name));
        input.Text = value;
    }

    static void InvokeWrite(UnitPaletteView view)
    {
        MethodInfo? method = typeof(UnitPaletteView).GetMethod(
            "Write_Click",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(view, new object?[] { null, new RoutedEventArgs() });
    }

    static void InvokePrivate(UnitPaletteView view, string methodName)
    {
        MethodInfo? method = typeof(UnitPaletteView).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(view, null);
    }
}
