[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$clock=[Diagnostics.Stopwatch]::StartNew()
. (Join-Path $PSScriptRoot 'RestagePolicy.ps1')
$RestagerSourceManifestSha256='f'*64
$SourceGateReference='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-9000000001'
$AuthorizationReference='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-9000000002'
# All pins, IDs and installed metadata below are synthetic in-memory test data.
$p = '33333333333333333333333333333333'
# Test strings are not allocated run IDs and are never used for filesystem access.
$n = '0123456789abcdef0123456789abcdef'
$sha = 'a' * 64
$row = [ordered]@{path='a.dll';bytes=3L;sha256=$sha}
$receipt = [ordered]@{
    stage='Inputs';runId=$p;passed=$true;failure=$null
    sourceManifestSha256='b';helperSourceManifestSha256='d';toolMetadataSha256='m'
    sourceGateReference='gate';authorizationReference='grant'
    before=@{head='head';tree='tree';clean=$true};after=@{head='head';tree='tree';clean=$true}
}
$binding = @{stage='Inputs';runId=$p;sourceManifestSha256='b';helperSourceManifestSha256='d';toolMetadataSha256='m';sourceGateReference='gate';authorizationReference='grant';head='head';tree='tree'}
$outerSample=@{passed=$true;exitConfirmed=$true;outputConfirmed=$true;environmentCleared=$true;stdinClosed=$true;drainingBeforeImageObservation=$true;timedOut=$false;killAttempts=0;exitCode=0;failure=$null;exe='host';image='host';hostSha256=$sha;pid=1;startTicks=1L}
$environmentSample=@{
    HOME='C:\owned\profile';USERPROFILE='C:\owned\profile';APPDATA='C:\owned\profile\AppData\Roaming'
    LOCALAPPDATA='C:\owned\profile\AppData\Local';TEMP='C:\owned\scratch';TMP='C:\owned\scratch'
    SystemRoot='C:\Windows';WINDIR='C:\Windows';ComSpec='C:\Windows\System32\cmd.exe'
    PATH='C:\ExampleHost;C:\Windows\System32';POWERSHELL_TELEMETRY_OPTOUT='1';POWERSHELL_UPDATECHECK='Off'
    PSModulePath='C:\ExampleHost\Modules'
    PATHEXT='.EXE;.CPL'
}
function Clone($x) { return ConvertFrom-RJson ($x | ConvertTo-Json -Depth 32 -Compress) }
function ChangedReceipt([string]$key,$value) { $r=Clone $receipt; $r[$key]=$value; Assert-RPriorReceipt $r $binding }
function ChangedCopy([string]$key,$value) {
    $x=@{expectedBytes=3L;readBytes=3L;writtenBytes=3L;sourceBefore=$sha;sourceAfter=$sha;destination=$sha;expectedSha=$sha;createdNew=$true}
    $x[$key]=$value; Assert-RCopy $x
}
function New-TestPayload {
    $apps=[Collections.Generic.List[object]]::new()
    $literal='<root><item><key>Language</key><value>en</value></item><item><key>func_auto_update</key><value>0</value></item></root>'
    $configBytes=[Text.Encoding]::UTF8.GetByteCount($literal)
    $remaining=131L-$configBytes
    for($i=0;$i -lt 4;$i++) {
        $bytes=[Math]::Min(1L,$remaining);$remaining-=$bytes
        $apps.Add([ordered]@{path="file-$i.dll";bytes=$bytes;sha256=$sha})
    }
    $apps.Add([ordered]@{path='config\config.xml';bytes=$configBytes;sha256='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'})
    $names=@('System.Private.Windows.Core.dll','System.Private.Windows.GdiPlus.dll','WindowsBase.dll','UIAutomationTypes.dll','UIAutomationClient.dll','Microsoft.Win32.SystemEvents.dll','System.Drawing.Common.dll')
    $lengths=@(1,1,1,1,1,1,1)
    $runtime=@(for($i=0;$i -lt 7;$i++){[ordered]@{path=$names[$i];bytes=$lengths[$i];sha256=$sha}})
    $pins=@(for($i=0;$i -lt 7;$i++){[ordered]@{path=('C:\ExampleHost\'+$names[$i]);bytes=$lengths[$i];sha256=$sha}})
    $refs=@(for($i=0;$i -lt 26;$i++){[ordered]@{path="C:\ExampleHost\ref\ref-$i.dll";bytes=0;sha256=$sha}})
    $m=[ordered]@{
        schema='windows-desktop-bounded-input-v1';applicationSource='2222222222222222222222222222222222222222';runId=$p
        syntheticRomFormat='fe8u-synthetic-huffman-v1'
        powershellHost=@{path='C:\ExampleHost\pwsh.exe';sha256='cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc'}
        appFiles=$apps.ToArray()
        fixtures=@(
            [ordered]@{path='zipdb-invalid.zip';bytes=167;sha256='dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd'}
            [ordered]@{path='zipdb-proof.gba';bytes=16777216;sha256='eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee'}
            [ordered]@{path='zipdb-valid.zip';bytes=394;sha256='ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff'}
        )
        runtimeAssemblies=$runtime;compileReferences=$refs
        installedFiles=@(
            [ordered]@{bytes=118;path='proof\PATCH_offline.txt';sha256='0000000000000000000000000000000000000000000000000000000000000000'}
            [ordered]@{bytes=2;path='proof\payload.bin';sha256='1111111111111111111111111111111111111111111111111111111111111111'}
        )
    }
    return @{manifest=$m;metadata=@{runtimeAssemblies=$pins;compileReferences=$refs}}
}

$sample=New-TestPayload
$script:RestageExpected=@{priorId=$p;sourceGate=$SourceGateReference;authorization=$AuthorizationReference;
    systemRoot='C:\Windows';hostRoot='C:\ExampleHost';payload=(Clone $sample.manifest);files=605;bytes=656346042L}
$trxText='<?xml version="1.0" encoding="utf-8"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary><Counters total="4" executed="4" passed="4" failed="0" /></ResultSummary></TestRun>'
$bom=[string][char]0xfeff
function Assert-TestInventory([object[]]$Rows,[int]$Count) {
    $json=ConvertTo-RInventoryJson -Rows $Rows
    Assert-R ($json -is [string] -and $json.StartsWith('[') -and $json.EndsWith(']')) 'Inventory root must be an explicit array.'
    $parsed=ConvertFrom-RJson $json
    Assert-R ($parsed -is [array] -and $parsed.Count -eq $Count) 'Parsed inventory array shape/count.'
    for($i=0;$i -lt $Count;$i++) { Assert-REqual $Rows[$i] $parsed[$i] 'inventory row order/content' }
    if(!$Count) { Assert-R ($json -ceq '[]') 'Empty inventory disappeared.' }
}
function Invoke-TestPublication([string]$FailureAt='',[object[]]$Rows=@(),$Changes=@{},[switch]$Unknown,[switch]$WithoutVerifier,[scriptblock]$CompleteReceipt) {
    $memory=@{events=[Collections.Generic.List[string]]::new();terminal=$null;inventory=$null;receipt=$null;result=$null;error=$null;completionInput=$null}
    $observations=if($Unknown){@{}}else{
        @{pid=1;startTicks=1L;image='host';exitCode=0;exitConfirmed=$true;outputConfirmed=$true
          timedOut=$false;killAttempts=0;environmentCleared=$true;stdinClosed=$true
          drainingBeforeImageObservation=$true;failure=$null}
    }
    foreach($key in $Changes.Keys) { $observations[$key]=$Changes[$key] }
    $bindings=[ordered]@{schema='test-outer';stage='Pure';exe='host';hostSha256=$sha}
    if($FailureAt -ceq 'terminal-format') { $bindings.padding='x'*1048576 }
    if($FailureAt -ceq 'binding-collision') { $bindings.exitCode=0 }
    if($FailureAt -ceq 'completion-binding-collision') { $bindings.receiptCompletionFailure=$null }
    $arguments=@{
        Bindings=$bindings;Observations=$observations
        WriteTerminal={
            param([byte[]]$Bytes)
            $memory.events.Add('terminal')
            if($FailureAt -ceq 'terminal-write') { throw 'Injected terminal publication failure.' }
            $memory.terminal=$Bytes.Clone()
            return ($FailureAt -cne 'terminal-ack')
        }
        WriteInventory={
            param([byte[]]$Bytes)
            $memory.events.Add('inventory')
            if($FailureAt -ceq 'inventory-write') { throw 'Injected inventory publication failure.' }
            $memory.inventory=$Bytes.Clone()
            return ($FailureAt -cne 'inventory-ack')
        }
        WriteReceipt={
            param([byte[]]$Bytes)
            $memory.events.Add('receipt')
            if($FailureAt -ceq 'receipt-write') { throw 'Injected receipt publication failure.' }
            $memory.receipt=$Bytes.Clone()
            return ($FailureAt -cne 'receipt-ack')
        }
    }
    if(!$WithoutVerifier) {
        $arguments.VerifyOutput={
            $memory.events.Add('verify')
            Assert-R ($null -ne $memory.terminal) 'Verification ran before terminal publication.'
            if($FailureAt -ceq 'verify') { throw 'Injected output verification failure.' }
            if($FailureAt -ceq 'verify-shape') { return @() }
            if($FailureAt -ceq 'inventory-format') { return @{passed=$true;rows=@(@{text='x'*1048576});details=@{}} }
            if($FailureAt -ceq 'receipt-format') { return @{passed=$true;rows=$Rows;details=@{padding='x'*1048576}} }
            return @{passed=($FailureAt -cne 'verify-negative');rows=$Rows;details=@{kind='in-memory'}}
        }
    }
    if($null -ne $CompleteReceipt) {
        $testCompleter=$CompleteReceipt
        $bindings.testStatic=@{value='static';rows=@(@{value='nested'})}
        $arguments.CompleteReceipt={
            param([Collections.IDictionary]$Receipt)
            $memory.events.Add('complete')
            Assert-R ($null -ne $memory.terminal) 'Completion ran before terminal publication.'
            $memory.completionInput=ConvertTo-RPublicationBytes $Receipt
            $testCompletionOutput=& $testCompleter -Receipt $Receipt
            return ,$testCompletionOutput
        }
    }
    try { $memory.result=Invoke-RTerminalPublication @arguments } catch { $memory.error=[string]$_.Exception.Message }
    return $memory
}
function Assert-TestRetentionPublication([double]$External=100,[double]$Execution=105,[double]$End=105,[double]$Now=105,[string]$FailureAt='',[switch]$Unknown,[switch]$FailedTerminal) {
    $memory=@{events=[Collections.Generic.List[string]]::new();terminal=$null;error=$null;deadline=(Get-RTerminalRetentionDeadline $End);previous=$End}
    $observed=if($Unknown){@{}}else{@{pid=1;startTicks=1L;image='host';exitCode=0;exitConfirmed=$true;outputConfirmed=$true;timedOut=$false;killAttempts=0;environmentCleared=$true;stdinClosed=$true;drainingBeforeImageObservation=$true;stdoutEof=$true;stderrEof=$true}}
    if($FailedTerminal) { $observed.failure='Observed timeout.';$observed.timedOut=$true;$observed.killAttempts=1;$observed.exitCode=$null;$observed.exitConfirmed=$null }
    $write={
        param([byte[]]$Bytes)
        $memory.events.Add('terminal')
        Assert-RTerminalRetentionAdmission $End $memory.deadline $memory.previous $Now $Bytes.Length
        $memory.previous=$Now
        $memory.terminal=$Bytes.Clone()
        if($FailureAt -ceq 'overrun') { Assert-RTerminalRetentionAdmission $End $memory.deadline $memory.previous $memory.deadline $Bytes.Length }
        if($FailureAt -ceq 'throw') { throw 'Terminal acknowledgment failure.' }
        if($FailureAt -ceq 'false') { return $false }
        if($FailureAt -ceq 'missing') { return }
        if($FailureAt -ceq 'extra') { $true;$true;return }
        return $true
    }
    $verify={ $memory.events.Add('verify');Assert-ROptionalPublicationAdmission $Execution $External;throw 'Injected optional log closure failure.' }
    $complete={ param($Receipt) $memory.events.Add('complete');Assert-ROptionalPublicationAdmission $Execution $External;throw 'Injected optional completion failure.' }
    $receipt={ param([byte[]]$Bytes) $memory.events.Add('receipt');Assert-ROptionalPublicationAdmission $Execution $External;throw 'Injected optional receipt failure.' }
    try {
        $null=Invoke-RTerminalPublication -Bindings @{exe='host';hostSha256=$sha} -Observations $observed -WriteTerminal $write -VerifyOutput $verify -WriteInventory {throw 'Must not run.'} -CompleteReceipt $complete -WriteReceipt $receipt
    } catch { $memory.error=[string]$_.Exception.Message }
    Assert-R ($null -ne $memory.error -and $null -ne $memory.terminal) 'Failure must retain the exact terminal snapshot.'
    $terminal=Read-TestPublication $memory.terminal
    Assert-R (!$terminal.passed -and $terminal.terminalEvidenceOnly) 'Retention cannot preapprove success.'
    if($Unknown) { foreach($name in @('pid','exitCode','exitConfirmed','outputConfirmed','timedOut','killAttempts','stdoutEof')) { Assert-R ($null -eq $terminal[$name]) 'Unknown observation reconstructed.' } }
    if($FailedTerminal) { Assert-R ($terminal.failure -ceq 'Observed timeout.' -and $terminal.timedOut -and $terminal.killAttempts -eq 1 -and $null -eq $terminal.exitCode) 'Original failure observations lost.' }
    $expected=if($FailureAt){@('terminal')}elseif($Unknown -or $FailedTerminal){@('terminal','complete','receipt')}else{@('terminal','verify','complete','receipt')}
    Assert-REqual $memory.events.ToArray() $expected 'retention precedes all optional work; failed acknowledgment stops publication'
}
function Read-TestPublication([byte[]]$Bytes) {
    Assert-R ($null -ne $Bytes) 'Missing in-memory publication.'
    return ConvertFrom-RJson ([Text.UTF8Encoding]::new($false,$true).GetString($Bytes))
}
function Assert-TestPublicationFailure($Memory,[string[]]$Events,[bool]$ReceiptExpected=$true) {
    Assert-R (![string]::IsNullOrEmpty($Memory.error) -and $null -eq $Memory.result) 'Publication failure must throw, not return success.'
    Assert-REqual $Memory.events.ToArray() $Events 'failure publication order'
    if($ReceiptExpected) {
        $r=Read-TestPublication $Memory.receipt
        Assert-R ($r.passed -is [bool] -and !$r.passed -and $null -ne $r.failure) 'A failed final receipt must not look successful.'
    }
}
function Assert-TestPublicationSuccess([object[]]$Rows) {
    $x=Invoke-TestPublication -Rows $Rows
    Assert-R ($null -eq $x.error -and $x.result.passed) 'Expected successful in-memory orchestration.'
    Assert-REqual $x.events.ToArray() @('terminal','verify','inventory','receipt') 'successful publication order'
    $terminal=Read-TestPublication $x.terminal;$receipt=Read-TestPublication $x.receipt
    Assert-R (!$terminal.passed -and $terminal.terminalEvidenceOnly -and $terminal.exitCode -eq 0) 'Observed terminal evidence cannot preapprove output.'
    Assert-R ($receipt.passed -and $receipt.terminalPublicationConfirmed -and $receipt.outputVerificationConfirmed -and $receipt.inventoryPublicationConfirmed) 'Missing final confirmations.'
    Assert-R ($receipt.terminalEvidenceSha256 -ceq [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($x.terminal)).ToLowerInvariant()) 'Terminal digest chain.'
    Assert-R ($receipt.outputInventorySha256 -ceq [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($x.inventory)).ToLowerInvariant()) 'Inventory digest chain.'
    $inventory=Read-TestPublication $x.inventory
    Assert-R ($inventory -is [array] -and $inventory.Count -eq $Rows.Count) 'Orchestrated inventory array shape.'
}
$cases = @(
    @{name='ids-distinct';reject=$false;run={Assert-RIds $p $n}}
    @{name='ids-equal';reject=$true;run={Assert-RIds $p $p}}
    @{name='ids-wrong-prior';reject=$true;run={Assert-RIds $n $p}}
    @{name='ids-uppercase';reject=$true;run={Assert-RIds $p $n.ToUpperInvariant()}}
    @{name='ids-short';reject=$true;run={Assert-RIds $p 'abc'}}
    @{name='ids-long';reject=$true;run={Assert-RIds $p ($n+'0')}}
    @{name='ids-nonhex';reject=$true;run={Assert-RIds $p ('z'*32)}}
    @{name='ids-empty';reject=$true;run={Assert-RIds $p ''}}
    @{name='grant-distinct';reject=$false;run={Assert-RGrant 'https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-9000000001' 'https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-9000000002'}}
    @{name='grant-reused';reject=$true;run={Assert-RGrant $SourceGateReference $SourceGateReference}}
    @{name='grant-old-gui';reject=$true;run={Assert-RGrant $SourceGateReference 'https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-9000000003'}}
    @{name='grant-old-inputs';reject=$true;run={Assert-RGrant $SourceGateReference 'https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-9000000004'}}
    @{name='grant-other-repo';reject=$true;run={Assert-RGrant $SourceGateReference 'https://github.com/other/repo/issues/1#issuecomment-9000000002'}}
    @{name='json-valid';reject=$false;run={$null=ConvertFrom-RJson '{"a":[1,2],"b":true}'}}
    @{name='json-duplicate';reject=$true;run={$null=ConvertFrom-RJson '{"a":1,"a":2}'}}
    @{name='json-case-collision';reject=$true;run={$null=ConvertFrom-RJson '{"a":1,"A":2}'}}
    @{name='json-malformed';reject=$true;run={$null=ConvertFrom-RJson '{"a":'}}
    @{name='json-unknown-field';reject=$true;run={Assert-RKeys @{a=1;b=2} @('a')}}
    @{name='json-missing-field';reject=$true;run={Assert-RKeys @{a=1} @('a','b')}}
    @{name='path-safe';reject=$false;run={Assert-RRelative 'config\data\file.txt'}}
    @{name='path-absolute';reject=$true;run={Assert-RRelative 'C:\file'}}
    @{name='path-drive-relative';reject=$true;run={Assert-RRelative 'C:file'}}
    @{name='path-unc';reject=$true;run={Assert-RRelative '\\host\share\file'}}
    @{name='path-device';reject=$true;run={Assert-RRelative '\\?\C:\file'}}
    @{name='path-ads';reject=$true;run={Assert-RRelative 'a:stream'}}
    @{name='path-forward-slash';reject=$true;run={Assert-RRelative 'a/b'}}
    @{name='path-dot';reject=$true;run={Assert-RRelative 'a\.\b'}}
    @{name='path-parent';reject=$true;run={Assert-RRelative 'a\..\b'}}
    @{name='path-empty-component';reject=$true;run={Assert-RRelative 'a\\b'}}
    @{name='path-trailing-dot';reject=$true;run={Assert-RRelative 'a.\b'}}
    @{name='path-trailing-space';reject=$true;run={Assert-RRelative 'a \b'}}
    @{name='path-reserved';reject=$true;run={Assert-RRelative 'aux.txt'}}
    @{name='path-control';reject=$true;run={Assert-RRelative "a`nb"}}
    @{name='path-wildcard';reject=$true;run={Assert-RRelative 'a*'}}
    @{name='path-patch2';reject=$true;run={Assert-RRelative 'config\patch2\db.txt'}}
    @{name='path-import-state';reject=$true;run={Assert-RRelative '.patch2-import\state'}}
    @{name='path-old-run';reject=$true;run={Assert-RRelative ('run-'+$p+'\app\a')}}
    @{name='path-fixture-name';reject=$false;run={Assert-RRelative 'zipdb-invalid.zip'}}
    @{name='rows-valid';reject=$false;run={Assert-RRows @($row) 1 3 3}}
    @{name='rows-missing';reject=$true;run={Assert-RRows @() 1 3 3}}
    @{name='rows-extra';reject=$true;run={Assert-RRows @($row,$row) 1 3 3}}
    @{name='rows-duplicate';reject=$true;run={Assert-RRows @($row,$row) 2 6 3}}
    @{name='rows-case-collision';reject=$true;run={$x=Clone $row;$x.path='A.dll';Assert-RRows @($row,$x) 2 6 3}}
    @{name='rows-byte-overflow';reject=$true;run={Assert-RRows @($row) 1 2 3}}
    @{name='rows-file-overflow';reject=$true;run={Assert-RRows @($row) 1 3 2}}
    @{name='rows-bad-sha';reject=$true;run={$x=Clone $row;$x.sha256='bad';Assert-RRows @($x) 1 3 3}}
    @{name='rows-negative-bytes';reject=$true;run={$x=Clone $row;$x.bytes=-1;Assert-RRows @($x) 1 3 3}}
    @{name='rows-fractional-bytes';reject=$true;run={$x=Clone $row;$x.bytes=3.5;Assert-RRows @($x) 1 3 4}}
    @{name='receipt-valid';reject=$false;run={Assert-RPriorReceipt $receipt $binding}}
    @{name='receipt-wrong-stage';reject=$true;run={ChangedReceipt 'stage' 'GUI'}}
    @{name='receipt-wrong-id';reject=$true;run={ChangedReceipt 'runId' $n}}
    @{name='receipt-failed';reject=$true;run={ChangedReceipt 'passed' $false}}
    @{name='receipt-string-pass';reject=$true;run={ChangedReceipt 'passed' 'true'}}
    @{name='receipt-wrong-b';reject=$true;run={ChangedReceipt 'sourceManifestSha256' 'x'}}
    @{name='receipt-wrong-d';reject=$true;run={ChangedReceipt 'helperSourceManifestSha256' 'x'}}
    @{name='receipt-wrong-metadata';reject=$true;run={ChangedReceipt 'toolMetadataSha256' 'x'}}
    @{name='receipt-wrong-gate';reject=$true;run={ChangedReceipt 'sourceGateReference' 'x'}}
    @{name='receipt-wrong-grant';reject=$true;run={ChangedReceipt 'authorizationReference' 'x'}}
    @{name='receipt-before-after';reject=$true;run={$x=Clone $receipt;$x.after.tree='bad';Assert-RPriorReceipt $x $binding}}
    @{name='receipt-dirty-source';reject=$true;run={$x=Clone $receipt;$x.before.clean=$false;Assert-RPriorReceipt $x $binding}}
    @{name='receipt-refused-gui';reject=$true;run={Assert-RPriorReceipt @{stage='GUI';run_id=$p;passed=$false} $binding}}
    @{name='payload-valid';reject=$false;run={$x=New-TestPayload;Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-app-missing';reject=$true;run={$x=New-TestPayload;$x.manifest.appFiles=$x.manifest.appFiles[0..3];Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-wrong-config';reject=$true;run={$x=New-TestPayload;$x.manifest.appFiles[4].sha256=$sha;Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-wrong-fixture-size';reject=$true;run={$x=New-TestPayload;$x.manifest.fixtures[0].bytes=168;Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-wrong-fixture-sha';reject=$true;run={$x=New-TestPayload;$x.manifest.fixtures[0].sha256=$sha;Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-wrong-fixture-name';reject=$true;run={$x=New-TestPayload;$x.manifest.fixtures[0].path='other.zip';Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-runtime-order';reject=$true;run={$x=New-TestPayload;$x.manifest.runtimeAssemblies[0].path='other.dll';Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-runtime-metadata';reject=$true;run={$x=New-TestPayload;$x.metadata.runtimeAssemblies[0].sha256='b'*64;Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-compiler-missing';reject=$true;run={$x=New-TestPayload;$x.manifest.compileReferences=@();Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-installed-count';reject=$true;run={$x=New-TestPayload;$x.manifest.installedFiles=@();Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-installed-duplicate';reject=$true;run={$x=New-TestPayload;$x.manifest.installedFiles[1].path='proof\PATCH_offline.txt';Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-installed-sha';reject=$true;run={$x=New-TestPayload;$x.manifest.installedFiles[1].sha256=$sha;Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-extra-field';reject=$true;run={$x=New-TestPayload;$x.manifest.extra=1;Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-wrong-source';reject=$true;run={$x=New-TestPayload;$x.manifest.applicationSource='other';Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-wrong-host';reject=$true;run={$x=New-TestPayload;$x.manifest.powershellHost.path='C:\other\pwsh.exe';Assert-RPayload $x.manifest $x.metadata}}
    @{name='payload-wrong-format';reject=$true;run={$x=New-TestPayload;$x.manifest.syntheticRomFormat='header-only';Assert-RPayload $x.manifest $x.metadata}}
    @{name='delta-only-id';reject=$false;run={$a=[ordered]@{runId=$p;fixtures=@($row);value=$p};$b=Clone $a;$b.runId=$n;Assert-RDelta $a $b $p $n}}
    @{name='delta-global-replace';reject=$true;run={$a=[ordered]@{runId=$p;value=$p};$b=Clone $a;$b.runId=$n;$b.value=$n;Assert-RDelta $a $b $p $n}}
    @{name='delta-extra-field';reject=$true;run={$a=[ordered]@{runId=$p};$b=Clone $a;$b.runId=$n;$b.extra=1;Assert-RDelta $a $b $p $n}}
    @{name='delta-array-order';reject=$true;run={$a=[ordered]@{runId=$p;refs=@('one','two')};$b=Clone $a;$b.runId=$n;$b.refs=@('two','one');Assert-RDelta $a $b $p $n}}
    @{name='delta-config-mutation';reject=$true;run={$a=[ordered]@{runId=$p;config='normal'};$b=Clone $a;$b.runId=$n;$b.config='test';Assert-RDelta $a $b $p $n}}
    @{name='copy-valid';reject=$false;run={ChangedCopy 'writtenBytes' ([long]3)}}
    @{name='copy-short-read';reject=$true;run={ChangedCopy 'readBytes' ([long]2)}}
    @{name='copy-extra-read';reject=$true;run={ChangedCopy 'readBytes' ([long]4)}}
    @{name='copy-short-write';reject=$true;run={ChangedCopy 'writtenBytes' ([long]2)}}
    @{name='copy-source-before';reject=$true;run={ChangedCopy 'sourceBefore' ('b'*64)}}
    @{name='copy-source-after';reject=$true;run={ChangedCopy 'sourceAfter' ('b'*64)}}
    @{name='copy-destination-hash';reject=$true;run={ChangedCopy 'destination' ('b'*64)}}
    @{name='copy-create-new-conflict';reject=$true;run={ChangedCopy 'createdNew' $false}}
    @{name='state-copy-admitted';reject=$false;run={Assert-RAdmission @{failed=$false;completed=$false;files=0;bytes=0L} 0 300}}
    @{name='state-expired';reject=$true;run={Assert-RAdmission @{failed=$false;completed=$false;files=0;bytes=0L} 300 300}}
    @{name='state-after-failure';reject=$true;run={Assert-RAdmission @{failed=$true;completed=$false;files=0;bytes=0L} 1 300}}
    @{name='state-after-completion';reject=$true;run={Assert-RAdmission @{failed=$false;completed=$true;files=605;bytes=656346042L} 1 300}}
    @{name='state-file-overflow';reject=$true;run={Assert-RAdmission @{failed=$false;completed=$false;files=606;bytes=0L} 1 300}}
    @{name='state-byte-overflow';reject=$true;run={Assert-RAdmission @{failed=$false;completed=$false;files=0;bytes=656346043L} 1 300}}
    @{name='finish-valid';reject=$false;run={Assert-RFinish @{failed=$false;completed=$false;files=605;bytes=656346042L} $true $true 1}}
    @{name='finish-postcheck-missing';reject=$true;run={Assert-RFinish @{failed=$false;completed=$false;files=605;bytes=656346042L} $false $true 1}}
    @{name='finish-inventory-missing';reject=$true;run={Assert-RFinish @{failed=$false;completed=$false;files=605;bytes=656346042L} $true $false 1}}
    @{name='finish-copy-missing';reject=$true;run={Assert-RFinish @{failed=$false;completed=$false;files=604;bytes=656346042L} $true $true 1}}
    @{name='finish-deadline';reject=$true;run={Assert-RFinish @{failed=$false;completed=$false;files=605;bytes=656346042L} $true $true 300}}
    @{name='observation-source-reparse';reject=$true;run={Assert-RPlainObservation $true $true $false}}
    @{name='observation-destination-reparse';reject=$true;run={Assert-RPlainObservation $true $false $true}}
    @{name='observation-missing';reject=$true;run={Assert-RPlainObservation $false $false $false}}
    @{name='observation-plain';reject=$false;run={Assert-RPlainObservation $true $false $false}}
    @{name='fresh-inputs-consumed';reject=$true;run={Assert-RAbsentObservation $true}}
    @{name='fresh-run-consumed';reject=$true;run={Assert-RAbsentObservation $true}}
    @{name='fresh-launch-consumed';reject=$true;run={Assert-RAbsentObservation $true}}
    @{name='fresh-outer-consumed';reject=$true;run={Assert-RAbsentObservation $true}}
    @{name='fresh-claim-consumed';reject=$true;run={Assert-RAbsentObservation $true}}
    @{name='fresh-destination-absent';reject=$false;run={Assert-RAbsentObservation $false}}
    @{name='outer-valid';reject=$false;run={Assert-ROuter $outerSample 'host' $sha}}
    @{name='outer-timeout';reject=$true;run={$x=Clone $outerSample;$x.timedOut=$true;Assert-ROuter $x 'host' $sha}}
    @{name='outer-unconfirmed-exit';reject=$true;run={$x=Clone $outerSample;$x.exitConfirmed=$false;Assert-ROuter $x 'host' $sha}}
    @{name='outer-unconfirmed-output';reject=$true;run={$x=Clone $outerSample;$x.outputConfirmed=$false;Assert-ROuter $x 'host' $sha}}
    @{name='outer-one-kill-not-pass';reject=$true;run={$x=Clone $outerSample;$x.killAttempts=1;Assert-ROuter $x 'host' $sha}}
    @{name='outer-repeated-kill';reject=$true;run={$x=Clone $outerSample;$x.killAttempts=2;Assert-ROuter $x 'host' $sha}}
    @{name='outer-identity-refused';reject=$true;run={$x=Clone $outerSample;$x.image='wrong';Assert-ROuter $x 'host' $sha}}
    @{name='outer-missing-startticks';reject=$true;run={$x=Clone $outerSample;$x.startTicks=0;Assert-ROuter $x 'host' $sha}}
    @{name='environment-valid';reject=$false;run={Assert-REnvironment $environmentSample 'C:\owned'}}
    @{name='environment-unexpected-key';reject=$true;run={$x=Clone $environmentSample;$x.EXTRA='not-a-secret';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-unowned-home';reject=$true;run={$x=Clone $environmentSample;$x.HOME='C:\other';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-unpinned-path';reject=$true;run={$x=Clone $environmentSample;$x.PATH='C:\other';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-user-modules';reject=$true;run={$x=Clone $environmentSample;$x.PSModulePath='C:\other\Modules';Assert-REnvironment $x 'C:\owned'}}
    @{name='test-inventory-valid';reject=$false;run={Assert-RTestResult @{passed=$true;failed=0;skipped=0;cases=@('one','two');executed=2} @('one','two')}}
    @{name='test-inventory-skipped';reject=$true;run={Assert-RTestResult @{passed=$true;failed=0;skipped=1;cases=@('one');executed=1} @('one','two')}}
    @{name='test-inventory-unexpected';reject=$true;run={Assert-RTestResult @{passed=$true;failed=0;skipped=0;cases=@('one','extra');executed=2} @('one','two')}}
    @{name='environment-pathext-exact';reject=$false;run={$x=Clone $environmentSample;$x.PATHEXT='.EXE;.CPL';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-pathext-missing';reject=$true;run={$x=Clone $environmentSample;$null=$x.Remove('PATHEXT');Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-pathext-empty';reject=$true;run={$x=Clone $environmentSample;$x.PATHEXT='';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-pathext-arbitrary';reject=$true;run={$x=Clone $environmentSample;$x.PATHEXT='.EXE';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-pathext-extra';reject=$true;run={$x=Clone $environmentSample;$x.PATHEXT='.EXE;.CPL;.CMD';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-pathext-lowercase';reject=$true;run={$x=Clone $environmentSample;$x.PATHEXT='.exe;.cpl';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-modules-exact';reject=$false;run={$x=Clone $environmentSample;$x.PSModulePath='C:\ExampleHost\Modules';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-modules-owned-prefix';reject=$true;run={$x=Clone $environmentSample;$x.PSModulePath='C:\owned\profile\Documents\PowerShell\Modules;C:\ExampleHost\Modules';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-modules-system-prefix';reject=$true;run={$x=Clone $environmentSample;$x.PSModulePath='C:\OtherModules;C:\ExampleHost\Modules';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-modules-duplicate';reject=$true;run={$x=Clone $environmentSample;$x.PSModulePath='C:\ExampleHost\Modules;C:\ExampleHost\Modules';Assert-REnvironment $x 'C:\owned'}}
    @{name='environment-modules-empty';reject=$true;run={$x=Clone $environmentSample;$x.PSModulePath='';Assert-REnvironment $x 'C:\owned'}}
    @{name='trx-valid-no-bom';reject=$false;run={Assert-RHistoricalTrx $trxText}}
    @{name='trx-valid-one-bom';reject=$false;run={Assert-RHistoricalTrx ($bom+$trxText)}}
    @{name='trx-duplicate-leading-bom';reject=$true;run={Assert-RHistoricalTrx ($bom+$bom+$trxText)}}
    @{name='trx-bom-before-declaration-after-space';reject=$true;run={Assert-RHistoricalTrx (' '+$bom+$trxText)}}
    @{name='trx-bom-between-declaration-and-root';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('?><TestRun',('?>'+$bom+'<TestRun')))}}
    @{name='trx-bom-after-root';reject=$true;run={Assert-RHistoricalTrx ($trxText+$bom)}}
    @{name='trx-bom-in-legitimate-text';reject=$false;run={Assert-RHistoricalTrx ($trxText.Replace('</TestRun>',('<Description>a'+$bom+'b</Description></TestRun>')))}}
    @{name='trx-bom-in-legitimate-attribute';reject=$false;run={Assert-RHistoricalTrx ($trxText.Replace('<ResultSummary>',('<ResultSummary name="a'+$bom+'b">')))}}
    @{name='trx-legitimate-unicode-content';reject=$false;run={Assert-RHistoricalTrx ($trxText.Replace('</TestRun>','<Description>Δ 日本語 &amp; текст</Description></TestRun>'))}}
    @{name='trx-malformed-xml';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('</TestRun>','</Wrong>'))}}
    @{name='trx-leading-space-before-declaration';reject=$true;run={Assert-RHistoricalTrx (' '+$trxText)}}
    @{name='trx-prohibited-internal-dtd';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('?><TestRun','?><!DOCTYPE TestRun [<!ENTITY sample "text">]><TestRun'))}}
    @{name='trx-prohibited-external-entity';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('?><TestRun','?><!DOCTYPE TestRun [<!ENTITY sample SYSTEM "file:///not-read">]><TestRun').Replace('</TestRun>','<Description>&sample;</Description></TestRun>'))}}
    @{name='trx-prohibited-external-dtd';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('?><TestRun','?><!DOCTYPE TestRun SYSTEM "https://invalid.example/not-read"><TestRun'))}}
    @{name='trx-wrong-total';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('total="4"','total="5"'))}}
    @{name='trx-wrong-executed';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('executed="4"','executed="3"'))}}
    @{name='trx-wrong-passed';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('passed="4"','passed="3"'))}}
    @{name='trx-wrong-failed';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('failed="0"','failed="1"'))}}
    @{name='trx-missing-counters';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('<Counters total="4" executed="4" passed="4" failed="0" />',''))}}
    @{name='trx-missing-total';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('total="4" ',''))}}
    @{name='trx-missing-executed';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('executed="4" ',''))}}
    @{name='trx-missing-passed';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('passed="4" ',''))}}
    @{name='trx-missing-failed';reject=$true;run={Assert-RHistoricalTrx ($trxText.Replace('failed="0" ',''))}}
    @{name='inventory-json-empty-array';reject=$false;run={Assert-TestInventory @() 0}}
    @{name='inventory-json-single-array';reject=$false;run={Assert-TestInventory @($row) 1}}
    @{name='inventory-json-multiple-array';reject=$false;run={$other=Clone $row;$other.path='b.dll';Assert-TestInventory @($row,$other) 2}}
    @{name='inventory-json-empty-generic-list';reject=$false;run={$list=[Collections.Generic.List[object]]::new();Assert-TestInventory $list.ToArray() 0}}
    @{name='inventory-json-single-generic-list';reject=$false;run={$list=[Collections.Generic.List[object]]::new();$list.Add($row);Assert-TestInventory $list.ToArray() 1}}
    @{name='inventory-json-null-input';reject=$true;run={$null=ConvertTo-RInventoryJson -Rows $null}}
    @{name='inventory-json-null-row';reject=$true;run={$null=ConvertTo-RInventoryJson -Rows @($null)}}
    @{name='inventory-json-output-bound';reject=$true;run={$null=ConvertTo-RInventoryJson -Rows @(@{text='x'*1048576})}}
    @{name='terminal-success-empty-inventory';reject=$false;run={Assert-TestPublicationSuccess @()}}
    @{name='terminal-success-single-inventory';reject=$false;run={Assert-TestPublicationSuccess @($row)}}
    @{name='terminal-success-multiple-inventory';reject=$false;run={$other=Clone $row;$other.path='b.dll';Assert-TestPublicationSuccess @($row,$other)}}
    @{name='terminal-unknown-observations-stay-null';reject=$false;run={
        $x=Invoke-TestPublication -Unknown;Assert-TestPublicationFailure $x @('terminal','receipt')
        $t=Read-TestPublication $x.terminal;$r=Read-TestPublication $x.receipt
        foreach($key in @('pid','startTicks','image','exitCode','exitConfirmed','outputConfirmed','timedOut','killAttempts','environmentCleared','stdinClosed','drainingBeforeImageObservation','stdoutBytes','stderrBytes','stdoutEof','stderrEof','custodyConfirmed')) {
            Assert-R ($t.Contains($key) -and $null -eq $t[$key] -and $null -eq $r[$key]) "Unknown observation was reconstructed: $key"
        }
    }}
    @{name='terminal-observed-failure-preserved';reject=$false;run={
        $x=Invoke-TestPublication -Changes @{exitCode=7;failure='Observed child failure.'};Assert-TestPublicationFailure $x @('terminal','receipt')
        $t=Read-TestPublication $x.terminal;$r=Read-TestPublication $x.receipt
        Assert-R ($t.exitCode -eq 7 -and $r.exitCode -eq 7 -and $r.failure -ceq 'Observed child failure.') 'Observed failure lost.'
    }}
    @{name='terminal-timeout-never-pass';reject=$false;run={$x=Invoke-TestPublication -Changes @{timedOut=$true};Assert-TestPublicationFailure $x @('terminal','receipt')}}
    @{name='terminal-kill-never-pass';reject=$false;run={$x=Invoke-TestPublication -Changes @{killAttempts=1};Assert-TestPublicationFailure $x @('terminal','receipt')}}
    @{name='terminal-unconfirmed-exit-never-pass';reject=$false;run={$x=Invoke-TestPublication -Changes @{exitConfirmed=$null;exitCode=$null};Assert-TestPublicationFailure $x @('terminal','receipt')}}
    @{name='terminal-unconfirmed-output-never-pass';reject=$false;run={$x=Invoke-TestPublication -Changes @{outputConfirmed=$false};Assert-TestPublicationFailure $x @('terminal','receipt')}}
    @{name='terminal-before-verifier-failure';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'verify';Assert-TestPublicationFailure $x @('terminal','verify','receipt')
        $t=Read-TestPublication $x.terminal;Assert-R ($t.exitCode -eq 0 -and $t.exitConfirmed -and $t.killAttempts -eq 0 -and !$t.passed) 'Observed telemetry lost on verifier failure.'
    }}
    @{name='terminal-before-verifier-shape-failure';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'verify-shape';Assert-TestPublicationFailure $x @('terminal','verify','receipt')}}
    @{name='terminal-before-verifier-negative';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'verify-negative';Assert-TestPublicationFailure $x @('terminal','verify','receipt')}}
    @{name='terminal-before-inventory-format-failure';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'inventory-format';Assert-TestPublicationFailure $x @('terminal','verify','receipt')}}
    @{name='terminal-before-inventory-write-failure';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'inventory-write';Assert-TestPublicationFailure $x @('terminal','verify','inventory','receipt')}}
    @{name='terminal-inventory-ack-required';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'inventory-ack';Assert-TestPublicationFailure $x @('terminal','verify','inventory','receipt')}}
    @{name='terminal-write-failure-stops-verification';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'terminal-write';Assert-TestPublicationFailure $x @('terminal') $false}}
    @{name='terminal-ack-required-before-verification';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'terminal-ack';Assert-TestPublicationFailure $x @('terminal') $false}}
    @{name='terminal-format-failure-stops-verification';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'terminal-format';Assert-TestPublicationFailure $x @() $false}}
    @{name='terminal-final-write-failure-not-success';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'receipt-write';Assert-TestPublicationFailure $x @('terminal','verify','inventory','receipt') $false}}
    @{name='terminal-final-ack-failure-not-success';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'receipt-ack';Assert-TestPublicationFailure $x @('terminal','verify','inventory','receipt') $false}}
    @{name='terminal-no-verifier-not-success';reject=$false;run={$x=Invoke-TestPublication -WithoutVerifier;Assert-TestPublicationFailure $x @('terminal','receipt')}}
    @{name='terminal-binding-collision-refused';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'binding-collision';Assert-TestPublicationFailure $x @() $false}}
    @{name='terminal-string-confirmation-refused';reject=$false;run={$x=Invoke-TestPublication -Changes @{exitConfirmed='true'};Assert-TestPublicationFailure $x @() $false}}
    @{name='terminal-null-kill-count-not-zero';reject=$false;run={$x=Invoke-TestPublication -Changes @{killAttempts=$null};Assert-TestPublicationFailure $x @('terminal','receipt')}}
    @{name='inventory-json-depth-bound';reject=$true;run={
        $deep=@{value='leaf'};for($i=0;$i -lt 40;$i++) { $deep=@{child=$deep} }
        $null=ConvertTo-RInventoryJson -Rows @($deep)
    }}
    @{name='terminal-final-format-failure-not-success';reject=$false;run={$x=Invoke-TestPublication -FailureAt 'receipt-format';Assert-TestPublicationFailure $x @('terminal','verify','inventory') $false}}
    @{name='terminal-known-eof-failure-not-success';reject=$false;run={$x=Invoke-TestPublication -Changes @{stdoutEof=$false};Assert-TestPublicationFailure $x @('terminal','receipt')}}
    @{name='terminal-known-custody-failure-not-success';reject=$false;run={$x=Invoke-TestPublication -Changes @{custodyConfirmed=$false};Assert-TestPublicationFailure $x @('terminal','receipt')}}
    @{name='terminal-observed-failure-survives-reporting-failure';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'verify' -Changes @{exitCode=9;failure='Original failure.'};Assert-TestPublicationFailure $x @('terminal','receipt')
        $r=Read-TestPublication $x.receipt
        Assert-R ($r.failure -ceq 'Original failure.' -and $r.exitCode -eq 9 -and $null -ne $r.reportingFailure) 'Original evidence replaced by a reporting error.'
    }}
    @{name='receipt-completion-root-hash-exact-bytes';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{stdoutSha256=$sha;metadata=@{verified=$Receipt.outputVerificationConfirmed}}}
        Assert-R ($null -eq $x.error -and $x.result.passed) 'Enriched receipt did not complete.'
        Assert-REqual $x.events.ToArray() @('terminal','verify','inventory','complete','receipt') 'completion order'
        $t=Read-TestPublication $x.terminal;$r=Read-TestPublication $x.receipt
        Assert-R (!$t.Contains('stdoutSha256') -and $r.stdoutSha256 -ceq $sha -and $r.metadata.verified) 'Root enrichment timing/content.'
        $core=[Text.Encoding]::UTF8.GetString($x.completionInput);$written=[Text.Encoding]::UTF8.GetString($x.receipt)
        Assert-R ($written.StartsWith($core.Substring(0,$core.Length-1)+',',[StringComparison]::Ordinal)) 'Core serialized members changed.'
        Assert-REqual $x.result $r 'returned and exact published receipt'
        Assert-R ($r.terminalEvidenceSha256 -ceq [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($x.terminal)).ToLowerInvariant()) 'Enrichment changed terminal digest.'
    }}
    @{name='receipt-completion-omitted-legacy-shape';reject=$false;run={
        $x=Invoke-TestPublication;$r=Read-TestPublication $x.receipt
        Assert-R ($null -eq $x.error -and !$r.Contains('receiptCompletionFailure')) 'Absent callback changed legacy fields.'
        Assert-REqual $x.events.ToArray() @('terminal','verify','inventory','receipt') 'absent callback order'
    }}
    @{name='receipt-completion-empty-additions';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{}}
        Assert-R ($null -eq $x.error -and $x.result.passed) 'Empty dictionary is valid completion.'
        Assert-R ([Convert]::ToBase64String($x.completionInput) -ceq [Convert]::ToBase64String($x.receipt)) 'Empty additions changed core bytes.'
    }}
    @{name='receipt-completion-input-copy-top-level';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) $Receipt.passed=$false;$Receipt.exe='changed';$Receipt.exitCode=99;@{note='copied'}}
        $r=Read-TestPublication $x.receipt
        Assert-R ($null -eq $x.error -and $r.passed -and $r.exe -ceq 'host' -and $r.exitCode -eq 0 -and $r.note -ceq 'copied') 'Completion mutated core fields.'
    }}
    @{name='receipt-completion-input-copy-nested';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) $Receipt.outputVerification.kind='changed';$Receipt.testStatic.rows[0].value='changed';$Receipt.testStatic.value='changed';@{}}
        $r=Read-TestPublication $x.receipt
        Assert-R ($null -eq $x.error -and $r.outputVerification.kind -ceq 'in-memory' -and $r.testStatic.value -ceq 'static' -and $r.testStatic.rows[0].value -ceq 'nested') 'Nested completion input aliases core.'
    }}
    @{name='receipt-completion-collision-passed';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{passed=$true}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-collision-static';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{stage='replacement'}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-collision-observation';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{exitCode=7}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
        Assert-R ((Read-TestPublication $x.receipt).exitCode -eq 0) 'Observed exit code overwritten.'
    }}
    @{name='receipt-completion-collision-publication';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{outputInventorySha256='b'*64}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-collision-original-failure';reject=$false;run={
        $x=Invoke-TestPublication -Changes @{failure='Original failure.';exitCode=9} -CompleteReceipt {param($Receipt) @{failure=$null}}
        Assert-TestPublicationFailure $x @('terminal','complete','receipt')
        Assert-R ((Read-TestPublication $x.receipt).failure -ceq 'Original failure.') 'Original failure overwritten.'
    }}
    @{name='receipt-completion-collision-reporting-failure';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{reportingFailure=$null}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-collision-completion-failure';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{receiptCompletionFailure=$null}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-collision-case-insensitive';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{HOSTSHA256='b'*64}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-collision-all-core-fields';reject=$false;run={
        $baseline=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{}}
        foreach($testProtectedName in (Read-TestPublication $baseline.completionInput).Keys) {
            $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) $extra=[ordered]@{};$extra.Add($testProtectedName,$null);return $extra}
            Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
        }
    }}
    @{name='receipt-completion-addition-case-collision';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) $extra=[Collections.Specialized.OrderedDictionary]::new([StringComparer]::Ordinal);$extra.Add('newField',1);$extra.Add('NEWFIELD',2);return $extra}
        Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-atomic-rejection';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) [ordered]@{newField='not committed';passed=$true}}
        Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
        Assert-R (!(Read-TestPublication $x.receipt).Contains('newField')) 'Partially merged enrichment survived rejection.'
    }}
    @{name='receipt-completion-throws';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) throw 'Injected completion failure.'}
        Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
        Assert-R ((Read-TestPublication $x.receipt).receiptCompletionFailure -ceq 'Injected completion failure.') 'Completion failure not retained.'
    }}
    @{name='receipt-completion-null-shape';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) return $null};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-scalar-shape';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) return 'not a dictionary'};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-empty-array-shape';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) return ,@()};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-single-array-shape';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) return ,@(@{newField=1})};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-extra-pipeline-output';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) 'unexpected output';@{newField=1}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-nonstring-key';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{1='not a string key'}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-output-bound';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{padding='x'*1048576}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
        Assert-R (!(Read-TestPublication $x.receipt).Contains('padding')) 'Oversized enrichment leaked into failure receipt.'
    }}
    @{name='receipt-completion-depth-bound';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) $deep=@{value='leaf'};for($i=0;$i -lt 40;$i++){$deep=@{child=$deep}};@{extra=$deep}}
        Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-nested-case-collision';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) $extra=[Collections.Specialized.OrderedDictionary]::new([StringComparer]::Ordinal);$extra.Add('value',1);$extra.Add('VALUE',2);@{metadata=$extra}}
        Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
    }}
    @{name='receipt-completion-failed-terminal-metadata';reject=$false;run={
        $x=Invoke-TestPublication -Unknown -CompleteReceipt {param($Receipt) Assert-R (!$Receipt.passed -and $null -eq $Receipt.exitCode) 'Unknown terminal promoted.';@{stdoutSha256=$null;note='unconfirmed'}}
        Assert-TestPublicationFailure $x @('terminal','complete','receipt')
        $r=Read-TestPublication $x.receipt;Assert-R ($null -eq $r.stdoutSha256 -and $null -eq $r.exitCode -and $r.note -ceq 'unconfirmed') 'Unknown receipt enrichment fabricated evidence.'
    }}
    @{name='receipt-completion-failed-terminal-throws';reject=$false;run={
        $x=Invoke-TestPublication -Changes @{failure='Original failure.';exitCode=9} -CompleteReceipt {param($Receipt) throw 'Completion also failed.'}
        Assert-TestPublicationFailure $x @('terminal','complete','receipt')
        $r=Read-TestPublication $x.receipt
        Assert-R ($r.failure -ceq 'Original failure.' -and $r.reportingFailure -clike 'Terminal observations*' -and $r.receiptCompletionFailure -ceq 'Completion also failed.' -and $r.exitCode -eq 9) 'Prior failure channels were replaced.'
    }}
    @{name='receipt-completion-verifier-failure-preserved';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'verify' -CompleteReceipt {param($Receipt) throw 'Completion also failed.'}
        Assert-TestPublicationFailure $x @('terminal','verify','complete','receipt')
        $r=Read-TestPublication $x.receipt;Assert-R ($r.failure -ceq 'Injected output verification failure.' -and $r.reportingFailure -ceq $r.failure -and $r.receiptCompletionFailure -ceq 'Completion also failed.') 'Verification failure replaced.'
    }}
    @{name='receipt-completion-inventory-failure-preserved';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'inventory-write' -CompleteReceipt {param($Receipt) @{note='inventory failed'}}
        Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
        $r=Read-TestPublication $x.receipt;Assert-R (!$r.inventoryPublicationConfirmed -and $r.note -ceq 'inventory failed') 'Completion repaired inventory failure.'
    }}
    @{name='receipt-completion-terminal-write-failure-stops';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'terminal-write' -CompleteReceipt {param($Receipt) throw 'Must not run.'};Assert-TestPublicationFailure $x @('terminal') $false
    }}
    @{name='receipt-completion-terminal-ack-failure-stops';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'terminal-ack' -CompleteReceipt {param($Receipt) throw 'Must not run.'};Assert-TestPublicationFailure $x @('terminal') $false
    }}
    @{name='receipt-completion-terminal-format-failure-stops';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'terminal-format' -CompleteReceipt {param($Receipt) throw 'Must not run.'};Assert-TestPublicationFailure $x @() $false
    }}
    @{name='receipt-completion-receipt-write-failure';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'receipt-write' -CompleteReceipt {param($Receipt) @{stdoutSha256=$sha}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt') $false
    }}
    @{name='receipt-completion-receipt-ack-failure';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'receipt-ack' -CompleteReceipt {param($Receipt) @{stdoutSha256=$sha}};Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt') $false
    }}
    @{name='receipt-completion-mutated-input-then-throw';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) $Receipt.exitCode=7;$Receipt.failure='fake';$Receipt.testStatic.rows[0].value='fake';throw 'After input mutation.'}
        Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
        $r=Read-TestPublication $x.receipt;Assert-R ($r.exitCode -eq 0 -and $r.failure -ceq 'After input mutation.' -and $r.testStatic.rows[0].value -ceq 'nested') 'Thrown callback mutated original core.'
    }}
    @{name='receipt-completion-combined-output-bound';reject=$false;run={
        $x=Invoke-TestPublication -CompleteReceipt {param($Receipt) @{padding='x'*(1048576-32)}}
        Assert-TestPublicationFailure $x @('terminal','verify','inventory','complete','receipt')
        Assert-R ((Read-TestPublication $x.receipt).receiptCompletionFailure -ceq 'Completed receipt byte bound.') 'Combined receipt bound was not enforced.'
    }}
    @{name='receipt-completion-static-reserved-name-refused';reject=$false;run={
        $x=Invoke-TestPublication -FailureAt 'completion-binding-collision';Assert-TestPublicationFailure $x @() $false
    }}
    @{name='retention-deadline-zero';reject=$false;run={Assert-R ((Get-RTerminalRetentionDeadline 0) -eq 5) 'No-child total-clock deadline.'}}
    @{name='retention-deadline-pure-105';reject=$false;run={Assert-R ((Get-RTerminalRetentionDeadline 105) -eq 110) 'Pure reporting-only window.'}}
    @{name='retention-deadline-compatibility-315';reject=$false;run={Assert-R ((Get-RTerminalRetentionDeadline 315) -eq 320) 'Compatibility reporting-only window.'}}
    @{name='retention-deadline-negative';reject=$true;run={Get-RTerminalRetentionDeadline -1}}
    @{name='retention-deadline-nan';reject=$true;run={Get-RTerminalRetentionDeadline ([double]::NaN)}}
    @{name='retention-deadline-positive-infinity';reject=$true;run={Get-RTerminalRetentionDeadline ([double]::PositiveInfinity)}}
    @{name='retention-deadline-negative-infinity';reject=$true;run={Get-RTerminalRetentionDeadline ([double]::NegativeInfinity)}}
    @{name='retention-deadline-unrepresentable';reject=$true;run={Get-RTerminalRetentionDeadline ([double]::MaxValue)}}
    @{name='optional-pure-before-105';reject=$false;run={Assert-ROptionalPublicationAdmission 104.999 100}}
    @{name='optional-pure-equal-105';reject=$true;run={Assert-ROptionalPublicationAdmission 105 100}}
    @{name='optional-pure-after-105';reject=$true;run={Assert-ROptionalPublicationAdmission 105.001 100}}
    @{name='optional-compatibility-before-315';reject=$false;run={Assert-ROptionalPublicationAdmission 314.999 310}}
    @{name='optional-compatibility-equal-315';reject=$true;run={Assert-ROptionalPublicationAdmission 315 310}}
    @{name='optional-compatibility-after-315';reject=$true;run={Assert-ROptionalPublicationAdmission 315.001 310}}
    @{name='optional-negative-elapsed';reject=$true;run={Assert-ROptionalPublicationAdmission -1 100}}
    @{name='optional-nan-elapsed';reject=$true;run={Assert-ROptionalPublicationAdmission ([double]::NaN) 100}}
    @{name='optional-infinite-elapsed';reject=$true;run={Assert-ROptionalPublicationAdmission ([double]::PositiveInfinity) 100}}
    @{name='optional-invalid-external-bound';reject=$true;run={Assert-ROptionalPublicationAdmission 0 105}}
    @{name='retention-admits-exact-105';reject=$false;run={Assert-RTerminalRetentionAdmission 105 110 105 105 1048576}}
    @{name='retention-admits-exact-315';reject=$false;run={Assert-RTerminalRetentionAdmission 315 320 315 315 1048576}}
    @{name='retention-before-cutoff';reject=$false;run={Assert-RTerminalRetentionAdmission 105 110 109 109.999 1}}
    @{name='retention-equal-cutoff';reject=$true;run={Assert-RTerminalRetentionAdmission 105 110 109 110 1}}
    @{name='retention-after-cutoff';reject=$true;run={Assert-RTerminalRetentionAdmission 315 320 319 320.001 1}}
    @{name='retention-negative-now';reject=$true;run={Assert-RTerminalRetentionAdmission 0 5 0 -1 1}}
    @{name='retention-nan-now';reject=$true;run={Assert-RTerminalRetentionAdmission 0 5 0 ([double]::NaN) 1}}
    @{name='retention-infinite-now';reject=$true;run={Assert-RTerminalRetentionAdmission 0 5 0 ([double]::PositiveInfinity) 1}}
    @{name='retention-negative-previous';reject=$true;run={Assert-RTerminalRetentionAdmission 0 5 -1 1 1}}
    @{name='retention-nan-previous';reject=$true;run={Assert-RTerminalRetentionAdmission 0 5 ([double]::NaN) 1 1}}
    @{name='retention-infinite-previous';reject=$true;run={Assert-RTerminalRetentionAdmission 0 5 ([double]::PositiveInfinity) 1 1}}
    @{name='retention-before-observation';reject=$true;run={Assert-RTerminalRetentionAdmission 105 110 104 105 1}}
    @{name='retention-backward-now';reject=$true;run={Assert-RTerminalRetentionAdmission 105 110 107 106 1}}
    @{name='retention-equal-previous';reject=$false;run={Assert-RTerminalRetentionAdmission 105 110 107 107 1}}
    @{name='retention-deadline-extension-refused';reject=$true;run={Assert-RTerminalRetentionAdmission 105 111 105 105 1}}
    @{name='retention-deadline-reset-refused';reject=$true;run={Assert-RTerminalRetentionAdmission 105 115 109 109 1}}
    @{name='retention-nan-deadline';reject=$true;run={Assert-RTerminalRetentionAdmission 0 ([double]::NaN) 0 1 1}}
    @{name='retention-infinite-deadline';reject=$true;run={Assert-RTerminalRetentionAdmission 0 ([double]::PositiveInfinity) 0 1 1}}
    @{name='retention-negative-bytes';reject=$true;run={Assert-RTerminalRetentionAdmission 0 5 0 1 -1}}
    @{name='retention-oversize-bytes';reject=$true;run={Assert-RTerminalRetentionAdmission 0 5 0 1 1048577}}
    @{name='retention-expired-pure-optional-preserves-terminal';reject=$false;run={Assert-TestRetentionPublication}}
    @{name='retention-expired-compatibility-optional-preserves-terminal';reject=$false;run={Assert-TestRetentionPublication -External 310 -Execution 315 -End 315 -Now 315}}
    @{name='retention-no-child-unstarted-execution-clock';reject=$false;run={Assert-TestRetentionPublication -Execution 0 -End 12 -Now 12 -Unknown}}
    @{name='retention-early-failure-preserves-observations';reject=$false;run={Assert-TestRetentionPublication -Execution 1 -End 12 -Now 12 -FailedTerminal}}
    @{name='retention-timeout-kill-unknown-exit-preserved';reject=$false;run={Assert-TestRetentionPublication -FailedTerminal}}
    @{name='retention-log-finalization-failure-after-terminal';reject=$false;run={Assert-TestRetentionPublication -Execution 1 -End 12 -Now 12}}
    @{name='retention-false-ack-stops-optional';reject=$false;run={Assert-TestRetentionPublication -FailureAt 'false'}}
    @{name='retention-missing-ack-stops-optional';reject=$false;run={Assert-TestRetentionPublication -FailureAt 'missing'}}
    @{name='retention-extra-ack-stops-optional';reject=$false;run={Assert-TestRetentionPublication -FailureAt 'extra'}}
    @{name='retention-thrown-ack-stops-optional';reject=$false;run={Assert-TestRetentionPublication -FailureAt 'throw'}}
    @{name='retention-io-overrun-fails-after-bytes-retained';reject=$false;run={Assert-TestRetentionPublication -FailureAt 'overrun'}}
    @{name='retention-fractional-start-never-exceeds-five';reject=$false;run={
        $deadline=Get-RTerminalRetentionDeadline 3.8
        Assert-R ($deadline -gt 3.8 -and $deadline-3.8 -le 5) 'Floating-point deadline extended the window.'
        Assert-RTerminalRetentionAdmission 3.8 $deadline 3.8 3.8 1
    }}
)
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$done=[Collections.Generic.List[string]]::new()
foreach($case in $cases) {
    if($clock.Elapsed.TotalSeconds -ge 90) { throw 'Pure-test deadline; never retry.' }
    if(!$seen.Add($case.name)) { throw 'Duplicate test name.' }
    $rejected=$false
    try { & $case.run 3>$null } catch { $rejected=$true }
    if($rejected -ne $case.reject) { throw "Pure case failed: $($case.name). Never retry." }
    $done.Add($case.name)
}
$report=[ordered]@{
    schema='windows-desktop-restage-pure-v1';passed=$true;failed=0;skipped=0
    sourceManifestSha256=$RestagerSourceManifestSha256;sourceGateReference=$SourceGateReference
    authorizationReference=$AuthorizationReference;cases=$done.ToArray();executed=$done.Count
    nativeCalls=$false;appLaunched=$false;copiesPerformed=$false;actualEnvironmentVerified=$false
    startupModulePathNarrowed=$false
}
Assert-RTestResult $report @($cases.name)
if($clock.Elapsed.TotalSeconds -ge 90) { throw 'Pure-test terminal deadline.' }
Write-Output (ConvertTo-Json -InputObject $report -Depth 8 -Compress -ErrorAction Stop -WarningAction Stop)
