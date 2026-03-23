<# .SYNOPSIS
    Scan all Avalonia .axaml files for hardcoded translatable strings.
    Reports which strings already have translation entries and which are new.

    .DESCRIPTION
    Catalogs all TextBlock.Text, Expander.Header, Button.Content, CheckBox.Content,
    TabItem.Header, and Window.Title attributes that contain English text.
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

# Scan all .axaml files
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

# Report
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

Write-Host ""
Write-Host "=== Files by string count (top 20) ===" -ForegroundColor Cyan
$fileStats.GetEnumerator() | Sort-Object -Property Value -Descending | Select-Object -First 20 | ForEach-Object {
    Write-Host ("  {0,-50} {1}" -f $_.Key, $_.Value)
}
