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
changing its existing failure propagation.

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

## Validation boundary

Build and run only injected tests before independent source/security review:

```powershell
dotnet build FEBuilderGBA.E2ETests\FEBuilderGBA.E2ETests.csproj -c Release -p:Platform=x86 --no-restore
dotnet test FEBuilderGBA.E2ETests\FEBuilderGBA.E2ETests.csproj --no-build -c Release -p:Platform=x86 --filter FullyQualifiedName~DesktopReadinessTests
```

Restore existing dependencies only if the build explicitly reports them missing.
The focused suite injects observations, process dispatch and capture surfaces;
it invokes no actual readiness, window-capture or GUI APIs. It checks rejection,
CLI preservation, resource ownership, native-return handling, failure propagation
and removal of direct workflow GUI diagnostics.

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
