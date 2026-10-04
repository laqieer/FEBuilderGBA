# SPDX-License-Identifier: GPL-3.0-or-later
#requires -Version 7.0
[CmdletBinding()]
param([string]$PacketPath)

function Get-MetadataPolicy([switch]$IsolationInterface) {
    $policy = [pscustomobject]@{
        Root = 'C:\Projects\laqieer\FEBuilderGBA\TestResults\worktrees\issue-2160-linux-x11-20260913T075332Z'
        Stem = 'issue-2160-current-metadata-20260914T090444Z'
        Schema = 'issue2160-current-metadata-v1'
        IsolationInterface = [bool]$IsolationInterface
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
    if ($IsolationInterface) {
        $policy.Stem = 'issue-2160-isolation-interface-20260914T130000Z'
        $policy.Schema = 'issue2160-isolation-interface-v1'
        $prefix = 'issue-2160-current-metadata-20260914T090444Z.'
        $policy.Historical[$prefix + 'packet.json'] = '4a5c8b9855cbcc862977e8fc627e27dc09ca2672b4e809fc426d7c43b32972fb'
        $policy.Historical[$prefix + 'before.json'] = 'f0fb71cf3e5c9caf478c41cc164a0ff0266f538ea8f7a38993a2904615334678'
        $policy.Historical[$prefix + 'stdout.json'] = 'fa09dbfec71b54aab0cfb5ba26b0e72e50096c447aefbea3c17808ec9baa4560'
        $policy.Historical[$prefix + 'stderr.txt'] = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
        $policy.Historical[$prefix + 'outer.json'] = 'f2e1cb38e8bd3377744a3239602520da38ecca5e27aed647154335ffa82c86ea'
        $policy.Historical[$prefix + 'after.json'] = 'f0fb71cf3e5c9caf478c41cc164a0ff0266f538ea8f7a38993a2904615334678'
        $policy.Historical[$prefix + 'result.json'] = 'b5b4945c8a2bc58a9aebbd0cd2f264a218359fdacb764e3c3902e60044dde02d'
        $policy.Historical[$prefix + 'hashes.json'] = 'b3c93e877ae3420339fb2837faba19a876e99b3d0f587564f08e669b5510ecca'
    }
    return $policy
}

function Get-MetadataPacketPolicy([string]$Path) {
    foreach ($interface in @($false, $true)) {
        $policy = Get-MetadataPolicy -IsolationInterface:$interface
        if ($Path -ceq ($policy.Root + '\' + $policy.Stem + '.packet.json')) { return $policy }
    }
    throw 'One of the two exact absolute packet paths is required.'
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
        $policy = Get-MetadataPolicy -IsolationInterface:($packet['schema'] -ceq 'issue2160-isolation-interface-v1')
        if ($packet['schema'] -cne $policy.Schema -or
            $packet['expectedHead'] -cnotmatch '\A[0-9a-f]{40}\z' -or
            $packet['receiptStem'] -cne $policy.Stem) { throw 'Packet binding mismatch.' }
        if ($packet['sourceSha256'].Count -ne $policy.SourcePaths.Count) { throw 'Wrong source pin set.' }
        foreach ($name in $policy.SourcePaths) {
            if (-not $packet['sourceSha256'].ContainsKey($name)) { throw 'Missing exact source pin.' }
        }
        if ($packet['historicalSha256'].Count -ne $policy.Historical.Count) { throw 'Wrong historical pin set.' }
        foreach ($pin in $policy.Historical.GetEnumerator()) {
            if (-not $packet['historicalSha256'].ContainsKey($pin.Key) -or
                $packet['historicalSha256'][$pin.Key] -cne $pin.Value) { throw 'Historical receipt mapping mismatch.' }
        }
        return ,$packet
    } finally {
        $document.Dispose()
    }
}

function New-MetadataStartInfo([switch]$IsolationInterface) {
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
        'scripts/linux_x11_metadata.py'
    )) { $info.ArgumentList.Add($argument) }
    $info.ArgumentList.Add($(if ($IsolationInterface) { '--observe-isolation-interface' } else { '--observe' }))
    return $info
}

function Get-MetadataShortError([Exception]$ErrorObject) {
    $text = $ErrorObject.GetType().Name + ': ' + $ErrorObject.Message
    return $text.Substring(0, [Math]::Min(300, $text.Length))
}

function Receive-MetadataRead($Stream) {
    if ($null -eq $Stream.Task -or -not $Stream.Task.IsCompleted) { return $null }
    $task = $Stream.Task
    $Stream.Task = $null
    try {
        $count = $task.GetAwaiter().GetResult()
        if ($count -lt 0 -or $count -gt $Stream.Requested) { throw 'Invalid asynchronous read count.' }
        if ($count -eq 0) {
            $Stream.Eof = $true
        } else {
            $Stream.Observed += $count
            if ($Stream.Observed -gt $Stream.Limit) {
                $Stream.Overflow = $true
                throw ('Outer ' + $Stream.Name + ' raw-byte cap.')
            }
        }
        return $null
    } catch {
        if ($null -eq $Stream.Error) { $Stream.Error = Get-MetadataShortError $_.Exception }
        return $Stream.Error
    }
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
        [pscustomobject]@{ Name = 'stdout'; Limit = 16384; Buffer = [byte[]]::new(16385); Observed = 0; Requested = 0; Eof = $false; Overflow = $false; Error = $null; Stream = $null; Task = $null }
        [pscustomobject]@{ Name = 'stderr'; Limit = 4096; Buffer = [byte[]]::new(4097); Observed = 0; Requested = 0; Eof = $false; Overflow = $false; Error = $null; Stream = $null; Task = $null }
    )
    $retainedHandle = $null
    $started = & $Now
    try {
        $record.started = $Process.Start()
        if (-not $record.started) { throw 'Exact outer process did not start.' }
        $streams[0].Stream = $Process.StandardOutput.BaseStream
        $streams[1].Stream = $Process.StandardError.BaseStream
        $firstFailure = $null
        foreach ($stream in $streams) {
            if ((& $Now) - $started -ge 5) {
                $record.timed_out = $true
                if ($null -eq $firstFailure) { $firstFailure = 'Outer stream setup deadline.' }
                break
            }
            try {
                $stream.Requested = 4096
                $stream.Task = $stream.Stream.ReadAsync($stream.Buffer, 0, $stream.Requested, $cancel.Token)
            } catch {
                $stream.Error = Get-MetadataShortError $_.Exception
                if ($null -eq $firstFailure) { $firstFailure = $stream.Error }
            }
        }
        if ($null -ne $firstFailure) { throw $firstFailure }
        $retainedHandle = $Process.SafeHandle
        $record.pid = $Process.Id
        $record.process_start_utc = $Process.StartTime.ToUniversalTime().ToString('o')
        while ($true) {
            $firstFailure = $null
            foreach ($stream in $streams) {
                if ((& $Now) - $started -ge 5) {
                    $record.timed_out = $true
                    if ($null -eq $firstFailure) { $firstFailure = 'Outer launch/capture deadline.' }
                    break
                }
                $failure = Receive-MetadataRead $stream
                if ($null -ne $failure -and $null -eq $firstFailure) { $firstFailure = $failure }
            }
            if ($null -ne $firstFailure) { throw $firstFailure }
            foreach ($stream in $streams) {
                if ((& $Now) - $started -ge 5) {
                    $record.timed_out = $true
                    if ($null -eq $firstFailure) { $firstFailure = 'Outer launch/capture deadline.' }
                    break
                }
                if ($null -eq $stream.Task -and -not $stream.Eof) {
                    try {
                        $stream.Requested = [Math]::Min(4096, $stream.Limit + 1 - $stream.Observed)
                        $stream.Task = $stream.Stream.ReadAsync($stream.Buffer, $stream.Observed, $stream.Requested, $cancel.Token)
                    } catch {
                        $stream.Error = Get-MetadataShortError $_.Exception
                        if ($null -eq $firstFailure) { $firstFailure = $stream.Error }
                    }
                }
            }
            if ($null -ne $firstFailure) { throw $firstFailure }
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
            $failure = Receive-MetadataRead $stream
            if ($null -ne $failure -and $null -eq $record.failure) { $record.failure = $failure }
            if ($null -ne $stream.Stream) {
                try { $stream.Stream.Dispose() }
                catch { $record.cleanup[$stream.Name] = Get-MetadataShortError $_.Exception }
            }
            $failure = Receive-MetadataRead $stream
            if ($null -ne $failure -and $null -eq $record.failure) { $record.failure = $failure }
            $record.streams[$stream.Name] = [ordered]@{
                limit_bytes = $stream.Limit; observed_bytes = $stream.Observed
                retained_bytes = [Math]::Min($stream.Limit, $stream.Observed)
                eof = $stream.Eof; overflow = $stream.Overflow; error = $stream.Error
                pending = $null -ne $stream.Task
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
    $policy = Get-MetadataPolicy -IsolationInterface:($Packet['schema'] -ceq 'issue2160-isolation-interface-v1')
    if ($policy.IsolationInterface) { Assert-MetadataHead $Packet['expectedHead'] }
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
    if ($policy.IsolationInterface) { Assert-MetadataHead $Packet['expectedHead'] }
    return $snapshot
}

function Assert-MetadataHead([string]$ExpectedHead) {
    $root = (Get-MetadataPolicy).Root
    $gitRoot = 'C:\Projects\laqieer\FEBuilderGBA\.git'
    $worktreeName = 'issue-2160-linux-x11-20260913T075332Z'
    $gitDirectory = $gitRoot + '\worktrees\' + $worktreeName
    $gitFile = [Text.Encoding]::UTF8.GetString((Read-MetadataBytes ($root + '\.git') 256)).TrimEnd("`r", "`n")
    if ($gitFile -cne ('gitdir: ' + $gitDirectory.Replace('\', '/'))) { throw 'Fixed worktree Git binding changed.' }
    $head = [Text.Encoding]::UTF8.GetString((Read-MetadataBytes ($gitDirectory + '\HEAD') 256)).TrimEnd("`r", "`n")
    if ($head -cne 'ref: refs/heads/fix/issue-2160-linux-x11-attribution') { throw 'Fixed branch binding changed.' }
    $actual = [Text.Encoding]::UTF8.GetString((Read-MetadataBytes ($gitRoot + '\refs\heads\fix\issue-2160-linux-x11-attribution') 256)).TrimEnd("`r", "`n")
    if ($ExpectedHead -cnotmatch '\A[0-9a-f]{40}\z' -or $actual -cne $ExpectedHead) { throw 'Current HEAD pin mismatch.' }
}

function Assert-MetadataReceipt($Observed, $Policy) {
    if ($Observed['schema'] -cne $Policy.Schema -or $Observed['status'] -cne 'observed' -or
        $Observed['cleanup_failures'].Count -ne 0 -or $Observed['elapsed_seconds'] -ge 3) { throw 'Metadata receipt did not pass.' }
    foreach ($flag in @('native_attempted', 'primitive_accepted', 'application_accepted', 'isolation_accepted')) {
        if ($Observed[$flag] -isnot [bool] -or $Observed[$flag]) { throw 'Invalid observation-only flag.' }
    }
    if ($Observed['checks'].Count -ne 7 -or @($Observed['checks'].Values | Where-Object { $_ -isnot [bool] -or -not $_ }).Count -ne 0) { throw 'Metadata identity checks did not pass.' }
    if (-not $Policy.IsolationInterface) { return }
    foreach ($flag in @('namespace_attempted', 'descendant_cleanup_proven')) {
        if ($Observed[$flag] -isnot [bool] -or $Observed[$flag]) { throw 'Invalid interface-only flag.' }
    }
    foreach ($flag in @('binary_documentation_attempted', 'binary_documentation_observed')) {
        if ($Observed[$flag] -isnot [bool] -or -not $Observed[$flag]) { throw 'Binary documentation was not observed.' }
    }
    if ($Observed['descriptor_bound_including_setup'] -gt 16 -or $Observed['commands'].Count -ne 2) { throw 'Interface resource bound mismatch.' }
    $binary = $Observed['binary']
    if ($binary['path'] -cne '/usr/bin/unshare' -or $binary['kind'] -cne 'regular' -or
        $binary['mode'] -cne '0755' -or $binary['uid'] -ne 0 -or $binary['gid'] -ne 0 -or
        $binary['sha256'] -cnotmatch '\A[0-9a-f]{64}\z' -or $binary['bytes'] -gt 1048576 -or
        $binary['file_capabilities_absent'] -isnot [bool] -or -not $binary['file_capabilities_absent']) { throw 'Binary identity receipt did not pass.' }
    $index = 0
    foreach ($option in @('--version', '--help')) {
        $command = $Observed['commands'][$index]
        $stdoutLimit = if ($index -eq 0) { 512 } else { 8192 }
        if ($command['option'] -cne $option -or $command['attempted'] -isnot [bool] -or -not $command['attempted'] -or
            $command['normal_completion'] -isnot [bool] -or -not $command['normal_completion'] -or
            $command['termination_confirmed'] -isnot [bool] -or -not $command['termination_confirmed'] -or
            $command['kill_attempted'] -isnot [bool] -or $command['kill_attempted'] -or
            $command['exit_code'] -ne 0 -or $null -ne $command['failure'] -or
            $command['elapsed_seconds'] -ge 1) { throw 'Incomplete binary documentation command.' }
        foreach ($name in @('stdout', 'stderr')) {
            $stream = $command['streams'][$name]
            $limit = if ($name -ceq 'stdout') { $stdoutLimit } else { 512 }
            $raw = [Convert]::FromBase64String($stream['base64'])
            if ($stream['limit'] -ne $limit -or $raw.Length -gt $limit -or
                $raw.Length -ne $stream['observed_bytes'] -or
                $stream['eof'] -isnot [bool] -or -not $stream['eof'] -or
                $stream['overflow'] -isnot [bool] -or $stream['overflow'] -or
                $null -ne $stream['error'] -or ($name -ceq 'stderr' -and $raw.Length -ne 0)) { throw 'Incomplete binary documentation stream.' }
        }
        $index++
    }
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
    $policy = Get-MetadataPacketPolicy $Path
    if (-not $IsWindows -or [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) -cne $policy.Root) { throw 'Fixed Windows worktree required.' }
    $packetName = Join-Path $policy.Root ($policy.Stem + '.packet.json')
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::GetFullPath($Path) -cne $packetName) { throw 'Exact packet path required.' }
    $packetBytes = Read-MetadataBytes $packetName 16384
    $packet = ConvertFrom-MetadataPacket ([Text.UTF8Encoding]::new($false, $true).GetString($packetBytes))
    if ($packet['schema'] -cne $policy.Schema) { throw 'Packet path/profile mismatch.' }
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
    $process.StartInfo = New-MetadataStartInfo -IsolationInterface:$policy.IsolationInterface
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
    if ($policy.IsolationInterface) {
        $result['binary_documentation_attempted'] = if ($capture.Record.started) { $null } else { $false }
        $result['binary_documentation_observed'] = $false
        $result['namespace_attempted'] = $false
    }
    $observationPassed = $false
    try {
        $after = Get-MetadataSnapshot $packet
        Write-MetadataJson $paths['after.json'] $after
        if ($policy.IsolationInterface -and (Get-MetadataHash (Read-MetadataBytes $packetName 16384)) -cne (Get-MetadataHash $packetBytes)) { throw 'Immutable packet changed.' }
        $observed = [Text.UTF8Encoding]::new($false, $true).GetString($capture.Stdout) | ConvertFrom-Json -AsHashtable
        if ($policy.IsolationInterface -and $observed['schema'] -ceq $policy.Schema -and
            $observed['binary_documentation_attempted'] -is [bool]) {
            $result['binary_documentation_attempted'] = $observed['binary_documentation_attempted']
        }
        if (-not $capture.Record.ok) { throw 'Outer capture did not pass.' }
        if ($capture.Stderr.Length -ne 0) { throw 'Metadata produced stderr.' }
        Assert-MetadataReceipt $observed $policy
        $result.status = 'observed; independent diagnosis required'
        if ($policy.IsolationInterface) { $result['binary_documentation_observed'] = $true }
        else { $result.metadata_observed = $true }
        $observationPassed = $true
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
    if (-not $observationPassed) { throw 'Bounded observation failed; no retry authorized.' }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version Latest
    Invoke-MetadataOperation $PacketPath
}
