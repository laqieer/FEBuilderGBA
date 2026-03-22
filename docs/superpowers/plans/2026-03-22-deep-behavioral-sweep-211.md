# Deep Behavioral Sweep Tests — Complete Coverage (#211)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cover ALL 152 ViewModels with Write methods with behavioral round-trip tests, bringing behavioral coverage from 3.3% to 100%.

**Architecture:** Reflection-based sweep tests discover ViewModels at runtime and verify write idempotency (Load→Write→Reload produces identical data) and field mutation round-trips. This avoids writing 147 individual test files.

**Tech Stack:** xUnit, C# reflection, `IDataVerifiable`, `EditorFormRef`, `CoreState.ROM`

---

## Key Insight

152 ViewModels have Write methods. Most follow one of two patterns:
1. **EditorFormRef pattern** (~85 VMs): `EditorFormRef.WriteFields(rom, addr, values, _fields)`
2. **Direct write pattern** (~67 VMs): Manual `rom.write_u8/u16/u32` calls

Both patterns can be tested generically:
- **Idempotency test**: Load entry → Write (no modification) → Verify ROM bytes unchanged
- **Round-trip test**: Load → Modify first uint property → Write → Reload → Verify property matches

## File Structure

```
FEBuilderGBA.Avalonia.Tests/
├── WriteIdempotencySweepTests.cs   — NEW: Load→Write→verify no byte change (all 152 VMs)
├── WriteRoundTripSweepTests.cs     — NEW: Load→modify→Write→Reload→verify (all 152 VMs)
├── ListIterationSweepTests.cs      — NEW: Iterate all entries in every list-based VM
├── UndoSweepTests.cs               — NEW: UndoService Begin/Write/Commit/Undo for all writable VMs
```

## Shared Infrastructure

All test classes need a registry mapping each ViewModel type to its methods. Since method names vary (`LoadUnit`, `LoadEntry`, `LoadSong`, `LoadClass`, etc.), the registry provides:
- VM Type
- List method name (e.g., "LoadUnitList", "LoadList", "LoadSongList")
- Load method name (e.g., "LoadUnit", "LoadEntry", "LoadSong")
- Write method name (e.g., "WriteUnit", "Write", "WriteSong")

The registry is built by reflection: scan all types in the Avalonia assembly implementing `IDataVerifiable`, find methods matching `Load*List` / `Load*` / `Write*` patterns.

---

### Task 1: WritableViewModelRegistry — Auto-Discovery Infrastructure

**Files:**
- Create: `FEBuilderGBA.Avalonia.Tests/WritableViewModelRegistry.cs`

- [ ] **Step 1: Write the registry class**

```csharp
// Discovers all ViewModels with Write methods via reflection.
// Returns (Type vmType, string listMethod, string loadMethod, string writeMethod)
// for use as xUnit MemberData.
```

The registry:
1. Scans the Avalonia assembly for all concrete classes inheriting ViewModelBase
2. For each, finds methods matching: `public void Write*()`, `public List<AddrResult> Load*List()`, `public void Load*(uint addr)`
3. Returns tuples for xUnit `[MemberData]`

- [ ] **Step 2: Verify discovery count**

Run: `dotnet test --filter "WritableViewModelRegistry" --list-tests`
Expected: Registry discovers 152 writable ViewModels

- [ ] **Step 3: Commit**

---

### Task 2: WriteIdempotencySweepTests — Load+Write Is a No-Op

**Files:**
- Create: `FEBuilderGBA.Avalonia.Tests/WriteIdempotencySweepTests.cs`

**Pattern for each VM:**
1. Get list, load entry[1]
2. Snapshot ROM bytes at `CurrentAddr` for struct size
3. Call Write() (no property modifications)
4. Compare ROM bytes — they MUST be identical (write-back of loaded values is idempotent)
5. Restore snapshot

This catches: offset mismatches, type width bugs, sign extension, uninitialized fields.

- [ ] **Step 1: Write the Theory test**

```csharp
[Theory]
[MemberData(nameof(WritableViewModelRegistry.AllWritableViewModels),
            MemberType = typeof(WritableViewModelRegistry))]
public void WriteIsIdempotent(Type vmType, string listMethod, string loadMethod, string writeMethod)
```

- [ ] **Step 2: Build and verify test discovery**

Run: `dotnet build FEBuilderGBA.Avalonia.Tests/ -v quiet`
Expected: 0 errors

- [ ] **Step 3: Commit**

---

### Task 3: WriteRoundTripSweepTests — Field Mutation Verifies

**Files:**
- Create: `FEBuilderGBA.Avalonia.Tests/WriteRoundTripSweepTests.cs`

**Pattern for each VM:**
1. Get list, load entry[1]
2. Snapshot ROM bytes
3. Find the first `uint` property on the VM, save original value
4. Set it to `original == 42 ? 43 : 42`
5. Call Write()
6. Reload entry
7. Assert property equals the new value
8. Restore ROM bytes

- [ ] **Step 1: Write the Theory test**
- [ ] **Step 2: Build and verify**
- [ ] **Step 3: Commit**

---

### Task 4: ListIterationSweepTests — Every Entry Loads Without Crash

**Files:**
- Create: `FEBuilderGBA.Avalonia.Tests/ListIterationSweepTests.cs`

**Pattern for each VM:**
1. Get list via Load*List()
2. Iterate ALL entries, call Load*(addr) for each
3. Assert CurrentAddr != 0 for each
4. Report total count

- [ ] **Step 1: Write the Theory test**
- [ ] **Step 2: Build and verify**
- [ ] **Step 3: Commit**

---

### Task 5: UndoSweepTests — Undo Restores for All Writable VMs

**Files:**
- Create: `FEBuilderGBA.Avalonia.Tests/UndoSweepTests.cs`

**Pattern for each VM:**
1. Get list, load entry[1]
2. Snapshot ROM bytes
3. UndoService.Begin → modify first uint property → Write → Commit
4. Verify ROM bytes changed
5. CoreState.Undo.RunUndo()
6. Verify ROM bytes match original snapshot
7. Restore ROM bytes (safety net)

- [ ] **Step 1: Write the Theory test**
- [ ] **Step 2: Build and verify**
- [ ] **Step 3: Commit**

---

### Task 6: Verification and PR

- [ ] **Step 1: Run full test suite**

```bash
dotnet test FEBuilderGBA.Avalonia.Tests/ -v quiet
```
Expected: All tests pass, 0 failures

- [ ] **Step 2: Verify coverage count**

Count total `[Theory]` test cases discovered — should be 152×4 = ~608 new test cases

- [ ] **Step 3: Commit all, push, create PR**
- [ ] **Step 4: Post updated coverage audit on issue #211**
