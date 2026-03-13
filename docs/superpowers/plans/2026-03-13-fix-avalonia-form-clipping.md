# Fix Avalonia Form Content Clipping

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure all Avalonia GUI forms display all fields without clipping by adding ScrollViewer wrappers and converting fixed sizes to minimum sizes.

**Architecture:** Two-part fix: (1) For all 357 Window views, convert `Width`/`Height` to use them as initial sizes while allowing windows to grow via `SizeToContent`. (2) For the 76 views lacking a `ScrollViewer`, wrap root content in `<ScrollViewer>` so content is always accessible.

**Tech Stack:** Avalonia 11.2.3 AXAML, Python scripting for batch transforms

---

## Root Cause

All 357 Avalonia Window views use fixed `Width`/`Height` attributes. 76 of those views have no `ScrollViewer`. When content exceeds the window size, fields are clipped with no way to scroll.

## Fix Strategy

For all 357 Window-based views:
1. Keep `Width`/`Height` as initial window dimensions (they serve as defaults when the window opens)
2. Add `SizeToContent="WidthAndHeight"` so the window auto-sizes to fit content
3. Add `MaxWidth` and `MaxHeight` constraints to prevent windows exceeding reasonable screen sizes

For the 76 views without ScrollViewer:
1. Wrap the root content element in `<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">`

## Chunk 1: Batch Transform Script

### Task 1: Write and run Python batch transform for all 357 views

**Files:**
- Modify: All 357 `FEBuilderGBA.Avalonia/Views/*.axaml` files

- [ ] **Step 1: Write the batch transform script**

Create `scripts/fix_avalonia_scrolling.py` that:
1. For each `.axaml` file in `FEBuilderGBA.Avalonia/Views/`:
   - If root is `<Window` with `Width`/`Height`: add `SizeToContent="WidthAndHeight"` if not present
   - If file has no `<ScrollViewer`: wrap the first child element of `<Window>` in `<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">`
2. Report changes made

- [ ] **Step 2: Run the script**

```bash
python3 scripts/fix_avalonia_scrolling.py
```

- [ ] **Step 3: Build to verify no XAML parse errors**

```bash
dotnet build FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj
```

- [ ] **Step 4: Fix any build errors**

- [ ] **Step 5: Run core tests**

```bash
dotnet test FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj
```

- [ ] **Step 6: Commit**

### Task 2: Add unit test for the transform

**Files:**
- Create: `FEBuilderGBA.Core.Tests/AvaloniaScrollViewerTests.cs`

- [ ] **Step 1: Write test that verifies all views have ScrollViewer**

A test that scans all .axaml files and asserts:
- Every Window-based view either has a ScrollViewer or is in an explicit exclude list
- Every Window has SizeToContent attribute

- [ ] **Step 2: Run the test**

```bash
dotnet test FEBuilderGBA.Core.Tests/FEBuilderGBA.Core.Tests.csproj --filter AvaloniaScrollViewer
```

- [ ] **Step 3: Commit**

### Task 3: Launch Avalonia app to verify visually

- [ ] **Step 1: Build and run**

```bash
dotnet run --project FEBuilderGBA.Avalonia/FEBuilderGBA.Avalonia.csproj
```

- [ ] **Step 2: Open a ROM and check several editor forms for proper scrolling**

### Task 4: Update docs

- [ ] **Step 1: Update README noting the scrolling fix**
- [ ] **Step 2: Commit and push**
