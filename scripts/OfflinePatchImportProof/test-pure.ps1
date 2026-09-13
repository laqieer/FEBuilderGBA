$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Core') { throw 'PowerShell 7 required.' }
# This entry point never loads Desktop.cs, calls Capture, or operates on a desktop.
Add-Type -Path @(
    (Join-Path $PSScriptRoot 'Readiness.cs'),
    (Join-Path $PSScriptRoot 'Policy.cs'),
    (Join-Path $PSScriptRoot 'Policy.Tests.cs')
)
. (Join-Path $PSScriptRoot 'RuntimeBinding.ps1')
. (Join-Path $PSScriptRoot 'RuntimeBinding.Tests.ps1')
$imageCases=& (Join-Path $PSScriptRoot 'ProcessImage.Tests.ps1')
$bindingCases=Invoke-PinnedRuntimeBindingTests
$lexicalCases=Invoke-WindowsLexicalBindingTests
$policyCases=[DesktopPolicyTests]::Run()
if($imageCases -ne 10 -or $bindingCases -ne 22 -or $policyCases -ne 522) { throw 'Incomplete original pure case inventory.' }
[pscustomobject]@{
    cases = $policyCases
    processImageCases = $imageCases
    runtimeBindingCases = $bindingCases
    windowsLexicalCases = $lexicalCases
    passed = $true
    native_calls = $false
    gui_authorization = $false
} | ConvertTo-Json -Compress
