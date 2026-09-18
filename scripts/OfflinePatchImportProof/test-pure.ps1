function ProofEnvelope {
    function Invoke-AggregateEntry {
        $ErrorActionPreference = 'Stop'
        Set-StrictMode -Version Latest
        if ($PSVersionTable.PSEdition -ne 'Core' -or $PSVersionTable.PSVersion.Major -ne 7 -or $PSVersionTable.PSVersion -lt [version]'7.5') { throw 'Supported PowerShell 7.5+ required.' }
        $repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
        $root=Join-Path $repository ('TestResults\offline-patch-proof-'+[guid]::NewGuid().ToString('N'))
        [void][IO.Directory]::CreateDirectory($root)
        . (Join-Path $PSScriptRoot '..\WindowsDesktopProof\PinnedLoader.Tests.ps1')
        $compiled=0
        if($IsWindows){
            $references=@([IO.Directory]::GetFiles((Join-Path $PSHOME 'ref'),'*.dll'))
            $runtime=@(foreach($name in @('System.Private.Windows.Core.dll','System.Private.Windows.GdiPlus.dll','WindowsBase.dll','UIAutomationTypes.dll','UIAutomationClient.dll','Microsoft.Win32.SystemEvents.dll','System.Drawing.Common.dll')){
                $path=Join-Path $PSHOME $name
                if(![IO.File]::Exists($path)){throw "Missing existing PowerShell runtime reference: $name"}
                $path
            })
            Add-Type -Path @((Join-Path $PSScriptRoot 'Readiness.cs'),(Join-Path $PSScriptRoot 'Policy.cs')) -ReferencedAssemblies $references -OutputAssembly (Join-Path $root 'Policy.compile-only.dll') -OutputType Library
            Add-Type -Path @((Join-Path $PSScriptRoot 'Readiness.cs'),(Join-Path $PSScriptRoot 'Policy.cs'),(Join-Path $PSScriptRoot 'Desktop.cs')) -ReferencedAssemblies ($references+$runtime) -OutputAssembly (Join-Path $root 'Desktop.compile-only.dll') -OutputType Library
            $compiled=2
        }
        . (Join-Path $PSScriptRoot 'Configuration.ps1')
        . (Join-Path $PSScriptRoot 'Configuration.Tests.ps1')
        . (Join-Path $PSScriptRoot 'ProcessImage.ps1')
        # Match the supervisor's standalone reader assembly; do not redefine its types
        # inside the test assembly before the live integration loads the same reader.
        if($IsWindows){
            $readerAssembly=Join-Path $root 'Readiness.tests.dll'
            $imageReferences=@(Get-ProofTestCompileReferences | ForEach-Object {$_.path})
            Add-Type -Path (Join-Path $PSScriptRoot 'Readiness.cs') -ReferencedAssemblies $imageReferences -OutputAssembly $readerAssembly
            $readerStream=[IO.MemoryStream]::new([IO.File]::ReadAllBytes($readerAssembly),$false)
            try{[void][Runtime.Loader.AssemblyLoadContext]::Default.LoadFromStream($readerStream)}
            finally{$readerStream.Dispose()}
            Add-Type -Path @((Join-Path $PSScriptRoot 'Policy.cs'),(Join-Path $PSScriptRoot 'Policy.Tests.cs')) -ReferencedAssemblies @($references + $readerAssembly)
        }else{
            Add-Type -Path @((Join-Path $PSScriptRoot 'Readiness.cs'),(Join-Path $PSScriptRoot 'Policy.cs'),(Join-Path $PSScriptRoot 'Policy.Tests.cs'))
        }
        . (Join-Path $PSScriptRoot 'RuntimeBinding.ps1')
        . (Join-Path $PSScriptRoot 'RuntimeBinding.Tests.ps1')
        $emptyPatchLibraryCases=Invoke-EmptyPatchLibraryTests $root
        $rejectedRootClassCases=[DesktopPolicyTests]::RunRejectedRootClassTests()
        Assert-PickerIsolationInventories $emptyPatchLibraryCases $rejectedRootClassCases
        $rejectedRootClassSerializationNames=@(Invoke-RejectedRootClassSerializationTests)
        Assert-Proof ($rejectedRootClassSerializationNames.Count -eq 120) 'Rejected class wire case count.'
        $imageCases=& (Join-Path $PSScriptRoot 'ProcessImage.Tests.ps1')
        $retainedImageCases=[RetainedProcessImageTests]::Run()
        if($retainedImageCases -ne 32) { throw 'Retained image inventory changed.' }
        $bindingCases=Invoke-PinnedRuntimeBindingTests
        $lexicalCases=Invoke-WindowsLexicalBindingTests
        $policyCases=[DesktopPolicyTests]::Run()
        $snapshotCases=[DesktopPolicyTests]::RunSnapshotTests($root)
        if($snapshotCases -ne 26) { throw 'Incomplete installed snapshot cases.' }
        $startupCases=[DesktopPolicyTests]::RunStartupTests()
        if($startupCases -ne 75) { throw 'Startup observation case inventory changed.' }
        [DesktopPolicyTests]::AssertStartupCaseInventory()
        $ownedTreeCases=[DesktopPolicyTests]::RunOwnedTreeTests()
        if($ownedTreeCases -ne 146) { throw "Owned-tree case inventory changed: $ownedTreeCases." }
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
        if($queryDiagnosticCases -ne 39){throw 'Query diagnostic case inventory changed.'}
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
                    'ResolvePidRelation','RejectedRootClass','Nodes','Calls','Windows')
                $expectedRejectedClass=if($queryDiagnosticSampleNames[$sampleIndex] -ceq
                    'resolve-success-before-registration-failure'){'OwnedAuxiliaryClass'}else{$null}
                Assert-Proof ($decoded.ContainsKey('RejectedRootClass') -and
                    $decoded.RejectedRootClass -ceq $expectedRejectedClass) 'Exact original-sample rejected class.'
                Assert-Proof ($decoded.Count -eq 21 -and $decoded.Stage -ceq 'loading-handoff' -and
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
        Assert-Proof ($queryDiagnosticSerializationNames.Count -eq 48 -and $queryDiagnosticSerializationDigest -ceq
            'daf65e77d4b7fb0a6775f4a14193d0041270d10fa46256beacfb7c3a64e933f7') 'Query diagnostic serialization inventory changed.'
        [DesktopPolicyTests]::AssertQueryDiagnosticCaseInventory()
        if($imageCases -ne 10 -or $bindingCases -ne 22 -or $policyCases -ne 522) { throw 'Incomplete original pure case inventory.' }
        $startupAcquisitionCases=[DesktopPolicyTests]::RunStartupAcquisitionTests()
        if($startupAcquisitionCases -ne 115){throw 'Startup acquisition case inventory changed.'}
        [DesktopPolicyTests]::AssertStartupAcquisitionCaseInventory()
        . (Join-Path $PSScriptRoot 'restage\RestagePolicy.ps1')
        $imageReportingCases=Invoke-RetainedImageReportingTests
        if($imageReportingCases -ne 16) { throw 'Retained image reporting inventory changed.' }
        $preparationExitCases=@(Invoke-PreparationExitObservationTests)
        Assert-PreparationExitCaseNames $preparationExitCases 88 'd31e67d296b9e17b9dc7df0c1ea73957252eec3cd28f4b555a44e6ed8019a4af'
        $preparationExitReportingCases=@(Invoke-PreparationExitReportingTests)
        Assert-PreparationExitCaseNames $preparationExitReportingCases 72 'f6028266a57578cd815a9458c04c7a46fa85878a5f184dba41ea854e838aab42'
        $writerCases=Invoke-ReportingWriterTests $root
        $laterProductionCases=Invoke-LaterProductionTests $root
        $configurationCases=Invoke-ConfigurationIntegrationTests $root
        $referenceCases=Invoke-CompilerReferenceFixtureTests $root
        if($referenceCases -ne $(if($IsWindows){8}else{0})){throw 'Compiler reference fixture inventory changed.'}
        $restage=& (Join-Path $PSScriptRoot 'restage\test-pure.ps1') | ConvertFrom-Json
        if(!$restage.passed -or $restage.executed -ne 322 -or $restage.failed -ne 0 -or $restage.skipped -ne 0){throw 'Restage inventory incomplete.'}
        $loaderCases=Invoke-PinnedLoaderTests $root
        $result=[pscustomobject]@{
            cases = $policyCases
            installedSnapshotCases = $snapshotCases
            startupObservationCases = $startupCases
            startupAcquisitionCases = $startupAcquisitionCases
            ownedTreeCases = $ownedTreeCases
            processImageCases = $imageCases
            retainedImageCases = $retainedImageCases
            retainedImageReportingCases = $imageReportingCases
            runtimeBindingCases = $bindingCases
            windowsLexicalCases = $lexicalCases
            reportingWriterCases = $writerCases
            laterProductionCases = $laterProductionCases
            configurationIntegrationCases = $configurationCases
            compilerReferenceFixtureCases = $referenceCases
            restageCases = $restage.executed
            loaderCases = $loaderCases
            liveProcessImageIntegrationCases = [int]$IsWindows
            outputOnlyCompilations = $compiled
            passed = $true
            native_calls = [bool]$IsWindows
            gui_authorization = $false
        }
        [IO.Directory]::Delete($root,$true)
        $result | ConvertTo-Json -Compress
    }
}

# This outer driver trusts the reviewed checkout; it is not independently pinned admission.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\WindowsDesktopProof\PinnedLoader.ps1')
. (Join-Path $PSScriptRoot '..\WindowsDesktopProof\PinnedLoader.Tests.ps1')
$checkoutRoot=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) ('TestResults\pinned-proof-'+[guid]::NewGuid().ToString('N'))
$checkoutFixture=New-PinnedProofFixture $checkoutRoot
Invoke-PinnedTestMode -Fixture $checkoutFixture -Mode AggregatePure
[IO.Directory]::Delete($checkoutRoot,$true)
$preparedRoot=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) ('TestResults\prepared-proof-'+[guid]::NewGuid().ToString('N'))
$preparedFixture=New-PinnedProofFixture $preparedRoot -Prepared
Invoke-PinnedTestMode -Fixture $preparedFixture -Mode PreparedPure
[IO.Directory]::Delete($preparedRoot,$true)
