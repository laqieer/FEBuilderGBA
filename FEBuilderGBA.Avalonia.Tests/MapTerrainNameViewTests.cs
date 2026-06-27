// #1601 headless Avalonia tests for the multibyte/JP Map Terrain Name editor.
//
// Verifies the editor exposes an EDITABLE Name string TextBox (MapTerrainName_Name_Input)
// and that the raw Name Pointer box is read-only diagnostics (MapTerrainName_Pointer_Input).
// Control-presence assertions run without a ROM.
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Views;
using Xunit;

namespace FEBuilderGBA.Avalonia.Tests;

[Collection("SharedState")]
public class MapTerrainNameViewTests : IClassFixture<RomFixture>
{
    [AvaloniaFact]
    public void NameField_IsEditable_And_PointerField_IsReadOnly()
    {
        var view = new MapTerrainNameView();

        // The entry list is still present.
        Assert.NotNull(view.FindControl<AddressListControl>("EntryList"));

        // The Name field is now an EDITABLE TextBox (was a read-only TextBlock).
        var nameBox = view.FindControl<TextBox>("NameBox");
        Assert.NotNull(nameBox);
        Assert.False(nameBox!.IsReadOnly, "the terrain Name TextBox must be editable");

        // The Name Pointer box is read-only diagnostics.
        var pointerBox = view.FindControl<TextBox>("PointerBox");
        Assert.NotNull(pointerBox);
        Assert.True(pointerBox!.IsReadOnly, "the Name Pointer box must be read-only diagnostics");

        // AutomationIds wired for both fields + the Write button.
        var ids = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet();
        Assert.Contains("MapTerrainName_Name_Input", ids);
        Assert.Contains("MapTerrainName_Pointer_Input", ids);
        Assert.Contains("MapTerrainName_Write_Button", ids);
    }
}
