<#
.SYNOPSIS
    Validates AutomationProperties.AutomationId coverage and naming compliance
    across all Avalonia .axaml files.

.DESCRIPTION
    Checks that:
    1. All interactive controls have AutomationProperties.AutomationId
    2. IDs follow the naming convention: {EditorName}_{FieldName}_{ControlType}
    3. No duplicate IDs exist across files
    4. Exempt files (templates, reusable controls, App.axaml) are correctly skipped
    5. Controls inside DataTemplate/ControlTemplate blocks do not have IDs

.PARAMETER AvaloniaRoot
    Path to the FEBuilderGBA.Avalonia directory.
#>
param(
    [string]$AvaloniaRoot = (Join-Path (Join-Path $PSScriptRoot "..") "FEBuilderGBA.Avalonia")
)

$ErrorActionPreference = "Stop"
$AvaloniaRoot = (Resolve-Path $AvaloniaRoot).Path

# Files that should be skipped (no AutomationIds inside).
# These are reusable UserControls — their host views set ONE AutomationId on
# the control instance and the control derives suffixed ids on its inner
# elements at runtime (so the inner elements never carry literal AutomationId
# attributes in the .axaml source).
$exemptInternalFiles = @(
    "Controls\BitFlagPanel.axaml",
    "Controls\AddressListControl.axaml",
    "Controls\GbaImageControl.axaml",
    "Controls\IconPreviewControl.axaml",
    "Controls\IdFieldControl.axaml",
    "Controls\EditorTopBar.axaml",
    "App.axaml"
)

# Valid control type suffixes
$validSuffixes = @('_Input', '_Combo', '_Button', '_List', '_Check', '_Expander',
                   '_TabControl', '_Tab', '_Image', '_Label', '_Control', '_Link',
                   '_TopBar')

# Interactive control types that should have AutomationIds
$interactiveControlTypes = @(
    'TextBox', 'NumericUpDown', 'ComboBox', 'Button',
    'ListBox', 'ListView', 'CheckBox', 'ToggleButton',
    'Expander', 'TabControl', 'TabItem', 'RadioButton',
    'Slider', 'ToggleSwitch',
    'MenuItem', 'ItemsControl', 'Image', 'AutoCompleteBox'
)

$customControlTypes = @(
    'controls:BitFlagPanel', 'controls:AddressListControl', 'controls:GbaImageControl',
    'controls:IdFieldControl', 'controls:EditorTopBar'
)

$totalErrors = 0
$totalWarnings = 0
$totalIds = 0
$allIds = @{}  # Track all IDs globally for duplicate detection
$fileStats = @{}

function Get-EditorName {
    param([string]$FilePath)
    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($FilePath)
    $name = $fileName
    if ($fileName -match '^(.+)(View|Window)$') {
        $name = $Matches[1]
    }
    return $name
}

function Test-IsInsideTemplate {
    param(
        [string[]]$Lines,
        [int]$LineIndex
    )
    $templateDepth = 0
    for ($i = 0; $i -lt $LineIndex; $i++) {
        $line = $Lines[$i]
        if ($line -match '<\s*(DataTemplate|ControlTemplate|TreeDataTemplate)[\s>]' -and $line -notmatch '/>') {
            $templateDepth++
        }
        if ($line -match '</\s*(DataTemplate|ControlTemplate|TreeDataTemplate)\s*>') {
            $templateDepth--
            if ($templateDepth -lt 0) { $templateDepth = 0 }
        }
    }
    return $templateDepth -gt 0
}

Write-Host "=== Validating AutomationProperties.AutomationId Coverage ===" -ForegroundColor Cyan
Write-Host "Root: $AvaloniaRoot" -ForegroundColor Gray
Write-Host ""

$allAxamlFiles = Get-ChildItem -Path $AvaloniaRoot -Filter "*.axaml" -Recurse | Sort-Object FullName

foreach ($file in $allAxamlFiles) {
    $relativePath = $file.FullName.Substring($AvaloniaRoot.Length + 1)
    $editorName = Get-EditorName -FilePath $file.FullName
    $isExempt = $exemptInternalFiles -contains $relativePath

    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    $lines = $content -split "`n"

    $fileIdCount = 0
    $fileMissingCount = 0
    $fileErrors = @()

    # Check 1: Exempt files should NOT have AutomationIds
    if ($isExempt) {
        if ($content -match 'AutomationProperties\.AutomationId') {
            $fileErrors += "EXEMPT file has AutomationProperties.AutomationId (should not)"
            $totalErrors++
        }
        continue
    }

    # Check 2: Scan all lines for interactive controls
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        # Skip template blocks
        if (Test-IsInsideTemplate -Lines $lines -LineIndex $i) {
            # Controls inside templates should NOT have AutomationIds
            if ($line -match 'AutomationProperties\.AutomationId') {
                $fileErrors += "Line $($i+1): AutomationId found inside template block (should not be there)"
                $totalErrors++
            }
            continue
        }

        # Check if this line starts an interactive control
        $isInteractive = $false
        foreach ($ct in ($interactiveControlTypes + $customControlTypes)) {
            $escapedCT = [regex]::Escape($ct)
            if ($line -match "^\s*<$escapedCT[\s/>]") {
                $isInteractive = $true
                break
            }
        }

        # Named TextBlock elements (with Name= attribute) are also interactive/dynamic
        if (-not $isInteractive -and $line -match '^\s*<TextBlock[\s]' ) {
            # Build full element to check for Name attribute
            $tbElement = $line
            $tj = $i + 1
            while ($tj -lt $lines.Count -and $tbElement -notmatch '(?:/>|(?<!=)>)\s*$') {
                $tbElement += "`n" + $lines[$tj]
                $tj++
            }
            if ($tbElement -match '\bName\s*=\s*"[^"]+"') {
                $isInteractive = $true
            }
        }

        if ($isInteractive) {
            # Build full element for multi-line
            $fullElement = $line
            $j = $i + 1
            while ($j -lt $lines.Count -and $fullElement -notmatch '(?:/>|(?<!=)>)\s*$') {
                $fullElement += "`n" + $lines[$j]
                $j++
            }

            if ($fullElement -match 'AutomationProperties\.AutomationId\s*=\s*"([^"]+)"') {
                $automationId = $Matches[1]
                $fileIdCount++
                $totalIds++

                # Validate naming convention
                $validFormat = $false
                foreach ($suffix in $validSuffixes) {
                    if ($automationId.EndsWith($suffix)) {
                        $validFormat = $true
                        break
                    }
                }
                if (-not $validFormat) {
                    $fileErrors += "Line $($i+1): ID '$automationId' does not end with valid suffix ($($validSuffixes -join ', '))"
                    $totalErrors++
                }

                # Validate editor name prefix
                if (-not $automationId.StartsWith("${editorName}_")) {
                    $fileErrors += "Line $($i+1): ID '$automationId' does not start with expected prefix '${editorName}_'"
                    $totalErrors++
                }

                # Track for duplicates
                if ($allIds.ContainsKey($automationId)) {
                    $fileErrors += "Line $($i+1): DUPLICATE ID '$automationId' (also in $($allIds[$automationId]))"
                    $totalErrors++
                } else {
                    $allIds[$automationId] = $relativePath
                }
            } else {
                $fileMissingCount++
                $totalWarnings++
            }
        }
    }

    $fileStats[$relativePath] = @{ Ids = $fileIdCount; Missing = $fileMissingCount; Errors = $fileErrors.Count }

    if ($fileErrors.Count -gt 0) {
        Write-Host "  ERRORS in ${relativePath}:" -ForegroundColor Red
        foreach ($err in $fileErrors) {
            Write-Host "    - $err" -ForegroundColor Red
        }
    }
    if ($fileMissingCount -gt 0) {
        Write-Host "  WARN: $relativePath - $fileMissingCount interactive controls without AutomationId" -ForegroundColor Yellow
    }
}

# Summary
Write-Host ""
Write-Host "=== Validation Summary ===" -ForegroundColor Cyan
Write-Host "  Total .axaml files:          $($allAxamlFiles.Count)"
Write-Host "  Exempt files (skipped):      $($exemptInternalFiles.Count)"
Write-Host "  Total AutomationIds found:   $totalIds" -ForegroundColor $(if ($totalIds -gt 0) { "Green" } else { "Red" })
Write-Host "  Unique IDs:                  $($allIds.Count)"
Write-Host "  Duplicate IDs:               $($totalIds - $allIds.Count)" -ForegroundColor $(if ($totalIds - $allIds.Count -gt 0) { "Red" } else { "Green" })
Write-Host "  Naming errors:               $totalErrors" -ForegroundColor $(if ($totalErrors -gt 0) { "Red" } else { "Green" })
Write-Host "  Missing ID warnings:         $totalWarnings" -ForegroundColor $(if ($totalWarnings -gt 0) { "Yellow" } else { "Green" })

# Coverage stats
$filesWithIds = ($fileStats.Values | Where-Object { $_.Ids -gt 0 }).Count
$filesWithoutIds = ($fileStats.Values | Where-Object { $_.Ids -eq 0 }).Count
Write-Host ""
Write-Host "  Files with AutomationIds:    $filesWithIds"
Write-Host "  Files without AutomationIds: $filesWithoutIds"

# Top 10 files by ID count
Write-Host ""
Write-Host "  Top 10 files by ID count:" -ForegroundColor Gray
$fileStats.GetEnumerator() | Sort-Object { $_.Value.Ids } -Descending | Select-Object -First 10 | ForEach-Object {
    Write-Host "    $($_.Value.Ids.ToString().PadLeft(4)) IDs: $($_.Key)"
}

# Exit code
if ($totalErrors -gt 0 -or $totalWarnings -gt 0) {
    Write-Host ""
    Write-Host "VALIDATION FAILED: $totalErrors error(s), $totalWarnings warning(s) found" -ForegroundColor Red
    exit 1
} else {
    Write-Host ""
    Write-Host "VALIDATION PASSED" -ForegroundColor Green
    exit 0
}
