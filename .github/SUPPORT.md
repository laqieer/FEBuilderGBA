# Getting Help

- **Bug reports** — Use **Help → Report a Bug…** in the app (see the [Reporting Bugs guide](../docs/REPORTING-BUGS.md)), or [open a bug issue](https://github.com/laqieer/FEBuilderGBA/issues/new/choose) on GitHub.
- **Usage questions** — Post in [Q&A Discussions](https://github.com/laqieer/FEBuilderGBA/discussions/categories/q-a).
- **Feature requests** — Post in [Ideas Discussions](https://github.com/laqieer/FEBuilderGBA/discussions/categories/ideas). New GUI features target the cross-platform **Avalonia GUI**; the **WinForms GUI is stable (bug fixes only)** — see the [GUI Strategy](../docs/GUI-STRATEGY.md).
- **Community chat** — Join the [FE hacking Discord](https://discordapp.com/invite/Yzztqqa).

Please do **not** attach your ROM file (.gba) to any issue or discussion.

## Triage

Every bug issue filed via the structured [Issue Forms](https://github.com/laqieer/FEBuilderGBA/issues/new/choose) is automatically labeled by a GitHub Actions workflow ([`workflows/issue-triage.yml`](workflows/issue-triage.yml)):

| Field | Label(s) applied |
|-------|-----------------|
| **App** = Avalonia GUI | `avalonia` |
| **App** = WinForms GUI | `winforms` |
| **Area** starts with `--<flag>` or contains word "CLI" | `cli` |
| **Area** = other / not specified | `core` |
| **Platform** = Windows x64 / x86 | `os:windows` |
| **Platform** = Linux x64 | `os:linux` |
| **Platform** = macOS (any) | `os:macos` |
| **Platform** = Android | `os:android` |
| **Platform** = iOS / iPadOS | `os:ios` |
| always | `needs-triage` |

### Maintainer triage workflow

1. Work the [`needs-triage` queue](https://github.com/laqieer/FEBuilderGBA/issues?q=is%3Aissue+is%3Aopen+label%3Aneeds-triage) — these are newly filed bugs awaiting review.
2. Confirm the bug is reproducible, add any extra labels (e.g. `good first issue`, `help wanted`), and assign a milestone if appropriate.
3. Once triaged, **remove the `needs-triage` label** to keep the queue clean.

### Why there is no separate "Bug Reports" Discussions category

The structured Issue Forms (added in #1709) already provide the same intake as a Discussions category would — they guide reporters through all required fields and prevent lumped / off-topic reports.  A separate Discussions category for "early / fuzzy" bug reports would duplicate the Issue Forms and split the signal.  The `needs-triage` label + auto-labeling (this workflow, #1710) replace the triage project board proposed in #1711 without requiring a manual board setup.
