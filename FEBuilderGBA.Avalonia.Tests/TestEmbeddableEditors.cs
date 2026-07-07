using System;
using Avalonia;
using Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests;

public class TestEmbeddableEditor : TranslatedUserControl, IEmbeddableEditor
{
    bool _loadedOnce;
    public static int CreatedCount;
    public static TestEmbeddableEditor? LastInstance;

    public TestEmbeddableEditor()
    {
        CreatedCount++;
        LastInstance = this;
        Content = new TextBlock { Text = "embeddable" };
    }

    public int NavigateCalls { get; private set; }
    public uint LastAddress { get; private set; }
    public int LoadCalls { get; private set; }
    public int SelectFirstItemCalls { get; private set; }

    public virtual EditorDescriptor Descriptor => new("Test Embeddable", 321, 123, MinWidth: 111, MinHeight: 99);
    public virtual object? DialogResult => null;
    public string ViewTitle => Descriptor.Title;
    public new bool IsLoaded => _loadedOnce;
    EventHandler? _closeRequested;
    public int CloseRequestedSubscriberCount => _closeRequested?.GetInvocationList().Length ?? 0;
    public event EventHandler? CloseRequested
    {
        add => _closeRequested += value;
        remove => _closeRequested -= value;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_loadedOnce)
        {
            _loadedOnce = true;
            LoadCalls++;
        }
    }

    public void NavigateTo(uint address)
    {
        LastAddress = address;
        NavigateCalls++;
    }

    public void SelectFirstItem() => SelectFirstItemCalls++;
    public void RequestClose() => _closeRequested?.Invoke(this, EventArgs.Empty);

    public static void Reset()
    {
        CreatedCount = 0;
        LastInstance = null;
    }
}

public sealed class ModalEmbeddableEditor : TestEmbeddableEditor
{
    public override EditorDescriptor Descriptor => new("Modal Embeddable", 222, 111, CanBeModal: true);
}

public sealed class ModalResultEmbeddableEditor : TestEmbeddableEditor
{
    public override EditorDescriptor Descriptor => new("Modal Result Embeddable", 222, 111, CanBeModal: true);
    public override object? DialogResult => Result;
    public string? Result { get; private set; }
    public void DismissWith(string? result)
    {
        Result = result;
        RequestClose();
    }
}

public sealed class PickableEmbeddableEditor : TestEmbeddableEditor, IPickableEditor
{
    public bool PickModeEnabled { get; private set; }
    public event Action<PickResult>? SelectionConfirmed;
    public void EnablePickMode() => PickModeEnabled = true;
    public void Confirm(PickResult result) => SelectionConfirmed?.Invoke(result);
}

public sealed class CoverEmbeddableEditor : TestEmbeddableEditor
{
    public override EditorDescriptor Descriptor => new("Cover", 100, 100);
}
