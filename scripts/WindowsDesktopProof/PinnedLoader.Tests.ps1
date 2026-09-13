function Invoke-PinnedLoaderTests([string]$Root) {
    . (Join-Path $PSScriptRoot 'PinnedLoader.ps1')
    $cases=0
    function Reject([scriptblock]$Action) {
        $failed=$false;try{& $Action|Out-Null}catch{$failed=$true}
        if(!$failed){throw 'Loader accepted a negative fixture.'}
    }
    $path=Join-Path $Root 'loader-bytes.txt'
    $bytes=[Text.Encoding]::UTF8.GetBytes('{}')
    [IO.File]::WriteAllBytes($path,$bytes)
    $hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    $read=Read-PinnedLoaderBytes $path 2 $hash 16384
    if([Convert]::ToHexString($read) -cne [Convert]::ToHexString($bytes)){throw 'Loader bytes changed.'};$cases++
    foreach($length in @(0,1,3,16385)){Reject {Read-PinnedLoaderBytes $path $length $hash 16384};$cases++}
    Reject {Read-PinnedLoaderBytes $path 2 ('f'*64) 16384};$cases++
    Reject {Read-PinnedLoaderBytes (Join-Path $Root '.\loader-bytes.txt') 2 $hash 16384};$cases++
    Reject {ConvertFrom-PinnedLoaderUtf8 ([byte[]]@(239,187,191,123,125))};$cases++
    Reject {ConvertFrom-PinnedLoaderUtf8 ([byte[]]@(255,255))};$cases++
    Reject {ConvertFrom-PinnedLoaderUtf8 ([byte[]]@())};$cases++
    foreach($length in @(1,16384)){
        $boundary=Join-Path $Root ('loader-boundary-'+$length+'.txt')
        $data=[Text.Encoding]::UTF8.GetBytes((' '*$length));[IO.File]::WriteAllBytes($boundary,$data)
        $digest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data)).ToLowerInvariant()
        if((Read-PinnedLoaderBytes $boundary $length $digest 16384).Length -ne $length){throw 'Loader inclusive boundary.'};$cases++
    }
    $link=Join-Path $Root 'loader-reparse'
    if($IsWindows){$null=New-Item -ItemType Junction -Path $link -Value $Root -ErrorAction Stop}
    else{$null=[IO.Directory]::CreateSymbolicLink($link,$Root)}
    try{Reject {Read-PinnedLoaderBytes (Join-Path $link 'loader-bytes.txt') 2 $hash 16384};$cases++}
    finally{[IO.Directory]::Delete($link)}
    $command=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\OfflinePatchImportProof\restage\test-pure.ps1'))
    $commandData=[IO.File]::ReadAllBytes($command)
    $commandHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($commandData)).ToLowerInvariant()
    $binding=[ordered]@{schema='pinned-proof-bindings-v1';mode='Pure';configuration=$null;configurationSha256=$null;newGuiId=$null;
        inputManifest=$null;inputManifestSha256=$null;sourceManifestSha256=$null;authorizationReference=$null}
    $bindingPath=Join-Path $Root 'loader-bindings.json'
    $json=ConvertTo-Json -InputObject $binding -Compress
    [IO.File]::WriteAllText($bindingPath,$json,[Text.UTF8Encoding]::new($false))
    $bindingBytes=[IO.File]::ReadAllBytes($bindingPath)
    $spec=@{Mode='Pure';CommandBytes=$commandData.Length;CommandSha256=$commandHash;BindingsPath=$bindingPath;
        BindingsBytes=$bindingBytes.Length;BindingsSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bindingBytes)).ToLowerInvariant()}
    Reject {Invoke-PinnedProof @spec -CommandPath $command};$cases++
    Reject {Invoke-PinnedProof @spec -ScriptText 'throw "must not execute"'};$cases++
    $wrongCommand=$spec.Clone();$wrongCommand.CommandSha256='f'*64
    Reject {Invoke-PinnedProof @wrongCommand};$cases++
    foreach($replacement in @($json.Replace('"mode":"Pure"','"mode":"Gui"'),$json.Replace('"configuration":null','"configuration":"not admitted"'),
        $json.Replace('"schema":','"unknown":null,"schema":'),$json.Replace('"mode":','"Mode":null,"mode":'),$json.Replace('"configuration":null','"configuration":[]'))){
        [IO.File]::WriteAllText($bindingPath,$replacement,[Text.UTF8Encoding]::new($false))
        $bad=$spec.Clone();$bad.BindingsBytes=([IO.FileInfo]$bindingPath).Length
        $bad.BindingsSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($bindingPath))).ToLowerInvariant()
        Reject {Invoke-PinnedProof @bad};$cases++
    }
    [IO.File]::WriteAllText($bindingPath,$json,[Text.UTF8Encoding]::new($false))
    $result=Invoke-PinnedProof @spec | ConvertFrom-Json
    if(!$result.passed -or $result.executed -ne 286 -or $result.nativeCalls){throw 'Fixed pure fixture invocation failed.'}
    $cases++
    return $cases
}
