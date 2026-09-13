# Bounded PS7 supervisor adapted from the reviewed readiness/old Windows launchers.
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
Assert-Proof ($SourceManifestSha256 -ceq $proofConfiguration.sourceManifest.sha256) 'Source configuration binding.'
Assert-Proof ($AuthorizationReference -ceq $proofConfiguration.approval.authorizationReference) 'Caller GUI approval binding.'
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSEdition -ne 'Core' -or !$IsWindows) { throw 'Windows PowerShell 7 required.' }
$PSModuleAutoLoadingPreference = 'None'
foreach ($name in @('Microsoft.PowerShell.Utility','Microsoft.PowerShell.Management','Microsoft.PowerShell.Security')) {
    Import-Module ([IO.Path]::Combine($PSHOME, 'Modules', $name, $name + '.psd1')) -ErrorAction Stop
}
$root=$proofConfiguration.outputRoot
function Plain([string]$path) {
    if (![IO.Path]::IsPathFullyQualified($path) -or $path.StartsWith('\\') -or
        $path.Contains('/') -or [IO.Path]::GetFullPath($path) -cne $path) { throw 'Noncanonical local path.' }
    for ($p = $path; $p; $p = [IO.Path]::GetDirectoryName($p)) {
        if ([IO.File]::GetAttributes($p) -band [IO.FileAttributes]::ReparsePoint) { throw 'Reparse ancestry.' }
    }
}
function Verify([string]$path, [string]$hash, [long]$limit) {
    Plain $path
    if ([IO.FileInfo]::new($path).Length -gt $limit -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) {
        throw 'Pinned identity mismatch.'
    }
}
Plain $root
Verify $InputManifest $InputManifestSha256 4194304
$manifest = Read-ProofInput $InputManifest $InputManifestSha256 $proofConfiguration
if ($manifest.schema -cne 'windows-desktop-bounded-input-v1' -or
    $manifest.applicationSource -cne $proofConfiguration.applicationSource -or
    $manifest.runId -cnotmatch '^[0-9a-f]{32}$') { throw 'Input contract.' }
if ($InputManifest -cne (Join-Path $root ('inputs-' + $manifest.runId + '\input-manifest.json'))) {
    throw 'Input location.'
}
$pwsh = Join-Path $PSHOME 'pwsh.exe'
if ($manifest.powershellHost.path -cne $pwsh) { throw 'PowerShell host mismatch.' }
Verify $pwsh $manifest.powershellHost.sha256 16777216
$policyReferences = [Collections.Generic.List[string]]::new()
$referenceSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if ($manifest.compileReferences.Count -lt 1 -or $manifest.compileReferences.Count -gt 256) {
    throw 'Compiler reference bound.'
}
foreach ($row in $manifest.compileReferences) {
    if ([IO.Path]::GetDirectoryName($row.path) -cne (Join-Path $PSHOME 'ref') -or
        [IO.Path]::GetExtension($row.path) -cne '.dll' -or !$referenceSeen.Add($row.path)) {
        throw 'Compiler reference path.'
    }
    Verify $row.path $row.sha256 268435456
    $policyReferences.Add($row.path)
}
# The launcher uses the same pure cleanup decision exercised by Policy.Tests.cs.
Add-Type -Path (Join-Path $Code 'Policy.cs') -ReferencedAssemblies $policyReferences.ToArray()
$launchRoot = Join-Path $root ('launch-' + $manifest.runId)
$runRoot = Join-Path $root ('run-' + $manifest.runId)
if (Test-Path -LiteralPath $launchRoot) { throw 'Consumed launch ID; never retry.' }
if (Test-Path -LiteralPath $runRoot) { throw 'Consumed run ID; never retry.' }
New-Item -ItemType Directory -Path $launchRoot -ErrorAction Stop | Out-Null
foreach ($name in @('scratch','profile','profile\AppData','profile\AppData\Roaming','profile\AppData\Local')) {
    New-Item -ItemType Directory -Path (Join-Path $launchRoot $name) -ErrorAction Stop | Out-Null
}
$start = [Diagnostics.ProcessStartInfo]::new()
$start.FileName = $pwsh
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.WorkingDirectory = $root
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
foreach ($arg in @('-NoLogo','-NoProfile','-NonInteractive','-STA','-File',(Join-Path $Code 'run.ps1'),
    '-Configuration',$Configuration,'-ConfigurationSha256',$ConfigurationSha256,
    '-InputManifest',$InputManifest,'-InputManifestSha256',$InputManifestSha256,
    '-SourceManifestSha256',$SourceManifestSha256,'-AuthorizationReference',$AuthorizationReference)) {
    $start.ArgumentList.Add($arg)
}
$start.Environment.Clear()
$environment = @{
    SystemRoot = $proofConfiguration.machine.systemRoot; windir = $proofConfiguration.machine.systemRoot; SystemDrive = ([IO.Path]::GetPathRoot($proofConfiguration.machine.systemRoot).TrimEnd('\'))
    PATH = (Join-Path $proofConfiguration.machine.systemRoot 'System32'); PSModulePath = (Join-Path $PSHOME 'Modules')
    TEMP = (Join-Path $launchRoot 'scratch'); TMP = (Join-Path $launchRoot 'scratch')
    USERPROFILE = (Join-Path $launchRoot 'profile'); HOME = (Join-Path $launchRoot 'profile')
    APPDATA = (Join-Path $launchRoot 'profile\AppData\Roaming')
    LOCALAPPDATA = (Join-Path $launchRoot 'profile\AppData\Local')
    HTTP_PROXY = 'http://127.0.0.1:9'; HTTPS_PROXY = 'http://127.0.0.1:9'; ALL_PROXY = 'http://127.0.0.1:9'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'; DOTNET_EnableDiagnostics = '0'
}
foreach ($entry in $environment.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
$runner = $null
$app = $null
$appReceipt = $null
$receipt = [ordered]@{ passed = $false; timed_out = $false; limit_ms = 480000
    started_utc = [DateTime]::UtcNow.ToString('o'); source_manifest_sha256 = $SourceManifestSha256
    input_manifest_sha256 = $InputManifestSha256; worker_stop_requested = $false
    execution_boundary_exited = $false
    runner_cleanup = @{ kill_attempted = $false; exit_confirmed = $false }
    app_cleanup = @{ identity_retained = $false; kill_attempted = $false; exit_confirmed = $false } }
$expectedApp = Join-Path $runRoot 'app\FEBuilderGBA.Avalonia.exe'
$clock = $null
function RetainApp {
    $path = Join-Path $runRoot 'app-identity.json'
    if ($script:appReceipt -or !(Test-Path -LiteralPath $path)) { return }
    Plain $path
    if ([IO.FileInfo]::new($path).Length -gt 2048) { throw 'App receipt bound.' }
    $r = Read-ProofJsonFile $path
    foreach($key in @('pid','start_ticks','runner_pid','runner_start_ticks')){
        Assert-Proof (($r[$key] -is [int] -or $r[$key] -is [long]) -and $r[$key] -gt 0) 'Integer retained identity required.'
    }
    if ($r.run_id -cne $manifest.runId -or $r.runner_pid -ne $runner.Id -or
        $r.runner_start_ticks -ne $runner.StartTime.ToUniversalTime().Ticks -or
        $r.input_sha256 -cne $InputManifestSha256 -or $r.source_sha256 -cne $SourceManifestSha256 -or
        $r.exe -cne $expectedApp -or
        $r.pid -le 0 -or $r.start_ticks -lt $runner.StartTime.ToUniversalTime().Ticks) {
        throw 'App receipt binding.'
    }
    $script:appReceipt = $r
    # One exact receipt-bound PID lookup, never enumeration. Keep its handle thereafter.
    try { $candidate = [Diagnostics.Process]::GetProcessById([int]$r.pid) }
    catch [ArgumentException] { return }
    try {
        if ($candidate.HasExited) { return }
        if ($candidate.StartTime.ToUniversalTime().Ticks -ne $r.start_ticks -or
            $candidate.MainModule.FileName -cne $expectedApp) { throw 'App identity changed.' }
        $null = $candidate.Handle
        $script:app = $candidate
        $script:receipt.app_cleanup.identity_retained = $true
        $script:receipt.app_cleanup.pid = $r.pid
        $script:receipt.app_cleanup.start_ticks = $r.start_ticks
        $candidate = $null
    } finally { if ($candidate) { $candidate.Dispose() } }
}
function CheckWorkerStop {
    $path = Join-Path $runRoot 'worker-stop.json'
    if (!(Test-Path -LiteralPath $path)) { return }
    Plain $path
    if ([IO.FileInfo]::new($path).Length -gt 131072) { throw 'Worker stop receipt bound.' }
    $r = Read-ProofJsonFile $path
    Assert-ProofBooleanFields $r @('dispatch_admission_closed') @('worker_joined')
    if (!$appReceipt -or $r.schema -cne 'windows-desktop-worker-stop-v1' -or
        $r.run_id -cne $manifest.runId -or $r.input_sha256 -cne $InputManifestSha256 -or
        $r.source_sha256 -cne $SourceManifestSha256 -or $r.runner_pid -ne $runner.Id -or
        $r.runner_start_ticks -ne $runnerTicks -or $r.app_pid -ne $appReceipt.pid -or
        $r.app_start_ticks -ne $appReceipt.start_ticks -or !$r.dispatch_admission_closed -or
        $r.worker_joined -or $r.reason -cnotmatch '^timeout:[a-z-]{1,64}$') {
        throw 'Worker stop receipt binding.'
    }
    $script:receipt.worker_stop_requested = $true
    $script:receipt.timed_out = $true
    $script:receipt.worker_stop_requested_utc = $r.requested_utc
    $script:receipt.worker_stop_reason = $r.reason
    $script:receipt.worker_stop_receipt = 'run-' + $manifest.runId + '\worker-stop.json'
    $script:receipt.worker_stop_sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    throw 'Worker deadline; terminate the runner boundary before app cleanup.'
}
try {
    $runner = [Diagnostics.Process]::Start($start)
    $runnerTicks = $runner.StartTime.ToUniversalTime().Ticks
    $receipt.runner_pid = $runner.Id
    $receipt.runner_start_ticks = $runnerTicks
    $outDrain = $runner.StandardOutput.BaseStream.CopyToAsync([IO.Stream]::Null)
    $errDrain = $runner.StandardError.BaseStream.CopyToAsync([IO.Stream]::Null)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (!$runner.WaitForExit(100)) {
        RetainApp
        CheckWorkerStop
        if ($clock.ElapsedMilliseconds -ge 480000) { $receipt.timed_out = $true; throw 'Runner deadline.' }
    }
    if ($clock.ElapsedMilliseconds -ge 480000) { $receipt.timed_out = $true; throw 'Late runner exit is not success.' }
    RetainApp
    CheckWorkerStop
    $resultPath = Join-Path $runRoot 'result.json'
    Plain $resultPath
    if ([IO.FileInfo]::new($resultPath).Length -gt 131072) { throw 'Result bound.' }
    $result = Read-ProofJsonFile $resultPath
    Assert-ProofBooleanFields $result @('passed') @()
    Assert-ProofBooleanFields $result.gui @('NormalCloseObserved','WorkerJoined','DispatchAdmissionClosed') @('TerminationBoundaryRequired','TimedOut')
    $receipt.passed = $runner.ExitCode -eq 0 -and $result.passed -and $result.gui.NormalCloseObserved -and
        $result.gui.WorkerJoined -and $result.gui.DispatchAdmissionClosed -and
        !$result.gui.TerminationBoundaryRequired -and !$result.gui.TimedOut -and $app -and $app.HasExited -and
        $result.source_manifest_sha256 -ceq $SourceManifestSha256 -and
        $result.input_manifest_sha256 -ceq $InputManifestSha256 -and $result.run_id -ceq $manifest.runId
    $receipt.result_sha256 = (Get-FileHash -LiteralPath $resultPath -Algorithm SHA256).Hash.ToLowerInvariant()
} catch { $receipt.passed = $false; $receipt.failure_type = $_.Exception.GetType().Name }
finally {
    $boundaryExited = $false
    if ($runner) {
        try {
            $boundaryExited = $runner.HasExited
            if (!$boundaryExited) {
                $receipt.passed = $false
                if ($runner.StartTime.ToUniversalTime().Ticks -ne $runnerTicks -or
                    $runner.MainModule.FileName -cne $pwsh) { throw 'Runner identity refused.' }
                $receipt.runner_cleanup.kill_attempted = $true
                $receipt.runner_cleanup.requested_utc = [DateTime]::UtcNow.ToString('o')
                $runner.Kill()
                $boundaryExited = $runner.WaitForExit(5000)
            }
            $receipt.runner_cleanup.exit_confirmed = $boundaryExited
            if ($boundaryExited) { $receipt.runner_cleanup.exit_confirmed_utc = [DateTime]::UtcNow.ToString('o') }
        } catch { $receipt.passed = $false; $receipt.runner_cleanup.identity_or_exit_failure = $true }
        $receipt.execution_boundary_exited = $boundaryExited
        if (!$boundaryExited) { $receipt.passed = $false }
        try {
            RetainApp
            if ($app) {
                $receipt.app_cleanup.exit_confirmed = $app.HasExited
            }
            if ($app -and !$receipt.app_cleanup.exit_confirmed) {
                $receipt.passed = $false
                if (![DesktopPolicy]::MayCleanupApp($boundaryExited,
                    $receipt.app_cleanup.identity_retained, $receipt.app_cleanup.kill_attempted)) {
                    $receipt.app_cleanup.skipped = 'runner-exit-unconfirmed-or-cleanup-already-attempted'
                    throw 'App cleanup refused without an exited execution boundary.'
                }
                if ($app.StartTime.ToUniversalTime().Ticks -ne $appReceipt.start_ticks -or
                    $app.MainModule.FileName -cne $expectedApp) { throw 'App cleanup identity refused.' }
                $receipt.app_cleanup.kill_attempted = $true
                $receipt.app_cleanup.requested_utc = [DateTime]::UtcNow.ToString('o')
                $app.Kill()
                $receipt.app_cleanup.exit_confirmed = $app.WaitForExit(5000)
            }
            if ($receipt.app_cleanup.exit_confirmed) {
                $receipt.app_cleanup.exit_confirmed_utc = [DateTime]::UtcNow.ToString('o')
            } else { $receipt.passed = $false }
            if (!$appReceipt) { $receipt.app_identity_not_published = $true }
            if (!$app) { $receipt.app_cleanup.skipped = 'no-retained-app-identity'; $receipt.passed = $false }
        } catch { $receipt.passed = $false; $receipt.app_cleanup.identity_or_exit_failure = $true }
        finally { if ($app) { $app.Dispose() }; $runner.Dispose() }
    }
    if ($null -ne $clock -and $clock.ElapsedMilliseconds -ge 480000) { $receipt.timed_out = $true; $receipt.passed = $false }
    Plain $launchRoot
    $receipt.completed_utc = [DateTime]::UtcNow.ToString('o')
    $stream = [IO.File]::Open((Join-Path $launchRoot 'launch-result.json'), [IO.FileMode]::CreateNew)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($receipt | ConvertTo-Json -Depth 6))
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
}
if (!$receipt.passed) { throw 'One bounded attempt failed/refused; preserve outputs, no retry.' }
Write-Output 'Runtime assertions passed; independent image review remains required.'
