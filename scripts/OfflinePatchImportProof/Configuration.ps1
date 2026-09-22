Set-StrictMode -Version Latest

function Assert-Proof([bool]$Condition,[string]$Message) {
    if (!$Condition) { throw $Message }
}
function Assert-ProofPublicResource([string]$Relative,[switch]$SyntheticFixture) {
    $value=$Relative.Replace('/','\')
    Assert-Proof ($value -notmatch '(^|\\)(log|logs)(\\|$)' -and
        $value -notmatch '(^|\\)generated-core-suite-log-preserved\.txt$' -and
        ($value -notmatch '\.(gba|gb|gbc|nds|rom)$' -or
        ($SyntheticFixture -and $value -ceq 'fixtures\zipdb-proof.gba'))) 'Private resource refused before content access.'
}
function Assert-ProofProcessDeadline([Collections.IDictionary]$Record,[double]$Elapsed,[double]$Deadline) {
    if($Record.timedOut -isnot [bool] -or $Record.timedOut -or ![double]::IsFinite($Elapsed) -or
        ![double]::IsFinite($Deadline) -or $Elapsed -lt 0 -or $Elapsed -ge $Deadline){
        $Record.timedOut=$true;$Record.passed=$false
        throw 'Process deadline; no retry or late success.'
    }
}
function Assert-ProofBooleanFields($Value,[string[]]$TrueFields,[string[]]$FalseFields) {
    foreach($name in $TrueFields){Assert-Proof ($Value.$name -is [bool] -and $Value.$name) "Required true Boolean: $name"}
    foreach($name in $FalseFields){Assert-Proof ($Value.$name -is [bool] -and !$Value.$name) "Required false Boolean: $name"}
}
function Assert-ProofKeys($Value,[string[]]$Keys) {
    Assert-Proof ($Value -is [Collections.IDictionary] -and $Value.Count -eq $Keys.Count) 'Configuration object shape.'
    $actual=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($name in $Value.Keys){Assert-Proof ($name -is [string] -and $actual.Add($name)) 'Configuration key type.'}
    foreach ($key in $Keys) { Assert-Proof ($actual.Contains($key)) "Configuration field: $key" }
}
function ConvertFrom-ProofJson([string]$Text,[long]$MaximumBytes=4194304,[int]$MaximumNodes=50000) {
    Assert-Proof ($MaximumBytes -ge 1 -and $MaximumBytes -le 33554432) 'JSON maximum byte bound.'
    Assert-Proof ($MaximumNodes -ge 1 -and $MaximumNodes -le 1000000) 'JSON maximum structural bound.'
    Assert-Proof (![string]::IsNullOrWhiteSpace($Text) -and [Text.Encoding]::UTF8.GetByteCount($Text) -le $MaximumBytes) 'JSON byte bound.'
    $document=[Text.Json.JsonDocument]::Parse($Text)
    try {
        $pending=[Collections.Generic.Stack[object]]::new()
        $pending.Push(@{value=$document.RootElement;depth=0});$count=0
        while ($pending.Count) {
            $item=$pending.Pop();$count++
            Assert-Proof ($count -le $MaximumNodes -and $item.depth -le 32) 'JSON structural bound.'
            if ($item.value.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
                $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                foreach ($property in $item.value.EnumerateObject()) {
                    Assert-Proof ($seen.Add($property.Name)) 'Duplicate/case-colliding JSON key.'
                    $pending.Push(@{value=$property.Value;depth=$item.depth+1})
                }
            } elseif ($item.value.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
                foreach ($value in $item.value.EnumerateArray()) { $pending.Push(@{value=$value;depth=$item.depth+1}) }
            }
        }
    } finally { $document.Dispose() }
    return ,(ConvertFrom-Json -InputObject $Text -AsHashtable -NoEnumerate -Depth 32)
}
function Assert-ProofPath([string]$Path,[switch]$Existing) {
    Assert-Proof (![string]::IsNullOrWhiteSpace($Path) -and [IO.Path]::IsPathFullyQualified($Path) -and
        [IO.Path]::GetFullPath($Path) -ceq $Path -and !$Path.StartsWith('\\')) 'Canonical local path required.'
    for ($cursor=$Path;$cursor;$cursor=[IO.Path]::GetDirectoryName($cursor)) {
        try { $attributes=[IO.File]::GetAttributes($cursor) }
        catch [IO.FileNotFoundException] { if ($Existing -and $cursor -ceq $Path) { throw };continue }
        catch [IO.DirectoryNotFoundException] { if ($Existing -and $cursor -ceq $Path) { throw };continue }
        Assert-Proof (($attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Reparse ancestry refused.'
    }
}
function Initialize-ProofEmptyPatchLibrary([string]$AppRoot) {
    Assert-ProofPath $AppRoot -Existing
    $target=Join-Path $AppRoot 'config\patch2\FE8U'
    Assert-ProofPath $target
    for($cursor=$target;$cursor;$cursor=[IO.Path]::GetDirectoryName($cursor)){
        try{$attributes=[IO.File]::GetAttributes($cursor)}
        catch [IO.FileNotFoundException]{continue}
        catch [IO.DirectoryNotFoundException]{continue}
        Assert-Proof ($cursor -cne $target) 'Canonical patch library already exists.'
        Assert-Proof (($attributes -band [IO.FileAttributes]::Directory) -ne 0) 'Plain directory ancestry required.'
    }
    [void][IO.Directory]::CreateDirectory($target)
    Assert-ProofPath $target -Existing
    $entries=[IO.Directory]::EnumerateFileSystemEntries($target).GetEnumerator()
    try{Assert-Proof (!$entries.MoveNext()) 'Canonical patch library must be empty.'}
    finally{$entries.Dispose()}
    return $true
}
function Assert-ProofPin($Pin) {
    Assert-ProofKeys $Pin @('path','bytes','sha256')
    Assert-Proof ($Pin.path -is [string]) 'Pin path type.'
    Assert-ProofPath $Pin.path
    Assert-Proof (($Pin.bytes -is [int] -or $Pin.bytes -is [long]) -and $Pin.bytes -ge 0 -and $Pin.bytes -le 268435456) 'Pin size/type.'
    Assert-Proof ($Pin.sha256 -is [string] -and $Pin.sha256 -cmatch '^[0-9a-f]{64}$') 'Pin hash/type.'
}
function Read-ProofPinnedBytes($Pin,[long]$Maximum=4194304) {
    Assert-ProofPin $Pin
    Assert-Proof ($Pin.bytes -le $Maximum) 'Pinned read bound.'
    Assert-ProofPath $Pin.path -Existing
    $stream=[IO.File]::Open($Pin.path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        Assert-Proof ($stream.Length -eq $Pin.bytes) 'Pinned read length.'
        $bytes=[byte[]]::new([int]$Pin.bytes);$offset=0
        while ($offset -lt $bytes.Length) {
            $read=$stream.Read($bytes,$offset,$bytes.Length-$offset)
            Assert-Proof ($read -gt 0) 'Pinned read short EOF.'
            $offset+=$read
        }
        Assert-Proof ($stream.ReadByte() -eq -1) 'Pinned read extra bytes.'
        Assert-Proof ([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -ceq $Pin.sha256) 'Pinned read hash.'
    } finally { $stream.Dispose() }
    return ,$bytes
}
function Read-ProofPinnedJson($Pin) {
    return ,(ConvertFrom-ProofJson ([Text.UTF8Encoding]::new($false,$true).GetString((Read-ProofPinnedBytes $Pin))))
}
function Read-ProofJsonFile([string]$Path,[long]$Maximum=4194304,[int]$MaximumNodes=50000) {
    Assert-ProofPath $Path -Existing
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        Assert-Proof ($stream.Length -ge 1 -and $stream.Length -le $Maximum) 'JSON file bound.'
        $bytes=[byte[]]::new([int]$stream.Length);$offset=0
        while($offset -lt $bytes.Length){
            $read=$stream.Read($bytes,$offset,$bytes.Length-$offset)
            Assert-Proof ($read -gt 0) 'JSON short EOF.';$offset+=$read
        }
        Assert-Proof ($stream.ReadByte() -eq -1) 'JSON extra bytes.'
    }finally{$stream.Dispose()}
    return ,(ConvertFrom-ProofJson ([Text.UTF8Encoding]::new($false,$true).GetString($bytes)) $Maximum $MaximumNodes)
}
function Read-ProofInput([string]$Path,[string]$Sha256,$Configuration) {
    Assert-ProofPath $Path -Existing
    $value=Read-ProofPinnedJson @{path=$Path;bytes=([IO.FileInfo]$Path).Length;sha256=$Sha256}
    Assert-ProofKeys $value @('schema','applicationSource','runId','syntheticRomFormat','powershellHost','appFiles','fixtures','runtimeAssemblies','compileReferences','installedFiles')
    Assert-Proof ($value.schema -ceq 'windows-desktop-bounded-input-v1' -and
        $value.applicationSource -ceq $Configuration.applicationSource -and $value.runId -is [string] -and
        $value.runId -cmatch '^[0-9a-f]{32}$' -and $value.syntheticRomFormat -ceq 'fe8u-synthetic-huffman-v1') 'Input identity/format.'
    Assert-ProofKeys $value.powershellHost @('path','sha256')
    Assert-Proof ($value.powershellHost.path -ceq $Configuration.host.path -and
        $value.powershellHost.sha256 -ceq $Configuration.host.sha256) 'Configured input host.'
    foreach($name in @('appFiles','fixtures','runtimeAssemblies','installedFiles')){
        $rows=$value[$name];$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$total=0L
        Assert-Proof ($rows -is [array] -and $rows.Count -ge 1 -and $rows.Count -le 10000) 'Input row count/type.'
        foreach($row in $rows){
            Assert-ProofKeys $row @('path','bytes','sha256')
            Assert-Proof ($row.path -is [string] -and $row.path.Length -le 500 -and $row.path -cnotmatch '[/:*?"<>|\x00-\x1f]' -and $seen.Add($row.path)) 'Input relative path/duplicate.'
            foreach($part in $row.path.Split('\')){
                Assert-Proof ($part -and $part -cnotin @('.','..') -and $part -notmatch '[. ]$' -and
                    $part -notmatch '^(?i:CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|$)' -and
                    $part -notmatch '^(?i:patch2|\.patch2-import.*|(?:run|launch|inputs)-[0-9a-f]{32})$') 'Input unsafe path component.'
            }
            Assert-Proof (($row.bytes -is [int] -or $row.bytes -is [long]) -and $row.bytes -ge 0 -and $row.bytes -le 268435456 -and
                $row.sha256 -is [string] -and $row.sha256 -cmatch '^[0-9a-f]{64}$') 'Input row pin/type.'
            $total+=$row.bytes;Assert-Proof ($total -le 2147483648L) 'Input total bound.'
        }
    }
    Assert-Proof ($value.fixtures.Count -eq 3 -and $value.runtimeAssemblies.Count -eq 7 -and $value.installedFiles.Count -eq 2) 'Fixed feature fixture/runtime shape.'
    Assert-Proof ($value.compileReferences -is [array] -and $value.compileReferences.Count -ge 1 -and $value.compileReferences.Count -le 256) 'Input compiler closure.'
    foreach($row in $value.compileReferences){Assert-ProofPin $row}
    return ($value|ConvertTo-Json -Depth 32|ConvertFrom-Json)
}
function Read-ProofConfiguration {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Sha256,
        [ValidateSet('Gui','Build','Restage')][string]$Purpose='Gui')
    Assert-ProofPath $Path -Existing
    $pin=@{path=$Path;bytes=([IO.FileInfo]$Path).Length;sha256=$Sha256}
    $value=Read-ProofPinnedJson $pin
    Assert-ProofKeys $value @('schema','outputRoot','evidenceRoot','applicationSource','host','sourceManifest','approval','machine','preparation','restage')
    Assert-Proof ($value.schema -ceq 'offline-patch-import-proof-v1') 'Configuration schema.'
    foreach ($name in @('outputRoot','evidenceRoot')) {
        Assert-Proof ($value[$name] -is [string]) 'Root type.'
        Assert-ProofPath $value[$name] -Existing
    }
    Assert-Proof ($value.applicationSource -is [string] -and $value.applicationSource -cmatch '^[0-9a-f]{40}$') 'Application source pin.'
    Assert-ProofPin $value.host;Assert-ProofPin $value.sourceManifest
    Assert-ProofKeys $value.approval @('sourceGateReference','authorizationReference','authorizationSha256')
    foreach ($name in @('sourceGateReference','authorizationReference')) {
        Assert-Proof ($value.approval[$name] -is [string] -and $value.approval[$name] -cmatch '^https://github\.com/laqieer/FEBuilderGBA/issues/[0-9]+#issuecomment-[0-9]+$') 'Caller approval reference.'
    }
    Assert-Proof ($value.approval.sourceGateReference -cne $value.approval.authorizationReference -and
        $value.approval.authorizationSha256 -is [string] -and $value.approval.authorizationSha256 -cmatch '^[0-9a-f]{64}$') 'Distinct caller approval bindings.'
    Assert-ProofKeys $value.machine @('systemRoot','dotnetRoot','gitPath','programFiles','programFilesX86')
    foreach ($name in $value.machine.Keys) {
        Assert-Proof ($value.machine[$name] -is [string]) 'Machine path type.'
        Assert-ProofPath $value.machine[$name]
    }
    if ($null -ne $value.preparation) {
        Assert-ProofKeys $value.preparation @('root','inputs','metadata')
        Assert-ProofPath $value.preparation.root -Existing
        Assert-ProofPin $value.preparation.inputs;Assert-ProofPin $value.preparation.metadata
    }
    if ($null -ne $value.restage) {
        Assert-ProofKeys $value.restage @('priorId','donorRoot','manifest','metadata','history','receipts','expected')
        Assert-Proof ($value.restage.priorId -is [string] -and $value.restage.priorId -cmatch '^[0-9a-f]{32}$') 'Donor ID.'
        Assert-ProofPath $value.restage.donorRoot -Existing
        Assert-ProofPin $value.restage.manifest;Assert-ProofPin $value.restage.metadata
        Assert-ProofPin $value.restage.history
        Assert-Proof ($value.restage.receipts -is [array] -and $value.restage.receipts.Count -eq 3) 'Three prior stages required.'
        $stages=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($row in $value.restage.receipts) {
            Assert-ProofKeys $row @('pin','stage','runId','sourceManifestSha256','helperSourceManifestSha256','toolMetadataSha256','sourceGateReference','authorizationReference','head','tree')
            Assert-ProofPin $row.pin
            Assert-Proof ($row.stage -cin @('Validate','Build','Inputs') -and $stages.Add($row.stage)) 'Exact distinct prior stages.'
            foreach($key in @('sourceManifestSha256','helperSourceManifestSha256','toolMetadataSha256')){
                Assert-Proof ($row[$key] -is [string] -and $row[$key] -cmatch '^[0-9a-f]{64}$') 'Prior digest type.'
            }
            foreach($key in @('head','tree')){Assert-Proof ($row[$key] -is [string] -and $row[$key] -cmatch '^[0-9a-f]{40}$') 'Prior source identity type.'}
            foreach ($key in @($row.Keys | Where-Object { $_ -cne 'pin' })) {
                Assert-Proof ($row[$key] -is [string] -and ![string]::IsNullOrWhiteSpace($row[$key]) -and $row[$key].Length -le 2048) 'Prior binding value.'
            }
        }
        Assert-ProofKeys $value.restage.expected @('payload','files','bytes')
        Assert-ProofPin $value.restage.expected.payload
        Assert-Proof (($value.restage.expected.files -is [int] -or $value.restage.expected.files -is [long]) -and $value.restage.expected.files -ge 1 -and $value.restage.expected.files -le 10000) 'Payload file bound.'
        Assert-Proof (($value.restage.expected.bytes -is [int] -or $value.restage.expected.bytes -is [long]) -and $value.restage.expected.bytes -ge 0 -and $value.restage.expected.bytes -le 2147483648L) 'Payload byte bound.'
    }
    Assert-Proof ($Purpose -cne 'Build' -or $null -ne $value.preparation) 'Preparation configuration required.'
    Assert-Proof ($Purpose -cne 'Restage' -or $null -ne $value.restage) 'Restage configuration required.'
    return ,$value
}
function Read-ProofHistory($Pin) {
    $history=Read-ProofPinnedJson $Pin
    Assert-ProofKeys $history @('schema','status','root','planReference','planGateReference','planBoardSha256','priorId',
        'applicationHead','applicationTree','priorSourceGate','files','evidence','pureCaseNames','limits','externalClosureRule')
    Assert-Proof ($history.schema -ceq 'windows-desktop-restage-source-v1') 'Historical manifest schema.'
    foreach($key in @('status','root','planReference','planGateReference','priorSourceGate','externalClosureRule')){
        Assert-Proof ($history[$key] -is [string] -and ![string]::IsNullOrWhiteSpace($history[$key]) -and
            $history[$key].Length -le 4096) 'Historical manifest text/type bound.'
    }
    Assert-Proof ($history.planBoardSha256 -is [string] -and $history.planBoardSha256 -cmatch '^[0-9a-f]{64}$' -and
        $history.priorId -is [string] -and $history.priorId -cmatch '^[0-9a-f]{32}$') 'Historical manifest identity.'
    foreach($key in @('applicationHead','applicationTree')){
        Assert-Proof ($history[$key] -is [string] -and $history[$key] -cmatch '^[0-9a-f]{40}$') 'Historical source identity.'
    }
    Assert-Proof ($history.files -is [array] -and $history.files.Count -le 10000 -and
        $history.evidence -is [Collections.IDictionary] -and $history.evidence.Count -ge 1 -and $history.evidence.Count -le 64 -and
        $history.limits -is [Collections.IDictionary] -and $history.limits.Count -ge 1 -and $history.limits.Count -le 64) 'Historical closure shape.'
    foreach($row in $history.evidence.Values){Assert-ProofPin $row}
    Assert-Proof ($history.pureCaseNames -is [array] -and $history.pureCaseNames.Count -ge 1 -and $history.pureCaseNames.Count -le 1000) 'Historical case inventory bound.'
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($name in $history.pureCaseNames){
        Assert-Proof ($name -is [string] -and $name.Length -ge 1 -and $name.Length -le 256 -and $seen.Add($name)) 'Historical case inventory entry.'
    }
    return ,$history
}
function Assert-ProofTiming($Report) {
    Assert-Proof (($Report.elapsedSeconds -is [double] -or $Report.elapsedSeconds -is [decimal] -or
        $Report.elapsedSeconds -is [int] -or $Report.elapsedSeconds -is [long]) -and
        [double]::IsFinite([double]$Report.elapsedSeconds) -and $Report.elapsedSeconds -ge 0) 'Report elapsed time shape.'
    foreach($key in @('startedUtc','completedUtc')){
        $value=$Report[$key]
        if($value -is [DateTime]){continue}
        $parsed=[DateTime]::MinValue
        Assert-Proof ($value -is [string] -and $value.Length -le 64 -and
            [DateTime]::TryParse($value,[Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind,[ref]$parsed)) 'Report timestamp projection shape.'
    }
}
function Get-ProofRestageCaseNames([string]$Root=$PSScriptRoot) {
    foreach($name in @('restage\test-pure.ps1','Configuration.Tests.ps1')){
        $path=Join-Path $Root $name
        Assert-ProofPath $path -Existing
        Assert-Proof (([IO.FileInfo]$path).Length -le 1048576) 'Case-source byte bound.'
        foreach($match in [regex]::Matches([IO.File]::ReadAllText($path),"(?m)^\s+@\{name='([^']+)';reject=")){
            $match.Groups[1].Value
        }
    }
}
function Assert-ProofSource($Configuration,[string]$Root) {
    $manifest=Read-ProofPinnedJson $Configuration.sourceManifest
    Assert-ProofKeys $manifest @('files')
    $required=@('Desktop.cs','Policy.cs','Policy.Tests.cs','Readiness.cs','RuntimeBinding.ps1','RuntimeBinding.Tests.ps1',
        'ProcessImage.ps1','ProcessImage.Tests.ps1','prepare.ps1','validate-helper.ps1','run.ps1','launch.ps1','test-pure.ps1',
        'Configuration.ps1','Configuration.Tests.ps1','restage\RestagePolicy.ps1','restage\restage.ps1','restage\test-pure.ps1',
        'supervision\NonCopySupervisor.ps1','supervision\RestageSupervisor.ps1')
    Assert-Proof ($manifest.files -is [array] -and $manifest.files.Count -eq $required.Count) 'Complete source closure required.'
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($row in $manifest.files) {
        Assert-ProofKeys $row @('path','bytes','sha256')
        Assert-Proof ($row.path -is [string] -and $required -ccontains $row.path -and $seen.Add($row.path)) 'Source relative path.'
        $path=Join-Path $Root ($row.path.Replace('\',[IO.Path]::DirectorySeparatorChar))
        $null=Read-ProofPinnedBytes @{path=$path;bytes=$row.bytes;sha256=$row.sha256} 1048576
    }
    return ,$manifest
}

function Read-ProofPreparation($Configuration) {
    Assert-Proof ($null -ne $Configuration.preparation) 'Preparation configuration required.'
    $inputs=Read-ProofPinnedJson $Configuration.preparation.inputs
    $metadata=Read-ProofPinnedJson $Configuration.preparation.metadata
    Assert-ProofKeys $inputs @('application_pins_status','worktree','head','head_tree','accepted_application_source','reviewed_application_base',
        'reviewed_localization_delta_sha256','reviewed_documentation_delta_paths','patch2_submodule','fe_info_submodule','dotnet','package_folders','source_sha256')
    Assert-ProofKeys $metadata @('dotnetRoot','pshome','systemRoot','sdk','runtime','tools','runtimeAssemblies','compileReferences','assets')
    foreach($key in @('application_pins_status','worktree','head','head_tree','accepted_application_source','reviewed_application_base',
        'reviewed_localization_delta_sha256','reviewed_documentation_delta_paths','patch2_submodule','fe_info_submodule','dotnet','package_folders','source_sha256')) {
        Assert-Proof ($inputs.Contains($key)) "Preparation input missing: $key"
    }
    Assert-Proof ($inputs.application_pins_status -ceq 'final-pinned' -and $inputs.accepted_application_source -ceq $Configuration.applicationSource) 'Accepted preparation application.'
    foreach($key in @('head','head_tree','accepted_application_source','reviewed_application_base','patch2_submodule','fe_info_submodule')) {
        Assert-Proof ($inputs[$key] -is [string] -and $inputs[$key] -cmatch '^[0-9a-f]{40}$') 'Preparation commit/tree type.'
    }
    foreach($key in @('worktree','dotnet')) {Assert-Proof ($inputs[$key] -is [string]) 'Preparation path type.';Assert-ProofPath $inputs[$key] -Existing}
    foreach($key in @('source_sha256','reviewed_localization_delta_sha256')) {
        Assert-Proof ($inputs[$key] -is [Collections.IDictionary]) 'Source pin map type.'
        foreach($name in $inputs[$key].Keys) {
            Assert-Proof ($name -is [string] -and $name -cmatch '^[A-Za-z][A-Za-z0-9_.\\-]*$' -and !$name.Contains('..') -and
                $inputs[$key][$name] -is [string] -and $inputs[$key][$name] -cmatch '^[0-9a-f]{64}$') 'Source pin map entry.'
        }
    }
    foreach($key in @('package_folders','reviewed_documentation_delta_paths')) {
        Assert-Proof ($inputs[$key] -is [array] -and $inputs[$key].Count -le 64) 'Preparation list bound.'
        foreach($item in $inputs[$key]) {Assert-Proof ($item -is [string] -and ![string]::IsNullOrWhiteSpace($item)) 'Preparation list element.'}
    }
    Assert-Proof ($inputs.package_folders.Count -eq 2) 'Two explicit package cache roots required.'
    foreach($path in $inputs.package_folders){Assert-ProofPath $path.TrimEnd('\') -Existing}
    foreach($key in @('dotnetRoot','pshome','systemRoot','sdk','runtime','tools','runtimeAssemblies','compileReferences','assets')){
        Assert-Proof ($metadata.Contains($key)) "Tool metadata missing: $key"
    }
    foreach($key in @('sdk','runtime')){Assert-Proof ($metadata[$key] -is [string] -and $metadata[$key] -cmatch '^[0-9]+\.[0-9]+\.[0-9]+$') 'Tool version format.'}
    Assert-Proof ($metadata.pshome -ceq [IO.Path]::GetDirectoryName($Configuration.host.path) -and
        $metadata.dotnetRoot -ceq $Configuration.machine.dotnetRoot -and $metadata.systemRoot -ceq $Configuration.machine.systemRoot) 'Machine metadata binding.'
    foreach($key in @('tools','runtimeAssemblies','compileReferences')){
        Assert-Proof ($metadata[$key] -is [array] -and $metadata[$key].Count -ge 1 -and $metadata[$key].Count -le 256) 'Tool closure bound.'
        foreach($row in $metadata[$key]){Assert-ProofPin $row}
    }
    foreach($path in @($Configuration.host.path,$inputs.dotnet,$Configuration.machine.gitPath)){
        Assert-Proof (@($metadata.tools | Where-Object {$_.path -ceq $path}).Count -eq 1) 'Required executable tool pin.'
    }
    Assert-Proof ($metadata.assets -is [array] -and $metadata.assets.Count -le 256) 'Asset closure.'
    foreach($row in $metadata.assets){
        Assert-ProofKeys $row @('path','bytes','sha256')
        Assert-Proof ($row.path -is [string] -and $row.path -cmatch '^[A-Za-z][A-Za-z0-9_.\\-]*$' -and !$row.path.Contains('..')) 'Asset relative path.'
        Assert-ProofPin @{path=(Join-Path $inputs.worktree $row.path);bytes=$row.bytes;sha256=$row.sha256}
    }
    return @{inputs=($inputs|ConvertTo-Json -Depth 32|ConvertFrom-Json);metadata=($metadata|ConvertTo-Json -Depth 32|ConvertFrom-Json)}
}
