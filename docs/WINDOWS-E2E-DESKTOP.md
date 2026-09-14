# Windows E2E desktop admission

Windows GUI automation requires an affirmatively ready desktop. A Windows host,
an interactive logon in some other session, or a successful `OpenInputDesktop`
call alone is not sufficient. `OpenInputDesktop` can succeed while disconnected.

## Decision and failures

`DesktopReadiness` separates native collection from a pure decision:

| Observation | Result |
| --- | --- |
| Own process session is 0 | Blocked / `SessionZero` |
| Own WTS connection state is known and not `WTSActive` | Blocked / `SessionInactive` |
| Own thread desktop is not receiving input (`UOI_IO`) | Blocked / `ThreadDesktopNotInput` |
| Query fails, buffer is invalid, or a required observation is indeterminate | Unknown |
| Nonzero own session, `WTSActive`, and input thread desktop | Ready / `ActiveInputDesktop` |

An affirmative blocking observation takes precedence in the pure decision.
Collection stops at a blocking or unknown prerequisite. The probe queries only
the current process's session and the current thread's desktop; it does not
enumerate, connect, unlock, switch or manipulate sessions/desktops. WTS buffers
are validated and freed, including non-null buffers returned on failure. The
borrowed `GetThreadDesktop` handle is never closed.

Blocked and Unknown both throw `DesktopUnavailableException` before starting a
GUI process or capturing a window. They fail the test rather than silently
skipping it. There is no ignore-readiness flag. Check the bounded reason code and
run GUI validation only in an independently suitable, already-active environment.
Do not lock, disconnect or switch a user's session to create a test case.

## Launch-path audit

All E2E `AppRunner.Launch` calls require admission. Every `AvaloniaAppRunner.Run`
call also requires admission: all ten existing sites create a desktop lifetime,
including data verification and image export.

Captured-output WinForms GUI paths use `AppRunner.RunGui`:

- `CliHelpTests`: no arguments.
- `WinFormsCliOutputLogNoRomTests`: no arguments and bogus command.
- `WinFormsScreenshotAllCliTests`: both screenshot-all commands.
- `WinFormsCliOutputLogRomTests`: all six legacy ROM commands.
- `EditorImageComparisonTests`: both WinForms image exports.

The legacy ROM commands are conservatively GUI-capable because `Program.LoadROM`
can show an unknown-ROM dialog. Production startup is unchanged. The legacy
best-effort no-args smoke test's catch explicitly excludes admission failures;
it is not required GUI-interaction proof.

`AppRunner.Run` remains explicitly CLI-only; it does not inspect the desktop.
The 25 unchanged test files with verified CLI-only launch sites remain unchanged.
This includes empty arguments to `FEBuilderGBA.CLI`, the CLI-only `RomCliTests`
retry wrapper, and WinForms' early `--version` exit (including `CliTests`).
New GUI-capable callers must choose `RunGui`, not infer safety from redirected
output or a command-line flag.

`e2e-run.yml` retains direct CLI version diagnostics but removes duplicate
unguarded GUI startup and screenshot-all launches. Its existing E2E step now
owns those paths and propagates failures. `check.yml` inherits the guards without
weakening failure propagation, preserves the Debug test loop and adds the hosted
Release/x86 unit coverage described below.

## Owned-window capture

Both public `ScreenshotHelper` capture methods require the retained live
`Process` returned by `AppRunner.Launch`, plus its target HWND. Keep that Process
undisposed for the entire capture. The helper verifies liveness, the original
retained process handle's identity, and `GetWindowThreadProcessId` ownership
before inspecting bounds or allocating a capture surface. Foreign/stale HWNDs,
disposed/exited processes and failed identity queries produce explicit errors.

Capture uses only target-window `PrintWindow` (full-content, then flag 0).
It never requests foreground focus, acquires a screen DC or falls back to
whole-screen pixels. A false native return is failure even if pixels appear
nonempty. Empty captures are not saved as success-shaped PNGs. Acquired HDCs,
Graphics objects and bitmaps are released on success and failure. Required
capture failures throw `WindowCaptureException` and fail their caller.

Readiness and ownership are point-in-time preconditions, not atomic guarantees.
`PrintWindow` is synchronous and may still block. Sampled nonuniform pixels are
not proof of correct rendering or interaction. Application-internal
`DrawToBitmap`/`RenderTargetBitmap` paths receive prelaunch admission only, not
per-capture checks. Optional diagnostic output is not GUI-interaction proof.

The startup termination test reports direct optional capture refusals and
continues other diagnostic windows. Readiness rejection, wrapped unexpected
capture errors, and enumeration/reporting failures still fail after cleanup.
Its fallback and fixture disposal share one retained-process cleanup attempt:
no tree kill or PID reacquisition, one finite wait, and no retry after failure.
`App_StartupProcessExitsAfterCloseOrOwnedCleanup` checks termination after a close
request or owned cleanup; a pass is not proof of normal application shutdown.
Synchronous capture can still block; this helper is not a hard-timeout supervisor.

## Validation boundary

Build and run only injected tests before independent source/security review:

```powershell
dotnet build FEBuilderGBA.E2ETests\FEBuilderGBA.E2ETests.csproj -c Release -p:Platform=x86 --no-restore
dotnet test FEBuilderGBA.E2ETests\FEBuilderGBA.E2ETests.csproj --no-build -c Release -p:Platform=x86 --filter "FullyQualifiedName~StartupCloseDiagnosticsTests|FullyQualifiedName~DesktopReadinessTests|FullyQualifiedName~DesktopProbeCommandTests"
```

Restore existing dependencies only if the build explicitly reports them missing.
The focused suite injects observations, process dispatch and capture surfaces;
it invokes no actual readiness, window-capture or GUI APIs. It checks rejection,
CLI preservation, resource ownership, native-return handling, failure propagation
and removal of direct workflow GUI diagnostics. Startup cleanup tests inject
process liveness, termination and waits, including failed and repeated cleanup;
they neither launch nor inspect a real process.

### Hosted full Release/x86 WinForms unit coverage

Required Windows `Check` preserves the four-project Debug/x86 test loop and its
coverage collection. After that loop succeeds, it builds and runs the complete
`FEBuilderGBA.Tests` project in Release/x86 on the hosted runner:

```powershell
dotnet build FEBuilderGBA.Tests\FEBuilderGBA.Tests.csproj -c Release -p:Platform=x86 --no-restore -warnaserror
dotnet test FEBuilderGBA.Tests\FEBuilderGBA.Tests.csproj -c Release -p:Platform=x86 --no-build --verbosity normal --logger "trx;LogFileName=unit-release-x86.trx" --blame-hang --blame-hang-timeout 20m
```

The preceding Debug build, solution restore, submodule initialization and x86
runtime preparation remain prerequisites. The existing test-project dependency
copy target consumes Debug config; the Release steps do not replace that build
or silently restore missing prerequisites.

The build and test step caps are 15 and 30 minutes. The unchanged 120-minute job
deadline overrides both; these are upper bounds, not reserved execution time.
The existing 20-minute hang detector remains enabled. Native command failures
propagate, and a failed prerequisite may skip subsequent steps. Failed, skipped,
timed-out, missing or empty Release results do not satisfy the full-suite
requirement. Acceptance requires exact-head hosted logs for both commands and
nonzero unfiltered `unit-release-x86.trx` results with pass/fail/skip counts; the
existing `**/TestResults/*.trx` reporter also picks up this distinct file.

The unfiltered unit suite contains real WinForms `form.Show()` calls. **Do not
run that test command locally under a no-native/no-GUI workstation grant.**
Source review, builds and the injected tests above do not authorize it or
substitute for its full hosted results. Conversely, hosted unit results are not
own-desktop native readiness/transport verification, actual application GUI or
screenshot proof, or evidence of normal application close. Those boundaries and
any required fresh bounded authorization remain separate.

## Opt-in probe command

The standalone `tools\DesktopReadinessProbe` console project references the built
Release/x86 E2E DLL, not its project or native helper source. Build the E2E project
first, then the command:

```powershell
dotnet build tools\DesktopReadinessProbe\DesktopReadinessProbe.csproj -c Release -p:Platform=x86 --no-restore
```

Restore only after an explicit missing-assets/dependency failure. The tool adds
no packages and does not build E2E, discover tests, launch an application or
capture a window. `DesktopProbeCommandTests` links only the pure dispatcher and
injects its callback/writer; it checks rejection before invocation, exact call
counts, result/exit mapping, bounded output and exception-detail suppression.

The resulting `tools\DesktopReadinessProbe\bin\x86\Release\net10.0-windows\DesktopReadinessProbe.dll`
requires an x86 .NET 10 host with `Microsoft.NETCore.App` and
`Microsoft.WindowsDesktop.App`. It is not loadable into an x64 PowerShell process.
There is no apphost executable. The following is only the child-command template
for a separately approved, externally supervised native execution:

```powershell
& $ApprovedX86Dotnet $ReviewedHarnessDll --probe-own-desktop
```

Do not run that command without independent source review and a distinct bounded
native grant. Pin the exact host, runtime, harness/E2E binaries and runtime
metadata in that grant/session evidence, not in committed machine-specific
configuration. Its external supervisor must retain the exact child process,
bound output and elapsed time, classify startup/load failures and timeouts, and
terminate/wait only that retained child if required. The probe itself has no
intrinsic timeout.

Only the exact sole argument `--probe-own-desktop` authorizes one call to the
existing public `DesktopReadiness.Probe()`. Missing, unknown, duplicate or extra
arguments never invoke it. The command does not retry. It emits one ASCII line
of at most 128 bytes including a fixed LF:

```text
State=<token>;Reason=<token>
```

| Exit | State | Reason |
| --- | --- | --- |
| 0 | Ready | `ActiveInputDesktop` |
| 2 | Blocked | A blocking readiness reason |
| 3 | Unknown | An indeterminate readiness reason |
| 4 | ExecutionError | `OptInRequired`, `InvalidArguments`, `InvalidProbeResult` or `ProbeFailed` |

Undefined or inconsistent state/reason pairs are execution errors. Exception
messages, arguments, paths and session details are never printed. An output
failure returns 4 without retrying the probe and may leave an absent/incomplete line;
host-startup diagnostics and incomplete output still require external supervision.

A result describes only the invoking process's session and thread's desktop at
that instant. It does **not** prove GUI admission for another process, subsequent
readiness, application startup, capture or rendering.

## Separate native verification

Native verification is separate, after review: own-session observations in
already-existing active/blocked environments, then bounded owned-window startup
and target capture in a suitable interactive environment. Do not blindly run the
full E2E suite or native automation on a shared workstation.

Neither [GitHub-hosted runner documentation](https://docs.github.com/en/actions/using-github-hosted-runners/about-github-hosted-runners)
nor [PrintWindow documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)
guarantees usable GUI automation while locked/disconnected. A separate VM is an
architecture option, not a provisioned or verified environment. Pure tests and a
successful build do not establish native desktop or application GUI acceptance.

Rollback is a revert of the change, never an admission bypass.
