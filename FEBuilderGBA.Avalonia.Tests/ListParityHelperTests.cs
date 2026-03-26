using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Tests for ListParityHelper comparison logic and AddressListControl.GetItems().
/// </summary>
public class ListParityHelperTests
{
    static List<AddrResult> MakeItems(int count, uint baseAddr = 0x1000, string prefix = "Item")
    {
        var list = new List<AddrResult>();
        for (int i = 0; i < count; i++)
            list.Add(new AddrResult(baseAddr + (uint)(i * 4), $"{prefix} {i}"));
        return list;
    }

    // ---------------------------------------------------------------
    // AddressListControl.GetItems()
    // ---------------------------------------------------------------

    [AvaloniaFact]
    public void GetItems_EmptyByDefault()
    {
        var control = new AddressListControl();
        var items = control.GetItems();
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [AvaloniaFact]
    public void GetItems_ReturnsLoadedItems()
    {
        var control = new AddressListControl();
        var original = MakeItems(5);
        control.SetItems(original);

        var retrieved = control.GetItems();
        Assert.Equal(5, retrieved.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(original[i].addr, retrieved[i].addr);
            Assert.Equal(original[i].name, retrieved[i].name);
        }
    }

    [AvaloniaFact]
    public void GetItems_ReturnsDefensiveCopy()
    {
        var control = new AddressListControl();
        control.SetItems(MakeItems(3));

        var copy1 = control.GetItems();
        var copy2 = control.GetItems();
        Assert.NotSame(copy1, copy2);

        // Mutating the copy should not affect the original
        copy1.Clear();
        var copy3 = control.GetItems();
        Assert.Equal(3, copy3.Count);
    }

    // ---------------------------------------------------------------
    // ListParityHelper.CompareLists()
    // ---------------------------------------------------------------

    [Fact]
    public void CompareLists_IdenticalLists_Match()
    {
        var list1 = MakeItems(10);
        var list2 = MakeItems(10);

        var result = ListParityHelper.CompareLists("TestEditor", list1, list2);
        Assert.True(result.IsMatch);
        Assert.Equal(10, result.AvaloniaCount);
        Assert.Equal(10, result.WinFormsCount);
        Assert.Equal(10, result.TextMatches);
        Assert.Equal(-1, result.FirstAddrDiffIndex);
        Assert.Equal(-1, result.FirstTextDiffIndex);
    }

    [Fact]
    public void CompareLists_EmptyLists_Match()
    {
        var result = ListParityHelper.CompareLists("TestEditor", new List<AddrResult>(), new List<AddrResult>());
        Assert.True(result.IsMatch);
        Assert.Equal(0, result.AvaloniaCount);
        Assert.Equal(0, result.WinFormsCount);
    }

    [Fact]
    public void CompareLists_CountDifference_Mismatch()
    {
        var list1 = MakeItems(5);
        var list2 = MakeItems(3);

        var result = ListParityHelper.CompareLists("TestEditor", list1, list2);
        Assert.False(result.IsMatch);
        Assert.Equal(5, result.AvaloniaCount);
        Assert.Equal(3, result.WinFormsCount);
    }

    [Fact]
    public void CompareLists_TextDifference_Mismatch()
    {
        var list1 = MakeItems(5, prefix: "Avalonia");
        var list2 = MakeItems(5, prefix: "WinForms");

        var result = ListParityHelper.CompareLists("TestEditor", list1, list2);
        Assert.False(result.IsMatch);
        Assert.Equal(0, result.FirstTextDiffIndex);
        Assert.Contains("Avalonia", result.FirstTextDiffAvalonia);
        Assert.Contains("WinForms", result.FirstTextDiffWinForms);
    }

    [Fact]
    public void CompareLists_AddressDifference_Mismatch()
    {
        var list1 = MakeItems(5, baseAddr: 0x1000);
        var list2 = MakeItems(5, baseAddr: 0x2000);

        var result = ListParityHelper.CompareLists("TestEditor", list1, list2);
        Assert.False(result.IsMatch);
        Assert.Equal(0, result.FirstAddrDiffIndex);
    }

    [Fact]
    public void CompareLists_DiffAtMiddle_ReportsCorrectIndex()
    {
        var list1 = MakeItems(5);
        var list2 = MakeItems(5);
        // Change item 3 text
        list2[3] = new AddrResult(list2[3].addr, "DIFFERENT");

        var result = ListParityHelper.CompareLists("TestEditor", list1, list2);
        Assert.False(result.IsMatch);
        Assert.Equal(3, result.FirstTextDiffIndex);
        Assert.Equal(4, result.TextMatches); // items 0,1,2,4 match
    }

    // ---------------------------------------------------------------
    // ListParityResult.FormatResult()
    // ---------------------------------------------------------------

    [Fact]
    public void FormatResult_Match_ContainsMatchStatus()
    {
        var result = new ListParityResult
        {
            EditorName = "TestEditor",
            AvaloniaCount = 10,
            WinFormsCount = 10,
            TextMatches = 10,
            IsMatch = true,
            FirstAddrDiffIndex = -1,
            FirstTextDiffIndex = -1,
        };

        string formatted = result.FormatResult();
        Assert.Contains("LISTPARITY:", formatted);
        Assert.Contains("TestEditor", formatted);
        Assert.Contains("MATCH", formatted);
        Assert.Contains("avalonia_count=10", formatted);
        Assert.Contains("winforms_count=10", formatted);
    }

    [Fact]
    public void FormatResult_Mismatch_ContainsDiffDetails()
    {
        var result = new ListParityResult
        {
            EditorName = "TestEditor",
            AvaloniaCount = 10,
            WinFormsCount = 8,
            TextMatches = 7,
            IsMatch = false,
            FirstAddrDiffIndex = -1,
            FirstTextDiffIndex = 2,
            FirstTextDiffAvalonia = "Foo",
            FirstTextDiffWinForms = "Bar",
        };

        string formatted = result.FormatResult();
        Assert.Contains("MISMATCH", formatted);
        Assert.Contains("count differs", formatted);
        Assert.Contains("first text diff at [2]", formatted);
    }

    // ---------------------------------------------------------------
    // ListParityHelper.HasMapping() and GetMapping()
    // ---------------------------------------------------------------

    [Fact]
    public void HasMapping_KnownEditor_ReturnsTrue()
    {
        Assert.True(ListParityHelper.HasMapping("UnitEditorView"));
        Assert.True(ListParityHelper.HasMapping("ItemEditorView"));
        Assert.True(ListParityHelper.HasMapping("ClassEditorView"));
    }

    [Fact]
    public void HasMapping_UnknownEditor_ReturnsFalse()
    {
        Assert.False(ListParityHelper.HasMapping("NonExistentView"));
    }

    [Fact]
    public void GetMapping_KnownEditor_ReturnsCorrectFormAndMethod()
    {
        var mapping = ListParityHelper.GetMapping("UnitEditorView");
        Assert.NotNull(mapping);
        Assert.Equal("UnitForm", mapping.Value.FormType);
        Assert.Equal("MakeList", mapping.Value.MethodName);
    }

    [Fact]
    public void GetMapping_UnknownEditor_ReturnsNull()
    {
        Assert.Null(ListParityHelper.GetMapping("NonExistentView"));
    }

    [Fact]
    public void GetAllMappedEditors_ReturnsNonEmptyCollection()
    {
        var editors = ListParityHelper.GetAllMappedEditors();
        Assert.NotEmpty(editors);
        Assert.Contains("UnitEditorView", editors);
    }
}
