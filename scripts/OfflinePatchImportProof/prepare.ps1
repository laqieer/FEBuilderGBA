throw 'PinnedProof.UnsupportedDirectRoute'
function ProofEnvelope {
    function Invoke-PreparationEntry {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory)][string]$Configuration,
            [Parameter(Mandatory)][string]$ConfigurationSha256,
            [Parameter(Mandatory)][ValidateSet('Build','Validate','Inputs')][string]$Stage,
            [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{32}$')][string]$Id,
            [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$SourceManifestSha256,
            [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$HelperSourceManifestSha256,
            [Parameter(Mandatory)][string]$SourceGateReference,
            [Parameter(Mandatory)][string]$AuthorizationReference,
            [ValidatePattern('^[0-9a-f]{64}$')][string]$BuildReceiptSha256,
            [ValidatePattern('^[0-9a-f]{64}$')][string]$ValidationReceiptSha256
        )
        . (Join-Path $PSScriptRoot 'Configuration.ps1')
        $proofConfiguration=Read-ProofConfiguration $Configuration $ConfigurationSha256 -Purpose Build
        $Code=$PSScriptRoot
        $null=Assert-ProofSource $proofConfiguration $Code
        Assert-Proof ($SourceManifestSha256 -ceq $proofConfiguration.sourceManifest.sha256) 'Source configuration binding.'
        Assert-Proof ($HelperSourceManifestSha256 -ceq $SourceManifestSha256 -and $SourceGateReference -ceq $proofConfiguration.approval.sourceGateReference -and $AuthorizationReference -ceq $proofConfiguration.approval.authorizationReference) 'Preparation approval/source binding.'
        $ErrorActionPreference = 'Stop'
        Set-StrictMode -Version Latest
        $B=$proofConfiguration.preparation.root
        $D=$proofConfiguration.outputRoot
        if ($PSVersionTable.PSEdition -cne 'Core') { throw 'Exact B root and PS7 required.' }
        if ($Id -cnotmatch '^[0-9a-f]{32}$' -or $Stage -cnotin @('Build','Validate','Inputs')) { throw 'Exact stage spelling and 32 lower-hex ID required.' }
        foreach ($url in @($SourceGateReference,$AuthorizationReference)) {
            if ($url -cnotmatch '^https://github\.com/laqieer/FEBuilderGBA/issues/[0-9]+#issuecomment-[0-9]+$') { throw 'Fork issue-2158 gate reference required.' }
        }
        if ($SourceGateReference -ceq $AuthorizationReference) { throw 'Source review is not stage authorization.' }
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $limit = @{Build=2100;Validate=300;Inputs=300}[$Stage]
        $utf8 = [Text.UTF8Encoding]::new($false)
        $processes = [Collections.Generic.List[object]]::new()
        $processState = @{sequence=0}

        function Budget { if ($clock.Elapsed.TotalSeconds -ge $limit) { throw 'Overall stage deadline; no retry.' } }
        function Plain([string]$path) {
            if ([IO.Path]::GetFullPath($path) -cne $path -or $path.StartsWith('\\')) { throw "Noncanonical path: $path" }
            for ($p=$path; $p; $p=[IO.Path]::GetDirectoryName($p)) {
                if (([IO.File]::GetAttributes($p) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse ancestry: $p" }
            }
        }
        function Child([string]$root,[string]$relative) {
            if (!$relative -or $relative -match '[:/]' -or @($relative.Split('\') | Where-Object { !$_ -or $_ -in @('.','..') -or $_.EndsWith('.') -or $_.EndsWith(' ') }).Count) { throw 'Unsafe relative path.' }
            $p = [IO.Path]::GetFullPath((Join-Path $root $relative))
            if (!$p.StartsWith($root+'\',[StringComparison]::Ordinal)) { throw 'Root escape.' }
            return $p
        }
        function Hash([string]$path) {
            Budget; Plain $path
            $f=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
            try { if ($f.Length -gt 268435456) { throw "File bound: $path" }; return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($f)).ToLowerInvariant() }
            finally { $f.Dispose() }
        }
        function Row([string]$path,[string]$name) {
            $h=Hash $path
            return [pscustomobject]@{path=$name;bytes=([IO.FileInfo]$path).Length;sha256=$h}
        }
        function Verify([string]$path,$row) {
            if ($row.sha256 -cnotmatch '^[0-9a-f]{64}$' -or (Hash $path) -cne $row.sha256 -or ([IO.FileInfo]$path).Length -ne $row.bytes) { throw "Pin mismatch: $path" }
        }
        function ReadJson([string]$path) { Plain $path; return Read-ProofJsonFile $path }
        function WriteNew([string]$path,[byte[]]$bytes) {
            Plain ([IO.Path]::GetDirectoryName($path))
            $s=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try { $s.Write($bytes) } finally { $s.Dispose() }
        }
        function JsonNew([string]$path,$value) { WriteNew $path $utf8.GetBytes(($value | ConvertTo-Json -Depth 16)) }
        function Fresh([string]$path) {
            Plain ([IO.Path]::GetDirectoryName($path))
            if ([IO.Path]::Exists($path)) { throw "Consumed destination; no retry: $path" }
            [void][IO.Directory]::CreateDirectory($path); Plain $path
        }
        function MakeParent([string]$path) {
            $parent=[IO.Path]::GetDirectoryName($path)
            if (![IO.Directory]::Exists($parent)) { MakeParent $parent; [void][IO.Directory]::CreateDirectory($parent) }
            Plain $parent
        }
        function CopyPinned([string]$source,[string]$destination,$row) {
            Verify $source $row; MakeParent $destination
            [IO.File]::Copy($source,$destination,$false)
            Verify $source $row; Verify $destination $row
        }
        function Excluded([string]$path) {
            return $path -match '(^|\\)\.patch2-import(\\|$)' -or $path -match '^config\\patch2(\\|$)'
        }
        function Tree([string]$root,[bool]$projection=$false) {
            Plain $root
            $rows=[Collections.Generic.List[object]]::new()
            $todo=[Collections.Generic.Stack[string]]::new(); $todo.Push($root)
            [long]$bytes=0; $entries=0
            while ($todo.Count) {
                Budget
                foreach ($p in [IO.Directory]::EnumerateFileSystemEntries($todo.Pop())) {
                    if (++$entries -gt 200000) { throw 'Tree entry bound.' }
                    Plain $p
                    $rel=[IO.Path]::GetRelativePath($root,$p)
                    if ($projection -and ((Excluded $rel) -or ($root -ceq "$W\config" -and $rel -match '^patch2(\\|$)'))) { continue }
                    if ([IO.Directory]::Exists($p)) { $todo.Push($p); continue }
                    $r=Row $p $rel; $bytes+=$r.bytes; $rows.Add($r)
                    $maxFiles=if ($root -ceq "$B\publish" -and !$projection) { 100000 } else { 10000 }
                    $maxBytes=if ($maxFiles -eq 100000) { 8589934592 } else { 2147483648 }
                    if ($rows.Count -gt $maxFiles -or $bytes -gt $maxBytes) { throw 'Tree size bound.' }
                }
            }
            return @($rows | Sort-Object path -CaseSensitive)
        }
        function SameRows($expected,$actual) {
            if (($expected | ConvertTo-Json -Depth 5 -Compress) -cne ($actual | ConvertTo-Json -Depth 5 -Compress)) { throw 'File manifest changed.' }
        }
        function Freeze {
            $null=Assert-ProofSource $proofConfiguration $Code
            foreach ($r in @($tools.tools)+@($tools.runtimeAssemblies)+@($tools.compileReferences)) { Verify $r.path $r }
            foreach ($r in $tools.assets) { Verify (Child $W $r.path) $r }
            foreach ($p in $plan.source_sha256.PSObject.Properties) {
                if ((Hash (Child $W $p.Name)) -cne $p.Value.ToLowerInvariant()) { throw "Source changed: $($p.Name)" }
            }
            $deltas=@($plan.reviewed_localization_delta_sha256.PSObject.Properties)
            if ($plan.accepted_application_source -cne $plan.reviewed_application_base -and !$deltas.Count) { throw 'Corrected application source requires parent-reviewed localization delta pins.' }
            foreach ($p in $deltas) {
                if ($p.Value -cnotmatch '^[0-9a-f]{64}$' -or (Hash (Child $W $p.Name)) -cne $p.Value) { throw "Reviewed localization delta changed: $($p.Name)" }
            }
        }
        function Run([string]$exe,[string[]]$arguments,[int]$seconds,[string]$cwd,[bool]$maySpawn=$false) {
            Budget; Plain $exe; Plain $cwd
            $processState.sequence++
            $prefix=Join-Path $control ('process-'+$processState.sequence)
            $record=[ordered]@{exe=$exe;arguments=$arguments;cwd=$cwd;startedUtc=[DateTime]::UtcNow.ToString('o');seconds=$seconds;pid=$null;startTicks=$null;exitCode=$null;passed=$false;timedOut=$false;cleanup='not-needed';descendantsTracked=$false;descendantCleanup=$(if($maySpawn){'not-established; no enumeration or tree kill'}else{'not-claimed'});stdout=$prefix+'.out';stderr=$prefix+'.err'}
            $processes.Add($record)
            $info=[Diagnostics.ProcessStartInfo]::new($exe); $info.UseShellExecute=$false; $info.CreateNoWindow=$true
            $info.WorkingDirectory=$cwd; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
            foreach ($a in $arguments) { $info.ArgumentList.Add($a) }
            $info.Environment.Clear()
            $environment=@{
                SystemRoot=$tools.systemRoot; WINDIR=$tools.systemRoot; ComSpec="$($tools.systemRoot)\System32\cmd.exe"
                ProgramFiles=$proofConfiguration.machine.programFiles; 'ProgramFiles(x86)'=$proofConfiguration.machine.programFilesX86; ProgramW6432=$proofConfiguration.machine.programFiles
                PATH="$($tools.systemRoot)\System32;$($tools.systemRoot);$($tools.dotnetRoot);$($tools.pshome);$([IO.Path]::GetDirectoryName($proofConfiguration.machine.gitPath))"
                PSModulePath="$($tools.pshome)\Modules"; HOME="$control\home"; USERPROFILE="$control\home"
                APPDATA="$control\home\AppData\Roaming"; LOCALAPPDATA="$control\home\AppData\Local"
                TEMP="$control\temp"; TMP="$control\temp"; DOTNET_CLI_HOME="$control\home"
                DOTNET_ROOT=$tools.dotnetRoot; DOTNET_ROOT_X64=$tools.dotnetRoot; DOTNET_MULTILEVEL_LOOKUP='0'
                DOTNET_CLI_TELEMETRY_OPTOUT='1'; DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'; DOTNET_NOLOGO='1'
                AVALONIA_TELEMETRY_OPTOUT='1'
                POWERSHELL_TELEMETRY_OPTOUT='1'
                DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='1'; DOTNET_ROLL_FORWARD='LatestPatch'
                NUGET_PACKAGES=$plan.package_folders[0]; NUGET_FALLBACK_PACKAGES=$plan.package_folders[1]
                NUGET_HTTP_CACHE_PATH="$control\home\nuget-http"; MSBuildEnableWorkloadResolver='false'
                MSBuildSDKsPath="$($tools.dotnetRoot)\sdk\$($tools.sdk)\Sdks"
                MSBUILDDISABLENODEREUSE='1'; DOTNET_CLI_USE_MSBUILD_SERVER='0'; DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1'
                GIT_CONFIG_NOSYSTEM='1'; GIT_CONFIG_GLOBAL="$control\empty-git-config"; GIT_TERMINAL_PROMPT='0'
                GCM_INTERACTIVE='Never'; GIT_OPTIONAL_LOCKS='0'; GIT_PAGER='cat'
            }
            foreach ($k in $environment.Keys) { $info.Environment[$k]=[string]$environment[$k] }
            $p=[Diagnostics.Process]::new(); $p.StartInfo=$info
            $out=$null; $err=$null; $retainedHandle=$null; $cts=[Threading.CancellationTokenSource]::new()
            try {
                $out=[IO.File]::Open($record.stdout,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
                $err=[IO.File]::Open($record.stderr,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
                $deadline=[Math]::Min($limit,$clock.Elapsed.TotalSeconds+$seconds)
                if (!$p.Start()) { throw 'Process start failed.' }
                $retainedHandle=$p.SafeHandle; $record.pid=$p.Id
                $record.startTicks=$p.StartTime.ToUniversalTime().Ticks
                $copyOut=$p.StandardOutput.BaseStream.CopyToAsync($out,81920,$cts.Token)
                $copyErr=$p.StandardError.BaseStream.CopyToAsync($err,81920,$cts.Token)
                $record.imageObservation=@{}
                $null=Get-ProcessImageObservation `
                    { $p.HasExited } { [BoundedProcessImage]::Read($retainedHandle) } `
                    { ($deadline-$clock.Elapsed.TotalSeconds)*1000 } `
                    $exe ([StringComparison]::OrdinalIgnoreCase) prepare-initial $record.imageObservation
                while ($true) {
                    Assert-ProofProcessDeadline $record $clock.Elapsed.TotalSeconds $deadline
                    $exited=$p.WaitForExit([int][Math]::Min(100,[Math]::Max(0,[Math]::Floor(($deadline-$clock.Elapsed.TotalSeconds)*1000))))
                    Assert-ProofProcessDeadline $record $clock.Elapsed.TotalSeconds $deadline
                    if($exited){break}
                    if ($out.Length -gt 16777216 -or $err.Length -gt 16777216) { throw 'Process log bound.' }
                }
                $record.exitCode=$p.ExitCode
                foreach ($task in @($copyOut,$copyErr)) {
                    Assert-ProofProcessDeadline $record $clock.Elapsed.TotalSeconds $deadline
                    $drained=$task.Wait([int][Math]::Min(1000,[Math]::Max(0,[Math]::Floor(($deadline-$clock.Elapsed.TotalSeconds)*1000))))
                    Assert-ProofProcessDeadline $record $clock.Elapsed.TotalSeconds $deadline
                    if (!$drained) { throw 'Output pipe still held; descendant cleanup unproven.' }
                }
                if ($out.Length -gt 16777216 -or $err.Length -gt 16777216) { throw 'Process log bound.' }
                if ($record.exitCode -ne 0) { throw "Process exit $($record.exitCode); see owned logs." }
                $record.stdoutBytes=$out.Length; $record.stderrBytes=$err.Length
                $out.Dispose(); $err.Dispose()
                $record.stdoutSha256=Hash $record.stdout; $record.stderrSha256=Hash $record.stderr
                Assert-ProofProcessDeadline $record $clock.Elapsed.TotalSeconds $deadline
                Budget; $record.passed=$true
            } finally {
                if ($record.pid -and !$p.HasExited) {
                    $record.cleanup='refused-or-incomplete'
                    try {
                        if ($record.startTicks -and $p.Id -eq $record.pid -and $p.StartTime.ToUniversalTime().Ticks -eq $record.startTicks) {
                            $cleanupClock=[Diagnostics.Stopwatch]::StartNew()
                            $record.cleanupImageObservation=@{}
                            $cleanupImage=Get-ProcessImageObservation { $p.HasExited } `
                                { [BoundedProcessImage]::Read($retainedHandle) } { 5000-$cleanupClock.ElapsedMilliseconds } `
                                $exe ([StringComparison]::OrdinalIgnoreCase) prepare-cleanup $record.cleanupImageObservation
                            if($cleanupImage.state -cne 'image-observed') { throw 'Cleanup image unobserved.' }
                            $p.Kill()
                            $record.cleanup=$(if($p.WaitForExit([int][Math]::Max(0,5000-$cleanupClock.ElapsedMilliseconds))){'retained-process-killed; NOT descendant cleanup'}else{'retained-process-kill-incomplete'})
                        }
                    } catch { $record.cleanup='retained-process-cleanup-failed' }
                }
                $cts.Cancel(); $cts.Dispose()
                if ($out) { $out.Dispose() }; if ($err) { $err.Dispose() }; $p.Dispose()
                $record.endedUtc=[DateTime]::UtcNow.ToString('o')
                JsonNew ($prefix+'.json') $record
            }
            return $record
        }
        function Git([string]$directory,[string[]]$arguments) {
            $r=Run $proofConfiguration.machine.gitPath (@('-c','core.fsmonitor=false','-c',"core.hooksPath=$control\empty-hooks",'-c','submodule.recurse=false','-C',$directory)+$arguments) 15 $control
            return [IO.File]::ReadAllText($r.stdout).TrimEnd([char[]]"`r`n")
        }
        function SourceState {
            Freeze
            $head=Git $W @('rev-parse','HEAD')
            $tree=Git $W @('rev-parse','HEAD^{tree}')
            $status=Git $W @('status','--porcelain=v1','--untracked-files=all','--ignore-submodules=none')
            $null=Git $W @('merge-base','--is-ancestor',$plan.reviewed_application_base,$plan.accepted_application_source)
            $null=Git $W @('merge-base','--is-ancestor',$plan.accepted_application_source,'HEAD')
            $diff=Git $W @('diff','--no-ext-diff','--no-textconv','--name-only',$plan.reviewed_application_base,'HEAD','--')
            $sinceAccepted=Git $W @('diff','--no-ext-diff','--no-textconv','--name-only',$plan.accepted_application_source,'HEAD','--')
            $docs=@($plan.reviewed_documentation_delta_paths)
            foreach ($path in $docs) { if (!$path.StartsWith('docs\',[StringComparison]::Ordinal)) { throw 'Documentation delta outside docs.' } }
            $deltaPaths=@($plan.reviewed_localization_delta_sha256.PSObject.Properties | ForEach-Object Name)
            $allowed=@(($docs+$deltaPaths) | ForEach-Object { $_.Replace('\','/') } | Sort-Object)
            $docsGit=@($docs | ForEach-Object { $_.Replace('\','/') })
            $afterAccepted=@($sinceAccepted -split "`n" | Where-Object { $_ })
            if (@($afterAccepted | Where-Object { $_ -cnotin $docsGit }).Count) { throw 'Non-documentation change after the accepted corrected source.' }
            if ($head -cne $plan.head -or $tree -cne $plan.head_tree -or $status -cne '' -or (($diff -split "`n" | Sort-Object) -join "`n") -cne ($allowed -join "`n")) { throw 'HEAD/clean/reviewed-delta gate failed.' }
            $patch=Git "$W\config\patch2" @('rev-parse','HEAD')
            if ($patch -cne $plan.patch2_submodule -or (Git "$W\config\patch2" @('status','--porcelain=v1','--untracked-files=all')) -cne '') { throw 'Patch-data provenance gate failed.' }
            $feInfoRoot=Child $W 'resources\fe-info'; Plain $feInfoRoot
            $feInfoLink=Git $W @('ls-tree','HEAD:resources','--','fe-info')
            if ($feInfoLink -cne "160000 commit $($plan.fe_info_submodule)`tfe-info") { throw 'FE-info gitlink does not match its approved pin.' }
            $feInfoTop=Git $feInfoRoot @('rev-parse','--show-toplevel')
            if ([IO.Path]::GetFullPath($feInfoTop) -cne $feInfoRoot) { throw 'FE-info must be an initialized repository at its exact root, not a parent fallback.' }
            $feInfo=Git $feInfoRoot @('rev-parse','HEAD')
            if ($feInfo -cne $plan.fe_info_submodule -or (Git $feInfoRoot @('status','--porcelain=v1','--untracked-files=all','--ignore-submodules=none')) -cne '') { throw 'FE-info must be initialized at its pinned commit and clean; no provisioning or fallback authorized here.' }
            return [pscustomobject]@{head=$head;tree=$tree;clean=$true;acceptedApplicationSource=$plan.accepted_application_source;reviewedApplicationBase=$plan.reviewed_application_base;reviewedLocalizationDeltaSha256=$plan.reviewed_localization_delta_sha256;reviewedDocumentationPaths=$docsGit;docsSinceAccepted=$afterAccepted;patch2=$patch;feInfo=[pscustomobject]@{root=$feInfoRoot;gitlink=$feInfoLink;head=$feInfo;initialized=$true;clean=$true}}
        }
        function Resources {
            foreach ($p in @("$W\config\data","$W\resources\fe-info\json")) {
                if (![IO.Directory]::Exists($p)) { throw "Declared preparation resource projection absent: $p. No initialization/download/fallback authorized." }
                Plain $p
            }
            $config=@(Tree "$W\config" $true | Where-Object { $_.path -cne 'config.xml' -and $_.path -notmatch '^patch2(\\|$)' })
            $codes=@(Tree "$W\resources\fe-info\json" | Where-Object { $_.path -match '^[^\\]+\\code\.json$' })
            if (!$config.Count) { throw 'Declared config projection absent; no old-binary fallback.' }
            if (!@($codes | Where-Object { $_.path -ceq 'fe8\code.json' }).Count) { throw 'Declared FE8 disassembly map absent from this preparation projection: json\fe8\code.json. This optional app data is not an import prerequisite.' }
            return [pscustomobject]@{config=$config;codes=$codes}
        }
        function PassingReceipt([string]$path,[string]$sha,[string]$expectedStage) {
            if (!$sha -or (Hash $path) -cne $sha) { throw 'Passing receipt hash required.' }
            $r=ReadJson $path
            if ($r.stage -cne $expectedStage -or $r.passed -isnot [bool] -or !$r.passed -or $r.runId -cne $Id -or $r.sourceManifestSha256 -cne $SourceManifestSha256 -or $r.helperSourceManifestSha256 -cne $HelperSourceManifestSha256) { throw 'Receipt identity/pass mismatch.' }
            return $r
        }
        function Zip([string]$path,[object[]]$entries) {
            $stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
            try {
                $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
                try {
                    foreach ($e in $entries) {
                        $entry=$zip.CreateEntry($e.name,[IO.Compression.CompressionLevel]::NoCompression)
                        $entry.LastWriteTime=[DateTimeOffset]::new(2026,1,1,0,0,0,[TimeSpan]::Zero)
                        $s=$entry.Open(); try { $s.Write([byte[]]$e.bytes) } finally { $s.Dispose() }
                    }
                } finally { $zip.Dispose() }
            } finally { $stream.Dispose() }
            if (([IO.FileInfo]$path).Length -gt 65536) { throw 'ZIP bound.' }
            # Create only these fixed entries. In particular, never extract/open the malformed ZIP.
        }

        $prepared=Read-ProofPreparation $proofConfiguration; $plan=$prepared.inputs; $tools=$prepared.metadata; $W=$plan.worktree
        Freeze
        . "$Code\ProcessImage.ps1"
        if ($PSHOME -cne $tools.pshome) { throw 'Pinned PSHOME required.' }
        $hostRow=@($tools.tools | Where-Object { $_.path -ceq "$PSHOME\pwsh.exe" })[0]
        Verify "$PSHOME\pwsh.exe" $hostRow
        if ($Stage -ceq 'Inputs') {
            $null=PassingReceipt "$B\build-attempt\result.json" $BuildReceiptSha256 'Build'
            $null=PassingReceipt "$B\validate-$Id\result.json" $ValidationReceiptSha256 'Validate'
        }
        foreach ($p in $plan.package_folders) { Plain $p.TrimEnd('\') }
        foreach ($p in @("$($tools.systemRoot)\System32","$PSHOME\Modules")) { Plain $p }
        $root=switch($Stage) { Build { "$B\build-attempt" }; Validate { "$B\validate-$Id" }; Inputs { "$D\inputs-$Id" } }
        Fresh $root
        $control=if($Stage -ceq 'Inputs') { "$root\preparation" } else { $root }
        if ($control -cne $root) { Fresh $control }
        foreach ($p in @('home','home\AppData','home\AppData\Local','home\AppData\Roaming','temp','empty-hooks')) { Fresh (Join-Path $control $p) }
        WriteNew "$control\empty-git-config" ([byte[]]@())
        JsonNew "$control\global.json" @{sdk=@{version=$tools.sdk;rollForward='disable';allowPrerelease=$false}}
        $report=[ordered]@{schema='windows-desktop-preparation-receipt-v1';stage=$Stage;runId=$Id;passed=$false;sourceManifestSha256=$SourceManifestSha256;helperSourceManifestSha256=$HelperSourceManifestSha256;sourceGateReference=$SourceGateReference;authorizationReference=$AuthorizationReference;seconds=$limit;startedUtc=[DateTime]::UtcNow.ToString('o');before=$null;after=$null;processes=$processes;failure=$null}
        $report.toolMetadataSha256=$proofConfiguration.preparation.metadata.sha256
        JsonNew "$control\attempt.json" $report
        try {
            Add-Type -Path "$Code\Readiness.cs" -ReferencedAssemblies @($tools.compileReferences | ForEach-Object path)
            Budget
            if($Stage -ceq 'Validate') {
                $report.processImageCases=& "$Code\ProcessImage.Tests.ps1"
                if($report.processImageCases -ne 10) { throw 'Ten pure process-image cases required.' }
            }
            $report.before=SourceState
            if ($Stage -ceq 'Build') {
                $resources=Resources
                JsonNew "$control\resource-sources.json" $resources
                Fresh "$B\publish"; Fresh "$B\generator"
                $testEvidence=[Collections.Generic.List[object]]::new()
                $common=@('--no-restore','--disable-build-servers','-p:E2E_HOOKS=false','-p:UseSharedCompilation=false','-p:MSBuildEnableWorkloadResolver=false','-p:BuildProjectReferences=true','-p:ImportDirectoryBuildProps=false','-p:ImportDirectoryBuildTargets=false','-p:ImportDirectoryPackagesProps=false','-m:1','-nr:false','-p:UsedAvaloniaProducts=')
                # Rebuild separately; dotnet test --no-build must not silently validate an old assembly.
                foreach ($configuration in @('Debug','Release')) {
                    $project="$W\FEBuilderGBA.Avalonia.Tests\FEBuilderGBA.Avalonia.Tests.csproj"
                    $null=Run $plan.dotnet (@('build',$project,'-c',$configuration,'-t:Rebuild')+$common) 360 $control $true
                    $testRoot="$W\FEBuilderGBA.Avalonia.Tests\bin\$configuration\net10.0"
                    $testFiles=@(foreach ($name in @('FEBuilderGBA.Avalonia.Tests.dll','FEBuilderGBA.Avalonia.Tests.deps.json','FEBuilderGBA.Avalonia.Tests.runtimeconfig.json','FEBuilderGBA.Avalonia.dll','FEBuilderGBA.Core.dll','FEBuilderGBA.SkiaSharp.dll','testhost.dll')) { Row (Child $testRoot $name) $name })
                    $trx="fixture-$($configuration.ToLowerInvariant()).trx"
                    $null=Run $plan.dotnet (@('test',$project,'-c',$configuration,'--no-build','--filter','FullyQualifiedName~FEBuilderGBA.Avalonia.Tests.SyntheticPatchImportFixtureTests','--logger',"trx;LogFileName=$trx",'--results-directory',"$control\tests")+$common) 180 $control $true
                    $settings=[Xml.XmlReaderSettings]::new(); $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit; $settings.XmlResolver=$null
                    $reader=[Xml.XmlReader]::Create("$control\tests\$trx",$settings)
                    try { $xml=[Xml.XmlDocument]::new(); $xml.XmlResolver=$null; $xml.Load($reader) } finally { $reader.Dispose() }
                    $results=@($xml.SelectNodes("//*[local-name()='UnitTestResult']"))
                    $c=$xml.SelectSingleNode("//*[local-name()='Counters']")
                    if (!$c -or $c.total -ne '4' -or $c.executed -ne '4' -or $c.passed -ne '4' -or $c.failed -ne '0' -or $results.Count -ne 4 -or @($results | Where-Object { $_.outcome -cne 'Passed' -or $_.testName -notmatch 'SyntheticPatchImportFixtureTests\.' }).Count) { throw 'Exactly four passing fixture cases required in each configuration.' }
                    foreach ($r in $testFiles) { Verify (Child $testRoot $r.path) $r }
                    $testEvidence.Add(@{configuration=$configuration;passedCases=4;binaryRoot=$testRoot;files=$testFiles;trx=(Row "$control\tests\$trx" "tests\$trx")})
                }
                $null=Run $plan.dotnet (@('publish',"$W\FEBuilderGBA.Avalonia\FEBuilderGBA.Avalonia.csproj",'-c','Release','-t:Rebuild,Publish','-o',"$B\publish")+$common) 360 $control $true
                $null=Run $plan.dotnet (@('publish',"$W\scripts\SyntheticProofFixtures\SyntheticProofFixtures.csproj",'-c','Debug','-t:Rebuild,Publish','-o',"$B\generator")+$common) 180 $control $true
                $outputs=[ordered]@{applicationSource=$plan.accepted_application_source;worktreeHead=$plan.head;appFiles=@(Tree "$B\publish");generatorFiles=@(Tree "$B\generator");projectionFiles=@(Tree "$B\publish" $true);resources=$resources;tests=$testEvidence}
                foreach ($p in @('FEBuilderGBA.Avalonia.exe','FEBuilderGBA.Avalonia.dll','FEBuilderGBA.Avalonia.runtimeconfig.json','FEBuilderGBA.Avalonia.deps.json')) { Plain (Child "$B\publish" $p) }
                foreach ($p in @('SyntheticProofFixtures.dll','SyntheticProofFixtures.runtimeconfig.json','SyntheticProofFixtures.deps.json')) { Plain (Child "$B\generator" $p) }
                $runtime=ReadJson "$B\publish\FEBuilderGBA.Avalonia.runtimeconfig.json"
                if ($runtime.runtimeOptions.tfm -cne 'net10.0' -or $runtime.runtimeOptions.framework.name -cne 'Microsoft.NETCore.App' -or $runtime.runtimeOptions.framework.version -notmatch '^10\.0\.') { throw 'App/runtime major-minor mismatch.' }
                $afterResources=Resources
                SameRows $resources.config $afterResources.config; SameRows $resources.codes $afterResources.codes
                foreach ($r in $resources.config) { Verify (Child "$B\publish\config" $r.path) $r }
                foreach ($r in $resources.codes) { Verify (Child "$B\publish\resources\fe-info\json" $r.path) $r }
                JsonNew "$control\output-manifest.json" $outputs
                $report.outputManifestSha256=Hash "$control\output-manifest.json"
            } elseif ($Stage -ceq 'Validate') {
                foreach ($mode in @('Pure','Compile')) {
                    $childArguments=New-PinnedProofChildArguments -ChildMode ('Validate'+$mode+'Child') -Path "$control\$mode.bindings.json" -Values @{
                        configuration=$Configuration;configurationSha256=$ConfigurationSha256;id=$Id;
                        sourceManifestSha256=$SourceManifestSha256;helperSourceManifestSha256=$HelperSourceManifestSha256}
                    $null=Run "$PSHOME\pwsh.exe" $childArguments 90 $control
                    $r=ReadJson "$control\$mode.json"
                    if ($r.passed -isnot [bool] -or !$r.passed -or $r.nativeCalls -isnot [bool] -or $r.nativeCalls -or
                        $r.appLaunched -isnot [bool] -or $r.appLaunched) { throw 'Non-GUI validation failed.' }
                }
                $report.validationFiles=@(
                    foreach ($name in @('Pure.json','Compile.json','Policy.compile-only.dll','Desktop.compile-only.dll')) { Row "$control\$name" $name }
                    foreach ($r in $tools.runtimeAssemblies) {
                        $name='support\'+[IO.Path]::GetFileName($r.path)
                        Verify (Child $control $name) $r
                        Row (Child $control $name) $name
                    }
                )
            } else {
                $build=PassingReceipt "$B\build-attempt\result.json" $BuildReceiptSha256 'Build'
                $validation=PassingReceipt "$B\validate-$Id\result.json" $ValidationReceiptSha256 'Validate'
                foreach ($r in $validation.validationFiles) { Verify (Child "$B\validate-$Id" $r.path) $r }
                if ((Hash "$B\build-attempt\output-manifest.json") -cne $build.outputManifestSha256) { throw 'Build output manifest changed.' }
                $outputs=ReadJson "$B\build-attempt\output-manifest.json"
                SameRows $outputs.appFiles @(Tree "$B\publish")
                SameRows $outputs.generatorFiles @(Tree "$B\generator")
                SameRows $outputs.projectionFiles @(Tree "$B\publish" $true)
                Fresh "$root\app"; Fresh "$root\support"; Fresh "$root\desktop-proof-$Id"
                $provenance=[Collections.Generic.List[object]]::new()
                foreach ($r in $outputs.projectionFiles) {
                    if (Excluded $r.path) { throw 'Disallowed projection path.' }
                    if ($r.path -ceq 'config\config.xml') { continue }
                    CopyPinned (Child "$B\publish" $r.path) (Child "$root\app" $r.path) $r
                    $provenance.Add([pscustomobject]@{path=$r.path;source=(Child "$B\publish" $r.path);bytes=$r.bytes;sha256=$r.sha256})
                }
                $config='<root><item><key>Language</key><value>en</value></item><item><key>func_auto_update</key><value>0</value></item></root>'
                MakeParent "$root\app\config\config.xml"; WriteNew "$root\app\config\config.xml" $utf8.GetBytes($config)
                $provenance.Add([pscustomobject]@{path='config\config.xml';source='reviewed prepare.ps1 literal; fresh owned projection only';bytes=$utf8.GetByteCount($config);sha256=(Hash "$root\app\config\config.xml")})
                foreach ($r in $outputs.resources.config) { Verify (Child "$root\app\config" $r.path) $r }
                foreach ($r in $outputs.resources.codes) { Verify (Child "$root\app\resources\fe-info\json" $r.path) $r }
                $runtimeRows=@(foreach ($r in $tools.runtimeAssemblies) {
                    $name=[IO.Path]::GetFileName($r.path)
                    CopyPinned $r.path (Child "$root\support" $name) $r
                    Row (Child "$root\support" $name) $name
                })
                $fixture="$root\desktop-proof-$Id"
                $g=Run $plan.dotnet @('exec','--fx-version',$tools.runtime,'--roll-forward','Disable',"$B\generator\SyntheticProofFixtures.dll","$fixture\zipdb-proof.gba") 60 $control
                $generated=ReadJson $g.stdout
                if ($generated.Format -cne 'fe8u-synthetic-huffman-v1' -or $generated.Length -ne 16777216 -or $generated.Sha256 -cne (Hash "$fixture\zipdb-proof.gba")) { throw 'Functional synthetic generator receipt mismatch.' }
                $descriptor=$utf8.GetBytes("NAME=Offline ZIP Proof`nTYPE=BIN`nDESCRIPTION=Generated proof fixture.`nPATCHED_IF:0x200=0xAB 0xCD`nBIN:0x300=payload.bin`n")
                $payload=[byte[]]@(0xAA,0x55)
                Zip "$fixture\zipdb-valid.zip" @(@{name='FE8U/proof/PATCH_offline.txt';bytes=$descriptor},@{name='FE8U/proof/payload.bin';bytes=$payload})
                Zip "$fixture\zipdb-invalid.zip" @(@{name='FE8U/../../zipdb-import-escape.txt';bytes=[byte[]]@(1)})
                $fixtureRows=@(Tree $fixture)
                if ($fixtureRows.Count -ne 3 -or ([IO.FileInfo]"$fixture\zipdb-proof.gba").Length -ne 16777216) { throw 'Fixture closure/length mismatch.' }
                $manifest=[ordered]@{
                    schema='windows-desktop-bounded-input-v1'; applicationSource=$plan.accepted_application_source; runId=$Id
                    syntheticRomFormat='fe8u-synthetic-huffman-v1'; powershellHost=@{path=$hostRow.path;sha256=$hostRow.sha256}
                    appFiles=@(Tree "$root\app"); fixtures=$fixtureRows; runtimeAssemblies=$runtimeRows; compileReferences=$tools.compileReferences
                    installedFiles=@(
                        @{path='proof\PATCH_offline.txt';bytes=$descriptor.Length;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($descriptor)).ToLowerInvariant()}
                        @{path='proof\payload.bin';bytes=2;sha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($payload)).ToLowerInvariant()}
                    )
                }
                JsonNew "$control\projection-provenance.json" @{buildReceiptSha256=$BuildReceiptSha256;validationReceiptSha256=$ValidationReceiptSha256;sourceManifestSha256=$SourceManifestSha256;helperSourceManifestSha256=$HelperSourceManifestSha256;worktreeHead=$plan.head;applicationSource=$plan.accepted_application_source;files=$provenance}
                SameRows $outputs.appFiles @(Tree "$B\publish"); SameRows $outputs.generatorFiles @(Tree "$B\generator")
                JsonNew "$root\input-manifest.json" $manifest
                $report.inputManifestSha256=Hash "$root\input-manifest.json"
                $report.projectionProvenanceSha256=Hash "$control\projection-provenance.json"
            }
            $report.after=SourceState
            Budget; $report.passed=$true
        } catch {
            $report.failure=$_.Exception.Message
            # A failed/expired attempt never starts more tools for a nominal postcheck.
            $report.afterFailure='No passing after-state claimed; no retry or further process launch.'
        } finally {
            $report.endedUtc=[DateTime]::UtcNow.ToString('o'); $report.elapsedSeconds=$clock.Elapsed.TotalSeconds
            JsonNew "$control\result.json" $report
        }
        if (!$report.passed) { throw "Stage failed: $($report.failure). Inspect $control\result.json; do not retry." }
        Write-Output "$control\result.json"
    }
}
