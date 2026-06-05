// SPDX-License-Identifier: GPL-3.0-or-later
// VM + smoke-guard tests for the map-PLIST label resolver rewire (#952).
//
//  * VM tests: MapPointerViewModel (across ALL filter type indices) and
//    MapChangeViewModel produce RESOLVED labels (TYPE MapName / NULL /
//    -EMPTY- / UNK) — never a bare 0x… pointer.
//  * Smoke guard: a narrowed source scan asserting the LIST-BUILDING methods
//    (MapPointerViewModel.LoadMapPointerList, MapChangeViewModel.LoadMapChangeList,
//    ListParityHelper.BuildMapPointerList / BuildMapChangeList) no longer emit
//    a bare 0x{...:X08} row label. Scoped to the list builders only — detail
//    / report fields (GetDataReport, GetRawRomReport) keep their 0x… formatting.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FEBuilderGBA;
using FEBuilderGBA.Avalonia.ViewModels;

namespace FEBuilderGBA.Avalonia.Tests;

public class MapPListResolverParityTests : IClassFixture<RomFixture>
{
    readonly RomFixture _rom;

    public MapPListResolverParityTests(RomFixture rom)
    {
        _rom = rom;
    }

    // -----------------------------------------------------------------
    // VM: MapPointer across ALL filter type indices.
    // -----------------------------------------------------------------

    [Fact]
    public void MapPointer_AllFilters_ProduceResolvedLabels()
    {
        if (!_rom.IsAvailable) return; // skip

        var vm = new MapPointerViewModel();
        int filterCount = vm.GetPlistTypeNames().Count;
        Assert.True(filterCount >= 7, "expected at least 7 PLIST filters");

        for (int typeIndex = 0; typeIndex < filterCount; typeIndex++)
        {
            List<AddrResult> rows = vm.LoadMapPointerList(typeIndex);
            // Some filter tables may be empty on a given ROM (e.g. WORLDMAP on
            // non-FE6); that's fine. When rows exist, none may be a raw 0x…
            // pointer label.
            foreach (AddrResult r in rows)
            {
                AssertResolvedLabel(r.name, $"MapPointer filter {typeIndex}");
            }
        }
    }

    [Fact]
    public void MapPointer_DefaultMapFilter_HasRows_AndResolved()
    {
        if (!_rom.IsAvailable) return;

        var vm = new MapPointerViewModel();
        List<AddrResult> rows = vm.LoadMapPointerList(0); // MAP
        Assert.NotEmpty(rows);
        foreach (AddrResult r in rows)
            AssertResolvedLabel(r.name, "MapPointer MAP filter");

        // At least one MAP-typed or NULL/EMPTY row is expected — prove the
        // resolver actually named a map (split) or scanned a field (non-split)
        // rather than leaving everything as "-EMPTY-"/raw.
        bool anyTypeLabel = rows.Exists(r =>
            r.name.Contains(" MAP ") || r.name.Contains(" CONFIG ") ||
            r.name.Contains(" MAPCHANGE ") || r.name.Contains(" EVENT ") ||
            r.name.Contains(" OBJ ") || r.name.Contains(" PAL") ||
            r.name.Contains(" ANIME"));
        Assert.True(anyTypeLabel, "expected at least one resolved TYPE label in the MAP filter");
    }

    // -----------------------------------------------------------------
    // VM: MapChange.
    // -----------------------------------------------------------------

    [Fact]
    public void MapChange_List_ProducesResolvedLabels()
    {
        if (!_rom.IsAvailable) return;

        var vm = new MapChangeViewModel();
        List<AddrResult> rows = vm.LoadMapChangeList();
        Assert.NotEmpty(rows);
        foreach (AddrResult r in rows)
            AssertResolvedLabel(r.name, "MapChange list");

        // Prove at least one row resolved to a MAPCHANGE map name (or NULL).
        bool anyResolved = rows.Exists(r =>
            r.name.Contains(" MAPCHANGE ") || r.name.EndsWith(" NULL") ||
            r.name.Contains(" UNK") || r.name.Contains(" -EMPTY-"));
        Assert.True(anyResolved, "expected MAPCHANGE/NULL/UNK/-EMPTY- labels");
    }

    // -----------------------------------------------------------------
    // Smoke guard: list-building source must not emit a bare 0x{...:X08} row.
    // -----------------------------------------------------------------

    [Fact]
    public void ListBuilders_DoNotEmit_RawHexPointerLabel()
    {
        // MapPointerViewModel.LoadMapPointerList
        AssertNoRawHexInMethod(
            Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "ViewModels", "MapPointerViewModel.cs"),
            "LoadMapPointerList");

        // MapChangeViewModel.LoadMapChangeList
        AssertNoRawHexInMethod(
            Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "ViewModels", "MapChangeViewModel.cs"),
            "LoadMapChangeList");

        // ListParityHelper.BuildMapPointerList + BuildMapChangeList
        string parity = Path.Combine(FindRepoRoot(), "FEBuilderGBA.Avalonia", "Services", "ListParityHelper.cs");
        AssertNoRawHexInMethod(parity, "BuildMapPointerList");
        AssertNoRawHexInMethod(parity, "BuildMapChangeList");
    }

    // =================================================================
    // Helpers.
    // =================================================================

    static void AssertResolvedLabel(string label, string ctx)
    {
        Assert.NotNull(label);
        // A resolved row is "{idHex} {TYPE MapName}" / "{idHex} NULL" /
        // "{idHex} -EMPTY-" / "{idHex} UNK". The id prefix itself uses
        // U.ToHexString (e.g. "0x01"), which is allowed; what's forbidden is
        // an 8-digit raw POINTER like "0x08123456" as the value.
        Assert.DoesNotContain("0x08", label);
        // Belt-and-braces: no 6-hex-digit run (pointer tail) after the id.
        Assert.False(Regex.IsMatch(label, @"0x[0-9A-Fa-f]{8}"),
            $"{ctx}: row label still contains a raw 0x… pointer: '{label}'");
    }

    /// <summary>
    /// Read the source of <paramref name="methodName"/> from <paramref name="file"/>
    /// (a brace-balanced slice) and assert it contains no <c>0x{...:X08}</c>
    /// or <c>.ToString("X08")</c> row-label formatting. Scoped to ONE method
    /// so detail/report formatters elsewhere in the same file are untouched.
    /// </summary>
    static void AssertNoRawHexInMethod(string file, string methodName)
    {
        Assert.True(File.Exists(file), $"source not found: {file}");
        string src = File.ReadAllText(file);
        string body = ExtractMethodBody(src, methodName);
        Assert.False(string.IsNullOrEmpty(body), $"method {methodName} not found in {file}");

        Assert.DoesNotContain(":X08}", body);
        Assert.DoesNotContain("\"X08\"", body);
    }

    /// <summary>
    /// Extract a method's body by finding its signature line and walking
    /// brace balance to the matching close. Good enough for these well-formed
    /// editor sources.
    /// </summary>
    static string ExtractMethodBody(string src, string methodName)
    {
        int sig = src.IndexOf(methodName + "(");
        if (sig < 0) return "";
        int open = src.IndexOf('{', sig);
        if (open < 0) return "";
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}')
            {
                depth--;
                if (depth == 0) return src.Substring(open, i - open + 1);
            }
        }
        return "";
    }

    static string FindRepoRoot()
    {
        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        for (int i = 0; i < 12; i++)
        {
            if (File.Exists(Path.Combine(dir, "FEBuilderGBA.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir)!;
        }
        throw new System.InvalidOperationException("Repo root not found");
    }
}
