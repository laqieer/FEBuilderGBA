[CmdletBinding()]
param([string]$Configuration,[string]$ConfigurationSha256,[string]$NewGuiId)
$ErrorActionPreference='Stop'
$entryParameters=@{}+$PSBoundParameters
$entryArgumentCount=$args.Count
. (Join-Path $PSScriptRoot 'NonCopySupervisor.ps1')
function Write-RestageReceipt {
    param([string]$Path,[byte[]]$Bytes,[Collections.IDictionary]$Window,
        [scriptblock]$ReadClock,[string]$Phase,[double]$ExternalSeconds,
        [scriptblock]$CheckOperation={ param($Operation) $true })
    Write-RReportingFile @PSBoundParameters
}
if($MyInvocation.InvocationName -ne '.') {
    Assert-Proof ($entryArgumentCount -eq 0 -and $entryParameters.Configuration -and $entryParameters.ConfigurationSha256 -and
        $entryParameters.NewGuiId -cmatch '^[0-9a-f]{32}$') 'Declared configuration and fresh ID required.'
    Invoke-ProofSupervision -Mode Restage @entryParameters
}
