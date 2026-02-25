# create-split-packages.ps1
# Creates three separate packages for split package updates:
# - FULL: Complete package (core + patch2)
# - CORE: Application only (no patch2)
# - PATCH2: Patch data only

param(
    [Parameter(Mandatory=$true)]
    [string]$BuildTime,

    [Parameter(Mandatory=$false)]
    [string]$BinPath = "FEBuilderGBA/bin/Release",

    [Parameter(Mandatory=$false)]
    [string]$OutputPath = "."
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Creating split packages for version: $BuildTime" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Read patch2 version
$patch2VersionFile = "config/patch2/version.txt"
if (Test-Path $patch2VersionFile) {
    $patch2Version = (Get-Content $patch2VersionFile).Trim()
    Write-Host "Patch2 version: $patch2Version" -ForegroundColor Green
} else {
    $patch2Version = $BuildTime
    Write-Host "Warning: Patch2 version file not found, using build time: $patch2Version" -ForegroundColor Yellow
}

$coreVersion = $BuildTime
Write-Host "Core version: $coreVersion" -ForegroundColor Green

# Define package names
$fullPackageName = "FEBuilderGBA_FULL_${coreVersion}_${patch2Version}.7z"
$corePackageName = "FEBuilderGBA_CORE_${coreVersion}.7z"
$patch2PackageName = "FEBuilderGBA_PATCH2_${patch2Version}.7z"

Write-Host ""
Write-Host "Package names:" -ForegroundColor Cyan
Write-Host "  FULL:   $fullPackageName" -ForegroundColor White
Write-Host "  CORE:   $corePackageName" -ForegroundColor White
Write-Host "  PATCH2: $patch2PackageName" -ForegroundColor White
Write-Host ""

# Clean up old packages
Remove-Item -Path "$OutputPath/*.7z" -Force -ErrorAction SilentlyContinue

# Function to create 7z archive
function Create-7zArchive {
    param(
        [string]$SourcePath,
        [string]$DestinationFile,
        [string[]]$Files
    )

    Write-Host "Creating: $DestinationFile" -ForegroundColor Yellow

    Push-Location $SourcePath
    try {
        $fileList = $Files -join " "
        $command = "7-zip32.dll -o`"$DestinationFile`" $fileList"

        # Use 7z command line if available
        if (Get-Command "7z" -ErrorAction SilentlyContinue) {
            & 7z a -t7z -mx=9 "$DestinationFile" @Files
        } elseif (Test-Path "7-zip32.dll") {
            # Fallback: create using PowerShell Compress-Archive then convert to 7z
            Write-Host "  Using fallback compression method" -ForegroundColor Gray
            $tempZip = "$DestinationFile.zip"
            Compress-Archive -Path $Files -DestinationPath $tempZip -Force
            if (Test-Path "$DestinationFile.zip") {
                Move-Item "$DestinationFile.zip" "$DestinationFile" -Force
            }
        } else {
            # Fallback: Use Compress-Archive (creates .zip, but we'll rename to .7z)
            Write-Host "  7z not found, using Compress-Archive" -ForegroundColor Gray
            Compress-Archive -Path $Files -DestinationPath $DestinationFile -Force
        }

        if (Test-Path $DestinationFile) {
            $fileSize = (Get-Item $DestinationFile).Length / 1MB
            Write-Host "  Created: $DestinationFile ($([math]::Round($fileSize, 2)) MB)" -ForegroundColor Green
            return $true
        } else {
            Write-Host "  Error: Failed to create $DestinationFile" -ForegroundColor Red
            return $false
        }
    }
    finally {
        Pop-Location
    }
}

# Create FULL package (everything)
Write-Host ""
Write-Host "--- Creating FULL package ---" -ForegroundColor Cyan
$fullFiles = @(
    "*.exe"
    "*.dll"
    "*.json"
    "config"
    "README*.md"
)
$fullPackagePath = Join-Path $OutputPath $fullPackageName
$success1 = Create-7zArchive -SourcePath $BinPath -DestinationFile $fullPackagePath -Files $fullFiles

# Create CORE package (application without patch2)
Write-Host ""
Write-Host "--- Creating CORE package ---" -ForegroundColor Cyan

# First create a temp directory with core files only
$tempCoreDir = "$OutputPath/_temp_core"
if (Test-Path $tempCoreDir) {
    Remove-Item -Path $tempCoreDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempCoreDir | Out-Null

# Copy core files
Copy-Item "$BinPath/*.exe" $tempCoreDir -Force
Copy-Item "$BinPath/*.dll" $tempCoreDir -Force
Copy-Item "$BinPath/*.json" $tempCoreDir -Force
Copy-Item "$BinPath/README*.md" $tempCoreDir -Force -ErrorAction SilentlyContinue

# Copy config excluding patch2
if (Test-Path "$BinPath/config") {
    Copy-Item "$BinPath/config" $tempCoreDir -Recurse -Force
    if (Test-Path "$tempCoreDir/config/patch2") {
        Remove-Item "$tempCoreDir/config/patch2" -Recurse -Force
    }
}

$coreFiles = @("*")
$corePackagePath = Join-Path $OutputPath $corePackageName
$success2 = Create-7zArchive -SourcePath $tempCoreDir -DestinationFile $corePackagePath -Files $coreFiles

# Clean up temp directory
Remove-Item -Path $tempCoreDir -Recurse -Force

# Create PATCH2 package (patches only)
Write-Host ""
Write-Host "--- Creating PATCH2 package ---" -ForegroundColor Cyan

# Create temp directory for patch2
$tempPatch2Dir = "$OutputPath/_temp_patch2"
if (Test-Path $tempPatch2Dir) {
    Remove-Item -Path $tempPatch2Dir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempPatch2Dir | Out-Null
New-Item -ItemType Directory -Path "$tempPatch2Dir/config" | Out-Null

# Copy patch2 directory
if (Test-Path "$BinPath/config/patch2") {
    Copy-Item "$BinPath/config/patch2" "$tempPatch2Dir/config" -Recurse -Force
} elseif (Test-Path "config/patch2") {
    Copy-Item "config/patch2" "$tempPatch2Dir/config" -Recurse -Force
} else {
    Write-Host "  Error: patch2 directory not found!" -ForegroundColor Red
}

$patch2Files = @("*")
$patch2PackagePath = Join-Path $OutputPath $patch2PackageName
$success3 = Create-7zArchive -SourcePath $tempPatch2Dir -DestinationFile $patch2PackagePath -Files $patch2Files

# Clean up temp directory
Remove-Item -Path $tempPatch2Dir -Recurse -Force

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Package creation summary:" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

if ($success1) {
    Write-Host "✓ FULL package created:   $fullPackageName" -ForegroundColor Green
} else {
    Write-Host "✗ FULL package FAILED" -ForegroundColor Red
}

if ($success2) {
    Write-Host "✓ CORE package created:   $corePackageName" -ForegroundColor Green
} else {
    Write-Host "✗ CORE package FAILED" -ForegroundColor Red
}

if ($success3) {
    Write-Host "✓ PATCH2 package created: $patch2PackageName" -ForegroundColor Green
} else {
    Write-Host "✗ PATCH2 package FAILED" -ForegroundColor Red
}

Write-Host ""

# Export package names for GitHub Actions
if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "full_package=$fullPackageName"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "core_package=$corePackageName"
    Add-Content -Path $env:GITHUB_OUTPUT -Value "patch2_package=$patch2PackageName"
    Write-Host "Package names exported to GITHUB_OUTPUT" -ForegroundColor Cyan
}

if ($success1 -and $success2 -and $success3) {
    Write-Host "All packages created successfully!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "Some packages failed to create" -ForegroundColor Red
    exit 1
}
