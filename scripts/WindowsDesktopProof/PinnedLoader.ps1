[CmdletBinding(PositionalBinding=$false)]
param([ValidateSet('Pure','ReadOnlyPrerequisites','Restage','Gui')][string]$Mode,
    [long]$CommandBytes,[string]$CommandSha256,
    [string]$BindingsPath,[long]$BindingsBytes,[string]$BindingsSha256)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

function Read-PinnedLoaderBytes {
    param([string]$Path,[long]$Length,[string]$Sha256,[long]$Maximum)
    if($Length -lt 1 -or $Length -gt $Maximum -or $Sha256 -cnotmatch '^[0-9a-f]{64}$' -or
        ![IO.Path]::IsPathFullyQualified($Path) -or [IO.Path]::GetFullPath($Path) -cne $Path -or $Path.StartsWith('\\')) {throw 'Loader pin/bound/path.'}
    for($ancestor=$Path;$ancestor;$ancestor=[IO.Path]::GetDirectoryName($ancestor)){
        if(([IO.File]::GetAttributes($ancestor) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Loader reparse ancestry.'}
    }
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try{
        if($stream.Length -ne $Length){throw 'Loader length.'}
        $bytes=[byte[]]::new([int]$Length);$offset=0
        while($offset -lt $bytes.Length){
            $read=$stream.Read($bytes,$offset,$bytes.Length-$offset)
            if($read -le 0){throw 'Loader short EOF.'};$offset+=$read
        }
        if($stream.ReadByte() -ne -1){throw 'Loader extra bytes.'}
    }finally{$stream.Dispose()}
    if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -cne $Sha256){throw 'Loader hash.'}
    return ,$bytes
}
function ConvertFrom-PinnedLoaderUtf8([byte[]]$Bytes){
    $text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes)
    if($text.Length -eq 0 -or $text[0] -eq [char]0xfeff){throw 'Loader empty/BOM text.'}
    return $text
}
function Invoke-PinnedProof {
    [CmdletBinding(PositionalBinding=$false)]
    param([Parameter(Mandatory)][ValidateSet('Pure','ReadOnlyPrerequisites','Restage','Gui')][string]$Mode,
        [Parameter(Mandatory)][long]$CommandBytes,[Parameter(Mandatory)][string]$CommandSha256,
        [Parameter(Mandatory)][string]$BindingsPath,[Parameter(Mandatory)][long]$BindingsBytes,
        [Parameter(Mandatory)][string]$BindingsSha256)
    if($args.Count){throw 'Undeclared loader arguments.'}
    $package=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\OfflinePatchImportProof'))
    $relative=switch($Mode){
        Pure {'restage\test-pure.ps1'}
        ReadOnlyPrerequisites {'supervision\NonCopySupervisor.ps1'}
        Restage {'supervision\RestageSupervisor.ps1'}
        Gui {'launch.ps1'}
    }
    $command=Join-Path $package $relative
    $commandData=Read-PinnedLoaderBytes $command $CommandBytes $CommandSha256 65536
    $bindingData=Read-PinnedLoaderBytes $BindingsPath $BindingsBytes $BindingsSha256 16384
    $text=ConvertFrom-PinnedLoaderUtf8 $commandData
    $json=ConvertFrom-PinnedLoaderUtf8 $bindingData
    $document=[Text.Json.JsonDocument]::Parse($json)
    try{
        if($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object){throw 'Loader binding object.'}
        $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $expected=@('schema','mode','configuration','configurationSha256','newGuiId','inputManifest','inputManifestSha256','sourceManifestSha256','authorizationReference')
        foreach($property in $document.RootElement.EnumerateObject()){
            if(!$keys.Add($property.Name) -or $expected -cnotcontains $property.Name -or
                $property.Value.ValueKind -notin @([Text.Json.JsonValueKind]::String,[Text.Json.JsonValueKind]::Null)){throw 'Loader binding field/type.'}
        }
        if($keys.Count -ne $expected.Count){throw 'Loader incomplete bindings.'}
    }finally{$document.Dispose()}
    $binding=ConvertFrom-Json -InputObject $json -AsHashtable -Depth 4
    if($binding.schema -cne 'pinned-proof-bindings-v1' -or $binding.mode -cne $Mode){throw 'Loader fixed mode binding.'}
    $arguments=@{}
    if($Mode -ceq 'Pure'){
        foreach($name in @('configuration','configurationSha256','newGuiId','inputManifest','inputManifestSha256','sourceManifestSha256','authorizationReference')){
            if($null -ne $binding[$name]){throw 'Pure loader admits no execution configuration.'}
        }
    }else{
        if([string]::IsNullOrWhiteSpace($binding.configuration) -or $binding.configurationSha256 -cnotmatch '^[0-9a-f]{64}$'){throw 'Loader configuration binding.'}
        $arguments.Configuration=$binding.configuration;$arguments.ConfigurationSha256=$binding.configurationSha256
        if($Mode -ceq 'ReadOnlyPrerequisites'){$arguments.Mode=$Mode}
        if($Mode -ceq 'Restage'){
            if($binding.newGuiId -cnotmatch '^[0-9a-f]{32}$'){throw 'Loader fresh ID.'}
            $arguments.NewGuiId=$binding.newGuiId
        }elseif($null -ne $binding.newGuiId){throw 'Unexpected GUI ID.'}
        foreach($name in @('inputManifest','inputManifestSha256','sourceManifestSha256','authorizationReference')){
            if($Mode -ceq 'Gui'){
                if([string]::IsNullOrWhiteSpace($binding[$name])){throw 'Incomplete GUI binding.'}
                $arguments[$name]=$binding[$name]
            }elseif($null -ne $binding[$name]){throw 'Unexpected GUI binding.'}
        }
    }
    # Parse the authenticated buffer with its fixed source filename. PSScriptRoot
    # then resolves normally without reopening or reconstructing executable text.
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseInput($text,$command,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'Pinned command syntax.'}
    & $ast.GetScriptBlock() @arguments
}
if($MyInvocation.InvocationName -ne '.'){
    if($args.Count){throw 'Undeclared loader arguments.'}
    Invoke-PinnedProof @PSBoundParameters
}
