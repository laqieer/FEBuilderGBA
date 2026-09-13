function Assert-ProofTestThrows([scriptblock]$Action) {
    $failed=$false
    try { & $Action | Out-Null } catch {
        if($_.Exception -is [Management.Automation.CommandNotFoundException]){throw}
        $failed=$true
    }
    Assert-Proof $failed 'Expected refusal.'
}
function Invoke-ProofBooleanTests {
    . (Join-Path $PSScriptRoot 'Configuration.ps1')
    Assert-ProofBooleanFields @{yes=$true;no=$false} @('yes') @('no')
    $cases=1
    foreach($value in @('false','true',0,1,$null,@())){
        foreach($positive in @($true,$false)){
            $errorMessage=$null
            try{
                if($positive){Assert-ProofBooleanFields @{yes=$value;no=$false} @('yes') @('no')}
                else{Assert-ProofBooleanFields @{yes=$true;no=$value} @('yes') @('no')}
            }catch{$errorMessage=$_.Exception.Message}
            Assert-Proof ($errorMessage -cmatch '^Required (true|false) Boolean: (yes|no)$') 'Wrong Boolean refusal or missing implementation.'
        }
        $cases++
    }
    return $cases
}
function Write-ProofTestBytes([string]$Path,[byte[]]$Bytes) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {$stream.Write($Bytes);$stream.Flush($true)}finally{$stream.Dispose()}
    return @{path=$Path;bytes=$Bytes.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()}
}
function Write-ProofTestJson([string]$Path,$Value) {
    Write-ProofTestBytes $Path ([Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 32 -Compress)))
}
function New-ProofRestageFixture([string]$Root,[ValidateSet('None','PurePassed','CompilePassed','BindingAccepted','OuterSource')][string]$InvalidType='None') {
    $prior='1'*32;$head='2'*40;$tree='3'*40
    $b=Join-Path $Root 'build';$d=Join-Path $Root 'desktop';$r=Join-Path $Root 'history'
    $donor=Join-Path $d ('inputs-'+$prior)
    $evidence=Join-Path $Root 'evidence';$output=Join-Path $Root 'output'
    foreach($path in @($b,$d,$r,$donor,$evidence,$output)){[void][IO.Directory]::CreateDirectory($path)}
    $hostPin=Write-ProofTestBytes (Join-Path $Root 'host\pwsh.exe') ([Text.Encoding]::UTF8.GetBytes('synthetic data; never executable'))
    $apps=@(for($i=0;$i -lt 594;$i++){
        $name="file-$i.dat";$pin=Write-ProofTestBytes (Join-Path $donor ('app\'+$name)) ([byte[]]@($i%256))
        @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
    })
    $literal='<root><item><key>Language</key><value>en</value></item><item><key>func_auto_update</key><value>0</value></item></root>'
    $configPin=Write-ProofTestBytes (Join-Path $donor 'app\config\config.xml') ([Text.Encoding]::UTF8.GetBytes($literal))
    $configRow=@{path='config\config.xml';bytes=$configPin.bytes;sha256=$configPin.sha256}
    $runtime=@(for($i=0;$i -lt 7;$i++){
        $name="runtime-$i.dat";$pin=Write-ProofTestBytes (Join-Path $donor ('support\'+$name)) ([byte[]]@(7))
        @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
    })
    $runtimePins=@(foreach($row in $runtime){@{path=(Join-Path ([IO.Path]::GetDirectoryName($hostPin.path)) $row.path);bytes=$row.bytes;sha256=$row.sha256}})
    $fixtures=@(foreach($name in @('zipdb-invalid.zip','zipdb-proof.gba','zipdb-valid.zip')){
        $pin=Write-ProofTestBytes (Join-Path $donor ("desktop-proof-$prior\"+$name)) ([Text.Encoding]::UTF8.GetBytes('synthetic bytes; not a ROM/archive'))
        @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
    })
    $refs=@(for($i=0;$i -lt 26;$i++){@{path=(Join-Path $Root ("host\ref-$i.dat"));bytes=1;sha256=('4'*64)}})
    $manifest=[ordered]@{schema='windows-desktop-bounded-input-v1';applicationSource=$head;runId=$prior;syntheticRomFormat='fe8u-synthetic-huffman-v1';
        powershellHost=@{path=$hostPin.path;sha256=$hostPin.sha256};appFiles=@($apps)+@($configRow);fixtures=$fixtures;
        runtimeAssemblies=$runtime;compileReferences=$refs;installedFiles=@(
            @{path='proof\PATCH_offline.txt';bytes=1;sha256=('5'*64)},@{path='proof\payload.bin';bytes=1;sha256=('6'*64)})}
    $manifestPin=Write-ProofTestJson (Join-Path $donor 'input-manifest.json') $manifest
    $metadata=@{tools=@(1..13|ForEach-Object {$hostPin});runtimeAssemblies=$runtimePins;compileReferences=$refs;assets=@(1..15|ForEach-Object {$configRow})}
    $metadataPin=Write-ProofTestJson (Join-Path $b 'metadata.json') $metadata
    $historyEvidence=@{inputManifest=$manifestPin;metadata=$metadataPin}
    foreach($source in @(@{name='bSource';root=$b;count=7},@{name='dSource';root=$d;count=10})){
        $rows=@(for($i=0;$i -lt $source.count;$i++){
            $name="source-$i.txt";$pin=Write-ProofTestBytes (Join-Path $source.root $name) ([byte[]]@(1))
            @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
        })
        $value=@{files=$rows}
        if($source.name -ceq 'dSource'){$value.externalSourceDependencies=@($hostPin,$hostPin,$hostPin)}
        $historyEvidence[$source.name]=Write-ProofTestJson (Join-Path $source.root 'source-manifest.json') $value
    }
    $preflightRow=@{path=$hostPin.path;bytes=$hostPin.bytes;sha256=$hostPin.sha256;nonReparseAncestry=$true}
    $historyEvidence.preflight=Write-ProofTestJson (Join-Path $r 'preflight.json') @{runId=$prior;passed=$true;rows=@(1..99|ForEach-Object {$preflightRow});installedPins=46}
    $trx='<?xml version="1.0" encoding="utf-8"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary><Counters total="4" executed="4" passed="4" failed="0" /></ResultSummary></TestRun>'
    $tests=@(foreach($configuration in @('Debug','Release')){
        $name=$configuration+'.trx';$pin=Write-ProofTestBytes (Join-Path $b ('build-attempt\'+$name)) ([Text.Encoding]::UTF8.GetBytes(([string][char]0xfeff)+$trx))
        @{configuration=$configuration;passedCases=4;trx=@{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}}
    })
    $outputs=@{applicationSource=$head;worktreeHead=$head;appFiles=@($apps)+@(
        @{path='excluded-a.dat';bytes=0;sha256=('0'*64)},@{path='excluded-b.dat';bytes=0;sha256=('0'*64)});
        generatorFiles=@(1..8|ForEach-Object {$configRow});projectionFiles=$apps;tests=$tests}
    $historyEvidence.outputs=Write-ProofTestJson (Join-Path $b 'outputs.json') $outputs
    $validationRoot=Join-Path $b ('validate-'+$prior)
    $validationRows=@(foreach($name in @('Pure.json','Compile.json','Policy.compile-only.dll','Desktop.compile-only.dll','a.dat','b.dat','c.dat','d.dat','e.dat','f.dat','g.dat')){
        $value=switch($name){
            'Pure.json' {@{passed=$true;cases=522;bindingCases=22;nativeCalls=$false;appLaunched=$false}}
            'Compile.json' {@{passed=$true;nativeCalls=$false;appLaunched=$false;runtimeBindings=@(1..7|ForEach-Object {@{accepted=$true;actualSha256=('6'*64);expectedSha256=('6'*64);actualIdentity='synthetic';expectedIdentity='synthetic'}})}}
            default {@{synthetic=$true}}
        }
        if($name -ceq 'Pure.json' -and $InvalidType -ceq 'PurePassed'){$value.passed='false'}
        if($name -ceq 'Compile.json' -and $InvalidType -ceq 'CompilePassed'){$value.passed='false'}
        if($name -ceq 'Compile.json' -and $InvalidType -ceq 'BindingAccepted'){$value.runtimeBindings[0].accepted='false'}
        $pin=Write-ProofTestJson (Join-Path $validationRoot $name) $value
        @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
    })
    $gate='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-1'
    $grant='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-2'
    $stages=[Collections.Generic.List[object]]::new();$configuredReceipts=[Collections.Generic.List[object]]::new()
    foreach($stage in @('Validate','Build','Inputs')){
        $binding=@{stage=$stage;runId=$prior;sourceManifestSha256=$historyEvidence.bSource.sha256;helperSourceManifestSha256=$historyEvidence.dSource.sha256;
            toolMetadataSha256=$metadataPin.sha256;sourceGateReference=$gate;authorizationReference=$grant;head=$head;tree=$tree}
        $receipt=$binding.Clone();$receipt.Remove('head');$receipt.Remove('tree')
        $receipt.schema='windows-desktop-preparation-receipt-v1';$receipt.passed=$true;$receipt.failure=$null
        $receipt.before=[ordered]@{head=$head;tree=$tree;clean=$true};$receipt.after=[ordered]@{head=$head;tree=$tree;clean=$true}
        $receipt.processes=@(1..(@{Validate=28;Build=32;Inputs=27}[$stage])|ForEach-Object {
            @{passed=$true;exitCode=0;timedOut=$false;pid=1;startTicks=1L;exe=$hostPin.path;imageObservation=@{path=$hostPin.path}}
        })
        if($stage -ceq 'Validate'){$receipt.processImageCases=10;$receipt.validationFiles=$validationRows}
        if($stage -ceq 'Build'){$receipt.outputManifestSha256=$historyEvidence.outputs.sha256}
        if($stage -ceq 'Inputs'){
            $provenanceRows=@(foreach($row in $apps){$copy=$row.Clone();$copy.source=Join-Path $b ('publish\'+$row.path);$copy})
            $configProof=$configRow.Clone();$configProof.source='reviewed prepare.ps1 literal; fresh owned projection only'
            $historyEvidence.projection=Write-ProofTestJson (Join-Path $r 'projection.json') @{
                buildReceiptSha256=$historyEvidence.build.sha256;validationReceiptSha256=$historyEvidence.validate.sha256;
                sourceManifestSha256=$historyEvidence.bSource.sha256;helperSourceManifestSha256=$historyEvidence.dSource.sha256;
                worktreeHead=$head;applicationSource=$head;files=@($provenanceRows)+@($configProof)}
            $receipt.inputManifestSha256=$manifestPin.sha256;$receipt.projectionProvenanceSha256=$historyEvidence.projection.sha256
        }
        $label=$stage.ToLowerInvariant()
        $historyEvidence[$label]=Write-ProofTestJson (Join-Path $r ($label+'.json')) $receipt
        $binding.pin=$historyEvidence[$label];$configuredReceipts.Add($binding)
        $auth=Write-ProofTestBytes (Join-Path $r ($label+'-authorization.txt')) ([Text.Encoding]::UTF8.GetBytes('Synthetic caller approval metadata. Not an authorization.'))
        $grantPin=Write-ProofTestJson (Join-Path $r ($label+'-grant.json')) @{
            stage=$stage;runId=$prior;authorizationReference=$grant;authorizationSha256=$auth.sha256;sourceGateReference=$gate}
        $outer=$receipt.Clone();$outer.Remove('before');$outer.Remove('after');$outer.Remove('processes')
        foreach($key in @('exitConfirmed','outputConfirmed','environmentCleared','stdinClosed','drainingBeforeImageObservation','sourceFreezesReverified')){$outer[$key]=$true}
        if($InvalidType -ceq 'OuterSource'){$outer.sourceFreezesReverified='false'}
        $outer.timedOut=$false;$outer.killAttempts=0;$outer.exitCode=0;$outer.exe=$hostPin.path;$outer.image=$hostPin.path
        $outer.hostSha256=$hostPin.sha256;$outer.pid=1;$outer.startTicks=1L;$outer.stageReceiptPath=$historyEvidence[$label].path
        $outer.stageReceiptSha256=$historyEvidence[$label].sha256;$outer.authorizationSha256=$auth.sha256
        if($stage -ceq 'Validate'){$outer.bindingCases=22;$outer.pureCases=522;$outer.runtimeBindingsAccepted=7;$outer.outputOnlyDlls=2}
        $outerPin=Write-ProofTestJson (Join-Path $r ($label+'-outer.json')) $outer
        $stages.Add(@{stage=$stage;receiptPath=$historyEvidence[$label].path;receiptSha256=$historyEvidence[$label].sha256;
            authorizationReference=$grant;authorizationPath=$auth.path;authorizationSha256=$auth.sha256;
            grantReceiptPath=$grantPin.path;grantReceiptSha256=$grantPin.sha256;outerReceiptPath=$outerPin.path;outerReceiptSha256=$outerPin.sha256})
    }
    $historyEvidence.handoff=Write-ProofTestJson (Join-Path $r 'handoff.json') @{runId=$prior;stages=$stages.ToArray()}
    $historyEvidence.refused=Write-ProofTestJson (Join-Path $r 'refused.json') @{
        run_id=$prior;passed=$false;input_manifest_sha256=$manifestPin.sha256;source_manifest_sha256=$historyEvidence.dSource.sha256;readiness=@{Ready=$false}}
    $historyPin=Write-ProofTestJson (Join-Path $r 'history.json') @{
        priorId=$prior;applicationHead=$head;applicationTree=$tree;priorSourceGate=$gate;files=@();evidence=$historyEvidence}
    $sourceRows=@(foreach($name in @('Desktop.cs','Policy.cs','Policy.Tests.cs','Readiness.cs','RuntimeBinding.ps1','RuntimeBinding.Tests.ps1',
        'ProcessImage.ps1','ProcessImage.Tests.ps1','prepare.ps1','validate-helper.ps1','run.ps1','launch.ps1','test-pure.ps1',
        'Configuration.ps1','Configuration.Tests.ps1','restage\RestagePolicy.ps1','restage\restage.ps1','restage\test-pure.ps1',
        'supervision\NonCopySupervisor.ps1','supervision\RestageSupervisor.ps1')){
        $bytes=[IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $name))
        @{path=$name;bytes=$bytes.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}
    })
    $sourcePin=Write-ProofTestJson (Join-Path $Root 'source.json') @{files=$sourceRows}
    $total=[long]((@($manifest.appFiles)+@($runtime)+@($fixtures)|Measure-Object bytes -Sum).Sum)
    $configuration=@{schema='offline-patch-import-proof-v1';outputRoot=$output;evidenceRoot=$evidence;applicationSource=$head;host=$hostPin;sourceManifest=$sourcePin;
        approval=@{sourceGateReference=$gate;authorizationReference=$grant;authorizationSha256=('7'*64)};
        machine=@{systemRoot=(Join-Path $Root 'system');dotnetRoot=(Join-Path $Root 'dotnet');gitPath=(Join-Path $Root 'git.exe');
            programFiles=(Join-Path $Root 'program-files');programFilesX86=(Join-Path $Root 'program-files-x86')};
        preparation=$null;restage=@{priorId=$prior;donorRoot=$donor;manifest=$manifestPin;metadata=$metadataPin;history=$historyPin;
            receipts=$configuredReceipts.ToArray();expected=@{payload=$manifestPin;files=605;bytes=$total}}}
    $configurationPin=Write-ProofTestJson (Join-Path $Root 'configuration.json') $configuration
    return @{config=$configuration;pin=$configurationPin;manifest=$manifest}
}
function Invoke-ReportingWriterTests([string]$Root) {
    . (Join-Path $PSScriptRoot 'supervision\NonCopySupervisor.ps1')
    . (Join-Path $PSScriptRoot 'supervision\RestageSupervisor.ps1')
    $cases=0
    foreach($writer in @('Write-NonCopyReceipt','Write-RestageReceipt')) {
        foreach($external in @(100,310)){
            foreach($failureAt in @('verify','inventory','completion')){
                $prefix=Join-Path $Root ($writer+'-orchestration-'+$external+'-'+$failureAt)
                $testTime=@{now=10.0}
                $terminalWindow=@{start=10.0;deadline=15.0;previous=10.0;attempted=$false}
                $observations=@{pid=1;startTicks=1L;image='synthetic';exitCode=0;exitConfirmed=$true;outputConfirmed=$true;timedOut=$false;
                    killAttempts=0;environmentCleared=$true;stdinClosed=$true;drainingBeforeImageObservation=$true;failure=$null}
                Assert-ProofTestThrows {
                    $null=Invoke-RTerminalPublication -Bindings @{exe='synthetic';hostSha256=('1'*64)} -Observations $observations -WriteTerminal {
                        param($Bytes)
                        & $writer -Path ($prefix+'-terminal.json') -Bytes $Bytes -Window $terminalWindow -ReadClock {$testTime.now} -Phase Terminal -ExternalSeconds $external
                    } -VerifyOutput {
                        if($failureAt -ceq 'verify'){$testTime.now=$external+5;Assert-ROptionalPublicationAdmission $testTime.now $external}
                        @{passed=$true;rows=@();details=@{}}
                    } -WriteInventory {
                        param($Bytes)
                        $testTime.reads=0
                        Write-ProofInventory ($prefix+'-inventory.json') $Bytes {
                            $testTime.reads++
                            if($failureAt -ceq 'inventory' -and $testTime.reads -eq 2){$testTime.now=$external+5}
                            $testTime.now
                        } $external
                    } -CompleteReceipt {
                        param($Receipt)
                        $testTime.now=$external+5
                        Assert-ROptionalPublicationAdmission $testTime.now $external
                        @{}
                    } -WriteReceipt {
                        param($Bytes)
                        $window=@{start=[double]$testTime.now;deadline=([double]$testTime.now+5);previous=[double]$testTime.now;attempted=$false}
                        & $writer -Path ($prefix+'-receipt.json') -Bytes $Bytes -Window $window -ReadClock {$testTime.now} -Phase Final -ExternalSeconds $external
                    }
                }
                $receipt=Read-ProofJsonFile ($prefix+'-receipt.json')
                Assert-Proof (!$receipt.passed -and $receipt.terminalPublicationConfirmed -and $receipt.exitCode -eq 0 -and
                    !$receipt.timedOut -and $receipt.killAttempts -eq 0 -and $receipt.reportingFailure) 'Optional failure lost/damaged final observations.'
                Assert-Proof ($terminalWindow.start -eq 10 -and $terminalWindow.deadline -eq 15 -and $terminalWindow.attempted) 'Terminal reporting window renewed.'
                $cases++
            }
        }
        foreach($elapsed in @(105,315)) {
            foreach($phase in @('Terminal','Final')) {
                $path=Join-Path $Root ($writer+'-'+$elapsed+'-'+$phase+'.json')
                $bytes=ConvertTo-RPublicationBytes @{passed=$false;failure='optional completion expired';exitCode=$null;timedOut=$true}
                $window=@{start=[double]$elapsed;deadline=([double]$elapsed+5);previous=[double]$elapsed;attempted=$false}
                $operations=[Collections.Generic.List[string]]::new()
                $ack=& $writer -Path $path -Bytes $bytes -Window $window -ReadClock {$elapsed} -Phase $phase -ExternalSeconds ($elapsed-5) -CheckOperation {param($step) $operations.Add($step);$true}
                Assert-Proof ($ack -is [bool] -and $ack) 'Writer must acknowledge exactly Boolean true.'
                Assert-Proof (([Convert]::ToHexString([IO.File]::ReadAllBytes($path))) -ceq [Convert]::ToHexString($bytes)) 'Writer changed exact bytes.'
                Assert-Proof (($operations -join ',') -ceq 'admission,write,flush,close,hash,acknowledgement') 'Durability/verification order.'
                Assert-ProofTestThrows { & $writer -Path $path -Bytes $bytes -Window $window -ReadClock {$elapsed} -Phase $phase -ExternalSeconds ($elapsed-5) }
                $fresh=@{start=[double]$elapsed;deadline=([double]$elapsed+5);previous=[double]$elapsed;attempted=$false}
                Assert-ProofTestThrows { & $writer -Path $path -Bytes $bytes -Window $fresh -ReadClock {$elapsed} -Phase $phase -ExternalSeconds ($elapsed-5) }
                $cases++
            }
        }
        foreach($failure in @('write','flush','hash','acknowledgement')) {
            $path=Join-Path $Root ($writer+'-'+$failure+'.json')
            $window=@{start=105.0;deadline=110.0;previous=105.0;attempted=$false}
            Assert-ProofTestThrows {
                & $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100 -CheckOperation {param($step) $step -cne $failure}
            }
            Assert-Proof $window.attempted 'Failure did not consume writer admission.'
            $cases++
        }
        foreach($kind in @('missing','string','extra')){
            $path=Join-Path $Root ($writer+'-ack-'+$kind+'.json')
            $window=@{start=105.0;deadline=110.0;previous=105.0;attempted=$false}
            Assert-ProofTestThrows {
                & $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100 -CheckOperation {
                    param($step)
                    if($step -cne 'acknowledgement'){$true}
                    elseif($kind -ceq 'string'){'true'}
                    elseif($kind -ceq 'extra'){$true;$true}
                }
            }
            Assert-Proof ([IO.File]::ReadAllText($path) -ceq '{}') 'Failed acknowledgement discarded written evidence.';$cases++
        }
        $path=Join-Path $Root ($writer+'-real-hash-mismatch.json')
        $window=@{start=105.0;deadline=110.0;previous=105.0;attempted=$false}
        Assert-ProofTestThrows {
            & $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100 -CheckOperation {
                param($step)
                if($step -ceq 'hash'){[IO.File]::WriteAllBytes($path,([byte[]]@(91,93)))}
                $true
            }
        };$cases++
        $path=Join-Path $Root ($writer+'-maximum.json')
        $window=@{start=105.0;deadline=110.0;previous=105.0;attempted=$false}
        $ack=& $writer -Path $path -Bytes ([byte[]]::new(1048576)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100
        Assert-Proof ($ack -is [bool] -and $ack -and ([IO.FileInfo]$path).Length -eq 1048576) 'Inclusive reporting byte cap.';$cases++
        $path=Join-Path $Root ($writer+'-deadline.json')
        $window=@{start=105.0;deadline=110.0;previous=105.0;attempted=$false}
        Assert-ProofTestThrows { & $writer -Path $path -Bytes ([byte[]]@(123,125)) -Window $window -ReadClock {110} -Phase Final -ExternalSeconds 100 }
        $cases++
        $window=@{start=105.0;deadline=110.0;previous=105.0;attempted=$false}
        Assert-ProofTestThrows { & $writer -Path $path -Bytes ([byte[]]::new(1048577)) -Window $window -ReadClock {105} -Phase Final -ExternalSeconds 100 }
        $cases++
    }
    return $cases
}
function Invoke-ConfigurationIntegrationTests([string]$Root) {
    . (Join-Path $PSScriptRoot 'supervision\NonCopySupervisor.ps1')
    $fixture=New-ProofRestageFixture (Join-Path $Root 'physical')
    $config=$fixture.config;$pin=$fixture.pin;$cases=0
    $parsed=Read-ProofConfiguration $pin.path $pin.sha256 -Purpose Restage
    $null=Assert-ProofSource $parsed $PSScriptRoot;$cases++
    $deadlineRecord=@{timedOut=$false;passed=$false}
    Assert-ProofProcessDeadline $deadlineRecord 9.99 10;$cases++
    Assert-ProofTestThrows {Assert-ProofProcessDeadline $deadlineRecord 10 10}
    Assert-Proof ($deadlineRecord.timedOut -and !$deadlineRecord.passed) 'Exact deadline became success.';$cases++
    Assert-ProofTestThrows {Assert-ProofProcessDeadline $deadlineRecord 1 10};$cases++
    $cases+=Invoke-ProofBooleanTests
    $adapterError=$null
    try {
        & (Join-Path $PSScriptRoot 'supervision\RestageSupervisor.ps1') -Configuration $pin.path -ConfigurationSha256 $pin.sha256 -NewGuiId ('a'*32)
    }catch{$adapterError=$_.Exception.Message}
    Assert-Proof ($adapterError -ceq 'Pinned PowerShell 7 host required.') 'Actual restage adapter lost declared configuration before the no-spawn host gate.'
    $cases++
    $before=@([IO.Directory]::GetFileSystemEntries($config.outputRoot)).Count+@([IO.Directory]::GetFileSystemEntries($config.evidenceRoot)).Count
    Assert-ProofTestThrows {Read-ProofConfiguration '' ('0'*64)};$cases++
    Assert-ProofTestThrows {Read-ProofConfiguration (Join-Path $Root 'missing.json') ('0'*64)};$cases++
    $json=[IO.File]::ReadAllText($pin.path)
    foreach($bad in @('{}',$json.Replace('"schema":','"Schema":null,"schema":'),$json.Replace('"schema":','"command":"not allowed","schema":'),
        $json.Replace('"schema":"offline-patch-import-proof-v1"','"schema":[]'),$json.Replace('"files":605','"files":"605"'))){
        $badPin=Write-ProofTestBytes (Join-Path $Root ('bad-'+$cases+'.json')) ([Text.Encoding]::UTF8.GetBytes($bad))
        Assert-ProofTestThrows {Read-ProofConfiguration $badPin.path $badPin.sha256 -Purpose Restage};$cases++
    }
    Assert-Proof ($before -eq (@([IO.Directory]::GetFileSystemEntries($config.outputRoot)).Count+@([IO.Directory]::GetFileSystemEntries($config.evidenceRoot)).Count)) 'Configuration rejection allocated output.'
    $cases++
    $relocated=Join-Path $Root 'relocated source'
    $source=Read-ProofPinnedJson $config.sourceManifest
    foreach($row in $source.files){
        $path=Join-Path $relocated $row.path
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        [IO.File]::Copy((Join-Path $PSScriptRoot $row.path),$path,$false)
    }
    $readOnly=& (Join-Path $relocated 'restage\restage.ps1') -Configuration $pin.path -ConfigurationSha256 $pin.sha256 -PriorId $config.restage.priorId -ReadOnlyPrerequisites | ConvertFrom-Json -AsHashtable
    Assert-Proof ($readOnly.passed -and !$readOnly.copiesPerformed) 'Relocated production prerequisites.';$cases++
    $null=Confirm-ProofChildResult $config $pin.sha256 'ReadOnlyPrerequisites' '' $readOnly {};$cases++
    $newId='8'*32
    New-ProofRestageClaim $config $pin.sha256 $newId
    $report=& (Join-Path $PSScriptRoot 'restage\restage.ps1') -Configuration $pin.path -ConfigurationSha256 $pin.sha256 -PriorId $config.restage.priorId -NewGuiId $newId | ConvertFrom-Json -AsHashtable
    Assert-Proof ($report.passed -and $report.payloadFiles -eq 605 -and $report.payloadBytes -eq $config.restage.expected.bytes) 'Production fresh copy failed.';$cases++
    $rows=@(Confirm-ProofChildResult $config $pin.sha256 'Restage' $newId $report {})
    Assert-Proof ($rows.Count -eq 608) 'Production supervisor inventory did not verify every output.';$cases++
    Assert-ProofTestThrows {New-ProofRestageClaim $config $pin.sha256 $newId};$cases++
    Assert-ProofTestThrows {
        & (Join-Path $PSScriptRoot 'restage\restage.ps1') -Configuration $pin.path -ConfigurationSha256 $pin.sha256 -PriorId $config.restage.priorId -NewGuiId $newId
    };$cases++
    foreach($group in @(@{prefix='app';rows=$fixture.manifest.appFiles},@{prefix='support';rows=$fixture.manifest.runtimeAssemblies},
        @{prefix=('desktop-proof-'+$config.restage.priorId);rows=$fixture.manifest.fixtures})){
        foreach($row in $group.rows){
            $path=Join-Path $config.restage.donorRoot ($group.prefix+'\'+$row.path)
            $null=Read-ProofPinnedBytes @{path=$path;bytes=$row.bytes;sha256=$row.sha256}
        }
    };$cases++
    $corruptPath=Join-Path $config.restage.donorRoot 'app\file-0.dat'
    [IO.File]::WriteAllBytes($corruptPath,([byte[]]@(255)))
    $failedId='9'*32;New-ProofRestageClaim $config $pin.sha256 $failedId
    Assert-ProofTestThrows {
        & (Join-Path $PSScriptRoot 'restage\restage.ps1') -Configuration $pin.path -ConfigurationSha256 $pin.sha256 -PriorId $config.restage.priorId -NewGuiId $failedId
    };$cases++
    $failed=Read-ProofJsonFile (Join-Path $config.evidenceRoot ('restage-'+$failedId+'.result.json'))
    Assert-Proof (!$failed.passed -and !$failed.copiesPerformed -and $failed.failure -and
        ![IO.Path]::Exists((Join-Path $config.outputRoot ('inputs-'+$failedId)))) 'Production failure output/admission preservation.';$cases++
    foreach($invalidType in @('PurePassed','CompilePassed','BindingAccepted','OuterSource')){
        $badFixture=New-ProofRestageFixture (Join-Path $Root ('bad-history-'+$invalidType)) $invalidType
        Assert-ProofTestThrows {
            & (Join-Path $PSScriptRoot 'restage\restage.ps1') -Configuration $badFixture.pin.path -ConfigurationSha256 $badFixture.pin.sha256 `
                -PriorId $badFixture.config.restage.priorId -ReadOnlyPrerequisites
        }
        Assert-Proof (@([IO.Directory]::GetFileSystemEntries($badFixture.config.outputRoot)).Count -eq 0) 'Malformed history allocated output.'
        $cases++
    }
    return $cases
}
