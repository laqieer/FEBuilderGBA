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
function New-TestPacket {
    $policy = Get-MetadataPolicy
    $sources = [ordered]@{}
    foreach ($name in $policy.SourcePaths) { $sources[$name] = '0' * 64 }
    return [ordered]@{
        schema = 'issue2160-current-metadata-v1'
        expectedHead = '0' * 40
        sourceSha256 = $sources
        historicalSha256 = $policy.Historical
        receiptStem = 'issue-2160-current-metadata-20260914T090444Z'
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

$source = [IO.File]::ReadAllText("$PSScriptRoot\..\Invoke-LinuxX11Metadata.ps1")
Assert-True (-not ($source -match 'Invoke-Expression|Start-Process|Get-Process|Stop-Process|taskkill|\.Kill\(\s*\$true')) 'Forbidden command/termination surface.'
Write-Output 'Linux metadata supervisor pure contracts passed.'
