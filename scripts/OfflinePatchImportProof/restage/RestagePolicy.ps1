Set-StrictMode -Version Latest

function Write-RReportingFile {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][byte[]]$Bytes,
        [Parameter(Mandatory)][Collections.IDictionary]$Window,
        [Parameter(Mandatory)][scriptblock]$ReadClock,
        [ValidateSet('Terminal','Final')][string]$Phase,
        [double]$ExternalSeconds=100,
        [scriptblock]$CheckOperation={ param($Operation) $true })
    Assert-R (!$Window.attempted) 'Reporting publication is single-attempt.'
    $Window.attempted=$true
    # Each reporting phase has one window on the same running total clock.
    # Expired optional work cannot prevent retaining its failed final receipt.
    $check={
        param([string]$Operation)
        $now=[double](& $ReadClock)
        if($Phase -ceq 'Final'){
            Assert-R ($Window.Contains('terminalPrevious')) 'Final reporting requires the preceding total-clock observation.'
            Assert-RReceiptRetentionAdmission $Window.terminalPrevious $Window.start $Window.deadline $Window.previous $now $Bytes.Length
        }else{
            Assert-RTerminalRetentionAdmission $Window.start $Window.deadline $Window.previous $now $Bytes.Length
        }
        $Window.previous=$now
        $ack=& $CheckOperation $Operation
        Assert-R ($ack -is [bool] -and $ack) "Reporting operation unconfirmed: $Operation"
    }
    & $check 'admission'
    Assert-ProofPath ([IO.Path]::GetDirectoryName($Path)) -Existing
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {
        & $check 'write';$stream.Write($Bytes,0,$Bytes.Length)
        & $check 'flush';$stream.Flush($true)
    } finally { $stream.Dispose() }
    & $check 'close'
    & $check 'hash'
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant()
    $read=Read-ProofPinnedBytes @{path=$Path;bytes=$Bytes.Length;sha256=$hash} 1048576
    Assert-R ($read.Length -eq $Bytes.Length) 'Reporting readback length.'
    & $check 'acknowledgement'
    return $true
}

function Assert-R([bool]$Condition,[string]$Message) {
    if(!$Condition) { throw $Message }
}
function Assert-RInteger($Value,[long]$Minimum,[long]$Maximum) {
    Assert-R (($Value -is [int] -or $Value -is [long]) -and $Value -ge $Minimum -and $Value -le $Maximum) 'Integer observation required.'
}
function Assert-RKeys($Map,[string[]]$Names) {
    Assert-R ($Map -is [Collections.IDictionary]) 'Expected JSON object.'
    Assert-R ($Map.Count -eq $Names.Count) 'Object field count.'
    foreach($name in $Names) {
        Assert-R (@($Map.Keys | Where-Object { $_ -ceq $name }).Count -eq 1) "Missing/extraneous field: $name"
    }
}
function Assert-RJsonNode([Text.Json.JsonElement]$Node,[int]$Depth,[ref]$Nodes) {
    $Nodes.Value++
    Assert-R ($Depth -le 32 -and $Nodes.Value -le 50000) 'JSON structural bound.'
    if($Node.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach($prop in $Node.EnumerateObject()) {
            Assert-R ($seen.Add($prop.Name)) 'Duplicate/case-colliding JSON property.'
            Assert-RJsonNode $prop.Value ($Depth+1) $Nodes
        }
    } elseif($Node.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach($item in $Node.EnumerateArray()) { Assert-RJsonNode $item ($Depth+1) $Nodes }
    }
}
function ConvertFrom-RJson([string]$Text) {
    Assert-R ([Text.Encoding]::UTF8.GetByteCount($Text) -le 4194304) 'JSON byte bound.'
    $doc=[Text.Json.JsonDocument]::Parse($Text)
    try { $count=0; Assert-RJsonNode $doc.RootElement 0 ([ref]$count) } finally { $doc.Dispose() }
    return ,(ConvertFrom-Json -InputObject $Text -AsHashtable -NoEnumerate -Depth 32)
}
function Assert-REqual($Left,$Right,[string]$Label) {
    Assert-R (($Left | ConvertTo-Json -Depth 32 -Compress) -ceq ($Right | ConvertTo-Json -Depth 32 -Compress)) "Mismatch: $Label"
}

function Assert-RIds([string]$Prior,[string]$New) {
    Assert-R ($Prior -ceq $script:RestageExpected.priorId) 'Configured donor identity required.'
    Assert-R ($New -cmatch '^[0-9a-f]{32}$' -and $New -cne $Prior) 'Fresh distinct lower-hex ID required.'
}


function Assert-RGrant([string]$Gate,[string]$Grant) {
    Assert-R ($Gate -ceq $script:RestageExpected.sourceGate -and $Grant -ceq $script:RestageExpected.authorization -and $Gate -cne $Grant) 'Distinct caller-approved source and execution bindings required.'
}

function Assert-RRelative([string]$Path) {
    Assert-R (![string]::IsNullOrEmpty($Path) -and $Path.Length -le 500) 'Relative path length.'
    Assert-R ($Path -notmatch '[/:*?"<>|\x00-\x1f]') 'Unsafe path character.'
    foreach($part in $Path.Split('\')) {
        Assert-R ($part.Length -gt 0 -and $part -cne '.' -and $part -cne '..' -and $part -notmatch '[. ]$') 'Unsafe path component.'
        Assert-R ($part -notmatch '^(?i:CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)') 'Reserved Windows name.'
        Assert-R ($part -notmatch '^(?i:patch2|\.patch2-import.*|(?:run|launch|inputs)-[0-9a-f]{32})$') 'Forbidden patch/import/prior-run state.'
    }
}
function Assert-RRows([object[]]$Rows,[int]$Count,[long]$Bytes,[long]$Maximum) {
    Assert-R ($Rows.Count -eq $Count -and $Count -le 10000) 'Row count mismatch.'
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$total=0
    foreach($row in $Rows) {
        Assert-RKeys $row @('path','bytes','sha256')
        Assert-RRelative $row.path
        Assert-R ($seen.Add($row.path)) 'Duplicate/case-colliding row.'
        Assert-R (($row.bytes -is [int] -or $row.bytes -is [long]) -and $row.bytes -ge 0 -and $row.bytes -le $Maximum) 'Row length/type bound.'
        Assert-R ($row.sha256 -is [string] -and $row.sha256 -cmatch '^[0-9a-f]{64}$') 'Row SHA256.'
        $total+=$row.bytes
        Assert-R ($total -le $Bytes) 'Aggregate bytes exceeded.'
    }
    Assert-R ($total -eq $Bytes) 'Aggregate byte mismatch.'
}
function Assert-RPriorReceipt($Receipt,$Expected) {
    foreach($key in @('stage','runId','sourceManifestSha256','helperSourceManifestSha256','toolMetadataSha256','sourceGateReference','authorizationReference')) {
        Assert-R ($Receipt.Contains($key) -and $Receipt[$key] -is [string] -and $Receipt[$key] -ceq $Expected[$key]) "Prior receipt binding: $key"
    }
    Assert-R ($Receipt.passed -is [bool] -and $Receipt.passed -and $null -eq $Receipt.failure) 'Prior preparation must have passed.'
    foreach($name in @('before','after')) {
        $state=$Receipt[$name]
        Assert-R ($state.head -ceq $Expected.head -and $state.tree -ceq $Expected.tree -and $state.clean -is [bool] -and $state.clean) 'Historical source state mismatch.'
    }
    Assert-REqual $Receipt.before $Receipt.after 'historical before/after'
}
function Assert-ROuter($Outer,[string]$HostPath,[string]$HostSha) {
    Assert-RInteger $Outer.killAttempts 0 0;Assert-RInteger $Outer.exitCode 0 0
    Assert-RInteger $Outer.pid 1 ([int]::MaxValue);Assert-RInteger $Outer.startTicks 1 ([long]::MaxValue)
    foreach($key in @('passed','exitConfirmed','outputConfirmed','environmentCleared','stdinClosed','drainingBeforeImageObservation')) {
        Assert-R ($Outer[$key] -is [bool] -and $Outer[$key]) "Outer confirmation: $key"
    }
    Assert-R ($Outer.timedOut -is [bool] -and !$Outer.timedOut -and $Outer.killAttempts -eq 0 -and $Outer.exitCode -eq 0 -and $null -eq $Outer.failure) 'Outer failure/timeout/kill.'
    Assert-R ($Outer.exe -ceq $HostPath -and $Outer.image -ceq $HostPath -and $Outer.hostSha256 -ceq $HostSha -and $Outer.pid -gt 0 -and $Outer.startTicks -gt 0) 'Outer retained-process identity.'
}
function Assert-RDelta($Old,$New,[string]$Prior,[string]$Next) {
    Assert-RIds $Prior $Next
    Assert-R ($Old.runId -ceq $Prior -and $New.runId -ceq $Next) 'Manifest ID delta.'
    $copy=ConvertFrom-RJson ($New | ConvertTo-Json -Depth 32 -Compress)
    $copy.runId=$Prior
    Assert-REqual $Old $copy 'only runId may change; all row/field order preserved'
}

function Assert-RPayload($Manifest,$Metadata) {
    Assert-RKeys $Manifest @('schema','applicationSource','runId','syntheticRomFormat','powershellHost','appFiles','fixtures','runtimeAssemblies','compileReferences','installedFiles')
    Assert-R ($Manifest.schema -ceq 'windows-desktop-bounded-input-v1' -and $Manifest.runId -ceq $script:RestageExpected.priorId -and $Manifest.applicationSource -ceq $script:RestageExpected.payload.applicationSource -and $Manifest.syntheticRomFormat -ceq 'fe8u-synthetic-huffman-v1') 'Donor input contract.'
    Assert-RKeys $Manifest.powershellHost @('path','sha256')
    foreach($name in @('appFiles','runtimeAssemblies','fixtures','installedFiles')) {
        $expected=@($script:RestageExpected.payload[$name])
        Assert-RRows @($Manifest[$name]) $expected.Count ([long](($expected | Measure-Object bytes -Sum).Sum)) 268435456
        Assert-REqual @($Manifest[$name]) $expected "pinned payload $name"
    }
    Assert-REqual $Manifest.powershellHost $script:RestageExpected.payload.powershellHost 'host'
    Assert-R ($Manifest.fixtures.Count -eq 3 -and $Manifest.installedFiles.Count -eq 2) 'Fixed feature fixture shape.'
    Assert-REqual @($Manifest.fixtures.path) @('zipdb-invalid.zip','zipdb-proof.gba','zipdb-valid.zip') 'fixed fixture names/order'
    Assert-REqual $Manifest.compileReferences $script:RestageExpected.payload.compileReferences 'compiler references'
    Assert-R ($Metadata.runtimeAssemblies.Count -eq $Manifest.runtimeAssemblies.Count) 'Runtime metadata count.'
    for($i=0;$i -lt $Manifest.runtimeAssemblies.Count;$i++) {
        $row=$Manifest.runtimeAssemblies[$i];$pin=$Metadata.runtimeAssemblies[$i]
        $runtimePath=if($script:RestageExpected.hostRoot -cmatch '^[A-Z]:\\'){$script:RestageExpected.hostRoot+'\'+$row.path}else{Join-Path $script:RestageExpected.hostRoot $row.path}
        Assert-R ($pin.path -ceq $runtimePath -and $pin.bytes -eq $row.bytes -and $pin.sha256 -ceq $row.sha256) 'Runtime metadata pin.'
    }
    Assert-REqual $Manifest.compileReferences $Metadata.compileReferences 'compiler metadata'
}

function Assert-RPlainObservation([bool]$Exists,[bool]$Reparse,[bool]$AncestorReparse) {
    Assert-R ($Exists -and !$Reparse -and !$AncestorReparse) 'Missing/reparse path or ancestor.'
}
function Assert-RAbsentObservation([bool]$Exists) {
    Assert-R (!$Exists) 'Consumed/existing destination.'
}

function Get-RChildEnvironment([string]$Outer,[string]$SystemRoot,[string]$HostRoot) {
    return [ordered]@{
        HOME="$Outer\profile";USERPROFILE="$Outer\profile"
        APPDATA="$Outer\profile\AppData\Roaming";LOCALAPPDATA="$Outer\profile\AppData\Local"
        TEMP="$Outer\scratch";TMP="$Outer\scratch"
        SystemRoot=$SystemRoot;WINDIR=$SystemRoot;ComSpec="$SystemRoot\System32\cmd.exe"
        PATH="$HostRoot;$SystemRoot\System32";PATHEXT='.EXE;.CPL'
        POWERSHELL_TELEMETRY_OPTOUT='1';POWERSHELL_UPDATECHECK='Off';PSModulePath="$HostRoot\Modules"
    }
}
function Assert-REnvironment($Environment,[string]$Outer) {
    $expected=Get-RChildEnvironment $Outer $script:RestageExpected.systemRoot $script:RestageExpected.hostRoot
    Assert-RKeys $Environment @($expected.Keys)
    foreach($key in $expected.Keys) { Assert-R ($Environment[$key] -is [string] -and $Environment[$key] -ceq $expected[$key]) "Isolated environment: $key" }
}

function Assert-RCopy($Observation) {
    Assert-R ($Observation.createdNew -is [bool] -and $Observation.createdNew) 'Copy must create a new file.'
    Assert-R ($Observation.readBytes -eq $Observation.expectedBytes -and $Observation.writtenBytes -eq $Observation.expectedBytes) 'Short/extra copy.'
    foreach($key in @('sourceBefore','sourceAfter','destination')) {
        Assert-R ($Observation[$key] -ceq $Observation.expectedSha) "Copy hash: $key"
    }
}
function Assert-RAdmission($State,[double]$Elapsed,[double]$Limit) {
    Assert-R (!$State.failed -and !$State.completed -and $Elapsed -ge 0 -and $Elapsed -lt $Limit) 'Terminal/expired operation.'
    Assert-R ($State.files -ge 0 -and $State.files -le $script:RestageExpected.files -and $State.bytes -ge 0 -and $State.bytes -le $script:RestageExpected.bytes) 'Copy budget.'
}
function Assert-RFinish($State,[bool]$Postchecked,[bool]$InventoryVerified,[double]$Elapsed) {
    Assert-RAdmission $State $Elapsed 300
    Assert-R ($Postchecked -and $InventoryVerified -and $State.files -eq $script:RestageExpected.files -and $State.bytes -eq $script:RestageExpected.bytes) 'Incomplete copy/postcheck.'
}
function Assert-RTestResult($Result,[string[]]$Expected) {
    Assert-RInteger $Result.failed 0 0;Assert-RInteger $Result.skipped 0 0;Assert-RInteger $Result.executed $Expected.Count $Expected.Count
    Assert-R ($Result.passed -is [bool] -and $Result.passed -and $Result.failed -eq 0 -and $Result.skipped -eq 0 -and $Result.executed -eq $Expected.Count) 'Pure tests incomplete.'
    Assert-REqual @($Result.cases) @($Expected) 'frozen pure inventory'
}
function Assert-RHistoricalTrx([string]$Text) {
    Assert-R ($null -ne $Text -and [Text.UTF8Encoding]::new($false,$true).GetByteCount($Text) -le 4194304) 'Historical TRX text bound.'
    # ReadPinnedText authenticates the original bytes; only this XML boundary consumes one preamble character.
    if($Text.Length -gt 0 -and $Text[0] -eq [char]0xfeff) { $Text=$Text.Substring(1) }
    $settings=[Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver=$null
    $settings.MaxCharactersInDocument=4194304
    $textReader=[IO.StringReader]::new($Text)
    $reader=$null
    try {
        $reader=[Xml.XmlReader]::Create($textReader,$settings)
        $doc=[Xml.XmlDocument]::new();$doc.XmlResolver=$null
        $doc.Load($reader)
        $c=$doc.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
        Assert-R ($null -ne $c -and $c.GetAttribute('total') -ceq '4' -and $c.GetAttribute('executed') -ceq '4' -and $c.GetAttribute('passed') -ceq '4' -and $c.GetAttribute('failed') -ceq '0') 'Historical TRX counters.'
    } finally {
        if($null -ne $reader) { $reader.Dispose() }
        $textReader.Dispose()
    }
}
function ConvertTo-RInventoryJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Rows)
    Assert-R ($null -ne $Rows -and $Rows.Count -le 10000) 'Inventory array/count required.'
    foreach($row in $Rows) { Assert-R ($null -ne $row) 'Null inventory row.' }
    $json=ConvertTo-Json -InputObject $Rows -Depth 32 -Compress -ErrorAction Stop -WarningAction Stop
    Assert-R ($json -is [string] -and [Text.UTF8Encoding]::new($false,$true).GetByteCount($json) -le 1048576) 'Inventory JSON output bound.'
    return $json
}
function Assert-ROptionalPublicationAdmission([double]$Elapsed,[double]$External) {
    Assert-R ([double]::IsFinite($Elapsed) -and $Elapsed -ge 0 -and $External -cin @(100,310) -and $Elapsed -lt $External+5) 'Optional publication deadline exhausted.'
}
function Get-RTerminalRetentionDeadline([double]$ObservationEndedSeconds) {
    Assert-R ([double]::IsFinite($ObservationEndedSeconds) -and $ObservationEndedSeconds -ge 0) 'Invalid terminal observation time.'
    $deadline=$ObservationEndedSeconds+5
    if($deadline-$ObservationEndedSeconds -gt 5) { $deadline=[Math]::BitDecrement($deadline) }
    Assert-R ([double]::IsFinite($deadline) -and $deadline -gt $ObservationEndedSeconds -and $deadline-$ObservationEndedSeconds -le 5) 'Unrepresentable terminal retention window.'
    return $deadline
}
function Assert-RTerminalRetentionAdmission([double]$Start,[double]$Deadline,[double]$Previous,[double]$Now,[long]$Bytes) {
    $expected=Get-RTerminalRetentionDeadline $Start
    Assert-R ([double]::IsFinite($Deadline) -and $Deadline -eq $expected -and [double]::IsFinite($Previous) -and $Previous -ge $Start -and [double]::IsFinite($Now) -and $Now -ge $Previous -and $Now -lt $Deadline) 'Terminal retention deadline/backward time.'
    Assert-R ($Bytes -ge 0 -and $Bytes -le 1048576) 'Terminal retention byte bound.'
}
function ConvertTo-RPublicationBytes($Value) {
    $json=ConvertTo-Json -InputObject $Value -Depth 32 -Compress -ErrorAction Stop -WarningAction Stop
    Assert-R ($null -ne $Value -and $json -is [string]) 'Missing publication object.'
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($json)
    Assert-R ($bytes.Length -le 1048576) 'Terminal publication byte bound.'
    return ,$bytes
}
function Get-RReceiptRetentionDeadline([double]$TerminalPrevious,[double]$ReceiptStartedSeconds) {
    Assert-R ([double]::IsFinite($TerminalPrevious) -and $TerminalPrevious -ge 0 -and
        [double]::IsFinite($ReceiptStartedSeconds) -and $ReceiptStartedSeconds -ge $TerminalPrevious) 'Receipt retention must follow terminal acknowledgment on the same total clock.'
    return (Get-RTerminalRetentionDeadline $ReceiptStartedSeconds)
}
function Assert-RReceiptRetentionAdmission([double]$TerminalPrevious,[double]$Start,[double]$Deadline,[double]$Previous,[double]$Now,[long]$Bytes) {
    $expected=Get-RReceiptRetentionDeadline $TerminalPrevious $Start
    Assert-R ($Deadline -eq $expected) 'Receipt retention deadline changed.'
    Assert-RTerminalRetentionAdmission $Start $Deadline $Previous $Now $Bytes
}
function Test-RExternalDeadlineExceeded([double]$Elapsed,[double]$External) {
    Assert-R ([double]::IsFinite($Elapsed) -and $Elapsed -ge 0 -and $External -cin @(100,310)) 'Invalid external deadline observation.'
    return ($Elapsed -ge $External)
}
function Assert-RImageObservation($Value) {
    Assert-RKeys $Value @('schema','role','method','flags','code','queries','handleValid','nativeError',
        'capacity','returnedChars','expectedPathSha256','observedPathSha256','observedPathLength',
        'observedPathPreview','previewTruncated','remainingBeforeMs','remainingAfterMs')
    Assert-R ($Value.schema -ceq 'retained-process-image-observation-v1' -and
        $Value.method -ceq 'QueryFullProcessImageNameW') 'Image diagnostic schema/method.'
    Assert-R ($Value.role -is [string] -and $Value.role -cin @('prepare-initial','prepare-cleanup',
        'launch-runner-initial','launch-runner-cleanup','launch-app-retain','launch-app-cleanup',
        'run-self','run-app-initial','desktop-app','supervision-self','supervision-child-initial',
        'supervision-child-cleanup')) 'Image diagnostic role.'
    Assert-R ($Value.code -is [string] -and $Value.code -cin @('not-queried','invalid-handle','native-error',
        'query-exception','invalid-image','image-observed','image-mismatch','image-deadline',
        'exit-check-failed','exited-unobserved','exited-after-query-error','image-source-failed','identity-refused')) 'Image diagnostic code.'
    foreach($key in @('flags','queries','capacity')) {
        Assert-R ($Value[$key] -is [int] -or $Value[$key] -is [long]) 'Image diagnostic integer.'
    }
    Assert-R ($Value.flags -eq 0 -and $Value.queries -in @(0,1) -and $Value.capacity -eq 32768) 'Image query bounds.'
    foreach($key in @('handleValid','previewTruncated')) {
        Assert-R ($null -eq $Value[$key] -or $Value[$key] -is [bool]) 'Image diagnostic nullable boolean.'
    }
    Assert-R ($null -eq $Value.nativeError -or
        (($Value.nativeError -is [int] -or $Value.nativeError -is [long]) -and
            $Value.nativeError -ge [int]::MinValue -and $Value.nativeError -le [int]::MaxValue)) 'Image native error.'
    foreach($key in @('returnedChars','observedPathLength')) {
        Assert-R ($null -eq $Value[$key] -or (($Value[$key] -is [int] -or $Value[$key] -is [long]) -and
            $Value[$key] -ge 1 -and $Value[$key] -le 32767)) 'Image diagnostic length.'
    }
    Assert-R ($Value.expectedPathSha256 -is [string] -and $Value.expectedPathSha256 -cmatch '^[0-9a-f]{64}$') 'Expected image digest.'
    Assert-R ($null -eq $Value.observedPathSha256 -or ($Value.observedPathSha256 -is [string] -and
        $Value.observedPathSha256 -cmatch '^[0-9a-f]{64}$')) 'Observed image digest.'
    Assert-R ($null -eq $Value.observedPathPreview -or ($Value.observedPathPreview -is [string] -and
        $Value.observedPathPreview.Length -le 256)) 'Image preview bound.'
    foreach($key in @('remainingBeforeMs','remainingAfterMs')) {
        Assert-R ($null -eq $Value[$key] -or (($Value[$key] -is [int] -or $Value[$key] -is [long] -or
            $Value[$key] -is [double] -or $Value[$key] -is [decimal]) -and
            [double]::IsFinite([double]$Value[$key]))) 'Image diagnostic budget.'
    }
    if($Value.code -ceq 'exited-after-query-error'){
        Assert-R ($Value.role -ceq 'prepare-initial' -and $Value.nativeError -eq 31 -and
            $Value.handleValid -is [bool] -and $Value.handleValid -and $Value.queries -eq 1) 'Post-query exit diagnostic origin.'
        foreach($key in @('returnedChars','observedPathSha256','observedPathLength','observedPathPreview','previewTruncated')){
            Assert-R ($null -eq $Value[$key]) 'Post-query exit has no observed image.'
        }
        Assert-R ($null -ne $Value.remainingBeforeMs -and $null -ne $Value.remainingAfterMs -and
            $Value.remainingBeforeMs -gt 0 -and $Value.remainingAfterMs -gt 0 -and
            $Value.remainingAfterMs -le $Value.remainingBeforeMs) 'Post-query exit diagnostic budget.'
    }
    $bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes(($Value|ConvertTo-Json -Depth 4 -Compress))
    Assert-R ($bytes.Length -le 4096) 'Image diagnostic byte bound.'
}
function New-RTerminalRecord([Collections.IDictionary]$Bindings,[Collections.IDictionary]$Observations) {
    Assert-R ($null -ne $Bindings -and $null -ne $Observations) 'Explicit terminal bindings and observations required.'
    $booleans=@('exitConfirmed','outputConfirmed','timedOut','environmentCleared','stdinClosed','drainingBeforeImageObservation','stdoutEof','stderrEof','custodyConfirmed')
    $integers=@('pid','startTicks','exitCode','killAttempts','stdoutBytes','stderrBytes')
    $strings=@('image','failure')
    $images=@('selfImageObservation','imageObservation','cleanupImageObservation')
    $names=$booleans+$integers+$strings+$images
    $reserved=$names+@('passed','terminalEvidenceOnly','terminalEvidenceSha256','terminalPublicationConfirmed','outputVerificationConfirmed','inventoryPublicationConfirmed','reportingFailure','receiptCompletionFailure','outputVerification','outputInventorySha256','outputInventoryBytes','outputInventoryCount')
    Assert-R ($Bindings.Contains('exe') -and $Bindings.exe -is [string] -and $Bindings.Contains('hostSha256') -and $Bindings.hostSha256 -is [string]) 'Expected host bindings required.'
    $record=[ordered]@{}
    foreach($key in $Bindings.Keys) {
        Assert-R ($key -is [string] -and $reserved -notcontains $key) 'Terminal binding collides with observed/publication fields.'
        $record.Add($key,$Bindings[$key])
    }
    foreach($name in $names) { $record.Add($name,$null) }
    foreach($key in $Observations.Keys) {
        Assert-R ($names -ccontains $key) 'Unexpected terminal observation.'
        $value=$Observations[$key]
        if($null -ne $value) {
            if($images -ccontains $key) { Assert-RImageObservation $value }
            elseif($booleans -ccontains $key) { Assert-R ($value -is [bool]) 'Terminal observation must be boolean or null.' }
            elseif($integers -ccontains $key) { Assert-R ($value -is [int] -or $value -is [long]) 'Terminal observation must be integer or null.' }
            else { Assert-R ($value -is [string] -and $value.Length -le 2048) 'Terminal observation string bound/type.' }
        }
        $record[$key]=$value
    }
    $record.passed=$false
    $record.terminalEvidenceOnly=$true
    return ,$record
}
function Invoke-RTerminalPublication {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][Collections.IDictionary]$Bindings,
        [Parameter(Mandatory)][AllowEmptyCollection()][Collections.IDictionary]$Observations,
        [Parameter(Mandatory)][scriptblock]$WriteTerminal,
        [scriptblock]$VerifyOutput,
        [scriptblock]$WriteInventory,
        [Parameter(Mandatory)][scriptblock]$WriteReceipt,
        [scriptblock]$CompleteReceipt
    )
    $ErrorActionPreference='Stop'
    $terminal=New-RTerminalRecord $Bindings $Observations
    $terminalBytes=ConvertTo-RPublicationBytes $terminal
    $ack=& $WriteTerminal -Bytes $terminalBytes
    Assert-R ($ack -is [bool] -and $ack) 'Durable terminal publication was not acknowledged.'
    # Freeze the observed snapshot before any injected verifier/writer can mutate caller-owned maps.
    $result=ConvertFrom-RJson ([Text.UTF8Encoding]::new($false,$true).GetString($terminalBytes))
    $result.terminalEvidenceOnly=$false
    $result.terminalEvidenceSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($terminalBytes)).ToLowerInvariant()
    $result.terminalPublicationConfirmed=$true
    $result.outputVerificationConfirmed=$false
    $result.inventoryPublicationConfirmed=$false
    $result.reportingFailure=$null
    $result.outputVerification=$null
    $result.outputInventorySha256=$null;$result.outputInventoryBytes=$null;$result.outputInventoryCount=$null
    $terminalPassed=$true
    foreach($key in @('exitConfirmed','outputConfirmed','environmentCleared','stdinClosed','drainingBeforeImageObservation')) {
        if($result[$key] -isnot [bool] -or !$result[$key]) { $terminalPassed=$false }
    }
    foreach($key in @('stdoutEof','stderrEof','custodyConfirmed')) {
        if($null -ne $result[$key] -and !$result[$key]) { $terminalPassed=$false }
    }
    if($result.timedOut -isnot [bool] -or $result.timedOut -or $null -eq $result.killAttempts -or $result.killAttempts -ne 0 -or $null -eq $result.exitCode -or $result.exitCode -ne 0 -or $null -ne $result.failure) { $terminalPassed=$false }
    if($null -eq $result.pid -or $result.pid -le 0 -or $null -eq $result.startTicks -or $result.startTicks -le 0 -or $null -eq $result.image -or $result.image -cne $result.exe) { $terminalPassed=$false }
    try {
        Assert-R $terminalPassed 'Terminal observations do not positively confirm success; output verification is not admitted.'
        Assert-R ($null -ne $VerifyOutput -and $null -ne $WriteInventory) 'Output verification/publication not performed.'
        $verification=& $VerifyOutput
        Assert-RKeys $verification @('passed','rows','details')
        Assert-R ($verification.passed -is [bool] -and $verification.passed -and $verification.rows -is [object[]] -and $verification.details -is [Collections.IDictionary]) 'Output verification did not positively complete.'
        $inventoryJson=ConvertTo-RInventoryJson -Rows $verification.rows
        $inventoryBytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($inventoryJson)
        $result.outputVerificationConfirmed=$true
        $result.outputVerification=$verification.details
        $ack=& $WriteInventory -Bytes $inventoryBytes
        Assert-R ($ack -is [bool] -and $ack) 'Durable inventory publication was not acknowledged.'
        $result.inventoryPublicationConfirmed=$true
        $result.outputInventorySha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($inventoryBytes)).ToLowerInvariant()
        $result.outputInventoryBytes=$inventoryBytes.Length;$result.outputInventoryCount=$verification.rows.Count
    } catch {
        $message=[string]$_.Exception.Message
        $result.reportingFailure=$message.Substring(0,[Math]::Min(2048,$message.Length))
        if($null -eq $result.failure) { $result.failure=$result.reportingFailure }
    }
    $result.passed=$terminalPassed -and $result.outputVerificationConfirmed -and $result.inventoryPublicationConfirmed -and $null -eq $result.reportingFailure
    if(!$result.passed -and $null -eq $result.failure) { $result.failure='Terminal observations do not positively confirm success.' }
    $receiptBytes=$null
    if($null -ne $CompleteReceipt) {
        $result.receiptCompletionFailure=$null
        $encoding=[Text.UTF8Encoding]::new($false,$true)
        $coreBytes=ConvertTo-RPublicationBytes $result
        $coreText=$encoding.GetString($coreBytes)
        try {
            $completionInput=ConvertFrom-RJson $coreText
            $additions=& $CompleteReceipt -Receipt $completionInput
            Assert-R ($additions -is [Collections.IDictionary]) 'Receipt completion must return exactly one dictionary of additional fields.'
            $protected=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach($key in $result.Keys) { Assert-R ($protected.Add($key)) 'Core receipt field collision.' }
            foreach($key in $additions.Keys) {
                Assert-R ($key -is [string] -and $protected.Add($key)) 'Receipt completion collides with a core/static/observation/publication or additional field.'
            }
            $additionalText=$encoding.GetString((ConvertTo-RPublicationBytes $additions))
            Assert-R ($additionalText.StartsWith('{',[StringComparison]::Ordinal) -and $additionalText.EndsWith('}',[StringComparison]::Ordinal)) 'Receipt completion must serialize as an object.'
            # Append only separately serialized object members; keep the frozen core bytes verbatim.
            $completedText=if($additions.Count -eq 0){$coreText}else{$coreText.Substring(0,$coreText.Length-1)+','+$additionalText.Substring(1)}
            $candidateBytes=$encoding.GetBytes($completedText)
            Assert-R ($candidateBytes.Length -le 1048576) 'Completed receipt byte bound.'
            $candidate=ConvertFrom-RJson $completedText
            $receiptBytes=$candidateBytes
            $result=$candidate
        } catch {
            $message=[string]$_.Exception.Message
            $result.receiptCompletionFailure=$message.Substring(0,[Math]::Min(2048,$message.Length))
            $result.passed=$false
            if($null -eq $result.reportingFailure) { $result.reportingFailure=$result.receiptCompletionFailure }
            if($null -eq $result.failure) { $result.failure=$result.receiptCompletionFailure }
        }
    }
    if($null -eq $receiptBytes) { $receiptBytes=ConvertTo-RPublicationBytes $result }
    $ack=& $WriteReceipt -Bytes $receiptBytes
    Assert-R ($ack -is [bool] -and $ack) 'Durable final receipt publication was not acknowledged.'
    if(!$result.passed) { throw "Terminal/output publication failed: $($result.failure)" }
    return ,$result
}
