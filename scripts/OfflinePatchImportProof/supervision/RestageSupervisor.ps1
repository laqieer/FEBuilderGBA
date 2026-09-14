throw 'PinnedProof.UnsupportedDirectRoute'
function ProofEnvelope {
    . (Get-PinnedProofLibrary -Library NonCopySupervisor)
    function Write-RestageReceipt {
        param([string]$Path,[byte[]]$Bytes,[Collections.IDictionary]$Window,
            [scriptblock]$ReadClock,[string]$Phase,[double]$ExternalSeconds,
            [scriptblock]$CheckOperation={ param($Operation) $true })
        Write-RReportingFile @PSBoundParameters
    }
    function Invoke-RestageSupervisorEntry {
        [CmdletBinding()]
        param([string]$Configuration,[string]$ConfigurationSha256,[string]$NewGuiId)
        $ErrorActionPreference='Stop'
        Assert-Proof ($args.Count -eq 0 -and $Configuration -and $ConfigurationSha256 -and
            $NewGuiId -cmatch '^[0-9a-f]{32}$') 'Declared configuration and fresh ID required.'
        Invoke-ProofSupervision -Mode Restage @PSBoundParameters
    }
}
