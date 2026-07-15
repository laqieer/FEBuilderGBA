# Test Plan & Results — cli-anything-febuildergba

## Test Inventory

- `test_core.py`: ~25 unit tests (synthetic data, no external deps)
- `test_full_e2e.py`: ~15 E2E tests (real ROMs, real CLI backend)

## Unit Test Plan (`test_core.py`)

### Session module (`core/session.py`)
- Create new session (default path)
- Create session with custom path
- Open ROM (records state)
- Close session (clears state)
- Record operations (history tracking)
- Mark modified flag
- Session persistence (save + reload)
- History limit (capped at 100 entries)
- Info output format
- **Edge cases:** empty session, missing file, corrupt JSON

### Project module (`core/project.py`)
- ROM version detection from header bytes
- List tables returns all 40 entries
- Validate ROM (valid file, invalid file, missing file)
- **Edge cases:** truncated file, wrong extension

### Data module (`core/data.py`)
- TSV reading with proper parsing
- TSV summary with row count and column names
- Unknown table name raises ValueError
- **Edge cases:** empty TSV, malformed TSV

### Backend module (`utils/febuildergba_backend.py`)
- Backend check returns status dict
- **Edge cases:** missing dotnet, missing project

## E2E Test Plan (`test_full_e2e.py`)

### Prerequisites
- .NET 10.0 SDK installed
- FEBuilderGBA.CLI built
- ROM files in `roms/` directory

### Scenarios
1. **Backend check** — verify CLI is findable and runnable
2. **ROM info** — load real ROM, verify version detection
3. **Data export** — export units table, verify TSV structure
4. **Data roundtrip** — export → import → export, compare
5. **Text export** — export all text, verify TSV output
6. **Text roundtrip** — validate lossless text cycle
7. **Lint** — run lint on real ROM, check output format
8. **Full workflow** — session open → data export → lint → session close
9. **CLI subprocess** — invoke installed command via subprocess
10. **JSON output** — verify --json flag produces parseable JSON

### Realistic Workflow Scenarios

**Translation workflow:**
1. Open ROM session
2. Export all text to TSV
3. (Modify text externally)
4. Import modified text
5. Validate with roundtrip

**Data editing workflow:**
1. Export units/classes/items tables
2. Inspect exported TSV
3. Modify and reimport
4. Validate with roundtrip

**ROM validation workflow:**
1. Run lint check
2. Export all data tables
3. Validate all roundtrips

---

## Test Results

### Unit Tests (`test_core.py`) — 26 passed, 0 failed

```
test_core.py::TestSession::test_create_default_session PASSED
test_core.py::TestSession::test_open_rom PASSED
test_core.py::TestSession::test_close_session PASSED
test_core.py::TestSession::test_record_operation PASSED
test_core.py::TestSession::test_mark_modified PASSED
test_core.py::TestSession::test_session_persistence PASSED
test_core.py::TestSession::test_history_limit PASSED
test_core.py::TestSession::test_info_output PASSED
test_core.py::TestSession::test_empty_session_info PASSED
test_core.py::TestSession::test_corrupt_json_recovery PASSED
test_core.py::TestSession::test_force_version_stored PASSED
test_core.py::TestProject::test_list_tables PASSED
test_core.py::TestProject::test_validate_rom_missing PASSED
test_core.py::TestProject::test_validate_rom_small_file PASSED
test_core.py::TestProject::test_detect_version_fe8u PASSED
test_core.py::TestProject::test_detect_version_fe7u PASSED
test_core.py::TestProject::test_detect_version_fe6 PASSED
test_core.py::TestProject::test_detect_version_forced PASSED
test_core.py::TestProject::test_detect_version_unknown PASSED
test_core.py::TestData::test_read_tsv PASSED
test_core.py::TestData::test_tsv_summary PASSED
test_core.py::TestData::test_unknown_table_raises PASSED
test_core.py::TestData::test_empty_tsv PASSED
test_core.py::TestSessionState::test_to_dict PASSED
test_core.py::TestSessionState::test_from_dict PASSED
test_core.py::TestSessionState::test_from_dict_extra_keys PASSED

26 passed in 1.68s
```

### E2E Tests (`test_full_e2e.py`) — 19 passed, 0 failed

```
test_full_e2e.py::TestBackend::test_backend_check PASSED
test_full_e2e.py::TestBackend::test_backend_version PASSED
  Backend version: FEBuilderGBA Version:20260314.01
test_full_e2e.py::TestROMOperations::test_rom_info PASSED
  ROM: FE8U, Size: 16.0 MB
test_full_e2e.py::TestROMOperations::test_rom_validate PASSED
test_full_e2e.py::TestROMOperations::test_rom_version_detection PASSED
  Detected: FE8U
test_full_e2e.py::TestDataExport::test_export_units PASSED
  units.tsv: 60,995 bytes
test_full_e2e.py::TestDataExport::test_export_classes PASSED
  classes.tsv: 45,422 bytes
test_full_e2e.py::TestDataExport::test_export_inspect_units PASSED
  Units: 255 rows, 41 columns
test_full_e2e.py::TestTextExport::test_export_text PASSED
  texts.tsv: 1,091,877 bytes
test_full_e2e.py::TestLint::test_lint_rom PASSED
  Lint: 1 errors, 0 warnings
test_full_e2e.py::TestSessionE2E::test_full_session_workflow PASSED
test_full_e2e.py::TestCLISubprocess::test_help PASSED
test_full_e2e.py::TestCLISubprocess::test_version PASSED
test_full_e2e.py::TestCLISubprocess::test_rom_tables PASSED
test_full_e2e.py::TestCLISubprocess::test_json_rom_tables PASSED
test_full_e2e.py::TestCLISubprocess::test_rom_validate_nonexistent PASSED
test_full_e2e.py::TestCLISubprocess::test_rom_info_real PASSED
  ROM info via subprocess: FE8U
test_full_e2e.py::TestCLISubprocess::test_check_backend PASSED
test_full_e2e.py::TestCLISubprocess::test_session_status_no_session PASSED

19 passed in 13.50s
```

### Summary

| Suite | Tests | Passed | Failed | Time |
|-------|-------|--------|--------|------|
| Unit (test_core.py) | 26 | 26 | 0 | 1.68s |
| E2E (test_full_e2e.py) | 19 | 19 | 0 | 13.50s |
| **Total** | **45** | **45** | **0** | **15.18s** |

**Pass rate: 100%**

### Coverage Notes

- Unit tests cover session management, project info, data parsing, and serialization
- E2E tests verify real backend invocation with FE8U ROM (16 MB)
- Subprocess tests use `_resolve_cli()` and found installed command
- Data exports verified: units (255 rows, 41 columns), classes, text (1 MB)
- Lint produces real validation output
- JSON output mode verified via subprocess
