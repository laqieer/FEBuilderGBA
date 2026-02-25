# Quick test of ArchSevenZip extraction
$testDir = "test_archive_temp"
$testFile = "test.txt"
$archiveFile = "test.zip"

# Create test file
"Test content" | Out-File -FilePath $testFile

# Create zip archive using PowerShell
Compress-Archive -Path $testFile -DestinationPath $archiveFile -Force

# Clean up test file
Remove-Item $testFile

# Try to extract using the application
Write-Host "Testing extraction..."
$testExtractDir = "test_extract_output"
New-Item -ItemType Directory -Path $testExtractDir -Force | Out-Null

# We'll need to test this through the actual application
# For now, let's just verify the zip was created
if (Test-Path $archiveFile) {
    Write-Host "Test archive created: $archiveFile"
    Write-Host "Size: $((Get-Item $archiveFile).Length) bytes"
} else {
    Write-Host "ERROR: Failed to create test archive"
}

# Cleanup
Remove-Item $archiveFile -ErrorAction SilentlyContinue
Remove-Item $testExtractDir -Recurse -ErrorAction SilentlyContinue
