using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using FEBuilderGBA.Avalonia.Controls;
using FEBuilderGBA.Avalonia.Services;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests;

/// <summary>
/// Tests for ListParityHelper comparison logic and AddressListControl.GetItems().
/// </summary>
public class ListParityHelperTests : IClassFixture<RomFixture>
{
    readonly RomFixture _rom;

    public ListParityHelperTests(RomFixture rom)
    {
        _rom = rom;
    }

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

        // The returned IReadOnlyList should not allow mutation;
        // verify we get consistent counts on repeated calls.
        Assert.Equal(3, copy1.Count);
        Assert.Equal(3, copy2.Count);
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

    // ---------------------------------------------------------------
    // ROM-backed: reference builders vs Avalonia VM list loaders
    // These tests verify that BuildReferenceList() output matches
    // the corresponding ViewModel Load*List() for a real ROM.
    // ---------------------------------------------------------------

    [Fact]
    public void BuildReferenceList_UnitEditor_MatchesViewModel()
    {
        if (!_rom.IsAvailable) return; // skip if no ROM

        var vm = new UnitEditorViewModel();
        var vmList = vm.LoadUnitList();
        var refList = ListParityHelper.BuildReferenceList("UnitEditorView");

        Assert.NotNull(refList);
        Assert.Equal(vmList.Count, refList.Count);
        for (int i = 0; i < vmList.Count; i++)
        {
            Assert.Equal(vmList[i].addr, refList[i].addr);
            Assert.Equal(vmList[i].name, refList[i].name);
        }
    }

    [Fact]
    public void BuildReferenceList_ItemEditor_MatchesViewModel()
    {
        if (!_rom.IsAvailable) return;

        var vm = new ItemEditorViewModel();
        var vmList = vm.LoadItemList();
        var refList = ListParityHelper.BuildReferenceList("ItemEditorView");

        Assert.NotNull(refList);
        Assert.Equal(vmList.Count, refList.Count);
        for (int i = 0; i < vmList.Count; i++)
        {
            Assert.Equal(vmList[i].addr, refList[i].addr);
            Assert.Equal(vmList[i].name, refList[i].name);
        }
    }

    [Fact]
    public void BuildReferenceList_ClassEditor_MatchesViewModel()
    {
        if (!_rom.IsAvailable) return;

        var vm = new ClassEditorViewModel();
        var vmList = vm.LoadClassList();
        var refList = ListParityHelper.BuildReferenceList("ClassEditorView");

        Assert.NotNull(refList);
        Assert.Equal(vmList.Count, refList.Count);
        for (int i = 0; i < vmList.Count; i++)
        {
            Assert.Equal(vmList[i].addr, refList[i].addr);
            Assert.Equal(vmList[i].name, refList[i].name);
        }
    }

    [Fact]
    public void BuildReferenceList_PortraitViewer_MatchesViewModel()
    {
        if (!_rom.IsAvailable) return;

        var vm = new PortraitViewerViewModel();
        var vmList = vm.LoadPortraitList();
        var refList = ListParityHelper.BuildReferenceList("PortraitViewerView");

        Assert.NotNull(refList);
        Assert.Equal(vmList.Count, refList.Count);
        for (int i = 0; i < vmList.Count; i++)
        {
            Assert.Equal(vmList[i].addr, refList[i].addr);
            Assert.Equal(vmList[i].name, refList[i].name);
        }
    }

    [Fact]
    public void BuildReferenceList_SoundRoom_MatchesViewModel()
    {
        if (!_rom.IsAvailable) return;

        var vm = new SoundRoomViewerViewModel();
        var vmList = vm.LoadSoundRoomList();
        var refList = ListParityHelper.BuildReferenceList("SoundRoomViewerView");

        Assert.NotNull(refList);
        Assert.Equal(vmList.Count, refList.Count);
        for (int i = 0; i < vmList.Count; i++)
        {
            Assert.Equal(vmList[i].addr, refList[i].addr);
            Assert.Equal(vmList[i].name, refList[i].name);
        }
    }

    [Fact]
    public void BuildReferenceList_GenericEnemyPortrait_MatchesViewModel()
    {
        if (!_rom.IsAvailable) return;

        var vm = new ImageGenericEnemyPortraitViewModel();
        var vmList = vm.LoadList();
        var refList = ListParityHelper.BuildReferenceList("ImageGenericEnemyPortraitView");

        Assert.NotNull(refList);
        Assert.Equal(vmList.Count, refList.Count);
        for (int i = 0; i < vmList.Count; i++)
        {
            Assert.Equal(vmList[i].addr, refList[i].addr);
            Assert.Equal(vmList[i].name, refList[i].name);
        }
    }

    // ---------------------------------------------------------------
    // SoundBossBGM — lockstep VM vs golden + unresolved-id label (#962)
    // ---------------------------------------------------------------

    /// <summary>
    /// The Boss BGM VM list and the golden ListParityHelper builder must
    /// stay byte-for-byte identical (the lockstep song-name restore, #961).
    /// </summary>
    [Fact]
    public void BuildReferenceList_SoundBossBGM_MatchesViewModel()
    {
        if (!_rom.IsAvailable) return;

        var vm = new SoundBossBGMViewerViewModel();
        var vmList = vm.LoadSoundBossBGMList();
        var refList = ListParityHelper.BuildReferenceList("SoundBossBGMViewerView");

        Assert.NotNull(refList);
        Assert.Equal(vmList.Count, refList.Count);
        for (int i = 0; i < vmList.Count; i++)
        {
            Assert.Equal(vmList[i].addr, refList[i].addr);
            Assert.Equal(vmList[i].name, refList[i].name);
        }
    }

    /// <summary>
    /// #962 review #2/#3: an unresolved song id must NOT duplicate the hex
    /// with a "Song 0x.." placeholder (the old "1BSong 0x1B" bug). Every
    /// Boss BGM label must contain " : " (the song-hex segment) and must NEVER
    /// contain the "Song 0x" placeholder — for an unknown song the label
    /// collapses to just the song hex (matching WinForms' empty-string append).
    /// </summary>
    [Fact]
    public void SoundBossBGM_Labels_NeverContainSongPlaceholder()
    {
        if (!_rom.IsAvailable) return;

        var vm = new SoundBossBGMViewerViewModel();
        var vmList = vm.LoadSoundBossBGMList();

        foreach (var row in vmList)
        {
            // The placeholder "Song 0x" must never leak into the label.
            Assert.DoesNotContain("Song 0x", row.name);
            // The colon-separated song-hex segment is always present.
            Assert.Contains(" : ", row.name);
        }
    }
}
