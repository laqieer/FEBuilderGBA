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
python -m unittest discover -s scripts\tests -p test_linux_x11.py
```

Tests use Python fakes and ctypes storage only, not Xlib, C callbacks, displays,
Xvfb, WSL, applications, elevation, or additional dependencies. They exercise ABI
field layout, lazy initialization, one retained native handler via a fake loader,
exact attribution, fault/overflow boundaries, transaction serialization, payload
bounds/cleanup, root confirmation, and inert smoke-source validation.
Host-layout tests are not a substitute for a Linux native ABI smoke.

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
