# SPDX-License-Identifier: GPL-3.0-or-later
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. "$PSScriptRoot\..\Invoke-LinuxX11Metadata.ps1"

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Rejected([scriptblock]$Action) {
    $rejected = $false
    try { $null = & $Action } catch { $rejected = $true }
    Assert-True $rejected 'Expected fail-closed rejection.'
}
function New-TestPacket([switch]$IsolationInterface) {
    $policy = Get-MetadataPolicy -IsolationInterface:$IsolationInterface
    $sources = [ordered]@{}
    foreach ($name in $policy.SourcePaths) { $sources[$name] = '0' * 64 }
    return [ordered]@{
        schema = $policy.Schema
        expectedHead = '0' * 40
        sourceSha256 = $sources
        historicalSha256 = $policy.Historical
        receiptStem = $policy.Stem
    }
}

$packet = New-TestPacket
$json = $packet | ConvertTo-Json -Depth 8 -Compress
$parsed = ConvertFrom-MetadataPacket $json
Assert-True ($parsed.expectedHead -ceq ('0' * 40)) 'Packet did not round-trip.'
Assert-Rejected { ConvertFrom-MetadataPacket ($json -replace '"schema":', '"extra":1,"schema":') }
Assert-Rejected { ConvertFrom-MetadataPacket ($json -replace '"schema":', '"schema":"duplicate","schema":') }
Assert-Rejected { ConvertFrom-MetadataPacket ($json -replace '"schema":', '"Schema":') }
Assert-Rejected { ConvertFrom-MetadataPacket ($json -replace 'issue2160-current-metadata-v1', 'arbitrary-command') }
Assert-Rejected { ConvertFrom-MetadataPacket (' ' * 16385) }
Assert-Rejected { ConvertFrom-MetadataPacket ($json -replace '"expectedHead":"[0-9]+"', '"expectedHead":3') }
Assert-Rejected { ConvertFrom-MetadataPacket ($json -replace 'd29c2e5d', '00000000') }
Assert-Rejected { ConvertFrom-MetadataPacket ($json -replace '"sourceSha256":', '"sourceSha256":{},"other":') }
Assert-Rejected { ConvertFrom-MetadataPacket ($json -replace '"receiptStem":', '"receiptStem":"bad","receiptStem":') }

$info = New-MetadataStartInfo
Assert-True (-not $info.UseShellExecute) 'Shell execution is forbidden.'
Assert-True (-not $info.RedirectStandardInput) 'Unexpected stdin redirection.'
Assert-True ($info.RedirectStandardOutput -and $info.RedirectStandardError) 'Missing pipe redirection.'
Assert-True ($info.ArgumentList.Count -eq 16) 'Expected sixteen literal arguments.'
Assert-True (($info.ArgumentList | Select-Object -Last 2) -join ' ' -ceq 'scripts/linux_x11_metadata.py --observe') 'Unexpected guest entry point.'
Assert-True ($info.Environment.Count -eq 3) 'Inherited environment leaked.'
Assert-True ($info.Environment['SystemRoot'] -ceq 'C:\Windows') 'Wrong SystemRoot.'
Assert-True (-not $info.Environment.ContainsKey('WSLENV')) 'WSLENV leaked.'
$expectedArguments = @(
    '--distribution', 'Ubuntu', '--cd'
    '/mnt/c/Projects/laqieer/FEBuilderGBA/TestResults/worktrees/issue-2160-linux-x11-20260913T075332Z'
    '--exec', '/usr/bin/env', '-i', 'PATH=/usr/bin:/bin', 'LANG=C.UTF-8'
    'PYTHONDONTWRITEBYTECODE=1', '/usr/bin/python3', '-I', '-S', '-B'
    'scripts/linux_x11_metadata.py', '--observe'
)
Assert-True (($info.ArgumentList -join "`n") -ceq ($expectedArguments -join "`n")) 'Literal argv changed.'

$interfacePolicy = Get-MetadataPolicy -IsolationInterface
$interfacePacket = New-TestPacket -IsolationInterface
$interfaceJson = $interfacePacket | ConvertTo-Json -Depth 8 -Compress
$interfaceParsed = ConvertFrom-MetadataPacket $interfaceJson
Assert-True ($interfaceParsed['schema'] -ceq 'issue2160-isolation-interface-v1') 'Wrong interface schema.'
Assert-True ($interfaceParsed['historicalSha256'].Count -eq 10) 'N1 must preserve exactly ten historical inputs.'
Assert-True ($interfaceParsed['sourceSha256'].Count -eq 10) 'N1 must pin all ten SOURCE paths.'
Assert-True ((Get-MetadataPolicy).Historical.Count -eq 2) 'Metadata mode history changed.'
Assert-True ($interfacePolicy.Stem -ceq 'issue-2160-isolation-interface-20260914T130000Z') 'N1 allocation changed.'
Assert-Rejected { ConvertFrom-MetadataPacket ($interfaceJson -replace 'isolation-interface-v1', 'current-metadata-v1') }
Assert-Rejected { ConvertFrom-MetadataPacket ($interfaceJson -replace 'isolation-interface-20260914T130000Z', 'current-metadata-20260914T090444Z') }
Assert-Rejected { ConvertFrom-MetadataPacket ($interfaceJson -replace '"schema":', '"schema":"duplicate","schema":') }
Assert-Rejected { ConvertFrom-MetadataPacket ($interfaceJson -replace '"schema":', '"Schema":') }
Assert-Rejected { ConvertFrom-MetadataPacket ($interfaceJson -replace '"schema":', '"command":"--user","schema":') }
foreach ($pin in $interfacePolicy.Historical.GetEnumerator()) {
    Assert-Rejected { ConvertFrom-MetadataPacket ($interfaceJson.Replace($pin.Value, '0' * 64)) }
    Assert-Rejected { ConvertFrom-MetadataPacket ($interfaceJson.Replace($pin.Key, 'unexpected-history.json')) }
}
$interfaceInfo = New-MetadataStartInfo -IsolationInterface
$interfaceArguments = @($expectedArguments)
$interfaceArguments[-1] = '--observe-isolation-interface'
Assert-True (($interfaceInfo.ArgumentList -join "`n") -ceq ($interfaceArguments -join "`n")) 'Only the final guest mode may change.'
Assert-True ($interfaceInfo.Environment.Count -eq 3) 'Interface Windows environment leaked.'
foreach ($key in $info.Environment.Keys) {
    Assert-True ($interfaceInfo.Environment[$key] -ceq $info.Environment[$key]) 'Interface environment changed.'
}
foreach ($policy in @((Get-MetadataPolicy), $interfacePolicy)) {
    $fixedPacket = $policy.Root + '\' + $policy.Stem + '.packet.json'
    $selected = Get-MetadataPacketPolicy $fixedPacket
    Assert-True ($selected.Schema -ceq $policy.Schema) 'Exact absolute profile selection failed.'
    Assert-Rejected { Get-MetadataPacketPolicy ($policy.Stem + '.packet.json') }
    Assert-Rejected { Get-MetadataPacketPolicy ($fixedPacket + '.other') }
    Assert-Rejected { Get-MetadataPacketPolicy $fixedPacket.ToUpperInvariant() }
}

$originalReader = ${function:Read-MetadataBytes}
try {
    $script:fakeHead = '0' * 40
    $script:fakeBranch = 'ref: refs/heads/fix/issue-2160-linux-x11-attribution'
    $script:headReads = [Collections.Generic.List[string]]::new()
    function Read-MetadataBytes([string]$Path, [int]$Limit) {
        Assert-True ($Limit -eq 256) 'Unexpected head-read limit.'
        $script:headReads.Add($Path)
        $value = switch -CaseSensitive ($Path) {
            'C:\Projects\laqieer\FEBuilderGBA\TestResults\worktrees\issue-2160-linux-x11-20260913T075332Z\.git' {
                'gitdir: C:/Projects/laqieer/FEBuilderGBA/.git/worktrees/issue-2160-linux-x11-20260913T075332Z'
            }
            'C:\Projects\laqieer\FEBuilderGBA\.git\worktrees\issue-2160-linux-x11-20260913T075332Z\HEAD' { $script:fakeBranch }
            'C:\Projects\laqieer\FEBuilderGBA\.git\refs\heads\fix\issue-2160-linux-x11-attribution' { $script:fakeHead }
            default { throw 'Unexpected Git metadata path.' }
        }
        return ,([Text.Encoding]::UTF8.GetBytes($value + "`n"))
    }
    Assert-MetadataHead ('0' * 40)
    Assert-True ($script:headReads.Count -eq 3) 'Head verification must have three fixed reads.'
    $script:fakeHead = '1' * 40
    Assert-Rejected { Assert-MetadataHead ('0' * 40) }
    $script:fakeHead = '0' * 40
    $script:fakeBranch = 'ref: refs/heads/other'
    Assert-Rejected { Assert-MetadataHead ('0' * 40) }
} finally { ${function:Read-MetadataBytes} = $originalReader }

function New-InterfaceReceipt {
    $commands = @()
    foreach ($option in @('--version', '--help')) {
        $limit = if ($option -ceq '--version') { 512 } else { 8192 }
        $commands += @{
            option = $option; attempted = $true; normal_completion = $true
            exit_code = 0; failure = $null; kill_attempted = $false
            termination_confirmed = $true; elapsed_seconds = 0.02
            streams = @{
                stdout = @{ limit = $limit; observed_bytes = 3; eof = $true; overflow = $false; error = $null; base64 = 'YWJj' }
                stderr = @{ limit = 512; observed_bytes = 0; eof = $true; overflow = $false; error = $null; base64 = '' }
            }
        }
    }
    return @{
        schema = 'issue2160-isolation-interface-v1'; status = 'observed'
        cleanup_failures = @(); elapsed_seconds = 0.1
        checks = @{ platform = $true; uid = $true; euid = $true; python = $true; version = $true; architecture = $true; cwd = $true }
        binary_documentation_attempted = $true; binary_documentation_observed = $true
        native_attempted = $false; namespace_attempted = $false; primitive_accepted = $false
        application_accepted = $false; isolation_accepted = $false; descendant_cleanup_proven = $false
        descriptor_bound_including_setup = 14
        commands = $commands
        binary = @{ path = '/usr/bin/unshare'; kind = 'regular'; mode = '0755'; uid = 0; gid = 0; sha256 = '0' * 64; bytes = 20; file_capabilities_absent = $true }
    }
}
Assert-MetadataReceipt (New-InterfaceReceipt) $interfacePolicy
foreach ($field in @('native_attempted', 'namespace_attempted', 'primitive_accepted', 'application_accepted', 'isolation_accepted', 'descendant_cleanup_proven')) {
    $receipt = New-InterfaceReceipt
    $receipt[$field] = $true
    Assert-Rejected { Assert-MetadataReceipt $receipt $interfacePolicy }
}
foreach ($field in @('binary_documentation_attempted', 'binary_documentation_observed')) {
    $receipt = New-InterfaceReceipt
    $receipt[$field] = $false
    Assert-Rejected { Assert-MetadataReceipt $receipt $interfacePolicy }
}
foreach ($field in @('eof', 'overflow', 'error', 'observed_bytes')) {
    $receipt = New-InterfaceReceipt
    $receipt.commands[0].streams.stdout[$field] = switch ($field) {
        'eof' { $false }; 'overflow' { $true }; 'error' { 'injected' }; 'observed_bytes' { 4 }
    }
    Assert-Rejected { Assert-MetadataReceipt $receipt $interfacePolicy }
}
$receipt = New-InterfaceReceipt
$receipt.commands[1].option = '--user'
Assert-Rejected { Assert-MetadataReceipt $receipt $interfacePolicy }
$receipt = New-InterfaceReceipt
$receipt.descriptor_bound_including_setup = 17
Assert-Rejected { Assert-MetadataReceipt $receipt $interfacePolicy }
$receipt = New-InterfaceReceipt
$receipt.commands[0].elapsed_seconds = 1
Assert-Rejected { Assert-MetadataReceipt $receipt $interfacePolicy }

function New-FakePipe([byte[]]$Bytes, [bool]$Pending = $false) {
    $pipe = [pscustomobject]@{
        Bytes = $Bytes; Position = 0; Pending = $Pending; Disposed = $false
        Reads = [System.Collections.Generic.List[int]]::new()
        Completion = [System.Threading.Tasks.TaskCompletionSource[int]]::new()
    }
    $pipe | Add-Member ScriptMethod ReadAsync {
        param($buffer, $offset, $count, $token)
        $this.Reads.Add($count)
        if ($this.Pending) { return $this.Completion.Task }
        $size = [Math]::Min($count, $this.Bytes.Length - $this.Position)
        [Array]::Copy($this.Bytes, $this.Position, $buffer, $offset, $size)
        $this.Position += $size
        return [System.Threading.Tasks.Task]::FromResult([int]$size)
    }
    $pipe | Add-Member ScriptMethod Dispose { $this.Disposed = $true }
    return $pipe
}
function New-FakeProcess($Stdout, $Stderr, [bool]$Exited = $true) {
    $process = [pscustomobject]@{
        StandardOutput = [pscustomobject]@{ BaseStream = $Stdout }
        StandardError = [pscustomobject]@{ BaseStream = $Stderr }
        HasExited = $Exited; ExitCode = 0; Id = 12345
        SafeHandle = [object]::new(); StartTime = [DateTime]::UtcNow
        StartCount = 0; KillCount = 0; Disposed = $false
    }
    $process | Add-Member ScriptMethod Start { $this.StartCount++; return $true }
    $process | Add-Member ScriptMethod Kill { $this.KillCount++; $this.HasExited = $true }
    $process | Add-Member ScriptMethod Dispose { $this.Disposed = $true }
    return $process
}
function Invoke-FakeCapture($Process) {
    $state = [pscustomobject]@{ Seconds = 0.0 }
    $now = { $state.Seconds }.GetNewClosure()
    $pause = { param($pending, $millis, $owned) $state.Seconds += 0.125 }.GetNewClosure()
    return Invoke-MetadataCapture -Process $Process -Now $now -Wait $pause
}

$stdout = New-FakePipe ([Text.Encoding]::UTF8.GetBytes('{"status":"observed"}'))
$stderr = New-FakePipe ([byte[]]::new(0))
$process = New-FakeProcess $stdout $stderr
$capture = Invoke-FakeCapture $process
Assert-True $capture.Record.ok 'Complete fake streams should pass capture.'
Assert-True ($capture.Record.streams.stdout.eof -and $capture.Record.streams.stderr.eof) 'EOF must be observed.'
Assert-True ($process.StartCount -eq 1 -and $process.KillCount -eq 0) 'Wrong original-process actions.'
Assert-True ($stdout.Disposed -and $stderr.Disposed -and $process.Disposed) 'Resources not closed.'
Assert-True (-not $capture.Record.descendant_cleanup_proven) 'Outer exit cannot prove guest cleanup.'

$stdout = New-FakePipe ([byte[]]::new(16384))
$stderr = New-FakePipe ([byte[]]::new(4096))
$process = New-FakeProcess $stdout $stderr
$capture = Invoke-FakeCapture $process
Assert-True $capture.Record.ok 'Exactly capped streams plus observed EOF should pass capture.'
Assert-True ($stdout.Reads.Contains(1) -and $stderr.Reads.Contains(1)) 'EOF needs its bounded sentinel read.'

$process = New-FakeProcess (New-FakePipe ([byte[]]::new(0))) (New-FakePipe ([byte[]]::new(0)))
$process.ExitCode = 1
$capture = Invoke-FakeCapture $process
Assert-True (-not $capture.Record.ok -and $capture.Record.exit_code -eq 1) 'Nonzero exit cannot pass.'
Assert-True ($process.KillCount -eq 0) 'Do not kill an already exited child.'

foreach ($name in @('stdout', 'stderr')) {
    $limit = if ($name -ceq 'stdout') { 16384 } else { 4096 }
    $stdout = New-FakePipe ([byte[]]::new($(if ($name -ceq 'stdout') { $limit + 2 } else { 0 })))
    $stderr = New-FakePipe ([byte[]]::new($(if ($name -ceq 'stderr') { $limit + 2 } else { 0 })))
    $process = New-FakeProcess $stdout $stderr $false
    $capture = Invoke-FakeCapture $process
    Assert-True (-not $capture.Record.ok) 'Overflow must fail.'
    Assert-True $capture.Record.streams[$name].overflow 'Overflow was not recorded.'
    Assert-True ($capture.Record.streams[$name].observed_bytes -eq $limit + 1) 'Read beyond one overflow sentinel.'
    Assert-True ($process.KillCount -eq 1) 'Must kill the retained original object once.'
}

$stdout = New-FakePipe ([byte[]]::new(0)) $true
$stderr = New-FakePipe ([byte[]]::new(0)) $true
$process = New-FakeProcess $stdout $stderr $false
$capture = Invoke-FakeCapture $process
Assert-True (-not $capture.Record.ok -and $capture.Record.timed_out) 'Unfinished reads must time out.'
Assert-True ($process.KillCount -eq 1 -and $capture.Record.termination_confirmed) 'Bounded original-process abort missing.'
Assert-True (-not $capture.Record.streams.stdout.eof) 'Cancellation is not EOF.'
Assert-True ($capture.Record.elapsed_seconds -le 10) 'Original total budget exceeded.'

$stdout = New-FakePipe ([byte[]]::new(0))
$stdout | Add-Member ScriptMethod Dispose { throw 'injected close error' } -Force
$process = New-FakeProcess $stdout (New-FakePipe ([byte[]]::new(0)))
$capture = Invoke-FakeCapture $process
Assert-True (-not $capture.Record.ok -and $null -ne $capture.Record.cleanup.stdout) 'Cleanup error cannot pass.'
Assert-True $process.StandardError.BaseStream.Disposed 'One close error must not skip the other stream.'

$stdout = New-FakePipe ([byte[]]::new(0))
$stderr = New-FakePipe ([byte[]]::new(0))
$stderr | Add-Member ScriptMethod ReadAsync { param($buffer, $offset, $count, $token) throw 'injected read setup error' } -Force
$process = New-FakeProcess $stdout $stderr $false
$capture = Invoke-FakeCapture $process
Assert-True (-not $capture.Record.ok -and $process.KillCount -eq 1) 'Setup error must fail and abort the owned child.'
Assert-True ($stdout.Disposed -and $stderr.Disposed) 'Acquired streams must both close on setup error.'

$diagnostic = [Text.Encoding]::UTF8.GetBytes('retained sibling diagnostic')
foreach ($failureKind in @('setup', 'completed-read')) {
    foreach ($failedName in @('stdout', 'stderr')) {
        $stdout = New-FakePipe $diagnostic
        $stderr = New-FakePipe $diagnostic
        $failed = if ($failedName -ceq 'stdout') { $stdout } else { $stderr }
        if ($failureKind -ceq 'setup') {
            $failed | Add-Member ScriptMethod ReadAsync {
                param($buffer, $offset, $count, $token)
                throw 'injected symmetric setup failure'
            } -Force
        } else {
            $failed.Completion.SetException([Exception]::new('injected completed read failure'))
            $failed | Add-Member ScriptMethod ReadAsync {
                param($buffer, $offset, $count, $token)
                return $this.Completion.Task
            } -Force
        }
        $process = New-FakeProcess $stdout $stderr $false
        $capture = Invoke-FakeCapture $process
        $sibling = if ($failedName -ceq 'stdout') { 'stderr' } else { 'stdout' }
        Assert-True (-not $capture.Record.ok) 'A stream failure cannot pass.'
        Assert-True ($capture.Record.streams[$sibling].retained_bytes -eq $diagnostic.Length) "$failureKind on $failedName discarded completed sibling bytes."
        $retained = if ($sibling -ceq 'stdout') { $capture.Stdout } else { $capture.Stderr }
        Assert-True ([Convert]::ToHexString($retained) -ceq [Convert]::ToHexString($diagnostic)) 'Sibling byte content changed.'
        Assert-True ($null -ne $capture.Record.streams[$failedName].error) 'Failed stream error not retained.'
        Assert-True ($process.KillCount -eq 1) 'Failure must retain original-object-only abort.'
    }
}

$source = [IO.File]::ReadAllText("$PSScriptRoot\..\Invoke-LinuxX11Metadata.ps1")
Assert-True (-not ($source -match 'Invoke-Expression|Start-Process|Get-Process|Stop-Process|taskkill|\.Kill\(\s*\$true')) 'Forbidden command/termination surface.'
Write-Output 'Linux metadata supervisor pure contracts passed.'
