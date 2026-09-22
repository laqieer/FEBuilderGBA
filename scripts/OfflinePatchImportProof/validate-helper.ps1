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
                . "$Code\Configuration.Tests.ps1"
                . "$Code\ProcessImage.ps1"
                . (Get-PinnedProofLibrary -Library RestagePolicy)
                $report.bindingCases=Invoke-PinnedRuntimeBindingTests
                if ($report.bindingCases -ne 22) { throw 'Incomplete pure binding cases.' }
                Add-Type -Path @("$Code\Readiness.cs","$Code\Policy.cs","$Code\Policy.Tests.cs") -ReferencedAssemblies $references
                $emptyPatchLibraryCases=Invoke-EmptyPatchLibraryTests $owned
                $rejectedRootClassCases=[DesktopPolicyTests]::RunRejectedRootClassTests()
                Assert-PickerIsolationInventories $emptyPatchLibraryCases $rejectedRootClassCases
                $rejectedRootClassSerializationNames=@(Invoke-RejectedRootClassSerializationTests)
                Assert-Proof ($rejectedRootClassSerializationNames.Count -eq 120) 'Rejected class wire case count.'
                $report.processImageCases=& "$Code\ProcessImage.Tests.ps1"
                if($report.processImageCases -ne 10) { throw 'Ten pure process-image cases required.' }
                $report.retainedImageCases=[RetainedProcessImageTests]::Run()
                if($report.retainedImageCases -ne 32) { throw 'Retained image inventory changed.' }
                $preparationExitCases=@(Invoke-PreparationExitObservationTests)
                Assert-PreparationExitCaseNames $preparationExitCases 88 'd31e67d296b9e17b9dc7df0c1ea73957252eec3cd28f4b555a44e6ed8019a4af'
                $preparationExitReportingCases=@(Invoke-PreparationExitReportingTests)
                Assert-PreparationExitCaseNames $preparationExitReportingCases 72 'f6028266a57578cd815a9458c04c7a46fa85878a5f184dba41ea854e838aab42'
                $report.cases=[DesktopPolicyTests]::Run()
                if ($report.cases -ne 539) { throw 'Original pure case inventory changed.' }
                $report.installedSnapshotCases=[DesktopPolicyTests]::RunSnapshotTests($owned)
                if($report.installedSnapshotCases -ne 26) { throw 'Incomplete installed snapshot cases.' }
                $report.startupObservationCases=[DesktopPolicyTests]::RunStartupTests()
                if($report.startupObservationCases -ne 75) { throw 'Startup observation case inventory changed.' }
                [DesktopPolicyTests]::AssertStartupCaseInventory()
                $report.ownedTreeCases=[DesktopPolicyTests]::RunOwnedTreeTests()
                if($report.ownedTreeCases -ne 159) { throw "Owned-tree case inventory changed: $($report.ownedTreeCases)." }
                [DesktopPolicyTests]::AssertOwnedTreeCaseInventory()
                $windowIdentityCases=[DesktopPolicyTests]::RunWindowIdentityTests()
                if($windowIdentityCases -ne 57){throw 'Window identity case inventory changed.'}
                [DesktopPolicyTests]::AssertWindowIdentityCaseInventory()
                $startupDiagnosticSerializationCases=0
                foreach($sample in [DesktopPolicyTests]::StartupDiagnosticSamples){
                    foreach($shape in @('compact','pretty','nested')){
                        $json=if($shape -ceq 'compact'){$sample|ConvertTo-Json -Depth 8 -Compress}
                            elseif($shape -ceq 'pretty'){$sample|ConvertTo-Json -Depth 8}
                            else{@{gui=@{StartupFailure=$sample}}|ConvertTo-Json -Depth 10}
                        Assert-Proof ([Text.Encoding]::UTF8.GetByteCount($json) -le 4096) 'Startup diagnostic byte bound.'
                        $decoded=ConvertFrom-Json $json -AsHashtable
                        if($shape -ceq 'nested'){$decoded=$decoded.gui.StartupFailure}
                        Assert-ProofKeys $decoded @('Predicate','Roots')
                        Assert-Proof ($decoded.Predicate -is [string] -and $decoded.Predicate.Length -le 64 -and
                            $decoded.Roots -is [array] -and $decoded.Roots.Count -le 8) 'Startup diagnostic envelope.'
                        foreach($row in $decoded.Roots){
                            Assert-ProofKeys $row @('Handle','Owner','WindowClass','Visible','MainControlVisible','LoadingLabelVisible','SetupWizardControlVisible')
                            Assert-Proof (($row.Handle -is [int] -or $row.Handle -is [long]) -and $row.Handle -gt 0 -and
                                $row.Handle -le [uint]::MaxValue -and ($row.Owner -is [int] -or $row.Owner -is [long]) -and
                                $row.Owner -ge 0 -and $row.Owner -le [uint]::MaxValue) 'Startup diagnostic handles.'
                            Assert-Proof ($row.WindowClass -ceq '#32770' -or ($row.WindowClass.Length -eq 45 -and
                                [DesktopPolicy]::AvaloniaClass($row.WindowClass))) 'Startup diagnostic class.'
                            foreach($key in @('Visible','MainControlVisible','LoadingLabelVisible','SetupWizardControlVisible')){
                                Assert-Proof ($row[$key] -is [bool]) 'Startup diagnostic role type.'
                            }
                        }
                        $startupDiagnosticSerializationCases++
                    }
                }
                if($startupDiagnosticSerializationCases -ne 12){throw 'Startup diagnostic serialization inventory changed.'}
                $queryDiagnosticCases=[DesktopPolicyTests]::RunQueryDiagnosticTests()
                if($queryDiagnosticCases -ne 40){throw 'Query diagnostic case inventory changed.'}
                [DesktopPolicyTests]::AssertQueryDiagnosticSampleInventory()
                $queryDiagnosticSerializationNames=[Collections.Generic.List[string]]::new()
                $queryDiagnosticSamples=[DesktopPolicyTests]::QueryDiagnosticSamples
                $queryDiagnosticSampleNames=[DesktopPolicyTests]::QueryDiagnosticSampleNames
                for($sampleIndex=0;$sampleIndex -lt $queryDiagnosticSamples.Length;$sampleIndex++){
                    $sample=$queryDiagnosticSamples[$sampleIndex]
                    foreach($shape in @('compact','pretty','nested')){
                        $json=if($shape -ceq 'compact'){$sample|ConvertTo-Json -Depth 8 -Compress}
                            elseif($shape -ceq 'pretty'){$sample|ConvertTo-Json -Depth 8}
                            else{@{gui=@{QueryFailure=$sample}}|ConvertTo-Json -Depth 10}
                        Assert-Proof ([Text.Encoding]::UTF8.GetByteCount($json) -le 4096) 'Query diagnostic byte bound.'
                        $decoded=ConvertFrom-Json $json -AsHashtable
                        if($shape -ceq 'nested'){$decoded=$decoded.gui.QueryFailure}
                        Assert-ProofKeys $decoded @('Stage','Selector','Predicate','ExpectedOwnedRoot','PreviouslyOwnedHandle',
                            'OwnedRootBefore','OwnedRootAfter','AliveBefore','AliveAfter','OwnPidBefore','OwnPidAfter',
                            'RootMatchesBefore','RootMatchesAfter','SeedOrdinal','SeedResolveKeyEqual','ResolveAlive',
                            'ResolvePidRelation','RejectedRootClass','OwnerHandle','OwnerClass','OwnerParent','OwnerNativeRoot',
                            'OwnerDepth','OwnerIsSelfRoot','OwnerChainCount','OwnerChainTransient','OwnerChainFirstHandle',
                            'OwnerChainFirstClass','OwnerChainLastHandle','OwnerChainLastClass','Nodes','Calls','Windows')
                        $expectedRejectedClass=if($queryDiagnosticSampleNames[$sampleIndex] -ceq
                            'resolve-success-before-registration-failure'){'OwnedAuxiliaryClass'}else{$null}
                        Assert-Proof ($decoded.ContainsKey('RejectedRootClass') -and
                            $decoded.RejectedRootClass -ceq $expectedRejectedClass) 'Exact original-sample rejected class.'
                        if($queryDiagnosticSampleNames[$sampleIndex] -ceq 'owner-diagnostics-nondefault'){
                            Assert-Proof (($decoded.OwnerHandle -is [int] -or $decoded.OwnerHandle -is [long]) -and
                                $decoded.OwnerHandle -eq 400 -and $decoded.OwnerClass -is [string] -and
                                $decoded.OwnerClass -ceq '#32770' -and
                                ($decoded.OwnerParent -is [int] -or $decoded.OwnerParent -is [long]) -and
                                $decoded.OwnerParent -eq 0 -and
                                ($decoded.OwnerNativeRoot -is [int] -or $decoded.OwnerNativeRoot -is [long]) -and
                                $decoded.OwnerNativeRoot -eq 400 -and
                                ($decoded.OwnerDepth -is [int] -or $decoded.OwnerDepth -is [long]) -and
                                $decoded.OwnerDepth -eq 0 -and $decoded.OwnerIsSelfRoot -is [bool] -and
                                $decoded.OwnerIsSelfRoot -and $null -eq $decoded.OwnerChainCount -and
                                $null -eq $decoded.OwnerChainTransient -and
                                ($decoded.OwnerChainFirstHandle -is [int] -or $decoded.OwnerChainFirstHandle -is [long]) -and
                                $decoded.OwnerChainFirstHandle -eq 0 -and $null -eq $decoded.OwnerChainFirstClass -and
                                ($decoded.OwnerChainLastHandle -is [int] -or $decoded.OwnerChainLastHandle -is [long]) -and
                                $decoded.OwnerChainLastHandle -eq 0 -and $null -eq $decoded.OwnerChainLastClass) 'Exact non-default owner diagnostics.'
                        }
                        Assert-Proof ($decoded.Count -eq 33 -and $decoded.Stage -ceq 'loading-handoff' -and
                            $decoded.Selector -ceq 'Discovery' -and $decoded.Predicate -is [string] -and
                            $decoded.Predicate -ceq $sample.Predicate -and $decoded.Predicate.Length -le 64) 'Query diagnostic envelope.'
                        foreach($key in @('ExpectedOwnedRoot','PreviouslyOwnedHandle','OwnedRootBefore','OwnedRootAfter')){
                            Assert-Proof (($decoded[$key] -is [int] -or $decoded[$key] -is [long]) -and
                                $decoded[$key] -ge 0 -and $decoded[$key] -le [uint]::MaxValue -and
                                $decoded[$key] -eq $sample.$key) 'Query diagnostic owned handle.'
                        }
                        foreach($key in @('AliveBefore','AliveAfter','OwnPidBefore','OwnPidAfter','RootMatchesBefore',
                            'RootMatchesAfter','SeedResolveKeyEqual','ResolveAlive')){
                            Assert-Proof (($null -eq $decoded[$key] -or $decoded[$key] -is [bool]) -and
                                $decoded[$key] -ceq $sample.$key) 'Query diagnostic nullable Boolean.'
                        }
                        Assert-Proof (($null -eq $decoded.SeedOrdinal -or
                            (($decoded.SeedOrdinal -is [int] -or $decoded.SeedOrdinal -is [long]) -and
                                $decoded.SeedOrdinal -ge 1 -and $decoded.SeedOrdinal -le 8)) -and
                            $decoded.SeedOrdinal -ceq $sample.SeedOrdinal) 'Query diagnostic bounded ordinal.'
                        Assert-Proof (($null -eq $decoded.ResolvePidRelation -or
                            ($decoded.ResolvePidRelation -is [string] -and
                                $decoded.ResolvePidRelation -cin @('zero','owned','foreign'))) -and
                            $decoded.ResolvePidRelation -ceq $sample.ResolvePidRelation) 'Query diagnostic fixed PID relation.'
                        foreach($key in @('Nodes','Calls','Windows')){
                            $maximum=@{Nodes=4096;Calls=65536;Windows=8}[$key]
                            Assert-Proof (($decoded[$key] -is [int] -or $decoded[$key] -is [long]) -and
                                $decoded[$key] -ge 0 -and $decoded[$key] -le $maximum -and
                                $decoded[$key] -eq $sample.$key) 'Query diagnostic legacy counter.'
                        }
                        foreach($sentinel in [DesktopPolicyTests]::QueryDiagnosticPrivateSentinels){
                            Assert-Proof ($json.IndexOf($sentinel,[StringComparison]::OrdinalIgnoreCase) -lt 0) 'Query diagnostic private value leaked.'
                        }
                        $queryDiagnosticSerializationNames.Add($queryDiagnosticSampleNames[$sampleIndex]+':'+$shape)
                    }
                }
                $queryDiagnosticSerializationDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
                    [Text.Encoding]::UTF8.GetBytes(($queryDiagnosticSerializationNames -join "`n")))).ToLowerInvariant()
                Assert-Proof ($queryDiagnosticSerializationNames.Count -eq 51 -and $queryDiagnosticSerializationDigest -ceq
                    '9ac69bee98244df7a57d34bab907750b663d40e630130d95f8d554aaf92989db') 'Query diagnostic serialization inventory changed.'
                [DesktopPolicyTests]::AssertQueryDiagnosticCaseInventory()
                $report.startupAcquisitionCases=[DesktopPolicyTests]::RunStartupAcquisitionTests()
                if($report.startupAcquisitionCases -ne 115){throw 'Startup acquisition case inventory changed.'}
                [DesktopPolicyTests]::AssertStartupAcquisitionCaseInventory()
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
