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
            Add-Type -Path (Join-Path $PSScriptRoot 'Policy.cs') -ReferencedAssemblies $references -OutputAssembly (Join-Path $root 'Policy.compile-only.dll') -OutputType Library
            Add-Type -Path @((Join-Path $PSScriptRoot 'Readiness.cs'),(Join-Path $PSScriptRoot 'Policy.cs'),(Join-Path $PSScriptRoot 'Desktop.cs')) -ReferencedAssemblies ($references+$runtime) -OutputAssembly (Join-Path $root 'Desktop.compile-only.dll') -OutputType Library
            $compiled=2
        }
        # Output-only compilation above does not load or invoke desktop/native methods.
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
        $snapshotCases=[DesktopPolicyTests]::RunSnapshotTests($root)
        if($snapshotCases -ne 26) { throw 'Incomplete installed snapshot cases.' }
        $startupCases=[DesktopPolicyTests]::RunStartupTests()
        if($startupCases -ne 75) { throw 'Startup observation case inventory changed.' }
        if($imageCases -ne 10 -or $bindingCases -ne 22 -or $policyCases -ne 522) { throw 'Incomplete original pure case inventory.' }
        . (Join-Path $PSScriptRoot 'Configuration.ps1')
        . (Join-Path $PSScriptRoot 'Configuration.Tests.ps1')
        $writerCases=Invoke-ReportingWriterTests $root
        $laterProductionCases=Invoke-LaterProductionTests $root
        $configurationCases=Invoke-ConfigurationIntegrationTests $root
        $restage=& (Join-Path $PSScriptRoot 'restage\test-pure.ps1') | ConvertFrom-Json
        if(!$restage.passed -or $restage.executed -ne 322 -or $restage.failed -ne 0 -or $restage.skipped -ne 0){throw 'Restage inventory incomplete.'}
        $loaderCases=Invoke-PinnedLoaderTests $root
        [pscustomobject]@{
            cases = $policyCases
            installedSnapshotCases = $snapshotCases
            startupObservationCases = $startupCases
            processImageCases = $imageCases
            runtimeBindingCases = $bindingCases
            windowsLexicalCases = $lexicalCases
            reportingWriterCases = $writerCases
            laterProductionCases = $laterProductionCases
            configurationIntegrationCases = $configurationCases
            restageCases = $restage.executed
            loaderCases = $loaderCases
            outputOnlyCompilations = $compiled
            passed = $true
            native_calls = $false
            gui_authorization = $false
        } | ConvertTo-Json -Compress
        [IO.Directory]::Delete($root,$true)
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
