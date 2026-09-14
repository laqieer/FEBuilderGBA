throw 'PinnedProof.UnsupportedDirectRoute'
function ProofEnvelope {
    function Invoke-RestageEntry {
        [CmdletBinding(DefaultParameterSetName='Restage')]
        param([Parameter(Mandatory)][string]$Configuration,
            [Parameter(Mandatory)][string]$ConfigurationSha256,
            [Parameter(Mandatory)][string]$PriorId,
            [Parameter(Mandatory,ParameterSetName='Restage')][string]$NewGuiId,
            [Parameter(Mandatory,ParameterSetName='Compatibility')][switch]$ReadOnlyPrerequisites)
        $ErrorActionPreference='Stop'
        Set-StrictMode -Version Latest
        . (Join-Path $PSScriptRoot '..\Configuration.ps1')
        $config=Read-ProofConfiguration $Configuration $ConfigurationSha256 -Purpose Restage
        $sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
        $null=Assert-ProofSource $config $sourceRoot
        . (Join-Path $PSScriptRoot 'RestagePolicy.ps1')
        Assert-R ($args.Count -eq 0 -and $PriorId -ceq $config.restage.priorId) 'Declared arguments and donor binding required.'
        $clock=[Diagnostics.Stopwatch]::StartNew()
        $state=@{failed=$false;completed=$false;files=0;bytes=0L}
        $script:RestageExpected=@{
            priorId=$PriorId;sourceGate=$config.approval.sourceGateReference;authorization=$config.approval.authorizationReference
            files=$config.restage.expected.files;bytes=$config.restage.expected.bytes
            payload=(Read-ProofPinnedJson $config.restage.expected.payload)
            hostRoot=[IO.Path]::GetDirectoryName($config.host.path);systemRoot=$config.machine.systemRoot
        }
        $compatibility=$PSCmdlet.ParameterSetName -ceq 'Compatibility'
        if(!$compatibility){Assert-RIds $PriorId $NewGuiId}
        $owned=$false;$claimHandle=$null;$resultPath=$null
        $report=[ordered]@{schema='windows-desktop-restage-result-v1';stage=$(if($compatibility){'ReadOnlyPrerequisites'}else{'Restage'});
            startedUtc=[DateTime]::UtcNow.ToString('o');completedUtc=$null
            passed=$false;reusedPreparationId=$PriorId;freshGuiRunId=$NewGuiId;configurationSha256=$ConfigurationSha256
            sourceManifestSha256=$config.sourceManifest.sha256;sourceGateReference=$config.approval.sourceGateReference
            authorizationReference=$config.approval.authorizationReference;authorizationSha256=$config.approval.authorizationSha256
            copiesOnly=(!$compatibility);copiesPerformed=$false;guiPassed=$false;nativeReadinessClaimed=$false;freshBuildClaimed=$false;failure=$null}
        $freeze=Read-ProofHistory $config.restage.history
        Assert-R ($freeze -is [Collections.IDictionary] -and $freeze.priorId -ceq $PriorId -and $freeze.applicationHead -ceq $config.applicationSource) 'Historical source configuration.'
        Assert-REqual $freeze.evidence.inputManifest $config.restage.manifest 'historical donor manifest'
        Assert-REqual $freeze.evidence.metadata $config.restage.metadata 'historical tool metadata'
        $R=[IO.Path]::GetDirectoryName($config.restage.history.path)
        $B=[IO.Path]::GetDirectoryName($freeze.evidence.bSource.path)
        $D=[IO.Path]::GetDirectoryName($freeze.evidence.dSource.path)
        Assert-R ([IO.Path]::GetFullPath((Join-Path $D ('inputs-'+$PriorId))) -ceq $config.restage.donorRoot) 'Historical donor root.'
        $RestagerSourceManifestSha256=$config.restage.history.sha256
        $sourceBytes=$config.restage.history.bytes
        $hostPath=$config.host.path;$hostSha=$config.host.sha256
        $utf8=[Text.UTF8Encoding]::new($false,$true)
        function Budget { Assert-RAdmission $state $clock.Elapsed.TotalSeconds 300 }
        function Plain([string]$Path) { Budget;Assert-ProofPath $Path -Existing }
        function Absent([string]$Path) { Budget;Assert-ProofPath $Path;Assert-R (![IO.Path]::Exists($Path)) 'Consumed destination.' }
        function Child([string]$Root,[string]$Relative) {
            Assert-RRelative $Relative
            $path=[IO.Path]::GetFullPath((Join-Path $Root ($Relative.Replace('\',[IO.Path]::DirectorySeparatorChar))))
            Assert-R ($path.StartsWith($Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::Ordinal)) 'Root escape.'
            return $path
        }
        function HashStream([IO.Stream]$Stream,[long]$Length) {
            Budget;Assert-R ($Length -ge 0 -and $Length -le 268435456 -and $Stream.Length -eq $Length) 'Pinned length bound.'
            $Stream.Position=0;$buffer=[byte[]]::new(1048576);$total=0L
            $hash=[Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
            try {
                while($true){
                    Budget;$read=$Stream.Read($buffer,0,$buffer.Length);Budget
                    if(!$read){break};$total+=$read;Assert-R ($total -le $Length) 'Extra input bytes.'
                    $hash.AppendData($buffer,0,$read)
                }
                Assert-R ($total -eq $Length) 'Short input bytes.'
                return [Convert]::ToHexString($hash.GetHashAndReset()).ToLowerInvariant()
            } finally {$hash.Dispose()}
        }
        function Verify([string]$Path,$Row) {
            Assert-ProofPin @{path=$Path;bytes=$Row.bytes;sha256=$Row.sha256}
            Plain $Path
            $s=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
            try {Assert-R ((HashStream $s $Row.bytes) -ceq $Row.sha256) 'Payload pin mismatch.'}finally{$s.Dispose()}
        }
        function Inventory([string]$Root,[object[]]$Rows) {
            Plain $Root
            $files=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $directories=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach($row in $Rows){
                Assert-RRelative $row.path;Assert-R ($files.Add($row.path)) 'Duplicate inventory row.'
                $parts=$row.path.Split('\');$prefix=''
                for($i=0;$i -lt $parts.Length-1;$i++){$prefix=if($prefix){$prefix+'\'+$parts[$i]}else{$parts[$i]};$null=$directories.Add($prefix)}
            }
            $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($Root)
            $seenFiles=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $seenDirectories=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            while($pending.Count){
                Budget;$current=$pending.Pop()
                foreach($path in [IO.Directory]::EnumerateFileSystemEntries($current)){
                    Plain $path;$relative=[IO.Path]::GetRelativePath($Root,$path).Replace([IO.Path]::DirectorySeparatorChar,'\')
                    if([IO.Directory]::Exists($path)){Assert-R ($directories.Contains($relative) -and $seenDirectories.Add($relative)) 'Unexpected directory.';$pending.Push($path)}
                    else{Assert-R ($files.Contains($relative) -and $seenFiles.Add($relative)) 'Unexpected file.'}
                }
            }
            Assert-R ($seenFiles.Count -eq $files.Count -and $seenDirectories.Count -eq $directories.Count) 'Missing inventory entry.'
        }
        function ReadPinnedText([string]$Path,$Row,[long]$Maximum=4194304) {
            Budget
            $bytes=Read-ProofPinnedBytes @{path=$Path;bytes=$Row.bytes;sha256=$Row.sha256} $Maximum
            Budget
            return $utf8.GetString($bytes)
        }
        function ReadDynamic([string]$Path,[string]$Sha,[long]$Maximum) {
            Plain $Path
            $row=@{bytes=([IO.FileInfo]$Path).Length;sha256=$Sha}
            return ConvertFrom-RJson (ReadPinnedText $Path $row $Maximum)
        }
        function Frozen([string]$Name) {
            $row=$freeze.evidence[$Name]
            return ConvertFrom-RJson (ReadPinnedText $row.path $row)
        }
        function VerifySourceClosure {
            $null=Verify $config.restage.history.path @{bytes=$sourceBytes;sha256=$RestagerSourceManifestSha256}
            foreach($row in $freeze.files) { $null=Verify (Child $R $row.path) $row }
            foreach($row in $freeze.evidence.Values) { $null=Verify $row.path $row }
            $pre=Frozen 'preflight'
            Assert-RInteger $pre.installedPins 46 46
            Assert-R ($pre.runId -ceq $PriorId -and $pre.passed -is [bool] -and $pre.passed -and $pre.rows.Count -eq 99 -and $pre.installedPins -eq 46) 'Prior preflight closure.'
            foreach($row in $pre.rows) {
                Assert-R ($row.nonReparseAncestry -is [bool] -and $row.nonReparseAncestry) 'Historical reparse check missing.'
                $null=Verify $row.path $row
            }
            foreach($item in @(@{name='bSource';root=$B;count=7},@{name='dSource';root=$D;count=10})) {
                $source=Frozen $item.name
                Assert-R ($source.files.Count -eq $item.count) 'Original source member count.'
                $names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                foreach($row in $source.files) {
                    Assert-R ($names.Add($row.path)) 'Original source duplicate.'
                    $null=Verify (Child $item.root $row.path) $row
                }
                if($item.name -ceq 'dSource') {
                    Assert-R ($source.externalSourceDependencies.Count -eq 3) 'Generator source closure.'
                    foreach($row in $source.externalSourceDependencies) { $null=Verify $row.path $row }
                }
            }
        }
        function PriorEvidence {
            VerifySourceClosure
            $handoff=Frozen 'handoff';$metadata=Frozen 'metadata';$manifest=Frozen 'inputManifest'
            Assert-R ($metadata.tools.Count -eq 13 -and $metadata.runtimeAssemblies.Count -eq 7 -and $metadata.compileReferences.Count -eq 26 -and $metadata.assets.Count -eq 15) 'Installed/tool asset closure counts.'
            Assert-R ($handoff.runId -ceq $PriorId -and $handoff.stages.Count -eq 3) 'Handoff identity.'
            $stages=[ordered]@{};$outers=[ordered]@{}
            foreach($name in @('Validate','Build','Inputs')) {
                $rows=@($handoff.stages | Where-Object { $_.stage -ceq $name })
                Assert-R ($rows.Count -eq 1) 'Handoff stage closure.'
                $link=$rows[0]
                $label=@{Validate='validate';Build='build';Inputs='inputs'}[$name]
                Assert-R ($link.receiptPath -ceq $freeze.evidence[$label].path -and $link.receiptSha256 -ceq $freeze.evidence[$label].sha256) 'Handoff stage digest link.'
                $receipt=Frozen $label
                $expected=@{
                    stage=$name;runId=$PriorId;sourceManifestSha256=$freeze.evidence.bSource.sha256
                    helperSourceManifestSha256=$freeze.evidence.dSource.sha256;toolMetadataSha256=$freeze.evidence.metadata.sha256
                    sourceGateReference=$freeze.priorSourceGate;authorizationReference=$link.authorizationReference
                    head=$freeze.applicationHead;tree=$freeze.applicationTree
                }
                Assert-RPriorReceipt $receipt $expected
                Assert-R ($receipt.schema -ceq 'windows-desktop-preparation-receipt-v1') 'Original receipt schema.'
                $null=ReadPinnedText $link.authorizationPath @{bytes=([IO.FileInfo]$link.authorizationPath).Length;sha256=$link.authorizationSha256}
                $grant=ReadDynamic $link.grantReceiptPath $link.grantReceiptSha256 4194304
                Assert-R ($grant.stage -ceq $name -and $grant.runId -ceq $PriorId -and $grant.authorizationReference -ceq $link.authorizationReference -and $grant.authorizationSha256 -ceq $link.authorizationSha256 -and $grant.sourceGateReference -ceq $freeze.priorSourceGate) 'Original grant receipt.'
                $outer=ReadDynamic $link.outerReceiptPath $link.outerReceiptSha256 4194304
                Assert-ROuter $outer $hostPath $hostSha
                foreach($key in @('stage','runId','sourceManifestSha256','helperSourceManifestSha256','toolMetadataSha256','sourceGateReference','authorizationReference')) {
                    Assert-R ($outer[$key] -ceq $expected[$key]) "Outer prior binding: $key"
                }
                Assert-R ($outer.stageReceiptPath -ceq $link.receiptPath -and $outer.stageReceiptSha256 -ceq $link.receiptSha256 -and $outer.authorizationSha256 -ceq $link.authorizationSha256 -and
                    $outer.sourceFreezesReverified -is [bool] -and $outer.sourceFreezesReverified) 'Outer stage/grant/source chain.'
                $processCount=@{Validate=28;Build=32;Inputs=27}[$name]
                Assert-R ($receipt.processes.Count -eq $processCount) 'Prior process count.'
                foreach($proc in $receipt.processes) {
                    Assert-RInteger $proc.exitCode 0 0;Assert-RInteger $proc.pid 1 ([int]::MaxValue);Assert-RInteger $proc.startTicks 1 ([long]::MaxValue)
                    Assert-R ($proc.passed -is [bool] -and $proc.passed -and $proc.exitCode -eq 0 -and $proc.timedOut -is [bool] -and !$proc.timedOut -and $proc.pid -gt 0 -and $proc.startTicks -gt 0 -and $proc.imageObservation.path -ceq $proc.exe) 'Prior process did not positively finish.'
                }
                $stages[$name]=$receipt;$outers[$name]=$outer
            }
            $v=$stages.Validate
            Assert-R ($v.processImageCases -eq 10 -and $v.processes.Count -eq 28 -and $v.validationFiles.Count -eq 11) 'Original Validate counts.'
            $validationRoot=Join-Path $B ('validate-'+$PriorId)
            foreach($row in $v.validationFiles) { $null=Verify (Child $validationRoot $row.path) $row }
            $pureRow=@($v.validationFiles | Where-Object { $_.path -ceq 'Pure.json' })[0]
            $compileRow=@($v.validationFiles | Where-Object { $_.path -ceq 'Compile.json' })[0]
            $pure=ConvertFrom-RJson (ReadPinnedText (Join-Path $validationRoot 'Pure.json') $pureRow)
            $compile=ConvertFrom-RJson (ReadPinnedText (Join-Path $validationRoot 'Compile.json') $compileRow)
            Assert-RInteger $v.processImageCases 10 10;Assert-RInteger $pure.cases 522 522;Assert-RInteger $pure.bindingCases 22 22
            Assert-R ($pure.passed -is [bool] -and $pure.passed -and $pure.cases -eq 522 -and $pure.bindingCases -eq 22 -and
                $pure.nativeCalls -is [bool] -and !$pure.nativeCalls -and $pure.appLaunched -is [bool] -and !$pure.appLaunched) 'Original pure result.'
            Assert-R ($compile.passed -is [bool] -and $compile.passed -and $compile.nativeCalls -is [bool] -and !$compile.nativeCalls -and
                $compile.appLaunched -is [bool] -and !$compile.appLaunched -and $compile.runtimeBindings.Count -eq 7) 'Original compile result.'
            foreach($binding in $compile.runtimeBindings) {
                Assert-R ($binding.accepted -is [bool] -and $binding.accepted -and $binding.actualSha256 -is [string] -and
                    $binding.actualSha256 -cmatch '^[0-9a-f]{64}$' -and $binding.actualSha256 -ceq $binding.expectedSha256 -and
                    $binding.actualIdentity -is [string] -and ![string]::IsNullOrWhiteSpace($binding.actualIdentity) -and
                    $binding.actualIdentity -ceq $binding.expectedIdentity) 'Original seven-runtime binding.'
            }
            foreach($key in @('bindingCases','pureCases','runtimeBindingsAccepted','outputOnlyDlls')) {
                $expectedCount=@{bindingCases=22;pureCases=522;runtimeBindingsAccepted=7;outputOnlyDlls=2}[$key]
                Assert-RInteger $outers.Validate[$key] $expectedCount $expectedCount
                Assert-R ($outers.Validate[$key] -eq $expectedCount) 'Original outer Validate count.'
            }
            $outputs=Frozen 'outputs';$provenance=Frozen 'projection'
            Assert-R ($stages.Build.outputManifestSha256 -ceq $freeze.evidence.outputs.sha256 -and $stages.Inputs.inputManifestSha256 -ceq $freeze.evidence.inputManifest.sha256 -and $stages.Inputs.projectionProvenanceSha256 -ceq $freeze.evidence.projection.sha256) 'Prior output digest chain.'
            Assert-R ($outputs.applicationSource -ceq $freeze.applicationHead -and $outputs.worktreeHead -ceq $freeze.applicationHead -and $outputs.appFiles.Count -eq 596 -and $outputs.generatorFiles.Count -eq 8 -and $outputs.projectionFiles.Count -eq 594 -and $outputs.tests.Count -eq 2) 'Historical Build output closure.'
            Assert-R ($provenance.buildReceiptSha256 -ceq $freeze.evidence.build.sha256 -and $provenance.validationReceiptSha256 -ceq $freeze.evidence.validate.sha256 -and $provenance.sourceManifestSha256 -ceq $freeze.evidence.bSource.sha256 -and $provenance.helperSourceManifestSha256 -ceq $freeze.evidence.dSource.sha256 -and $provenance.worktreeHead -ceq $freeze.applicationHead -and $provenance.applicationSource -ceq $freeze.applicationHead -and $provenance.files.Count -eq 595) 'Projection provenance chain.'
            Assert-REqual @($outputs.tests.configuration) @('Debug','Release') 'historical configurations'
            foreach($test in $outputs.tests) {
                Assert-RInteger $test.passedCases 4 4
                Assert-R ($test.passedCases -eq 4) 'Historical four fixture tests.'
                $trx=ReadPinnedText (Child (Join-Path $B 'build-attempt') $test.trx.path) $test.trx
                Assert-RHistoricalTrx $trx
            }
            Assert-RPayload $manifest $metadata
            Assert-R ($manifest.appFiles.Count -eq $provenance.files.Count) 'Complete application projection required.'
            $proofIndex=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            $projectionIndex=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            $publishIndex=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            foreach($item in $provenance.files) { $proofIndex.Add($item.path,$item) }
            foreach($item in $outputs.projectionFiles) { $projectionIndex.Add($item.path,$item) }
            foreach($item in $outputs.appFiles) { $publishIndex.Add($item.path,$item) }
            foreach($row in $manifest.appFiles) {
                Budget
                Assert-R ($proofIndex.ContainsKey($row.path)) 'Missing input provenance.'
                $proof=$proofIndex[$row.path]
                Assert-R ($proof.bytes -eq $row.bytes -and $proof.sha256 -ceq $row.sha256) 'Input/projection row mismatch.'
                if($row.path -ceq 'config\config.xml') {
                    Assert-R ($proof.source -ceq 'reviewed prepare.ps1 literal; fresh owned projection only') 'Normal config provenance.'
                } else {
                    Assert-R ($projectionIndex.ContainsKey($row.path) -and $publishIndex.ContainsKey($row.path) -and $proof.source -ceq (Child (Join-Path $B 'publish') $row.path)) 'Publish mapping.'
                    Assert-REqual $row $projectionIndex[$row.path] 'projection row';Assert-REqual $row $publishIndex[$row.path] 'publish row'
                }
            }
            $refused=Frozen 'refused'
            Assert-R ($refused.run_id -ceq $PriorId -and $refused.passed -is [bool] -and !$refused.passed -and $refused.input_manifest_sha256 -ceq $freeze.evidence.inputManifest.sha256 -and $refused.source_manifest_sha256 -ceq $freeze.evidence.dSource.sha256 -and $refused.readiness.Ready -is [bool] -and !$refused.readiness.Ready) 'Historical refusal identity; never positive evidence.'
            return @{manifest=$manifest;metadata=$metadata;handoff=$handoff}
        }
        function ReadPrerequisites {
            Budget;$null=Assert-ProofSource $config $sourceRoot
            $prior=PriorEvidence
            $manifest=$prior.manifest
            Assert-R ($manifest.powershellHost.path -ceq $config.host.path -and $manifest.powershellHost.sha256 -ceq $config.host.sha256) 'Configured donor host binding.'
            foreach($row in $config.restage.receipts){
                Assert-REqual $row.pin $freeze.evidence[$row.stage.ToLowerInvariant()] 'configured historical receipt pin'
                Assert-R ($row.runId -ceq $PriorId -and $row.head -ceq $freeze.applicationHead -and $row.tree -ceq $freeze.applicationTree) 'Configured historical source binding.'
                Assert-RPriorReceipt (Read-ProofPinnedJson $row.pin) $row
            }
            $total=0L;$files=0
            foreach($group in @(@{path='app';rows=$manifest.appFiles},@{path='support';rows=$manifest.runtimeAssemblies},@{path="desktop-proof-$PriorId";rows=$manifest.fixtures})){
                $root=Join-Path $config.restage.donorRoot $group.path
                Inventory $root @($group.rows)
                foreach($row in $group.rows){Verify (Child $root $row.path) $row;$total+=$row.bytes;$files++}
            }
            Assert-R ($files -eq $script:RestageExpected.files -and $total -eq $script:RestageExpected.bytes) 'Exact donor totals.'
            return ,$manifest
        }
        function JsonNew([string]$Path,$Value){
            Budget;Assert-ProofPath ([IO.Path]::GetDirectoryName($Path)) -Existing
            $bytes=ConvertTo-RPublicationBytes $Value
            $s=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try{$s.Write($bytes);Budget;$s.Flush($true);Budget}finally{$s.Dispose()}
            return @{path=$Path;bytes=$bytes.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}
        }
        function CopyRow([string]$Source,[string]$Destination,$Row){
            Budget;Plain $Source;Absent $Destination
            $parent=[IO.Path]::GetDirectoryName($Destination);Assert-ProofPath $parent
            [void][IO.Directory]::CreateDirectory($parent);Plain $parent
            $read=[IO.File]::Open($Source,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
            try{
                $before=HashStream $read $Row.bytes;Assert-R ($before -ceq $Row.sha256) 'Source pre-copy hash.'
                $read.Position=0;$written=0L;$buffer=[byte[]]::new(1048576)
                $write=[IO.File]::Open($Destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
                try{
                    while($true){Budget;$n=$read.Read($buffer,0,$buffer.Length);Budget;if(!$n){break}
                        $written+=$n;Assert-R ($written -le $Row.bytes -and $state.bytes+$written -le $script:RestageExpected.bytes) 'Copy byte bound.'
                        $write.Write($buffer,0,$n);Budget}
                    $write.Flush($true);Budget
                }finally{$write.Dispose()}
                $after=HashStream $read $Row.bytes
            }finally{$read.Dispose()}
            Verify $Destination $Row;Plain $Source
            Assert-RCopy @{expectedBytes=$Row.bytes;readBytes=$written;writtenBytes=$written;expectedSha=$Row.sha256;
                sourceBefore=$before;sourceAfter=$after;destination=$Row.sha256;createdNew=$true}
            $state.files++;$state.bytes+=$written
        }
        try {
            if(!$compatibility){
                $inputRoot=Join-Path $config.outputRoot ('inputs-'+$NewGuiId)
                foreach($name in @('inputs-','run-','launch-')){Absent (Join-Path $config.outputRoot ($name+$NewGuiId))}
                $claimPath=Join-Path $config.evidenceRoot ('restage-'+$NewGuiId+'.claim.json')
                Plain $claimPath
                $claimHandle=[IO.File]::Open($claimPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
                Assert-R ($claimHandle.Length -le 4096) 'Claim bound.'
                $reader=[IO.StreamReader]::new($claimHandle,[Text.UTF8Encoding]::new($false,$true),$false,4096,$true)
                try{$claim=ConvertFrom-RJson $reader.ReadToEnd()}finally{$reader.Dispose()}
                Assert-RKeys $claim @('priorId','newGuiId','configurationSha256','sourceManifestSha256','inputManifestSha256','authorizationSha256')
                Assert-R ($claim.priorId -ceq $PriorId -and $claim.newGuiId -ceq $NewGuiId -and $claim.configurationSha256 -ceq $ConfigurationSha256 -and
                    $claim.sourceManifestSha256 -ceq $config.sourceManifest.sha256 -and $claim.inputManifestSha256 -ceq $config.restage.manifest.sha256 -and
                    $claim.authorizationSha256 -ceq $config.approval.authorizationSha256) 'Exclusive claim binding.'
                $owned=$true;$resultPath=Join-Path $config.evidenceRoot ('restage-'+$NewGuiId+'.result.json')
                Absent $resultPath
            }
            $manifest=ReadPrerequisites
            if($compatibility){Budget;$report.passed=$true;$state.completed=$true}
            else{
                Absent $inputRoot;[void][IO.Directory]::CreateDirectory($inputRoot)
                $control=Join-Path $inputRoot 'restaging';[void][IO.Directory]::CreateDirectory($control)
                $owned=$true;$resultPath=Join-Path $control 'result.json'
                $rows=[Collections.Generic.List[object]]::new()
                foreach($group in @(@{old='app';new='app';rows=$manifest.appFiles},@{old='support';new='support';rows=$manifest.runtimeAssemblies},@{old="desktop-proof-$PriorId";new="desktop-proof-$NewGuiId";rows=$manifest.fixtures})){
                    foreach($row in $group.rows){
                        $relative=$group.new+'\'+$row.path
                        CopyRow (Child (Join-Path $config.restage.donorRoot $group.old) $row.path) (Child $inputRoot $relative) $row
                        $rows.Add(@{path=$relative;bytes=$row.bytes;sha256=$row.sha256})
                    }
                }
                $next=ConvertFrom-RJson ($manifest|ConvertTo-Json -Depth 32 -Compress)
                $next.runId=$NewGuiId;Assert-RDelta $manifest $next $PriorId $NewGuiId
                $manifestPin=JsonNew (Join-Path $inputRoot 'input-manifest.json') $next
                Assert-RDelta $manifest (Read-ProofPinnedJson $manifestPin) $PriorId $NewGuiId
                $provenancePin=JsonNew (Join-Path $control 'provenance.json') @{donorManifest=$config.restage.manifest;priorReceipts=$config.restage.receipts;configurationSha256=$ConfigurationSha256;files=$rows.ToArray()}
                $rows.Add(@{path='input-manifest.json';bytes=$manifestPin.bytes;sha256=$manifestPin.sha256})
                $rows.Add(@{path='restaging\provenance.json';bytes=$provenancePin.bytes;sha256=$provenancePin.sha256})
                Inventory $inputRoot $rows.ToArray()
                foreach($row in $rows){Verify (Child $inputRoot $row.path) $row}
                $null=ReadPrerequisites
                foreach($name in @('run-','launch-')){Absent (Join-Path $config.outputRoot ($name+$NewGuiId))}
                Assert-RFinish $state $true $true $clock.Elapsed.TotalSeconds
                $report.copiesPerformed=$true;$report.payloadFiles=$state.files;$report.payloadBytes=$state.bytes
                $report.inputManifest=$manifestPin;$report.provenance=$provenancePin;$report.passed=$true
                $state.completed=$true
            }
        } catch {
            $state.failed=$true;$report.passed=$false;$report.failure=([string]$_.Exception.Message).Substring(0,[Math]::Min(2048,([string]$_.Exception.Message).Length))
        } finally {
            if($claimHandle){$claimHandle.Dispose()}
            $report.completedUtc=[DateTime]::UtcNow.ToString('o')
            $report.elapsedSeconds=$clock.Elapsed.TotalSeconds
            if($owned){
                $start=$clock.Elapsed.TotalSeconds
                $window=@{terminalPrevious=$report.elapsedSeconds;start=$start;deadline=(Get-RReceiptRetentionDeadline $report.elapsedSeconds $start);previous=$start;attempted=$false}
                $null=Write-RReportingFile -Path $resultPath -Bytes (ConvertTo-RPublicationBytes $report) -Window $window -ReadClock {$clock.Elapsed.TotalSeconds} -Phase Final -ExternalSeconds 310
            }
        }
        if(!$report.passed){throw "Restage refused: $($report.failure). Preserve all evidence; never retry."}
        Write-Output ([Text.UTF8Encoding]::new($false,$true).GetString((ConvertTo-RPublicationBytes $report)))
    }
}
