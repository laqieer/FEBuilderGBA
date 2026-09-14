[CmdletBinding(PositionalBinding=$false)]
param([string]$Mode,[long]$CommandBytes,[string]$CommandSha256,
    [string]$BindingsPath,[long]$BindingsBytes,[string]$BindingsSha256,
    [string]$ClosurePath,[long]$ClosureBytes,[string]$ClosureSha256)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

function Get-PinnedProofFiles {
    foreach($name in @('Desktop.cs','Policy.cs','Policy.Tests.cs','Readiness.cs','RuntimeBinding.ps1','RuntimeBinding.Tests.ps1',
        'ProcessImage.ps1','ProcessImage.Tests.ps1','prepare.ps1','validate-helper.ps1','run.ps1','launch.ps1','test-pure.ps1',
        'Configuration.ps1','Configuration.Tests.ps1','restage\RestagePolicy.ps1','restage\restage.ps1','restage\test-pure.ps1',
        'supervision\NonCopySupervisor.ps1','supervision\RestageSupervisor.ps1')){"OfflinePatchImportProof\$name"}
    'WindowsDesktopProof\PinnedLoader.ps1'
    'WindowsDesktopProof\PinnedLoader.Tests.ps1'
}
function Read-PinnedLoaderBytes {
    param([string]$Path,[long]$Length,[string]$Sha256,[long]$Maximum)
    try {
        if($Length -lt 1 -or $Length -gt $Maximum -or $Sha256 -cnotmatch '^[0-9a-f]{64}$' -or
            ![IO.Path]::IsPathFullyQualified($Path) -or [IO.Path]::GetFullPath($Path) -cne $Path -or $Path.StartsWith('\\')){throw 'Pin/bound/path.'}
        for($ancestor=$Path;$ancestor;$ancestor=[IO.Path]::GetDirectoryName($ancestor)){
            if(([IO.File]::GetAttributes($ancestor) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Reparse ancestry.'}
        }
        $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        try {
            if($stream.Length -ne $Length){throw 'Length.'}
            $bytes=[byte[]]::new([int]$Length);$offset=0
            while($offset -lt $bytes.Length){
                $read=$stream.Read($bytes,$offset,$bytes.Length-$offset)
                if($read -le 0){throw 'Short EOF.'};$offset+=$read
            }
            if($stream.ReadByte() -ne -1){throw 'Extra bytes.'}
        }finally{$stream.Dispose()}
        if([Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() -cne $Sha256){throw 'Hash.'}
        return ,$bytes
    }catch{throw "PinnedProof.Authentication: Read '$Path': $($_.Exception.Message)"}
}
function ConvertFrom-PinnedLoaderUtf8([byte[]]$Bytes){
    try {
        $text=[Text.UTF8Encoding]::new($false,$true).GetString($Bytes)
        if($text.Length -eq 0 -or $text[0] -eq [char]0xfeff){throw 'Empty/BOM text.'}
        return $text
    }catch{throw "PinnedProof.Authentication: UTF8: $($_.Exception.Message)"}
}
function ConvertFrom-PinnedProofJson([byte[]]$Bytes){
    try {
        $text=ConvertFrom-PinnedLoaderUtf8 $Bytes
        $options=[Text.Json.JsonDocumentOptions]::new();$options.MaxDepth=8
        $document=[Text.Json.JsonDocument]::Parse($text,$options)
        try{
            $pending=[Collections.Generic.Stack[Text.Json.JsonElement]]::new();$pending.Push($document.RootElement);$count=0
            while($pending.Count){
                $item=$pending.Pop();if(++$count -gt 256){throw 'JSON node bound.'}
                if($item.ValueKind -eq [Text.Json.JsonValueKind]::Object){
                    $keys=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                    foreach($property in $item.EnumerateObject()){
                        if(!$keys.Add($property.Name)){throw 'Duplicate/case-colliding JSON key.'}
                        $pending.Push($property.Value)
                    }
                }elseif($item.ValueKind -eq [Text.Json.JsonValueKind]::Array){
                    foreach($value in $item.EnumerateArray()){$pending.Push($value)}
                }
            }
        }finally{$document.Dispose()}
        return ,(ConvertFrom-Json -InputObject $text -AsHashtable -NoEnumerate -Depth 8)
    }catch{throw "PinnedProof.Authentication: JSON: $($_.Exception.Message)"}
}
function Assert-PinnedProofKeys($Value,[string[]]$Names){
    if($Value -isnot [Collections.IDictionary] -or $Value.Count -ne $Names.Count){throw 'PinnedProof.Authentication: Object shape.'}
    foreach($name in $Value.Keys){if($Names -cnotcontains $name){throw 'PinnedProof.Authentication: Object field.'}}
}
function Get-PinnedProofDispatch([string]$Mode){
    $configuration=@('configuration','configurationSha256')
    $gui=$configuration+@('inputManifest','inputManifestSha256','sourceManifestSha256','authorizationReference')
    $prepare=$configuration+@('id','sourceManifestSha256','helperSourceManifestSha256','sourceGateReference','authorizationReference')
    $validate=$configuration+@('id','sourceManifestSha256','helperSourceManifestSha256')
    $file=$null;$entry=$null;$required=@();$fixed=@{}
    switch -CaseSensitive ($Mode){
        Pure {$file='restage\test-pure.ps1';$entry='Invoke-PinnedRestagePure'}
        AggregatePure {$file='test-pure.ps1';$entry='Invoke-AggregateEntry'}
        SupervisedPure {$file='supervision\NonCopySupervisor.ps1';$entry='Invoke-NonCopyEntry';$required=$configuration;$fixed.Mode='Pure'}
        ReadOnlyPrerequisites {$file='supervision\NonCopySupervisor.ps1';$entry='Invoke-NonCopyEntry';$required=$configuration;$fixed.Mode='ReadOnlyPrerequisites'}
        Restage {$file='supervision\RestageSupervisor.ps1';$entry='Invoke-RestageSupervisorEntry';$required=$configuration+@('newGuiId')}
        Gui {$file='launch.ps1';$entry='Invoke-LaunchEntry';$required=$gui}
        RunChild {$file='run.ps1';$entry='Invoke-RunEntry';$required=$gui}
        Build {$file='prepare.ps1';$entry='Invoke-PreparationEntry';$required=$prepare;$fixed.Stage='Build'}
        Validate {$file='prepare.ps1';$entry='Invoke-PreparationEntry';$required=$prepare;$fixed.Stage='Validate'}
        Inputs {$file='prepare.ps1';$entry='Invoke-PreparationEntry';$required=$prepare+@('buildReceiptSha256','validationReceiptSha256');$fixed.Stage='Inputs'}
        ValidatePureChild {$file='validate-helper.ps1';$entry='Invoke-ValidationEntry';$required=$validate;$fixed.Mode='Pure'}
        ValidateCompileChild {$file='validate-helper.ps1';$entry='Invoke-ValidationEntry';$required=$validate;$fixed.Mode='Compile'}
        PrerequisitesChild {$file='restage\restage.ps1';$entry='Invoke-RestageEntry';$required=$configuration+@('priorId');$fixed.ReadOnlyPrerequisites=$true}
        RestageChild {$file='restage\restage.ps1';$entry='Invoke-RestageEntry';$required=$configuration+@('priorId','newGuiId')}
        default {throw 'PinnedProof.Authentication: Fixed mode.'}
    }
    return @{file="OfflinePatchImportProof\$file";entry=$entry;required=$required;fixed=$fixed}
}
function New-PinnedProofBinding([string]$Mode,[Collections.IDictionary]$Values){
    $binding=[ordered]@{schema='pinned-proof-bindings-v2';mode=$Mode;configuration=$null;configurationSha256=$null;newGuiId=$null;
        inputManifest=$null;inputManifestSha256=$null;sourceManifestSha256=$null;authorizationReference=$null;id=$null;priorId=$null;
        helperSourceManifestSha256=$null;sourceGateReference=$null;buildReceiptSha256=$null;validationReceiptSha256=$null}
    foreach($name in $Values.Keys){
        if($name -cin @('schema','mode') -or @($binding.Keys) -cnotcontains $name){throw 'PinnedProof.Authentication: Binding field.'}
        $binding[$name]=$Values[$name]
    }
    return $binding
}
function Confirm-PinnedProofBinding($Binding,[string]$Mode){
    $shape=New-PinnedProofBinding $Mode @{}
    Assert-PinnedProofKeys $Binding @($shape.Keys)
    if($Binding.schema -isnot [string] -or $Binding.mode -isnot [string] -or
        $Binding.schema -cne 'pinned-proof-bindings-v2' -or $Binding.mode -cne $Mode){throw 'PinnedProof.Authentication: Binding schema/mode.'}
    $dispatch=Get-PinnedProofDispatch $Mode;$arguments=@{}+$dispatch.fixed
    foreach($name in $shape.Keys){
        if($name -cin @('schema','mode')){continue}
        $value=$Binding[$name]
        if($dispatch.required -cnotcontains $name){
            if($null -ne $value){throw "PinnedProof.Authentication: Unexpected binding: $name"}
            continue
        }
        if($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)){throw "PinnedProof.Authentication: Required binding: $name"}
        if($name.EndsWith('Sha256',[StringComparison]::Ordinal) -and $value -cnotmatch '^[0-9a-f]{64}$'){throw 'PinnedProof.Authentication: Binding hash.'}
        if($name -cin @('id','priorId','newGuiId') -and $value -cnotmatch '^[0-9a-f]{32}$'){throw 'PinnedProof.Authentication: Binding ID.'}
        if($name.EndsWith('Reference',[StringComparison]::Ordinal) -and $value -cnotmatch '^https://github\.com/laqieer/FEBuilderGBA/issues/[0-9]+#issuecomment-[0-9]+$'){throw 'PinnedProof.Authentication: Binding reference.'}
        if($name -cin @('configuration','inputManifest') -and
            (![IO.Path]::IsPathFullyQualified($value) -or [IO.Path]::GetFullPath($value) -cne $value -or $value.StartsWith('\\'))){throw 'PinnedProof.Authentication: Binding path.'}
        $arguments[$name]=$value
    }
    return $arguments
}
function Read-PinnedProofClosure([string]$Path,[long]$Bytes,[string]$Sha256){
    $manifest=ConvertFrom-PinnedProofJson (Read-PinnedLoaderBytes $Path $Bytes $Sha256 16384)
    Assert-PinnedProofKeys $manifest @('schema','files')
    $required=@(Get-PinnedProofFiles)
    if($manifest.schema -isnot [string] -or $manifest.schema -cne 'pinned-proof-closure-v1' -or
        $manifest.files -isnot [array] -or $manifest.files.Count -ne 22){throw 'PinnedProof.Authentication: Complete closure required.'}
    $root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $rows=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    $buffers=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::Ordinal)
    [long]$total=0
    foreach($row in $manifest.files){
        Assert-PinnedProofKeys $row @('path','bytes','sha256')
        if($row.path -isnot [string] -or $required -cnotcontains $row.path -or $rows.ContainsKey($row.path) -or
            ($row.bytes -isnot [int] -and $row.bytes -isnot [long]) -or $row.bytes -lt 1 -or $row.bytes -gt 1048576 -or
            $row.sha256 -isnot [string] -or $row.sha256 -cnotmatch '^[0-9a-f]{64}$'){throw 'PinnedProof.Authentication: Closure row.'}
        $total+=$row.bytes;if($total -gt 23068672){throw 'PinnedProof.Authentication: Closure total bound.'}
        $rows.Add($row.path,$row)
    }
    foreach($name in $required){
        $row=$rows[$name]
        $memberPath=Join-Path $root ($name.Replace('\',[IO.Path]::DirectorySeparatorChar))
        $buffers.Add($name,(Read-PinnedLoaderBytes $memberPath $row.bytes $row.sha256 1048576))
    }
    return @{root=$root;rows=$rows;buffers=$buffers;pin=@{path=$Path;bytes=$Bytes;sha256=$Sha256}}
}
function ConvertTo-PinnedProofAst([byte[]]$Bytes,[string]$Path){
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseInput((ConvertFrom-PinnedLoaderUtf8 $Bytes),$Path,[ref]$tokens,[ref]$errors)
    if($errors.Count){throw 'PinnedProof.Authentication: Source syntax.'}
    return $ast
}
function Get-PinnedProofEnvelope($Ast,[string]$File,[string]$Entry){
    $statements=@($Ast.EndBlock.Statements)
    $envelopes=@($statements|Where-Object {$_ -is [Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -ceq 'ProofEnvelope'})
    if($envelopes.Count -ne 1 -or $Ast.ParamBlock -or $Ast.DynamicParamBlock -or $Ast.BeginBlock -or $Ast.ProcessBlock -or $Ast.CleanBlock){throw 'PinnedProof.Authentication: Envelope shape.'}
    if($File -cne 'OfflinePatchImportProof\test-pure.ps1' -and
        ($statements.Count -ne 2 -or $statements[0] -isnot [Management.Automation.Language.ThrowStatementAst] -or
        $statements[0].Extent.Text -cne "throw 'PinnedProof.UnsupportedDirectRoute'")){throw 'PinnedProof.Authentication: Direct-route envelope.'}
    $body=$envelopes[0].Body
    if($body.ParamBlock -or $body.DynamicParamBlock -or $body.BeginBlock -or $body.ProcessBlock -or $body.CleanBlock){throw 'PinnedProof.Authentication: Envelope declaration.'}
    $imports=switch -CaseSensitive ($File){
        'OfflinePatchImportProof\supervision\NonCopySupervisor.ps1' {@('. (Get-PinnedProofLibrary -Library Configuration)','. (Get-PinnedProofLibrary -Library RestagePolicy)')}
        'OfflinePatchImportProof\supervision\RestageSupervisor.ps1' {@('. (Get-PinnedProofLibrary -Library NonCopySupervisor)')}
        default {@()}
    }
    foreach($statement in $body.EndBlock.Statements){
        if($statement -is [Management.Automation.Language.FunctionDefinitionAst]){continue}
        if(@($imports) -cnotcontains $statement.Extent.Text){throw 'PinnedProof.Authentication: Envelope is not declaration-only.'}
    }
    $entries=@($body.EndBlock.Statements|Where-Object {$_ -is [Management.Automation.Language.FunctionDefinitionAst] -and $_.Name -ceq $Entry})
    if($entries.Count -ne 1){throw 'PinnedProof.Authentication: Fixed named entry.'}
    return $body.GetScriptBlock()
}
function Invoke-PinnedProof {
    [CmdletBinding(PositionalBinding=$false)]
    param([Parameter(Mandatory)][string]$Mode,[Parameter(Mandatory)][long]$CommandBytes,[Parameter(Mandatory)][string]$CommandSha256,
        [Parameter(Mandatory)][string]$BindingsPath,[Parameter(Mandatory)][long]$BindingsBytes,[Parameter(Mandatory)][string]$BindingsSha256,
        [Parameter(Mandatory)][string]$ClosurePath,[Parameter(Mandatory)][long]$ClosureBytes,[Parameter(Mandatory)][string]$ClosureSha256)
    if($MyInvocation.UnboundArguments.Count){throw 'PinnedProof.Authentication: Undeclared arguments.'}
    $dispatch=Get-PinnedProofDispatch $Mode
    $admission=Read-PinnedProofClosure $ClosurePath $ClosureBytes $ClosureSha256
    $row=$admission.rows[$dispatch.file]
    if($CommandBytes -lt 1 -or $CommandBytes -gt 65536 -or $CommandBytes -ne $row.bytes -or $CommandSha256 -cne $row.sha256){throw 'PinnedProof.Authentication: Selected command pin.'}
    $binding=ConvertFrom-PinnedProofJson (Read-PinnedLoaderBytes $BindingsPath $BindingsBytes $BindingsSha256 16384)
    $entryArguments=Confirm-PinnedProofBinding $binding $Mode
    $libraries=@{}
    foreach($library in @(
        @{id='Configuration';file='Configuration.ps1';entry=$null},@{id='RestagePolicy';file='restage\RestagePolicy.ps1';entry=$null},
        @{id='NonCopySupervisor';file='supervision\NonCopySupervisor.ps1';entry='Invoke-NonCopyEntry'},
        @{id='RestageSupervisor';file='supervision\RestageSupervisor.ps1';entry='Invoke-RestageSupervisorEntry'})){
        $name='OfflinePatchImportProof\'+$library.file
        $ast=ConvertTo-PinnedProofAst $admission.buffers[$name] (Join-Path $admission.root $name)
        $libraries[$library.id]=if($library.entry){Get-PinnedProofEnvelope $ast $name $library.entry}else{$ast.GetScriptBlock()}
    }
    function Get-PinnedProofLibrary {
        param([Parameter(Mandatory)][ValidateSet('Configuration','RestagePolicy','NonCopySupervisor','RestageSupervisor')][string]$Library)
        return $libraries[$Library]
    }
    function New-PinnedProofChildArguments {
        param([Parameter(Mandatory)][ValidateSet('Pure','PrerequisitesChild','RestageChild','ValidatePureChild','ValidateCompileChild','RunChild')][string]$ChildMode,
            [Parameter(Mandatory)][string]$Path,[Parameter(Mandatory)][Collections.IDictionary]$Values)
        $fresh=Read-PinnedProofClosure $admission.pin.path $admission.pin.bytes $admission.pin.sha256
        $childBinding=New-PinnedProofBinding $ChildMode $Values
        $null=Confirm-PinnedProofBinding $childBinding $ChildMode
        if(![IO.Path]::IsPathFullyQualified($Path) -or [IO.Path]::GetFullPath($Path) -cne $Path -or $Path.StartsWith('\\')){throw 'PinnedProof.Authentication: Child binding path.'}
        for($ancestor=[IO.Path]::GetDirectoryName($Path);$ancestor;$ancestor=[IO.Path]::GetDirectoryName($ancestor)){
            if(([IO.File]::GetAttributes($ancestor) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'PinnedProof.Authentication: Child binding ancestry.'}
        }
        $data=[Text.UTF8Encoding]::new($false).GetBytes(($childBinding|ConvertTo-Json -Compress))
        if($data.Length -gt 16384){throw 'PinnedProof.Authentication: Child binding bound.'}
        $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try{$stream.Write($data);$stream.Flush($true)}finally{$stream.Dispose()}
        $target=Get-PinnedProofDispatch $ChildMode;$pin=$fresh.rows[$target.file]
        $result=@('-NoLogo','-NoProfile','-NonInteractive')
        if($ChildMode -cin @('ValidatePureChild','ValidateCompileChild','RunChild')){$result+='-STA'}
        return $result+@('-File',(Join-Path $fresh.root 'WindowsDesktopProof\PinnedLoader.ps1'),
            '-Mode',$ChildMode,'-CommandBytes',[string]$pin.bytes,'-CommandSha256',$pin.sha256,
            '-BindingsPath',$Path,'-BindingsBytes',[string]$data.Length,'-BindingsSha256',[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data)).ToLowerInvariant(),
            '-ClosurePath',$admission.pin.path,'-ClosureBytes',[string]$admission.pin.bytes,'-ClosureSha256',$admission.pin.sha256)
    }
    $entryAst=ConvertTo-PinnedProofAst $admission.buffers[$dispatch.file] (Join-Path $admission.root $dispatch.file)
    if($Mode -ceq 'Pure'){
        function Invoke-PinnedRestagePure { & $entryAst.GetScriptBlock() }
        Invoke-PinnedRestagePure
    }else{
        . (Get-PinnedProofEnvelope $entryAst $dispatch.file $dispatch.entry)
        & $dispatch.entry @entryArguments
    }
}
if($MyInvocation.InvocationName -ne '.'){
    if($MyInvocation.UnboundArguments.Count){throw 'PinnedProof.Authentication: Undeclared arguments.'}
    Invoke-PinnedProof @PSBoundParameters
}
