# Weapon Lock (Vennou) Editor — Usage Guide

This page explains how to use the **Weapon Lock (Vennou) Editor** in the
Avalonia GUI, what each field controls, and the current limitations of the
Avalonia implementation versus the WinForms version. The editor is named
after the GBAGE-era "Vennou" weapon-lock format adopted by the
[SkillSystems](https://github.com/FireEmblemUniverse/SkillSystem)
`WeaponLockArray` patch.

- **Avalonia view:** [`FEBuilderGBA.Avalonia/Views/VennouWeaponLockView.axaml`](../FEBuilderGBA.Avalonia/Views/VennouWeaponLockView.axaml)
- **Avalonia view-model:** [`FEBuilderGBA.Avalonia/ViewModels/VennouWeaponLockViewModel.cs`](../FEBuilderGBA.Avalonia/ViewModels/VennouWeaponLockViewModel.cs)
- **WinForms reference:** [`FEBuilderGBA/VennouWeaponLockForm.cs`](../FEBuilderGBA/VennouWeaponLockForm.cs)
- **Patch lookup helper:** [`FEBuilderGBA/PatchUtil.cs` `SearchVennouWeaponLockArrayAddr`](../FEBuilderGBA/PatchUtil.cs)

> **Tracking issue:** [#373](https://github.com/laqieer/FEBuilderGBA/issues/373)
> The Avalonia editor today has the limitations described in the
> [Opening the editor](#opening-the-editor-avalonia-current-state) section
> below. They are tracked separately under the Avalonia patch-aware-UI work
> in [`docs/avalonia-gap-analysis.md`](avalonia-gap-analysis.md).

---

## 1. What it is

The Weapon Lock (Vennou) Editor edits a **variable-length, null-terminated
byte string** that describes one *weapon lock*. A lock determines which
units or classes are allowed to wield a particular weapon. Each lock string
lives at the end of a 32-bit pointer stored in the `WeaponLockArray` table
(installed by the SkillSystems `WeaponLockArray_SkillSystems` patch on FE8U).

Each entry in `WeaponLockArray` corresponds to one lock index. The item-level
byte at `item_base + 0x11` (B11 — *Trait 4* in Avalonia) selects which lock
applies to the item.

---

## 2. Prerequisites — when the editor has data

The editor only resolves a list of lock entries when **all** of these are
true:

- The ROM is **FE8U** (`Program.ROM.RomInfo.version == 8` and
  non-multibyte). This is enforced by both
  `PatchUtil.SearchVennouWeaponLockArrayAddrLow()` and
  `PatchDetectionService.DetectVennouWeaponLock()`.
- The SkillSystems `WeaponLockArray` patch is installed. The
  signature is the value `0xFF3D3C00` at offset `0x16DD8`, plus the file
  `config/patch2/FE8U/WeaponLockArray_SkillSystems/AdvWeaponLocks.dmp` is
  resolvable at runtime.
- The Avalonia main window detects this state via
  `PatchDetectionService.Instance.VennouWeaponLock`. If the property is
  `false`, the editor still opens but the `WeaponLockArray` does not exist
  in ROM, so there is nothing to inspect or edit.

---

## 3. Opening the editor (Avalonia current state)

> **Important caveat — read this before clicking the launcher.**
> Today there is **no end-user Avalonia GUI path** that opens the Weapon
> Lock (Vennou) Editor with data. The launcher hands the view no address,
> the view defaults to `_baseAddr = 0`, and `BuildList(0)` returns an empty
> list — even on a fully patched ROM. The same launcher works in WinForms
> because the WinForms form resolves the array address itself.

### Path

Avalonia main window → search/filter for `VennouWeaponLockView` → click
the `VennouWeaponLockView` button.

### Why the list is empty

`OpenVennouWeaponLock_Click` calls
`WindowManager.Instance.Open<VennouWeaponLockView>()`
without passing an address. The view's `NavigateTo` is therefore never
called and `_baseAddr` remains `0`. References:

- `FEBuilderGBA.Avalonia/Views/MainWindow.axaml.cs` line 3043
- `FEBuilderGBA.Avalonia/Views/VennouWeaponLockView.axaml.cs` lines 24–37
- `FEBuilderGBA.Avalonia/ViewModels/VennouWeaponLockViewModel.cs` lines 43–47

No other Avalonia editor calls
`WindowManager.Instance.Navigate<VennouWeaponLockView>(addr)`, and the
Avalonia **Hex Editor "Jump to address"** does *not* populate this view —
the Hex Editor jump only navigates the hex view itself.

### Recommended workflows today

Use one of these until a follow-up GUI feature lands (see [section 7](#7-pitfalls-and-current-limitations)):

1. **Use the WinForms editor.** The WinForms `VennouWeaponLockForm`
   resolves the `WeaponLockArray` table via
   `PatchUtil.SearchVennouWeaponLockArrayAddr()` and exposes both the list
   and per-byte editing. This is the only fully working GUI path today.
2. **Programmatic Avalonia navigation** (developer / extension builds
   only). Call:
   ```csharp
   WindowManager.Instance.Navigate<VennouWeaponLockView>(addr);
   ```
   where `addr` is the ROM offset of an existing lock string. Compute it
   from the `WeaponLockArray` table:
   ```
   array_addr   = PatchUtil.SearchVennouWeaponLockArrayAddr();
   pointer_addr = array_addr + (index * 4);   // index from item.B11
   lock_addr    = Program.ROM.p32(pointer_addr);
   ```
   Index `0` is reserved (renders as `-NULL-` in WinForms).
3. **Raw hex inspection.** Open the ROM in any hex editor at the computed
   address. The bytes are 1 BPP — straightforward to read by hand once you
   know the format described in [section 4](#4-data-format).

A follow-up GitHub issue should track adding an end-user navigation path
(e.g. prompt for a `WeaponLockArray` index on launcher click, or a
Vennou-index cross-link in the Avalonia Item Editor). That work is **out
of scope** for this docs page.

---

## 4. Data format

A weapon-lock string is a contiguous sequence of bytes:

| Offset | Size | Field            | Notes                                                              |
|-------:|-----:|------------------|--------------------------------------------------------------------|
|    `0` |  `1` | **Lock type**    | One of `0`–`3`. Determines how the following bytes are interpreted. |
|    `1` |  `1` | Payload byte 1   | Unit ID (types 0/1) or Class ID (types 2/3).                       |
|    `2` |  `1` | Payload byte 2   | …                                                                  |
|    `…` |  `1` | …                | Zero or more payload bytes.                                        |
|    `N` |  `1` | **Terminator**   | `0x00`. Ends the list. Reading stops at the first zero after byte 0.|

Both `VennouWeaponLockForm.CalcLength` (WinForms) and
`VennouWeaponLockViewModel.BuildList` (Avalonia) follow the same rule:
include byte 0 unconditionally, then walk forward and stop at the first
`0x00`.

---

## 5. Field-by-field guide (Avalonia)

The Avalonia view layout (`VennouWeaponLockView.axaml`):

| Control (axaml `Name=`) | Label in UI       | Purpose                                                                                                                                                                                                                                                                                                                                                |
|-------------------------|-------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `EntryList`             | (left list)       | Shows every byte in the current lock string. Row 0 is the type header; rows 1+ are the payload bytes. The terminator is **not** displayed. Each row stores the address of *that byte*, not the start of the string.                                                                                                                                |
| `AddrLabel`             | `Address:`        | Read-only ROM offset of the **currently selected** byte (`_vm.CurrentAddr`).                                                                                                                                                                                                                                                                            |
| `FieldLabelText`        | (dynamic)         | Retitles based on the selected row: `Type:` for row 0, `Unit:` for rows 1+ when the type ≤ 1, `Class:` for rows 1+ when the type ≥ 2. Wiring lives in `VennouWeaponLockViewModel.LoadEntry` lines 103–125.                                                                                                                                              |
| `LockTypeBox`           | (next to label)   | The selected byte's value (8-bit; 0–255). For row 0 this is the lock type (0–3). For rows 1+ this is the Unit or Class ID.                                                                                                                                                                                                                              |
| `LinkedNameLabel`       | `Linked Name:`    | Human-readable resolution: type name for row 0; for rows 1+, the Unit name (via `NameResolver.GetUnitName`) or Class name (via `NameResolver.GetClassName`).                                                                                                                                                                                            |
| `ExplanationBox`        | `Description:`    | Read-only blurb. Row 0 explains the four lock types and the soft/hard distinction. Rows 1+ show `Unit/Class ID 0xNN in a <type> list.`                                                                                                                                                                                                                  |
| `Write_Click` button    | `Write`           | Overwrites the byte at `CurrentAddr` with the value in `LockTypeBox`. **Single-byte write only** — it cannot insert, delete, or grow the list. The terminator is preserved because it is not exposed as a row.                                                                                                                                          |

---

## 6. Lock type semantics

This is the section the issue specifically asked for. The four type values
are defined in `VennouWeaponLockForm.TypeIDToString` (WinForms, lines
59–78) and mirrored in `VennouWeaponLockViewModel.TypeIDToString` (Avalonia,
lines 78–88).

| Type | Display name           | Payload bytes are…   | Effect in game                                                                                  |
|:----:|------------------------|----------------------|-------------------------------------------------------------------------------------------------|
|  `0` | `Soft character lock`  | Unit IDs             | Only listed **units** may equip the weapon. **AI checks may be skipped** (e.g. enemy steal).    |
|  `1` | `Hard character lock`  | Unit IDs             | Only listed **units** may equip. **AI also respects** the restriction.                          |
|  `2` | `Soft class lock`      | Class IDs            | Only listed **classes** may equip. **AI checks may be skipped.**                                |
|  `3` | `Hard class lock`      | Class IDs            | Only listed **classes** may equip. **AI also respects** the restriction.                        |

"Soft" vs. "Hard" is the WinForms `J_0.Text = "ユニット" / "クラス"` toggle at
`VennouWeaponLockForm.cs:222–235` plus the SkillSystems patch's
AI-respecting variant.

---

## 7. Linking a weapon to a lock

This is the workflow that changes the most between WinForms and Avalonia.

### WinForms (full patch-aware UI)

When `PatchUtil.SearchVennouWeaponLockArray()` returns `true`,
`ItemForm.VennouWeaponLockArray()` (`ItemForm.cs:120–145`) rewrites B11
(the item byte at offset `0x11`) so it acts as a **`WeaponLockArray`
index**, with a `VENNOUWEAPONLOCK_INDEX` linked display
(`InputFormRef.cs:2880–2896`). Clicking the linked label opens the Vennou
editor jumped to the corresponding lock string.

### Avalonia (current state)

The Avalonia Item Editor still renders B11 as `Trait 4 (B11)` bit flags
(`ItemEditorView.axaml:209–210`). There is **no** `WeaponLockArray` link,
no name resolver, and no cross-editor jump to the Vennou view.

To wire a weapon to a lock today in Avalonia you must:

1. Note the desired `WeaponLockArray` index from the WinForms Item Editor
   or from your patch's documentation (`PatchUtil.SearchVennouWeaponLockArrayAddr()`
   returns the table base).
2. Write that index into B11 as a raw byte. The `Trait 4 (B11)`
   `BitFlagPanel` shows it as eight independent bits — set the bit pattern
   that encodes your index, or edit B11 directly in the Hex Editor at
   `item_addr + 0x11`.

This Avalonia limitation is tracked under the existing patch-aware-UI gap
in [`docs/avalonia-gap-analysis.md`](avalonia-gap-analysis.md) (search for
`Vennou` — see lines 155 and 436). This page does **not** ship a code fix.

---

## 8. Example walkthrough — Sieglinde (Eirika-only)

Goal: a soft character lock so only Unit `0x02` (Eirika) can wield the
weapon.

Bytes at the lock address: `00 02 00`

| Offset | Byte  | Meaning                          |
|-------:|-------|----------------------------------|
|    `0` | `0x00`| Lock type = Soft character lock  |
|    `1` | `0x02`| Unit ID `0x02` (Eirika)          |
|    `2` | `0x00`| Terminator                       |

In the Avalonia editor, after navigating to the lock address (see
[section 3](#3-opening-the-editor-avalonia-current-state) for how):

- Row 0 displays `Soft character lock` (Lock Type / ID = 0).
- Row 1 displays `0x02 Eirika` (Lock Type / ID = 2). The Field label
  switches to `Unit:`.
- The terminator at offset 2 is **not** shown as a row.

To make this a *class*-locked weapon for, say, Lord (class `0x01`) only,
change byte 0 from `0x00` to `0x02` (Soft class lock) and byte 1 from
`0x02` to `0x01` — bytes `02 01 00`.

---

## 9. Pitfalls and current limitations

- **No GUI launcher path with data.** Opening the editor from the
  Avalonia main window always shows an empty list because the launcher
  does not pass an address. See [section 3](#3-opening-the-editor-avalonia-current-state) for the three workarounds.
- **Single-byte writes only.** `VennouWeaponLockViewModel.WriteEntry`
  (lines 138–146) overwrites the byte at `CurrentAddr`. There is **no**
  add / remove / insert UI — growing or shrinking a lock string requires
  the Hex Editor (with free space) or re-running the SkillSystems
  `WeaponLockArray` installer.
- **8-bit IDs only.** Unit and Class IDs larger than `0xFF` are not
  representable in this format.
- **Patch-gated.** Without the SkillSystems `WeaponLockArray` patch on
  FE8U, the `WeaponLockArray` does not exist in ROM and there is nothing
  to edit.
- **No cross-editor jump in Avalonia.** The Item Editor's B11 is bit
  flags, not a Vennou index link. See [section 7](#7-linking-a-weapon-to-a-lock).

---

## 10. Cross-references

- [`FEBuilderGBA.Avalonia/Views/VennouWeaponLockView.axaml`](../FEBuilderGBA.Avalonia/Views/VennouWeaponLockView.axaml)
- [`FEBuilderGBA.Avalonia/ViewModels/VennouWeaponLockViewModel.cs`](../FEBuilderGBA.Avalonia/ViewModels/VennouWeaponLockViewModel.cs)
- [`FEBuilderGBA/VennouWeaponLockForm.cs`](../FEBuilderGBA/VennouWeaponLockForm.cs)
- [`FEBuilderGBA/PatchUtil.cs`](../FEBuilderGBA/PatchUtil.cs) — `SearchVennouWeaponLockArrayAddr`, lines 1865–1923
- [`docs/avalonia-gap-analysis.md`](avalonia-gap-analysis.md) — patch-aware-UI gap tracker
- [`docs/avalonia-gui-forms.md`](avalonia-gui-forms.md) — editor coverage table
- Plan and review history: [issue #373](https://github.com/laqieer/FEBuilderGBA/issues/373)
