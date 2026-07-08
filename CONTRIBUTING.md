# Contributing to FEBuilderGBA

## Documentation Guidelines

Documentation is a first-class deliverable. Every code change should include corresponding doc updates.

### What to Update

| Change Type | Required Doc Updates |
|------------|---------------------|
| New CLI command | CLAUDE.md (Command-Line Tools section), --help text |
| New Avalonia feature | CLAUDE.md (Architecture Overview if structural) |
| New Core API | CLAUDE.md (Core Data Access Pattern) |
| Bug fix | None required unless it changes behavior |
| New config key | CLAUDE.md (Configuration Files section) |
| Build/CI change | CLAUDE.md (Build & Development Commands) |
| Submodule change | CLAUDE.md (Dependencies section), wiki if applicable |

### Where Documentation Lives

- **CLAUDE.md** — Primary project reference (architecture, commands, patterns)
- **DEVELOPMENT-WORKFLOW.md** — Development process and PR workflow
- **README.md** — User-facing overview and quick start
- **docs/** — Detailed specs and reports
- **[Wiki](https://github.com/laqieer/FEBuilderGBA/wiki)** — User guides, tutorials, and extended documentation

### Wiki Maintenance

The [project wiki](https://github.com/laqieer/FEBuilderGBA/wiki) hosts user guides and extended documentation.

**When to update the wiki:**
- New user-facing features (editors, CLI commands, settings)
- Changed workflows or setup procedures
- New submodule integrations

**How to update the wiki (maintainers):**
1. Clone the wiki: `git clone https://github.com/laqieer/FEBuilderGBA.wiki.git`
2. Edit markdown files and push
3. Or edit directly on GitHub via the wiki web UI

**How to propose wiki changes (contributors without write access):**
1. Open an issue with the `documentation` label describing the proposed changes
2. Or include the proposed wiki text in your PR description for a maintainer to apply

**Wiki pages to keep current:**
- Existing pages should be updated when their topic area changes
- New pages may be created for major features — coordinate with maintainers

### Doc Review in PRs

Every PR reviewer (human or automated) should verify:
1. New features have corresponding CLAUDE.md updates
2. Changed CLI commands have updated --help text
3. README reflects any user-facing changes
4. No stale documentation references removed features
5. Major features have wiki page updates noted in the PR description

### How to Propose Doc Changes

1. File an issue with the `documentation` label
2. Or include doc updates in your feature PR
3. For large doc restructuring, open a dedicated docs PR
4. For wiki updates, edit via the web UI or clone and push

## GUI Strategy & Feature Policy

FEBuilderGBA has **two GUIs held to different standards** (full policy:
[docs/GUI-STRATEGY.md](docs/GUI-STRATEGY.md)):

- **WinForms GUI (`FEBuilderGBA`)** — stable and widely used → **bug fixes only, no new features.**
- **Avalonia GUI (`FEBuilderGBA.Avalonia`)** — cross-platform **preview** → **all new GUI features ship here.**

So: build/propose a **new GUI feature against Avalonia**; a WinForms change is in
scope only when it fixes a bug/regression. `FEBuilderGBA.Core` / `FEBuilderGBA.CLI`
are shared and cross-platform and are **not** restricted by this policy. Reviewers
should reject or redirect new-feature PRs that target the WinForms GUI.

## Code Contribution Guidelines

See [DEVELOPMENT-WORKFLOW.md](DEVELOPMENT-WORKFLOW.md) for the mandatory development workflow including plan review, implementation, and PR review gates.

## Secret Scanning (ggshield)

This repo uses **[GitGuardian ggshield](https://github.com/gitguardian/ggshield)** to catch committed secrets (API keys, tokens) — "shift left" — both locally and in CI. Full setup: **[docs/SECRET-SCANNING.md](docs/SECRET-SCANNING.md)**.

- **Local (opt-in pre-commit hook):** `pip install pre-commit && pre-commit install --hook-type pre-commit --hook-type commit-msg` (these `--hook-type` flags register **both** the ggshield secret-scan hook *and* the commitlint commit-msg hook — see [Commit & PR Title Convention](#commit--pr-title-convention)), then authenticate once with `ggshield auth login` (or export `GITGUARDIAN_API_KEY`). ggshield then scans each commit and blocks one that would introduce a secret. Escape hatch for a false positive: `SKIP=ggshield git commit …` (or `git commit --no-verify`). Requires pre-commit ≥ 3.2.0.
- **CI:** `.github/workflows/ggshield.yml` scans the commit range of each push / PR. It runs only when the `GITGUARDIAN_API_KEY` repo secret is set, so **fork PRs skip it** (secrets aren't shared with forks) and it never breaks the build. It's an advisory (non-required) check — a finding fails the check but doesn't hard-block merge.

## Commit & PR Title Convention

Commit subjects and pull-request titles follow the
[Conventional Commits](https://www.conventionalcommits.org/) format so that
release changelogs can be generated reliably from the history:

```
<type>(<optional scope>): <subject>
```

**Allowed types:** `build`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`,
`refactor`, `revert`, `style`, `test`. **Scopes** are free-form and optional
(e.g. `avalonia`, `core`, `cli`, `gap-sweep`).

Examples: `feat(avalonia): add path-move editor`, `fix(core): guard EOF`,
`docs: update deployment guide`, `ci: lint PR titles`.

This is enforced **in CI** on every pull request: `.github/workflows/pr-title-lint.yml`
lints the PR title (covers squash merges) and every commit in the PR
(covers merge / rebase merges, via `commitlint.config.mjs`). If a check fails,
edit the offending PR title or commit message to add a valid `<type>:` prefix and
keep the subject ≤ 100 characters.

To **catch these locally before pushing** (recommended, esp. for automated
worktree work — the CI check stays the source of truth), enable the **opt-in**
commit-msg hook in [`.pre-commit-config.yaml`](.pre-commit-config.yaml). It runs
the same `commitlint.config.mjs` at commit time:

```bash
pip install pre-commit
pre-commit install --hook-type pre-commit --hook-type commit-msg   # both ggshield + commitlint
```

Requires **pre-commit ≥ 3.2.0**; pre-commit provisions its own Node.js toolchain
on first install (via nodeenv; needs network) — no system Node/npm required.
Already set up the ggshield hook? Re-run the command above to add commit-msg
linting (a bare `pre-commit install` only registers the `pre-commit` stage).
Escape hatch: `git commit --no-verify` (portable), or skip just this hook —
bash/zsh `SKIP=commitlint git commit …`, PowerShell `$env:SKIP='commitlint'; git commit …; Remove-Item Env:SKIP`.
You are never blocked offline: the hook is opt-in and bypassable, and CI remains authoritative.
See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md#commit--pr-title-convention) for
details and the link to the auto-changelog work (#1632).

## Commit Identity

When using Claude Code automation, commit as `laqieer <laqieer@126.com>`. Human contributors use their own identity.
