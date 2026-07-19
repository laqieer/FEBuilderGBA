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
Python launcher, spawn failure, an uncaught guard crash -- INCLUDING
CPython's own exit code 2 for a missing/unreadable script file) can never
accidentally deny a `view` call: only a *validated* exit-2 decision from the
guard -- stdout parses as a JSON object with permissionDecision -eq 'deny'
and a non-empty permissionDecisionReason -- propagates as a deny. Every
other outcome (any non-zero exit, or a normal exit 0) becomes the fixed
fail-open '{}' literal with wrapper exit 0 -- child stdout on a normal exit
0 is never forwarded verbatim, since the guard's only legitimate exit-0
decision is abstention.
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
    # CPython itself exits 2 for infrastructure failures unrelated to a
    # real deny decision (e.g. a missing/unreadable guard script). Only
    # propagate exit 2 when stdout genuinely parses as a JSON object with
    # permissionDecision -eq 'deny' and a non-empty permissionDecisionReason;
    # otherwise this is an infrastructure failure, not a deny, and must
    # fail open like every other non-exit-2 failure mode.
    $isValidDeny = $false
    try {
        $decision = $joined | ConvertFrom-Json -ErrorAction Stop
        if ($null -ne $decision -and
            ($decision.PSObject.Properties.Name -contains 'permissionDecision') -and
            ($decision.permissionDecision -eq 'deny') -and
            ($decision.PSObject.Properties.Name -contains 'permissionDecisionReason') -and
            ($decision.permissionDecisionReason -is [string]) -and
            (-not [string]::IsNullOrEmpty($decision.permissionDecisionReason))) {
            $isValidDeny = $true
        }
    } catch {
        $isValidDeny = $false
    }

    if ($isValidDeny) {
        Write-Output $joined
        exit 2
    }
    Write-Output '{}'
    exit 0
}

# Any other non-zero exit must NOT propagate as a wrapper failure, or the
# fail-closed preToolUse contract would deny unrelated view() calls.
if ($status -ne 0) {
    Write-Output '{}'
    exit 0
}

# The guard's only legitimate exit-0 decision is abstention: every code
# path in copilot_context_guard.py that is not a definitive, validated
# exit-2 deny prints exactly '{}' and exits 0. Never forward the child's
# raw stdout here -- doing so would let a corrupted/partial/arbitrary
# stdout payload on a normal exit (e.g. truncated output, a stray print,
# a future regression) masquerade as a decision object. Always emit the
# fixed abstention literal instead of trusting/echoing child stdout.
Write-Output '{}'
exit 0
