function Get-PinnedTestPin([string]$Path){
    $bytes=[IO.File]::ReadAllBytes($Path)
    return @{path=$Path;bytes=$bytes.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}
}
function Write-PinnedTestJson([string]$Path,$Value){
    [IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 8 -Compress),[Text.UTF8Encoding]::new($false))
    return Get-PinnedTestPin $Path
}
function New-PinnedProofFixture([string]$Root){
    $scripts=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $target=Join-Path $Root 'scripts';$buffers=@{}
    $rows=@(foreach($name in Get-PinnedProofFiles){
        $path=Join-Path $target $name
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        $bytes=[IO.File]::ReadAllBytes((Join-Path $scripts $name));$buffers[$name]=$bytes
        [IO.File]::WriteAllBytes($path,$bytes)
        $pin=Get-PinnedTestPin $path
        @{path=$name;bytes=$pin.bytes;sha256=$pin.sha256}
    })
    $closure=Write-PinnedTestJson (Join-Path $Root 'closure.json') @{schema='pinned-proof-closure-v1';files=$rows}
    $loader=Join-Path $target 'WindowsDesktopProof\PinnedLoader.ps1'
    $ast=ConvertTo-PinnedProofAst $buffers['WindowsDesktopProof\PinnedLoader.ps1'] $loader
    return @{root=$Root;scripts=$target;rows=$rows;buffers=$buffers;closure=$closure;loader=$ast.GetScriptBlock()}
}
function New-PinnedTestSpec($Fixture,[string]$Mode,[Collections.IDictionary]$Values=@{}){
    $binding=New-PinnedProofBinding $Mode $Values
    $pin=Write-PinnedTestJson (Join-Path $Fixture.root ('bindings-'+[guid]::NewGuid().ToString('N')+'.json')) $binding
    $dispatch=Get-PinnedProofDispatch $Mode;$row=@($Fixture.rows|Where-Object path -CEQ $dispatch.file)[0]
    return @{Mode=$Mode;CommandBytes=$row.bytes;CommandSha256=$row.sha256;BindingsPath=$pin.path;BindingsBytes=$pin.bytes;
        BindingsSha256=$pin.sha256;ClosurePath=$Fixture.closure.path;ClosureBytes=$Fixture.closure.bytes;ClosureSha256=$Fixture.closure.sha256}
}
function Invoke-PinnedTestMode {
    param($Fixture,[string]$Root,[string]$Mode,[Collections.IDictionary]$Values=@{})
    if(!$Fixture){$Fixture=New-PinnedProofFixture (Join-Path $Root ('dispatch-'+[guid]::NewGuid().ToString('N')))}
    $spec=New-PinnedTestSpec $Fixture $Mode $Values
    . $Fixture.loader
    Invoke-PinnedProof @spec
}
function Invoke-PinnedDependencyRegression([string]$Root) {
    $scripts=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $owned=Join-Path $Root 'dependency-regression'
    $rows=@(foreach($package in @('OfflinePatchImportProof','WindowsDesktopProof')){
        foreach($file in [IO.Directory]::GetFiles((Join-Path $scripts $package),'*',[IO.SearchOption]::AllDirectories)){
            if([IO.Path]::GetExtension($file) -cnotin @('.ps1','.cs')){continue}
            $relative=[IO.Path]::GetRelativePath($scripts,$file).Replace([IO.Path]::DirectorySeparatorChar,'\')
            $destination=Join-Path $owned $relative
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
            $data=[IO.File]::ReadAllBytes($file);[IO.File]::WriteAllBytes($destination,$data)
            @{path=$relative;bytes=$data.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($data)).ToLowerInvariant()}
        }
    })
    if($rows.Count -ne 22){throw 'Regression fixture inventory.'}
    $closure=Join-Path $owned 'closure.json'
    [IO.File]::WriteAllText($closure,(@{schema='pinned-proof-closure-v1';files=$rows}|ConvertTo-Json -Depth 4 -Compress),[Text.UTF8Encoding]::new($false))
    . (Join-Path $owned 'WindowsDesktopProof\PinnedLoader.ps1')
    $modern=(Get-Command Invoke-PinnedProof).Parameters.ContainsKey('ClosurePath')
    $binding=[ordered]@{schema=$(if($modern){'pinned-proof-bindings-v2'}else{'pinned-proof-bindings-v1'});mode='Pure';
        configuration=$null;configurationSha256=$null;newGuiId=$null;inputManifest=$null;inputManifestSha256=$null;
        sourceManifestSha256=$null;authorizationReference=$null}
    if($modern){foreach($name in @('id','priorId','helperSourceManifestSha256','sourceGateReference','buildReceiptSha256','validationReceiptSha256')){$binding[$name]=$null}}
    $bindingPath=Join-Path $owned 'bindings.json'
    [IO.File]::WriteAllText($bindingPath,($binding|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
    $entry=@($rows|Where-Object path -CEQ 'OfflinePatchImportProof\restage\test-pure.ps1')[0]
    $spec=@{Mode='Pure';CommandBytes=$entry.bytes;CommandSha256=$entry.sha256;BindingsPath=$bindingPath;
        BindingsBytes=([IO.FileInfo]$bindingPath).Length;BindingsSha256=(Get-FileHash $bindingPath).Hash.ToLowerInvariant()}
    if($modern){$spec.ClosurePath=$closure;$spec.ClosureBytes=([IO.FileInfo]$closure).Length;$spec.ClosureSha256=(Get-FileHash $closure).Hash.ToLowerInvariant()}
    $marker=Join-Path $owned 'SENTINEL'
    [IO.File]::WriteAllText((Join-Path $owned 'OfflinePatchImportProof\restage\RestagePolicy.ps1'),
        ("[IO.File]::WriteAllText('"+$marker.Replace("'","''")+"','owned benign sentinel');throw 'Owned dependency executed'"),[Text.UTF8Encoding]::new($false))
    $failure=$null;try{Invoke-PinnedProof @spec|Out-Null}catch{$failure=$_.Exception.Message}
    if([IO.File]::Exists($marker) -or $failure -notlike 'PinnedProof.Authentication:*'){
        throw "Dependency admission regression: marker=$([IO.File]::Exists($marker)); failure=$failure"
    }
    return 1
}

function Invoke-PinnedLoaderTests([string]$Root) {
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
    $fixture=New-PinnedProofFixture (Join-Path $Root 'loader-fixture')
    . $fixture.loader
    $command=[IO.Path]::GetFullPath((Join-Path $fixture.scripts 'OfflinePatchImportProof\restage\test-pure.ps1'))
    $commandData=[IO.File]::ReadAllBytes($command)
    $commandHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($commandData)).ToLowerInvariant()
    $binding=New-PinnedProofBinding Pure @{}
    $bindingPath=Join-Path $Root 'loader-bindings.json'
    $json=ConvertTo-Json -InputObject $binding -Compress
    [IO.File]::WriteAllText($bindingPath,$json,[Text.UTF8Encoding]::new($false))
    $bindingBytes=[IO.File]::ReadAllBytes($bindingPath)
    $spec=@{Mode='Pure';CommandBytes=$commandData.Length;CommandSha256=$commandHash;BindingsPath=$bindingPath;
        BindingsBytes=$bindingBytes.Length;BindingsSha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bindingBytes)).ToLowerInvariant();
        ClosurePath=$fixture.closure.path;ClosureBytes=$fixture.closure.bytes;ClosureSha256=$fixture.closure.sha256}
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
    $result=& {
        function Invoke-ProofShortAliasTrap { throw 'Short r alias must not execute.' }
        Set-Alias -Name r -Value Invoke-ProofShortAliasTrap -Scope Local -Force
        if((Get-Alias -Name r -ErrorAction Stop).Definition -cne 'Invoke-ProofShortAliasTrap'){throw 'Alias trap not established.'}
        Invoke-PinnedProof @spec | ConvertFrom-Json
    }
    $cases++
    if(!$result.passed -or $result.executed -ne 322 -or $result.nativeCalls){throw 'Fixed pure fixture invocation failed.'}
    $cases++
    $cases+=Invoke-PinnedDependencyRegression $Root
    $cases+=Invoke-PinnedScopeTests $Root
    $cases+=Invoke-PinnedClosureTests $Root
    return $cases
}

function Invoke-PinnedBuildTelemetryTests([Management.Automation.Language.ScriptBlockAst]$Prepare,
    [Management.Automation.Language.ScriptBlockAst]$Launch){
    $names=[Collections.Generic.List[string]]::new()
    $failures=[Collections.Generic.List[string]]::new()
    function Require([bool]$Value,[string]$Code){if(!$Value){throw $Code}}
    function Case([string]$Name,[scriptblock]$Assertion){
        Require (!$names.Contains($Name)) 'BuildTelemetry.DuplicateCase'
        $names.Add($Name)
        try{& $Assertion}catch{$failures.Add($Name+': '+$_.Exception.Message)}
    }
    function Reject([string]$Code,[scriptblock]$Assertion){
        $failure=$null
        try{& $Assertion}catch{$failure=$_.Exception.Message}
        Require ($failure -ceq $Code) ('BuildTelemetry.UnexpectedRefusal: '+$failure)
    }
    function Assignment($Scope,[string]$Name){
        $found=@($Scope.FindAll({param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
            $node.Left.VariablePath.UserPath -ceq $Name
        },$true))
        Require ($found.Count -eq 1) 'BuildTelemetry.Structure'
        return $found[0]
    }
    function EnvironmentShape($Ast,[bool]$IsLaunch=$false){
        $functionName=if($IsLaunch){'Invoke-LaunchEntry'}else{'Run'}
        $runs=@($Ast.FindAll({param($node)
            $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $functionName
        },$true))
        Require ($runs.Count -eq 1) 'BuildTelemetry.Structure'
        $assignment=Assignment $runs[0].Body environment
        Require ($assignment.Right -is [Management.Automation.Language.CommandExpressionAst] -and
            $assignment.Right.Expression -is [Management.Automation.Language.HashtableAst]) 'BuildTelemetry.Structure'
        return @{run=$runs[0];assignment=$assignment;table=$assignment.Right.Expression}
    }
    function LiteralArray($Ast){
        $allowed=@('CommandExpressionAst','ArrayExpressionAst','StatementBlockAst','PipelineAst',
            'ArrayLiteralAst','StringConstantExpressionAst')
        $nodes=@($Ast.FindAll({param($node)$true},$true))
        Require (@($nodes|Where-Object {$_.GetType().Name -cnotin $allowed}).Count -eq 0 -and
            @($nodes|Where-Object {$_ -is [Management.Automation.Language.ArrayExpressionAst]}).Count -eq 1) 'BuildTelemetry.Structure'
        $strings=@($nodes|Where-Object {$_ -is [Management.Automation.Language.StringConstantExpressionAst]})
        Require (@($strings|Where-Object {$_.StringConstantType.ToString() -cne 'SingleQuoted'}).Count -eq 0) 'BuildTelemetry.Structure'
        return ,@($strings|ForEach-Object Value)
    }
    function BuildBlock($Ast){
        $blocks=@(foreach($node in $Ast.FindAll({param($item)
            $item -is [Management.Automation.Language.IfStatementAst]
        },$true)){
            foreach($clause in $node.Clauses){
                if($clause.Item1.Extent.Text -ceq '$Stage -ceq ''Build'''){$clause.Item2}
            }
        })
        Require ($blocks.Count -eq 1) 'BuildTelemetry.Structure'
        return $blocks[0]
    }
    function AssertEnvironment($Ast,[string]$Key,[bool]$IsLaunch=$false){
        $shape=EnvironmentShape $Ast $IsLaunch
        $pairs=@($shape.table.KeyValuePairs|Where-Object {
            $_.Item1 -is [Management.Automation.Language.StringConstantExpressionAst] -and
            $_.Item1.Value -ieq $Key
        })
        Require ($pairs.Count -eq 1) 'BuildTelemetry.EnvironmentPolicy'
        $valueNodes=@($pairs[0].Item2.FindAll({param($node)$true},$true))
        $allowed=@('StatementBlockAst','PipelineAst','CommandExpressionAst','StringConstantExpressionAst')
        $literal=@($valueNodes|Where-Object {$_ -is [Management.Automation.Language.StringConstantExpressionAst]})
        Require ($pairs[0].Item1.Value -ceq $Key -and
            @($valueNodes|Where-Object {$_.GetType().Name -cnotin $allowed}).Count -eq 0 -and
            $literal.Count -eq 1 -and $literal[0].Value -ceq '1' -and
            $literal[0].StringConstantType.ToString() -ceq 'SingleQuoted') 'BuildTelemetry.EnvironmentPolicy'
        $clearText=if($IsLaunch){'$start.Environment.Clear()'}else{'$info.Environment.Clear()'}
        $startText=if($IsLaunch){'[Diagnostics.Process]::Start($start)'}else{'$p.Start()'}
        $populateText=if($IsLaunch){
            'foreach ($entry in $environment.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }'
        }else{'foreach ($k in $environment.Keys) { $info.Environment[$k]=[string]$environment[$k] }'}
        $environmentTarget=if($IsLaunch){'$start.Environment'}else{'$info.Environment'}
        $clear=@($shape.run.Body.FindAll({param($node)
            $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and
            $node.Extent.Text -ceq $clearText
        },$true))
        $start=@($shape.run.Body.FindAll({param($node)
            $node -is [Management.Automation.Language.InvokeMemberExpressionAst] -and
            $node.Extent.Text -ceq $startText
        },$true))
        $populate=@($shape.run.Body.FindAll({param($node)
            $node -is [Management.Automation.Language.ForEachStatementAst] -and
            ($node.Extent.Text -replace '\s+',' ').Trim() -ceq $populateText
        },$true))
        $writes=@($shape.run.Body.FindAll({param($node)
            $node -is [Management.Automation.Language.AssignmentStatementAst] -and
            $node.Left -is [Management.Automation.Language.IndexExpressionAst] -and
            $node.Left.Target.Extent.Text -ceq $environmentTarget
        },$true))
        Require ($clear.Count -eq 1 -and $start.Count -eq 1 -and $populate.Count -eq 1 -and $writes.Count -eq 1 -and
            $clear[0].Extent.StartOffset -lt $shape.assignment.Extent.StartOffset -and
            $shape.assignment.Extent.EndOffset -lt $populate[0].Extent.StartOffset -and
            $populate[0].Extent.EndOffset -lt $start[0].Extent.StartOffset -and
            $writes[0].Extent.StartOffset -gt $populate[0].Extent.StartOffset -and
            $writes[0].Extent.EndOffset -lt $populate[0].Extent.EndOffset) 'BuildTelemetry.EnvironmentOrder'
    }
    $commonPrefix=@('--no-restore','--disable-build-servers','-p:E2E_HOOKS=false','-p:UseSharedCompilation=false',
        '-p:MSBuildEnableWorkloadResolver=false','-p:BuildProjectReferences=true','-p:ImportDirectoryBuildProps=false',
        '-p:ImportDirectoryBuildTargets=false','-p:ImportDirectoryPackagesProps=false','-m:1','-nr:false')
    $expectedCalls=@(
        'Run $plan.dotnet (@(''build'',$project,''-c'',$configuration,''-t:Rebuild'')+$common) 360 $control $true'
        'Run $plan.dotnet (@(''test'',$project,''-c'',$configuration,''--no-build'',''--filter'',''FullyQualifiedName~FEBuilderGBA.Avalonia.Tests.SyntheticPatchImportFixtureTests'',''--logger'',"trx;LogFileName=$trx",''--results-directory'',"${CONTROL}")+$common) 180 $control $true'.Replace('${CONTROL}','$control\tests')
        'Run $plan.dotnet (@(''publish'',"${APP}",''-c'',''Release'',''-t:Rebuild,Publish'',''-o'',"${PUBLISH}")+$common) 360 $control $true'.Replace('${APP}','$W\FEBuilderGBA.Avalonia\FEBuilderGBA.Avalonia.csproj').Replace('${PUBLISH}','$B\publish')
        'Run $plan.dotnet (@(''publish'',"${GENERATOR}",''-c'',''Debug'',''-t:Rebuild,Publish'',''-o'',"${OUTPUT}")+$common) 180 $control $true'.Replace('${GENERATOR}','$W\scripts\SyntheticProofFixtures\SyntheticProofFixtures.csproj').Replace('${OUTPUT}','$B\generator')
    )
    function AssertBuildVectors($Ast){
        $build=BuildBlock $Ast
        $assignment=Assignment $build common
        $values=LiteralArray $assignment.Right
        $expected=@($commonPrefix)+@('-p:UsedAvaloniaProducts=')
        Require ($values.Count -eq 12 -and ($values -join "`n") -ceq ($expected -join "`n")) 'BuildTelemetry.CommonPolicy'
        $calls=@($build.FindAll({param($node)
            $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -ceq 'Run' -and
            $node.CommandElements.Count -gt 1 -and $node.CommandElements[1].Extent.Text -ceq '$plan.dotnet'
        },$true))
        Require ($calls.Count -eq 4) 'BuildTelemetry.BuildVectors'
        for($i=0;$i -lt 4;$i++){
            Require ($calls[$i].CommandElements.Count -eq 6 -and
                ($calls[$i].Extent.Text -replace '\s+',' ').Trim() -ceq $expectedCalls[$i]) 'BuildTelemetry.BuildVectors'
        }
        $loops=@($build.FindAll({param($node)
            $node -is [Management.Automation.Language.ForEachStatementAst] -and
            $node.Variable.VariablePath.UserPath -ceq 'configuration'
        },$true))
        Require ($loops.Count -eq 1) 'BuildTelemetry.BuildVectors'
        $configurations=LiteralArray $loops[0].Condition
        Require (($configurations -join '|') -ceq 'Debug|Release' -and
            $assignment.Extent.EndOffset -lt $loops[0].Extent.StartOffset) 'BuildTelemetry.BuildVectors'
        for($i=0;$i -lt 2;$i++){
            Require ($calls[$i].Extent.StartOffset -gt $loops[0].Body.Extent.StartOffset -and
                $calls[$i].Extent.EndOffset -lt $loops[0].Body.Extent.EndOffset) 'BuildTelemetry.BuildVectors'
            Require ($calls[$i+2].Extent.StartOffset -gt $loops[0].Extent.EndOffset) 'BuildTelemetry.BuildVectors'
        }
        Require (2*$configurations.Count+2 -eq 6) 'BuildTelemetry.BuildVectors'
        $guards=@($loops[0].Body.FindAll({param($node)
            $node -is [Management.Automation.Language.IfStatementAst] -and
            $node.Clauses[0].Item1.Extent.Text -like '*$c.total*'
        },$true))
        Require ($guards.Count -eq 1) 'BuildTelemetry.FixtureSelection'
        foreach($term in @('$c.total -ne ''4''','$c.executed -ne ''4''','$c.passed -ne ''4''',
            '$c.failed -ne ''0''','$results.Count -ne 4','$_.outcome -cne ''Passed''')){
            Require ($guards[0].Clauses[0].Item1.Extent.Text.Contains($term)) 'BuildTelemetry.FixtureSelection'
        }
    }
    function AssertPropertyPropagation([string]$Text){
        $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null
        $inputText=[IO.StringReader]::new($Text);$reader=[Xml.XmlReader]::Create($inputText,$settings)
        $xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null
        try{$xml.Load($reader)}finally{$reader.Dispose();$inputText.Dispose()}
        foreach($node in $xml.SelectNodes('//*')){
            $entries=@(@{name=$node.LocalName;value=$node.InnerText})
            foreach($attribute in $node.Attributes){$entries+=@{name=$attribute.LocalName;value=$attribute.Value}}
            foreach($entry in $entries){
                if($entry.name.ToLowerInvariant() -cnotin @('treataslocalproperty','globalpropertiestoremove',
                    'removeproperties','properties','additionalproperties')){continue}
                $value=[string]$entry.value
                $properties=@($value -split ';'|ForEach-Object {($_ -split '=',2)[0].Trim().ToLowerInvariant()})
                Require ($properties -cnotcontains 'usedavaloniaproducts' -and !$value.Contains('$(') -and
                    !$value.Contains('@(') -and !$value.Contains('*')) 'BuildTelemetry.PropertyPropagation'
            }
        }
    }
    function ParseVariant([string]$Text,$Original=$Prepare){
        return ConvertTo-PinnedProofAst ([Text.UTF8Encoding]::new($false).GetBytes($Text)) $Original.Extent.File
    }
    function EnvironmentVariants([string]$Source,$Ast,[string]$Key,[bool]$IsLaunch=$false){
        $shape=EnvironmentShape $Ast $IsLaunch
        $pairs=@($shape.table.KeyValuePairs|Where-Object {$_.Item1.Value -ceq $Key})
        Require ($pairs.Count -eq 1) 'BuildTelemetry.Structure'
        $begin=$pairs[0].Item1.Extent.StartOffset
        $end=$pairs[0].Item2.Extent.EndOffset
        if($end -lt $Source.Length -and $Source[$end] -eq ';'){$end++}
        $entry=$Source.Substring($begin,$end-$begin)
        $removed=$Source.Remove($begin,$end-$begin)
        $wrong=$removed.Insert($begin,$entry.Replace("'1'","'0'"))
        $clear=if($IsLaunch){'$start.Environment.Clear()'}else{'$info.Environment.Clear()'}
        $population=if($IsLaunch){
            'foreach ($entry in $environment.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }'
        }else{'foreach ($k in $environment.Keys) { $info.Environment[$k]=[string]$environment[$k] }'}
        return @(
            @{name='removed';text=$removed;code='BuildTelemetry.EnvironmentPolicy'}
            @{name='wrong';text=$wrong;code='BuildTelemetry.EnvironmentPolicy'}
            @{name='inherited-only';text=('$env:'+$Key+"='1'`n"+$removed);code='BuildTelemetry.EnvironmentPolicy'}
            @{name='comment-only';text=("<#`n"+$entry+"`n#>`n"+$removed);code='BuildTelemetry.EnvironmentPolicy'}
            @{name='late-clear';text=$Source.Replace($clear,'').Replace($population,$population+"`n"+$clear);code='BuildTelemetry.EnvironmentOrder'}
        )
    }
    # Only actual-source cases establish production policy. Controlled copies
    # isolate negative mutations without replacing or evaluating either body.
    $control=$Prepare.Extent.Text
    $common=Assignment (BuildBlock $Prepare) common
    $flag=",'-p:UsedAvaloniaProducts='"
    if((LiteralArray $common.Right) -cnotcontains '-p:UsedAvaloniaProducts='){
        $control=$control.Insert($common.Right.Extent.EndOffset-1,$flag)
    }
    $shape=EnvironmentShape $Prepare
    $preparationKeys=@('AVALONIA_TELEMETRY_OPTOUT','POWERSHELL_TELEMETRY_OPTOUT')
    foreach($key in $preparationKeys){
        if(@($shape.table.KeyValuePairs|Where-Object {$_.Item1.Value -ieq $key}).Count -eq 0){
            $control=$control.Insert($shape.table.Extent.StartOffset+2,"`n                "+$key+"='1';")
        }
    }
    $positive=ParseVariant $control
    $launchControl=$Launch.Extent.Text
    $launchShape=EnvironmentShape $Launch $true
    if(@($launchShape.table.KeyValuePairs|Where-Object {$_.Item1.Value -ieq 'POWERSHELL_TELEMETRY_OPTOUT'}).Count -eq 0){
        $launchControl=$launchControl.Insert($launchShape.table.Extent.StartOffset+2,"`n            POWERSHELL_TELEMETRY_OPTOUT='1';")
    }
    $launchPositive=ParseVariant $launchControl $Launch
    Case 'actual-run-avalonia-environment' {AssertEnvironment $Prepare 'AVALONIA_TELEMETRY_OPTOUT'}
    Case 'actual-run-powershell-environment' {AssertEnvironment $Prepare 'POWERSHELL_TELEMETRY_OPTOUT'}
    Case 'actual-launch-powershell-environment' {AssertEnvironment $Launch 'POWERSHELL_TELEMETRY_OPTOUT' $true}
    Case 'actual-build-vectors' {AssertBuildVectors $Prepare}
    Case 'controlled-run-avalonia-environment' {AssertEnvironment $positive 'AVALONIA_TELEMETRY_OPTOUT'}
    Case 'controlled-run-powershell-environment' {AssertEnvironment $positive 'POWERSHELL_TELEMETRY_OPTOUT'}
    Case 'controlled-launch-powershell-environment' {AssertEnvironment $launchPositive 'POWERSHELL_TELEMETRY_OPTOUT' $true}
    Case 'controlled-source-build-vectors' {AssertBuildVectors $positive}
    foreach($key in $preparationKeys){
        foreach($variant in (EnvironmentVariants $control $positive $key)){
            Case ('prepare-'+$key+'-'+$variant.name) {Reject $variant.code {AssertEnvironment (ParseVariant $variant.text) $key}}
        }
    }
    foreach($variant in (EnvironmentVariants $launchControl $launchPositive 'POWERSHELL_TELEMETRY_OPTOUT' $true)){
        Case ('launch-powershell-'+$variant.name) {
            Reject $variant.code {AssertEnvironment (ParseVariant $variant.text $Launch) 'POWERSHELL_TELEMETRY_OPTOUT' $true}
        }
    }
    $commonVariants=@(
        @{name='removed';text=$control.Replace($flag,'');code='BuildTelemetry.CommonPolicy'}
        @{name='nonempty';text=$control.Replace("-p:UsedAvaloniaProducts='","-p:UsedAvaloniaProducts=Other'");code='BuildTelemetry.CommonPolicy'}
        @{name='duplicate';text=$control.Replace($flag,$flag+$flag);code='BuildTelemetry.CommonPolicy'}
        @{name='conflicting';text=$control.Replace($flag,$flag+",'-p:UsedAvaloniaProducts=Other'");code='BuildTelemetry.CommonPolicy'}
        @{name='comment-only';text=("# '-p:UsedAvaloniaProducts='`n"+$control.Replace($flag,''));code='BuildTelemetry.CommonPolicy'}
    )
    foreach($variant in $commonVariants){
        Case ('common-'+$variant.name) {Reject $variant.code {AssertBuildVectors (ParseVariant $variant.text)}}
    }
    Case 'build-site-missing-common' {
        Reject 'BuildTelemetry.BuildVectors' {AssertBuildVectors (ParseVariant $control.Replace($expectedCalls[0],$expectedCalls[0].Replace('+$common','')))}
    }
    Case 'build-site-later-conflict' {
        Reject 'BuildTelemetry.BuildVectors' {AssertBuildVectors (ParseVariant $control.Replace($expectedCalls[0],$expectedCalls[0].Replace('+$common','+$common+@(''-p:UsedAvaloniaProducts=Other'')')))}
    }
    $xmlFixtures=@(
        @{name='ordinary-property';reject=$false;xml='<Project><PropertyGroup><UsedAvaloniaProducts>AvaloniaUI</UsedAvaloniaProducts></PropertyGroup></Project>'}
        @{name='unrelated-removal';reject=$false;xml='<Project><MSBuild RemoveProperties="Other" Properties="Unrelated=1"/></Project>'}
        @{name='treat-as-local';reject=$true;xml='<Project TreatAsLocalProperty="Configuration;UsedAvaloniaProducts"/>'}
        @{name='global-removal-element';reject=$true;xml='<Project><ProjectReference><GlobalPropertiesToRemove>UsedAvaloniaProducts</GlobalPropertiesToRemove></ProjectReference></Project>'}
        @{name='global-removal-attribute';reject=$true;xml='<Project><ProjectReference GlobalPropertiesToRemove="UsedAvaloniaProducts"/></Project>'}
        @{name='msbuild-removal';reject=$true;xml='<Project><MSBuild RemoveProperties="usedavaloniaproducts"/></Project>'}
        @{name='msbuild-override';reject=$true;xml='<Project><MSBuild Properties="Other=1;UsedAvaloniaProducts=Other"/></Project>'}
        @{name='reference-override-element';reject=$true;xml='<Project><ProjectReference><AdditionalProperties>UsedAvaloniaProducts=Other</AdditionalProperties></ProjectReference></Project>'}
        @{name='reference-override-attribute';reject=$true;xml='<Project><ProjectReference AdditionalProperties="UsedAvaloniaProducts=Other"/></Project>'}
        @{name='dynamic-removal';reject=$true;xml='<Project><MSBuild RemoveProperties="$(RemovedGlobals)"/></Project>'}
    )
    foreach($fixture in $xmlFixtures){
        Case ('xml-'+$fixture.name) {
            if($fixture.reject){Reject 'BuildTelemetry.PropertyPropagation' {AssertPropertyPropagation $fixture.xml}}
            else{AssertPropertyPropagation $fixture.xml}
        }
    }
    if($names.Count -ne 40){throw 'Build telemetry AST case inventory changed.'}
    if($failures.Count){throw ('Build telemetry AST cases failed: '+($failures -join '; '))}
    return $names.Count
}

function Invoke-PinnedScopeTests([string]$Root){
    $fixture=New-PinnedProofFixture (Join-Path $Root 'scope-fixture')
    . $fixture.loader
    $null=Read-PinnedProofClosure $fixture.closure.path $fixture.closure.bytes $fixture.closure.sha256
    $prepare=ConvertTo-PinnedProofAst $fixture.buffers['OfflinePatchImportProof\prepare.ps1'] (Join-Path $fixture.scripts 'OfflinePatchImportProof\prepare.ps1')
    $runs=@($prepare.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Run'},$true))
    if($runs.Count -ne 1){throw 'Fixed preparation Run probe.'}
    $processState=@{sequence=0};$observed=@{prefix=$null};$control=$Root
    & {
        function Budget {}
        function Plain {}
        function Join-Path([string]$Path,[string]$ChildPath){$observed.prefix=$ChildPath;throw 'Owned prefix probe stop'}
        $exe='owned.exe';$arguments=@();$seconds=1;$cwd=$Root;$maySpawn=$false
        foreach($expected in @(1,2)){
            $failure=$null
            try{& $runs[0].Body.GetScriptBlock()}catch{$failure=$_.Exception.Message}
            if($failure -cne 'Owned prefix probe stop' -or $processState.sequence -ne $expected -or $observed.prefix -cne "process-$expected"){throw "Preparation invocation-local sequence failed: $failure"}
        }
    }
    $launch=ConvertTo-PinnedProofAst $fixture.buffers['OfflinePatchImportProof\launch.ps1'] (Join-Path $fixture.scripts 'OfflinePatchImportProof\launch.ps1')
    $checks=@($launch.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'CheckWorkerStop'},$true))
    if($checks.Count -ne 1){throw 'Fixed launcher worker-stop probe.'}
    $runRoot=Join-Path $fixture.root 'run';[void][IO.Directory]::CreateDirectory($runRoot)
    $manifest=@{runId=('1'*32)};$InputManifestSha256='2'*64;$SourceManifestSha256='3'*64
    $runner=@{Id=123};$runnerTicks=456;$custody=@{app=$null;appReceipt=@{pid=789;start_ticks=999}}
    $receipt=@{worker_stop_requested=$false;timed_out=$false}
    $null=Write-PinnedTestJson (Join-Path $runRoot 'worker-stop.json') @{
        schema='windows-desktop-worker-stop-v1';run_id=$manifest.runId;input_sha256=$InputManifestSha256;
        source_sha256=$SourceManifestSha256;runner_pid=$runner.Id;runner_start_ticks=$runnerTicks;
        app_pid=$custody.appReceipt.pid;app_start_ticks=$custody.appReceipt.start_ticks;dispatch_admission_closed=$true;
        worker_joined=$false;reason='timeout:owned';requested_utc='2026-01-01T00:00:00Z'}
    & {
        function Plain {}
        $failure=$null
        try{& $checks[0].Body.GetScriptBlock()}catch{$failure=$_.Exception.Message}
        if($failure -cne 'Worker deadline; terminate the runner boundary before app cleanup.' -or
            !$receipt.worker_stop_requested -or !$receipt.timed_out -or $receipt.worker_stop_reason -cne 'timeout:owned'){throw "Launcher invocation-local custody failed: $failure"}
    }
    $telemetryCases=Invoke-PinnedBuildTelemetryTests $prepare $launch
    return (3+$telemetryCases)
}

function Invoke-PinnedClosureTests([string]$Root){
    $fixture=New-PinnedProofFixture (Join-Path $Root 'closure-cases')
    . $fixture.loader
    $cases=0;$marker=Join-Path $fixture.root 'SENTINEL'
    $modes=@('Pure','AggregatePure','SupervisedPure','ReadOnlyPrerequisites','Restage','Gui','Build','Validate','Inputs',
        'ValidatePureChild','ValidateCompileChild','PrerequisitesChild','RestageChild','RunChild')
    $allValues=@{configuration=(Join-Path $fixture.root 'configuration.json');configurationSha256=('1'*64);
        inputManifest=(Join-Path $fixture.root 'input.json');inputManifestSha256=('2'*64);sourceManifestSha256=('3'*64);
        helperSourceManifestSha256=('3'*64);authorizationReference='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-2';
        sourceGateReference='https://github.com/laqieer/FEBuilderGBA/issues/1#issuecomment-3';id=('4'*32);priorId=('5'*32);newGuiId=('6'*32);
        buildReceiptSha256=('7'*64);validationReceiptSha256=('8'*64)}
    function Values([string]$Mode){
        $values=@{};foreach($key in (Get-PinnedProofDispatch $Mode).required){$values[$key]=$allValues[$key]};return $values
    }
    function AuthenticateRefusal([Collections.IDictionary]$Spec,[string]$Expected='PinnedProof.Authentication:*'){
        $before=@([IO.Directory]::GetFileSystemEntries($fixture.root,'*',[IO.SearchOption]::AllDirectories)|Sort-Object)
        function Add-Type { $script:unexpectedCompile=$true;throw 'Compilation reached before admission.' }
        $failure=$null
        try{Invoke-PinnedProof @Spec|Out-Null}catch{$failure=$_.Exception.Message}
        if($failure -notlike $Expected -or [IO.File]::Exists($marker)){throw "Expected authentication refusal: $failure"}
        $after=@([IO.Directory]::GetFileSystemEntries($fixture.root,'*',[IO.SearchOption]::AllDirectories)|Sort-Object)
        if(($before -join "`n") -cne ($after -join "`n") -or $script:unexpectedCompile){throw 'Authentication refusal caused effects.'}
    }
    $script:unexpectedCompile=$false
    $specs=@{}
    foreach($mode in $modes){
        $values=Values $mode;$specs[$mode]=New-PinnedTestSpec $fixture $mode $values
        $null=Confirm-PinnedProofBinding (New-PinnedProofBinding $mode $values) $mode;$cases++
        foreach($key in $allValues.Keys){
            $changed=@{}+$values
            if($changed.ContainsKey($key)){$changed.Remove($key)}else{$changed[$key]=$allValues[$key]}
            $bad=New-PinnedTestSpec $fixture $mode $changed
            AuthenticateRefusal $bad;$cases++
        }
    }
    foreach($row in $fixture.rows){
        $path=Join-Path $fixture.scripts $row.path
        $sentinel=if($row.path.EndsWith('.ps1')){
            "[IO.File]::WriteAllText('"+$marker.Replace("'","''")+"','owned benign sentinel');throw 'Owned dependency executed'"
        }else{'not valid C sharp; authentication must reject before compilation'}
        [IO.File]::WriteAllText($path,$sentinel,[Text.UTF8Encoding]::new($false))
        try{
            foreach($mode in $modes){AuthenticateRefusal $specs[$mode];$cases++}
        }finally{[IO.File]::WriteAllBytes($path,$fixture.buffers[$row.path])}
    }
    $original=[IO.File]::ReadAllBytes($fixture.closure.path)
    foreach($kind in @('missing','extra','duplicate','case','unknown','string-size','negative','oversize','hash','wrong-length','schema','row-field')){
        $manifest=ConvertFrom-PinnedProofJson $original
        switch($kind){
            missing {$manifest.files=@($manifest.files|Select-Object -Skip 1)}
            extra {$manifest.files+=@{path='other.ps1';bytes=1;sha256=('1'*64)}}
            duplicate {$manifest.files[1]=$manifest.files[0]}
            case {$manifest.files[1]=@{}+$manifest.files[0];$manifest.files[1].path=$manifest.files[0].path.ToUpperInvariant()}
            unknown {$manifest.files[0].path='..\outside.ps1'}
            string-size {$manifest.files[0].bytes=[string]$manifest.files[0].bytes}
            negative {$manifest.files[0].bytes=-1}
            oversize {$manifest.files[0].bytes=1048577}
            hash {$manifest.files[0].sha256='f'*64}
            wrong-length {$manifest.files[0].bytes++}
            schema {$manifest.schema='other'}
            row-field {$manifest.files[0].extra=$true}
        }
        $pin=Write-PinnedTestJson $fixture.closure.path $manifest
        $bad=@{}+$specs.Pure;$bad.ClosureBytes=$pin.bytes;$bad.ClosureSha256=$pin.sha256
        AuthenticateRefusal $bad;$cases++
    }
    [IO.File]::WriteAllBytes($fixture.closure.path,$original)
    foreach($field in @('schema','mode','configuration')){
        foreach($value in @(@(),@{},1,$true)){
            $binding=New-PinnedProofBinding Pure @{};$binding[$field]=$value
            $pin=Write-PinnedTestJson (Join-Path $fixture.root ('typed-'+[guid]::NewGuid().ToString('N')+'.json')) $binding
            $bad=@{}+$specs.Pure;$bad.BindingsPath=$pin.path;$bad.BindingsBytes=$pin.bytes;$bad.BindingsSha256=$pin.sha256
            AuthenticateRefusal $bad;$cases++
        }
    }
    $badJson=@{
        bom=([byte[]]@(239,187,191)+$original);utf8=[byte[]]@(255,255);
        oversize=[Text.Encoding]::UTF8.GetBytes((' '*16385));
        duplicate=[Text.Encoding]::UTF8.GetBytes(([Text.Encoding]::UTF8.GetString($original)).Replace('"schema":','"Schema":null,"schema":'))
    }
    foreach($data in $badJson.Values){
        [IO.File]::WriteAllBytes($fixture.closure.path,$data)
        $pin=Get-PinnedTestPin $fixture.closure.path
        $bad=@{}+$specs.Pure;$bad.ClosureBytes=$pin.bytes;$bad.ClosureSha256=$pin.sha256
        AuthenticateRefusal $bad;$cases++
    }
    [IO.File]::WriteAllBytes($fixture.closure.path,$original)
    $binding=New-PinnedProofBinding Pure @{};$binding.schema='pinned-proof-bindings-v1'
    $pin=Write-PinnedTestJson (Join-Path $fixture.root 'old-bindings.json') $binding
    $bad=@{}+$specs.Pure;$bad.BindingsPath=$pin.path;$bad.BindingsBytes=$pin.bytes;$bad.BindingsSha256=$pin.sha256
    AuthenticateRefusal $bad;$cases++
    foreach($key in @('ClosureBytes','ClosureSha256','CommandBytes','CommandSha256')){
        $bad=@{}+$specs.Pure;$bad[$key]=if($key.EndsWith('Bytes')){0}else{'0'*64}
        AuthenticateRefusal $bad;$cases++
    }
    foreach($file in @('prepare.ps1','validate-helper.ps1','launch.ps1','run.ps1','restage\restage.ps1',
        'supervision\NonCopySupervisor.ps1','supervision\RestageSupervisor.ps1')){
        $path=Join-Path $fixture.scripts ('OfflinePatchImportProof\'+$file)
        foreach($route in @('call','dot')){
            $failure=$null
            try{if($route -ceq 'call'){& $path}else{. $path}}catch{$failure=$_.Exception.Message}
            if($failure -cne 'PinnedProof.UnsupportedDirectRoute'){throw "Direct $route route did not refuse: $file : $failure"};$cases++
        }
        $output=& (Join-Path $PSHOME $(if($IsWindows){'pwsh.exe'}else{'pwsh'})) -NoLogo -NoProfile -NonInteractive -File $path 2>&1
        if($LASTEXITCODE -eq 0 -or ($output|Out-String) -notlike '*PinnedProof.UnsupportedDirectRoute*'){throw 'Direct -File route did not refuse.'};$cases++
    }
    # A fresh host exercises every fixed child/parent loader mode; no production body is reached.
    $dependency=Join-Path $fixture.scripts 'OfflinePatchImportProof\Configuration.ps1'
    [IO.File]::WriteAllText($dependency,("[IO.File]::WriteAllText('"+$marker.Replace("'","''")+"','owned');throw 'owned'"),[Text.UTF8Encoding]::new($false))
    try{
        foreach($mode in $modes){
            $arguments=@('-NoLogo','-NoProfile','-NonInteractive','-File',(Join-Path $fixture.scripts 'WindowsDesktopProof\PinnedLoader.ps1'))
            foreach($key in $specs[$mode].Keys){$arguments+=@(('-'+$key),[string]$specs[$mode][$key])}
            $output=& (Join-Path $PSHOME $(if($IsWindows){'pwsh.exe'}else{'pwsh'})) @arguments 2>&1
            if($LASTEXITCODE -eq 0 -or ($output|Out-String) -notlike '*PinnedProof.Authentication:*' -or [IO.File]::Exists($marker)){throw "Fresh loader route failed: $mode : $output"};$cases++
        }
    }finally{[IO.File]::WriteAllBytes($dependency,$fixture.buffers['OfflinePatchImportProof\Configuration.ps1'])}
    if($IsWindows){
        # CreateProcess's current-directory limit is narrower than managed source reads.
        $supervisedRoot=Join-Path ([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))) 'TestResults\s'
        if([IO.Path]::Exists($supervisedRoot)){throw 'Consumed owned supervised fixture; retain evidence.'}
        $physical=New-ProofRestageFixture $supervisedRoot -CompileReferences (Get-ProofTestCompileReferences)
        $physical.config.host=Get-PinnedTestPin ([Environment]::ProcessPath)
        $physical.config.machine.systemRoot=$env:SystemRoot
        $pin=Write-PinnedTestJson (Join-Path $fixture.root 'supervised-configuration.json') $physical.config
        $result=Invoke-PinnedTestMode -Fixture $fixture -Mode SupervisedPure -Values @{
            configuration=$pin.path;configurationSha256=$pin.sha256}|ConvertFrom-Json -AsHashtable
        if(!$result.passed -or $result.selfImageObservation.code -cne 'image-observed' -or
            $result.imageObservation.code -cne 'image-observed'){
            throw 'Actual supervised Pure child image observations did not pass.'
        };$cases++
        [IO.Directory]::Delete($supervisedRoot,$true)
    }
    return $cases
}
