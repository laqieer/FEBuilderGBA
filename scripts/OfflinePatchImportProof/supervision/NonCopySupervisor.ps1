[CmdletBinding()]
param([ValidateSet('Pure','ReadOnlyPrerequisites')][string]$Mode,
    [string]$Configuration,[string]$ConfigurationSha256)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\Configuration.ps1')
. (Join-Path $PSScriptRoot '..\restage\RestagePolicy.ps1')

function Write-NonCopyReceipt {
    param([string]$Path,[byte[]]$Bytes,[Collections.IDictionary]$Window,
        [scriptblock]$ReadClock,[string]$Phase,[double]$ExternalSeconds,
        [scriptblock]$CheckOperation={ param($Operation) $true })
    Write-RReportingFile @PSBoundParameters
}
function Write-ProofInventory([string]$Path,[byte[]]$Bytes,[scriptblock]$ReadClock,[double]$ExternalSeconds) {
    Assert-ROptionalPublicationAdmission (& $ReadClock) $ExternalSeconds
    Assert-Proof ($Bytes.Length -ge 1 -and $Bytes.Length -le 1048576) 'Inventory byte bound.'
    Assert-ProofPath $Path
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$stream.Write($Bytes);$stream.Flush($true)}finally{$stream.Dispose()}
    Assert-ROptionalPublicationAdmission (& $ReadClock) $ExternalSeconds
    return $true
}
function New-ProofRestageClaim($Config,[string]$ConfigurationSha256,[string]$NewGuiId) {
    Assert-Proof ($NewGuiId -cmatch '^[0-9a-f]{32}$' -and $NewGuiId -cne $Config.restage.priorId) 'Fresh distinct claim identity.'
    foreach($prefix in @('inputs-','run-','launch-')){
        $path=Join-Path $Config.outputRoot ($prefix+$NewGuiId)
        Assert-ProofPath $path;Assert-Proof (![IO.Path]::Exists($path)) 'Consumed claim output.'
    }
    $path=Join-Path $Config.evidenceRoot ('restage-'+$NewGuiId+'.claim.json')
    Assert-ProofPath $path
    $bytes=ConvertTo-RPublicationBytes @{priorId=$Config.restage.priorId;newGuiId=$NewGuiId;configurationSha256=$ConfigurationSha256;
        sourceManifestSha256=$Config.sourceManifest.sha256;inputManifestSha256=$Config.restage.manifest.sha256;authorizationSha256=$Config.approval.authorizationSha256}
    $stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    $null=Read-ProofPinnedBytes @{path=$path;bytes=$bytes.Length;sha256=$hash} 4096
}
function Get-ProofSupervisionPin([string]$Path,[scriptblock]$Admission) {
    & $Admission;Assert-ProofPath $Path -Existing
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try{
        Assert-Proof ($stream.Length -le 268435456) 'Supervision file bound.'
        $pin=@{path=$Path;bytes=$stream.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}
    }finally{$stream.Dispose()}
    & $Admission
    return $pin
}
function Confirm-ProofChildResult($Config,[string]$ConfigurationSha256,[string]$Mode,[string]$NewGuiId,$Report,[scriptblock]$Admission) {
    & $Admission
    Assert-Proof ($Report.passed -is [bool] -and $Report.passed) 'Child result not passed.'
    if($Mode -ceq 'Pure'){
        $names=@(Get-ProofRestageCaseNames ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))))
        Assert-Proof ($names.Count -eq 322 -and $Report.schema -ceq 'windows-desktop-restage-pure-v1' -and
            $Report.nativeCalls -is [bool] -and !$Report.nativeCalls -and $Report.appLaunched -is [bool] -and !$Report.appLaunched) 'Pure report contract.'
        Assert-RTestResult $Report $names
        return @()
    }
    foreach($key in @('configurationSha256','sourceManifestSha256','sourceGateReference','authorizationReference','authorizationSha256')){
        $expected=switch($key){
            configurationSha256 {$ConfigurationSha256}
            sourceManifestSha256 {$Config.sourceManifest.sha256}
            default {$Config.approval[$key]}
        }
        Assert-Proof ($Report[$key] -is [string] -and $Report[$key] -ceq $expected) "Child result binding: $key"
    }
    Assert-Proof ($Report.schema -ceq 'windows-desktop-restage-result-v1' -and $Report.stage -ceq $Mode -and
        $Report.reusedPreparationId -ceq $Config.restage.priorId -and $null -eq $Report.failure) 'Child stage identity.'
    Assert-ProofTiming $Report
    foreach($key in @('guiPassed','nativeReadinessClaimed','freshBuildClaimed')){
        Assert-Proof ($Report[$key] -is [bool] -and !$Report[$key]) 'Restage cannot promote native/GUI/build evidence.'
    }
    if($Mode -ceq 'ReadOnlyPrerequisites'){
        Assert-Proof ($Report.copiesPerformed -is [bool] -and !$Report.copiesPerformed -and !$Report.copiesOnly) 'Read-only operation copied.'
        return @()
    }
    Assert-Proof ($Report.freshGuiRunId -ceq $NewGuiId -and $Report.copiesPerformed -is [bool] -and $Report.copiesPerformed -and
        $Report.copiesOnly -is [bool] -and $Report.copiesOnly -and $Report.payloadFiles -eq $Config.restage.expected.files -and
        $Report.payloadBytes -eq $Config.restage.expected.bytes) 'Restage completion totals/identity.'
    $root=Join-Path $Config.outputRoot ('inputs-'+$NewGuiId)
    Assert-Proof ($Report.inputManifest.path -ceq (Join-Path $root 'input-manifest.json') -and
        $Report.provenance.path -ceq (Join-Path $root 'restaging\provenance.json')) 'Restage report paths.'
    $manifest=Read-ProofPinnedJson $Report.inputManifest
    $old=Read-ProofPinnedJson $Config.restage.manifest
    Assert-Proof ($manifest.runId -ceq $NewGuiId) 'Restaged input ID.'
    $manifest.runId=$old.runId;Assert-REqual $manifest $old 'only restaged runId changed'
    $provenance=Read-ProofPinnedJson $Report.provenance
    Assert-REqual $provenance.donorManifest $Config.restage.manifest 'restage donor provenance'
    Assert-REqual $provenance.priorReceipts $Config.restage.receipts 'restage historical receipt provenance'
    Assert-Proof ($provenance.configurationSha256 -ceq $ConfigurationSha256) 'Restage configuration provenance.'
    $expected=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($group in @(@{prefix='app';rows=$old.appFiles},@{prefix='support';rows=$old.runtimeAssemblies},@{prefix=('desktop-proof-'+$NewGuiId);rows=$old.fixtures})){
        foreach($row in $group.rows){$expected.Add($group.prefix+'\'+$row.path,$row)}
    }
    Assert-Proof ($provenance.files.Count -eq $expected.Count) 'Restage provenance row count.'
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($row in $provenance.files){
        Assert-Proof ($seen.Add($row.path) -and $expected.ContainsKey($row.path) -and $row.bytes -eq $expected[$row.path].bytes -and
            $row.sha256 -ceq $expected[$row.path].sha256) 'Restage provenance payload row.'
    }
    $expected.Add('input-manifest.json',$Report.inputManifest);$expected.Add('restaging\provenance.json',$Report.provenance)
    $resultPath=Join-Path $root 'restaging\result.json'
    $stored=Read-ProofJsonFile $resultPath
    Assert-REqual $stored $Report 'durable child result equals stdout'
    $expected.Add('restaging\result.json',(Get-ProofSupervisionPin $resultPath $Admission))
    $rows=[Collections.Generic.List[object]]::new();$pending=[Collections.Generic.Stack[string]]::new();$pending.Push($root)
    $directories=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($name in $expected.Keys){
        $parts=$name.Split('\');$prefix=''
        for($i=0;$i -lt $parts.Length-1;$i++){$prefix=if($prefix){$prefix+'\'+$parts[$i]}else{$parts[$i]};$null=$directories.Add($prefix)}
    }
    while($pending.Count){
        & $Admission
        foreach($path in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())){
            Assert-ProofPath $path -Existing;$relative=[IO.Path]::GetRelativePath($root,$path).Replace([IO.Path]::DirectorySeparatorChar,'\')
            if([IO.Directory]::Exists($path)){Assert-Proof ($directories.Contains($relative)) 'Extra restaged directory.';$pending.Push($path);continue}
            Assert-Proof ($expected.ContainsKey($relative)) 'Extra restaged file.'
            $pin=Get-ProofSupervisionPin $path $Admission
            Assert-Proof ($pin.bytes -eq $expected[$relative].bytes -and $pin.sha256 -ceq $expected[$relative].sha256) 'Restaged output pin.'
            $rows.Add($pin)
        }
    }
    Assert-Proof ($rows.Count -eq $expected.Count) 'Missing restaged output.'
    return $rows.ToArray()
}
function Invoke-ProofSupervision {
    [CmdletBinding()]
    param([ValidateSet('Pure','ReadOnlyPrerequisites','Restage')][string]$Mode,
        [string]$Configuration,[string]$ConfigurationSha256,[string]$NewGuiId)
    $configurationData=Read-ProofConfiguration $Configuration $ConfigurationSha256 -Purpose Restage
    $root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $null=Assert-ProofSource $configurationData $root
    $hostPin=$configurationData.host
    Assert-Proof ([Environment]::ProcessPath -ceq $hostPin.path -and $PSVersionTable.PSVersion.Major -eq 7) 'Pinned PowerShell 7 host required.'
    $null=Read-ProofPinnedBytes $hostPin 16777216
    if($Mode -ceq 'Restage'){
        Assert-Proof ($NewGuiId -cmatch '^[0-9a-f]{32}$' -and $NewGuiId -cne $configurationData.restage.priorId) 'Fresh GUI ID.'
    }
    $external=if ($Mode -ceq 'Pure') { 100 } else { 310 }
    $totalClock=[Diagnostics.Stopwatch]::StartNew()
    $executionClock=[Diagnostics.Stopwatch]::new()
    $outer=Join-Path $configurationData.evidenceRoot ($Mode+'-'+$(if($Mode -ceq 'Restage'){$NewGuiId}else{$configurationData.sourceManifest.sha256}))
    Assert-ProofPath $outer
    Assert-Proof (![IO.Path]::Exists($outer)) 'Consumed supervisor destination; no retry.'
    [void][IO.Directory]::CreateDirectory($outer)
    $streams=[Collections.Generic.List[object]]::new()
    $observed=@{exitConfirmed=$null;outputConfirmed=$null;timedOut=$null;environmentCleared=$null;
        stdinClosed=$null;drainingBeforeImageObservation=$null;custodyConfirmed=$null;pid=$null;
        startTicks=$null;image=$null;exitCode=$null;killAttempts=0;failure=$null;stdoutEof=$null;stderrEof=$null}
    $child=$null;$confirmation=$null;$started=$false
    try {
        if($Mode -ceq 'Restage'){New-ProofRestageClaim $configurationData $ConfigurationSha256 $NewGuiId}
        $start=[Diagnostics.ProcessStartInfo]::new($hostPin.path)
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WorkingDirectory=$outer
        $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $start.Environment.Clear()
        foreach ($directory in @('profile','profile\AppData','profile\AppData\Roaming','profile\AppData\Local','scratch')) {
            [void][IO.Directory]::CreateDirectory((Join-Path $outer $directory))
        }
        $environment=Get-RChildEnvironment $outer $configurationData.machine.systemRoot ([IO.Path]::GetDirectoryName($hostPin.path))
        foreach ($key in $environment.Keys) { $start.Environment[$key]=[string]$environment[$key] }
        $observed.environmentCleared=$true
        $scriptPath=if($Mode -ceq 'Pure'){Join-Path $root 'restage\test-pure.ps1'}else{Join-Path $root 'restage\restage.ps1'}
        foreach($argument in @('-NoLogo','-NoProfile','-NonInteractive','-File',$scriptPath)) { $start.ArgumentList.Add($argument) }
        if($Mode -cne 'Pure') {
            foreach($argument in @('-Configuration',$Configuration,'-ConfigurationSha256',$ConfigurationSha256,
                '-PriorId',$configurationData.restage.priorId)) { $start.ArgumentList.Add($argument) }
            if($Mode -ceq 'Restage') { $start.ArgumentList.Add('-NewGuiId');$start.ArgumentList.Add($NewGuiId) }
            else { $start.ArgumentList.Add('-ReadOnlyPrerequisites') }
        }
        foreach($name in @('stdout','stderr')) {
            $path=Join-Path $outer ($name+'.log')
            $streams.Add(@{name=$name;path=$path;log=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read);
                buffer=[byte[]]::new(65536);bytes=0L;task=$null;pipe=$null;eof=$null;failed=$false})
        }
        $child=[Diagnostics.Process]::new();$child.StartInfo=$start
        $executionClock.Start()
        Assert-Proof ($child.Start()) 'Process start failed.'
        $started=$true
        $retainedHandle=$child.SafeHandle
        $child.StandardInput.Close();$observed.stdinClosed=$true
        $streams[0].pipe=$child.StandardOutput.BaseStream;$streams[1].pipe=$child.StandardError.BaseStream
        foreach($stream in $streams) { $stream.task=$stream.pipe.ReadAsync($stream.buffer,0,65536) }
        $observed.drainingBeforeImageObservation=$true
        $observed.pid=$child.Id;$observed.startTicks=$child.StartTime.ToUniversalTime().Ticks
        $observed.image=$child.MainModule.FileName
        Assert-Proof ($observed.image -ceq $hostPin.path -and !$retainedHandle.IsInvalid -and !$retainedHandle.IsClosed) 'Retained child custody.'
        $observed.custodyConfirmed=$true
    } catch { $observed.failure=[string]$_.Exception.Message }
    if($child -and $started) {
        while($true) {
            foreach($stream in $streams) {
                if($null -ne $stream.task -and $stream.task.IsCompleted) {
                    try {
                        $read=$stream.task.GetAwaiter().GetResult();$stream.task=$null
                        if(!$read) { $stream.eof=$true }
                        else {
                            $keep=[Math]::Min($read,1048576-$stream.bytes)
                            if($keep -gt 0) { $stream.log.Write($stream.buffer,0,$keep);$stream.bytes+=$keep }
                            Assert-Proof ($read -eq $keep) 'Raw output cap exceeded.'
                            $stream.task=$stream.pipe.ReadAsync($stream.buffer,0,[int][Math]::Min(65536,1048576-$stream.bytes+1))
                        }
                    } catch { $stream.failed=$true;if(!$observed.failure){$observed.failure=[string]$_.Exception.Message} }
                }
            }
            try {
                if($child.HasExited) { $observed.exitConfirmed=$true;$observed.exitCode=$child.ExitCode
                    if($child.ExitCode -ne 0 -and !$observed.failure){$observed.failure='Nonzero child exit.'} }
            } catch { if(!$observed.failure){$observed.failure=[string]$_.Exception.Message} }
            $observed.outputConfirmed=$streams.Count -eq 2 -and $streams[0].eof -eq $true -and $streams[1].eof -eq $true -and !$streams[0].failed -and !$streams[1].failed
            if(Test-RExternalDeadlineExceeded $executionClock.Elapsed.TotalSeconds $external) { $observed.timedOut=$true;if(!$observed.failure){$observed.failure='External exit/output deadline.'} }
            if($observed.exitConfirmed -and $observed.outputConfirmed) {
                if(!$observed.failure){$observed.timedOut=$false};break
            }
            if($observed.failure -and $null -eq $confirmation) {
                $confirmation=$executionClock.Elapsed.TotalSeconds
                if(!$observed.exitConfirmed) { $observed.killAttempts++;try{$child.Kill()}catch{$observed.failure+='; retained-child cleanup unconfirmed.'} }
            }
            if($null -ne $confirmation -and $executionClock.Elapsed.TotalSeconds-$confirmation -ge 5){break}
            [Threading.Thread]::Sleep(5)
        }
    }
    $end=$totalClock.Elapsed.TotalSeconds
    $terminalWindow=@{start=$end;deadline=(Get-RTerminalRetentionDeadline $end);previous=$end;attempted=$false}
    foreach($stream in $streams) { $observed[$stream.name+'Eof']=$stream.eof;$observed[$stream.name+'Bytes']=$stream.bytes }
    $bindings=@{exe=$hostPin.path;hostSha256=$hostPin.sha256;stage=$Mode;sourceManifestSha256=$configurationData.sourceManifest.sha256;
        configurationSha256=$ConfigurationSha256;sourceGateReference=$configurationData.approval.sourceGateReference;
        authorizationReference=$configurationData.approval.authorizationReference;authorizationSha256=$configurationData.approval.authorizationSha256}
    $writer=if($Mode -ceq 'Restage') { ${function:Write-RestageReceipt} } else { ${function:Write-NonCopyReceipt} }
    $writeTerminal={
        param([byte[]]$Bytes)
        & $writer -Path (Join-Path $outer 'terminal.json') -Bytes $Bytes -Window $terminalWindow -ReadClock {$totalClock.Elapsed.TotalSeconds} -Phase Terminal -ExternalSeconds $external
    }
    $verify={
        Assert-ROptionalPublicationAdmission $executionClock.Elapsed.TotalSeconds $external
        foreach($stream in $streams){$stream.log.Flush($true);$stream.log.Dispose()}
        Assert-Proof ($streams[1].bytes -eq 0) 'Unexpected stderr.'
        $report=Read-ProofJsonFile $streams[0].path 1048576
        $admission={Assert-ROptionalPublicationAdmission $executionClock.Elapsed.TotalSeconds $external}
        $rows=@(Confirm-ProofChildResult $configurationData $ConfigurationSha256 $Mode $NewGuiId $report $admission)
        foreach($stream in $streams){$rows+=Get-ProofSupervisionPin $stream.path $admission}
        $null=Assert-ProofSource $configurationData $root
        return @{passed=$true;rows=$rows;details=@{result=$report}}
    }
    $writeInventory={
        param([byte[]]$Bytes)
        Write-ProofInventory (Join-Path $outer 'inventory.json') $Bytes {$executionClock.Elapsed.TotalSeconds} $external
    }
    $writeReceipt={
        param([byte[]]$Bytes)
        $start=$totalClock.Elapsed.TotalSeconds
        $finalWindow=@{terminalPrevious=$terminalWindow.previous;start=$start;deadline=(Get-RReceiptRetentionDeadline $terminalWindow.previous $start);previous=$start;attempted=$false}
        & $writer -Path (Join-Path $outer 'receipt.json') -Bytes $Bytes -Window $finalWindow -ReadClock {$totalClock.Elapsed.TotalSeconds} -Phase Final -ExternalSeconds $external
    }
    try {
        $result=Invoke-RTerminalPublication -Bindings $bindings -Observations $observed -WriteTerminal $writeTerminal -VerifyOutput $verify -WriteInventory $writeInventory -WriteReceipt $writeReceipt
        $result | ConvertTo-Json -Depth 32 -Compress
    } finally {
        foreach($stream in $streams){$stream.log.Dispose();if($stream.pipe){$stream.pipe.Dispose()}}
        if($child){$child.Dispose()}
    }
}
if($MyInvocation.InvocationName -ne '.') {
    Assert-Proof ($args.Count -eq 0 -and $Mode -and $Configuration -and $ConfigurationSha256) 'Declared mode/configuration arguments required.'
    Invoke-ProofSupervision @PSBoundParameters
}
