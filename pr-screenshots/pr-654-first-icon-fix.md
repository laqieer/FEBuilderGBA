# PR #654 — First-item icon fix — Test Run Proof

**Note: this PR was prepared in an automated environment where the desktop
was locked, so a live MCP / WinCapture screenshot of the Avalonia GUI could
not be captured. The proof below is the actual headless test-run output
that exercises the entire fix path against a real FE8U ROM.**

## Data-layer proof (per-slot prefix + icon loader)

The headless `ItemShopFirstIconScreenshotTest` test loads the real FE8U ROM,
finds the first non-empty shop, and verifies the fix in two ways:

```
Shop @ 0x00A188E4 'Preparation Shop', slot[0] = '01 Iron Sword' itemId=0x01
slot[0] icon = Bitmap
```

Before this PR:
- `slot[0]` row text would be `"0 Iron Sword"` (slot index 0 — the bug)
- `ListIconLoaders.ItemIconLoader(slots, 0)` would return `null`
  (because `U.atoh("0 Iron Sword") == 0` and the loader hard-bailed on `id == 0`)
- The icon would NOT render

After this PR:
- `slot[0]` row text is `"01 Iron Sword"` (real item ID — matches WinForms)
- `ListIconLoaders.ItemIconLoader(slots, 0)` returns a non-null `Bitmap`
- The icon renders correctly

## Unit-test proof

```
Passed!  - Failed: 0, Passed: 22, Skipped: 0, Total: 22, Duration: 362 ms
  - FEBuilderGBA.Core.Tests.dll (ItemShopCoreTests)
Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 308 ms
  - FEBuilderGBA.Avalonia.Tests.dll (ListIconLoadersFirstRowTests)
Passed!  - Failed: 0, Passed: 1716, Skipped: 0, Total: 1716, Duration: 14 s
  - FEBuilderGBA.Tests.dll (full WinForms test suite, incl. updated
    AddressListControl_Axaml_HasDataTemplate_WithImageAndTextBlock)
Passed!  - Failed: 0, Passed: 1852, Skipped: 0, Total: 1852, Duration: 21 s
  - FEBuilderGBA.Core.Tests.dll (full Core test suite, no regressions)
```

Full Avalonia suite: 33 pre-existing failures on master vs. 33 with this PR
(0 regressions; we actually fixed 1 axaml-source-text assertion that was
checking for the removed `IsVisible` binding).
