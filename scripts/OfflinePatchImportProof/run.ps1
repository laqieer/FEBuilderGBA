throw 'PinnedProof.UnsupportedDirectRoute'
function ProofEnvelope {
    function Invoke-RunEntry {
        # SOURCE ONLY until a new complete source/security screen and exact runtime authorization.
        [CmdletBinding()]
        param(
            [Parameter(Mandatory)][string]$Configuration,
            [Parameter(Mandatory)][string]$ConfigurationSha256,
            [Parameter(Mandatory)][string]$InputManifest,
            [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$InputManifestSha256,
            [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{64}$')][string]$SourceManifestSha256,
            [Parameter(Mandatory)][ValidatePattern('^https://github\.com/laqieer/FEBuilderGBA/issues/[0-9]+#issuecomment-[0-9]+$')]
            [string]$AuthorizationReference
        )
        . (Join-Path $PSScriptRoot 'Configuration.ps1')
        $proofConfiguration=Read-ProofConfiguration $Configuration $ConfigurationSha256 -Purpose Gui
        $Code=$PSScriptRoot
        $null=Assert-ProofSource $proofConfiguration $Code
        . (Join-Path $Code 'ProcessImage.ps1')
        Assert-Proof ($SourceManifestSha256 -ceq $proofConfiguration.sourceManifest.sha256) 'Source configuration binding.'
        Assert-Proof ($AuthorizationReference -ceq $proofConfiguration.approval.authorizationReference) 'Caller GUI approval binding.'
        $ErrorActionPreference = 'Stop'
        Set-StrictMode -Version Latest
        $PSModuleAutoLoadingPreference = 'None'
        foreach ($name in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management','Microsoft.PowerShell.Security')) {
            Import-Module ([IO.Path]::Combine($PSHOME, 'Modules', $name, $name + '.psd1')) -ErrorAction Stop
        }
        $root=$proofConfiguration.outputRoot
        $source=$proofConfiguration.applicationSource
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $process = $null
        $currentHost = $null
        $startTicks = 0L
        $ownedOutput = $false
        $report = [ordered]@{
            schema = 'windows-desktop-bounded-result-v1'; passed = $false
            application_source = $source; source_manifest_sha256 = $SourceManifestSha256
            input_manifest_sha256 = $InputManifestSha256; supplied_authorization_reference = $AuthorizationReference
            started_utc = [DateTime]::UtcNow.ToString('o')
            limits_ms = @{ overall = 420000; preparation = 150000; gui = 240000 }
            app_cleanup_owner = 'launch.ps1-only-after-confirmed-runner-exit'
            readiness_is_not_gui_authorization = $true; image_review_required = $true
            stdout_stderr = 'drained without retaining contents'
            memory_ROM_undo_invariance_claimed = $false
            bindingPolicy = 'pinned-support-or-pshome'; runtimeBindings = @()
            empty_patch_library_initialized = $false
        }
        function Budget {
            if ($clock.ElapsedMilliseconds -ge 420000) { throw 'Overall deadline.' }
        }
        function Plain([string]$path) {
            if (![IO.Path]::IsPathFullyQualified($path) -or $path.StartsWith('\\') -or $path.Contains('/')) {
                throw 'Only canonical local Windows paths are accepted.'
            }
            $full = [IO.Path]::GetFullPath($path)
            if ($full -cne $path) { throw 'Noncanonical path.' }
            for ($p = $full; $p; $p = [IO.Path]::GetDirectoryName($p)) {
                if (([IO.File]::GetAttributes($p) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw 'Reparse ancestry refused.'
                }
            }
        }
        function Hash([string]$path) {
            Budget
            Plain $path
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        function NewDirectory([string]$path) {
            Budget
            if (![IO.Directory]::Exists($path)) {
                $parent = [IO.Path]::GetDirectoryName($path)
                if (![IO.Directory]::Exists($parent)) { NewDirectory $parent }
                Plain $parent
                [IO.Directory]::CreateDirectory($path) | Out-Null
            }
            Plain $path
        }
        function NewText([string]$path, [string]$text) {
            Budget
            Plain ([IO.Path]::GetDirectoryName($path))
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($text)
            $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        }
        function VerifyFile([string]$path, $row) {
            Budget
            if ($row.sha256 -cnotmatch '^[0-9a-f]{64}$' -or $row.bytes -lt 0 -or $row.bytes -gt 268435456) {
                throw 'Invalid file identity.'
            }
            Plain $path
            if ([IO.FileInfo]::new($path).Length -ne $row.bytes -or (Hash $path) -cne $row.sha256) {
                throw 'Pinned file mismatch.'
            }
        }
        function CopyFile([string]$from, [string]$to, $row) {
            VerifyFile $from $row
            NewDirectory ([IO.Path]::GetDirectoryName($to))
            [IO.File]::Copy($from, $to, $false)
            VerifyFile $to $row
        }
        try {
            if ($PSVersionTable.PSEdition -ne 'Core' -or !$IsWindows -or
                [Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
                throw 'Use the exact frozen root and PowerShell 7 -NoProfile -NonInteractive -STA.'
            }
            Plain $root
            . (Join-Path $Code 'RuntimeBinding.ps1')
            if ([IO.FileInfo]::new($InputManifest).Length -gt 4194304 -or (Hash $InputManifest) -cne $InputManifestSha256) {
                throw 'Input manifest identity or size mismatch.'
            }
            $manifest = Read-ProofInput $InputManifest $InputManifestSha256 $proofConfiguration
            if ($manifest.schema -cne 'windows-desktop-bounded-input-v1' -or $manifest.applicationSource -cne $source -or
                $manifest.runId -cnotmatch '^[0-9a-f]{32}$' -or
                $manifest.syntheticRomFormat -cne 'fe8u-synthetic-huffman-v1') { throw 'Wrong input manifest.' }
            $pwsh = Join-Path $PSHOME 'pwsh.exe'
            if ($manifest.powershellHost.path -cne $pwsh -or
                $manifest.powershellHost.sha256 -cnotmatch '^[0-9a-f]{64}$' -or
                (Hash $pwsh) -cne $manifest.powershellHost.sha256) { throw 'PowerShell host pin mismatch.' }
            $inputRoot = Join-Path $root ('inputs-' + $manifest.runId)
            $runRoot = Join-Path $root ('run-' + $manifest.runId)
            if ($InputManifest -cne (Join-Path $inputRoot 'input-manifest.json')) { throw 'Input manifest path mismatch.' }
            Plain $inputRoot
            if ([IO.Directory]::Exists($runRoot) -or [IO.File]::Exists($runRoot)) { throw 'Run root already exists; never retry.' }
            NewDirectory $runRoot
            $ownedOutput = $true
            NewText (Join-Path $runRoot 'attempt.json') (@{
                run_id = $manifest.runId; input_sha256 = $InputManifestSha256
                source_sha256 = $SourceManifestSha256; started_utc = $report.started_utc
            } | ConvertTo-Json -Compress)
            $report.run_id = $manifest.runId
            $report.output_root = $runRoot
            # Explicit parent-pinned desktop runtime assemblies; no SDK/runtime directory discovery.
            $assemblyNames = @('System.Private.Windows.Core.dll','System.Private.Windows.GdiPlus.dll','WindowsBase.dll','UIAutomationTypes.dll','UIAutomationClient.dll','Microsoft.Win32.SystemEvents.dll','System.Drawing.Common.dll')
            if ($manifest.runtimeAssemblies.Count -ne $assemblyNames.Count) { throw 'Runtime assembly closure mismatch.' }
            $references = [Collections.Generic.List[string]]::new()
            foreach ($name in $assemblyNames) {
                $rows = @($manifest.runtimeAssemblies | Where-Object { $_.path -ceq $name })
                if ($rows.Count -ne 1) { throw 'Runtime assembly missing.' }
                $path = Join-Path $inputRoot ('support\' + $name)
                $hostPath = Join-Path $PSHOME $name
                VerifyFile $hostPath $rows[0]
                VerifyFile $path $rows[0]
                $identity = [Reflection.AssemblyName]::GetAssemblyName($path).FullName
                $assembly = [Reflection.Assembly]::LoadFrom($path)
                $binding = @{expectedPath=$path;expectedHostPath=$hostPath;expectedIdentity=$identity;expectedSha256=$rows[0].sha256;actualPath=$assembly.Location;actualIdentity=$assembly.FullName;actualSha256=$null;accepted=$false}
                $report.runtimeBindings += $binding
                $accepted = Confirm-PinnedRuntimeBinding -SupportPath $path -HostPath $hostPath `
                    -ExpectedIdentity $identity -ExpectedBytes $rows[0].bytes -ExpectedSha256 $rows[0].sha256 `
                    -ActualPath $assembly.Location -ActualIdentity $assembly.FullName `
                    -ReadMetadata { param($candidate) Plain $candidate; @{bytes=([IO.FileInfo]$candidate).Length;sha256=(Hash $candidate)} }
                $binding.actualSha256=$accepted.actualSha256
                $binding.actualBytes=$accepted.actualBytes
                $binding.allowedLocation=$accepted.allowedLocation
                VerifyFile $hostPath $rows[0]
                VerifyFile $path $rows[0]
                $binding.accepted=$true
                $references.Add($path)
            }
            if ($manifest.compileReferences.Count -lt 1 -or $manifest.compileReferences.Count -gt 256) { throw 'Compiler reference bound.' }
            $compilerSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($row in $manifest.compileReferences) {
                if ([IO.Path]::GetDirectoryName($row.path) -cne (Join-Path $PSHOME 'ref') -or
                    [IO.Path]::GetExtension($row.path) -cne '.dll' -or !$compilerSeen.Add($row.path) -or
                    $assemblyNames -contains [IO.Path]::GetFileName($row.path)) { throw 'Compiler reference path.' }
                VerifyFile $row.path $row
                $references.Add($row.path)
            }
            # One compilation unit: no on-disk helper DLL and no dynamic-assembly reference guessing.
            Add-Type -Path @((Join-Path $Code 'Readiness.cs'), (Join-Path $Code 'Policy.cs'),
                (Join-Path $Code 'Desktop.cs')) -ReferencedAssemblies $references.ToArray()
            $currentHost=[Diagnostics.Process]::GetCurrentProcess()
            $hostHandle=$currentHost.SafeHandle
            $report.hostImageObservation=@{}
            $image=Get-ProcessImageObservation {$currentHost.HasExited} { [BoundedProcessImage]::Read($hostHandle) } `
                {150000-$clock.ElapsedMilliseconds} $pwsh ([StringComparison]::Ordinal) run-self $report.hostImageObservation
            if($image.state -cne 'image-observed') { throw 'Actual PowerShell host image unobserved.' }
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            if ($manifest.appFiles.Count -lt 1 -or $manifest.appFiles.Count -gt 10000) { throw 'Application closure bound.' }
            [long]$total = 0
            foreach ($row in $manifest.appFiles) {
                if (![DesktopPolicy]::RelativeFile($row.path) -or !$seen.Add($row.path) -or
                    $row.path.StartsWith('config\patch2\', [StringComparison]::OrdinalIgnoreCase) -or
                    $row.path.StartsWith('.patch2-import', [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Invalid or preseeded app relative path.'
                }
                $total += $row.bytes
                if ($total -gt 2147483648) { throw 'Application byte bound.' }
                CopyFile (Join-Path $inputRoot ('app\' + $row.path)) (Join-Path $runRoot ('app\' + $row.path)) $row
            }
            $exe = Join-Path $runRoot 'app\FEBuilderGBA.Avalonia.exe'
            Plain $exe
            $config = Join-Path $runRoot 'app\config\config.xml'
            $configExpected = '<root><item><key>Language</key><value>en</value></item><item><key>func_auto_update</key><value>0</value></item></root>'
            if ([IO.File]::ReadAllText($config) -cne $configExpected) { throw 'Only the isolated ordinary config is accepted.' }
            $fixtureNames = @('zipdb-proof.gba','zipdb-valid.zip','zipdb-invalid.zip')
            if ($manifest.fixtures.Count -ne 3 -or $manifest.installedFiles.Count -ne 2) { throw 'Fixture closure mismatch.' }
            foreach ($name in $fixtureNames) {
                $rows = @($manifest.fixtures | Where-Object { $_.path -ceq $name })
                if ($rows.Count -ne 1 -or
                    ($name -ceq 'zipdb-proof.gba' -and $rows[0].bytes -ne 16777216) -or
                    ($name -cne 'zipdb-proof.gba' -and $rows[0].bytes -gt 65536)) { throw 'Fixture bounds.' }
                CopyFile (Join-Path $inputRoot ('desktop-proof-' + $manifest.runId + '\' + $name)) `
                    (Join-Path $runRoot ('fixtures\' + $name)) $rows[0]
            }
            foreach ($name in @('proof\PATCH_offline.txt','proof\payload.bin')) {
                $rows = @($manifest.installedFiles | Where-Object { $_.path -ceq $name })
                if ($rows.Count -ne 1 -or $rows[0].bytes -le 0 -or $rows[0].bytes -gt 4096 -or
                    $rows[0].sha256 -cnotmatch '^[0-9a-f]{64}$') { throw 'Installed file contract.' }
            }
            $descriptor = @($manifest.installedFiles | Where-Object { $_.path -ceq 'proof\PATCH_offline.txt' })[0]
            $payload = @($manifest.installedFiles | Where-Object { $_.path -ceq 'proof\payload.bin' })[0]
            $definition = "NAME=Offline ZIP Proof`nTYPE=BIN`nDESCRIPTION=Generated proof fixture.`nPATCHED_IF:0x200=0xAB 0xCD`nBIN:0x300=payload.bin`n"
            $definitionBytes = [Text.Encoding]::UTF8.GetBytes($definition)
            $definitionHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($definitionBytes)).ToLowerInvariant()
            $payloadHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]@(0xAA, 0x55))).ToLowerInvariant()
            if ($descriptor.bytes -ne $definitionBytes.Length -or $descriptor.sha256 -cne $definitionHash -or
                $payload.bytes -ne 2 -or $payload.sha256 -cne $payloadHash) { throw 'Only the fixed benign proof definition is accepted.' }
            CheckArchive (Join-Path $runRoot 'fixtures\zipdb-valid.zip') $true
            CheckArchive (Join-Path $runRoot 'fixtures\zipdb-invalid.zip') $false
            foreach ($name in @('profile\Desktop','profile\AppData\Roaming','profile\AppData\Local','scratch','empty-roms')) {
                NewDirectory (Join-Path $runRoot $name)
            }
            NewText (Join-Path $runRoot 'empty-git.config') ''
            Budget
            if ($clock.ElapsedMilliseconds -ge 150000) { throw 'Preparation deadline; no GUI launch.' }
            $report.empty_patch_library_initialized=Initialize-ProofEmptyPatchLibrary (Join-Path $runRoot 'app')
            Budget
            if ($clock.ElapsedMilliseconds -ge 150000) { throw 'Preparation deadline; no GUI launch.' }
            $readiness = [BoundedWindowsReadiness]::Capture()
            $report.readiness_sample_utc = [DateTime]::UtcNow.ToString('o')
            $report.readiness = $readiness
            if (!$readiness.Ready) { throw 'Preflight refused; no GUI launch.' }
            $start = [Diagnostics.ProcessStartInfo]::new()
            $start.FileName = $exe
            $start.WorkingDirectory = Join-Path $runRoot 'app'
            $start.UseShellExecute = $false
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $start.ArgumentList.Add('--rom=' + (Join-Path $runRoot 'fixtures\zipdb-proof.gba'))
            $start.Environment.Clear()
            $environment = @{
                SystemRoot = $proofConfiguration.machine.systemRoot; windir = $proofConfiguration.machine.systemRoot; SystemDrive = ([IO.Path]::GetPathRoot($proofConfiguration.machine.systemRoot).TrimEnd('\')); OS = 'Windows_NT'
                PATH = "$($proofConfiguration.machine.systemRoot)\System32;$($proofConfiguration.machine.systemRoot)"; ComSpec = (Join-Path $proofConfiguration.machine.systemRoot 'System32\cmd.exe')
                PATHEXT = '.COM;.EXE;.BAT;.CMD'; DOTNET_ROOT = $proofConfiguration.machine.dotnetRoot
                TEMP = (Join-Path $runRoot 'scratch'); TMP = (Join-Path $runRoot 'scratch')
                USERPROFILE = (Join-Path $runRoot 'profile'); HOME = (Join-Path $runRoot 'profile')
                APPDATA = (Join-Path $runRoot 'profile\AppData\Roaming')
                LOCALAPPDATA = (Join-Path $runRoot 'profile\AppData\Local')
                HTTP_PROXY = 'http://127.0.0.1:9'; HTTPS_PROXY = 'http://127.0.0.1:9'; ALL_PROXY = 'http://127.0.0.1:9'
                NO_PROXY = '127.0.0.1,localhost'; GIT_CONFIG_NOSYSTEM = '1'; GIT_CONFIG_COUNT = '0'
                GIT_CONFIG_GLOBAL = (Join-Path $runRoot 'empty-git.config'); GIT_TERMINAL_PROMPT = '0'
                DOTNET_CLI_TELEMETRY_OPTOUT = '1'; DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
                DOTNET_EnableDiagnostics = '0'; ROMS_DIR = (Join-Path $runRoot 'empty-roms')
            }
            foreach ($entry in $environment.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
            $report.launch_requested_utc = [DateTime]::UtcNow.ToString('o')
            $workflowState=@{process=$null}
            try { Invoke-ProofAppWorkflow }
            finally { $process=$workflowState.process }
            foreach ($row in $manifest.fixtures) {
                VerifyFile (Join-Path $runRoot ('fixtures\' + $row.path)) $row
                VerifyFile (Join-Path $inputRoot ('desktop-proof-' + $manifest.runId + '\' + $row.path)) $row
            }
            foreach ($row in $manifest.appFiles) { VerifyFile (Join-Path $inputRoot ('app\' + $row.path)) $row }
        } catch {
            # No raw exception messages, paths from providers, environment values, or desktop titles.
            $report.passed = $false
            $report.failure_type = $_.Exception.GetType().Name
            $report.failure_utc = [DateTime]::UtcNow.ToString('o')
        } finally {
            if($currentHost) { $currentHost.Dispose() }
            if ($process) {
                try {
                    $report.app_exit_observed_by_runner = $process.HasExited
                    if (!$report.app_exit_observed_by_runner) { $report.passed = $false }
                } catch { $report.passed = $false; $report.app_exit_observation_failed = $true }
                finally { $process.Dispose() }
            }
            $report.completed_utc = [DateTime]::UtcNow.ToString('o')
            $report.elapsed_ms = $clock.ElapsedMilliseconds
            if ($report.elapsed_ms -ge 420000) { $report.passed = $false; $report.overall_timeout = $true }
            if ($ownedOutput) {
                # Write a terminal result even when the budget has expired; never replace an old result.
                Plain $runRoot
                $stream = [IO.File]::Open((Join-Path $runRoot 'result.json'), [IO.FileMode]::CreateNew,
                    [IO.FileAccess]::Write, [IO.FileShare]::None)
                try {
                    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($report | ConvertTo-Json -Depth 12))
                    $stream.Write($bytes, 0, $bytes.Length)
                } finally { $stream.Dispose() }
            }
        }
        if (!$report.passed) { throw 'Bounded GUI acceptance failed/refused; preserve the exact owned outputs, no retry.' }
        Write-Output 'Bounded runtime assertions passed; independent owned-editor image review remains required.'
    }
    function Invoke-ProofAppWorkflow {
            $process = [Diagnostics.Process]::Start($start)
            $workflowState.process=$process
            $report.launch_returned_utc=[DateTime]::UtcNow.ToString('o')
            $appHandle = $process.SafeHandle
            $startTicks = $process.StartTime.ToUniversalTime().Ticks
            $stdoutDrain = $process.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null)
            $stderrDrain = $process.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
            $runnerId = $currentHost.Id
            $runnerTicks = $currentHost.StartTime.ToUniversalTime().Ticks
            $identity = @{
                run_id = $manifest.runId; input_sha256 = $InputManifestSha256
                source_sha256 = $SourceManifestSha256
                runner_pid = $runnerId; runner_start_ticks = $runnerTicks
                pid = $process.Id; start_ticks = $startTicks; exe = $exe
            } | ConvertTo-Json -Compress
            NewText (Join-Path $runRoot 'app-identity.pending') $identity
            [IO.File]::Move((Join-Path $runRoot 'app-identity.pending'), (Join-Path $runRoot 'app-identity.json'), $false)
            $report.appImageObservation=@{}
            $image=Get-ProcessImageObservation {$process.HasExited} { [BoundedProcessImage]::Read($appHandle) } `
                {420000-$clock.ElapsedMilliseconds} $exe ([StringComparison]::Ordinal) run-app-initial $report.appImageObservation
            if($image.state -cne 'image-observed' -or $process.HasExited) { throw 'Initial app image unobserved.' }
            $requestStop = [System.Action[DesktopResult]] {
                param($gui)
                $stop = @{
                    schema = 'windows-desktop-worker-stop-v1'; run_id = $manifest.runId
                    input_sha256 = $InputManifestSha256; source_sha256 = $SourceManifestSha256
                    runner_pid = $runnerId; runner_start_ticks = $runnerTicks
                    app_pid = $process.Id; app_start_ticks = $startTicks
                    requested_utc = $gui.CancellationRequestedUtc; reason = $gui.Failure
                    dispatch_admission_closed = $gui.DispatchAdmissionClosed; worker_joined = $gui.WorkerJoined
                    gui = $gui
                } | ConvertTo-Json -Depth 12 -Compress
                # A request is not proof of cancellation completion or permission to kill the app.
                NewText (Join-Path $runRoot 'worker-stop.pending') $stop
                [IO.File]::Move((Join-Path $runRoot 'worker-stop.pending'), (Join-Path $runRoot 'worker-stop.json'), $false)
            }
            $report.gui = [BoundedDesktopSmoke]::Run($process, $exe, $runRoot, $descriptor.sha256, $payload.sha256, $requestStop)
            if (!$report.gui.WorkerJoined -or !$report.gui.DispatchAdmissionClosed -or !$report.gui.Passed) {
                throw 'GUI failed; app cleanup belongs to the launcher after this runner exits.'
            }
            $report.passed = $report.gui.Passed
    }
    function CheckArchive([string]$path, [bool]$validArchive) {
        $stream = [IO.File]::OpenRead($path)
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
        try {
            if ($validArchive) {
                if ($archive.Entries.Count -ne 2) { throw 'Valid ZIP must contain exactly two small files.' }
                $entryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($entry in $archive.Entries) {
                    $relative = $entry.FullName.Replace('/', '\')
                    $matched = @($manifest.installedFiles | Where-Object { ('FE8U\' + $_.path) -ceq $relative })
                    if ($matched.Count -ne 1 -or !$entryNames.Add($relative) -or
                        $entry.Length -ne $matched[0].bytes -or $entry.Length -gt 4096) {
                        throw 'Unexpected valid ZIP schema.'
                    }
                    $content = $entry.Open()
                    try {
                        $sha = [Security.Cryptography.SHA256]::Create()
                        try { $hash = [Convert]::ToHexString($sha.ComputeHash($content)).ToLowerInvariant() }
                        finally { $sha.Dispose() }
                    } finally { $content.Dispose() }
                    if ($hash -cne $matched[0].sha256) { throw 'ZIP payload identity mismatch.' }
                }
            } else {
                if ($archive.Entries.Count -ne 1 -or
                    $archive.Entries[0].FullName -cne 'FE8U/../../zipdb-import-escape.txt' -or
                    $archive.Entries[0].Length -ne 1) { throw 'Unexpected invalid ZIP schema.' }
                $content = $archive.Entries[0].Open()
                try { if ($content.ReadByte() -ne 1) { throw 'Invalid ZIP payload mismatch.' } }
                finally { $content.Dispose() }
            }
        } finally { $archive.Dispose(); $stream.Dispose() }
    }
}
