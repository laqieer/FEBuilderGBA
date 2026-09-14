# SPDX-License-Identifier: GPL-3.0-or-later
#requires -Version 7.0
[CmdletBinding()]
param([string]$PacketPath)

function Get-MetadataPolicy {
    [pscustomobject]@{
        Root = 'C:\Projects\laqieer\FEBuilderGBA\TestResults\worktrees\issue-2160-linux-x11-20260913T075332Z'
        Stem = 'issue-2160-current-metadata-20260914T090444Z'
        SourcePaths = @(
            '.github/workflows/crossplatform.yml'
            'docs/LINUX-DESKTOP-AUTOMATION.md'
            'scripts/linux_x11.py'
            'scripts/tests/linux_x11_native_smoke.py'
            'scripts/tests/test_linux_x11.py'
            'scripts/tests/test_crossplatform_workflow.py'
            'scripts/linux_x11_metadata.py'
            'scripts/Invoke-LinuxX11Metadata.ps1'
            'scripts/tests/test_linux_x11_metadata.py'
            'scripts/tests/test_linux_x11_metadata_supervisor.ps1'
        )
        Historical = [ordered]@{
            'linux-x11-smoke-e9c5b695-20260913T083114Z-7d2160a1/receipt.json' = 'd29c2e5d9ee2f835bed44b6e878c57a1fb6551b2ffa00d0dfd91f444203aa8fb'
            'linux-x11-smoke-6b01e234-20260913T095854Z-6f9c2a10/receipt.json' = '94ff7a9f9cd0b60aae7b42c110a6a8248f5cf4de29fb735a810fee79cf0d4716'
        }
    }
}

function ConvertFrom-MetadataPacket([string]$Json) {
    if ([Text.Encoding]::UTF8.GetByteCount($Json) -gt 16384) { throw 'Packet byte limit.' }
    $options = [System.Text.Json.JsonDocumentOptions]::new()
    $options.MaxDepth = 4
    $document = [System.Text.Json.JsonDocument]::Parse($Json, $options)
    try {
        $root = $document.RootElement
        if ($root.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw 'Packet must be an object.' }
        $allowed = @('schema', 'expectedHead', 'sourceSha256', 'historicalSha256', 'receiptStem')
        $packet = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        foreach ($property in $root.EnumerateObject()) {
            if ($allowed -cnotcontains $property.Name -or $packet.ContainsKey($property.Name)) { throw 'Unknown or duplicate packet key.' }
            if ($property.Name -cin @('sourceSha256', 'historicalSha256')) {
                if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw 'Pin map must be an object.' }
                $pins = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
                foreach ($pin in $property.Value.EnumerateObject()) {
                    if ($pins.ContainsKey($pin.Name) -or $pin.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { throw 'Invalid or duplicate pin.' }
                    $value = $pin.Value.GetString()
                    if ($value -cnotmatch '\A[0-9a-f]{64}\z') { throw 'Invalid SHA256.' }
                    $pins.Add($pin.Name, $value)
                }
                $packet.Add($property.Name, $pins)
            } else {
                if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { throw 'Packet field must be a string.' }
                $packet.Add($property.Name, $property.Value.GetString())
            }
        }
        if ($packet.Count -ne 5) { throw 'Missing packet keys.' }
        $policy = Get-MetadataPolicy
        if ($packet['schema'] -cne 'issue2160-current-metadata-v1' -or
            $packet['expectedHead'] -cnotmatch '\A[0-9a-f]{40}\z' -or
            $packet['receiptStem'] -cne $policy.Stem) { throw 'Packet binding mismatch.' }
        if ($packet['sourceSha256'].Count -ne $policy.SourcePaths.Count) { throw 'Wrong source pin set.' }
        foreach ($name in $policy.SourcePaths) {
            if (-not $packet['sourceSha256'].ContainsKey($name)) { throw 'Missing exact source pin.' }
        }
        if ($packet['historicalSha256'].Count -ne 2) { throw 'Wrong historical pin set.' }
        foreach ($pin in $policy.Historical.GetEnumerator()) {
            if (-not $packet['historicalSha256'].ContainsKey($pin.Key) -or
                $packet['historicalSha256'][$pin.Key] -cne $pin.Value) { throw 'Historical receipt mapping mismatch.' }
        }
        return ,$packet
    } finally {
        $document.Dispose()
    }
}

function New-MetadataStartInfo {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = 'C:\Windows\System32\wsl.exe'
    $info.WorkingDirectory = (Get-MetadataPolicy).Root
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.RedirectStandardInput = $false
    $info.CreateNoWindow = $true
    $info.Environment.Clear()
    $info.Environment['SystemRoot'] = 'C:\Windows'
    $info.Environment['WINDIR'] = 'C:\Windows'
    $info.Environment['PATH'] = 'C:\Windows\System32'
    foreach ($argument in @(
        '--distribution', 'Ubuntu', '--cd'
        '/mnt/c/Projects/laqieer/FEBuilderGBA/TestResults/worktrees/issue-2160-linux-x11-20260913T075332Z'
        '--exec', '/usr/bin/env', '-i', 'PATH=/usr/bin:/bin', 'LANG=C.UTF-8'
        'PYTHONDONTWRITEBYTECODE=1', '/usr/bin/python3', '-I', '-S', '-B'
        'scripts/linux_x11_metadata.py', '--observe'
    )) { $info.ArgumentList.Add($argument) }
    return $info
}

function Get-MetadataShortError([Exception]$ErrorObject) {
    $text = $ErrorObject.GetType().Name + ': ' + $ErrorObject.Message
    return $text.Substring(0, [Math]::Min(300, $text.Length))
}

function Invoke-MetadataCapture {
    param(
        [Parameter(Mandatory)]$Process,
        [Parameter(Mandatory)][scriptblock]$Now,
        [Parameter(Mandatory)][scriptblock]$Wait
    )
    $cancel = [Threading.CancellationTokenSource]::new()
    $record = [ordered]@{
        started = $false; pid = $null; process_start_utc = $null
        failure = $null; normal_completion = $false; timed_out = $false
        aborted = $false; kill_attempted = $false; kill_error = $null
        termination_confirmed = $false; confirmation_error = $null
        exit_observed = $false; exit_code = $null; exit_error = $null
        capture_budget_seconds = 5; confirmation_budget_seconds = 5
        elapsed_seconds = $null; deadline_exceeded = $false
        cleanup = [ordered]@{ cancellation = $null; stdout = $null; stderr = $null; process = $null; token = $null }
        ok = $false; streams = [ordered]@{}; descendant_cleanup_proven = $false
    }
    $streams = @(
        [pscustomobject]@{ Name = 'stdout'; Limit = 16384; Buffer = [byte[]]::new(16385); Observed = 0; Eof = $false; Overflow = $false; Error = $null; Stream = $null; Task = $null }
        [pscustomobject]@{ Name = 'stderr'; Limit = 4096; Buffer = [byte[]]::new(4097); Observed = 0; Eof = $false; Overflow = $false; Error = $null; Stream = $null; Task = $null }
    )
    $retainedHandle = $null
    $started = & $Now
    try {
        $record.started = $Process.Start()
        if (-not $record.started) { throw 'Exact outer process did not start.' }
        $streams[0].Stream = $Process.StandardOutput.BaseStream
        $streams[1].Stream = $Process.StandardError.BaseStream
        foreach ($stream in $streams) {
            try { $stream.Task = $stream.Stream.ReadAsync($stream.Buffer, 0, 4096, $cancel.Token) }
            catch { $stream.Error = Get-MetadataShortError $_.Exception; throw }
        }
        $retainedHandle = $Process.SafeHandle
        $record.pid = $Process.Id
        $record.process_start_utc = $Process.StartTime.ToUniversalTime().ToString('o')
        while ($true) {
            foreach ($stream in $streams) {
                if ((& $Now) - $started -ge 5) {
                    $record.timed_out = $true
                    throw 'Outer launch/capture deadline.'
                }
                if ($null -ne $stream.Task -and $stream.Task.IsCompleted) {
                    try { $count = $stream.Task.GetAwaiter().GetResult() }
                    catch { $stream.Error = Get-MetadataShortError $_.Exception; throw }
                    $stream.Task = $null
                    if ($count -eq 0) {
                        $stream.Eof = $true
                    } else {
                        $stream.Observed += $count
                        if ($stream.Observed -gt $stream.Limit) {
                            $stream.Overflow = $true
                            throw ('Outer ' + $stream.Name + ' raw-byte cap.')
                        }
                        if ((& $Now) - $started -ge 5) {
                            $record.timed_out = $true
                            throw 'Outer launch/capture deadline.'
                        }
                        $size = [Math]::Min(4096, $stream.Limit + 1 - $stream.Observed)
                        try { $stream.Task = $stream.Stream.ReadAsync($stream.Buffer, $stream.Observed, $size, $cancel.Token) }
                        catch { $stream.Error = Get-MetadataShortError $_.Exception; throw }
                    }
                }
            }
            if ((& $Now) - $started -ge 5) {
                $record.timed_out = $true
                throw 'Outer completion deadline.'
            }
            if ($Process.HasExited -and $streams[0].Eof -and $streams[1].Eof) {
                if ((& $Now) - $started -ge 5) {
                    $record.timed_out = $true
                    throw 'Completion was not observed within the original deadline.'
                }
                $record.exit_observed = $true
                $record.normal_completion = $true
                break
            }
            $millis = [int][Math]::Min(50, [Math]::Floor((5 - ((& $Now) - $started)) * 1000))
            if ($millis -le 0) { continue }
            $pending = [Threading.Tasks.Task[]]@($streams | Where-Object { $null -ne $_.Task } | ForEach-Object { $_.Task })
            $null = & $Wait $pending $millis $Process
        }
    } catch {
        $record.failure = Get-MetadataShortError $_.Exception
        $record.aborted = $true
        $abortDeadline = [Math]::Min($started + 10, (& $Now) + 5)
        try { $cancel.Cancel() } catch { $record.cleanup.cancellation = Get-MetadataShortError $_.Exception }
        if ($record.started) {
            try {
                if (-not $Process.HasExited) {
                    $record.kill_attempted = $true
                    $Process.Kill()
                }
            } catch { $record.kill_error = Get-MetadataShortError $_.Exception }
            try {
                while ((& $Now) -lt $abortDeadline) {
                    if ($Process.HasExited) {
                        if ((& $Now) -lt $abortDeadline) {
                            $record.termination_confirmed = $true
                            $record.exit_observed = $true
                        }
                        break
                    }
                    $millis = [int][Math]::Min(50, [Math]::Floor(($abortDeadline - (& $Now)) * 1000))
                    if ($millis -le 0) { break }
                    $null = & $Wait ([Threading.Tasks.Task[]]@()) $millis $Process
                }
            } catch { $record.confirmation_error = Get-MetadataShortError $_.Exception }
        }
    } finally {
        if ($record.started) {
            try {
                if ($Process.HasExited) {
                    $record.exit_observed = $true
                    $record.exit_code = $Process.ExitCode
                }
            } catch { $record.exit_error = Get-MetadataShortError $_.Exception }
        }
        foreach ($stream in $streams) {
            if ($null -ne $stream.Task -and $stream.Task.IsCompleted -and $stream.Task.IsFaulted) { $null = $stream.Task.Exception }
            if ($null -ne $stream.Stream) {
                try { $stream.Stream.Dispose() }
                catch { $record.cleanup[$stream.Name] = Get-MetadataShortError $_.Exception }
            }
            $record.streams[$stream.Name] = [ordered]@{
                limit_bytes = $stream.Limit; observed_bytes = $stream.Observed
                retained_bytes = [Math]::Min($stream.Limit, $stream.Observed)
                eof = $stream.Eof; overflow = $stream.Overflow; error = $stream.Error
            }
        }
        try { $Process.Dispose() } catch { $record.cleanup.process = Get-MetadataShortError $_.Exception }
        try { $cancel.Dispose() } catch { $record.cleanup.token = Get-MetadataShortError $_.Exception }
        $retainedHandle = $null
        $record.elapsed_seconds = (& $Now) - $started
        $budget = if ($record.aborted) { 10 } else { 5 }
        if ($record.elapsed_seconds -ge $budget) {
            $record.deadline_exceeded = $true
            $record.timed_out = $true
            if ($null -eq $record.failure) { $record.failure = 'Disposal exceeded the original deadline.' }
        }
    }
    $cleanupFailed = @($record.cleanup.Values | Where-Object { $null -ne $_ }).Count -ne 0
    $record.ok = $record.normal_completion -and -not $record.aborted -and -not $record.timed_out -and
        $record.exit_observed -and $record.exit_code -eq 0 -and $null -eq $record.failure -and
        $null -eq $record.exit_error -and -not $cleanupFailed
    $payloads = @{}
    foreach ($stream in $streams) {
        $bytes = [byte[]]::new([Math]::Min($stream.Limit, $stream.Observed))
        [Array]::Copy($stream.Buffer, 0, $bytes, 0, $bytes.Length)
        $payloads[$stream.Name] = $bytes
    }
    return [pscustomobject]@{ Record = $record; Stdout = $payloads['stdout']; Stderr = $payloads['stderr'] }
}

function Assert-MetadataNoReparse([string]$Path) {
    $current = [IO.Path]::GetFullPath($Path)
    for ($level = 0; $level -lt 64; $level++) {
        try {
            $attributes = [IO.File]::GetAttributes($current)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse path is prohibited.' }
        } catch [IO.FileNotFoundException] {
        } catch [IO.DirectoryNotFoundException] {
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrEmpty($parent)) { return }
        $current = $parent
    }
    throw 'Fixed path ancestry limit.'
}

function Read-MetadataBytes([string]$Path, [int]$Limit) {
    Assert-MetadataNoReparse $Path
    $file = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $buffer = [byte[]]::new($Limit + 1)
        $used = 0
        while ($used -le $Limit) {
            $count = $file.Read($buffer, $used, [Math]::Min(4096, $buffer.Length - $used))
            if ($count -eq 0) { break }
            $used += $count
            if ($used -gt $Limit) { throw 'Pinned file byte limit.' }
        }
        $answer = [byte[]]::new($used)
        [Array]::Copy($buffer, 0, $answer, 0, $used)
        Assert-MetadataNoReparse $Path
        return ,$answer
    } finally { $file.Dispose() }
}

function Get-MetadataHash([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-MetadataSnapshot($Packet) {
    $policy = Get-MetadataPolicy
    $snapshot = [ordered]@{ expected_head = $Packet['expectedHead']; sources = [ordered]@{}; historical = [ordered]@{} }
    foreach ($name in $policy.SourcePaths) {
        $hash = Get-MetadataHash (Read-MetadataBytes (Join-Path $policy.Root $name.Replace('/', '\')) 1048576)
        if ($hash -cne $Packet['sourceSha256'][$name]) { throw ('Source pin mismatch: ' + $name) }
        $snapshot.sources[$name] = $hash
    }
    foreach ($pin in $policy.Historical.GetEnumerator()) {
        $hash = Get-MetadataHash (Read-MetadataBytes (Join-Path $policy.Root $pin.Key.Replace('/', '\')) 262144)
        if ($hash -cne $pin.Value) { throw 'Historical receipt changed.' }
        $snapshot.historical[$pin.Key] = $hash
    }
    return $snapshot
}

function Write-MetadataBytes([string]$Path, [byte[]]$Bytes) {
    Assert-MetadataNoReparse $Path
    $file = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $file.Write($Bytes, 0, $Bytes.Length) } finally { $file.Dispose() }
}

function Write-MetadataJson([string]$Path, $Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 16
    Write-MetadataBytes $Path ([Text.UTF8Encoding]::new($false, $true).GetBytes($json + "`n"))
}

function Invoke-MetadataOperation([string]$Path) {
    $policy = Get-MetadataPolicy
    if (-not $IsWindows -or [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) -cne $policy.Root) { throw 'Fixed Windows worktree required.' }
    $packetName = Join-Path $policy.Root ($policy.Stem + '.packet.json')
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::GetFullPath($Path) -cne $packetName) { throw 'Exact packet path required.' }
    $packetBytes = Read-MetadataBytes $packetName 16384
    $packet = ConvertFrom-MetadataPacket ([Text.UTF8Encoding]::new($false, $true).GetString($packetBytes))
    $paths = [ordered]@{}
    foreach ($suffix in @('before.json', 'stdout.json', 'stderr.txt', 'outer.json', 'after.json', 'result.json', 'hashes.json')) {
        $output = Join-Path $policy.Root ($policy.Stem + '.' + $suffix)
        Assert-MetadataNoReparse $output
        try { $null = [IO.File]::GetAttributes($output); throw 'Fresh output collision.' }
        catch [IO.FileNotFoundException] { }
        catch [IO.DirectoryNotFoundException] { throw 'Evidence directory must already exist.' }
        $paths[$suffix] = $output
    }
    $before = Get-MetadataSnapshot $packet
    Write-MetadataJson $paths['before.json'] $before
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = New-MetadataStartInfo
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $now = { $clock.Elapsed.TotalSeconds }.GetNewClosure()
    $wait = {
        param($pending, $millis, $owned)
        if ($pending.Length -gt 0) { $null = [Threading.Tasks.Task]::WaitAny($pending, $millis) }
        else { $null = $owned.WaitForExit($millis) }
    }
    $capture = Invoke-MetadataCapture -Process $process -Now $now -Wait $wait
    $clock.Stop()
    Write-MetadataBytes $paths['stdout.json'] $capture.Stdout
    Write-MetadataBytes $paths['stderr.txt'] $capture.Stderr
    Write-MetadataJson $paths['outer.json'] $capture.Record
    $result = [ordered]@{
        expected_head = $packet['expectedHead']; packet_sha256 = Get-MetadataHash $packetBytes
        status = 'failed'; metadata_attempted = $capture.Record.started
        metadata_observed = $false; failure = $capture.Record.failure
        native_attempted = $false; primitive_accepted = $false
        application_accepted = $false; isolation_accepted = $false
        descendant_cleanup_proven = $false
    }
    try {
        $after = Get-MetadataSnapshot $packet
        Write-MetadataJson $paths['after.json'] $after
        if (-not $capture.Record.ok) { throw 'Outer capture did not pass.' }
        if ($capture.Stderr.Length -ne 0) { throw 'Metadata produced stderr.' }
        $observed = [Text.UTF8Encoding]::new($false, $true).GetString($capture.Stdout) | ConvertFrom-Json -AsHashtable
        if ($observed['schema'] -cne 'issue2160-current-metadata-v1' -or $observed['status'] -cne 'observed' -or
            $observed['cleanup_failures'].Count -ne 0 -or $observed['elapsed_seconds'] -ge 3) { throw 'Metadata receipt did not pass.' }
        foreach ($flag in @('native_attempted', 'primitive_accepted', 'application_accepted', 'isolation_accepted')) {
            if ($observed[$flag] -isnot [bool] -or $observed[$flag]) { throw 'Invalid metadata-only flag.' }
        }
        if ($observed['checks'].Count -ne 7 -or @($observed['checks'].Values | Where-Object { $_ -isnot [bool] -or -not $_ }).Count -ne 0) { throw 'Metadata identity checks did not pass.' }
        $result.status = 'observed; independent diagnosis required'
        $result.metadata_observed = $true
    } catch {
        $result.failure = Get-MetadataShortError $_.Exception
    }
    Write-MetadataJson $paths['result.json'] $result
    $hashes = [ordered]@{}
    foreach ($name in @('before.json', 'stdout.json', 'stderr.txt', 'outer.json', 'after.json', 'result.json')) {
        try { $hashes[$name] = Get-MetadataHash (Read-MetadataBytes $paths[$name] 65536) }
        catch [IO.FileNotFoundException] { $hashes[$name] = 'not-created' }
    }
    Write-MetadataJson $paths['hashes.json'] $hashes
    Write-Output (ConvertTo-Json -InputObject $result -Compress)
    if (-not $result.metadata_observed) { throw 'Bounded metadata observation failed; no retry authorized.' }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    Invoke-MetadataOperation $PacketPath
}
