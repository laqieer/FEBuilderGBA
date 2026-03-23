<# .SYNOPSIS
    Scan all Avalonia .axaml and .axaml.cs files for hardcoded translatable strings.
    Reports which strings already have translation entries and which are new.

    .DESCRIPTION
    Catalogs:
    - .axaml: TextBlock.Text, Expander.Header, Button.Content, CheckBox.Content,
      TabItem.Header, Window.Title attributes, and Watermark attributes
    - .axaml.cs: Hardcoded English strings (quoted strings not wrapped in R._())
    Cross-references with config/translate/en.txt to identify gaps.

    .EXAMPLE
    .\scripts\scan-translations.ps1
#>

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$avaloniaDir = Join-Path $repoRoot "FEBuilderGBA.Avalonia"
$enFile = Join-Path $repoRoot "config\translate\en.txt"

# Parse en.txt to get existing translations
$enContent = Get-Content $enFile -Raw -Encoding UTF8
$lines = $enContent -split "`n"
$existingKeys = @{}
$existingValues = @{}
$currentKey = $null
foreach ($line in $lines) {
    $trimmed = $line.TrimEnd()
    if ($trimmed.StartsWith(':')) {
        $currentKey = $trimmed.Substring(1) -replace '\\r\\n', "`r`n"
        $existingKeys[$currentKey] = $true
    } elseif ($currentKey -and $trimmed) {
        $val = $trimmed -replace '\\r\\n', "`r`n"
        $existingValues[$val.TrimEnd()] = $currentKey
        $currentKey = $null
    } else {
        $currentKey = $null
    }
}

# ================================================================
# Category 1: Scan .axaml files for Text/Header/Content/Title attrs
# ================================================================
$axamlFiles = Get-ChildItem -Path $avaloniaDir -Filter "*.axaml" -Recurse
$allStrings = @{}
$fileStats = @{}

foreach ($file in $axamlFiles) {
    $content = Get-Content $file.FullName -Raw -Encoding UTF8
    $matches = [regex]::Matches($content, '(?:Text|Header|Content|Title)="([^"]+)"')
    $count = 0
    foreach ($m in $matches) {
        $val = $m.Groups[1].Value
        # Skip binding expressions, WidthAndHeight marker, pure numbers
        if ($val.StartsWith('{')) { continue }
        if ($val -eq 'WidthAndHeight') { continue }
        if ($val -match '^\d+$') { continue }
        if (-not ($val -match '[A-Za-z]')) { continue }

        if (-not $allStrings.ContainsKey($val)) {
            $allStrings[$val] = @()
        }
        $allStrings[$val] += $file.Name
        $count++
    }
    if ($count -gt 0) {
        $fileStats[$file.Name] = $count
    }
}

# ================================================================
# Category 2: Scan .axaml files for Watermark attributes
# ================================================================
$watermarkStrings = @{}
$watermarkFileStats = @{}

foreach ($file in $axamlFiles) {
    $content = Get-Content $file.FullName -Raw -Encoding UTF8
    $matches = [regex]::Matches($content, 'Watermark="([^"]+)"')
    $count = 0
    foreach ($m in $matches) {
        $val = $m.Groups[1].Value
        # Decode XML entities
        $val = $val -replace '&#x0a;', "`n"
        # Skip pure hex placeholders like "0x08000000", "0x100", "0x200"
        if ($val -match '^0x[0-9A-Fa-f]+$') { continue }
        # Skip URL-like strings
        if ($val.StartsWith('http')) { continue }
        # Must contain at least one letter
        if (-not ($val -match '[A-Za-z]')) { continue }

        if (-not $watermarkStrings.ContainsKey($val)) {
            $watermarkStrings[$val] = @()
        }
        $watermarkStrings[$val] += $file.Name
        $count++
    }
    if ($count -gt 0) {
        $watermarkFileStats[$file.Name] = $count
    }
}

# ================================================================
# Category 3: Scan .axaml.cs files for hardcoded English strings
# ================================================================
$csFiles = Get-ChildItem -Path $avaloniaDir -Filter "*.axaml.cs" -Recurse
$csStrings = @{}
$csFileStats = @{}

foreach ($file in $csFiles) {
    $content = Get-Content $file.FullName -Raw -Encoding UTF8
    # Match quoted strings that are NOT inside R._()
    # First find all quoted strings
    $allQuoted = [regex]::Matches($content, '"([^"\\]*(?:\\.[^"\\]*)*)"')
    $count = 0
    foreach ($m in $allQuoted) {
        $val = $m.Groups[1].Value
        # Skip empty, single char, pure numbers, file paths, namespaces
        if ($val.Length -lt 3) { continue }
        if ($val -match '^\d+$') { continue }
        if ($val -match '^[\\\/\.]') { continue }
        if ($val -match '^\*\.') { continue }
        if ($val -match '^0x') { continue }
        if ($val.Contains('.') -and -not $val.Contains(' ')) { continue }
        # Must contain at least one letter and a space (likely human-readable)
        if (-not ($val -match '[A-Za-z].*\s|\s.*[A-Za-z]')) { continue }
        # Skip strings that are already wrapped in R._()
        $idx = $m.Index
        if ($idx -ge 3) {
            $prefix = $content.Substring([Math]::Max(0, $idx - 3), [Math]::Min(3, $idx))
            if ($prefix.EndsWith('_("') -or $prefix.EndsWith('._(')) { continue }
        }

        if (-not $csStrings.ContainsKey($val)) {
            $csStrings[$val] = @()
        }
        $csStrings[$val] += $file.Name
        $count++
    }
    if ($count -gt 0) {
        $csFileStats[$file.Name] = $count
    }
}

# ================================================================
# Report — Category 1: AXAML Text/Header/Content/Title
# ================================================================
$total = $allStrings.Count
$hasTranslation = 0
$needsTranslation = 0
$newStrings = @()

foreach ($key in $allStrings.Keys) {
    if ($existingKeys.ContainsKey($key) -or $existingValues.ContainsKey($key)) {
        $hasTranslation++
    } else {
        $needsTranslation++
        $newStrings += $key
    }
}

Write-Host "=== Avalonia Translation Scan Report ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "--- Category 1: AXAML Text/Header/Content/Title ---" -ForegroundColor Cyan
Write-Host "Total .axaml files scanned: $($axamlFiles.Count)"
Write-Host "Files with translatable strings: $($fileStats.Count)"
Write-Host "Total unique strings: $total"
Write-Host "Already have translations: $hasTranslation" -ForegroundColor Green
Write-Host "Need new translations: $needsTranslation" -ForegroundColor Yellow
Write-Host ""

if ($newStrings.Count -gt 0) {
    Write-Host "=== Strings needing translation (first 50) ===" -ForegroundColor Yellow
    $newStrings | Sort-Object | Select-Object -First 50 | ForEach-Object {
        Write-Host "  $_"
    }
}

# ================================================================
# Report — Category 2: AXAML Watermarks
# ================================================================
$wmTotal = $watermarkStrings.Count
$wmHas = 0
$wmNeeds = 0
$wmNew = @()

foreach ($key in $watermarkStrings.Keys) {
    if ($existingKeys.ContainsKey($key) -or $existingValues.ContainsKey($key)) {
        $wmHas++
    } else {
        $wmNeeds++
        $wmNew += $key
    }
}

Write-Host ""
Write-Host "--- Category 2: AXAML Watermark Attributes ---" -ForegroundColor Cyan
Write-Host "Files with watermarks: $($watermarkFileStats.Count)"
Write-Host "Total unique watermark strings: $wmTotal"
Write-Host "Already have translations: $wmHas" -ForegroundColor Green
Write-Host "Need new translations: $wmNeeds" -ForegroundColor Yellow

if ($wmNew.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Watermarks needing translation ===" -ForegroundColor Yellow
    $wmNew | Sort-Object | ForEach-Object {
        Write-Host "  $_"
    }
}

# ================================================================
# Report — Category 3: Code-behind (.axaml.cs) hardcoded strings
# ================================================================
$csTotal = $csStrings.Count
$csHas = 0
$csNeeds = 0
$csNew = @()

foreach ($key in $csStrings.Keys) {
    if ($existingKeys.ContainsKey($key) -or $existingValues.ContainsKey($key)) {
        $csHas++
    } else {
        $csNeeds++
        $csNew += $key
    }
}

Write-Host ""
Write-Host "--- Category 3: Code-behind (.axaml.cs) Hardcoded Strings ---" -ForegroundColor Cyan
Write-Host "Total .axaml.cs files scanned: $($csFiles.Count)"
Write-Host "Files with hardcoded strings: $($csFileStats.Count)"
Write-Host "Total unique hardcoded strings: $csTotal"
Write-Host "Already have translations: $csHas" -ForegroundColor Green
Write-Host "Need new translations: $csNeeds" -ForegroundColor Yellow

if ($csNew.Count -gt 0) {
    Write-Host ""
    Write-Host "=== Code-behind strings needing translation (first 50) ===" -ForegroundColor Yellow
    $csNew | Sort-Object | Select-Object -First 50 | ForEach-Object {
        Write-Host "  $_"
    }
}

# ================================================================
# Summary
# ================================================================
$grandTotal = $total + $wmTotal + $csTotal
$grandHas = $hasTranslation + $wmHas + $csHas
$grandNeeds = $needsTranslation + $wmNeeds + $csNeeds
$pct = if ($grandTotal -gt 0) { [Math]::Round($grandHas / $grandTotal * 100, 1) } else { 100 }

Write-Host ""
Write-Host "=== Overall Coverage Summary ===" -ForegroundColor Cyan
Write-Host "Grand total unique strings: $grandTotal"
Write-Host "Translated: $grandHas ($pct%)" -ForegroundColor Green
Write-Host "Missing: $grandNeeds" -ForegroundColor Yellow

Write-Host ""
Write-Host "=== Files by string count (top 20) ===" -ForegroundColor Cyan
$fileStats.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Host ("  {0,-50} {1}" -f $_.Key, $_.Value)
}
