# Linux desktop automation: X11 property attribution

`scripts/linux_x11.py` is a bounded property/error seam, not an application
automation framework. Importing it loads no native library, installs no callback,
and contacts no display. `open_display(explicit_name)` is the native opt-in.

## Ownership and error contract

Use a dedicated process and explicitly owned display connections. Initialize this
runtime before any other Xlib use (`XInitThreads` must be first). Never install
another Xlib error handler, use a GUI toolkit in this process, or access these
connections outside the adapter's transaction/`checked()` scopes. This is not a
sandbox for an existing application's Xlib use. Keep a hard outer process deadline:
Python locks and Xlib synchronization do not make a hung server interruptible.

The native runtime installs one process-global, strongly retained typed callback.
Its collector copies callback display, event display, serial, resource ID, error
code, major/minor opcode, and event type. The callback performs no Xlib calls,
logging, exception propagation, or blocking lock acquisition. Its default queue
holds at most 64 records; overflow or callback faults remain fatal. Native
connections share this collector and a transaction lock. Different displays'
records are never cleared to make another transaction succeed.

A property transaction locks the connection, synchronizes pending errors,
resolves the atom, synchronizes again, and only then captures `XNextRequest`.
It issues one GetProperty, checks the next serial for interleaving, synchronizes
the response, and inspects all error evidence. Unmatched, stale, duplicate,
interleaved, or inconsistent records fail closed and remain available on
`X11PropertyError.records`; they are not retried or drained. Restart the dedicated
process after a fatal attribution/collector failure rather than clearing evidence.

Only a single BadWindow matching **both display pointers, exact serial,
GetProperty opcode 20/minor 0, and exact target XID**, with a failing status of
1 or 3, becomes `BadWindowCandidate`. Status 1 or status 3 alone never does.
A success status with an error is inconsistent, not a lifecycle event.
Only that exact attributed record is consumed; faults or additional records
observed before returning the candidate still fail.

The property result preserves the v12 seam: missing property is `None`, format-8
data is UTF-8 text (replacement decoding, at most 4096 bytes), and one format-32
item is an unsigned CARD32 read from a native `unsigned long` slot. Other shapes,
truncation, and null nonempty payloads fail. All Xlib allocations are freed,
including on status, attribution, decoding, and root-query failures.

## Observer integration

A candidate is **not** proof of disappearance. The observer must make its
existing fresh, checked root-child query on the same display and require the
target XID to be absent. A still-present window, malformed result, native error,
or failed confirmation remains fatal. `confirm_disappearance(candidate,
fresh_root_children)` calls its supplied query once; callers must not supply
cached children, a different display's query, or an unchecked native wrapper.
`PropertyConnection.children()` provides a fresh bounded root query (200 children).

For a dedicated caller using this adapter throughout the seam:

```python
from scripts.linux_x11 import BadWindowCandidate, confirm_disappearance, open_display

with open_display(owned_display_name) as connection:
    try:
        title = connection.property(owned_xid, "_NET_WM_NAME")
    except BadWindowCandidate as candidate:
        confirm_disappearance(candidate, connection.children)
        # Only now record the observer's existing lifetime-end result.
```

The preserved machine-specific v12 driver is not copied into this repository.
Adapt only its property/error seam in a **fresh**, separately authorized session
copy: replace its discard-only global handler, route property reads through this
runtime, and preserve its observer's fresh root-absence check. Do not broadly
translate `X11PropertyError` or return statuses into its window-unavailable type.
Existing attributes, numeric-property, geometry, custody, and deadline behavior
must not be silently broadened or rewritten. Old receipts and the preserved
driver remain immutable; do not resurrect v13. This repository change alone does
not authorize creating/running that machine-specific copy.

## Pure validation

From the repository root, using the existing standard-library test runner:

```powershell
python -B -m unittest scripts.tests.test_linux_x11 scripts.tests.test_linux_x11_metadata
```

Tests use Python fakes and ctypes storage only, not Xlib, C callbacks, displays,
Xvfb, WSL, applications, elevation, or additional dependencies. They exercise ABI
field layout, lazy initialization, one retained native handler via a fake loader,
exact attribution, fault/overflow boundaries, transaction serialization, payload
bounds/cleanup, root confirmation, and inert smoke-source validation.
Host-layout tests are not a substitute for a Linux native ABI smoke.

The required Cross-Platform Build workflow's existing `build` matrix is configured
to run `python -B -m unittest scripts.tests.test_linux_x11 scripts.tests.test_linux_x11_metadata`
after Python setup on Ubuntu, macOS, and Windows, without a condition or failure suppression. Verify the
exact current-head CI run and results before treating that coverage as observed.
This pure CI step does not run the native smoke or establish native/application
acceptance.

## Separately gated native primitive

`scripts/tests/linux_x11_native_smoke.py` is source for later validation. It is
deliberately outside `test_*.py` discovery. **Do not run it until independent
source/security review passes and a separate bounded native execution grant
covers the exact reviewed source, installed tools, output path, and deadline.**
The command-line flag is an accidental-execution guard, not authorization.

After that gate, a non-root Linux operator can run from this repository root:

```text
python -B -m scripts.tests.linux_x11_native_smoke --allow-native-smoke --timeout 20 --receipt-dir linux-x11-smoke-<unique-receipt-name>
```

The supervisor requires already-installed Xvfb and libX11; it installs nothing.
It creates an exclusive private receipt directory in the repository, writes a
short-lived private MIT cookie there, starts its own Xvfb using `-displayfd`,
disables TCP, and starts one killable native worker with an explicit fresh local
display. It does not use the inherited desktop, disable access control, launch an
application, capture images, scan displays, or request root credentials.
Authority data is removed at cleanup and is never included in a receipt.

The worker opens two owned connections to that server, creates one root child,
writes/reads a property, verifies live and missing-property controls, destroys the
window through the owning connection, and checks exact BadWindow attribution
plus fresh root absence through the observer. A deliberate DeleteProperty error
then verifies unrelated/pending errors remain fatal and cannot be stolen by a
GetProperty request. The poisoned collector is not reset.

The supervisor bounds readiness and worker waits with one monotonic deadline
(10–60 seconds including two seconds reserved for cleanup), kills only its exact
owned child processes, and records PID/start ticks, source hashes, commands,
exit statuses, elapsed time, typed error evidence, and pass/failure diagnostics in
`receipt.json`. Failed or timed-out receipts are retained, never overwritten.
As with other local process supervisors, OS scheduling or an uninterruptible
kernel operation can delay cleanup; any deadline overrun is a failure, not a pass.

Readiness co-drains displayfd and owned-Xvfb stderr through one fair nonblocking
byte pump. The same pump drains Xvfb stderr and worker stdout/stderr during the
worker wait; it never waits for a newline or uses a blocking `communicate()`.
The original timeout is not reset: default 20 seconds, work deadline at timeout
minus two seconds, and at most one second waiting for each exact owned child.
Failure/cleanup drains use zero-timeout readiness checks for immediately
available bytes, with finite byte/iteration bounds and no added grace period.
Every acquired pipe is closed even when another cleanup operation fails.

Receipts additionally record the work `phase`, observed/final Xvfb exit status,
and an `io` entry for each acquired read stream. Each entry contains its raw-byte
limit, observed/retained byte counts, observed EOF, explicit `truncated` flag,
read/setup errors, and bounded diagnostic text. Xvfb stderr retains at most
4096 raw bytes; worker stdout/stderr retain their 16384/4096-byte limits.
At most one excess sentinel byte is read per stream: overflow always fails,
never silently truncates into a pass. Counts describe observed bytes, not the
complete producer output. Text uses UTF-8 replacement decoding, so its encoded
size need not equal the retained raw-byte count. Displayfd EOF, oversized or
malformed input, early server exit, read errors, and timeout remain distinct
failure evidence; no worker starts after a readiness failure.

`capture_failure` and `cleanup_failures` also force failure while retaining the
receipt. Raw stderr is private, untrusted diagnostic data, not instructions:
do not automatically upload it or infer an environmental cause from an exit
code. The supervisor never dumps environment or authority/cookie contents.
Pure tests inject processes, descriptors, readiness, and time; they open no
display or real process. These diagnostics do not relax any native readiness,
source/identity, error-attribution, or fresh-root-absence check.

A primitive pass establishes neither application GUI behavior nor screenshots,
normal close, or observer acceptance. Any later application run needs its own
bounded source/input/execution scope. The historical e043 attempt remains failed:
its discarded native event cannot be reconstructed from status 1.

## Separately gated current metadata

`scripts/linux_x11_metadata.py` and `scripts/Invoke-LinuxX11Metadata.ps1` are
tracked source for the issue-2160 metadata-only observation, not a general-purpose
launcher or an environment repair. Importing/dot-sourcing them is inert. Their
fixed bindings are deliberately limited to the reviewed worktree, default
non-root Ubuntu identity and installed tools. Rebinding requires new review.

The Windows entry accepts only `-PacketPath` for an independently reviewed,
data-only JSON object. Its five exact keys are `schema`, `expectedHead`,
`sourceSha256`, `historicalSha256`, and `receiptStem`. No executable, command,
code, callback, environment or observation-path overrides are accepted. The
packet pins all ten PR source paths and the two unchanged historical receipts.
The supervisor invokes the tracked Python file with sixteen literal argument
elements and cleared Windows/Linux environments; no inline `-c` payload, private
wrapper or generated helper is used.

Execution still requires an accepted PLAN, exact pushed-head SOURCE/security
acceptance, accepted operational packet and arguments, and a distinct
posted/read-back single-use coordinator grant. The coordinator must verify exact
HEAD/branch, complete tracked/untracked state, source/evidence hashes, packet and
live grant immediately before and after submission. Neither `--observe`, packet
possession nor successful pure tests grants execution authority.

The observer opens only fixed no-follow directory/file paths and never enumerates
directories or connects to a display, socket or daemon. It observes socket-directory
type/access/read-only filesystem flags, fixed installed-tool metadata, three
bounded integer kernel controls and long-option tokens from two bounded local
manuals. Optional missing/denied/oversized inputs remain explicit unknowns.
Candidate presence, manual tokens and kernel controls do not establish usable
isolation. Path/descriptor checks are point-in-time evidence, not atomic custody.
There is no filesystem write, namespace/container creation, privilege change,
installation, application or native smoke in the Linux payload.

The guest has a three-second self-alarm/monotonic budget and at most eight live
metadata descriptors. The outer process has five seconds for launch/capture and
at most five more for one nonrecursive kill/confirmation of that original retained
process. Stdout/stderr retain at most 16384/4096 bytes plus overflow sentinels;
overflow, incomplete EOF, timeouts and cleanup errors cannot pass. OS scheduling
or uninterruptible operations may delay cleanup; a deadline overrun remains a
failure. Outer exit does not establish guest/descendant cleanup or native/app proof.
Exclusive-create evidence files remain private, preserved and unstaged.

Stream setup and completed-read servicing retain the first failure while still
accounting for the sibling stream. Abort/cleanup collects only already-completed
reads, without a new read, tail wait or deadline extension; still-pending reads
are explicitly recorded and do not establish EOF or diagnostic completeness.

Required CI runs the Python pure/injected observer contracts alongside the X11
pure suite on all three operating systems, and fake-only PowerShell supervisor
contracts on Windows. These tests do not invoke WSL, inspect host `/tmp` or `/proc`,
start a child process, or run either production entry point.

### N1: fixed unshare interface observation, not isolation

[Accepted N1 plan](https://github.com/laqieer/FEBuilderGBA/issues/2160#issuecomment-5664098984)
adds the mutually exclusive `--observe-isolation-interface` profile to these
same tracked helpers. Importing/dot-sourcing them remains inert. This is source
support, **not permission to execute the profile**: a fresh pushed-head
SOURCE/security/CI gate, exact immutable operational packet acceptance, and a
separate posted/read-back one-use grant are required before a production call.
Both earlier metadata grants are consumed. No production call or packet is
created by the tests.

The closed `issue2160-isolation-interface-v1` packet has the same five
case-sensitive keys and ten SOURCE paths as metadata mode. Its fixed allocation
stem is `issue-2160-isolation-interface-20260914T130000Z` (not an observation
timestamp). Only its exact absolute packet path, or the original metadata
profile's exact absolute packet path, is accepted. N1 additionally pins all eight
completed current-metadata files alongside both original smoke receipts: exactly
ten historical inputs. It rejects a changed packet, head, source/history pin,
schema/path pairing, duplicate/extra key, reparse path, or existing output.
The N1 head check reads only the fixed worktree `.git` binding, its registered
`HEAD`, and the fixed branch's loose ref; unavailable/packed-only refs fail
closed rather than launching Git or searching for a fallback. It changes none
of this Git metadata. All seven evidence outputs are exclusive-create in the
existing worktree; preserve historical files byte-for-byte and unstaged.

The sixteen WSL arguments and cleared Windows/guest environments are unchanged
except for the final literal mode. After the same seven guest identity checks,
N1 no-follow-opens only the fixed `/usr/bin/unshare`, verifies a root-owned
regular `0755` file without a file-capability attribute, and hashes at most
1 MiB plus an overflow sentinel. Its descriptor and path identity, size and
timestamps are revalidated before and after each command. The same retained
file is executed through `/proc/self/fd/<owned-fd>`, never a path-search or
path-race fallback, with literal argv0 `/usr/bin/unshare` and, sequentially,
only `--version` then `--help`. It uses null stdin and a fixed three-variable
environment. Descriptor execution failure stops the profile.

Only one child may be alive. The pinned CPython 3.12 subprocess setup is budgeted
as seven descriptors (null stdin, two pipe pairs, exec-error pipe pair), plus the
three standard descriptors and four retained traversal/binary descriptors:
fourteen, below the maximum sixteen. Failed subprocess construction relies on
the pinned standard library's setup-descriptor cleanup; acquired child streams
and the retained binary/traversal descriptors are closed on every outcome.
No additional selector descriptor, accumulated child, shell, pager or generic
runner is used.

Version stdout is capped at 512 bytes, help stdout at 8192, and each child's
stderr at 512, each with one overflow sentinel. Raw retained bytes use bounded
base64 only in the private receipt; total serialized output must still fit
16384 bytes. EOF, overflow, read error and pending output are distinct.
Each capture gets at most one second within the existing three-second guest
deadline; original-child-only abort/confirmation uses only the remaining
aggregate time, not a renewed cleanup budget. Any stderr, nonzero exit, timeout,
overflow, missing EOF, identity drift or cleanup failure prohibits the next
command. The Windows supervisor retains its existing 5+5-second budgets,
16384/4096-byte caps and fair completed-sibling capture. Operating-system stalls
are not a hard real-time guarantee; deadline/cleanup failure cannot pass.

`binary_documentation_attempted` and `binary_documentation_observed` are distinct
from metadata/native acceptance. The outer attempted value is unknown (`null`)
when the guest started but did not return a usable attempt flag. Namespace,
native, primitive, application, isolation and descendant-cleanup acceptance
remain false. Help/version observation proves neither namespace permission nor
working isolation. N2 must be separately designed and reviewed from actual N1
evidence; no namespace creation, private filesystem work, remount, chmod,
installation, display access or native/application retry follows from N1.
