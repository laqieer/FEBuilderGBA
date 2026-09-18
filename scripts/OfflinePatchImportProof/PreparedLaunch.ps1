throw 'PinnedProof.UnsupportedDirectRoute'
function ProofEnvelope {
    function Write-PreparedJson([string]$Path,$Value) {
        Assert-ProofPath ([IO.Path]::GetDirectoryName($Path)) -Existing
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes(($Value|ConvertTo-Json -Depth 24 -Compress))
        Assert-Proof ($bytes.Length -le 4194304) 'Prepared JSON bound.'
        $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
        try{$stream.Write($bytes);$stream.Flush($true)}finally{$stream.Dispose()}
        return @{path=$Path;bytes=$bytes.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()}
    }
    function Get-PreparedPin([string]$Path) {
        Assert-ProofPath $Path -Existing
        $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        try{
            Assert-Proof ($stream.Length -le 268435456) 'Prepared file bound.'
            return @{path=$Path;bytes=$stream.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant()}
        }finally{$stream.Dispose()}
    }
    function New-PreparedDirectory([string]$Path) {
        Assert-ProofPath $Path
        Assert-Proof (![IO.Path]::Exists($Path)) 'Prepared destination consumed.'
        [void][IO.Directory]::CreateDirectory($Path)
        Assert-ProofPath $Path -Existing
    }
    function Get-PreparedInventory([string]$Root) {
        $files=[Collections.Generic.List[object]]::new()
        $directories=[Collections.Generic.List[string]]::new()
        $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($Root);$total=0L
        while($pending.Count){
            foreach($path in [IO.Directory]::EnumerateFileSystemEntries($pending.Pop())){
                $relative=[IO.Path]::GetRelativePath($Root,$path).Replace('/','\')
                Assert-ProofPublicResource $relative -SyntheticFixture
                Assert-ProofPath $path -Existing
                if([IO.Directory]::Exists($path)){
                    $directories.Add($relative);$pending.Push($path)
                }else{
                    $pin=Get-PreparedPin $path;$total+=$pin.bytes
                    $files.Add(@{path=$relative;bytes=$pin.bytes;sha256=$pin.sha256})
                }
                Assert-Proof ($files.Count -le 10000 -and $directories.Count -le 20000 -and $total -le 2147483648L) 'Prepared inventory bound.'
            }
        }
        return @{files=@($files|Sort-Object path -CaseSensitive);directories=@($directories|Sort-Object -CaseSensitive)}
    }
    function Copy-PreparedFile([string]$From,[string]$To,$Row,[string]$Relative) {
        Assert-ProofPublicResource $Relative -SyntheticFixture
        $pin=@{path=$From;bytes=$Row.bytes;sha256=$Row.sha256}
        $null=Read-ProofPinnedBytes $pin 268435456
        Assert-ProofPath $To
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($To))
        [IO.File]::Copy($From,$To,$false)
        $null=Read-ProofPinnedBytes @{path=$To;bytes=$Row.bytes;sha256=$Row.sha256} 268435456
    }
    function Read-PreparedConfiguration([string]$Configuration,[string]$ConfigurationSha256) {
        . (Get-PinnedProofLibrary -Library Configuration)
        Assert-Proof ($IsWindows -and $PSVersionTable.PSEdition -ceq 'Core' -and $PSVersionTable.PSVersion -ge [version]'7.5') 'Prepared Windows PowerShell host.'
        $c=Read-ProofPinnedJson @{path=$Configuration;bytes=([IO.FileInfo]$Configuration).Length;sha256=$ConfigurationSha256}
        Assert-ProofKeys $c @('schema','preparedId','applicationTree','baseConfiguration','inputManifest','receipts','root','evidenceRoot','workspaceIds','bundle','bootstrap','jsonReference','slots','authority')
        Assert-Proof ($c.schema -ceq 'windows-prepared-configuration-v1' -and $c.preparedId -cmatch '^[0-9a-f]{32}$' -and
            $c.applicationTree -cmatch '^[0-9a-f]{40}$') 'Prepared configuration identity.'
        foreach($name in @('root','evidenceRoot')){Assert-ProofPath $c[$name] -Existing}
        Assert-Proof ($c.root -cne $c.evidenceRoot -and !$c.evidenceRoot.StartsWith($c.root+'\',[StringComparison]::OrdinalIgnoreCase)) 'Separate control root.'
        Assert-ProofPin $c.baseConfiguration;Assert-ProofPin $c.inputManifest
        Assert-ProofKeys $c.receipts @('Build','Validate','Inputs')
        foreach($r in $c.receipts.Values){Assert-ProofPin $r}
        $ids=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        Assert-Proof ($c.workspaceIds -is [array] -and $c.workspaceIds.Count -ge 1 -and $c.workspaceIds.Count -le 8) 'Prepared capacity 1..8.'
        foreach($id in $c.workspaceIds){Assert-Proof ($id -is [string] -and $id -cmatch '^[0-9a-f]{32}$' -and $ids.Add($id)) 'Workspace ID.'}
        Assert-Proof ($c.slots -is [array] -and $c.slots.Count -le $c.workspaceIds.Count) 'Slot bound.'
        $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($slot in $c.slots){
            Assert-ProofKeys $slot @('workspaceId','manifest','runner','launch')
            Assert-Proof ($ids.Contains($slot.workspaceId) -and $seen.Add($slot.workspaceId)) 'Slot membership.'
            Assert-ProofPin $slot.manifest;Assert-ProofPin $slot.runner;Assert-ProofPin $slot.launch
        }
        if($null -ne $c.bundle){Assert-ProofPin $c.bundle}
        if($null -ne $c.bootstrap){
            Assert-ProofPin $c.bootstrap
            Assert-Proof ($c.bootstrap.path -ceq (Join-Path $c.root 'bundle\PreparedDesktop.dll')) 'Owned prepared bootstrap.'
        }
        Assert-ProofPin $c.jsonReference
        Assert-Proof ($c.jsonReference.path -ceq (Join-Path $PSHOME 'ref\System.Text.Json.dll')) 'Installed JSON compiler reference.'
        Assert-ProofKeys $c.authority @('reference','sha256','scenario','expiresUtc','revocationPath','replenishmentAllowed')
        Assert-Proof ($c.authority.reference -cmatch '^https://github\.com/laqieer/FEBuilderGBA/issues/[0-9]+#issuecomment-[0-9]+$' -and
            $c.authority.sha256 -cmatch '^[0-9a-f]{64}$' -and $c.authority.scenario -ceq 'offline-patch-import-v1' -and
            $c.authority.replenishmentAllowed -is [bool]) 'Prepared authority metadata.'
        Assert-Proof ($c.authority.revocationPath -ceq (Join-Path $c.evidenceRoot ($c.preparedId+'.revoked'))) 'Local revocation location.'
        Assert-ProofPath $c.authority.revocationPath
        if($null -ne $c.authority.expiresUtc){
            $expiry=[DateTimeOffset]::MinValue
            Assert-Proof (($c.authority.expiresUtc -is [string] -and [DateTimeOffset]::TryParse($c.authority.expiresUtc,[ref]$expiry)) -or
                $c.authority.expiresUtc -is [DateTime]) 'Expiry format.'
        }
        return ,$c
    }
    function Assert-PreparedAuthority($C) {
        Assert-Proof (![IO.Path]::Exists($C.authority.revocationPath)) 'prepared-revoked'
        if($null -ne $C.authority.expiresUtc){
            $expires=if($C.authority.expiresUtc -is [DateTime]){[DateTimeOffset]$C.authority.expiresUtc}else{[DateTimeOffset]::Parse($C.authority.expiresUtc)}
            Assert-Proof ([DateTimeOffset]::UtcNow -lt $expires) 'prepared-expired'
        }
    }
    function Read-PreparedBasis($C) {
        $base=Read-ProofConfiguration $C.baseConfiguration.path $C.baseConfiguration.sha256 -Purpose Gui
        $null=Assert-ProofSource $base $PSScriptRoot
        $input=Read-ProofInput $C.inputManifest.path $C.inputManifest.sha256 $base
        Assert-Proof ($base.host.path -ceq [Environment]::ProcessPath) 'Prepared host.'
        return @{base=$base;input=$input}
    }
    function Import-PreparedBootstrap($C) {
        Assert-Proof ($null -ne $C.bootstrap) 'Pinned prepared bootstrap required.'
        $bytes=Read-ProofPinnedBytes $C.bootstrap 268435456
        $memory=[IO.MemoryStream]::new($bytes,$false)
        try{return [Runtime.Loader.AssemblyLoadContext]::Default.LoadFromStream($memory)}finally{$memory.Dispose()}
    }
    function Read-PreparedJson($Pin,$Bootstrap) {
        if(!$Bootstrap){return ,(Read-ProofPinnedJson $Pin)}
        $bytes=Read-ProofPinnedBytes $Pin
        return ,($Bootstrap.GetType('PreparedJson',$true).GetMethod('Parse').Invoke($null,@(,$bytes)))
    }
    function Read-PreparedBundle($C,$Bootstrap=$null) {
        Assert-Proof ($null -ne $C.bundle) 'Bundle pin required.'
        $b=Read-PreparedJson $C.bundle $Bootstrap
        Assert-ProofKeys $b @('schema','preparedId','applicationSource','applicationTree','sourceSha256','protocolSha256','inputSha256','receipts','inventory','helper','runtimeAssemblies','installedFiles')
        Assert-Proof ($b.schema -ceq 'windows-prepared-bundle-v1' -and $b.preparedId -ceq $C.preparedId -and
            $b.applicationTree -ceq $C.applicationTree -and $b.inputSha256 -ceq $C.inputManifest.sha256 -and
            $b.protocolSha256 -ceq (Get-PinnedProofClosureIdentity)) 'Bundle binding.'
        Assert-ProofKeys $b.inventory @('files','directories')
        Assert-PreparedRuntime $C $b
        if($C.bootstrap){Assert-Proof ($b.helper.sha256 -ceq $C.bootstrap.sha256 -and $b.helper.bytes -eq $C.bootstrap.bytes) 'Bootstrap derived from bundle.'}
        return ,$b
    }
    function Assert-PreparedRuntime($C,$b) {
        Assert-ProofPin $b.helper
        Assert-Proof ($b.helper.path -ceq (Join-Path $C.root 'bundle\PreparedDesktop.dll')) 'Helper location.'
        $runtimeNames=@('System.Private.Windows.Core.dll','System.Private.Windows.GdiPlus.dll','WindowsBase.dll','UIAutomationTypes.dll','UIAutomationClient.dll','Microsoft.Win32.SystemEvents.dll','System.Drawing.Common.dll')
        Assert-Proof ($b.runtimeAssemblies -is [array] -and $b.runtimeAssemblies.Count -eq 7 -and
            $b.installedFiles -is [array] -and $b.installedFiles.Count -eq 2) 'Prepared runtime/fixture closure.'
        $runtimeSeen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach($row in $b.runtimeAssemblies){
            Assert-ProofKeys $row @('path','bytes','sha256')
            Assert-Proof ($row.path -cin $runtimeNames -and $runtimeSeen.Add($row.path)) 'Prepared runtime member.'
            Assert-ProofPin @{path=(Join-Path $C.root ('bundle\support\'+$row.path));bytes=$row.bytes;sha256=$row.sha256}
        }
    }
    function Import-PreparedHelper($C,$B,$Bootstrap=$null) {
        . (Join-Path $PSScriptRoot 'RuntimeBinding.ps1')
        Assert-PreparedRuntime $C $B
        $base=Read-ProofConfiguration $C.baseConfiguration.path $C.baseConfiguration.sha256 -Purpose Gui
        Assert-Proof ($base.host.path -ceq [Environment]::ProcessPath) 'Prepared host image path.'
        $null=Read-ProofPinnedBytes $base.host 16777216
        $basis=@{base=$base;input=@{runtimeAssemblies=$B.runtimeAssemblies;installedFiles=$B.installedFiles}}
        $basis.runtimeLeases=[Collections.Generic.List[object]]::new()
        Assert-Proof ($B.sourceSha256 -ceq $basis.base.sourceManifest.sha256 -and
            $B.applicationSource -ceq $basis.base.applicationSource) 'Current reviewed source binding.'
        $completed=$false
        try {
        foreach($row in $basis.input.runtimeAssemblies){
            $support=Join-Path $C.root ('bundle\support\'+$row.path)
            $hostPath=Join-Path $PSHOME $row.path
            Assert-ProofPath $support -Existing
            $basis.runtimeLeases.Add([IO.File]::Open($support,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read))
            $null=Read-ProofPinnedBytes @{path=$support;bytes=$row.bytes;sha256=$row.sha256} 268435456
            $a=[Reflection.Assembly]::LoadFrom($support)
            $null=Confirm-PinnedRuntimeBinding $support $hostPath ([Reflection.AssemblyName]::GetAssemblyName($support).FullName) `
                $row.bytes $row.sha256 $a.Location $a.FullName {param($p) $pin=Get-PreparedPin $p;@{bytes=[long]$pin.bytes;sha256=$pin.sha256}}
        }
        if(!$Bootstrap){
            $bytes=Read-ProofPinnedBytes $B.helper 268435456
            $memory=[IO.MemoryStream]::new($bytes,$false)
            try{[void][Runtime.Loader.AssemblyLoadContext]::Default.LoadFromStream($memory)}finally{$memory.Dispose()}
        }
        $completed=$true
        return $basis
        } finally {
            if(!$completed){foreach($held in $basis.runtimeLeases){$held.Dispose()}}
        }
    }
    function ConvertTo-PreparedStart($Start) {
        $environment=@{}
        foreach($entry in $Start.Environment.GetEnumerator()){$environment[$entry.Key]=[string]$entry.Value}
        return @{fileName=$Start.FileName;workingDirectory=$Start.WorkingDirectory;arguments=@($Start.ArgumentList);
            environment=$environment;redirectInput=$Start.RedirectStandardInput}
    }
    function Read-PreparedWorkspace($C,$B,$Pin,[string]$WorkspaceId,$Bootstrap=$null) {
        $slot=Read-PreparedJson $Pin $Bootstrap
        Assert-ProofKeys $slot @('schema','preparedId','workspaceId','bundleSha256','root','inventory','applicationStart')
        Assert-Proof ($slot.schema -ceq 'windows-prepared-workspace-v1' -and $slot.preparedId -ceq $C.preparedId -and
            $slot.workspaceId -ceq $WorkspaceId -and $WorkspaceId -cin $C.workspaceIds -and
            $slot.bundleSha256 -ceq $C.bundle.sha256 -and $slot.root -ceq (Join-Path $C.root ('workspace-'+$WorkspaceId))) 'Workspace binding.'
        Assert-ProofKeys $slot.inventory @('files','directories')
        Assert-Proof ($slot.inventory.files -is [array] -and $slot.inventory.files.Count -ge 1 -and
            $slot.inventory.files.Count -le 10000 -and $slot.inventory.directories -is [array] -and
            $slot.inventory.directories.Count -le 20000) 'Workspace inventory shape.'
        [PreparedContentLease]::ValidateManifest($slot.inventory,$B.inventory)
        Assert-Proof ($slot.applicationStart.fileName -ceq (Join-Path $slot.root 'app\FEBuilderGBA.Avalonia.exe') -and
            $slot.applicationStart.workingDirectory -ceq (Join-Path $slot.root 'app') -and
            $slot.applicationStart.arguments.Count -eq 1 -and
            $slot.applicationStart.arguments[0] -ceq ('--rom='+(Join-Path $slot.root 'fixtures\zipdb-proof.gba'))) 'Frozen ordinary application payload.'
        return ,$slot
    }
    function Read-PreparedStart($Value) {
        Assert-ProofKeys $Value @('fileName','workingDirectory','arguments','environment','redirectInput')
        Assert-ProofPath $Value.fileName -Existing;Assert-ProofPath $Value.workingDirectory -Existing
        Assert-Proof ($Value.arguments -is [array] -and $Value.arguments.Count -le 40 -and
            $Value.environment -is [Collections.IDictionary] -and $Value.environment.Count -le 48 -and $Value.redirectInput -is [bool]) 'Frozen process shape.'
        $start=[Diagnostics.ProcessStartInfo]::new($Value.fileName)
        $start.WorkingDirectory=$Value.workingDirectory;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.RedirectStandardInput=$Value.redirectInput
        $start.Environment.Clear()
        foreach($arg in $Value.arguments){Assert-Proof ($arg -is [string] -and $arg.Length -le 4096) 'Frozen argument.';$start.ArgumentList.Add($arg)}
        foreach($key in $Value.environment.Keys){Assert-Proof ($key -is [string] -and $Value.environment[$key] -is [string]) 'Frozen environment.';$start.Environment[$key]=$Value.environment[$key]}
        return $start
    }
    function Invoke-PrepareBundleEntry {
        param([string]$Configuration,[string]$ConfigurationSha256)
        . (Get-PinnedProofLibrary -Library Configuration)
        . (Get-PinnedProofLibrary -Library Run)
        $c=Read-PreparedConfiguration $Configuration $ConfigurationSha256
        Assert-PreparedAuthority $c
        $basis=Read-PreparedBasis $c;$base=$basis.base;$input=$basis.input
        foreach($stage in @('Build','Validate','Inputs')){
            $r=Read-ProofPinnedJson $c.receipts[$stage]
            Assert-Proof ($r.stage -ceq $stage -and $r.passed -is [bool] -and $r.passed -and $r.runId -ceq $input.runId -and
                $r.sourceManifestSha256 -ceq $base.sourceManifest.sha256 -and
                $r.helperSourceManifestSha256 -ceq $base.sourceManifest.sha256) 'Passing prepared provenance.'
            if($stage -ceq 'Inputs'){Assert-Proof ($r.inputManifestSha256 -ceq $c.inputManifest.sha256) 'Inputs provenance.'}
        }
        $bundle=Join-Path $c.root 'bundle';New-PreparedDirectory $bundle
        $inputRoot=[IO.Path]::GetDirectoryName($c.inputManifest.path)
        foreach($group in @(@{from='app';to='app';rows=$input.appFiles},
            @{from=('desktop-proof-'+$input.runId);to='fixtures';rows=$input.fixtures},
            @{from='support';to='support';rows=$input.runtimeAssemblies})){
            foreach($row in $group.rows){
                $relative=$group.to+'\'+$row.path
                Copy-PreparedFile (Join-Path $inputRoot ($group.from+'\'+$row.path)) (Join-Path $bundle $relative) $row $relative
            }
        }
        $manifest=$input
        CheckArchive (Join-Path $bundle 'fixtures\zipdb-valid.zip') $true
        CheckArchive (Join-Path $bundle 'fixtures\zipdb-invalid.zip') $false
        $refs=@(foreach($row in $input.compileReferences){
            $null=Read-ProofPinnedBytes @{path=$row.path;bytes=$row.bytes;sha256=$row.sha256} 268435456;$row.path
        })
        $null=Read-ProofPinnedBytes $c.jsonReference 268435456
        $refs+=,$c.jsonReference.path
        $runtime=@(foreach($row in $input.runtimeAssemblies){Join-Path $bundle ('support\'+$row.path)})
        $helper=Join-Path $bundle 'PreparedDesktop.dll'
        Add-Type -Path @((Join-Path $PSScriptRoot 'Readiness.cs'),(Join-Path $PSScriptRoot 'Policy.cs'),
            (Join-Path $PSScriptRoot 'Desktop.cs'),(Join-Path $PSScriptRoot 'PreparedLaunch.cs')) `
            -ReferencedAssemblies ($refs+$runtime) -OutputAssembly $helper -OutputType Library -ErrorAction Stop -WarningAction Stop
        $inventory=Get-PreparedInventory $bundle
        $pin=Write-PreparedJson (Join-Path $c.root 'bundle.json') @{
            schema='windows-prepared-bundle-v1';preparedId=$c.preparedId;applicationSource=$base.applicationSource;
            applicationTree=$c.applicationTree;sourceSha256=$base.sourceManifest.sha256;inputSha256=$c.inputManifest.sha256;
            protocolSha256=(Get-PinnedProofClosureIdentity);runtimeAssemblies=$input.runtimeAssemblies;installedFiles=$input.installedFiles;
            receipts=$c.receipts;inventory=$inventory;helper=(Get-PreparedPin $helper)}
        $createdLock=[IO.File]::Open((Join-Path $c.evidenceRoot ($c.preparedId+'.lock')),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        $createdLock.Dispose()
        $pin|ConvertTo-Json -Compress
    }
    function Invoke-PrepareWorkspaceEntry {
        param([string]$Configuration,[string]$ConfigurationSha256,[string]$workspaceId)
        . (Get-PinnedProofLibrary -Library Configuration)
        $c=Read-PreparedConfiguration $Configuration $ConfigurationSha256
        Assert-PreparedAuthority $c
        Assert-Proof ($c.slots.Count -eq 0 -or $c.authority.replenishmentAllowed) 'Workspace replenishment not authorized.'
        Assert-Proof ($workspaceId -cin $c.workspaceIds) 'Declared workspace.'
        $b=Read-PreparedBundle $c;$basis=Read-PreparedBasis $c;$base=$basis.base
        $root=Join-Path $c.root ('workspace-'+$workspaceId)
        New-PreparedDirectory $root
        foreach($row in $b.inventory.files){
            if($row.path.StartsWith('app\',[StringComparison]::Ordinal) -or $row.path.StartsWith('fixtures\',[StringComparison]::Ordinal)){
                Copy-PreparedFile (Join-Path $c.root ('bundle\'+$row.path)) (Join-Path $root $row.path) $row $row.path
            }
        }
        foreach($directory in @('profile\Desktop','profile\AppData\Roaming','profile\AppData\Local','scratch','empty-roms')){
            [void][IO.Directory]::CreateDirectory((Join-Path $root $directory))
        }
        $null=Initialize-ProofEmptyPatchLibrary (Join-Path $root 'app')
        [IO.File]::WriteAllText((Join-Path $root 'empty-git.config'),'',[Text.UTF8Encoding]::new($false))
        $app=[Diagnostics.ProcessStartInfo]::new((Join-Path $root 'app\FEBuilderGBA.Avalonia.exe'))
        $app.WorkingDirectory=Join-Path $root 'app';$app.ArgumentList.Add('--rom='+(Join-Path $root 'fixtures\zipdb-proof.gba'))
        $app.Environment.Clear()
        $envData=@{
            SystemRoot=$base.machine.systemRoot;windir=$base.machine.systemRoot;SystemDrive=([IO.Path]::GetPathRoot($base.machine.systemRoot).TrimEnd('\'));OS='Windows_NT'
            PATH="$($base.machine.systemRoot)\System32;$($base.machine.systemRoot)";ComSpec=(Join-Path $base.machine.systemRoot 'System32\cmd.exe')
            PATHEXT='.COM;.EXE;.BAT;.CMD';DOTNET_ROOT=$base.machine.dotnetRoot
            TEMP=(Join-Path $root 'scratch');TMP=(Join-Path $root 'scratch');USERPROFILE=(Join-Path $root 'profile');HOME=(Join-Path $root 'profile')
            APPDATA=(Join-Path $root 'profile\AppData\Roaming');LOCALAPPDATA=(Join-Path $root 'profile\AppData\Local')
            HTTP_PROXY='http://127.0.0.1:9';HTTPS_PROXY='http://127.0.0.1:9';ALL_PROXY='http://127.0.0.1:9';NO_PROXY='127.0.0.1,localhost'
            GIT_CONFIG_NOSYSTEM='1';GIT_CONFIG_COUNT='0';GIT_CONFIG_GLOBAL=(Join-Path $root 'empty-git.config');GIT_TERMINAL_PROMPT='0'
            DOTNET_CLI_TELEMETRY_OPTOUT='1';DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1';DOTNET_EnableDiagnostics='0';ROMS_DIR=(Join-Path $root 'empty-roms')
        }
        foreach($key in $envData.Keys){$app.Environment[$key]=$envData[$key]}
        $control=Join-Path $c.evidenceRoot ('slot-'+$workspaceId);New-PreparedDirectory $control
        foreach($name in @('profile\AppData\Roaming','profile\AppData\Local','scratch')){[void][IO.Directory]::CreateDirectory((Join-Path $control $name))}
        $slot=Write-PreparedJson (Join-Path $control 'workspace.json') @{
            schema='windows-prepared-workspace-v1';preparedId=$c.preparedId;workspaceId=$workspaceId;
            bundleSha256=$c.bundle.sha256;root=$root;inventory=(Get-PreparedInventory $root);applicationStart=(ConvertTo-PreparedStart $app)}
        $runnerPin=Write-PreparedJson (Join-Path $control 'runner.json') @{
            schema='windows-prepared-runner-v1';preparedId=$c.preparedId;workspaceId=$workspaceId;bundleSha256=$c.bundle.sha256;
            workspace=$slot;root=$root;applicationStart=(ConvertTo-PreparedStart $app);
            helper=$b.helper;runtimeAssemblies=$b.runtimeAssemblies;installedFiles=$b.installedFiles;
            sourceSha256=$b.sourceSha256;protocolSha256=$b.protocolSha256;applicationSource=$b.applicationSource}
        $arguments=New-PinnedProofChildArguments -ChildMode PreparedRun -Path (Join-Path $control 'runner-bindings.json') -Values @{
            configuration=$Configuration;configurationSha256=$ConfigurationSha256;workspaceId=$workspaceId;
            workspaceManifest=$runnerPin.path;workspaceManifestSha256=$runnerPin.sha256}
        $runner=[Diagnostics.ProcessStartInfo]::new($base.host.path);$runner.WorkingDirectory=$control
        foreach($arg in $arguments){$runner.ArgumentList.Add($arg)}
        $runner.Environment.Clear()
        $envData=@{
            SystemRoot=$base.machine.systemRoot;windir=$base.machine.systemRoot;SystemDrive=([IO.Path]::GetPathRoot($base.machine.systemRoot).TrimEnd('\'))
            PATH=(Join-Path $base.machine.systemRoot 'System32');PSModulePath=(Join-Path $PSHOME 'Modules')
            PSModuleAnalysisCachePath=(Join-Path $control 'scratch\ModuleAnalysisCache');TEMP=(Join-Path $control 'scratch');TMP=(Join-Path $control 'scratch')
            USERPROFILE=(Join-Path $control 'profile');HOME=(Join-Path $control 'profile')
            APPDATA=(Join-Path $control 'profile\AppData\Roaming');LOCALAPPDATA=(Join-Path $control 'profile\AppData\Local')
            HTTP_PROXY='http://127.0.0.1:9';HTTPS_PROXY='http://127.0.0.1:9';ALL_PROXY='http://127.0.0.1:9'
            DOTNET_CLI_TELEMETRY_OPTOUT='1';DOTNET_EnableDiagnostics='0';POWERSHELL_TELEMETRY_OPTOUT='1'
        }
        foreach($key in $envData.Keys){$runner.Environment[$key]=$envData[$key]}
        $launch=Write-PreparedJson (Join-Path $control 'launch.json') (ConvertTo-PreparedStart $runner)
        @{workspaceId=$workspaceId;manifest=$slot;runner=$runnerPin;launch=$launch}|ConvertTo-Json -Depth 6 -Compress
    }
    function Invoke-ActivatePreparedEntry {
        param([string]$Configuration,[string]$ConfigurationSha256,[string]$activationId,[string]$userTurn)
        . (Get-PinnedProofLibrary -Library Configuration)
        . (Get-PinnedProofLibrary -Library Launch)
        . (Join-Path $PSScriptRoot 'ProcessImage.ps1')
        Invoke-PreparedActivationCore
    }
    function Select-PreparedWorkspace($C) {
        foreach($slot in $C.slots){
            $control=Join-Path $C.evidenceRoot ('slot-'+$slot.workspaceId)
            Assert-ProofPath $control -Existing
            if(![IO.Path]::Exists((Join-Path $control 'committed.json')) -and
                ![IO.Path]::Exists((Join-Path $control 'active.json'))){return $slot}
        }
        return $null
    }
    function Invoke-PreparedActivationCore {
        $c=Read-PreparedConfiguration $Configuration $ConfigurationSha256
        Assert-Proof ($activationId -cmatch '^[0-9a-f]{32}$' -and $userTurn -cmatch '^[A-Za-z0-9._:-]{1,128}$') 'Explicit caller turn attestation.'
        $lock=$null;$lease=$null;$activePath=$null;$basis=$null
        $result=[ordered]@{schema='windows-prepared-activation-v1';preparedId=$c.preparedId;activationId=$activationId;userTurn=$userTurn;
            entryTicks=$PinnedProofEntryTicks;enteredUtc=[DateTime]::UtcNow.ToString('o');state='refused';attemptId=$null;stage='reservation'}
        $launchRoot=Join-Path $c.evidenceRoot ('activation-'+$activationId);New-PreparedDirectory $launchRoot
        try{
            Assert-PreparedAuthority $c
            $lockPath=Join-Path $c.evidenceRoot ($c.preparedId+'.lock')
            Assert-ProofPath $lockPath -Existing
            $lock=[IO.File]::Open($lockPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
            Assert-Proof ($lock.Length -eq 0) 'Owned reservation sentinel.'
            $result.stage='helper'
            $bootstrap=Import-PreparedBootstrap $c
            $b=Read-PreparedBundle $c $bootstrap;$basis=Import-PreparedHelper $c $b $bootstrap
            [PreparedContentLease]::Deadline($PinnedProofEntryTicks)
            $selected=Select-PreparedWorkspace $c
            Assert-Proof ($null -ne $selected) 'prepared-capacity-exhausted'
            $result.stage='inventory'
            $slotData=Read-PreparedWorkspace $c $b $selected.manifest $selected.workspaceId $bootstrap
            $result.stage='content'
            $lease=[PreparedContentLease]::Open($slotData.root,$slotData.inventory,$PinnedProofEntryTicks)
            $result.stage='runner'
            $start=Read-PreparedStart (Read-ProofPinnedJson $selected.launch)
            Assert-Proof ($start.FileName -ceq $basis.base.host.path) 'Frozen runner host.'
            $control=Join-Path $c.evidenceRoot ('slot-'+$selected.workspaceId)
            $activePath=Join-Path $control 'active.json'
            $self=[Diagnostics.Process]::GetCurrentProcess()
            try{
                $request=@{activationId=$activationId;attemptId=([guid]::NewGuid().ToString('N'));preparedId=$c.preparedId;
                    parentPid=$self.Id;parentTicks=$self.StartTime.ToUniversalTime().Ticks;entryTicks=$PinnedProofEntryTicks;outputRoot=$launchRoot}
            }finally{$self.Dispose()}
            $null=Write-PreparedJson $activePath $request
            $manifest=@{runId=$request.attemptId}
            $InputManifestSha256=$c.inputManifest.sha256;$SourceManifestSha256=$b.sourceSha256
            $runRoot=$slotData.root;$root=$c.root;$pwsh=$basis.base.host.path
            $PreparedCommitPath=Join-Path $control 'committed.json'
            function Plain([string]$p){Assert-ProofPath $p -Existing}
            [PreparedContentLease]::Deadline($PinnedProofEntryTicks)
            $receipt=Invoke-ProofSupervisedRunner -ActivationEntryTicks $PinnedProofEntryTicks -CommitPath $PreparedCommitPath
            $result.supervision=$receipt
            $result.state=if([IO.File]::Exists($PreparedCommitPath)){'attempt-terminal'}else{'refused-before-commit'}
            if([IO.File]::Exists((Join-Path $launchRoot 'readiness.json'))){
                $readiness=Read-ProofJsonFile (Join-Path $launchRoot 'readiness.json')
                $result.readiness=$readiness
                if(!$readiness.ready -and $receipt.execution_boundary_exited -and
                    ![IO.Path]::Exists((Join-Path $control 'committed.json')) -and ![IO.Path]::Exists((Join-Path $runRoot 'app-identity.json'))){
                    $result.state='desktop-refused-package-reusable'
                }else{$result.attemptId=$request.attemptId}
            }
            if($result.state -ceq 'desktop-refused-package-reusable'){[IO.File]::Delete($activePath);$activePath=$null}
        }catch{
            $result.failureType=$_.Exception.GetType().Name
            $reason=$_.Exception.GetBaseException().Message
            $result.failureCode=if($reason -cmatch '^prepared-[a-z-]{1,64}$'){$reason}else{'prepared-admission-refused'}
            $result.elapsedAtFailureMs=([Diagnostics.Stopwatch]::GetTimestamp()-$PinnedProofEntryTicks)*1000.0/[Diagnostics.Stopwatch]::Frequency
        }
        finally{
            if($lease){$lease.Dispose()};if($lock){$lock.Dispose()}
            if($basis -and $basis.ContainsKey('runtimeLeases')){foreach($held in $basis.runtimeLeases){$held.Dispose()}}
            $result.completedUtc=[DateTime]::UtcNow.ToString('o')
            $null=Write-PreparedJson (Join-Path $launchRoot 'activation.json') $result
        }
        $result|ConvertTo-Json -Depth 12 -Compress
    }
    function Invoke-PreparedRunEntry {
        param([string]$Configuration,[string]$ConfigurationSha256,[string]$workspaceId,[string]$workspaceManifest,[string]$workspaceManifestSha256)
        . (Get-PinnedProofLibrary -Library Configuration)
        . (Get-PinnedProofLibrary -Library Run)
        . (Join-Path $PSScriptRoot 'ProcessImage.ps1')
        Invoke-PreparedRunCore -Readiness { [BoundedWindowsReadiness]::Capture() } -Workflow { Invoke-ProofAppWorkflow }
    }
    function Invoke-PreparedRunCore {
        param([Parameter(Mandatory)][scriptblock]$Readiness,[Parameter(Mandatory)][scriptblock]$Workflow)
        Assert-Proof ([Threading.Thread]::CurrentThread.ApartmentState -eq 'STA') 'Prepared runner STA required.'
        $c=Read-PreparedConfiguration $Configuration $ConfigurationSha256;Assert-PreparedAuthority $c
        $bootstrap=Import-PreparedBootstrap $c
        $slot=Read-PreparedJson @{path=$workspaceManifest;bytes=([IO.FileInfo]$workspaceManifest).Length;sha256=$workspaceManifestSha256} $bootstrap
        Assert-ProofKeys $slot @('schema','preparedId','workspaceId','bundleSha256','workspace','root','applicationStart',
            'helper','runtimeAssemblies','installedFiles','sourceSha256','protocolSha256','applicationSource')
        Assert-Proof ($slot.schema -ceq 'windows-prepared-runner-v1' -and $slot.workspaceId -ceq $workspaceId -and
            $workspaceId -cin $c.workspaceIds -and $slot.preparedId -ceq $c.preparedId -and
            $slot.bundleSha256 -ceq $c.bundle.sha256 -and $slot.protocolSha256 -ceq (Get-PinnedProofClosureIdentity) -and
            $slot.root -ceq (Join-Path $c.root ('workspace-'+$workspaceId)) -and
            $slot.helper.sha256 -ceq $c.bootstrap.sha256 -and $slot.helper.bytes -eq $c.bootstrap.bytes) 'Frozen runner payload binding.'
        $b=$slot;$basis=Import-PreparedHelper $c $b $bootstrap
        $control=Join-Path $c.evidenceRoot ('slot-'+$workspaceId)
        Assert-Proof ([Environment]::CurrentDirectory -ceq $control -and $slot.workspaceId -ceq $workspaceId -and
            $slot.preparedId -ceq $c.preparedId -and $slot.bundleSha256 -ceq $c.bundle.sha256) 'Prepared child binding.'
        $request=Read-ProofJsonFile (Join-Path $control 'active.json')
        Assert-ProofKeys $request @('activationId','attemptId','preparedId','parentPid','parentTicks','entryTicks','outputRoot')
        Assert-Proof ($request.preparedId -ceq $c.preparedId -and $request.outputRoot -ceq
            (Join-Path $c.evidenceRoot ('activation-'+$request.activationId))) 'Active record binding.'
        $parent=[Diagnostics.Process]::GetProcessById([int]$request.parentPid)
        try{
            $parentHandle=$parent.SafeHandle
            Assert-Proof (!$parent.HasExited -and $parent.StartTime.ToUniversalTime().Ticks -eq $request.parentTicks) 'Live retained preparation owner.'
        }catch{$parent.Dispose();foreach($held in $basis.runtimeLeases){$held.Dispose()};throw}
        $runRoot=$slot.root;$manifest=@{runId=$request.attemptId}
        $InputManifestSha256=$c.inputManifest.sha256;$SourceManifestSha256=$b.sourceSha256
        $report=[ordered]@{passed=$false;run_id=$request.attemptId;input_manifest_sha256=$InputManifestSha256;
            source_manifest_sha256=$SourceManifestSha256;gui=$null;launch_requested_utc=$null}
        $clock=[Diagnostics.Stopwatch]::StartNew();$currentHost=[Diagnostics.Process]::GetCurrentProcess();$workflowState=@{process=$null}
        function Budget {if($clock.ElapsedMilliseconds -ge 420000){throw 'Runner deadline.'}}
        function Plain([string]$p){Assert-ProofPath $p -Existing}
        function NewText([string]$p,[string]$text){
            $stream=[IO.File]::Open($p,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
            try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($text);$stream.Write($bytes)}finally{$stream.Dispose()}
        }
        try{
            $start=Read-PreparedStart $slot.applicationStart
            $exe=$start.FileName
            Assert-Proof ($exe -ceq (Join-Path $runRoot 'app\FEBuilderGBA.Avalonia.exe')) 'Frozen app location.'
            $descriptor=@($basis.input.installedFiles|Where-Object path -CEQ 'proof\PATCH_offline.txt')[0]
            $payload=@($basis.input.installedFiles|Where-Object path -CEQ 'proof\payload.bin')[0]
            [PreparedContentLease]::Deadline($request.entryTicks)
            try{$observation=& $Readiness}
            catch{
                $null=Write-PreparedJson (Join-Path $request.outputRoot 'readiness.json') @{
                    ready=$false;status='unknown';observation=$null;failureType=$_.Exception.GetType().Name;
                    sampleUtc=[DateTime]::UtcNow.ToString('o')}
                return
            }
            $null=Write-PreparedJson (Join-Path $request.outputRoot 'readiness.json') @{
                ready=[bool]$observation.Ready;status='observed';observation=$observation;sampleUtc=[DateTime]::UtcNow.ToString('o')}
            Assert-PreparedAuthority $c
            Assert-Proof (!$parent.HasExited -and !$parentHandle.IsClosed -and !$parentHandle.IsInvalid) 'Prepared owner exited.'
            if(![PreparedAttempt]::ObserveAndCommit($observation.Ready,(Join-Path $control 'committed.json'),
                ($request|ConvertTo-Json -Compress),$request.entryTicks)){return}
            [PreparedContentLease]::Deadline($request.entryTicks)
            $report.launch_requested_utc=[DateTime]::UtcNow.ToString('o')
            $report.activationToStartMs=([Diagnostics.Stopwatch]::GetTimestamp()-$request.entryTicks)*1000.0/[Diagnostics.Stopwatch]::Frequency
            & $Workflow
        }catch{$report.failure_type=$_.Exception.GetType().Name}
        finally{
            if($workflowState.process){$workflowState.process.Dispose()};$currentHost.Dispose();$parent.Dispose()
            foreach($held in $basis.runtimeLeases){$held.Dispose()}
            if([IO.File]::Exists((Join-Path $control 'committed.json'))){$null=Write-PreparedJson (Join-Path $runRoot 'result.json') $report}
        }
    }
}
