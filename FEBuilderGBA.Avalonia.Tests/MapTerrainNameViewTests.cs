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
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.ViewModels;
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

    // After writing a NON-first entry, the list rebuild must keep the edited row
    // selected (regression: plain SetItems -> SelectFirst clobbered CurrentAddr).
    [AvaloniaFact]
    public void Write_PreservesSelectionOnNonFirstEntry_FE8J()
    {
        RomTestHelper.WithRom("FE8J", () =>
        {
            var view = new MapTerrainNameView();
            var entryList = view.FindControl<AddressListControl>("EntryList")!;
            var nameBox = view.FindControl<TextBox>("NameBox")!;

            // Build the list and pick a NON-first entry that holds a real string.
            var probe = new MapTerrainNameViewModel();
            var list = probe.LoadList();
            uint targetAddr = 0;
            for (int i = 1; i < list.Count; i++)
            {
                probe.LoadEntry(list[i].addr);
                if (!string.IsNullOrEmpty(probe.TerrainName)) { targetAddr = list[i].addr; break; }
            }
            Assert.NotEqual(0u, targetAddr);

            // Populate the list in the real control and select the target row.
            entryList.SetItems(list);
            Assert.True(entryList.SelectAddress(targetAddr));

            var vm = Assert.IsType<MapTerrainNameViewModel>(view.DataViewModel);
            Assert.Equal(targetAddr, vm.CurrentAddr);

            // Edit + write via the real Write button (ASCII is encoder-stable).
            nameBox.Text = "PRESERVE";
            var writeBtn = view.GetLogicalDescendants().OfType<Button>()
                .First(b => AutomationProperties.GetAutomationId(b) == "MapTerrainName_Write_Button");

            var undo = CoreState.Undo.NewUndoData("test");
            using (ROM.BeginUndoScope(undo))
            {
                // Raise the wired Click="Write_Click" handler (headless has no real
                // pointer input, so invoke Button.OnClick directly).
                var click = typeof(Button).GetMethod("OnClick",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                click!.Invoke(writeBtn, null);
            }

            // The edited row must still be selected (NOT reset to the first entry),
            // and the VM must still point at the edited slot.
            Assert.Equal(targetAddr, vm.CurrentAddr);
            Assert.Equal("PRESERVE", vm.TerrainName);
        });
    }
}
