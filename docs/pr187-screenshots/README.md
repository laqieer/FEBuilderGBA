# PR #187 GUI Test Screenshots — Language Options Validation

Screenshots captured from automated GUI testing of the FEBuilderGBA Avalonia application
using Windows UI Automation (UIA). The test validates that switching the language setting
in the Options dialog (Japanese, English, Auto Detect) works without errors and the main
window remains functional after each change.

ROM loaded: FE8U (Fire Emblem: The Sacred Stones, US).

## Test Sequence

### Step 1 — Initial State

| File | Description |
|------|-------------|
| `01_main_window.png` | Main window after launching with FE8U ROM. Shows the full editor categories (Characters, Items, Maps, Text, Graphics, Audio, Arena) with all navigation buttons visible. |

### Step 2 — Switch Language to Japanese

| File | Description |
|------|-------------|
| `02_a_options.png` | Options dialog opened, General tab active. Language combo box shows "en — English" (current setting). |
| `02_b_selected.png` | Language combo box changed to "ja — Japanese". Selection made but not yet confirmed. |
| `02_c_result.png` | Main window after clicking OK. Application continues running normally with the language change applied. Shows the lower half of the editor (Audio Advanced, Unit/Class Specialized, Text/Translation, Patches, Skill Systems, World Map, Structural Data, Tools). |

### Step 3 — Switch Language to English

| File | Description |
|------|-------------|
| `03_a_options.png` | Options dialog reopened. Language combo box shows "ja — Japanese" (set in previous step). |
| `03_b_selected.png` | Language combo box changed back to "en — English". |
| `03_c_result.png` | Main window after clicking OK. Application remains stable. Shows the same lower-half editor categories, confirming no crash or rendering issues from the language switch. |

### Step 4 — Switch Language to Auto Detect

| File | Description |
|------|-------------|
| `04_a_options.png` | Options dialog reopened. Language combo box shows "en — English" (set in previous step). |
| `04_b_selected.png` | Language combo box changed to "auto — Auto Detect". |
| `04_c_result.png` | Main window after clicking OK. Application continues running normally with auto-detect language. Shows the lower-half editor categories. |

### Step 5 — Final State

| File | Description |
|------|-------------|
| `05_final.png` | Final state of the main window after all three language switches. The application is fully functional with all editor sections visible and no errors. Shows the lower-half categories (Audio Advanced through Tools). |

## Test Method

- **Framework**: PowerShell script using Windows UI Automation (UIA) COM interfaces
- **Application**: FEBuilderGBA Avalonia (`FEBuilderGBA.Avalonia.exe`)
- **Platform**: Windows 11, .NET 9.0
- **ROM**: FE8U (`roms/FE8U.gba`)
- **Automation**: Programmatic click on Tools > Options menu, combo box selection, button click, screenshot capture at each stage
