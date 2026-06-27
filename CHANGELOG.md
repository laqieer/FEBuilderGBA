# Changelog

All notable changes to FEBuilderGBA are recorded here. The format is loosely
based on [Keep a Changelog](https://keepachangelog.com/), and the project uses
date-stamped version tags (`ver_YYYYMMDD.HH`).

## How release notes are generated

Release notes for each tag are **generated from the commit history**, not
hand-typed. The commit/PR-title corpus follows
[Conventional Commits](https://www.conventionalcommits.org/) (`feat`, `fix`,
`docs`, `ci`, `chore`, ...), enforced in CI by
[`.github/workflows/pr-title-lint.yml`](.github/workflows/pr-title-lint.yml)
(#1647), so the notes can be grouped automatically by change type.

Two complementary mechanisms produce the notes (see
[docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the full release procedure):

1. **Tag-triggered automation** — pushing a `ver_*` tag runs
   [`.github/workflows/release.yml`](.github/workflows/release.yml) (#1629),
   which calls [`scripts/generate-changelog.sh`](scripts/generate-changelog.sh)
   to build a **type-grouped release body** from the conventional-commit
   subjects between the previous tag and the new one, and passes it to the
   GitHub Release. [`.github/release.yml`](.github/release.yml) additionally
   groups GitHub's native auto-notes (by PR label) and excludes bot noise.
2. **Manual / local regeneration** — run the same script for any range:

   ```bash
   # Notes since the last ver_* tag (auto-detected):
   scripts/generate-changelog.sh

   # Notes for an explicit range:
   scripts/generate-changelog.sh ver_20260204.22 ver_20260601.00
   ```

   It needs only POSIX `sh` + `git` (no Node/jq/python) and prints grouped
   Markdown to stdout. As a one-liner alternative GitHub also offers
   `gh release create <tag> --generate-notes`.

To refresh the `[Unreleased]` section below, regenerate it with
`scripts/generate-changelog.sh ver_20260204.22 HEAD`.

---

## [Unreleased] — backlog since `ver_20260204.22` (2026-02-04)

`origin/master` is **2293 non-merge commits** ahead of the last release tag
`ver_20260204.22` (≈2830 including merges). This is the first time the full
delta has been reviewed for changelog completeness (issue #1632, AC3). Grouped
by conventional-commit type:

| Type | Section | Count |
|------|---------|------:|
| `feat`                         | 🚀 Features                  | 585 |
| `fix`                          | 🐛 Bug Fixes                 | 999 |
| `docs`                         | 📖 Documentation             | 408 |
| `ci` / `build`                 | 🤖 CI / Build / Packaging    |  40 |
| `chore`/`refactor`/`test`/`perf`/`style`/`revert` | 🧰 Maintenance & Refactoring | 132 |
| other (non-conforming / `i18n` / legacy pre-convention) | 🔧 Other Changes | 129 |
| **Total (non-merge)** | | **2293** |

> **Completeness note:** the 129 "Other Changes" are commits whose subject does
> not match a single conventional type — chiefly compound prefixes
> (`docs+gui:`, `test/docs:`), the `i18n:` localization prefix, `Revert "..."`
> subjects, and a small tail of pre-convention commits from early in the
> backlog. They are **not dropped** — the generator routes them to the
> catch-all section so the notes are lossless. No `feat`/`fix`/`docs` entries
> leak into "Other".

### Highlights

This release window completed several multi-issue campaigns. For the full,
itemized list run `scripts/generate-changelog.sh ver_20260204.22 HEAD`; the
themes are:

- **Avalonia cross-platform GUI** reached ~93% parity with the WinForms editor:
  hundreds of editor views ported (units, classes, items, events, maps, audio,
  graphics, world map), pick-and-return navigation, name resolution, and
  undo-tracked writes. Two large stub-audit waves and a release-audit campaign
  drove the remaining gaps to closure.
- **Native Android port** — the Avalonia UI runs on Android (single-view host,
  view-stack navigation, APK build in CI).
- **Decomp project support** — open a disassembly project, resolve addresses,
  export/import/round-trip assets, and write C/JSON sources.
- **CLI maturity** — many new headless commands (data export/import, portrait/
  battle-anime/MIDI render, palette I/O, ROM diff/merge/rebuild, decomp tools).
- **Release readiness** — tag-triggered multi-platform release workflow (#1629),
  Gitee mirror sync, conventional-commit PR/commit linting (#1647), and this
  automated changelog generation (#1632).
- **~1000 bug fixes** across the editors, ROM-write fault-safety, and CI/E2E
  stability (5-version nightly E2E matrix).

---

## ver_20260204.22 — 2026-02-04

Previous release tag. See the
[GitHub Releases page](https://github.com/laqieer/FEBuilderGBA/releases) for the
assets and notes of this and earlier builds.
