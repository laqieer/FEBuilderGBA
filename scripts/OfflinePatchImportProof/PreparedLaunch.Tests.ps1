throw 'PinnedProof.UnsupportedDirectRoute'
function ProofEnvelope {
    function Invoke-PreparedSharedStartEntry {
        . (Get-PinnedProofLibrary -Library Configuration)
        . (Get-PinnedProofLibrary -Library Prepared)
        . (Get-PinnedProofLibrary -Library Run)
        Assert-Proof ($IsWindows -and [Threading.Thread]::CurrentThread.ApartmentState -eq 'STA') 'Fresh Windows STA test host.'
        Assert-Proof ($null -eq ('BoundedDesktopSmoke' -as [type])) 'Real desktop driver must not be loaded into this test host.'
        Add-Type -Path @((Join-Path $PSScriptRoot 'PreparedLaunch.cs'),(Join-Path $PSScriptRoot 'PreparedLaunch.Tests.cs')) `
            -CompilerOptions '/define:PREPARED_SHARED_START_TEST' -ErrorAction Stop -WarningAction Stop
        # CreateProcess has a narrower working-directory bound than managed source reads.
        $testRoot=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) ('TestResults\ss-'+[guid]::NewGuid().ToString('N').Substring(0,8))
        [void][IO.Directory]::CreateDirectory($testRoot)
        $actorPin=Get-PreparedPin ([Environment]::ProcessPath)
        $originalDirectory=[Environment]::CurrentDirectory
        $names=[Collections.Generic.List[string]]::new()
        $allPassed=$false
        # Import/target-image substitutions are scoped to this test entry, not loader bindings or production modes.
        function Read-PreparedConfiguration {param($Configuration,$ConfigurationSha256) return $caseConfiguration}
        function Import-PreparedBootstrap {param($C) return 'test-only-bootstrap'}
        function Read-PreparedJson {param($Pin,$Bootstrap) return ,(Read-ProofPinnedJson $Pin)}
        function Import-PreparedHelper {param($C,$B,$Bootstrap) return $caseBasis}
        function Join-Path {
            param([string]$Path,[string]$ChildPath)
            if($Path -ceq $caseRoot -and $ChildPath -ceq 'app\FEBuilderGBA.Avalonia.exe'){return $actorPin.path}
            return Microsoft.PowerShell.Management\Join-Path -Path $Path -ChildPath $ChildPath
        }
        function Get-ProcessImageObservation {
            param($HasExited,$ReadImage,$Remaining,$Expected,$Comparison,$Role,$Observation)
            Assert-Proof (!(& $HasExited) -and $null -ne (& $ReadImage) -and (& $Remaining) -gt 0 -and
                $Expected -ceq $actorPin.path -and $Comparison -eq [StringComparison]::Ordinal -and
                $Role -ceq 'run-app-initial') 'Actual shared image-observation operands.'
            $identity=Read-ProofJsonFile (Microsoft.PowerShell.Management\Join-Path $runRoot 'app-identity.json')
            Assert-Proof ($identity.pid -eq $process.Id -and $identity.start_ticks -eq $process.StartTime.ToUniversalTime().Ticks -and
                $identity.runner_pid -eq $currentHost.Id -and $identity.runner_start_ticks -eq $currentHost.StartTime.ToUniversalTime().Ticks -and
                $identity.run_id -ceq $manifest.runId -and $identity.input_sha256 -ceq $InputManifestSha256 -and
                $identity.source_sha256 -ceq $SourceManifestSha256 -and $identity.exe -ceq $Expected) 'Actual shared identity receipt.'
            Assert-Proof ([IO.File]::Exists($caseCommit) -and $null -ne $stdoutDrain -and $null -ne $stderrDrain) 'Commit/start/drain/identity ordering.'
            $caseState.process=[Diagnostics.Process]::GetProcessById($identity.pid)
            $caseState.handle=$caseState.process.SafeHandle
            Assert-Proof ($caseState.process.StartTime.ToUniversalTime().Ticks -eq $identity.start_ticks) 'Exact owned test process retained.'
            $caseState.stdout=$stdoutDrain;$caseState.stderr=$stderrDrain;$caseState.identity=$identity
            $Observation.testOnly=$true
            if($kind -ceq 'uncertain-after-start'){throw 'Owned injected post-start observation failure.'}
            return @{state='image-observed'}
        }
        try{
            foreach($kind in @('shared-success','not-ready','deadline-before-commit','start-throws','uncertain-after-start')){
                $caseBase=Microsoft.PowerShell.Management\Join-Path $testRoot ('c'+$names.Count)
                $prepared=Microsoft.PowerShell.Management\Join-Path $caseBase 'prepared'
                $evidence=Microsoft.PowerShell.Management\Join-Path $caseBase 'evidence'
                $workspaceId=[guid]::NewGuid().ToString('N')
                $caseRoot=Microsoft.PowerShell.Management\Join-Path $prepared ('workspace-'+$workspaceId)
                $control=Microsoft.PowerShell.Management\Join-Path $evidence ('slot-'+$workspaceId)
                $activationId=[guid]::NewGuid().ToString('N');$attemptId=[guid]::NewGuid().ToString('N')
                $output=Microsoft.PowerShell.Management\Join-Path $evidence ('activation-'+$activationId)
                foreach($path in @($caseRoot,$control,$output)){[void][IO.Directory]::CreateDirectory($path)}
                $caseConfiguration=@{preparedId=('1'*32);root=$prepared;evidenceRoot=$evidence;workspaceIds=@($workspaceId);
                    bundle=@{sha256=('2'*64)};bootstrap=@{sha256=('3'*64);bytes=1};
                    inputManifest=@{sha256=('4'*64)};authority=@{revocationPath=(Microsoft.PowerShell.Management\Join-Path $evidence 'revoked');expiresUtc=$null}}
                $caseBasis=@{input=@{installedFiles=@(@{path='proof\PATCH_offline.txt';sha256=('d'*64)},@{path='proof\payload.bin';sha256=('e'*64)})};
                    runtimeLeases=[Collections.Generic.List[object]]::new()}
                $actor=[Diagnostics.ProcessStartInfo]::new($actorPin.path)
                $actor.WorkingDirectory=$caseRoot;$actor.Environment.Clear()
                foreach($entry in @{SystemRoot=$env:SystemRoot;POWERSHELL_TELEMETRY_OPTOUT='1';DOTNET_EnableDiagnostics='0';
                    TEMP=$caseRoot;TMP=$caseRoot;PSModuleAnalysisCachePath=(Microsoft.PowerShell.Management\Join-Path $caseRoot 'module-cache')}.GetEnumerator()){
                    $actor.Environment[$entry.Key]=$entry.Value
                }
                foreach($argument in @('-NoLogo','-NoProfile','-NonInteractive','-Command',
                    "[Console]::Out.Write('owned-out');[Console]::Error.Write('owned-err');Start-Sleep -Milliseconds 600")){
                    $actor.ArgumentList.Add($argument)
                }
                $slot=@{schema='windows-prepared-runner-v1';preparedId=$caseConfiguration.preparedId;workspaceId=$workspaceId;
                    bundleSha256=$caseConfiguration.bundle.sha256;workspace=@{};root=$caseRoot;applicationStart=(ConvertTo-PreparedStart $actor);
                    helper=$caseConfiguration.bootstrap;runtimeAssemblies=@();installedFiles=$caseBasis.input.installedFiles;
                    sourceSha256=('5'*64);protocolSha256=(Get-PinnedProofClosureIdentity);applicationSource=('6'*40)}
                $pin=Write-PreparedJson (Microsoft.PowerShell.Management\Join-Path $control 'runner.json') $slot
                $workspaceManifest=$pin.path;$workspaceManifestSha256=$pin.sha256
                $Configuration='test-only-scoped-configuration';$ConfigurationSha256=('7'*64)
                $self=[Diagnostics.Process]::GetCurrentProcess()
                try{
                    $request=@{activationId=$activationId;attemptId=$attemptId;preparedId=$caseConfiguration.preparedId;
                        parentPid=$self.Id;parentTicks=$self.StartTime.ToUniversalTime().Ticks;
                        entryTicks=[Diagnostics.Stopwatch]::GetTimestamp();outputRoot=$output}
                }finally{$self.Dispose()}
                $null=Write-PreparedJson (Microsoft.PowerShell.Management\Join-Path $control 'active.json') $request
                $caseCommit=Microsoft.PowerShell.Management\Join-Path $control 'committed.json'
                $caseState=@{process=$null;handle=$null;stdout=$null;stderr=$null;identity=$null;workflowCalls=0;readinessCalls=0;workflowFailure=$null}
                $smokeBefore=[BoundedDesktopSmoke]::Calls
                [Environment]::CurrentDirectory=$control
                try{
                    $null=Read-ProofPinnedBytes $actorPin 16777216
                    Invoke-PreparedRunCore -Readiness {
                        $caseState.readinessCalls++
                        if($kind -ceq 'deadline-before-commit'){$request.entryTicks-=[Diagnostics.Stopwatch]::Frequency*6}
                        return @{Ready=($kind -cne 'not-ready')}
                    } -Workflow {
                        $caseState.workflowCalls++
                        Assert-Proof ([IO.File]::Exists($caseCommit)) 'Commit must precede real shared Process.Start.'
                        if($kind -ceq 'start-throws'){$start.WorkingDirectory=Microsoft.PowerShell.Management\Join-Path $caseRoot 'absent-cwd'}
                        try{Invoke-ProofAppWorkflow}catch{$caseState.workflowFailure=$_.Exception.GetBaseException().Message;throw}
                    }
                    Assert-Proof ($caseState.readinessCalls -eq 1) 'Exactly one injected readiness sample.'
                    $committed=[IO.File]::Exists($caseCommit)
                    if($kind -cin @('not-ready','deadline-before-commit')){
                        Assert-Proof (!$committed -and $caseState.workflowCalls -eq 0 -and
                            ![IO.File]::Exists((Microsoft.PowerShell.Management\Join-Path $caseRoot 'app-identity.json'))) 'No start before readiness/deadline admission.'
                    }else{
                        Assert-Proof ($committed -and $caseState.workflowCalls -eq 1) 'One committed shared invocation.'
                        $result=Read-ProofJsonFile (Microsoft.PowerShell.Management\Join-Path $caseRoot 'result.json')
                        Assert-Proof ($result.run_id -ceq $attemptId -and $result.input_manifest_sha256 -ceq ('4'*64) -and
                            $result.source_manifest_sha256 -ceq ('5'*64)) 'Actual Core terminal composition.'
                        if($kind -ceq 'shared-success'){
                            Assert-Proof ($result.passed -and $result.gui.SimulationOnly -and [BoundedDesktopSmoke]::Calls -eq $smokeBefore+1) ('Simulated GUI only after real shared startup: '+$caseState.workflowFailure)
                            $stop=Read-ProofJsonFile (Microsoft.PowerShell.Management\Join-Path $caseRoot 'worker-stop.json')
                            Assert-Proof ($stop.app_pid -eq $caseState.identity.pid -and $stop.runner_pid -eq $PID -and
                                $stop.run_id -ceq $attemptId -and $stop.dispatch_admission_closed -and !$stop.worker_joined) 'Real shared stop callback locals/functions.'
                        }else{
                            Assert-Proof (!$result.passed -and $null -ne $result.failure_type -and
                                [BoundedDesktopSmoke]::Calls -eq $smokeBefore) 'Failure is not GUI success.'
                        }
                        if($kind -ceq 'start-throws'){Assert-Proof ($null -eq $caseState.identity) 'Throwing Start has no identity/EOF tasks.'}
                        else{Assert-Proof ($caseState.stdout.Wait(2000) -and $caseState.stderr.Wait(2000)) 'Actual shared stdout/stderr EOF.'}
                    }
                    $names.Add($kind)
                }finally{
                    if($caseState.process){
                        try{if(!$caseState.process.WaitForExit(3000)){$caseState.process.Kill();Assert-Proof ($caseState.process.WaitForExit(3000)) 'Owned test cleanup.'}}
                        finally{$caseState.process.Dispose()}
                    }
                }
            }
            foreach($field in @('readiness','workflow','targetImage')){
                $bad=New-PinnedProofBinding PreparedRun @{
                    configuration=(Microsoft.PowerShell.Management\Join-Path $testRoot 'config.json');configurationSha256=('1'*64);
                    workspaceId=('2'*32);workspaceManifest=(Microsoft.PowerShell.Management\Join-Path $testRoot 'runner.json');workspaceManifestSha256=('3'*64)}
                $bad[$field]='test-override'
                $refused=$false;try{$null=Confirm-PinnedProofBinding $bad PreparedRun}catch{$refused=$true}
                Assert-Proof $refused 'Production bindings must reject test substitutions.';$names.Add('reject-'+$field)
            }
            Assert-Proof ($names.Count -eq 8 -and [BoundedProcessImage]::Reads -eq 2) 'Shared-start case inventory.'
            $allPassed=$true
            @{schema='prepared-shared-start-tests-v1';passed=$true;names=$names;realSharedStartCalls=3;
                processesStarted=2;simulatedGuiOnly=$true;nativeReadiness=$false;nativeWindows=$false}|ConvertTo-Json -Compress
        }finally{
            [Environment]::CurrentDirectory=$originalDirectory
            if($allPassed){[IO.Directory]::Delete($testRoot,$true)}
        }
    }
    function Invoke-PreparedInertEntry {
        param([string]$Configuration,[string]$ConfigurationSha256,[string]$workspaceId,[string]$workspaceManifest,
            [string]$workspaceManifestSha256,[string]$testManifest,[string]$testManifestSha256)
        . (Get-PinnedProofLibrary -Library Configuration)
        . (Get-PinnedProofLibrary -Library Prepared)
        $test=Read-ProofPinnedJson @{path=$testManifest;bytes=([IO.FileInfo]$testManifest).Length;sha256=$testManifestSha256}
        Assert-ProofKeys $test @('schema','ready','host')
        Assert-Proof ($test.schema -ceq 'prepared-inert-actor-v1' -and $test.ready -is [bool] -and
            $test.host.path -ceq [Environment]::ProcessPath) 'Fixed inert actor.'
        Invoke-PreparedRunCore -Readiness { @{Ready=$test.ready} } -Workflow {
            $null=Read-ProofPinnedBytes $test.host 16777216
            $actor=[Diagnostics.ProcessStartInfo]::new($test.host.path)
            $actor.UseShellExecute=$false;$actor.CreateNoWindow=$true
            $actor.RedirectStandardOutput=$true;$actor.RedirectStandardError=$true
            $actor.ArgumentList.Add('--version')
            $actor.Environment.Clear()
            $actor.Environment['SystemRoot']=$basis.base.machine.systemRoot
            $actor.Environment['POWERSHELL_TELEMETRY_OPTOUT']='1'
            $actor.Environment['DOTNET_EnableDiagnostics']='0'
            $before=[Diagnostics.Stopwatch]::GetTimestamp()
            $process=[Diagnostics.Process]::Start($actor)
            try{
                $handle=$process.SafeHandle
                $out=$process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null)
                $err=$process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
                if(!$process.WaitForExit(5000)){$process.Kill();[void]$process.WaitForExit(5000);throw 'Inert actor timeout.'}
                Assert-Proof ($out.Wait(1000) -and $err.Wait(1000) -and $process.ExitCode -eq 0) 'Inert actor completion.'
                $null=Write-PreparedJson (Join-Path $request.outputRoot 'inert-start.json') @{
                    activationMs=($before-$request.entryTicks)*1000.0/[Diagnostics.Stopwatch]::Frequency;
                    startTicks=$before;pid=$process.Id;exitCode=$process.ExitCode;nativeReadiness=$false;gui=$false}
            }finally{$process.Dispose()}
        }
    }
    function Invoke-PreparedColdEntry {
        param([string]$testManifest,[string]$testManifestSha256)
        trap { Write-Error ($_.Exception.Message+"`n"+$_.ScriptStackTrace); break }
        . (Get-PinnedProofLibrary -Library Configuration)
        . (Get-PinnedProofLibrary -Library Prepared)
        . (Get-PinnedProofLibrary -Library Launch)
        . (Join-Path $PSScriptRoot 'ProcessImage.ps1')
        $test=Read-ProofPinnedJson @{path=$testManifest;bytes=([IO.FileInfo]$testManifest).Length;sha256=$testManifestSha256}
        if($test.stage -ceq 'Activate'){
            Assert-ProofKeys $test @('schema','stage','configuration','activationId')
            Assert-Proof ($test.schema -ceq 'prepared-cold-test-v1') 'Cold test schema.'
            $Configuration=$test.configuration.path;$ConfigurationSha256=$test.configuration.sha256
            $activationId=$test.activationId;$userTurn='inert-test-not-gui-authority'
            Invoke-PreparedActivationCore
            return
        }
        Assert-ProofKeys $test @('schema','stage','root','inputManifest','inputsReceipt','slots')
        Assert-Proof ($test.schema -ceq 'prepared-cold-test-v1' -and $test.stage -ceq 'Prepare' -and
            ($test.slots -is [int] -or $test.slots -is [long]) -and $test.slots -ge 2 -and $test.slots -le 8) 'Cold preparation test.'
        $historical=Read-ProofPinnedJson $test.inputsReceipt
        Assert-Proof ($historical.passed -is [bool] -and $historical.passed -and
            $historical.inputManifestSha256 -ceq $test.inputManifest.sha256) 'Historical donor input pin.'
        $input=Read-ProofPinnedJson $test.inputManifest
        foreach($row in $input.appFiles){Assert-ProofPublicResource $row.path}
        New-PreparedDirectory $test.root
        $root=$test.root;$bundleRoot=Join-Path $root 'prepared';$evidence=Join-Path $root 'evidence'
        New-PreparedDirectory $bundleRoot;New-PreparedDirectory $evidence
        $legacyFiles=@(Get-PinnedProofFiles|Where-Object {$_.StartsWith('OfflinePatchImportProof\',[StringComparison]::Ordinal)})
        $sourcePin=Write-PreparedJson (Join-Path $root 'source.json') @{files=@(foreach($relative in $legacyFiles){
            $name=$relative.Substring('OfflinePatchImportProof\'.Length);$pin=Get-PreparedPin (Join-Path $PSScriptRoot $name)
            @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
        })}
        $hostPin=Get-PreparedPin ([Environment]::ProcessPath)
        $basePin=Write-PreparedJson (Join-Path $root 'base.json') @{
            schema='offline-patch-import-proof-v1';outputRoot=$bundleRoot;evidenceRoot=$evidence;applicationSource=$input.applicationSource;
            host=$hostPin;sourceManifest=$sourcePin;approval=@{sourceGateReference='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-1';
                authorizationReference='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-2';authorizationSha256=('1'*64)};
            machine=@{systemRoot=$env:SystemRoot;dotnetRoot=$PSHOME;gitPath=$hostPin.path;programFiles=$env:ProgramFiles;programFilesX86=${env:ProgramFiles(x86)}};
            preparation=$null;restage=$null}
        $receipts=@{}
        foreach($stage in @('Build','Validate','Inputs')){
            $receipts[$stage]=Write-PreparedJson (Join-Path $root ($stage+'.synthetic.json')) @{
                stage=$stage;passed=$true;runId=$input.runId;sourceManifestSha256=$sourcePin.sha256;
                helperSourceManifestSha256=$sourcePin.sha256;inputManifestSha256=$test.inputManifest.sha256}
        }
        $id=[guid]::NewGuid().ToString('N')
        $c=@{schema='windows-prepared-configuration-v1';preparedId=$id;applicationTree=('0'*40);
            baseConfiguration=$basePin;inputManifest=$test.inputManifest;receipts=$receipts;root=$bundleRoot;evidenceRoot=$evidence;
            workspaceIds=@(1..$test.slots|ForEach-Object {[guid]::NewGuid().ToString('N')});bundle=$null;bootstrap=$null;
            jsonReference=(Get-PreparedPin (Join-Path $PSHOME 'ref\System.Text.Json.dll'));slots=@();
            authority=@{reference='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-2';sha256=('1'*64);
                scenario='offline-patch-import-v1';expiresUtc=$null;revocationPath=(Join-Path $evidence ($id+'.revoked'));replenishmentAllowed=$true}}
        $pin=Write-PreparedJson (Join-Path $root 'prepare-bundle.json') $c
        $c.bundle=Invoke-PrepareBundleEntry $pin.path $pin.sha256|ConvertFrom-Json -AsHashtable
        $c.bootstrap=(Read-ProofPinnedJson $c.bundle).helper
        $pin=Write-PreparedJson (Join-Path $root 'prepare-workspaces.json') $c
        $actorPin=Write-PreparedJson (Join-Path $root 'actor.json') @{schema='prepared-inert-actor-v1';ready=$true;host=$hostPin}
        foreach($workspaceId in $c.workspaceIds){
            $slot=Invoke-PrepareWorkspaceEntry $pin.path $pin.sha256 $workspaceId|ConvertFrom-Json -AsHashtable
            $control=Join-Path $evidence ('slot-'+$workspaceId)
            $args=New-PinnedProofChildArguments -ChildMode PreparedInertChild -Path (Join-Path $control 'inert-bindings.json') -Values @{
                configuration=$pin.path;configurationSha256=$pin.sha256;workspaceId=$workspaceId;
                workspaceManifest=$slot.runner.path;workspaceManifestSha256=$slot.runner.sha256;
                testManifest=$actorPin.path;testManifestSha256=$actorPin.sha256}
            $launch=Read-ProofPinnedJson $slot.launch
            $launch.arguments=$args
            $slot.launch=Write-PreparedJson (Join-Path $control 'inert-launch.json') $launch
            $c.slots+=,$slot
        }
        $final=Write-PreparedJson (Join-Path $root 'activate.json') $c
        $blockedActor=Write-PreparedJson (Join-Path $root 'blocked-actor.json') @{schema='prepared-inert-actor-v1';ready=$false;host=$hostPin}
        $first=$c.slots[0];$control=Join-Path $evidence ('slot-'+$first.workspaceId)
        $blockedArgs=New-PinnedProofChildArguments -ChildMode PreparedInertChild -Path (Join-Path $control 'blocked-bindings.json') -Values @{
            configuration=$pin.path;configurationSha256=$pin.sha256;workspaceId=$first.workspaceId;
            workspaceManifest=$first.runner.path;workspaceManifestSha256=$first.runner.sha256;
            testManifest=$blockedActor.path;testManifestSha256=$blockedActor.sha256}
        $blockedLaunch=Read-ProofPinnedJson $first.launch;$blockedLaunch.arguments=$blockedArgs
        $blockedLaunchPin=Write-PreparedJson (Join-Path $control 'blocked-launch.json') $blockedLaunch
        $blocked=@{}+$c
        $blocked.slots=@(@{workspaceId=$first.workspaceId;manifest=$first.manifest;runner=$first.runner;launch=$blockedLaunchPin})
        $blockedPin=Write-PreparedJson (Join-Path $root 'blocked-configuration.json') $blocked
        $blockedTest=Write-PreparedJson (Join-Path $root 'blocked.json') @{
            schema='prepared-cold-test-v1';stage='Activate';configuration=$blockedPin;activationId=[guid]::NewGuid().ToString('N')}
        $tests=@(foreach($number in 1..5){
            Write-PreparedJson (Join-Path $root ('cold-'+$number+'.json')) @{
                schema='prepared-cold-test-v1';stage='Activate';configuration=$final;activationId=[guid]::NewGuid().ToString('N')}
        })
        @{configuration=$final;blocked=$blockedTest;tests=$tests;payloadFiles=$input.appFiles.Count;payloadBytes=($input.appFiles|Measure-Object bytes -Sum).Sum}|ConvertTo-Json -Depth 8 -Compress
    }
    function Invoke-PreparedTestsEntry {
        . (Get-PinnedProofLibrary -Library Configuration)
        . (Get-PinnedProofLibrary -Library Prepared)
        $root=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) ('TestResults\prepared-pure-'+[guid]::NewGuid().ToString('N'))
        [void][IO.Directory]::CreateDirectory($root)
        try{
            Add-Type -Path @((Join-Path $PSScriptRoot 'PreparedLaunch.cs'),(Join-Path $PSScriptRoot 'PreparedLaunch.Tests.cs')) -ErrorAction Stop -WarningAction Stop
            $cases=[PreparedLaunchTests]::Run($root)
            Assert-Proof ($cases -eq $(if($IsWindows){25}else{24})) 'Prepared case inventory.'
            $jsonCases=[PreparedLaunchTests]::RunJson()
            Assert-Proof ($jsonCases -eq 12) 'Prepared bounded JSON inventory.'
            $manifestCases=[PreparedLaunchTests]::RunManifest()
            Assert-Proof ($manifestCases -eq 9) 'Prepared source-to-workspace manifest inventory.'
            $privacy=0
            foreach($path in @('config\log\log.txt','generated-core-suite-log-preserved.txt','private.gba','roms\game.ROM')){
                $refused=$false
                try{Assert-ProofPublicResource $path}catch{$refused=$true}
                Assert-Proof $refused 'Privacy admission before synthetic content access.';$privacy++
            }
            $tokens=$null;$errors=$null
            $prepareAst=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'prepare.ps1'),[ref]$tokens,[ref]$errors)
            Assert-Proof ($errors.Count -eq 0) 'Preparation parser.'
            foreach($name in @('Tree','Row','Hash')){
                $node=@($prepareAst.FindAll({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name},$true))
                Assert-Proof ($node.Count -eq 1) 'Exact production privacy seam.'
                . ([scriptblock]::Create($node[0].Extent.Text))
            }
            function Budget {}
            function Plain([string]$p){Assert-ProofPath $p -Existing}
            function Excluded([string]$p){return $false}
            $D='unused';$Id='unused';$B='unused'
            foreach($relative in @('log\log.txt','generated-core-suite-log-preserved.txt','game.gba','nested\private.rom')){
                $W=Join-Path $root ('canary-'+$privacy)
                $config=Join-Path $W 'config';$canary=Join-Path $config $relative
                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($canary))
                [IO.File]::WriteAllText($canary,'owned synthetic canary')
                $held=[IO.File]::Open($canary,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
                try{
                    $failure=$null
                    try{Tree $config $true|Out-Null}catch{$failure=$_.Exception.Message}
                    Assert-Proof ($failure -ceq 'Private resource refused before content access.') 'Actual Tree guard must run before opening the locked canary.'
                }finally{$held.Dispose()}
                $privacy++
            }
            Assert-Proof ($privacy -eq 8) 'Prepared privacy/canary inventory.'
            $stateNames=[Collections.Generic.List[string]]::new()
            $model=@{evidenceRoot=$root;slots=@(@{workspaceId=('a'*32)},@{workspaceId=('b'*32)});
                authority=@{revocationPath=(Join-Path $root 'revoked');expiresUtc=$null}}
            foreach($slot in $model.slots){[void][IO.Directory]::CreateDirectory((Join-Path $root ('slot-'+$slot.workspaceId)))}
            Assert-PreparedAuthority $model;$stateNames.Add('unexpired-unrevoked')
            $model.authority.expiresUtc=[DateTime]::UtcNow.AddSeconds(-1)
            $refused=$false;try{Assert-PreparedAuthority $model}catch{$refused=$_.Exception.Message -ceq 'prepared-expired'}
            Assert-Proof $refused 'Expired authority.';$stateNames.Add('expired')
            $model.authority.expiresUtc=$null
            [IO.File]::WriteAllText($model.authority.revocationPath,'revoked')
            $refused=$false;try{Assert-PreparedAuthority $model}catch{$refused=$_.Exception.Message -ceq 'prepared-revoked'}
            Assert-Proof $refused 'Revoked authority.';$stateNames.Add('revoked')
            Assert-Proof ((Select-PreparedWorkspace $model).workspaceId -ceq ('a'*32)) 'First pristine slot.';$stateNames.Add('first-pristine')
            $active=Join-Path $root ('slot-'+('a'*32)+'\active.json')
            [IO.File]::WriteAllText($active,'stale')
            Assert-Proof ((Select-PreparedWorkspace $model).workspaceId -ceq ('b'*32)) 'Stale slot stays reserved.';$stateNames.Add('stale-not-reclaimed')
            $committed=Join-Path $root ('slot-'+('b'*32)+'\committed.json')
            [IO.File]::WriteAllText($committed,'consumed')
            Assert-Proof ($null -eq (Select-PreparedWorkspace $model)) 'Exhausted capacity.';$stateNames.Add('capacity-exhausted')
            [IO.File]::Delete($active)
            Assert-Proof ((Select-PreparedWorkspace $model).workspaceId -ceq ('a'*32)) 'Released no-start reservation.';$stateNames.Add('readiness-release-reusable')
            $lockPath=Join-Path $root 'owned.lock'
            $held=[IO.File]::Open($lockPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
            try{
                $refused=$false
                try{$second=[IO.File]::Open($lockPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None);$second.Dispose()}catch{$refused=$true}
                Assert-Proof $refused 'Concurrent activation.';$stateNames.Add('exclusive-reservation')
            }finally{$held.Dispose()}
            Assert-Proof ($stateNames.Count -eq 8) 'Prepared authority/selection inventory.'
            $bindingCases=0
            $values=@{configuration=(Join-Path $root 'configuration.json');configurationSha256=('1'*64);
                workspaceId=('a'*32);workspaceManifest=(Join-Path $root 'runner.json');workspaceManifestSha256=('2'*64);
                activationId=('b'*32);userTurn='explicit-test-turn';testManifest=(Join-Path $root 'test.json');testManifestSha256=('3'*64)}
            foreach($mode in @('PrepareBundle','PrepareWorkspace','ActivatePrepared','PreparedRun','PreparedPure','PreparedCold','PreparedInertChild')){
                $data=@{}
                foreach($key in (Get-PinnedProofDispatch $mode).required){$data[$key]=$values[$key]}
                $binding=New-PinnedProofBinding $mode $data
                $null=Confirm-PinnedProofBinding $binding $mode;$bindingCases++
                foreach($key in @($binding.Keys|Where-Object {$_ -cnotin @('schema','mode')})){
                    $bad=@{}+$binding
                    $bad[$key]=if($null -eq $bad[$key]){'unexpected'}else{$null}
                    $refused=$false;try{$null=Confirm-PinnedProofBinding $bad $mode}catch{$refused=$true}
                    Assert-Proof $refused 'Prepared binding required/unused fields.';$bindingCases++
                }
                foreach($kind in @('legacy-schema','unknown-field')){
                    $bad=@{}+$binding
                    if($kind -ceq 'legacy-schema'){$bad.schema='pinned-proof-bindings-v2'}else{$bad.unexpected=$true}
                    $refused=$false;try{$null=Confirm-PinnedProofBinding $bad $mode}catch{$refused=$true}
                    Assert-Proof $refused 'Prepared schema/field refusal.';$bindingCases++
                }
            }
            Assert-Proof ($bindingCases -eq 74) 'Prepared binding case inventory.'
            $clockCases=0
            $originalEntryTicks=[Diagnostics.Stopwatch]::GetTimestamp()
            Start-Sleep -Milliseconds 10
            $handoff=New-PreparedActivationRequest ('a'*32) ('b'*32) $PID 1 (Join-Path $root 'clock-output')
            Assert-Proof ($handoff.entryTicks -gt $originalEntryTicks -and
                $handoff.request.entryTicks -eq $handoff.entryTicks) 'Fresh post-content request clock.';$clockCases++
            $clockActive=Join-Path $root 'clock-active.json'
            $null=Write-PreparedJson $clockActive $handoff.request
            $clockRequest=Read-ProofJsonFile $clockActive
            Assert-Proof ($clockRequest.entryTicks -eq $handoff.entryTicks -and
                $clockRequest.entryTicks -ne $originalEntryTicks) 'Active request carries the post-content clock.';$clockCases++
            $lateRefused=$false
            try{[PreparedContentLease]::Deadline($handoff.entryTicks-[Diagnostics.Stopwatch]::Frequency*6)}
            catch{$lateRefused=$_.Exception.GetBaseException().Message -ceq 'prepared-activation-deadline'}
            Assert-Proof $lateRefused 'Post-content admission remains five seconds.';$clockCases++
            $failedRoot=Join-Path $root 'clock-failed-content'
            [void][IO.Directory]::CreateDirectory($failedRoot)
            $failedActive=Join-Path $failedRoot 'active.json'
            $failedOpen=$false
            try{$unexpected=[PreparedContentLease]::Open($failedRoot,@{files=@(@{path='missing.bin';bytes=1;sha256=('f'*64)});directories=@()},
                    [Diagnostics.Stopwatch]::GetTimestamp());$unexpected.Dispose()}
            catch{$failedOpen=$true}
            Assert-Proof ($failedOpen -and ![IO.File]::Exists($failedActive)) 'Failed content admission creates no active request.';$clockCases++
            Assert-Proof ($clockCases -eq 4) 'Prepared clock case inventory.'
            $target=Join-Path $root 'owned-link-target';[void][IO.Directory]::CreateDirectory((Join-Path $target 'child'))
            $link=Join-Path $root 'owned-link'
            if($IsWindows){$null=New-Item -ItemType Junction -Path $link -Target $target -ErrorAction Stop}
            else{$null=[IO.Directory]::CreateSymbolicLink($link,$target)}
            $reparseCases=0
            try{
                foreach($path in @($link,(Join-Path $link 'child'))){
                    $refused=$false
                    try{$unexpected=[PreparedContentLease]::Open($path,@{files=@();directories=@()},[Diagnostics.Stopwatch]::GetTimestamp());$unexpected.Dispose()}
                    catch{$refused=$_.Exception.GetBaseException().Message -ceq 'prepared-reparse'}
                    Assert-Proof $refused 'Prepared root/ancestry reparse refusal.';$reparseCases++
                }
            }finally{[IO.Directory]::Delete($link)}
            $sharedCases=0
            if($IsWindows){
                $sharedArguments=New-PinnedProofChildArguments -ChildMode PreparedSharedStart `
                    -Path (Join-Path $root 'shared-start-bindings.json') -Values @{}
                $info=[Diagnostics.ProcessStartInfo]::new([Environment]::ProcessPath)
                $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WorkingDirectory=$root
                $info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
                foreach($argument in $sharedArguments){$info.ArgumentList.Add($argument)}
                $info.Environment['TEMP']=$root;$info.Environment['TMP']=$root
                $info.Environment['PSModuleAnalysisCachePath']=Join-Path $root 'shared-module-cache'
                $info.Environment['POWERSHELL_TELEMETRY_OPTOUT']='1'
                $child=[Diagnostics.Process]::Start($info)
                try{
                    $retained=$child.SafeHandle
                    $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
                    if(!$child.WaitForExit(60000)){$child.Kill();[void]$child.WaitForExit(5000);throw 'Shared-start test host timeout.'}
                    Assert-Proof ($stdout.Wait(1000) -and $stderr.Wait(1000) -and $child.ExitCode -eq 0) ('Shared-start host failed: '+$stderr.Result)
                    $shared=ConvertFrom-ProofJson $stdout.Result
                    Assert-Proof ($shared.passed -is [bool] -and $shared.passed -and $shared.names.Count -eq 8 -and
                        $shared.realSharedStartCalls -eq 3 -and $shared.processesStarted -eq 2 -and
                        $shared.simulatedGuiOnly -and !$shared.nativeReadiness -and !$shared.nativeWindows) 'Shared-start integration evidence.'
                    $sharedCases=$shared.names.Count
                }finally{$child.Dispose()}
            }
            @{schema='windows-prepared-tests-v1';cases=$cases;jsonCases=$jsonCases;privacyCases=$privacy;stateCases=$stateNames;
                reparseCases=$reparseCases;bindingCases=$bindingCases;manifestCases=$manifestCases;
                clockCases=$clockCases;sharedStartCases=$sharedCases;passed=$true;nativeReadiness=$false;appLaunched=$false}|ConvertTo-Json -Compress
        }finally{[IO.Directory]::Delete($root,$true)}
    }
}
