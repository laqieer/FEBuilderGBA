throw 'PinnedProof.UnsupportedDirectRoute'
function ProofEnvelope {
    function Invoke-ValidationEntry {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory)][string]$Configuration,
            [Parameter(Mandatory)][string]$ConfigurationSha256,
            [Parameter(Mandatory)][ValidateSet('Pure','Compile')][string]$Mode,
            [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{32}$')][string]$Id,
            [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$SourceManifestSha256,
            [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$HelperSourceManifestSha256
        )
        . (Join-Path $PSScriptRoot 'Configuration.ps1')
        $proofConfiguration=Read-ProofConfiguration $Configuration $ConfigurationSha256 -Purpose Build
        $Code=$PSScriptRoot
        $null=Assert-ProofSource $proofConfiguration $Code
        Assert-Proof ($SourceManifestSha256 -ceq $proofConfiguration.sourceManifest.sha256) 'Source configuration binding.'
        Assert-Proof ($HelperSourceManifestSha256 -ceq $SourceManifestSha256) 'Helper source binding.'
        $ErrorActionPreference='Stop'
        Set-StrictMode -Version Latest
        $PSModuleAutoLoadingPreference='None'
        foreach($name in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management','Microsoft.PowerShell.Security')) {
            Import-Module ([IO.Path]::Combine($PSHOME,'Modules',$name,$name+'.psd1')) -ErrorAction Stop
        }
        $B=$proofConfiguration.preparation.root
        $D=$proofConfiguration.outputRoot
        $owned="$B\validate-$Id"
        if ([Environment]::CurrentDirectory -cne $owned) { throw 'Only the bounded Validate parent may launch this source.' }
        function Plain([string]$path) {
            for($p=$path; $p; $p=[IO.Path]::GetDirectoryName($p)) {
                if (([IO.File]::GetAttributes($p) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse path.' }
            }
        }
        function Verify([string]$path,[string]$sha,[long]$bytes=-1) {
            Plain $path
            if ($bytes -ge 0 -and ([IO.FileInfo]$path).Length -ne $bytes) { throw "Size mismatch: $path" }
            if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sha) { throw "Pin mismatch: $path" }
        }
        function Read-BindingMetadata([string]$path) {
            Plain $path
            @{bytes=([IO.FileInfo]$path).Length;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}
        }
        . "$Code\RuntimeBinding.ps1"
        $attempt=Get-Content -LiteralPath "$owned\attempt.json" -Raw | ConvertFrom-Json
        if ($attempt.stage -cne 'Validate' -or $attempt.runId -cne $Id -or $attempt.sourceManifestSha256 -cne $SourceManifestSha256 -or $attempt.helperSourceManifestSha256 -cne $HelperSourceManifestSha256) { throw 'No matching bounded validation attempt.' }
        $prepared=Read-ProofPreparation $proofConfiguration; $tools=$prepared.metadata
        if ($PSHOME -cne $tools.pshome) { throw 'PS host mismatch.' }
        foreach($r in @($tools.tools)+@($tools.runtimeAssemblies)+@($tools.compileReferences)) { Verify $r.path $r.sha256 $r.bytes }
        $references=@($tools.compileReferences | ForEach-Object path)
        $report=[ordered]@{passed=$false;mode=$Mode;nativeCalls=$false;appLaunched=$false;cases=0;bindingCases=0;bindingPolicy='pinned-support-or-pshome';loadedRuntimeMetadata=@();runtimeBindings=@();failure=$null}
        try {
            $metadataRows=@($tools.runtimeAssemblies)+@($tools.tools | Where-Object { $_.path -ceq "$PSHOME\System.Drawing.dll" })
            $names=@($metadataRows | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_.path) })
            foreach($a in [AppDomain]::CurrentDomain.GetAssemblies()) {
                if ($a.GetName().Name -notin $names) { continue }
                $row=@($metadataRows | Where-Object { [IO.Path]::GetFileNameWithoutExtension($_.path) -ceq $a.GetName().Name })[0]
                $report.loadedRuntimeMetadata+=Confirm-PinnedRuntimeBinding `
                    -SupportPath (Join-Path "$owned\support" ([IO.Path]::GetFileName($row.path))) -HostPath $row.path `
                    -ExpectedIdentity ([Reflection.AssemblyName]::GetAssemblyName($row.path).FullName) `
                    -ExpectedBytes $row.bytes -ExpectedSha256 $row.sha256 -ActualPath $a.Location -ActualIdentity $a.FullName `
                    -ReadMetadata { param($path) Read-BindingMetadata $path }
            }
            if($Mode -ceq 'Pure') {
                . "$Code\RuntimeBinding.Tests.ps1"
                $report.bindingCases=Invoke-PinnedRuntimeBindingTests
                if ($report.bindingCases -ne 22) { throw 'Incomplete pure binding cases.' }
                Add-Type -Path @("$Code\Readiness.cs","$Code\Policy.cs","$Code\Policy.Tests.cs") -ReferencedAssemblies $references
                $report.processImageCases=& "$Code\ProcessImage.Tests.ps1"
                if($report.processImageCases -ne 10) { throw 'Ten pure process-image cases required.' }
                $report.retainedImageCases=[RetainedProcessImageTests]::Run()
                if($report.retainedImageCases -ne 32) { throw 'Retained image inventory changed.' }
                $report.cases=[DesktopPolicyTests]::Run()
                if ($report.cases -le 0) { throw 'No pure cases executed.' }
                $report.installedSnapshotCases=[DesktopPolicyTests]::RunSnapshotTests($owned)
                if($report.installedSnapshotCases -ne 26) { throw 'Incomplete installed snapshot cases.' }
                $report.startupObservationCases=[DesktopPolicyTests]::RunStartupTests()
                if($report.startupObservationCases -ne 75) { throw 'Startup observation case inventory changed.' }
                $report.ownedTreeCases=[DesktopPolicyTests]::RunOwnedTreeTests()
                if($report.ownedTreeCases -ne 108) { throw "Owned-tree case inventory changed: $($report.ownedTreeCases)." }
            } else {
                $support="$owned\support"
                if ([IO.Path]::Exists($support)) { throw 'Binding attempt already consumed.' }
                [void][IO.Directory]::CreateDirectory($support)
                $runtimeReferences=@(foreach($row in $tools.runtimeAssemblies) {
                    $path=Join-Path $support ([IO.Path]::GetFileName($row.path))
                    Verify $row.path $row.sha256 $row.bytes
                    [IO.File]::Copy($row.path,$path,$false)
                    Verify $row.path $row.sha256 $row.bytes
                    Verify $path $row.sha256 $row.bytes
                    $path
                })
                for($i=0; $i -lt $runtimeReferences.Count; $i++) {
                    $path=$runtimeReferences[$i]; $row=$tools.runtimeAssemblies[$i]
                    $identity=[Reflection.AssemblyName]::GetAssemblyName($path).FullName
                    $assembly=[Reflection.Assembly]::LoadFrom($path)
                    $binding=@{expectedPath=$path;expectedHostPath=$row.path;expectedIdentity=$identity;expectedSha256=$row.sha256;actualPath=$assembly.Location;actualIdentity=$assembly.FullName;actualSha256=$null;accepted=$false}
                    $report.runtimeBindings+=$binding
                    $accepted=Confirm-PinnedRuntimeBinding -SupportPath $path -HostPath $row.path `
                        -ExpectedIdentity $identity -ExpectedBytes $row.bytes -ExpectedSha256 $row.sha256 `
                        -ActualPath $assembly.Location -ActualIdentity $assembly.FullName `
                        -ReadMetadata { param($candidate) Read-BindingMetadata $candidate }
                    $binding.actualSha256=$accepted.actualSha256
                    $binding.actualBytes=$accepted.actualBytes
                    $binding.allowedLocation=$accepted.allowedLocation
                    Verify $row.path $row.sha256 $row.bytes
                    Verify $path $row.sha256 $row.bytes
                    $binding.accepted=$true
                }
                foreach($name in @('Policy.compile-only.dll','Desktop.compile-only.dll')) {
                    if ([IO.Path]::Exists("$owned\$name")) { throw 'Compile attempt already consumed.' }
                }
                # Output-only compilations cover the launch and runner shapes without invoking their methods.
                Add-Type -Path @("$Code\Readiness.cs","$Code\Policy.cs") -ReferencedAssemblies $references -OutputAssembly "$owned\Policy.compile-only.dll" -OutputType Library
                Add-Type -Path @("$Code\Readiness.cs","$Code\Policy.cs","$Code\Desktop.cs") -ReferencedAssemblies ($references+$runtimeReferences) -OutputAssembly "$owned\Desktop.compile-only.dll" -OutputType Library
                foreach($name in @('Policy.compile-only.dll','Desktop.compile-only.dll')) {
                    if (![IO.File]::Exists("$owned\$name")) { throw "Compile produced no assembly: $name" }
                }
                for($i=0; $i -lt $runtimeReferences.Count; $i++) {
                    Verify $runtimeReferences[$i] $tools.runtimeAssemblies[$i].sha256 $tools.runtimeAssemblies[$i].bytes
                }
            }
            $null=Assert-ProofSource $proofConfiguration $Code
            $report.passed=$true
        } catch {
            $report.failure=$_.Exception.Message
        } finally {
            $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($report | ConvertTo-Json -Depth 5))
            $s=[IO.File]::Open("$owned\$Mode.json",[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try { $s.Write($bytes) } finally { $s.Dispose() }
        }
        if (!$report.passed) { throw $report.failure }
    }
}
