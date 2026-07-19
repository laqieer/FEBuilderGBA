#Requires -Version 5.1
<#
.SYNOPSIS
Fail-open PowerShell wrapper for scripts/copilot_context_guard.py (issue #1995).

.DESCRIPTION
Mirrors scripts/copilot-context-guard.sh. Per the official GitHub Copilot CLI
hooks reference, a `preToolUse` command hook is fail-CLOSED on any non-zero,
non-timeout exit: exit 2 denies (with stdout JSON merged in), and any *other*
non-zero exit also denies the tool call outright, even if stdout reports
allow. This wrapper guarantees that infrastructure failures (missing/broken
Python launcher, spawn failure, an uncaught guard crash) can never
accidentally deny a `view` call: only a real exit-2 decision from the guard
propagates as a deny. Everything else becomes a fail-open "{}" with wrapper
exit 0.
#>

$hookDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$guard = Join-Path $hookDir 'copilot_context_guard.py'

function Resolve-WorkingPython {
    foreach ($candidate in @('python3', 'python', 'py')) {
        $cmd = Get-Command $candidate -ErrorAction SilentlyContinue
        if (-not $cmd) { continue }
        # Guard against the Windows Store "python.exe" stub, which is
        # present on PATH even when no real interpreter is installed and
        # exits non-zero (or opens the Store) instead of running code.
        try {
            & $cmd.Source '--version' *> $null
            if ($LASTEXITCODE -eq 0) {
                return $cmd.Source
            }
        } catch {
            continue
        }
    }
    return $null
}

$pythonBin = Resolve-WorkingPython
if (-not $pythonBin) {
    Write-Output '{}'
    exit 0
}

$stdinText = ''
try {
    $stdinText = [Console]::In.ReadToEnd()
} catch {
    $stdinText = ''
}

$output = $null
try {
    $output = $stdinText | & $pythonBin $guard 2>$null
} catch {
    Write-Output '{}'
    exit 0
}
$status = $LASTEXITCODE

$joined = if ($null -eq $output) { '' } else { ($output -join "`n") }

if ($status -eq 2) {
    if ([string]::IsNullOrEmpty($joined)) { Write-Output '{}' } else { Write-Output $joined }
    exit 2
}

# Any other non-zero exit must NOT propagate as a wrapper failure, or the
# fail-closed preToolUse contract would deny unrelated view() calls.
if ($status -ne 0) {
    Write-Output '{}'
    exit 0
}

if ([string]::IsNullOrEmpty($joined)) {
    Write-Output '{}'
} else {
    Write-Output $joined
}
exit 0
