# Secret Scanning (ggshield / GitGuardian)

FEBuilderGBA uses **[GitGuardian ggshield](https://github.com/gitguardian/ggshield)**
to detect secrets (API keys, tokens, credentials) *before* they land in the repo —
"shift left" — in two places: an **opt-in local pre-commit hook** and a **CI check**.
Tracked in #1903.

## Why

A leaked secret in git history is expensive to remediate (rotate the key, scrub
history). ggshield catches the common case — a secret in a *new* commit — at the
earliest point: your machine, then the pull request.

## Local pre-commit hook (opt-in)

The hook is defined in [`.pre-commit-config.yaml`](../.pre-commit-config.yaml)
(ggshield `v1.52.2`). It is **opt-in** — nothing runs until you install it.

1. Install [pre-commit](https://pre-commit.com/) (≥ 3.2.0) and the hook:
   ```bash
   pip install pre-commit
   pre-commit install
   ```
2. Authenticate ggshield once (free personal account):
   ```bash
   ggshield auth login
   # …or, instead of login, export a token:
   #   export GITGUARDIAN_API_KEY=<your token>
   ```
   Without auth the hook errors and blocks the commit.

Now every `git commit` scans the staged changes. A detected secret **blocks the
commit** with the finding location.

### False positive / escape hatch

If ggshield flags something that is *not* a real secret (e.g. a test fixture):

```bash
SKIP=ggshield git commit -m "…"   # skip just this hook for one commit
git commit --no-verify -m "…"      # skip all hooks for one commit
```

For a recurring false positive, silence it in a `.gitguardian.yaml`
(`secret.ignored-matches`) rather than disabling the hook — see the
[ggshield docs](https://docs.gitguardian.com/ggshield-docs/configuration).

## CI check

[`.github/workflows/ggshield.yml`](../.github/workflows/ggshield.yml) runs
`ggshield secret scan ci` on each push to `master` and each pull request. It scans
only the **commit range of the event** (the PR delta, or the push's
`before..after`) — never the whole repository history — so it will not fail on
pre-existing secrets in untouched history.

- **Gated on the `GITGUARDIAN_API_KEY` repo secret.** When the secret is absent —
  most importantly on **fork pull requests**, where GitHub never shares secrets —
  the scan is **skipped and the job is green**. So the check never breaks `master`
  or fork PRs.
- It is an **advisory, non-required** check: a finding fails the check (red) but
  does **not** hard-block merge via branch protection.
- On a GitGuardian API outage / quota error the check goes red (harmless noise
  since it's non-required); set `GITGUARDIAN_FAIL_ON_SERVER_ERROR=false` in the
  workflow's `env:` if you'd rather outages be non-fatal.

### Maintainer setup (one-time)

Add a repository secret **`GITGUARDIAN_API_KEY`** under
*Settings → Secrets and variables → Actions* (a GitGuardian *personal access
token* with the `scan` scope, from <https://dashboard.gitguardian.com>). The
workflow only ever references `${{ secrets.GITGUARDIAN_API_KEY }}` — the token
value is never committed.
